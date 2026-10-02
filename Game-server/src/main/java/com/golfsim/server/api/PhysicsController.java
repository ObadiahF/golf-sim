package com.golfsim.server.api;

import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.physics.PhysicsProfile;
import com.golfsim.server.physics.PhysicsService;
import com.golfsim.server.physics.PhysicsUpdate;
import org.springframework.web.bind.annotation.DeleteMapping;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** The live ball-physics profile; every change is pushed to the sims as a WebSocket {@code physics} message. */
@RestController
@RequestMapping("/api/physics")
public class PhysicsController {

    private final PhysicsService physics;

    public PhysicsController(PhysicsService physics) {
        this.physics = physics;
    }

    @GetMapping
    public PhysicsProfile get() {
        return physics.current();
    }

    /** Partial update: listed fields are set (number) or cleared (null); everything else stays as it is. */
    @PutMapping
    public PhysicsProfile update(@RequestBody JsonNode body) {
        return physics.update(PhysicsUpdate.parse(body));
    }

    /** Back to the game's built-in values. */
    @DeleteMapping
    public PhysicsProfile reset() {
        return physics.reset();
    }
}
