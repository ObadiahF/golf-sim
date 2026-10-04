package com.golfsim.server.ws;

import com.golfsim.server.IntegrationTest;
import com.golfsim.server.RawHttp;
import com.golfsim.server.game.GameService;
import java.util.Map;
import org.junit.jupiter.api.BeforeEach;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.web.server.LocalServerPort;

/** Shared setup for WebSocket tests: the server port, and sim/remote clients. */
abstract class SocketTestBase extends IntegrationTest {

    private static final Map<String, String> UPGRADE = Map.of(
            "Upgrade", "websocket", "Connection", "Upgrade",
            "Sec-WebSocket-Key", "dGhlIHNhbXBsZSBub25jZQ==", "Sec-WebSocket-Version", "13");

    @LocalServerPort
    protected int port;

    @Autowired
    protected GameService games;

    @Autowired
    protected WsHub hub;

    /** Connections closed by the previous test are unregistered asynchronously; start from an empty hub. */
    @BeforeEach
    void waitForEmptyHub() throws InterruptedException {
        for (int i = 0; i < 50 && !hub.clients().isEmpty(); i++) {
            Thread.sleep(50);
        }
    }

    /** Status of a raw upgrade request: 101 when accepted, else the handshake's refusal. */
    protected int upgrade(String query) throws Exception {
        return RawHttp.status(port, "GET", "/ws?" + query, UPGRADE, null);
    }

    protected String url(String query) {
        return "ws://localhost:" + port + "/ws?" + query;
    }

    protected WsTestClient sim() throws Exception {
        return WsTestClient.connect(url("token=" + TOKEN + "&role=sim&name=TV"));
    }

    protected WsTestClient remote() throws Exception {
        return remote("Obi%27s%20iPhone");
    }

    /** @param encodedName the {@code name} query value, already URL-encoded */
    protected WsTestClient remote(String encodedName) throws Exception {
        return WsTestClient.connect(url("token=" + TOKEN + "&role=remote&name=" + encodedName));
    }

    /** A sim in {@code room} with an install id; null leaves either out of the query. */
    protected WsTestClient simIn(String room, String installId) throws Exception {
        return WsTestClient.connect(url("token=" + TOKEN + "&role=sim&name=TV" + param("room", room) + param("id", installId)));
    }

    protected WsTestClient remoteIn(String room) throws Exception {
        return WsTestClient.connect(url("token=" + TOKEN + "&role=remote&name=Phone" + param("room", room)));
    }

    private static String param(String key, String value) {
        return value == null ? "" : "&" + key + "=" + value;
    }
}
