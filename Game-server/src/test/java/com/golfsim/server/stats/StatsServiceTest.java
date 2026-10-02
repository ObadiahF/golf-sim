package com.golfsim.server.stats;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.golfsim.server.IntegrationTest;
import com.golfsim.server.game.GameRequests;
import com.golfsim.server.game.GameService;
import com.golfsim.server.game.GameStatus;
import com.golfsim.server.stats.StatsViews.PlayerStats;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;

class StatsServiceTest extends IntegrationTest {

    @Autowired
    private GameService games;

    @Autowired
    private StatsService stats;

    /** Plays a full round where each player shoots the given strokes on every hole (par 4). */
    private long playRound(int holes, List<String> names, List<Integer> strokesPerHole) {
        long id = games.start(new GameRequests.StartGame(names, holes, null)).id();
        for (int hole = 1; hole <= holes; hole++) {
            for (int i = 0; i < names.size(); i++) {
                games.recordScore(id, new GameRequests.Score(names.get(i), hole, 4, strokesPerHole.get(i)));
            }
        }
        return id;
    }

    /** Plays a solo round of par 4s, all pars except the first hole, which sets the score to par. */
    private void playToPar(int holes, String name, int toPar) {
        long id = games.start(new GameRequests.StartGame(List.of(name), holes, null)).id();
        for (int hole = 1; hole <= holes; hole++) {
            games.recordScore(id, new GameRequests.Score(name, hole, 4, hole == 1 ? 4 + toPar : 4));
        }
    }

    @Test
    void playerStatsCountFinishedRoundsAndWins() {
        playRound(9, List.of("Obi", "Sam"), List.of(4, 5)); // Obi 36 (E), Sam 45 (+9)
        playRound(9, List.of("Obi", "Sam"), List.of(5, 3)); // Obi 45 (+9), Sam 27 (-9)
        playRound(1, List.of("Obi", "Sam"), List.of(2, 4)); // Obi wins a 1-hole game: counts as a round, not a score
        long abandoned = games.start(new GameRequests.StartGame(List.of("Obi"), 2, null)).id();
        games.recordScore(abandoned, new GameRequests.Score("Obi", 1, 4, 9));
        games.end(abandoned, GameStatus.ABANDONED);

        List<PlayerStats> all = stats.players();
        assertThat(all).extracting(PlayerStats::name).containsExactly("Obi", "Sam");
        PlayerStats obi = all.getFirst();
        assertThat(obi.gamesPlayed()).isEqualTo(4);
        assertThat(obi.finishedRounds()).isEqualTo(3);
        assertThat(obi.wins()).isEqualTo(2);
        assertThat(obi.best9()).isEqualTo(36);
        assertThat(obi.avg9()).isEqualTo(40.5);
        assertThat(obi.best18()).isNull();
        assertThat(obi.avg18()).isNull();
        assertThat(obi.averageToPar()).isEqualTo(9.0); // E and +18 per 18 holes; the 1-hole -2 is left out

        StatsViews.PlayerDetail detail = stats.player("OBI");
        assertThat(detail.stats().name()).isEqualTo("Obi");
        assertThat(detail.recentGames()).hasSize(4);
        assertThat(detail.recentGames().getFirst().status()).isEqualTo(GameStatus.ABANDONED);
        assertThat(detail.recentGames().get(3).won()).isTrue();
    }

    @Test
    void leaderboardRanksRoundsWinsAndAverages() {
        playRound(9, List.of("Obi", "Sam"), List.of(4, 5));
        playRound(9, List.of("Obi", "Sam"), List.of(5, 3));
        playRound(9, List.of("Obi", "Sam"), List.of(3, 4));

        StatsViews.Leaderboard board = stats.leaderboard();
        assertThat(board.bestRounds().getFirst().player()).isEqualTo("Sam");
        assertThat(board.bestRounds().getFirst().toPar()).isEqualTo(-9);
        assertThat(board.mostWins()).extracting(StatsViews.WinCount::player).containsExactly("Obi", "Sam");
        assertThat(board.mostWins().getFirst().wins()).isEqualTo(2);
        assertThat(board.bestAverageToPar()).hasSize(2);
    }

    /** M-2: only 9- and 18-hole rounds feed scores, compared per 18 holes; shorter games still count as rounds. */
    @Test
    void onlyNineAndEighteenHoleRoundsFeedScores() {
        playRound(9, List.of("Nine"), List.of(3));     // 27, -9: -18 per 18
        playRound(18, List.of("Eighteen"), List.of(3)); // 54, -18
        playToPar(18, "Eighteen", 2);                    // 74, +2
        for (int i = 0; i < 3; i++) {
            playRound(1, List.of("Short"), List.of(1)); // aces in 1-hole games
        }

        StatsViews.Leaderboard board = stats.leaderboard();
        // equal per 18, so the lower total (the 9-hole 27) comes first; the 1-hole aces aren't best rounds
        assertThat(board.bestRounds()).extracting(StatsViews.Round::player).containsExactly("Nine", "Eighteen", "Eighteen");
        assertThat(board.bestAverageToPar()).extracting(StatsViews.AverageToPar::player).containsExactly("Nine", "Eighteen");
        assertThat(board.bestAverageToPar()).extracting(StatsViews.AverageToPar::averageToPar).containsExactly(-18.0, -8.0);
        assertThat(board.lowestHandicap()).isEmpty();
        assertThat(board.mostWins()).extracting(StatsViews.WinCount::player).contains("Short");

        PlayerStats eighteen = stats.player("Eighteen").stats();
        assertThat(eighteen.best18()).isEqualTo(54);
        assertThat(eighteen.avg18()).isEqualTo(64.0);
        assertThat(eighteen.best9()).isNull();
        assertThat(eighteen.recentAverageToPar()).isEqualTo(-8.0);

        PlayerStats shortGames = stats.player("Short").stats();
        assertThat(shortGames.finishedRounds()).isEqualTo(3);
        assertThat(shortGames.wins()).isEqualTo(3);
        assertThat(shortGames.best9()).isNull();
        assertThat(shortGames.best18()).isNull();
        assertThat(shortGames.averageToPar()).isNull();
        assertThat(shortGames.recentAverageToPar()).isNull();
        assertThat(shortGames.handicap()).isNull();
    }

