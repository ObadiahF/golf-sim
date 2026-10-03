package com.golfsim.server.update;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.golfsim.server.IntegrationTest;
import java.nio.charset.StandardCharsets;
import java.util.Arrays;
import java.util.List;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

/** Uploads and releases through the publish API, as the publish tool does it. */
abstract class UpdateTestBase extends IntegrationTest {

    @Autowired
    protected MockMvc mockMvc;

    /** A file of a test release. */
    record TestFile(String path, byte[] content) {

        TestFile(String path, String content) {
            this(path, content.getBytes(StandardCharsets.UTF_8));
        }

        String sha256() {
            return sha(content);
        }

        String json() {
            return "{\"path\":\"" + path + "\",\"size\":" + content.length + ",\"sha256\":\"" + sha256() + "\"}";
        }
    }

    static String sha(byte[] content) {
        var digest = Sha256.digest();
        digest.update(content);
        return Sha256.hex(digest);
    }

    protected ResultActions admin(MockHttpServletRequestBuilder request) throws Exception {
        return mockMvc.perform(request.header(HttpHeaders.AUTHORIZATION, "Bearer " + UPDATE_TOKEN));
    }

    protected ResultActions user(MockHttpServletRequestBuilder request) throws Exception {
        return mockMvc.perform(request.header(HttpHeaders.AUTHORIZATION, "Bearer " + TOKEN));
    }

    protected ResultActions putPart(String sha256, int part, byte[] content) throws Exception {
        return admin(put("/api/updates/publish/blobs/" + sha256 + "/parts/" + part)
                .contentType(MediaType.APPLICATION_OCTET_STREAM).content(content));
    }

    protected ResultActions complete(String sha256, long size, int parts) throws Exception {
        return admin(post("/api/updates/publish/blobs/" + sha256 + "/complete").contentType(MediaType.APPLICATION_JSON)
                .content("{\"size\":" + size + ",\"parts\":" + parts + "}"));
    }

    /** Uploads the content in parts of at most MAX_PART bytes; returns the number of parts. */
    protected int upload(byte[] content) throws Exception {
        String sha = sha(content);
        int parts = Math.max(1, (content.length + MAX_PART - 1) / MAX_PART);
        for (int i = 0; i < parts; i++) {
            putPart(sha, i, Arrays.copyOfRange(content, i * MAX_PART, Math.min(content.length, (i + 1) * MAX_PART)))
                    .andExpect(status().isOk());
        }
        complete(sha, content.length, parts).andExpect(status().isOk());
        return parts;
    }

    protected ResultActions publish(String platform, String version, long build, List<TestFile> files) throws Exception {
        String body = "{\"platform\":\"" + platform + "\",\"version\":\"" + version + "\",\"build\":" + build
                + ",\"note\":\"test\",\"files\":[" + String.join(",", files.stream().map(TestFile::json).toList()) + "]}";
        return admin(post("/api/updates/publish/releases").contentType(MediaType.APPLICATION_JSON).content(body));
    }

    /** Uploads every file and publishes them as a release. */
    protected ResultActions release(String platform, String version, long build, List<TestFile> files) throws Exception {
        for (TestFile file : files) {
            upload(file.content());
        }
        return publish(platform, version, build, files);
    }
}
