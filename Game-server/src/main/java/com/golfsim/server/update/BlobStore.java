package com.golfsim.server.update;

import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.io.UncheckedIOException;
import java.nio.file.DirectoryStream;
import java.nio.file.Files;
import java.nio.file.NoSuchFileException;
import java.nio.file.Path;
import java.nio.file.StandardCopyOption;
import java.nio.file.attribute.FileTime;
import java.security.DigestOutputStream;
import java.security.MessageDigest;
import java.time.Clock;
import java.time.Instant;
import java.util.Optional;
import java.util.Set;
import java.util.UUID;
import java.util.stream.Stream;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Component;
import org.springframework.web.server.ResponseStatusException;

/**
 * Content-addressed file store under {@code updates.dir}: {@code blobs/ab/abcd...} holds a file by its sha256, so a
 * file that doesn't change between releases is stored (and uploaded) once. Uploads arrive in parts no bigger than
 * {@code updates.max-part-bytes} into {@code uploads/<sha256>/}; completing one joins the parts, checks the size and
 * the sha256 and only then moves the file into the store, so a blob is always complete and correct. Every name on
 * disk comes from a checked sha256 ({@link Sha256#isValid}) or a part number, never from the request otherwise.
 */
@Component
public class BlobStore {

    private static final Logger log = LoggerFactory.getLogger(BlobStore.class);
    private static final String TEMP = ".tmp-";

    private final Path blobs;
    private final Path uploads;
    private final UpdateProperties properties;
    private final Clock clock;

    public BlobStore(UpdateProperties properties, Clock clock) throws IOException {
        this.properties = properties;
        this.clock = clock;
        this.blobs = Files.createDirectories(properties.dir().resolve("blobs"));
        this.uploads = Files.createDirectories(properties.dir().resolve("uploads"));
    }

    /** The stored file, if there is one. */
    public Optional<Path> find(String sha256) {
        Path path = blobPath(sha256);
        return Files.isRegularFile(path) ? Optional.of(path) : Optional.empty();
    }

    /** The stored file's size, or -1 when it isn't stored. */
    public long size(String sha256) {
        try {
            return Files.size(blobPath(sha256));
        } catch (IOException e) {
            return -1;
        }
    }

    /**
     * True when the blob is stored; it is then marked as recently used, so garbage collection leaves it alone for a
     * while ({@code updates.gc-grace}) even if no release refers to it yet: a publisher was just told not to upload it.
     */
    public boolean has(String sha256) {
        Path path = blobPath(sha256);
        try {
            Files.setLastModifiedTime(path, FileTime.from(Instant.now(clock)));
            return true;
        } catch (NoSuchFileException e) {
            return false;
        } catch (IOException e) {
            throw new UncheckedIOException(e);
        }
    }

