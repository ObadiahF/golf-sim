package com.golfsim.server.config;

import com.golfsim.server.auth.TokenHandshakeInterceptor;
import com.golfsim.server.ws.GameSocketHandler;
import org.springframework.context.annotation.Configuration;
import org.springframework.web.socket.config.annotation.EnableWebSocket;
import org.springframework.web.socket.config.annotation.WebSocketConfigurer;
import org.springframework.web.socket.config.annotation.WebSocketHandlerRegistry;

@Configuration
@EnableWebSocket
public class WebSocketConfig implements WebSocketConfigurer {

    private final GameSocketHandler handler;
    private final TokenHandshakeInterceptor interceptor;

    public WebSocketConfig(GameSocketHandler handler, TokenHandshakeInterceptor interceptor) {
        this.handler = handler;
        this.interceptor = interceptor;
    }

    @Override
    public void registerWebSocketHandlers(WebSocketHandlerRegistry registry) {
        // Native clients (Unity, iOS) send no Origin; allow any so LAN browsers/tools work too.
        registry.addHandler(handler, "/ws").addInterceptors(interceptor).setAllowedOriginPatterns("*");
    }
}
