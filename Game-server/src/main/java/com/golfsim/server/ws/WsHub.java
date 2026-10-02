package com.golfsim.server.ws;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import com.fasterxml.jackson.databind.ObjectWriter;
import java.io.IOException;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.concurrent.ConcurrentHashMap;
import org.slf4j.Logger;
import org.slf4j.LoggerFactory;
import org.springframework.stereotype.Component;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;

/** Registry of connected sims and remotes, and the only place that writes frames to them. */
@Component
public class WsHub {

    private static final Logger log = LoggerFactory.getLogger(WsHub.class);

    /** A connected client; {@code session} is already wrapped for thread-safe sends. */
    public record Client(WebSocketSession session, Role role, String name) {
    }

    private final Map<String, Client> clients = new ConcurrentHashMap<>();
    private final ObjectWriter writer;
    private volatile JsonNode lastSimState;

    public WsHub(ObjectMapper objectMapper) {
        this.writer = objectMapper.writerFor(WsMessage.class);
    }

    public void add(Client client) {
        clients.put(client.session().getId(), client);
    }

    public Optional<Client> remove(String sessionId) {
        Client removed = clients.remove(sessionId);
        if (removed != null && removed.role() == Role.SIM && !simConnected()) {
            lastSimState = null;
        }
        return Optional.ofNullable(removed);
    }

    public Optional<Client> get(String sessionId) {
        return Optional.ofNullable(clients.get(sessionId));
    }

    public boolean simConnected() {
        return clients.values().stream().anyMatch(c -> c.role() == Role.SIM);
    }

    public List<String> remoteNames() {
        return clients.values().stream().filter(c -> c.role() == Role.REMOTE).map(Client::name).sorted().toList();
    }

    public JsonNode lastSimState() {
        return lastSimState;
    }

    public void setLastSimState(JsonNode state) {
        this.lastSimState = state;
    }

    public void send(Client client, WsMessage message) {
        write(client, serialize(message));
    }

    /** Sends a message to every client with the given role. */
    public void sendTo(Role role, WsMessage message) {
        relay(role, serialize(message));
    }

    /** Sends a raw JSON frame unchanged to every client with the given role. */
    public void relay(Role role, String json) {
        clients.values().stream().filter(c -> c.role() == role).forEach(c -> write(c, json));
    }

    public void broadcast(WsMessage message) {
        String json = serialize(message);
        clients.values().forEach(c -> write(c, json));
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
