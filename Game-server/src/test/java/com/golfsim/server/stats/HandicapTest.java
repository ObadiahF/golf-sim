package com.golfsim.server.stats;

import static org.assertj.core.api.Assertions.assertThat;

import com.golfsim.server.game.GameStatus;
import com.golfsim.server.stats.StatsViews.Round;
import java.time.Instant;
import java.util.List;
import java.util.stream.IntStream;
import org.junit.jupiter.api.Test;

class HandicapTest {

    private static Round round(int toPar, int holes) {
        return new Round("Obi", 1, GameStatus.FINISHED, null, holes, Instant.EPOCH, holes, 4 * holes + toPar, toPar, false);
    }

    @Test
    void needsThreeRounds() {
        assertThat(Handicap.index(List.of(round(5, 18), round(7, 18)))).isNull();
    }

    @Test
    void shortRecordUsesBestRoundMinusAdjustment() {
        // 3 rounds: lowest differential (5) minus 2.0
        assertThat(Handicap.index(List.of(round(9, 18), round(5, 18), round(12, 18)))).isEqualTo(3.0);
    }

    @Test
    void nineHoleRoundsAreScaledTo18() {
        // +3 over 9 holes is +6 over 18; 3 rounds: 6 - 2
        assertThat(Handicap.index(List.of(round(3, 9), round(4, 9), round(5, 9)))).isEqualTo(4.0);
    }

    @Test
    void fullRecordAveragesBestEightOfLastTwenty() {
        // newest first: twenty rounds 1..20 over par, then an old -10 that falls outside the window
        List<Round> rounds = new java.util.ArrayList<>(IntStream.rangeClosed(1, 20).mapToObj(i -> round(i, 18)).toList());
        rounds.add(round(-10, 18));
        assertThat(Handicap.index(rounds)).isEqualTo(4.5); // mean of 1..8
    }

    /** GS-13: halves round away from zero, so -2.75 is -2.8 (Math.round gave -2.7). */
    @Test
    void negativeIndexRoundsHalfAwayFromZero() {
        List<Round> rounds = new java.util.ArrayList<>(List.of(-2, -3, -3, -3, -3, -3, -3, -2).stream()
                .map(toPar -> round(toPar, 18)).toList());
        IntStream.range(0, 12).forEach(i -> rounds.add(round(10, 18)));
        assertThat(Handicap.index(rounds)).isEqualTo(-2.8);
        assertThat(OneDecimal.of(-2.75)).isEqualTo(-2.8);
        assertThat(OneDecimal.of(2.75)).isEqualTo(2.8);
        assertThat(OneDecimal.ratio(-11, 4)).isEqualTo(-2.8);
        assertThat(OneDecimal.ratio(11, 4)).isEqualTo(2.8);
        assertThat(OneDecimal.ratio(-1, 3)).isEqualTo(-0.3);
    }
}
