package com.golfsim.server.ws;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.game.GameRequests;
import com.golfsim.server.game.GameStatus;
import com.golfsim.server.game.GameView;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.junit.jupiter.params.ParameterizedTest;
import org.junit.jupiter.params.provider.ValueSource;
import org.springframework.web.socket.CloseStatus;

/** Rooms: one sim and its remotes per room; nothing crosses between rooms. */
class RoomSocketTest extends SocketTestBase {

    private static final String SHOT = "{\"type\":\"shot\",\"speed\":60,\"launch\":12,\"azimuth\":0,\"back\":2600,\"side\":0}";

    @Test
    void helloCarriesTheNormalisedRoom() throws Exception {
        try (WsTestClient remote = remoteIn("%20k7qf%20"); WsTestClient legacy = remote()) {
            JsonNode hello = remote.await("hello");
            assertThat(hello.get("room").asText()).isEqualTo("K7QF");
            assertThat(hello.get("remotes")).extracting(JsonNode::asText).containsExactly("Phone");
            assertThat(legacy.await("hello").get("room").asText()).isEmpty();
        }
        try (WsTestClient empty = remoteIn("")) {
            assertThat(empty.await("hello").get("room").asText()).isEmpty();
        }
    }

    @Test
    void twoRoomsDoNotSeeEachOthersCommandsOrStates() throws Exception {
        try (WsTestClient simA = simIn("AAAA", "tv-a"); WsTestClient remoteA = remoteIn("aaaa");
                WsTestClient simB = simIn("BBBB", "tv-b"); WsTestClient remoteB = remoteIn("BBBB")) {
            simA.await("hello");
            simB.await("hello");
            assertThat(remoteA.await("hello").get("simConnected").asBoolean()).isTrue();
            remoteB.await("hello");

            remoteA.send("{\"type\":\"club\",\"club\":\"7I\"}");
            assertThat(simA.await("club").get("club").asText()).isEqualTo("7I");
            simB.assertNo("club");

            simB.send("{\"type\":\"state\",\"screen\":\"game\",\"club\":\"Driver\"}");
            assertThat(remoteB.await("state").get("club").asText()).isEqualTo("Driver");
            remoteA.assertNo("state");

            try (WsTestClient lateA = remoteIn("AAAA"); WsTestClient lateB = remoteIn("BBBB")) {
                assertThat(lateA.await("hello").get("state").isNull()).isTrue();
                assertThat(lateB.await("hello").get("state").get("club").asText()).isEqualTo("Driver");
            }
        }
    }

    @Test
    void aRoomWithoutASimRefusesCommandsAndSimStatusStaysInTheRoom() throws Exception {
        try (WsTestClient empty = remoteIn("EMPTY"); WsTestClient legacy = remote(); WsTestClient sim = sim()) {
            assertThat(empty.await("hello").get("simConnected").asBoolean()).isFalse();
            legacy.await("hello");
            sim.await("hello");
            legacy.await("simStatus");
            empty.send(SHOT);
            assertThat(empty.await("error").get("message").asText()).isEqualTo("no sim connected");
            sim.assertNo("shot");

            try (WsTestClient other = simIn("EMPTY", null)) {
                other.await("hello");
                assertThat(empty.await("simStatus").get("connected").asBoolean()).isTrue();
                legacy.assertNo("simStatus");
            }
            assertThat(empty.await("simStatus").get("connected").asBoolean()).isFalse();
            legacy.assertNo("simStatus");
        }
    }

    @Test
    void aSecondSimWithADifferentIdIsRefusedAndTheFirstKeepsWorking() throws Exception {
        try (WsTestClient remote = remoteIn("K7QF"); WsTestClient first = simIn("K7QF", "tv-1")) {
            first.await("hello");
            remote.await("simStatus");
            for (String id : new String[] {"tv-2", null}) {
                try (WsTestClient second = simIn("k7qf", id)) {
                    assertThat(second.await("error").get("message").asText())
                            .isEqualTo("Another sim is already connected to room K7QF");
                    assertThat(second.awaitClose().getCode()).isEqualTo(4009);
                }
            }
            remote.assertNo("simStatus");
            remote.send(SHOT);
            first.await("shot");
            first.send("{\"type\":\"turn\",\"player\":\"Obi\",\"hole\":1}");
            remote.await("turn");
        }
    }

