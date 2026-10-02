package com.golfsim.server.ws;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.game.GameRequests;
import com.golfsim.server.game.GameView;
import java.util.List;
import org.junit.jupiter.api.Test;

class GameSocketTest extends SocketTestBase {

    @Test
    void helloAndSimStatus() throws Exception {
        try (WsTestClient remote = remote()) {
            JsonNode hello = remote.await("hello");
            assertThat(hello.get("role").asText()).isEqualTo("remote");
            assertThat(hello.get("simConnected").asBoolean()).isFalse();
            assertThat(hello.get("remotes")).extracting(JsonNode::asText).containsExactly("Obi's iPhone");
            assertThat(hello.get("game").isNull()).isTrue();

            WsTestClient sim = sim();
            assertThat(sim.await("hello").get("simConnected").asBoolean()).isTrue();
            assertThat(remote.await("simStatus").get("connected").asBoolean()).isTrue();
            WsTestClient secondSim = sim();
            secondSim.await("hello");
            remote.assertNo("simStatus"); // only changes are reported
            secondSim.close();
            remote.assertNo("simStatus");
            sim.close();
            assertThat(remote.await("simStatus").get("connected").asBoolean()).isFalse();
        }
    }

    @Test
    void remoteCommandsAreRelayedToTheSim() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            remote.send("{\"type\":\"nav\",\"key\":\"up\"}");
            assertThat(sim.await("nav").get("key").asText()).isEqualTo("up");

            remote.send("{\"type\":\"shot\",\"speed\":65.2,\"launch\":12.5,\"azimuth\":-1.5,\"back\":2600,\"side\":-300,\"club\":\"7I\",\"id\":7}");
            JsonNode shot = sim.await("shot");
            assertThat(shot.get("speed").asDouble()).isEqualTo(65.2);
            assertThat(shot.get("id").asInt()).isEqualTo(7);

