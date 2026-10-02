package com.golfsim.server.stats;

import com.golfsim.server.game.GameStatus;
import com.golfsim.server.game.Scoring;
import java.time.Instant;
import java.util.List;
import java.util.Set;

/** Response bodies for the players and leaderboard endpoints. */
public final class StatsViews {

    private StatsViews() {
    }

    /** One player's round; {@code total}/{@code toPar} cover the holes played so far. */
    public record Round(
            String player, long gameId, GameStatus status, String courseName, int holesCount, Instant playedAt,
            int holesPlayed, int total, int toPar, boolean won) {

        /** Round lengths that feed the scoring stats (bests, averages, handicap, best rounds). */
        static final Set<Integer> RATED_LENGTHS = Set.of(9, 18);

        /** A complete card in a FINISHED game ({@link Scoring#counts}); not serialised. */
        boolean counts() {
            return Scoring.counts(status, holesPlayed, holesCount);
        }

        /** A counted round of a {@link #RATED_LENGTHS rated length}, so it compares fairly with the others. */
        boolean rated() {
            return counts() && RATED_LENGTHS.contains(holesCount);
        }

        /** {@code toPar} scaled to 18 holes (a 9-hole round's is doubled); exact for the rated lengths. */
        int toParPer18() {
            return toPar * 18 / Math.max(1, holesCount);
        }
    }

    /**
     * {@code finishedRounds} and wins count complete cards in FINISHED games of any length; bests, averages and the
     * handicap count only such rounds of 9 or 18 holes ({@link Round#rated}), and the to-par averages are per 18
     * holes. Hole tallies count every hole played. {@code handicap} is null until 3 rated rounds;
     * {@code recentAverageToPar} covers the last 5. Averages and the handicap have one decimal, halves rounded away
     * from zero.
     */
    public record PlayerStats(
            String name, int gamesPlayed, int finishedRounds, int wins, Integer best9, Integer best18, Double avg9,
            Double avg18, Double averageToPar, Double recentAverageToPar, Double handicap, HoleTallies holes,
            Instant lastPlayedAt) {
    }

    /** How a player's holes went, across every game. */
    public record HoleTallies(long played, long holesInOne, long eagles, long birdies, long pars, long bogeys,
                              long doubleBogeysOrWorse) {
        static final HoleTallies NONE = new HoleTallies(0, 0, 0, 0, 0, 0, 0);
    }

    public record PlayerDetail(PlayerStats stats, List<Round> recentGames) {
    }

    public record WinCount(String player, int wins, int finishedRounds) {
    }

    public record AverageToPar(String player, int finishedRounds, double averageToPar) {
    }

    public record HandicapRank(String player, int finishedRounds, double handicap) {
    }

    public record BirdieCount(String player, long birdiesOrBetter, long holesPlayed) {
    }

    public record Leaderboard(List<Round> bestRounds, List<WinCount> mostWins, List<AverageToPar> bestAverageToPar,
                              List<HandicapRank> lowestHandicap, List<BirdieCount> mostBirdies) {
    }
}
