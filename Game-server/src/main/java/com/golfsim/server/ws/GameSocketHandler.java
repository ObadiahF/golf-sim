package com.golfsim.server.ws;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.exc.InvalidTypeIdException;
import com.fasterxml.jackson.databind.exc.MismatchedInputException;
import com.golfsim.server.api.JsonFields;
import com.golfsim.server.auth.TokenHandshakeInterceptor;
import com.golfsim.server.game.GameService;
import com.golfsim.server.game.GameView;
import com.golfsim.server.game.Rooms;
import com.golfsim.server.physics.PhysicsProfile;
import com.golfsim.server.physics.PhysicsService;
import com.golfsim.server.ws.WsHub.Client;
import jakarta.validation.ConstraintViolation;
import jakarta.validation.Validator;
import java.util.Map;
import java.util.Set;
import java.util.stream.Collectors;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;
import org.springframework.web.server.ResponseStatusException;
import org.springframework.web.socket.BinaryMessage;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.adapter.NativeWebSocketSession;
import org.springframework.web.socket.handler.ConcurrentWebSocketSessionDecorator;
import org.springframework.web.socket.handler.TextWebSocketHandler;

/**
 * {@code /ws}: relays remote commands to the sim in the remote's room and sim updates to the remotes in its room,
 * and persists sim hole scores.
 */
@Component
public class GameSocketHandler extends TextWebSocketHandler {

    private static final Logger log = LoggerFactory.getLogger(GameSocketHandler.class);
    private static final int SEND_TIME_LIMIT_MS = 5_000;
    private static final int SEND_BUFFER_BYTES = 512 * 1024;
    private static final String ASSEMBLER = MessageAssembler.class.getName();
    /**
     * Clients ping every ~20 s; one that sends nothing for this long is frozen or gone (a suspended phone, a dead
     * network behind the tunnel) and is closed, so it stops being listed and a dead sim stops counting as connected.
     */
    static final long READ_IDLE_TIMEOUT_MS = 60_000;
    private static final String TOMCAT_READ_IDLE_TIMEOUT = "org.apache.tomcat.websocket.READ_IDLE_TIMEOUT_MS";
    /** Close code for a sim refused because its room already has a sim (4000-4999: application-defined). */
    static final CloseStatus ROOM_TAKEN = new CloseStatus(4009, "Room already has a sim");

    private final WsHub hub;
    private final GameService games;
    private final PhysicsService physics;
    private final ObjectMapper objectMapper;
    private final Validator validator;

    public GameSocketHandler(WsHub hub, GameService games, PhysicsService physics, ObjectMapper objectMapper,
            Validator validator) {
        this.hub = hub;
        this.games = games;
        this.physics = physics;
        this.objectMapper = objectMapper;
        this.validator = validator;
    }

    @Override
    public void afterConnectionEstablished(WebSocketSession session) {
        if (session instanceof NativeWebSocketSession wrapper
                && wrapper.getNativeSession(jakarta.websocket.Session.class) instanceof jakarta.websocket.Session ws) {
            ws.getUserProperties().put(TOMCAT_READ_IDLE_TIMEOUT, READ_IDLE_TIMEOUT_MS);
        }
        Map<String, Object> attributes = session.getAttributes();
        Role role = (Role) attributes.get(TokenHandshakeInterceptor.ROLE);
        String name = (String) attributes
                .getOrDefault(TokenHandshakeInterceptor.NAME, role.wireName() + "-" + session.getId().substring(0, 6));
        String room = (String) attributes.get(TokenHandshakeInterceptor.ROOM);
        Client client = new Client(new ConcurrentWebSocketSessionDecorator(session, SEND_TIME_LIMIT_MS, SEND_BUFFER_BYTES),
                role, name, room, (String) attributes.get(TokenHandshakeInterceptor.INSTALL_ID));
        GameView game = games.current(room).orElse(null);
        PhysicsProfile profile = physics.current();
        WsHub.Admission admission;
        // Joining and the hello's state are one step against a sim's state being stored and relayed (handle, under the
        // same lock): a state either goes out before this client joins and is the one in its hello, or reaches it
        // after the hello. Otherwise a state relayed in between could arrive first and the older hello overwrite it.
        synchronized (hub) {
            admission = hub.add(client);
            if (admission.admitted()) {
                hub.send(client, new WsMessage.Hello(role, room, hub.simConnected(room), hub.remoteNames(room), game,
                        hub.lastSimState(room), profile));
            }
        }
        if (!admission.admitted()) {
            hub.sendAndClose(client, new WsMessage.Error("Another sim is already connected to " + Rooms.describe(room)),
                    ROOM_TAKEN);
            return;
        }
        if (admission.other() != null) {
            hub.sendAndClose(admission.other(), new WsMessage.Error("Replaced by a new connection from the same sim"),
                    CloseStatus.POLICY_VIOLATION);
        } else if (role == Role.SIM) {
            hub.sendTo(room, Role.REMOTE, new WsMessage.SimStatus(true));
        }
    }

