package com.golfsim.server.stats;

import com.golfsim.server.stats.StatsViews.Round;
import java.util.List;

/**
 * A World-Handicap-System style index without course ratings: each rated round's differential is its
 * score to par scaled to 18 holes, and the index is the average of the best few of the last 20 (the WHS
 * table below), so a few blow-up rounds don't count against you. Needs at least 3 rated rounds.
 */
final class Handicap {

    static final int WINDOW = 20;
    static final int MIN_ROUNDS = 3;

    private Handicap() {
    }

    /** @param ratedNewestFirst a player's {@link Round#rated rated} rounds, newest first */
    static Double index(List<Round> ratedNewestFirst) {
        List<Double> differentials = ratedNewestFirst.stream()
                .limit(WINDOW)
                .map(r -> (double) r.toParPer18())
                .sorted()
                .toList();
        int n = differentials.size();
        if (n < MIN_ROUNDS) {
            return null;
        }
        double best = differentials.stream().limit(countedRounds(n)).mapToDouble(Double::doubleValue).average().orElseThrow();
        double index = best + adjustment(n);
        return OneDecimal.of(index);
    }

    /** How many of the lowest differentials count (WHS rule 5.2). */
    static int countedRounds(int rounds) {
        if (rounds <= 5) return 1;
        if (rounds <= 8) return 2;
        if (rounds <= 11) return 3;
        if (rounds <= 14) return 4;
        if (rounds <= 16) return 5;
        if (rounds <= 18) return 6;
        if (rounds == 19) return 7;
        return 8;
    }

    /** WHS adjustment for short records (fewer rounds = more likely the best one was a fluke). */
    static double adjustment(int rounds) {
        return switch (rounds) {
            case 3 -> -2.0;
            case 4, 6 -> -1.0;
            default -> 0.0;
        };
    }
}
