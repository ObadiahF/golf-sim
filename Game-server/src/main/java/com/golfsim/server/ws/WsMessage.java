package com.golfsim.server.ws;

import com.fasterxml.jackson.annotation.JsonProperty;
import com.fasterxml.jackson.annotation.JsonSubTypes;
import com.fasterxml.jackson.annotation.JsonTypeInfo;
import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.game.GameRequests;
import com.golfsim.server.game.GameView;
import jakarta.validation.constraints.NotBlank;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Positive;
import java.util.List;

/**
 * Every WebSocket message, as JSON text frames of the form {@code {"type": "...", ...}}. This file is the single
 * source of truth for the wire schema; docs/PROTOCOL.md describes the same messages.
 */
@JsonTypeInfo(use = JsonTypeInfo.Id.NAME, property = "type")
@JsonSubTypes({
        @JsonSubTypes.Type(value = WsMessage.Ping.class, name = "ping"),
        @JsonSubTypes.Type(value = WsMessage.Pong.class, name = "pong"),
        // remote -> sim
        @JsonSubTypes.Type(value = WsMessage.Nav.class, name = "nav"),
        @JsonSubTypes.Type(value = WsMessage.Club.class, name = "club"),
        @JsonSubTypes.Type(value = WsMessage.Aim.class, name = "aim"),
        @JsonSubTypes.Type(value = WsMessage.AimReset.class, name = "aimReset"),
        @JsonSubTypes.Type(value = WsMessage.Shot.class, name = "shot"),
        @JsonSubTypes.Type(value = WsMessage.Mulligan.class, name = "mulligan"),
        @JsonSubTypes.Type(value = WsMessage.Skip.class, name = "skip"),
        // sim -> remotes
        @JsonSubTypes.Type(value = WsMessage.State.class, name = "state"),
        @JsonSubTypes.Type(value = WsMessage.ShotResult.class, name = "shotResult"),
        @JsonSubTypes.Type(value = WsMessage.Turn.class, name = "turn"),
        @JsonSubTypes.Type(value = WsMessage.ShotRejected.class, name = "shotRejected"),
        // sim -> server
        @JsonSubTypes.Type(value = WsMessage.HoleScore.class, name = "holeScore"),
        // server -> clients
        @JsonSubTypes.Type(value = WsMessage.Hello.class, name = "hello"),
        @JsonSubTypes.Type(value = WsMessage.SimStatus.class, name = "simStatus"),
        @JsonSubTypes.Type(value = WsMessage.GameStarted.class, name = "gameStarted"),
        @JsonSubTypes.Type(value = WsMessage.Scorecard.class, name = "scorecard"),
        @JsonSubTypes.Type(value = WsMessage.GameFinished.class, name = "gameFinished"),
        @JsonSubTypes.Type(value = WsMessage.Error.class, name = "error"),
})
public sealed interface WsMessage {

    /** Sent by a remote; validated and relayed unchanged to every connected sim. */
    sealed interface RemoteCommand extends WsMessage {
    }

    /** Sent by a sim; validated and relayed unchanged to every connected remote. */
    sealed interface SimUpdate extends WsMessage {
    }

    /** Only the server sends these; clients sending them get an error. */
    sealed interface ServerMessage extends WsMessage {
    }

    enum NavKey {
        @JsonProperty("up") UP,
        @JsonProperty("down") DOWN,
        @JsonProperty("left") LEFT,
        @JsonProperty("right") RIGHT,
        @JsonProperty("select") SELECT,
        @JsonProperty("back") BACK
    }

    // ---- keepalive (any client) ----

    record Ping() implements WsMessage {
    }

    record Pong() implements ServerMessage {
    }

    // ---- remote -> sim ----

    record Nav(@NotNull NavKey key) implements RemoteCommand {
    }

    record Club(@NotBlank String club) implements RemoteCommand {
    }

    /** @param delta degrees to turn the aim, + is right */
    record Aim(@NotNull Double delta) implements RemoteCommand {
    }

    record AimReset() implements RemoteCommand {
    }

    /**
     * Same fields as the phone's UDP shot datagram.
     *
     * @param speed   ball speed, m/s
     * @param launch  launch angle, degrees
     * @param azimuth start direction, degrees, + right
     * @param back    backspin, rpm
     * @param side    sidespin, rpm, + curves right
     */
    record Shot(@NotNull @Positive Double speed, @NotNull Double launch, @NotNull Double azimuth,
            @NotNull Double back, @NotNull Double side, String club, Long id) implements RemoteCommand {
    }

    record Mulligan() implements RemoteCommand {
    }

    record Skip() implements RemoteCommand {
    }

    // ---- sim -> remotes ----

    /** What the sim is showing, so the app can switch between remote (menu) mode and gameplay mode. */
    record State(@NotBlank String screen, Long gameId, String currentPlayer, Integer hole, Integer par,
            Integer strokes, String club, Double aim, Double distanceToPin, String lie) implements SimUpdate {
    }

    /** @param carry yards; @param total yards */
    record ShotResult(@NotBlank String player, Double carry, Double total, String lie, Boolean holed,
            Integer strokes) implements SimUpdate {
    }

    record Turn(@NotBlank String player, @NotNull Integer hole, Integer strokes) implements SimUpdate {
    }

    /**
     * A phone's {@code shot} arrived while the sim couldn't hit it (between turns, during a replay, ...).
     *
     * @param reason shown to the player, e.g. "Wait for the next turn"
     * @param id     the rejected shot's {@code id}, echoed when it had one
     */
    record ShotRejected(@NotBlank String reason, Long id) implements SimUpdate {
    }

    // ---- sim -> server ----

    /** Persisted like {@code POST /api/games/{gameId}/scores}; field rules come from {@link GameRequests.Score}. */
    record HoleScore(@NotNull Long gameId, String player, Integer hole, Integer par, Integer strokes)
            implements WsMessage {

        public GameRequests.Score toScore() {
            return new GameRequests.Score(player, hole, par, strokes);
        }
    }

    // ---- server -> clients ----

    /**
     * First message on every connection.
     *
     * @param remotes device names of the connected remotes
     * @param game    the IN_PROGRESS game, or null
     * @param state   the last {@code state} message from the sim, or null
     */
    record Hello(Role role, boolean simConnected, List<String> remotes, GameView game, JsonNode state)
            implements ServerMessage {
    }

    record SimStatus(boolean connected) implements ServerMessage {
    }

    record GameStarted(GameView game) implements ServerMessage {
    }

    record Scorecard(GameView game) implements ServerMessage {
    }

    /** The game left IN_PROGRESS; {@code game.status} is FINISHED or ABANDONED. */
    record GameFinished(GameView game) implements ServerMessage {
    }

    record Error(String message) implements ServerMessage {
    }
}
