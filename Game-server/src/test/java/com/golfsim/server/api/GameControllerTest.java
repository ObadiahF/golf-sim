package com.golfsim.server.api;

import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.golfsim.server.IntegrationTest;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.HttpHeaders;
import org.springframework.http.MediaType;
import org.springframework.test.web.servlet.MockMvc;
import org.springframework.test.web.servlet.ResultActions;
import org.springframework.test.web.servlet.request.MockHttpServletRequestBuilder;

class GameControllerTest extends IntegrationTest {

    @Autowired
    private MockMvc mockMvc;

    @Autowired
    private ObjectMapper objectMapper;

    private ResultActions call(MockHttpServletRequestBuilder request) throws Exception {
        return mockMvc.perform(request.header(HttpHeaders.AUTHORIZATION, "Bearer " + TOKEN));
    }

    private ResultActions postJson(String path, String json) throws Exception {
        return call(post(path).contentType(MediaType.APPLICATION_JSON).content(json));
    }

    private long startGame(String json) throws Exception {
        String body = postJson("/api/games", json).andExpect(status().isCreated()).andReturn().getResponse().getContentAsString();
        return objectMapper.readTree(body).get("id").asLong();
    }

    @Test
    void startGameReturnsFullState() throws Exception {
        postJson("/api/games", "{\"players\":[\"Obi\",\"Sam\"],\"holes\":3,\"courseName\":\"Pebble\"}")
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.status").value("IN_PROGRESS"))
                .andExpect(jsonPath("$.holesCount").value(3))
                .andExpect(jsonPath("$.courseName").value("Pebble"))
                .andExpect(jsonPath("$.players[0].name").value("Obi"))
                .andExpect(jsonPath("$.players[1].turnOrder").value(2))
                .andExpect(jsonPath("$.players[1].strokes.length()").value(3))
                .andExpect(jsonPath("$.pars.length()").value(3));
    }

    @Test
    void startGameValidatesBody() throws Exception {
        postJson("/api/games", "{\"players\":[],\"holes\":30}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.players").exists())
                .andExpect(jsonPath("$.fieldErrors.holes").exists());
        postJson("/api/games", "{not json").andExpect(status().isBadRequest());
    }

    @Test
    void currentGameIsNoContentWhenIdle() throws Exception {
        call(get("/api/games/current")).andExpect(status().isNoContent());
        long id = startGame("{\"players\":[\"Obi\"]}");
        call(get("/api/games/current")).andExpect(status().isOk()).andExpect(jsonPath("$.id").value(id));
    }

    @Test
    void startAndCurrentAreScopedToTheRoomParameter() throws Exception {
        long legacy = startGame("{\"players\":[\"Obi\"]}");
        call(get("/api/games/current").param("room", "K7QF")).andExpect(status().isNoContent());

        String body = call(post("/api/games").param("room", " k7qf").contentType(MediaType.APPLICATION_JSON)
                        .content("{\"players\":[\"Sam\"]}"))
                .andExpect(status().isCreated())
                .andExpect(jsonPath("$.room").value("K7QF"))
                .andReturn().getResponse().getContentAsString();
        long roomGame = objectMapper.readTree(body).get("id").asLong();
        call(get("/api/games/current?room=K7QF")).andExpect(jsonPath("$.id").value(roomGame));
        call(get("/api/games/current")).andExpect(jsonPath("$.id").value(legacy)).andExpect(jsonPath("$.room").value(""));
        call(get("/api/games/current?room=")).andExpect(jsonPath("$.id").value(legacy));
        call(get("/api/games/" + legacy)).andExpect(jsonPath("$.status").value("IN_PROGRESS"));

        startGame("{\"players\":[\"Sam\"]}"); // the default room's restart leaves K7QF alone
        call(get("/api/games/" + legacy)).andExpect(jsonPath("$.status").value("ABANDONED"));
        call(get("/api/games/" + roomGame)).andExpect(jsonPath("$.status").value("IN_PROGRESS"));

        call(get("/api/games/current?room=K7-Q"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.room").value("must be 4 to 8 letters or digits"));
        postJson("/api/games?room=ABC", "{\"players\":[\"Sam\"]}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.room").exists());
        call(get("/api/games/" + roomGame)).andExpect(jsonPath("$.status").value("IN_PROGRESS"));
    }

    @Test
    void scoresBuildTheScorecardAndFinishTheGame() throws Exception {
        long id = startGame("{\"players\":[\"Obi\",\"Sam\"],\"holes\":1}");
        postJson("/api/games/" + id + "/scores", "{\"player\":\"Obi\",\"hole\":1,\"par\":4,\"strokes\":3}")
                .andExpect(status().isOk())
                .andExpect(jsonPath("$.status").value("IN_PROGRESS"))
                .andExpect(jsonPath("$.players[0].toPar").value(-1));
        postJson("/api/games/" + id + "/scores", "{\"player\":\"Sam\",\"hole\":1,\"par\":4,\"strokes\":4}")
                .andExpect(jsonPath("$.status").value("FINISHED"))
                .andExpect(jsonPath("$.winners[0]").value("Obi"));

        call(get("/api/games/" + id))
                .andExpect(jsonPath("$.players[0].strokes[0]").value(3))
                .andExpect(jsonPath("$.pars[0]").value(4));
        call(get("/api/games?limit=5")).andExpect(jsonPath("$[0].id").value(id));
        call(get("/api/players"))
                .andExpect(jsonPath("$[0].name").value("Obi"))
                .andExpect(jsonPath("$[0].wins").value(1));
        call(get("/api/players/sam"))
                .andExpect(jsonPath("$.stats.name").value("Sam"))
                .andExpect(jsonPath("$.recentGames[0].gameId").value(id));
        call(get("/api/leaderboard")) // a 1-hole round wins but is no best round (only 9 and 18 holes are)
                .andExpect(jsonPath("$.mostWins[0].player").value("Obi"))
                .andExpect(jsonPath("$.bestRounds").isEmpty());
    }

    @Test
    void scoreValidationAndErrors() throws Exception {
        long id = startGame("{\"players\":[\"Obi\"]}");
        postJson("/api/games/" + id + "/scores", "{\"player\":\"Obi\",\"hole\":0,\"par\":4}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors.hole").exists())
                .andExpect(jsonPath("$.fieldErrors.strokes").exists());
        postJson("/api/games/" + id + "/scores", "{\"player\":\"Zed\",\"hole\":1,\"par\":4,\"strokes\":4}")
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Player 'Zed' is not in game " + id));
        call(get("/api/games/9999")).andExpect(status().isNotFound());
        call(get("/api/players/nobody")).andExpect(status().isNotFound());
        call(get("/api/games?limit=0")).andExpect(status().isBadRequest());
    }

    @Test
    void endAbandonsByDefaultAndRejectsTwice() throws Exception {
        long id = startGame("{\"players\":[\"Obi\"]}");
        call(post("/api/games/" + id + "/end")).andExpect(status().isOk()).andExpect(jsonPath("$.status").value("ABANDONED"));
        postJson("/api/games/" + id + "/end", "{\"status\":\"FINISHED\"}").andExpect(status().isConflict());

        long second = startGame("{\"players\":[\"Obi\"]}");
        postJson("/api/games/" + second + "/end", "{\"status\":\"FINISHED\"}").andExpect(jsonPath("$.status").value("FINISHED"));
        JsonNode games = objectMapper.readTree(call(get("/api/games")).andReturn().getResponse().getContentAsString());
        org.assertj.core.api.Assertions.assertThat(games).hasSize(2);
    }
}
