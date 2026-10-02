package com.golfsim.server.ws;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.RawHttp;
import com.golfsim.server.game.GameRequests;
import com.golfsim.server.game.GameStatus;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;

/** GS-2, GS-5, GS-8, GS-9, GS-10, Q5-9 over the WebSocket. */
class GameSocketInputTest extends SocketTestBase {

    private static final String GRIN = "%F0%9F%98%80";
    private static final Map<String, String> UPGRADE = Map.of(
            "Upgrade", "websocket", "Connection", "Upgrade",
            "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==", "Sec-WebSocket-Version", "13");

    private int upgrade(String query) throws Exception {
        return RawHttp.status(port, "GET", "/ws?" + query, UPGRADE, null);
    }

    @Test
    void emojiDeviceNameAtTheLimitDoesNotBreakHelloForAnyone() throws Exception {
        try (WsTestClient emoji = remote("A".repeat(39) + GRIN);
                WsTestClient longName = remote("B".repeat(40) + GRIN);
                WsTestClient sim = sim();
                WsTestClient other = remote()) {
            emoji.await("hello");
            longName.await("hello");
            sim.await("hello");
            JsonNode hello = other.await("hello");
            assertThat(hello.get("remotes")).extracting(JsonNode::asText)
                    .contains("A".repeat(39) + "😀", "B".repeat(40));
        }
    }

