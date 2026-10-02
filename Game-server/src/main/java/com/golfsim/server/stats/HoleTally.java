package com.golfsim.server.stats;

/** One player's hole-by-hole results across every game, loaded by {@link RoundRepository}. */
public record HoleTally(String player, long played, long holesInOne, long eagles, long birdies, long pars, long bogeys,
                        long doubleBogeysOrWorse) {

    StatsViews.HoleTallies view() {
        return new StatsViews.HoleTallies(played, holesInOne, eagles, birdies, pars, bogeys, doubleBogeysOrWorse);
    }
}
