package com.golfsim.server.ws;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.ObjectWriter;
import java.io.IOException;
import java.util.Collection;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;
import java.util.stream.Stream;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;
import org.springframework.web.socket.CloseStatus;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;

/**
 * Registry of connected sims and remotes, and the only place that writes frames to them. Everything but ball physics
 * is scoped to a room ({@link com.golfsim.server.game.Rooms}): one sim and its remotes, each with its own last state.
 */
@Component
public class WsHub {

    private static final Logger log = LoggerFactory.getLogger(WsHub.class);

    /**
     * A connected client; {@code session} is already wrapped for thread-safe sends.
     *
     * @param room      normalised room code, "" for the default room
     * @param installId the sim's permanent install id, or null (remotes, older sims)
     */
    public record Client(WebSocketSession session, Role role, String name, String room, String installId) {

        boolean isSimIn(String room) {
            return role == Role.SIM && this.room.equals(room);
        }

        /** The same sim installation reconnecting (both sent the same install id). */
        boolean sameInstall(Client other) {
            return installId != null && installId.equals(other.installId);
        }
    }

    /**
     * What {@link #add} did with a client.
     *
     * @param admitted false for a sim refused because its room already has another sim (that one is untouched)
     * @param other    the sim already in the room: refused-against when not admitted, replaced (and unregistered)
     *                 when admitted; null when the room had no sim or the client is a remote
     */
    public record Admission(boolean admitted, Client other) {
    }

    private final Map<String, Client> clients = new ConcurrentHashMap<>();
    private final Map<String, JsonNode> lastSimStates = new ConcurrentHashMap<>();
    private final ObjectWriter writer;

    public WsHub(ObjectMapper objectMapper) {
        this.writer = objectMapper.writerFor(WsMessage.class);
    }

    /**
     * Registers a client. One sim per room: a sim with the same install id as the room's sim replaces it (the TV
     * reconnecting before its old socket was noticed dead); any other second sim is refused. Synchronised with
     * {@link #remove} so two sims connecting at once can't both get in.
     */
    public synchronized Admission add(Client client) {
        Optional<Client> existing = client.role() == Role.SIM ? sim(client.room()) : Optional.empty();
        if (existing.isPresent() && !client.sameInstall(existing.get())) {
            return new Admission(false, existing.get());
        }
        existing.ifPresent(old -> clients.remove(old.session().getId()));
        clients.put(client.session().getId(), client);
        return new Admission(true, existing.orElse(null));
    }

    /** Unregisters a client; when it was its room's sim, the room's last state goes with it. */
    public synchronized Optional<Client> remove(String sessionId) {
        Client removed = clients.remove(sessionId);
        if (removed != null && removed.role() == Role.SIM) {
            lastSimStates.remove(removed.room());
        }
        return Optional.ofNullable(removed);
    }

    public Optional<Client> get(String sessionId) {
        return Optional.ofNullable(clients.get(sessionId));
    }

    /** Every connected client (package-private, for tests). */
    Collection<Client> clients() {
        return List.copyOf(clients.values());
    }

    public Optional<Client> sim(String room) {
        return clients.values().stream().filter(c -> c.isSimIn(room)).findFirst();
    }

    public boolean simConnected(String room) {
        return sim(room).isPresent();
    }

    public List<String> remoteNames(String room) {
        return inRoom(room).filter(c -> c.role() == Role.REMOTE).map(Client::name).sorted().toList();
    }

    /** The last {@code state} the room's sim sent, or null. */
    public JsonNode lastSimState(String room) {
        return lastSimStates.get(room);
    }

    public void setLastSimState(String room, JsonNode state) {
        lastSimStates.put(room, state);
    }

    public void send(Client client, WsMessage message) {
        write(client, serialize(message));
    }

    /** Sends a last message, then closes the connection. */
    public void sendAndClose(Client client, WsMessage message, CloseStatus status) {
        send(client, message);
        try {
            client.session().close(status);
        } catch (IOException | RuntimeException e) {
            log.warn("Close of {} {} failed: {}", client.role(), client.name(), e.getMessage());
        }
    }

    /** Sends a message to every client in the room with the given role. */
    public void sendTo(String room, Role role, WsMessage message) {
        relay(room, role, serialize(message));
    }

    /** Sends a raw JSON frame unchanged to every client in the room with the given role. */
    public void relay(String room, Role role, String json) {
        inRoom(room).filter(c -> c.role() == role).forEach(c -> write(c, json));
    }

    /** Sends a message to every client in the room. */
    public void broadcast(String room, WsMessage message) {
        String json = serialize(message);
        inRoom(room).forEach(c -> write(c, json));
    }

    /** Sends a message to every client in every room. */
    public void broadcastAll(WsMessage message) {
        String json = serialize(message);
        clients.values().forEach(c -> write(c, json));
    }

    private Stream<Client> inRoom(String room) {
        return clients.values().stream().filter(c -> c.room().equals(room));
    }

    private String serialize(WsMessage message) {
        try {
            return writer.writeValueAsString(message);
        } catch (JsonProcessingException e) {
            throw new IllegalStateException("Cannot serialize " + message, e);
        }
    }

    private void write(Client client, String json) {
        try {
            if (client.session().isOpen()) {
                client.session().sendMessage(new TextMessage(json));
            }
        } catch (IOException | RuntimeException e) {
            log.warn("Send to {} {} failed: {}", client.role(), client.name(), e.getMessage());
        }
    }
}
