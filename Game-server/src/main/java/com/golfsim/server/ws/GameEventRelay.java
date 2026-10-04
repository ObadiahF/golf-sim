package com.golfsim.server.ws;

import com.golfsim.server.game.GameEvents;
import com.golfsim.server.physics.PhysicsService;
import org.springframework.stereotype.Component;
import org.springframework.transaction.event.TransactionalEventListener;

/** Pushes committed game changes to the sim and remotes in the game's room, and ball-physics changes to everyone. */
@Component
public class GameEventRelay {

    private final WsHub hub;

    public GameEventRelay(WsHub hub) {
        this.hub = hub;
    }

    @TransactionalEventListener
    public void onStarted(GameEvents.Started event) {
        hub.broadcast(event.game().room(), new WsMessage.GameStarted(event.game()));
    }

    @TransactionalEventListener
    public void onScore(GameEvents.ScoreRecorded event) {
        hub.broadcast(event.game().room(), new WsMessage.Scorecard(event.game()));
        if (event.justFinished()) {
            hub.broadcast(event.game().room(), new WsMessage.GameFinished(event.game()));
        }
    }

    @TransactionalEventListener
    public void onEnded(GameEvents.Ended event) {
        hub.broadcast(event.game().room(), new WsMessage.GameFinished(event.game()));
    }

    @TransactionalEventListener
    public void onPhysics(PhysicsService.Changed event) {
        hub.broadcastAll(new WsMessage.Physics(event.profile()));
    }
}