    @Test
    void invisibleDeviceNameFallsBackToTheDefault() throws Exception {
        try (WsTestClient remote = remote("%E2%80%8B%E2%80%8B%00")) {
            assertThat(remote.await("hello").get("remotes").get(0).asText()).startsWith("remote-");
        }
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "token=golf-sim-dev-token&role=remote&name=%zz", "token=golf-sim-dev-token&role=remote&name=%",
            "token=golf-sim-dev-token&role=remote&name=%E", "token=%zz&role=remote", "token=golf-sim-dev-token&role=%zz"})
    void malformedQueryIs400AndHarmsNoOne(String query) throws Exception {
        assertThat(upgrade(query)).isEqualTo(400);
        try (WsTestClient remote = remote()) {
            remote.await("hello");
        }
    }

    @Test
    void wellFormedRawUpgradeSucceeds() throws Exception {
        assertThat(upgrade("token=golf-sim-dev-token&role=remote&name=Raw")).isEqualTo(101);
        assertThat(upgrade("token=nope&role=remote")).isEqualTo(401);
    }

    @Test
    void oversizedMessagesGetAnErrorAndTheConnectionStaysOpen() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            sim.await("hello");
            String medium = "{\"type\":\"state\",\"screen\":\"" + "m".repeat(20_000) + "\"}";
            sim.send(medium);
            assertThat(remote.await("state").get("screen").asText()).hasSize(20_000);

            sim.send("{\"type\":\"state\",\"screen\":\"" + "x".repeat(70_000) + "\"}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("Message too large (max 65536 characters)");
            remote.assertNo("state");
            assertThat(sim.isOpen()).isTrue();
            remote.assertNo("simStatus");

            String large = "{\"type\":\"state\",\"screen\":\"" + "y".repeat(60_000) + "\"}";
            sim.send(large);
            assertThat(remote.await("state").get("screen").asText()).hasSize(60_000);
            sim.send("{\"type\":\"ping\"}");
            sim.await("pong");
        }
    }

    @ParameterizedTest
    @ValueSource(strings = {
            "{\"type\":\"shot\",\"speed\":1e400,\"launch\":12,\"azimuth\":0,\"back\":2600,\"side\":0}|Invalid value for 'speed': numbers must be finite",
            "{\"type\":\"shot\",\"speed\":60,\"launch\":12,\"azimuth\":0,\"back\":-1e400,\"side\":0}|Invalid value for 'back': numbers must be finite",
            "{\"type\":\"aim\",\"delta\":1e400}|Invalid value for 'delta': numbers must be finite",
            "{\"type\":\"shot\",\"speed\":60,\"launch\":12,\"azimuth\":0,\"back\":2600,\"side\":0,\"id\":1.9}|Invalid value for 'id'",
            "{\"type\":\"shot\",\"speed\":\"60\",\"launch\":12,\"azimuth\":0,\"back\":2600,\"side\":0}|Invalid value for 'speed'",
            "{\"type\":\"aim\",\"delta\":2,\"extra\":{\"deep\":[1,1e999]}}|Invalid value for 'extra.deep[1]': numbers must be finite"})
    void nonFiniteAndWrongTypedValuesAreNotRelayed(String caseLine) throws Exception {
        String[] parts = caseLine.split("\\|");
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            remote.send(parts[0]);
            assertThat(remote.await("error").get("message").asText()).isEqualTo(parts[1]);
            sim.assertNo("shot");
            sim.assertNo("aim");
        }
    }

    /** Q5-9: parse errors are refused with a clean message, never Jackson's wording. */
    @ParameterizedTest
    @ValueSource(strings = {
            "{\"type\":\"aim\",\"delta\":NaN}|Invalid number",
            "{\"type\":\"aim\",\"delta\":Infinity}|Invalid number",
            "{\"type\":\"aim\",\"delta\":-Infinity}|Invalid number",
            "{\"type\":\"aim\",\"delta\":}|Invalid JSON",
            "{\"type\":\"aim\" \"delta\":1}|Invalid JSON",
            "{\"type\":\"aim\",\"delta\":tru}|Invalid JSON",
            "{\"type\":\"aim\",\"delta\":1|Invalid JSON"})
    void parseErrorsAreRefusedWithoutLibraryText(String caseLine) throws Exception {
        String[] parts = caseLine.split("\\|");
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            remote.send(parts[0]);
            String message = remote.await("error").get("message").asText();
            assertThat(message).isEqualTo(parts[1]);
            assertThat(message).doesNotContain("JsonReadFeature", "enable", "token", "line:", "column:");
            sim.assertNo("aim");
        }
    }

    @Test
    void duplicateKeysAreRejected() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            remote.send("{\"type\":\"type\",\"type\":\"nav\",\"key\":\"up\"}");
            assertThat(remote.await("error").get("message").asText()).startsWith("Invalid JSON: Duplicate field 'type'");
            remote.send("{\"type\":\"aim\",\"delta\":1,\"delta\":90}");
            remote.await("error");
            sim.assertNo("nav");
            sim.assertNo("aim");
        }
    }

    @Test
    void simUpdatesAndScoresAreCheckedToo() throws Exception {
        long id = games.start(new GameRequests.StartGame(List.of("Obi"), 2, null)).id();
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            sim.send("{\"type\":\"state\",\"screen\":\"game\",\"aim\":1e400}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("Invalid value for 'aim': numbers must be finite");
            sim.send("{\"type\":\"holeScore\",\"gameId\":" + id + ",\"player\":\"Obi\",\"hole\":1.5,\"par\":4,\"strokes\":4}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("Invalid value for 'hole'");
            sim.send("{\"type\":\"holeScore\",\"gameId\":" + id + ".0,\"player\":\"Obi\",\"hole\":1,\"par\":4,\"strokes\":4}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("Invalid value for 'gameId'");
            remote.assertNo("state");
            remote.assertNo("scorecard");
        }
    }

    /** GS-2: four sims send the last scores at the same moment; everyone gets exactly one gameFinished. */
    @Test
    void simultaneousFinalHoleScoresBroadcastGameFinishedOnce() throws Exception {
        List<String> names = List.of("W1", "W2", "W3", "W4");
        List<WsTestClient> sims = new ArrayList<>();
        try (WsTestClient remote = remote()) {
            for (int i = 0; i < names.size(); i++) {
                sims.add(sim());
                sims.get(i).await("hello");
            }
            for (int trial = 0; trial < 5; trial++) {
                long id = games.start(new GameRequests.StartGame(names, 1, null)).id();
                for (int i = 0; i < names.size(); i++) {
                    sims.get(i).send("{\"type\":\"holeScore\",\"gameId\":" + id + ",\"player\":\"" + names.get(i)
                            + "\",\"hole\":1,\"par\":4,\"strokes\":4}");
                }
                JsonNode finished = remote.await("gameFinished").get("game");
                assertThat(finished.get("id").asLong()).isEqualTo(id);
                assertThat(finished.get("status").asText()).isEqualTo("FINISHED");
                assertThat(finished.get("winners")).hasSize(4);
                remote.assertNo("gameFinished");
                assertThat(games.get(id).status()).isEqualTo(GameStatus.FINISHED);
            }
        } finally {
            for (WsTestClient sim : sims) {
                sim.close();
            }
        }
    }
}
