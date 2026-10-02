package com.golfsim.server.ws;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.physics.PhysicsField;
import com.golfsim.server.physics.PhysicsProfile;
import com.golfsim.server.physics.PhysicsService;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;

/** The live physics profile reaches sims: in {@code hello} when they connect, and as {@code physics} on a change. */
class PhysicsSocketTest extends SocketTestBase {

    @Autowired
    private PhysicsService physics;

    @Test
    void helloCarriesTheCurrentProfile() throws Exception {
        try (WsTestClient sim = sim()) {
            JsonNode empty = sim.await("hello").get("physics");
            assertThat(empty.get("surfaces")).hasSize(8);
            assertThat(empty.get("surfaces").get(0).has("rolling")).isFalse();
        }
        physics.update(List.of(new PhysicsProfile.Change("green", PhysicsField.ROLLING, 0.07)));
        try (WsTestClient lateSim = sim()) {
            JsonNode green = lateSim.await("hello").get("physics").get("surfaces").get(0);
            assertThat(green.get("surface").asText()).isEqualTo("green");
            assertThat(green.get("rolling").asDouble()).isEqualTo(0.07);
            assertThat(green.has("friction")).isFalse(); // absent, never null: Unity would read null as 0
        }
    }

    @Test
    void changesAreBroadcastToSimsAndRemotes() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            sim.await("hello");
            remote.await("hello");
            physics.update(List.of(new PhysicsProfile.Change("rough", PhysicsField.FRICTION, 0.6),
                    new PhysicsProfile.Change("tee", PhysicsField.RESTITUTION, 0.8)));
            JsonNode message = sim.await("physics");
            assertThat(message.get("profile").get("surfaces").get(3).get("friction").asDouble()).isEqualTo(0.6);
            assertThat(message.get("profile").get("surfaces").get(2).get("restitution").asDouble()).isEqualTo(0.8);
            assertThat(remote.await("physics").get("profile").get("surfaces")).hasSize(8);

            physics.reset();
            JsonNode reset = sim.await("physics").get("profile").get("surfaces").get(3);
            assertThat(reset.get("surface").asText()).isEqualTo("rough");
            assertThat(reset.has("friction")).isFalse();
        }
    }

    @Test
    void onlyTheServerSendsPhysics() throws Exception {
        try (WsTestClient sim = sim(); WsTestClient remote = remote()) {
            sim.await("hello");
            remote.await("hello");
            remote.send("{\"type\":\"physics\",\"profile\":{\"surfaces\":[]}}");
            assertThat(remote.await("error").get("message").asText()).isEqualTo("'physics' is sent by the server only");
            sim.assertNo("physics");
        }
    }
}
