package com.golfsim.server.game;

/** Events published by {@link GameService}; delivered to WebSocket clients after the transaction commits. */
public final class GameEvents {

    private GameEvents() {
    }

    public record Started(GameView game) {
    }

    /** A score was recorded; {@code justFinished} is true when it completed the game. */
    public record ScoreRecorded(GameView game, boolean justFinished) {
    }

    /** The game left IN_PROGRESS via /end or because a new game replaced it. */
    public record Ended(GameView game) {
    }
}
