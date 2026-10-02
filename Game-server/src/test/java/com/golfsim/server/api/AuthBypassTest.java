package com.golfsim.server.api;

import static org.assertj.core.api.Assertions.assertThat;

import com.golfsim.server.IntegrationTest;
import com.golfsim.server.RawHttp;
import java.util.Map;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.junit.jupiter.api.Test;
import org.springframework.boot.test.web.server.LocalServerPort;

/** GS-1: the token check can't be skipped with path tricks; it runs on everything but an explicit allow-list. */
class AuthBypassTest extends IntegrationTest {

    private static final Map<String, String> JSON = Map.of("Content-Type", "application/json");

    @LocalServerPort
    private int port;

    @ParameterizedTest
    @ValueSource(strings = {
            "/api;/players", "/api;jsessionid=x/players", "/api/players;", "/api;/leaderboard",
            "//api/players", "/api//players", "///api/players",
            "/api/../api/players", "/x/../api/players", "/api/./players", "/./api/players", "/x/..;/api/players",
            "/api/%2e%2e/api/players", "/x/%2E%2E/api/players", "/api/%2e/players",
            "/api%2Fplayers", "/api%2fplayers", "/x/..%2Fapi/players", "/api%5Cplayers", "/api\\players",
            "/actuator/health/../../api/players", "/actuator/health;/../../api/players",
            "/actuator/health/..%2F..%2Fapi/players", "/api/players/%zz", "/api/players/%00"})
    void nonNormalisedPathsAreRefused(String target) throws Exception {
        assertThat(RawHttp.get(port, target)).as(target).isEqualTo(400);
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "/api/players", "/%61pi/leaderboard", "/%61%70%69/players", "/a%70i/games/current", "/API/players",
            "/Api/leaderboard", "/api/PLAYERS", "/api", "/api/", "/", "/error", "/actuator", "/actuator/info",
            "/actuator/env", "/actuator/healthx", "/ws/x", "/api/ping"})
    void everythingElseNeedsTheToken(String target) throws Exception {
        assertThat(RawHttp.get(port, target)).as(target).isEqualTo(401);
    }

    @Test
    void writesThroughPathTricksAreRefusedAndChangeNothing() throws Exception {
        String body = "{\"players\":[\"Intruder\"],\"holes\":1}";
        assertThat(RawHttp.status(port, "POST", "/api;/games", JSON, body)).isEqualTo(400);
        assertThat(RawHttp.status(port, "POST", "/%61pi/games", JSON, body)).isEqualTo(401);
        assertThat(RawHttp.status(port, "POST", "/api;/games/1/end", JSON, null)).isEqualTo(400);
        assertThat(RawHttp.status(port, "POST", "/%61pi/games/1/end", JSON, null)).isEqualTo(401);
        assertThat(jdbc.queryForObject("select count(*) from games", Long.class)).isZero();
    }

    @Test
    void allowListAndTokenStillWork() throws Exception {
        assertThat(RawHttp.get(port, "/actuator/health")).isEqualTo(200);
        assertThat(RawHttp.get(port, "/actuator/health/liveness")).isEqualTo(200);
        Map<String, String> auth = Map.of("Authorization", "Bearer " + TOKEN);
        assertThat(RawHttp.status(port, "GET", "/api/players", auth, null)).isEqualTo(200);
        assertThat(RawHttp.status(port, "GET", "/%61pi/players", auth, null)).isEqualTo(200);
        assertThat(RawHttp.status(port, "GET", "/api/players/Obi%20Smith", auth, null)).isEqualTo(404);
        assertThat(RawHttp.status(port, "GET", "/API/players", auth, null)).isEqualTo(404);
    }
}
