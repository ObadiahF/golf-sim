package com.golfsim.server.game;

import java.util.List;
import java.util.Map;

/** Scoring rules shared by the scorecard and the stats. */
public final class Scoring {

    private Scoring() {
    }

    /**
     * Whether a player's round counts for winners and stats: the game is FINISHED and the card is complete
     * (a score on every hole). Partial cards from a game ended early never count.
     */
    public static boolean counts(GameStatus status, long holesPlayed, int holesCount) {
        return status == GameStatus.FINISHED && holesPlayed == holesCount;
    }

    /**
     * Names with the lowest total (ties share the win); empty when there are no totals. Callers pass complete
     * cards only, so a player who quit early can never win on a smaller total.
     */
    public static List<String> winners(Map<String, Integer> totalsByName) {
        int best = totalsByName.values().stream().mapToInt(Integer::intValue).min().orElse(Integer.MAX_VALUE);
        return totalsByName.entrySet().stream()
                .filter(e -> e.getValue() == best)
                .map(Map.Entry::getKey)
                .toList();
    }
}
