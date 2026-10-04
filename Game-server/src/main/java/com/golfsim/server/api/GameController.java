package com.golfsim.server.api;

import com.golfsim.server.game.GameRequests;
import com.golfsim.server.game.GameService;
import com.golfsim.server.game.GameStatus;
import com.golfsim.server.game.GameView;
import com.golfsim.server.game.Rooms;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import java.util.List;
import java.util.Map;
import org.springframework.http.HttpStatus;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

@RestController
@RequestMapping("/api/games")
public class GameController {

    private final GameService games;

    public GameController(GameService games) {
        this.games = games;
    }

    /** Starts a game in {@code ?room=} (default room when absent), abandoning only that room's game in progress. */
    @PostMapping
    @ResponseStatus(HttpStatus.CREATED)
    public GameView start(@Valid @RequestBody GameRequests.StartGame request, @RequestParam(required = false) String room) {
        return games.start(request, room(room));
    }

    /** The IN_PROGRESS game of {@code ?room=} (default room when absent), or 204 No Content when there is none. */
    @GetMapping("/current")
    public ResponseEntity<GameView> current(@RequestParam(required = false) String room) {
        return games.current(room(room)).map(ResponseEntity::ok).orElseGet(() -> ResponseEntity.noContent().build());
    }

    @GetMapping("/{id}")
    public GameView get(@PathVariable long id) {
        return games.get(id);
    }

    @GetMapping
    public List<GameView> recent(@RequestParam(defaultValue = "10") @Min(1) @Max(100) int limit) {
        return games.recent(limit);
    }

    @PostMapping("/{id}/end")
    public GameView end(@PathVariable long id, @RequestBody(required = false) GameRequests.End request) {
        return games.end(id, request == null ? GameStatus.ABANDONED : request.statusOrDefault());
    }

    @PostMapping("/{id}/scores")
    public GameView score(@PathVariable long id, @Valid @RequestBody GameRequests.Score request) {
        return games.recordScore(id, request);
    }

    private static String room(String raw) {
        return Rooms.parse(raw).orElseThrow(() -> new InvalidFieldsException(Map.of("room", Rooms.RULE)));
    }
}
