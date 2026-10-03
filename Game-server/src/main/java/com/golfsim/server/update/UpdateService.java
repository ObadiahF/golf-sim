package com.golfsim.server.update;

import com.golfsim.server.api.InvalidFieldsException;
import java.io.IOException;
import java.io.UncheckedIOException;
import java.sql.ResultSet;
import java.sql.SQLException;
import java.sql.Timestamp;
import java.time.Clock;
import java.time.Instant;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.TreeMap;
import org.springframework.http.HttpStatus;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Propagation;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.server.ResponseStatusException;

/**
 * Self-update releases in Postgres ({@code update_releases}, {@code update_files}); the files themselves are in the
 * {@link BlobStore}. Publishing checks the manifest ({@link ManifestRules}) and that every file is uploaded with the
 * listed size, makes the release the platform's latest (its build number must be the highest), keeps the newest
 * {@code updates.keep-releases} per platform and deletes the blobs no release needs any more.
 */
@Service
@Transactional
public class UpdateService {

    /** Advisory-lock id for publishing, so pruning and garbage collection never race a new release. */
    static final long PUBLISH_LOCK = 0x0_5E1F_0BDL;

    private static final String RELEASE_COLUMNS = "id, platform, version, build, note, total_size, created_at, "
            + "(select count(*) from update_files f where f.release_id = r.id) as file_count";

    private final JdbcTemplate jdbc;
    private final BlobStore blobs;
    private final UpdateProperties properties;
    private final Clock clock;

    public UpdateService(JdbcTemplate jdbc, BlobStore blobs, UpdateProperties properties, Clock clock) {
        this.jdbc = jdbc;
        this.blobs = blobs;
        this.properties = properties;
        this.clock = clock;
    }

    /** The platform's release with the highest build number, with its files. */
    @Transactional(readOnly = true)
    public Optional<ReleaseView> latest(String platform) {
        List<Row> rows = jdbc.query("select " + RELEASE_COLUMNS + " from update_releases r where platform = ? "
                + "order by build desc limit 1", UpdateService::row, platform);
        return rows.stream().findFirst().map(this::withFiles);
    }

    /** The platform's kept releases, newest first, without their files. */
    @Transactional(readOnly = true)
    public List<ReleaseView> releases(String platform) {
        return jdbc.query("select " + RELEASE_COLUMNS + " from update_releases r where platform = ? order by build desc",
                UpdateService::row, platform).stream().map(row -> row.view(null)).toList();
    }

    /** The given blobs the store doesn't have, in the order asked, without duplicates. */
    @Transactional(propagation = Propagation.NOT_SUPPORTED)
    public List<String> missing(List<String> sha256s) {
        return sha256s.stream().distinct().filter(sha -> !blobs.has(sha)).toList();
    }

