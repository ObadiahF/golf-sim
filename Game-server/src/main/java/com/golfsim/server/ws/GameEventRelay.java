package com.golfsim.server.ws;

import com.golfsim.server.game.GameEvents;
import org.springframework.stereotype.Component;
import org.springframework.transaction.event.TransactionalEventListener;

/** Pushes committed game changes to every connected sim and remote. */
@Component
public class GameEventRelay {

    private final WsHub hub;

    public GameEventRelay(WsHub hub) {
        this.hub = hub;
    }

    @TransactionalEventListener
    public void onStarted(GameEvents.Started event) {
        hub.broadcast(new WsMessage.GameStarted(event.game()));
    }

    @TransactionalEventListener
    public void onScore(GameEvents.ScoreRecorded event) {
        hub.broadcast(new WsMessage.Scorecard(event.game()));
        if (event.justFinished()) {
            hub.broadcast(new WsMessage.GameFinished(event.game()));
        }
    }

    @TransactionalEventListener
    public void onEnded(GameEvents.Ended event) {
        hub.broadcast(new WsMessage.GameFinished(event.game()));
    }
}
