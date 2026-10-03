package com.golfsim.server.update;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.golfsim.server.RawHttp;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.web.server.LocalServerPort;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;

/** Publishing takes the admin token only; reading takes the shared token only. */
class UpdateAuthTest extends UpdateTestBase {

    private static final String MISSING = "{\"sha256\":[]}";

    @LocalServerPort
    private int port;

    @Test
    void publishingNeedsTheAdminToken() throws Exception {
        user(post("/api/updates/publish/blobs/missing").contentType(MediaType.APPLICATION_JSON).content(MISSING))
                .andExpect(status().isUnauthorized());
        mockMvc.perform(post("/api/updates/publish/blobs/missing").contentType(MediaType.APPLICATION_JSON).content(MISSING))
                .andExpect(status().isUnauthorized());
        mockMvc.perform(post("/api/updates/publish/blobs/missing").contentType(MediaType.APPLICATION_JSON).content(MISSING)
                .header(HttpHeaders.AUTHORIZATION, "Bearer " + UPDATE_TOKEN + "x")).andExpect(status().isUnauthorized());
        admin(post("/api/updates/publish/blobs/missing").contentType(MediaType.APPLICATION_JSON).content(MISSING))
                .andExpect(status().isOk());
        String sha = sha(new byte[] {1});
        mockMvc.perform(post("/api/updates/publish/releases").header(HttpHeaders.AUTHORIZATION, "Bearer " + TOKEN)
                .contentType(MediaType.APPLICATION_JSON).content("{}")).andExpect(status().isUnauthorized());
        user(post("/api/updates/publish/blobs/" + sha + "/complete").contentType(MediaType.APPLICATION_JSON)
                .content("{\"size\":1,\"parts\":1}")).andExpect(status().isUnauthorized());
        // Unknown paths under the publish prefix are still the admin's (and then 404).
        user(get("/api/updates/publish")).andExpect(status().isUnauthorized());
        admin(get("/api/updates/publish/nothing")).andExpect(status().isNotFound());
    }

    @Test
    void readingNeedsTheSharedToken() throws Exception {
        release("macos", "v1", 1, List.of(new TestFile("a", "1"))).andExpect(status().isCreated());
        user(get("/api/updates/latest?platform=macos")).andExpect(status().isOk());
        admin(get("/api/updates/latest?platform=macos")).andExpect(status().isUnauthorized());
        mockMvc.perform(get("/api/updates/latest?platform=macos")).andExpect(status().isUnauthorized());
        admin(get("/api/updates/blobs/" + sha("1".getBytes()))).andExpect(status().isUnauthorized());
        admin(get("/api/players")).andExpect(status().isUnauthorized());
    }

    @Test
    void pathTricksDoNotReachThePublishEndpoints() throws Exception {
        Map<String, String> shared = Map.of("Authorization", "Bearer " + TOKEN, "Content-Type", "application/json");
        for (String target : List.of("/api/updates/%70ublish/blobs/missing", "/api/updates/publish%2Fblobs/missing",
                "/api/updates/x/../publish/blobs/missing", "/api;/updates/publish/blobs/missing",
                "/api/updates//publish/blobs/missing")) {
            assertThat(RawHttp.status(port, "POST", target, shared, MISSING)).as(target).isIn(400, 401);
        }
        Map<String, String> admin = Map.of("Authorization", "Bearer " + UPDATE_TOKEN, "Content-Type", "application/json");
        assertThat(RawHttp.status(port, "POST", "/api/updates/%70ublish/blobs/missing", admin, MISSING)).isEqualTo(200);
    }
}
