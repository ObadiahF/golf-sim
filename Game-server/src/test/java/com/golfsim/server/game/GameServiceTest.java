package com.golfsim.server.game;

import static org.assertj.core.api.Assertions.assertThat;
import static org.assertj.core.api.Assertions.assertThatThrownBy;

import com.golfsim.server.IntegrationTest;
import java.util.List;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.web.server.ResponseStatusException;

class GameServiceTest extends IntegrationTest {

    @Autowired
    private GameService games;

    @Autowired
    private PlayerRepository players;

    static GameRequests.StartGame start(int holes, String... names) {
        return new GameRequests.StartGame(List.of(names), holes, "Test Links");
    }

    static GameRequests.Score score(String player, int hole, int par, int strokes) {
        return new GameRequests.Score(player, hole, par, strokes);
    }

    @Test
    void startCreatesGameWithPlayersInTurnOrder() {
        GameView game = games.start(new GameRequests.StartGame(List.of(" Obi ", "Sam"), null, null));

        assertThat(game.status()).isEqualTo(GameStatus.IN_PROGRESS);
        assertThat(game.holesCount()).isEqualTo(9);
        assertThat(game.players()).extracting(GameView.PlayerCard::name).containsExactly("Obi", "Sam");
        assertThat(game.players()).extracting(GameView.PlayerCard::turnOrder).containsExactly(1, 2);
        assertThat(game.pars()).hasSize(9).containsOnlyNulls();
        assertThat(games.current()).map(GameView::id).contains(game.id());
    }

    @Test
    void playersAreMatchedByNameIgnoringCase() {
        games.start(start(1, "Obi"));
        GameView second = games.start(start(1, "OBI", "sam"));

        assertThat(players.count()).isEqualTo(2);
        assertThat(second.players()).extracting(GameView.PlayerCard::name).containsExactly("Obi", "sam");
    }

    @Test
    void startingANewGameAbandonsTheOldOne() {
        GameView first = games.start(start(9, "Obi"));
        GameView second = games.start(start(9, "Sam"));

        assertThat(games.get(first.id()).status()).isEqualTo(GameStatus.ABANDONED);
        assertThat(games.get(first.id()).finishedAt()).isNotNull();
        assertThat(games.current()).map(GameView::id).contains(second.id());
    }

    @Test
    void eachRoomHasItsOwnGameInProgress() {
        GameView legacy = games.start(start(9, "Obi"));
        GameView a = games.start(start(9, "Obi"), "AAAA");
        GameView b = games.start(start(9, "Sam"), "BBBB");

        assertThat(a.room()).isEqualTo("AAAA");
        assertThat(games.current()).map(GameView::id).contains(legacy.id());
        assertThat(games.current("AAAA")).map(GameView::id).contains(a.id());
        assertThat(games.current("CCCC")).isEmpty();

        GameView a2 = games.start(start(9, "Obi"), "AAAA");
        assertThat(games.get(a.id()).status()).isEqualTo(GameStatus.ABANDONED);
        assertThat(games.current("AAAA")).map(GameView::id).contains(a2.id());
        assertThat(games.get(legacy.id()).status()).isEqualTo(GameStatus.IN_PROGRESS);
        assertThat(games.get(b.id()).status()).isEqualTo(GameStatus.IN_PROGRESS);
        assertThat(jdbc.queryForObject("select count(*) from games where status = 'IN_PROGRESS'", Long.class)).isEqualTo(3);
    }

    @Test
    void duplicateNamesAreRejected() {
        assertThatThrownBy(() -> games.start(start(9, "Obi", "obi")))
                .isInstanceOf(ResponseStatusException.class)
                .hasMessageContaining("Duplicate player name");
    }

    @Test
    void scoresAreUpsertedAndTotalled() {
        GameView game = games.start(start(2, "Obi", "Sam"));

        games.recordScore(game.id(), score("obi", 1, 4, 6));
        GameView view = games.recordScore(game.id(), score("Obi", 1, 4, 5)); // correction replaces

        GameView.PlayerCard obi = view.players().getFirst();
        assertThat(obi.strokes()).containsExactly(5, null);
        assertThat(obi.total()).isEqualTo(5);
        assertThat(obi.par()).isEqualTo(4);
        assertThat(obi.toPar()).isEqualTo(1);
        assertThat(obi.holesPlayed()).isEqualTo(1);
        assertThat(view.pars()).containsExactly(4, null);
        assertThat(view.status()).isEqualTo(GameStatus.IN_PROGRESS);
    }

    @Test
    void gameFinishesWhenEveryPlayerHasEveryHole() {
        GameView game = games.start(start(2, "Obi", "Sam"));
        games.recordScore(game.id(), score("Obi", 1, 4, 4));
        games.recordScore(game.id(), score("Sam", 1, 4, 5));
        games.recordScore(game.id(), score("Obi", 2, 3, 3));
        GameView done = games.recordScore(game.id(), score("Sam", 2, 3, 2));

        assertThat(done.status()).isEqualTo(GameStatus.FINISHED);
        assertThat(done.finishedAt()).isNotNull();
        assertThat(done.winners()).containsExactly("Obi", "Sam"); // 7 each: tie shares the win
        assertThat(games.current()).isEmpty();

        GameView corrected = games.recordScore(game.id(), score("Sam", 2, 3, 3)); // still allowed when finished
        assertThat(corrected.winners()).containsExactly("Obi");
    }

    @Test
    void invalidScoresAreRejected() {
        GameView game = games.start(start(2, "Obi"));

        assertThatThrownBy(() -> games.recordScore(game.id(), score("Nobody", 1, 4, 4)))
                .hasMessageContaining("is not in game");
        assertThatThrownBy(() -> games.recordScore(game.id(), score("Obi", 3, 4, 4)))
                .hasMessageContaining("hole must be between 1 and 2");
        assertThatThrownBy(() -> games.recordScore(999, score("Obi", 1, 4, 4)))
                .hasMessageContaining("not found");

        games.end(game.id(), GameStatus.ABANDONED);
        assertThatThrownBy(() -> games.recordScore(game.id(), score("Obi", 1, 4, 4)))
                .hasMessageContaining("abandoned");
        assertThatThrownBy(() -> games.end(game.id(), GameStatus.FINISHED))
                .hasMessageContaining("already ABANDONED");
    }

    @Test
    void recentListsNewestFirstWithScores() {
        GameView first = games.start(start(1, "Obi"));
        games.recordScore(first.id(), score("Obi", 1, 3, 2));
        GameView second = games.start(start(1, "Sam"));

        List<GameView> recent = games.recent(10);
        assertThat(recent).extracting(GameView::id).containsExactly(second.id(), first.id());
        assertThat(recent.get(1).players().getFirst().total()).isEqualTo(2);
    }
}