    @Test
    void aSimWithoutAnIdIsNotReplacedByOneWithout() throws Exception {
        try (WsTestClient first = simIn("NOID", null); WsTestClient second = simIn("NOID", null)) {
            first.await("hello");
            assertThat(second.await("error").get("message").asText()).isEqualTo("Another sim is already connected to room NOID");
            second.awaitClose();
            assertThat(first.isOpen()).isTrue();
        }
    }

    @Test
    void theSameIdReplacesTheOldSession() throws Exception {
        try (WsTestClient remote = remoteIn("K7QF"); WsTestClient old = simIn("K7QF", "tv-1")) {
            old.await("hello");
            remote.await("simStatus");
            old.send("{\"type\":\"state\",\"screen\":\"game\"}");
            remote.await("state");
            try (WsTestClient fresh = simIn("K7QF", "tv-1")) {
                assertThat(fresh.await("hello").get("simConnected").asBoolean()).isTrue();
                assertThat(old.await("error").get("message").asText()).isEqualTo("Replaced by a new connection from the same sim");
                assertThat(old.awaitClose()).isEqualTo(CloseStatus.POLICY_VIOLATION);
                remote.assertNo("simStatus"); // a sim was connected throughout

                remote.send(SHOT);
                fresh.await("shot");
                fresh.send("{\"type\":\"state\",\"screen\":\"menu\"}");
                assertThat(remote.await("state").get("screen").asText()).isEqualTo("menu");
            }
            assertThat(remote.await("simStatus").get("connected").asBoolean()).isFalse();
        }
    }

    @Test
    void gamesAndTheirEventsBelongToOneRoom() throws Exception {
        GameView legacyGame = games.start(new GameRequests.StartGame(List.of("Obi"), 2, null));
        try (WsTestClient simA = simIn("AAAA", "tv-a"); WsTestClient legacy = remote()) {
            assertThat(simA.await("hello").get("game").isNull()).isTrue();
            assertThat(legacy.await("hello").get("game").get("id").asLong()).isEqualTo(legacyGame.id());

            GameView roomGame = games.start(new GameRequests.StartGame(List.of("Sam"), 1, null), "AAAA");
            JsonNode started = simA.await("gameStarted").get("game");
            assertThat(started.get("id").asLong()).isEqualTo(roomGame.id());
            assertThat(started.get("room").asText()).isEqualTo("AAAA");
            legacy.assertNo("gameStarted");
            legacy.assertNo("gameFinished"); // the default room's game was not abandoned
            assertThat(games.get(legacyGame.id()).status()).isEqualTo(GameStatus.IN_PROGRESS);

            simA.send("{\"type\":\"holeScore\",\"gameId\":" + roomGame.id() + ",\"player\":\"Sam\",\"hole\":1,\"par\":3,\"strokes\":3}");
            simA.await("scorecard");
            assertThat(simA.await("gameFinished").get("game").get("room").asText()).isEqualTo("AAAA");
            legacy.assertNo("scorecard");
        }
    }

    /** The last id is 65 characters (64 is the limit). */
    @ParameterizedTest
    @ValueSource(strings = {"room=ABC", "room=ABCDEFGHI", "room=K7-Q", "room=K7%C3%9CF", "room=%zz", "id=has%20space",
            "id=a.b", "id=xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx"})
    void malformedRoomOrIdIs400(String param) throws Exception {
        assertThat(upgrade("token=" + TOKEN + "&role=sim&" + param)).isEqualTo(400);
        assertThat(upgrade("token=" + TOKEN + "&role=remote&" + param)).isEqualTo(400);
    }

    @Test
    void wellFormedRoomAndIdUpgrade() throws Exception {
        assertThat(upgrade("token=" + TOKEN + "&role=sim&room=%20ab12cd34%20&id=" + "a-Z_9".repeat(12) + "abcd")).isEqualTo(101);
        assertThat(upgrade("token=" + TOKEN + "&role=sim&room=&id=")).isEqualTo(101);
    }
}