    @Override
    public void afterConnectionClosed(WebSocketSession session, CloseStatus status) {
        hub.remove(session.getId())
                .filter(c -> c.role() == Role.SIM)
                .ifPresent(c -> hub.sendTo(c.room(), Role.REMOTE, new WsMessage.SimStatus(false)));
    }

    /** Frames arrive in parts so oversized messages can be refused politely; see {@link MessageAssembler}. */
    @Override
    public boolean supportsPartialMessages() {
        return true;
    }

    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage frame) {
        Client client = hub.get(session.getId()).orElse(null);
        if (client == null) {
            return; // a refused or replaced sim's last frames, arriving while it closes
        }
        MessageAssembler assembler = (MessageAssembler) session.getAttributes()
                .computeIfAbsent(ASSEMBLER, k -> new MessageAssembler());
        if (!assembler.add(frame.getPayload(), frame.isLast())) {
            return;
        }
        String json = assembler.take();
        if (json == null) {
            reject(client, "Message too large (max " + MessageAssembler.MAX_CHARS + " characters)");
            return;
        }
        try {
            JsonNode tree = objectMapper.readTree(json);
            if (tree == null || !tree.isObject()) {
                reject(client, "Expected a JSON object");
                return;
            }
            JsonChecks.nonFiniteNumber(tree).ifPresent(field -> {
                throw new Rejected("Invalid value for '" + field + "': numbers must be finite");
            });
            handle(client, objectMapper.treeToValue(tree, WsMessage.class), json, tree);
        } catch (InvalidTypeIdException e) {
            reject(client, e.getTypeId() == null ? "Missing \"type\"" : "Unknown message type '" + e.getTypeId() + "'");
        } catch (MismatchedInputException e) {
            reject(client, JsonFields.invalidValue(e, "Invalid JSON")); // e.g. trailing data after the object
        } catch (JsonProcessingException e) {
            reject(client, JsonChecks.parseError(e));
        } catch (Rejected e) {
            reject(client, e.getMessage());
        } catch (ResponseStatusException e) {
            reject(client, e.getReason());
        } catch (RuntimeException e) {
            log.error("Failed to handle message from {} {}: {}", client.role(), client.name(), json, e);
            reject(client, "Unexpected server error");
        }
    }

    /** Binary frames are refused like any other invalid message, instead of closing the connection (1003). */
    @Override
    protected void handleBinaryMessage(WebSocketSession session, BinaryMessage frame) {
        if (frame.isLast()) {
            hub.get(session.getId()).ifPresent(client -> reject(client, "Binary messages are not supported"));
        }
    }

    private void handle(Client client, WsMessage message, String json, JsonNode tree) {
        switch (message) {
            case WsMessage.Ping ignored -> hub.send(client, new WsMessage.Pong());
            case WsMessage.RemoteCommand command -> {
                requireRole(client, Role.REMOTE, tree);
                validate(command, tree);
                if (!hub.simConnected(client.room())) {
                    throw new Rejected("no sim connected");
                }
                hub.relay(client.room(), Role.SIM, json);
            }
            case WsMessage.SimUpdate update -> {
                requireRole(client, Role.SIM, tree);
                validate(update, tree);
                synchronized (hub) { // see afterConnectionEstablished: never between a remote joining and its hello
                    if (update instanceof WsMessage.State) {
                        hub.setLastSimState(client.room(), tree);
                    }
                    hub.relay(client.room(), Role.REMOTE, json);
                }
            }
            case WsMessage.HoleScore score -> {
                requireRole(client, Role.SIM, tree);
                validate(score, tree);
                validate(score.toScore(), tree);
                games.recordScore(score.gameId(), score.toScore()); // scorecard broadcast via GameEventRelay
            }
            case WsMessage.ServerMessage ignored ->
                    throw new Rejected("'" + type(tree) + "' is sent by the server only");
        }
    }

    private static void requireRole(Client client, Role role, JsonNode tree) {
        if (client.role() != role) {
            throw new Rejected("'" + type(tree) + "' must be sent by a " + role.wireName());
        }
    }

    private void validate(Object value, JsonNode tree) {
        Set<ConstraintViolation<Object>> violations = validator.validate(value);
        if (!violations.isEmpty()) {
            throw new Rejected(type(tree) + ": " + violations.stream()
                    .map(v -> v.getPropertyPath() + " " + v.getMessage())
                    .sorted()
                    .collect(Collectors.joining(", ")));
        }
    }

    private void reject(Client client, String message) {
        hub.send(client, new WsMessage.Error(message));
    }

    private static String type(JsonNode tree) {
        return tree.path("type").asText();
    }

    /** A message the server refuses; its text goes back to the sender as {@code error}. */
    private static final class Rejected extends RuntimeException {
        Rejected(String message) {
            super(message, null, false, false);
        }
    }
}