    @Test
    void handicapAndHoleTalliesTrackHowEachPlayerScores() {
        playRound(9, List.of("Obi", "Sam"), List.of(3, 5)); // Obi birdies (-9), Sam bogeys (+9)
        playRound(9, List.of("Obi", "Sam"), List.of(4, 6)); // Obi pars (E), Sam doubles (+18)
        long id = playRound(9, List.of("Obi", "Sam"), List.of(4, 4));
        games.recordScore(id, new GameRequests.Score("Obi", 1, 3, 1)); // ace on a par 3 (corrects the score): -2

        PlayerStats obi = stats.player("Obi").stats();
        assertThat(obi.holes().played()).isEqualTo(27);
        assertThat(obi.holes().holesInOne()).isEqualTo(1);
        assertThat(obi.holes().birdies()).isEqualTo(9);
        assertThat(obi.holes().pars()).isEqualTo(17);
        assertThat(obi.handicap()).isEqualTo(-20.0); // best per-18 differential (-18) minus 2.0 for 3 rounds
        assertThat(obi.recentAverageToPar()).isNotNull();

        StatsViews.Leaderboard board = stats.leaderboard();
        assertThat(board.lowestHandicap().getFirst().player()).isEqualTo("Obi");
        assertThat(board.mostBirdies().getFirst().player()).isEqualTo("Obi");
        assertThat(stats.player("Sam").stats().holes().doubleBogeysOrWorse()).isEqualTo(9);
    }

    /** GS-4: a game ended early as FINISHED only crowns and counts complete cards. */
    @Test
    void partialCardsNeverWinOrFeedStats() {
        long id = games.start(new GameRequests.StartGame(List.of("Pro", "Quitter"), 9, null)).id();
        for (int hole = 1; hole <= 9; hole++) {
            games.recordScore(id, new GameRequests.Score("Pro", hole, 4, 3));
        }
        games.recordScore(id, new GameRequests.Score("Quitter", 1, 4, 8));
        assertThat(games.end(id, GameStatus.FINISHED).winners()).containsExactly("Pro");

        PlayerStats quitter = stats.player("quitter").stats();
        assertThat(quitter.gamesPlayed()).isEqualTo(1);
        assertThat(quitter.finishedRounds()).isZero();
        assertThat(quitter.wins()).isZero();
        assertThat(quitter.best9()).isNull();
        assertThat(quitter.avg9()).isNull();
        assertThat(quitter.holes().played()).isEqualTo(1); // hole tallies still count every hole played
        PlayerStats pro = stats.player("Pro").stats();
        assertThat(pro.wins()).isEqualTo(1);
        assertThat(pro.best9()).isEqualTo(27);

        StatsViews.Leaderboard board = stats.leaderboard();
        assertThat(board.bestRounds()).extracting(StatsViews.Round::player).containsExactly("Pro");
        assertThat(board.bestAverageToPar()).extracting(StatsViews.AverageToPar::player).containsExactly("Pro");
        assertThat(stats.player("Quitter").recentGames().getFirst().won()).isFalse();
    }

    @Test
    void earlyFinishWithNoCompleteCardHasNoWinner() {
        long id = games.start(new GameRequests.StartGame(List.of("A", "B"), 2, null)).id();
        games.recordScore(id, new GameRequests.Score("A", 1, 4, 4));
        assertThat(games.end(id, GameStatus.FINISHED).winners()).isEmpty();
        assertThat(stats.leaderboard().mostWins()).isEmpty();
    }

    /** GS-13: a hole in one is only an ace, whatever the par; categories add up to holes played. */
    @Test
    void aceOnAnyParCountsOnlyAsAnAce() {
        long id = games.start(new GameRequests.StartGame(List.of("Ace"), 4, null)).id();
        for (int hole = 1; hole <= 4; hole++) {
            games.recordScore(id, new GameRequests.Score("Ace", hole, hole, 1)); // par 1, 2, 3, 4
        }
        StatsViews.HoleTallies holes = stats.player("Ace").stats().holes();
        assertThat(holes.holesInOne()).isEqualTo(4);
        assertThat(holes.eagles() + holes.birdies() + holes.pars() + holes.bogeys() + holes.doubleBogeysOrWorse()).isZero();
        assertThat(stats.leaderboard().mostBirdies().getFirst().birdiesOrBetter()).isEqualTo(4);
    }

    /** GS-13: averages round halves away from zero, the same for under and over par. */
    @Test
    void negativeAveragesRoundLikePositiveOnes() {
        for (int toPar : List.of(-3, -3, -3, -2)) { // -2.75
            playToPar(18, "Under", toPar);
        }
        for (int toPar : List.of(3, 3, 3, 2)) { // 2.75
            playToPar(18, "Over", toPar);
        }
        assertThat(stats.player("Under").stats().averageToPar()).isEqualTo(-2.8);
        assertThat(stats.player("Over").stats().averageToPar()).isEqualTo(2.8);
    }

    @Test
    void unknownPlayerIsNotFound() {
        assertThatThrownBy(() -> stats.player("ghost")).hasMessageContaining("not found");
    }
}
