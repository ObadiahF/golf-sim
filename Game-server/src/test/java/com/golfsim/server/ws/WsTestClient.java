package com.golfsim.server.ws;

import static org.assertj.core.api.Assertions.assertThat;

import com.fasterxml.jackson.databind.JsonNode;
import com.fasterxml.jackson.databind.ObjectMapper;
import jakarta.websocket.ContainerProvider;
import jakarta.websocket.WebSocketContainer;
import java.net.URI;
import java.util.concurrent.BlockingQueue;
import java.util.concurrent.LinkedBlockingQueue;
import java.util.concurrent.TimeUnit;
import org.springframework.web.socket.TextMessage;
import org.springframework.web.socket.WebSocketSession;
import org.springframework.web.socket.client.standard.StandardWebSocketClient;
import org.springframework.web.socket.handler.TextWebSocketHandler;

/** A test WebSocket client that queues every JSON message it receives. */
class WsTestClient extends TextWebSocketHandler implements AutoCloseable {

    private static final ObjectMapper JSON = new ObjectMapper();
    private static final int MAX_MESSAGE_CHARS = 256 * 1024;

    private final BlockingQueue<JsonNode> inbox = new LinkedBlockingQueue<>();
    private WebSocketSession session;

    static WsTestClient connect(String url) throws Exception {
        WsTestClient client = new WsTestClient();
        WebSocketContainer container = ContainerProvider.getWebSocketContainer();
        container.setDefaultMaxTextMessageBufferSize(MAX_MESSAGE_CHARS); // receive relayed messages up to the server cap
        client.session = new StandardWebSocketClient(container).execute(client, null, URI.create(url))
                .get(5, TimeUnit.SECONDS);
        return client;
    }

    @Override
    protected void handleTextMessage(WebSocketSession session, TextMessage message) throws Exception {
        inbox.add(JSON.readTree(message.getPayload()));
    }

    void send(String json) throws Exception {
        session.sendMessage(new TextMessage(json));
    }

    /** Waits for the next message of the given type, skipping any others. */
    JsonNode await(String type) throws InterruptedException {
        long deadline = System.nanoTime() + TimeUnit.SECONDS.toNanos(5);
        while (System.nanoTime() < deadline) {
            JsonNode next = inbox.poll(100, TimeUnit.MILLISECONDS);
            if (next != null && type.equals(next.path("type").asText())) {
                return next;
            }
        }
        throw new AssertionError("No '" + type + "' message received");
    }

    /** Asserts nothing of the given type arrives within a short window. */
    void assertNo(String type) throws InterruptedException {
        long deadline = System.nanoTime() + TimeUnit.MILLISECONDS.toNanos(300);
        while (System.nanoTime() < deadline) {
            JsonNode next = inbox.poll(50, TimeUnit.MILLISECONDS);
            assertThat(next == null ? null : next.path("type").asText()).isNotEqualTo(type);
        }
    }

    boolean isOpen() {
        return session.isOpen();
    }

    @Override
    public void close() throws Exception {
        session.close();
    }
}
