package com.golfsim.server.game;

import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotEmpty;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.Size;
import java.util.List;

/** Request bodies for the game endpoints (also reused by the WebSocket {@code holeScore} message). */
public final class GameRequests {

    public static final int MAX_HOLES = 18;
    public static final int MAX_PLAYERS = 8;

    /**
     * Free text with no control characters or lone surrogates: Postgres refuses NUL (which surfaced as a misleading
     * "please retry" 409) and stores a lone surrogate as {@code ?}, so the response would not match what was saved.
     */
    static final String PLAIN_TEXT = "[^\\p{Cc}\\p{Cs}]*";

    private GameRequests() {
    }

    /** {@code POST /api/games}. Turn order follows the list order. */
    public record StartGame(
            @NotEmpty @Size(max = MAX_PLAYERS) List<@PlayerName String> players,
            @Min(1) @Max(MAX_HOLES) Integer holes,
            @Size(max = 100) @Pattern(regexp = PLAIN_TEXT, message = "must not contain control or invalid characters")
            String courseName) {

        public int holesOrDefault() {
            return holes == null ? 9 : holes;
        }
    }

    /** {@code POST /api/games/{id}/scores}: upsert of one player's strokes on one hole. */
    public record Score(
            @NotBlank String player,
            @NotNull @Min(1) @Max(MAX_HOLES) Integer hole,
            @NotNull @Min(1) @Max(10) Integer par,
            @NotNull @Min(1) @Max(99) Integer strokes) {
    }

    /** {@code POST /api/games/{id}/end}; status defaults to ABANDONED. */
    public record End(GameStatus status) {

        public GameStatus statusOrDefault() {
            return status == null ? GameStatus.ABANDONED : status;
        }
    }
}