    /** Validates and stores a release; it becomes the platform's latest. */
    public ReleaseView publish(UpdateRequests.Publish request) {
        List<UpdateRequests.ReleaseFile> files = request.files();
        validatePaths(files);
        jdbc.queryForObject("select 1 from pg_advisory_xact_lock(?)", Integer.class, PUBLISH_LOCK);
        Long newest = jdbc.queryForObject("select max(build) from update_releases where platform = ?", Long.class,
                request.platform());
        if (newest != null && request.build() <= newest) {
            throw new ResponseStatusException(HttpStatus.CONFLICT,
                    "Build " + request.build() + " is not newer than the latest " + request.platform() + " build " + newest);
        }
        Integer sameVersion = jdbc.queryForObject("select count(*) from update_releases where platform = ? and version = ?",
                Integer.class, request.platform(), request.version());
        if (sameVersion != null && sameVersion > 0) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "Version " + request.version() + " already exists");
        }
        validateBlobs(files);

        long total = files.stream().mapToLong(UpdateRequests.ReleaseFile::size).sum();
        Long id = jdbc.queryForObject("""
                insert into update_releases (platform, version, build, note, total_size, created_at)
                values (?, ?, ?, ?, ?, ?) returning id
                """, Long.class, request.platform(), request.version(), request.build(), blankToNull(request.note()),
                total, Timestamp.from(Instant.now(clock)));
        jdbc.batchUpdate("insert into update_files (release_id, path, size, sha256, executable) values (?, ?, ?, ?, ?)",
                files, 500, (ps, file) -> {
                    ps.setLong(1, id);
                    ps.setString(2, file.path());
                    ps.setLong(3, file.size());
                    ps.setString(4, file.sha256());
                    ps.setBoolean(5, Boolean.TRUE.equals(file.executable()));
                });
        prune(request.platform());
        collectGarbage();
        return latest(request.platform()).orElseThrow();
    }

    /** Deletes unreferenced blobs (older than the grace period) and expired uploads; the number of blobs deleted. */
    public int collectGarbage() {
        List<String> referenced = jdbc.queryForList("select distinct sha256 from update_files", String.class);
        try {
            return blobs.collect(new HashSet<>(referenced), Instant.now(clock).minus(properties.gcGrace()));
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    private void prune(String platform) {
        jdbc.update("""
                delete from update_releases where platform = ? and id not in
                    (select id from update_releases where platform = ? order by build desc limit ?)
                """, platform, platform, properties.keepReleases());
    }

    private static void validatePaths(List<UpdateRequests.ReleaseFile> files) {
        Map<String, String> errors = new TreeMap<>();
        for (int i = 0; i < files.size(); i++) {
            String problem = ManifestRules.pathProblem(files.get(i).path());
            if (problem != null) {
                errors.put("files[" + i + "].path", problem);
            }
        }
        if (errors.isEmpty()) {
            ManifestRules.conflicts(files.stream().map(UpdateRequests.ReleaseFile::path).toList())
                    .forEach((i, problem) -> errors.put("files[" + i + "].path", problem));
        }
        if (!errors.isEmpty()) {
            throw new InvalidFieldsException(errors);
        }
    }

    /** Every file must be uploaded, with the size the manifest says. */
    private void validateBlobs(List<UpdateRequests.ReleaseFile> files) {
        Map<String, String> errors = new LinkedHashMap<>();
        for (int i = 0; i < files.size() && errors.size() < 50; i++) {
            UpdateRequests.ReleaseFile file = files.get(i);
            long stored = blobs.size(file.sha256());
            if (stored < 0) {
                errors.put("files[" + i + "].sha256", "not uploaded");
            } else if (stored != file.size()) {
                errors.put("files[" + i + "].size", "the uploaded file is " + stored + " bytes");
            }
        }
        if (!errors.isEmpty()) {
            throw new InvalidFieldsException(errors);
        }
    }

    private ReleaseView withFiles(Row row) {
        List<ReleaseView.FileView> files = new ArrayList<>(jdbc.query(
                "select path, size, sha256, executable from update_files where release_id = ? order by path collate \"C\"",
                (rs, n) -> new ReleaseView.FileView(rs.getString("path"), rs.getLong("size"), rs.getString("sha256"),
                        rs.getBoolean("executable")), row.id()));
        return row.view(files);
    }

    private static String blankToNull(String s) {
        return s == null || s.isBlank() ? null : s.trim();
    }

    private record Row(long id, String platform, String version, long build, String note, long totalSize,
            Instant createdAt, int fileCount) {

        ReleaseView view(List<ReleaseView.FileView> files) {
            return new ReleaseView(platform, version, build, note, createdAt, totalSize, fileCount, files);
        }
    }

    private static Row row(ResultSet rs, int rowNum) throws SQLException {
        return new Row(rs.getLong("id"), rs.getString("platform"), rs.getString("version"), rs.getLong("build"),
                rs.getString("note"), rs.getLong("total_size"), rs.getTimestamp("created_at").toInstant(),
                rs.getInt("file_count"));
    }
}
