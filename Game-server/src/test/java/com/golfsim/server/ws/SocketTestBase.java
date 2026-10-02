package com.golfsim.server.ws;

import com.golfsim.server.IntegrationTest;
import com.golfsim.server.game.GameService;
import org.junit.jupiter.api.BeforeEach;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.web.server.LocalServerPort;

/** Shared setup for WebSocket tests: the server port, and sim/remote clients. */
abstract class SocketTestBase extends IntegrationTest {

    @LocalServerPort
    protected int port;

    @Autowired
    protected GameService games;

    @Autowired
    protected WsHub hub;

    /** Connections closed by the previous test are unregistered asynchronously; start from an empty hub. */
    @BeforeEach
    void waitForEmptyHub() throws InterruptedException {
        for (int i = 0; i < 50 && (hub.simConnected() || !hub.remoteNames().isEmpty()); i++) {
            Thread.sleep(50);
        }
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
}
