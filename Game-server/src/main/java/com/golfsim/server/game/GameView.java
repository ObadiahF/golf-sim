package com.golfsim.server.game;

import java.time.Instant;
import java.util.ArrayList;
import java.util.Arrays;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * Full game state / scorecard, as returned by REST and sent over WebSocket.
 *
 * @param pars    par per hole (index 0 = hole 1), null until a score for that hole is recorded
 * @param players in turn order
 * @param winners lowest total(s) among complete cards (a score on every hole) once FINISHED, otherwise empty
 */
public record GameView(
        long id,
        GameStatus status,
        int holesCount,
        String courseName,
        Instant createdAt,
        Instant finishedAt,
        List<Integer> pars,
        List<PlayerCard> players,
        List<String> winners) {

    /**
     * One player's row on the scorecard.
     *
     * @param turnOrder 1-based
     * @param strokes   strokes per hole (index 0 = hole 1), null where not yet played
     * @param par       sum of par over the holes played
     * @param toPar     total - par (negative is under par)
     */
    public record PlayerCard(
            String name, int turnOrder, List<Integer> strokes, int holesPlayed, int total, int par, int toPar) {
    }

    public static GameView of(Game game, List<HoleScore> scores) {
        int holes = game.getHolesCount();
        Integer[] pars = new Integer[holes];
        List<PlayerCard> cards = new ArrayList<>();
        Map<String, Integer> totals = new LinkedHashMap<>();
        List<Player> players = game.getPlayers();
        for (int i = 0; i < players.size(); i++) {
            Player player = players.get(i);
            Integer[] strokes = new Integer[holes];
            int total = 0;
            int par = 0;
            int played = 0;
            for (HoleScore s : scores) {
                if (!s.getPlayer().getId().equals(player.getId()) || s.getHoleNumber() > holes) {
                    continue;
                }
                strokes[s.getHoleNumber() - 1] = s.getStrokes();
                pars[s.getHoleNumber() - 1] = s.getPar();
                total += s.getStrokes();
                par += s.getPar();
                played++;
            }
            cards.add(new PlayerCard(player.getName(), i + 1, Arrays.asList(strokes), played, total, par, total - par));
            if (played == holes) {
                totals.put(player.getName(), total);
            }
        }
        List<String> winners = game.getStatus() == GameStatus.FINISHED ? Scoring.winners(totals) : List.of();
        return new GameView(game.getId(), game.getStatus(), holes, game.getCourseName(), game.getCreatedAt(),
                game.getFinishedAt(), Arrays.asList(pars), cards, winners);
    }
}
