package com.golfsim.server.stats;

import com.golfsim.server.game.GameStatus;
import com.golfsim.server.game.Scoring;
import java.time.Instant;

/** One player's totals in one game, loaded by {@link RoundRepository} in a single query. */
public record PlayerRound(
        String player,
        long gameId,
        GameStatus status,
        String courseName,
        int holesCount,
        Instant playedAt,
        long holesPlayed,
        long total,
        long par) {

    /** See {@link Scoring#counts}. */
    public boolean counts() {
        return Scoring.counts(status, holesPlayed, holesCount);
    }

    public int toPar() {
        return (int) (total - par);
    }
}
