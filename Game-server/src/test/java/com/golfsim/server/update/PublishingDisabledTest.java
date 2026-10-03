package com.golfsim.server.update;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import org.junit.jupiter.api.Test;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.test.context.TestPropertySource;

/** Without GOLF_UPDATE_TOKEN nothing can publish, whatever token is sent; the sim can still read. */
@TestPropertySource(properties = "updates.token=")
class PublishingDisabledTest extends UpdateTestBase {

    @Test
    void publishEndpointsAre404() throws Exception {
        for (String token : new String[] {UPDATE_TOKEN, TOKEN, ""}) {
            mockMvc.perform(post("/api/updates/publish/blobs/missing").header(HttpHeaders.AUTHORIZATION, "Bearer " + token)
                            .contentType(MediaType.APPLICATION_JSON).content("{\"sha256\":[]}"))
                    .andExpect(status().isNotFound())
                    .andExpect(jsonPath("$.message").value("Publishing is disabled (no GOLF_UPDATE_TOKEN)"));
        }
        user(get("/api/updates/latest?platform=windows-x64")).andExpect(status().isNoContent());
    }
}
