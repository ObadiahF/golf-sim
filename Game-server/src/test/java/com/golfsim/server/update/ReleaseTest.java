package com.golfsim.server.update;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.ObjectMapper;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.attribute.FileTime;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.stream.Stream;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.beans.factory.annotation.Autowired;

/** Creating releases: every blob uploaded, newest build wins, manifest paths checked, old releases and blobs dropped. */
class ReleaseTest extends UpdateTestBase {

    private static final TestFile EXE = new TestFile("GolfSim.exe", "exe v1");
    private static final TestFile DATA = new TestFile("GolfSim_Data/data.unity3d", "data v1");

    @Autowired
    private BlobStore blobs;

    @Autowired
    private UpdateService updates;

    @Test
    void theNewestBuildIsLatest() throws Exception {
        user(get("/api/updates/latest?platform=windows-x64")).andExpect(status().isNoContent());
        release("windows-x64", "2026.10.02-1754-abc", 20261002175400L, List.of(EXE, DATA))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.version").value("2026.10.02-1754-abc"))
                .andExpect(jsonPath("$.totalSize").value(13))
                .andExpect(jsonPath("$.fileCount").value(2));
        TestFile newData = new TestFile("GolfSim_Data/data.unity3d", "data v2");
        release("windows-x64", "2026.10.03-0900-def", 20261003090000L, List.of(EXE, newData)).andExpect(status().isCreated());

        user(get("/api/updates/latest?platform=windows-x64")).andExpect(status().isOk())
                .andExpect(jsonPath("$.platform").value("windows-x64"))
                .andExpect(jsonPath("$.build").value(20261003090000L))
                .andExpect(jsonPath("$.note").value("test"))
                .andExpect(jsonPath("$.createdAt").exists())
                .andExpect(jsonPath("$.files.length()").value(2))
                .andExpect(jsonPath("$.files[0].path").value("GolfSim.exe"))
                .andExpect(jsonPath("$.files[0].sha256").value(EXE.sha256()))
                .andExpect(jsonPath("$.files[0].executable").value(false))
                .andExpect(jsonPath("$.files[1].sha256").value(newData.sha256()));
        user(get("/api/updates/releases?platform=windows-x64")).andExpect(status().isOk())
                .andExpect(jsonPath("$.length()").value(2))
                .andExpect(jsonPath("$[0].version").value("2026.10.03-0900-def"))
                .andExpect(jsonPath("$[0].files").doesNotExist());
        user(get("/api/updates/latest?platform=macos")).andExpect(status().isNoContent());
        user(get("/api/updates/latest?platform=../x")).andExpect(status().isBadRequest());
        user(get("/api/updates/latest")).andExpect(status().isBadRequest());

        // Builds only go up, and a version is published once.
        publish("windows-x64", "older", 20261003090000L, List.of(EXE)).andExpect(status().isConflict());
        publish("windows-x64", "2026.10.03-0900-def", 20261004000000L, List.of(EXE)).andExpect(status().isConflict());
        publish("macos", "2026.10.03-0900-def", 1, List.of(EXE)).andExpect(status().isCreated());
    }

    @Test
    void everyFileMustBeUploadedWithItsSize() throws Exception {
        upload(EXE.content());
        publish("macos", "v1", 1, List.of(EXE, DATA)).andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['files[1].sha256']").value("not uploaded"));
        String body = "{\"platform\":\"macos\",\"version\":\"v1\",\"build\":1,\"files\":[{\"path\":\"a\",\"size\":99,"
                + "\"sha256\":\"" + EXE.sha256() + "\",\"executable\":true}]}";
        admin(post("/api/updates/publish/releases")
                .contentType("application/json").content(body))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['files[0].size']").value("the uploaded file is 6 bytes"));
        assertThat(jdbc.queryForObject("select count(*) from update_releases", Long.class)).isZero();
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "/etc/passwd", "../evil.exe", "GolfSim_Data/../../evil.exe", "a/./b", "a//b", "a/", "C:/Windows/x.dll",
            "C:evil", "a\\\\..\\\\b", "file.txt:stream", "a/b?", "a/b*", "a/<b>", "a/b|c", "CON", "data/nul.txt",
            "com1", "a/trailing.", "a/trailing ", " lead", "a\\u0001b", ""})
    void unsafePathsAreRefused(String path) throws Exception {
        TestFile file = new TestFile(path.replace("\\\\", "\\").replace("\\u0001", "\u0001"), "x");
        upload(file.content());
        String json = "{\"platform\":\"macos\",\"version\":\"v1\",\"build\":1,\"files\":[{\"path\":"
                + new ObjectMapper().writeValueAsString(file.path())
                + ",\"size\":1,\"sha256\":\"" + file.sha256() + "\"}]}";
        admin(post("/api/updates/publish/releases")
                .contentType("application/json").content(json))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['files[0].path']").exists());
        assertThat(jdbc.queryForObject("select count(*) from update_releases", Long.class)).isZero();
    }

    @Test
    void pathsThatCollideAreRefusedAndOrdinaryOnesAccepted() throws Exception {
        publish("macos", "v1", 1, List.of(new TestFile("Data/a.txt", "1"), new TestFile("data/A.TXT", "2")))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['files[1].path']").value(
                        "duplicate of files[0] (paths are compared ignoring case)"));
        publish("macos", "v1", 1, List.of(new TestFile("x", "1"), new TestFile("X/y", "2")))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['files[0].path']").value("is a file and also a folder of another path"));
        release("macos", "v1", 1, List.of(new TestFile("Contents/MacOS/Golf-sim", "bin"),
                new TestFile("Contents/Resources/Data/a b (1).txt", "x"), new TestFile(".hidden", "h")))
                .andExpect(status().isCreated());
    }

    @Test
    void onlyTheNewestReleasesAndTheirBlobsAreKept() throws Exception {
        TestFile shared = new TestFile("shared.dll", "same in every release");
        List<TestFile> uniques = new ArrayList<>();
        for (int i = 1; i <= 6; i++) {
            TestFile unique = new TestFile("data.bin", "release " + i);
            uniques.add(unique);
            release("windows-x64", "v" + i, i, List.of(shared, unique)).andExpect(status().isCreated());
        }
        user(get("/api/updates/releases?platform=windows-x64")).andExpect(jsonPath("$.length()").value(5))
                .andExpect(jsonPath("$[4].version").value("v2"));
        assertThat(jdbc.queryForObject("select count(*) from update_files", Long.class)).isEqualTo(10);

        // Fresh blobs survive the grace period (a publish may still need them); once old, the unreferenced one goes.
        assertThat(blobs.size(uniques.get(0).sha256())).isEqualTo(9);
        Path stale = updateProperties.dir().resolve("uploads").resolve(sha("abandoned".getBytes()));
        Files.createDirectories(stale);
        Files.writeString(stale.resolve("part-0"), "half");
        age(updateProperties.dir());
        assertThat(updates.collectGarbage()).isEqualTo(1);
        assertThat(blobs.size(uniques.get(0).sha256())).isEqualTo(-1);
        assertThat(Files.exists(stale)).isFalse();
        for (TestFile kept : List.of(shared, uniques.get(1), uniques.get(5))) {
            assertThat(blobs.size(kept.sha256())).isEqualTo(kept.content().length);
        }
    }

    private static void age(Path dir) throws Exception {
        FileTime old = FileTime.from(Instant.now().minusSeconds(3 * 24 * 3600));
        try (Stream<Path> files = Files.walk(dir)) {
            for (Path file : (Iterable<Path>) files::iterator) {
                Files.setLastModifiedTime(file, old);
            }
        }
    }
}
