package com.golfsim.server.game;

import static org.assertj.core.api.Assertions.assertThat;

import com.golfsim.server.IntegrationTest;
import java.util.ArrayList;
import java.util.List;
import java.util.concurrent.Callable;
import java.util.concurrent.CountDownLatch;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.function.IntFunction;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;

/** GS-2 / GS-3: concurrent scores and starts finish games exactly once and never fail. */
class ConcurrencyTest extends IntegrationTest {

    private static final int TRIALS = 10;

    @Autowired
    private GameService games;

    /** Runs {@code n} tasks released at the same instant; rethrows the first failure. */
    private static <T> List<T> together(int n, IntFunction<T> task) throws Exception {
        ExecutorService pool = Executors.newFixedThreadPool(n);
        try {
            CountDownLatch gate = new CountDownLatch(1);
            List<Future<T>> futures = new ArrayList<>();
            for (int i = 0; i < n; i++) {
                int index = i;
                futures.add(pool.submit((Callable<T>) () -> {
                    gate.await();
                    return task.apply(index);
                }));
            }
            gate.countDown();
            List<T> results = new ArrayList<>();
            for (Future<T> f : futures) {
                results.add(f.get());
            }
            return results;
        } finally {
            pool.shutdownNow();
        }
    }

    @Test
    void simultaneousFinalScoresFinishTheGameExactlyOnce() throws Exception {
        List<String> names = List.of("W1", "W2", "W3", "W4");
        for (int trial = 0; trial < TRIALS; trial++) {
            long id = games.start(new GameRequests.StartGame(names, 1, null)).id();
            List<GameView> views = together(4, i -> games.recordScore(id, new GameRequests.Score(names.get(i), 1, 4, 3 + i)));

            GameView done = games.get(id);
            assertThat(done.status()).as("trial " + trial).isEqualTo(GameStatus.FINISHED);
            assertThat(done.winners()).containsExactly("W1");
            assertThat(done.players()).allMatch(p -> p.holesPlayed() == 1);
            // Serialised: exactly one call saw the card complete (the others saw it still in progress).
            assertThat(views).filteredOn(v -> v.status() == GameStatus.FINISHED).hasSize(1);
        }
    }

    @Test
    void simultaneousUpsertsOfOneHoleAllSucceedAndLeaveOneRow() throws Exception {
        for (int trial = 0; trial < TRIALS; trial++) {
            long id = games.start(new GameRequests.StartGame(List.of("D1", "D2"), 2, null)).id();
            together(6, i -> games.recordScore(id, new GameRequests.Score("D1", 1, 4, 3 + i % 3)));

            assertThat(jdbc.queryForObject("select count(*) from hole_scores where game_id = ?", Long.class, id)).isOne();
            assertThat(games.get(id).players().getFirst().strokes().getFirst()).isBetween(3, 5);
        }
    }

    @Test
    void simultaneousStartsAllSucceedAndLeaveOneGameInProgress() throws Exception {
        List<GameView> started = together(6, i -> games.start(new GameRequests.StartGame(List.of("S" + i, "Shared"), 1, null)));

        assertThat(started).allMatch(g -> g.status() == GameStatus.IN_PROGRESS);
        assertThat(jdbc.queryForObject("select count(*) from games where status = 'IN_PROGRESS'", Long.class)).isOne();
        assertThat(jdbc.queryForObject("select count(*) from games where status = 'ABANDONED'", Long.class)).isEqualTo(5);
        assertThat(jdbc.queryForObject("select count(*) from players where name = 'Shared'", Long.class)).isOne();
    }

    @Test
    void endRacingTheLastScoreLeavesOneConsistentOutcome() throws Exception {
        for (int trial = 0; trial < TRIALS; trial++) {
            long id = games.start(new GameRequests.StartGame(List.of("E1"), 1, null)).id();
            List<String> outcomes = together(2, i -> {
                try {
                    return i == 0
                            ? games.recordScore(id, new GameRequests.Score("E1", 1, 4, 4)).status().name()
                            : games.end(id, GameStatus.ABANDONED).status().name();
                } catch (org.springframework.web.server.ResponseStatusException e) {
                    return String.valueOf(e.getStatusCode().value());
                }
            });
            GameStatus status = games.get(id).status();
            // Either the score finished it first (end gets 409) or end abandoned it first (score gets 409).
            assertThat(outcomes).containsAnyOf("409").doesNotContain("500");
            assertThat(status).isIn(GameStatus.FINISHED, GameStatus.ABANDONED);
        }
    }
}
