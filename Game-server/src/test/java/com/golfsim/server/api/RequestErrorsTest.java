package com.golfsim.server.api;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.delete;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.patch;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.put;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.golfsim.server.IntegrationTest;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

/** GS-6, GS-7, GS-8, GS-10, GS-12: bad requests get clean 4xx JSON errors, never 500. */
class RequestErrorsTest extends IntegrationTest {

    @Autowired
    private MockMvc mockMvc;

    private ResultActions call(MockHttpServletRequestBuilder request) throws Exception {
        return mockMvc.perform(request.header(HttpHeaders.AUTHORIZATION, "Bearer " + TOKEN));
    }

    private ResultActions startGame(String json) throws Exception {
        return call(post("/api/games").contentType(MediaType.APPLICATION_JSON).content(json));
    }

    @Test
    void wrongMethodIs405WithAllowHeader() throws Exception {
        call(delete("/api/games/1")).andExpect(status().isMethodNotAllowed())
                .andExpect(jsonPath("$.status").value(405))
                .andExpect(jsonPath("$.path").value("/api/games/1"))
                .andExpect(header().exists(HttpHeaders.ALLOW));
        call(put("/api/games/1")).andExpect(status().isMethodNotAllowed());
        call(patch("/api/games/1")).andExpect(status().isMethodNotAllowed());
        call(post("/api/ping")).andExpect(status().isMethodNotAllowed()).andExpect(jsonPath("$.status").value(405));
    }

    @Test
    void wrongContentTypeIs415() throws Exception {
        call(post("/api/games").contentType(MediaType.TEXT_PLAIN).content("x"))
                .andExpect(status().isUnsupportedMediaType())
                .andExpect(jsonPath("$.status").value(415));
        call(post("/api/games/1/scores").contentType(MediaType.APPLICATION_FORM_URLENCODED).content("a=b"))
                .andExpect(status().isUnsupportedMediaType());
    }

    @ParameterizedTest
    @ValueSource(strings = {"a\\u0000b", "tab\\tname", "new\\nline", "\\u200B", " ", "\\u00A0", "", "a\\u202Eb"})
    void badNamesAre400(String jsonEscapedName) throws Exception {
        startGame("{\"players\":[\"" + jsonEscapedName + "\"],\"holes\":1}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['players[0]']").value(org.hamcrest.Matchers.startsWith("must be 1 to 40")));
    }

    @Test
    void namesUpTo40CodePointsAreAccepted() throws Exception {
        String emoji21 = "😀".repeat(21);
        startGame("{\"players\":[\"" + emoji21 + "\"],\"holes\":1}")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.players[0].name").value(emoji21));
        startGame("{\"players\":[\"" + "😀".repeat(41) + "\"],\"holes\":1}").andExpect(status().isBadRequest());
    }

    @Test
    void turkishDottedCapitalIMatchesTheSamePlayer() throws Exception {
        startGame("{\"players\":[\"İvan\"],\"holes\":1}").andExpect(status().isCreated());
        startGame("{\"players\":[\"ivan\"],\"holes\":1}").andExpect(status().isCreated())
                .andExpect(jsonPath("$.players[0].name").value("İvan"));
        startGame("{\"players\":[\"IVAN\"],\"holes\":1}").andExpect(status().isCreated());
        startGame("{\"players\":[\"İvan\",\"ivan\"],\"holes\":1}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Duplicate player name: ivan"));
        call(get("/api/players/IVAN")).andExpect(status().isOk()).andExpect(jsonPath("$.stats.gamesPlayed").value(3));
        org.assertj.core.api.Assertions.assertThat(jdbc.queryForObject("select count(*) from players", Long.class)).isOne();
    }

    @Test
    void numbersMustHaveTheRightType() throws Exception {
        startGame("{\"players\":[\"Obi\"],\"holes\":1.7}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Invalid value for 'holes'"));
        startGame("{\"players\":[\"Obi\"],\"holes\":\"9\"}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Invalid value for 'holes'"));
        startGame("{\"players\":[\"Obi\"],\"holes\":1e400}").andExpect(status().isBadRequest());
    }

    @Test
    void trailingDataAfterTheBodyIs400() throws Exception {
        startGame("{\"players\":[\"Obi\"],\"holes\":1}garbage").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Malformed JSON request body"));
        startGame("{\"players\":[\"Obi\"],\"holes\":1} {\"x\":1}").andExpect(status().isBadRequest());
    }

    /** Postgres refuses NUL (it used to come back as a "please retry" 409) and turns a lone surrogate into '?'. */
    @ParameterizedTest
    @ValueSource(strings = {"x\\u0000y", "tab\\there", "a\\ud800b"})
    void courseNamesWithControlOrInvalidCharactersAre400(String jsonEscaped) throws Exception {
        startGame("{\"players\":[\"Obi\"],\"holes\":1,\"courseName\":\"" + jsonEscaped + "\"}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.courseName").value("must not contain control or invalid characters"));
        startGame("{\"players\":[\"Obi\"],\"holes\":1,\"courseName\":\"Pebble 😀 Beach\"}").andExpect(status().isCreated());
    }

    @Test
    void outOfRangeQueryParameterIsNamed() throws Exception {
        call(get("/api/games?limit=0")).andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.limit").value("must be greater than or equal to 1"));
        call(get("/api/games?limit=101")).andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.limit").exists());
    }

    @Test
    void duplicateKeysAre400() throws Exception {
        startGame("{\"players\":[\"Obi\"],\"holes\":1,\"holes\":2}").andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Malformed JSON request body"));
    }
}