    /**
     * Streams one part of an upload to disk (replacing an earlier attempt of the same part). Refused with 413 when it
     * is bigger than {@code updates.max-part-bytes}, by its Content-Length or while reading.
     */
    public long writePart(String sha256, int part, long contentLength, InputStream body) throws IOException {
        long limit = properties.maxPartBytes();
        if (contentLength > limit) {
            throw tooLarge("A part may be at most " + limit + " bytes");
        }
        Path folder = Files.createDirectories(uploads.resolve(checked(sha256)));
        Path temp = folder.resolve(TEMP + UUID.randomUUID());
        try {
            long written;
            try (OutputStream out = Files.newOutputStream(temp)) {
                written = copy(body, out, limit);
            }
            if (written < 0) {
                throw tooLarge("A part may be at most " + limit + " bytes");
            }
            Files.move(temp, partPath(folder, part), StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
            return written;
        } finally {
            Files.deleteIfExists(temp);
        }
    }

    /**
     * Joins parts 0..parts-1 into the blob after checking that they add up to {@code size} and hash to
     * {@code sha256}. A mismatch (400) deletes the upload, so the publisher starts that file again. Completing a blob
     * that is already stored just cleans up.
     */
    public void complete(String sha256, long size, int parts) throws IOException {
        Path folder = uploads.resolve(checked(sha256));
        if (find(sha256).isPresent()) {
            deleteTree(folder);
            return;
        }
        if (size > properties.maxBlobBytes()) {
            throw tooLarge("A file may be at most " + properties.maxBlobBytes() + " bytes");
        }
        long total = 0;
        for (int i = 0; i < parts; i++) {
            Path part = partPath(folder, i);
            if (!Files.isRegularFile(part)) {
                throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "Part " + i + " has not been uploaded");
            }
            total += Files.size(part);
        }
        if (total != size) {
            deleteTree(folder);
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST,
                    "The parts add up to " + total + " bytes, not " + size + "; upload the file again");
        }
        Path target = blobPath(sha256);
        Path temp = blobs.resolve(TEMP + UUID.randomUUID());
        try {
            MessageDigest digest = Sha256.digest();
            try (OutputStream out = new DigestOutputStream(Files.newOutputStream(temp), digest)) {
                for (int i = 0; i < parts; i++) {
                    Files.copy(partPath(folder, i), out);
                }
            }
            String actual = Sha256.hex(digest);
            if (!actual.equals(sha256)) {
                deleteTree(folder);
                throw new ResponseStatusException(HttpStatus.BAD_REQUEST,
                        "The parts hash to " + actual + ", not " + sha256 + "; upload the file again");
            }
            Files.createDirectories(target.getParent());
            Files.move(temp, target, StandardCopyOption.REPLACE_EXISTING, StandardCopyOption.ATOMIC_MOVE);
        } finally {
            Files.deleteIfExists(temp);
        }
        deleteTree(folder);
    }

    /**
     * Deletes stored blobs that are not in {@code keep} and haven't been touched since {@code olderThan}, and uploads
     * nobody has added to for {@code updates.upload-expiry}. Returns how many blobs were deleted.
     */
    public int collect(Set<String> keep, Instant olderThan) throws IOException {
        int deleted = 0;
        try (Stream<Path> files = Files.walk(blobs)) {
            for (Path file : (Iterable<Path>) files.filter(Files::isRegularFile)::iterator) {
                String name = file.getFileName().toString();
                boolean stale = Files.getLastModifiedTime(file).toInstant().isBefore(olderThan);
                if (stale && !keep.contains(name) && (Sha256.isValid(name) || name.startsWith(TEMP))) {
                    Files.deleteIfExists(file);
                    deleted += Sha256.isValid(name) ? 1 : 0;
                }
            }
        }
        Instant expired = Instant.now(clock).minus(properties.uploadExpiry());
        try (DirectoryStream<Path> folders = Files.newDirectoryStream(uploads)) {
            for (Path folder : folders) {
                if (lastChange(folder).isBefore(expired)) {
                    deleteTree(folder);
                }
            }
        }
        if (deleted > 0) {
            log.info("Deleted {} unreferenced update blobs", deleted);
        }
        return deleted;
    }

    private Path blobPath(String sha256) {
        return blobs.resolve(checked(sha256).substring(0, 2)).resolve(sha256);
    }

    private static Path partPath(Path folder, int part) {
        return folder.resolve("part-" + part);
    }

    private static String checked(String sha256) {
        if (!Sha256.isValid(sha256)) {
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "sha256 must be 64 lowercase hex characters");
        }
        return sha256;
    }

    /** Copies at most {@code limit} bytes; -1 when there was more. */
    private static long copy(InputStream in, OutputStream out, long limit) throws IOException {
        byte[] buffer = new byte[64 * 1024];
        long total = 0;
        for (int n; (n = in.read(buffer)) >= 0; ) {
            total += n;
            if (total > limit) {
                return -1;
            }
            out.write(buffer, 0, n);
        }
        return total;
    }

    private static Instant lastChange(Path folder) throws IOException {
        Instant latest = Files.getLastModifiedTime(folder).toInstant();
        try (Stream<Path> files = Files.list(folder)) {
            for (Path file : (Iterable<Path>) files::iterator) {
                Instant changed = Files.getLastModifiedTime(file).toInstant();
                latest = changed.isAfter(latest) ? changed : latest;
            }
        }
        return latest;
    }

    private static void deleteTree(Path folder) throws IOException {
        if (!Files.exists(folder)) {
            return;
        }
        try (Stream<Path> files = Files.walk(folder)) {
            for (Path file : (Iterable<Path>) files.sorted((a, b) -> b.getNameCount() - a.getNameCount())::iterator) {
                Files.deleteIfExists(file);
            }
        }
    }

    private static ResponseStatusException tooLarge(String message) {
        return new ResponseStatusException(HttpStatus.PAYLOAD_TOO_LARGE, message);
    }
}
