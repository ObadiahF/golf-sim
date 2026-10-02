package com.golfsim.server.game;

import java.time.Clock;
import java.time.Instant;
import java.time.temporal.ChronoUnit;
import java.util.HashSet;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import java.util.stream.Collectors;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.data.domain.Limit;
import org.springframework.http.HttpStatus;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;
import org.springframework.web.server.ResponseStatusException;

/** Games, players and scores. Every change publishes a {@link GameEvents} event for the WebSocket clients. */
@Service
@Transactional
public class GameService {

    private final GameRepository games;
    private final PlayerRepository players;
    private final HoleScoreRepository scores;
    private final ApplicationEventPublisher events;
    private final Clock clock;

    public GameService(GameRepository games, PlayerRepository players, HoleScoreRepository scores,
            ApplicationEventPublisher events, Clock clock) {
        this.games = games;
        this.players = players;
        this.scores = scores;
        this.events = events;
        this.clock = clock;
    }

    /** Advisory-lock id for {@link #start}; any constant works as long as nothing else uses it. */
    static final long START_LOCK = 0x601F_57A7L;

    /**
     * Starts a new game, abandoning any game still in progress. Concurrent starts are serialised (advisory lock),
     * so each succeeds in turn and the last one is the game left IN_PROGRESS.
     */
    public GameView start(GameRequests.StartGame request) {
        List<String> names = request.players().stream().map(Names::normalize).toList();
        Set<String> seen = new HashSet<>();
        for (String name : names) {
            if (!seen.add(Names.key(name))) {
                throw badRequest("Duplicate player name: " + name);
            }
        }
        games.advisoryLock(START_LOCK);
        for (Game old : games.findByStatusForUpdate(GameStatus.IN_PROGRESS)) {
            endGame(old, GameStatus.ABANDONED);
        }
        games.flush(); // free the single-in-progress unique index before inserting the new game

        List<Player> roster = names.stream().map(this::findOrCreatePlayer).toList();
        String course = request.courseName() == null || request.courseName().isBlank() ? null : request.courseName().trim();
        Game game = games.save(new Game(request.holesOrDefault(), course, roster));
        GameView view = GameView.of(game, List.of());
        events.publishEvent(new GameEvents.Started(view));
        return view;
    }

    @Transactional(readOnly = true)
    public Optional<GameView> current() {
        return games.findFirstByStatus(GameStatus.IN_PROGRESS).map(this::view);
    }

    @Transactional(readOnly = true)
    public GameView get(long id) {
        return view(load(id));
    }

    @Transactional(readOnly = true)
    public List<GameView> recent(int limit) {
        List<Game> recent = games.findAllByOrderByCreatedAtDescIdDesc(Limit.of(limit));
        Map<Long, List<HoleScore>> byGame = scores.findByGameIn(recent).stream()
                .collect(Collectors.groupingBy(s -> s.getGame().getId()));
        return recent.stream().map(g -> GameView.of(g, byGame.getOrDefault(g.getId(), List.of()))).toList();
    }

    /** Finishes or abandons an in-progress game. */
    public GameView end(long id, GameStatus status) {
        if (status == GameStatus.IN_PROGRESS) {
            throw badRequest("status must be FINISHED or ABANDONED");
        }
        Game game = loadForUpdate(id);
        if (!game.isInProgress()) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "Game " + id + " is already " + game.getStatus());
        }
        return endGame(game, status);
    }

    /**
     * Idempotent upsert of one player's strokes on one hole. Allowed while the game is in progress or
     * finished (corrections, retries); the game becomes FINISHED once every player has every hole.
     * The game row is locked first, so concurrent scores for one game run one after another: the
     * find-then-insert upsert cannot race, and exactly one of them sees the card complete and finishes the game.
     */
    public GameView recordScore(long gameId, GameRequests.Score request) {
        Game game = loadForUpdate(gameId);
        if (game.getStatus() == GameStatus.ABANDONED) {
            throw new ResponseStatusException(HttpStatus.CONFLICT, "Game " + gameId + " was abandoned");
        }
        if (request.hole() > game.getHolesCount()) {
            throw badRequest("hole must be between 1 and " + game.getHolesCount());
        }
        Player player = game.findPlayer(request.player())
                .orElseThrow(() -> badRequest("Player '" + request.player() + "' is not in game " + gameId));
        HoleScore score = scores.findByGameAndPlayerAndHoleNumber(game, player, request.hole())
                .orElseGet(() -> new HoleScore(game, player, request.hole()));
        score.update(request.par(), request.strokes());
        scores.saveAndFlush(score);

        boolean complete = scores.countByGame(game) >= (long) game.getPlayers().size() * game.getHolesCount();
        boolean justFinished = complete && game.isInProgress();
        if (justFinished) {
            game.end(GameStatus.FINISHED, now());
        }
        GameView view = view(game);
        events.publishEvent(new GameEvents.ScoreRecorded(view, justFinished));
        return view;
    }

    private GameView endGame(Game game, GameStatus status) {
        game.end(status, now());
        GameView view = view(game);
        events.publishEvent(new GameEvents.Ended(view));
        return view;
    }

    /** Microsecond precision, matching what Postgres stores. */
    private Instant now() {
        return Instant.now(clock).truncatedTo(ChronoUnit.MICROS);
    }

    private Player findOrCreatePlayer(String name) {
        return players.findByName(name).orElseGet(() -> players.save(new Player(name)));
    }

    private Game load(long id) {
        return games.findById(id).orElseThrow(() -> notFound(id));
    }

    private Game loadForUpdate(long id) {
        return games.findByIdForUpdate(id).orElseThrow(() -> notFound(id));
    }

    private static ResponseStatusException notFound(long id) {
        return new ResponseStatusException(HttpStatus.NOT_FOUND, "Game " + id + " not found");
    }

    private GameView view(Game game) {
        return GameView.of(game, scores.findByGame(game));
    }

    private static ResponseStatusException badRequest(String message) {
        return new ResponseStatusException(HttpStatus.BAD_REQUEST, message);
    }
}