            remote.send("{\"type\":\"nav\",\"key\":\"sideways\"}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("Invalid value for 'key'");
            remote.send("{\"type\":\"club\"}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("club: club must not be blank");
            remote.send("{\"type\":\"teleport\"}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("Unknown message type 'teleport'");
            remote.send("not json");
            remote.await("error");
            remote.send("{\"type\":\"state\",\"screen\":\"menu\"}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("'state' must be sent by a sim");
            sim.assertNo("club");

            remote.send("{\"type\":\"ping\"}");
            remote.await("pong");
        }
    }

    @Test
    void remoteCommandWithoutSimIsAnError() throws Exception {
        try (WsTestClient remote = remote()) {
            remote.send("{\"type\":\"aimReset\"}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("no sim connected");
        }
    }

    @Test
    void simUpdatesReachRemotesAndNewRemotesGetTheLastState() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            sim.send("{\"type\":\"state\",\"screen\":\"game\",\"currentPlayer\":\"Obi\",\"hole\":1,\"par\":4}");
            assertThat(remote.await("state").get("currentPlayer").asText()).isEqualTo("Obi");
            sim.send("{\"type\":\"shotResult\",\"player\":\"Obi\",\"carry\":231.4,\"total\":248.0,\"lie\":\"fairway\",\"holed\":false,\"strokes\":1}");
            assertThat(remote.await("shotResult").get("lie").asText()).isEqualTo("fairway");

            try (WsTestClient late = remote()) {
                assertThat(late.await("hello").get("state").get("screen").asText()).isEqualTo("game");
            }
        }
    }

    /** R-6: the sim tells the phone why a swing wasn't hit; extra state fields pass through unchanged. */
    @Test
    void shotRejectedAndWaitStateAreRelayedToRemotes() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            remote.await("hello");
            sim.send("{\"type\":\"state\",\"screen\":\"game\",\"canShoot\":false,\"waitReason\":\"Wait for the next turn\"}");
            JsonNode state = remote.await("state");
            assertThat(state.get("canShoot").asBoolean()).isFalse();
            assertThat(state.get("waitReason").asText()).isEqualTo("Wait for the next turn");

            sim.send("{\"type\":\"shotRejected\",\"reason\":\"Wait for the next turn\",\"id\":7}");
            JsonNode rejected = remote.await("shotRejected");
            assertThat(rejected.get("reason").asText()).isEqualTo("Wait for the next turn");
            assertThat(rejected.get("id").asLong()).isEqualTo(7);
            sim.send("{\"type\":\"shotRejected\",\"reason\":\"Instant replay\"}");
            assertThat(remote.await("shotRejected").has("id")).isFalse();

            sim.send("{\"type\":\"shotRejected\",\"reason\":\" \",\"id\":8}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("shotRejected: reason must not be blank");
            sim.send("{\"type\":\"shotRejected\",\"reason\":\"Wait\",\"id\":\"seven\"}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("Invalid value for 'id'");
            remote.assertNo("shotRejected");

            remote.send("{\"type\":\"shotRejected\",\"reason\":\"Wait\"}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("'shotRejected' must be sent by a sim");
        }
    }

    @Test
    void startingAGameNotifiesTheSim() throws Exception {
        try (WsTestClient sim = sim()) {
            sim.await("hello");
            GameView game = games.start(new GameRequests.StartGame(List.of("Obi", "Sam"), 9, "Pebble"));
            JsonNode started = sim.await("gameStarted").get("game");
            assertThat(started.get("id").asLong()).isEqualTo(game.id());
            assertThat(started.get("players").get(1).get("name").asText()).isEqualTo("Sam");
            assertThat(started.get("courseName").asText()).isEqualTo("Pebble");
        }
    }

    @Test
    void holeScoresArePersistedAndBroadcast() throws Exception {
        GameView game = games.start(new GameRequests.StartGame(List.of("Obi"), 2, null));
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            assertThat(remote.await("hello").get("game").get("id").asLong()).isEqualTo(game.id());

            sim.send("{\"type\":\"holeScore\",\"gameId\":" + game.id() + ",\"player\":\"Obi\",\"hole\":1,\"par\":4,\"strokes\":5}");
            JsonNode card = remote.await("scorecard").get("game");
            assertThat(card.get("players").get(0).get("strokes").get(0).asInt()).isEqualTo(5);
            sim.await("scorecard");
            assertThat(games.get(game.id()).players().getFirst().total()).isEqualTo(5);

            sim.send("{\"type\":\"holeScore\",\"gameId\":" + game.id() + ",\"player\":\"Obi\",\"hole\":2,\"par\":3,\"strokes\":3}");
            remote.await("scorecard");
            JsonNode finished = remote.await("gameFinished").get("game");
            assertThat(finished.get("status").asText()).isEqualTo("FINISHED");
            assertThat(finished.get("players").get(0).get("total").asInt()).isEqualTo(8);

            sim.send("{\"type\":\"holeScore\",\"gameId\":" + game.id() + ",\"player\":\"Obi\",\"hole\":0,\"par\":3,\"strokes\":3}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("holeScore: hole must be greater than or equal to 1");
            sim.send("{\"type\":\"holeScore\",\"gameId\":424242,\"player\":\"Obi\",\"hole\":1,\"par\":3,\"strokes\":3}");
            assertThat(sim.await("error").get("message").asText()).isEqualTo("Game 424242 not found");
        }
    }

    @Test
    void handshakeRequiresTokenAndRole() {
        assertThatThrownBy(() -> WsTestClient.connect(url("role=sim"))).hasMessageContaining("401");
        assertThatThrownBy(() -> WsTestClient.connect(url("token=nope&role=sim"))).hasMessageContaining("401");
        assertThatThrownBy(() -> WsTestClient.connect(url("token=" + TOKEN + "&role=tv"))).hasMessageContaining("400");
    }
}
