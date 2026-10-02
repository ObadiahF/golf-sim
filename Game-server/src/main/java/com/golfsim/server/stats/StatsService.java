package com.golfsim.server.stats;

import com.golfsim.server.game.PlayerRepository;
import com.golfsim.server.game.Scoring;
import com.golfsim.server.stats.StatsViews.AverageToPar;
import com.golfsim.server.stats.StatsViews.BirdieCount;
import com.golfsim.server.stats.StatsViews.HandicapRank;
import com.golfsim.server.stats.StatsViews.HoleTallies;
import com.golfsim.server.stats.StatsViews.Leaderboard;
import com.golfsim.server.stats.StatsViews.PlayerDetail;
import com.golfsim.server.stats.StatsViews.PlayerStats;
import com.golfsim.server.stats.StatsViews.Round;
import com.golfsim.server.stats.StatsViews.WinCount;
import java.util.Comparator;
import java.util.HashSet;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Set;
import java.util.function.ToIntFunction;
import java.util.stream.Collectors;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.server.ResponseStatusException;

/** Player history and leaderboards, computed from {@link PlayerRound}s (small data: one query, then Java). */
@Service
@Transactional(readOnly = true)
public class StatsService {

    static final int RECENT_GAMES = 20;
    static final int LEADERBOARD_SIZE = 10;
    static final int RECENT_FORM = 5;

    private final RoundRepository rounds;
    private final PlayerRepository players;

    public StatsService(RoundRepository rounds, PlayerRepository players) {
        this.rounds = rounds;
        this.players = players;
    }

    public List<PlayerStats> players() {
        Map<String, HoleTallies> tallies = loadTallies();
        return byPlayer(loadRounds()).entrySet().stream()
                .map(e -> stats(e.getKey(), e.getValue(), tallies))
                .sorted(Comparator.comparing(s -> s.name().toLowerCase()))
                .toList();
    }

    public PlayerDetail player(String name) {
        String canonical = players.findByName(name)
                .orElseThrow(() -> new ResponseStatusException(HttpStatus.NOT_FOUND, "Player '" + name + "' not found"))
                .getName();
        List<Round> mine = loadRounds().stream().filter(r -> r.player().equals(canonical)).toList();
        return new PlayerDetail(stats(canonical, mine, loadTallies()), mine.stream().limit(RECENT_GAMES).toList());
    }

    public Leaderboard leaderboard() {
        List<Round> all = loadRounds();
        List<Round> bestRounds = all.stream()
                .filter(Round::rated)
                .sorted(Comparator.comparingInt(Round::toParPer18).thenComparingInt(Round::total).thenComparing(Round::playedAt))
                .limit(LEADERBOARD_SIZE)
                .toList();
        Map<String, HoleTallies> tallies = loadTallies();
        List<PlayerStats> stats = byPlayer(all).entrySet().stream().map(e -> stats(e.getKey(), e.getValue(), tallies))
                .filter(s -> s.finishedRounds() > 0).toList();
        List<WinCount> mostWins = stats.stream()
                .filter(s -> s.wins() > 0)
                .sorted(Comparator.comparingInt(PlayerStats::wins).reversed().thenComparing(PlayerStats::name))
                .limit(LEADERBOARD_SIZE)
                .map(s -> new WinCount(s.name(), s.wins(), s.finishedRounds()))
                .toList();
        List<AverageToPar> bestAverage = stats.stream()
                .filter(s -> s.averageToPar() != null)
                .sorted(Comparator.comparingDouble(PlayerStats::averageToPar).thenComparing(PlayerStats::name))
                .limit(LEADERBOARD_SIZE)
                .map(s -> new AverageToPar(s.name(), s.finishedRounds(), s.averageToPar()))
                .toList();
        List<HandicapRank> lowestHandicap = stats.stream()
                .filter(s -> s.handicap() != null)
                .sorted(Comparator.comparingDouble(PlayerStats::handicap).thenComparing(PlayerStats::name))
                .limit(LEADERBOARD_SIZE)
                .map(s -> new HandicapRank(s.name(), s.finishedRounds(), s.handicap()))
                .toList();
        List<BirdieCount> mostBirdies = tallies.entrySet().stream()
                .map(e -> new BirdieCount(e.getKey(), e.getValue().holesInOne() + e.getValue().eagles() + e.getValue().birdies(),
                        e.getValue().played()))
                .filter(b -> b.birdiesOrBetter() > 0)
                .sorted(Comparator.comparingLong(BirdieCount::birdiesOrBetter).reversed().thenComparing(BirdieCount::player))
                .limit(LEADERBOARD_SIZE)
                .toList();
        return new Leaderboard(bestRounds, mostWins, bestAverage, lowestHandicap, mostBirdies);
    }

    /** All rounds, newest first, with the {@code won} flag worked out per finished game among complete cards. */
    private List<Round> loadRounds() {
        List<PlayerRound> raw = rounds.findAllRounds();
        Set<String> wins = new HashSet<>();
        raw.stream().filter(PlayerRound::counts)
                .collect(Collectors.groupingBy(PlayerRound::gameId,
                        Collectors.toMap(PlayerRound::player, r -> (int) r.total())))
                .forEach((gameId, totals) -> Scoring.winners(totals).forEach(p -> wins.add(gameId + "/" + p)));
        return raw.stream().map(r -> new Round(r.player(), r.gameId(), r.status(), r.courseName(), r.holesCount(),
                r.playedAt(), (int) r.holesPlayed(), (int) r.total(), r.toPar(), wins.contains(r.gameId() + "/" + r.player())))
                .toList();
    }

    private Map<String, HoleTallies> loadTallies() {
        return rounds.findHoleTallies().stream().collect(Collectors.toMap(HoleTally::player, HoleTally::view));
    }

    private static Map<String, List<Round>> byPlayer(List<Round> all) {
        return all.stream().collect(Collectors.groupingBy(Round::player, LinkedHashMap::new, Collectors.toList()));
    }

    /** Stats for one player's rounds (newest first). */
    private static PlayerStats stats(String name, List<Round> mine, Map<String, HoleTallies> tallies) {
        List<Round> finished = mine.stream().filter(Round::counts).toList();
        List<Round> rated = finished.stream().filter(Round::rated).toList();
        List<Round> nines = ofLength(rated, 9);
        List<Round> eighteens = ofLength(rated, 18);
        return new PlayerStats(
                name,
                mine.size(),
                finished.size(),
                (int) finished.stream().filter(Round::won).count(),
                best(nines),
                best(eighteens),
                average(nines, Round::total),
                average(eighteens, Round::total),
                average(rated, Round::toParPer18),
                average(rated.stream().limit(RECENT_FORM).toList(), Round::toParPer18),
                Handicap.index(rated),
                tallies.getOrDefault(name, HoleTallies.NONE),
                mine.isEmpty() ? null : mine.getFirst().playedAt());
    }

    private static List<Round> ofLength(List<Round> rounds, int holes) {
        return rounds.stream().filter(r -> r.holesCount() == holes).toList();
    }

    private static Integer best(List<Round> rounds) {
        return rounds.stream().map(Round::total).min(Integer::compare).orElse(null);
    }

    private static Double average(List<Round> rounds, ToIntFunction<Round> value) {
        return rounds.isEmpty() ? null : OneDecimal.ratio(rounds.stream().mapToLong(value::applyAsInt).sum(), rounds.size());
    }
}
