package com.golfsim.server.auth;

import com.golfsim.server.game.Names;
import com.golfsim.server.game.Rooms;
import com.golfsim.server.ws.Role;
import java.net.URI;
import java.net.URISyntaxException;
import java.net.URLDecoder;
import java.nio.charset.StandardCharsets;
import java.util.Map;
import java.util.Optional;
import java.util.regex.Pattern;
import org.springframework.http.HttpStatus;
import org.springframework.http.server.ServerHttpRequest;
import org.springframework.http.server.ServerHttpResponse;
import org.springframework.http.server.ServletServerHttpRequest;
import org.springframework.stereotype.Component;
import org.springframework.util.LinkedMultiValueMap;
import org.springframework.util.MultiValueMap;
import org.springframework.web.socket.WebSocketHandler;
import org.springframework.web.socket.server.HandshakeInterceptor;
import org.springframework.web.util.UriComponentsBuilder;

/**
 * Accepts the WebSocket upgrade only with a well-formed query (400 otherwise), a valid {@code ?token=} (401) and a
 * {@code role} of sim or remote (400), plus an optional {@code room} code ({@link Rooms}) and sim install {@code id}
 * (400 when malformed). Stores the role, the room (normalised; {@link Rooms#DEFAULT} when absent), the install id
 * (when given) and the cleaned device name ({@link Names#deviceName}) in the session attributes.
 */
@Component
public class TokenHandshakeInterceptor implements HandshakeInterceptor {

    public static final String ROLE = "role";
    public static final String NAME = "name";
    public static final String ROOM = "room";
    public static final String INSTALL_ID = "installId";

    /** A sim's permanent install id: tells the same TV reconnecting apart from a second sim in its room. */
    private static final Pattern INSTALL_ID_FORMAT = Pattern.compile("[A-Za-z0-9_-]{1,64}");

    private final ApiToken token;

    public TokenHandshakeInterceptor(ApiToken token) {
        this.token = token;
    }

    @Override
    public boolean beforeHandshake(ServerHttpRequest request, ServerHttpResponse response, WebSocketHandler handler,
            Map<String, Object> attributes) {
        Optional<MultiValueMap<String, String>> params = queryParams(request);
        if (params.isEmpty()) {
            response.setStatusCode(HttpStatus.BAD_REQUEST);
            return false;
        }
        if (!token.matches(decoded(params.get().getFirst("token")))) {
            response.setStatusCode(HttpStatus.UNAUTHORIZED);
            return false;
        }
        Optional<Role> role = Role.parse(decoded(params.get().getFirst("role")));
        if (role.isEmpty()) {
            response.setStatusCode(HttpStatus.BAD_REQUEST);
            return false;
        }
        Optional<String> room = Rooms.parse(decoded(params.get().getFirst("room")));
        String installId = decoded(params.get().getFirst("id"));
        boolean hasId = installId != null && !installId.isEmpty();
        if (room.isEmpty() || hasId && !INSTALL_ID_FORMAT.matcher(installId).matches()) {
            response.setStatusCode(HttpStatus.BAD_REQUEST);
            return false;
        }
        attributes.put(ROLE, role.get());
        attributes.put(ROOM, room.get());
        if (hasId) {
            attributes.put(INSTALL_ID, installId);
        }
        String name = Names.deviceName(decoded(params.get().getFirst("name")));
        if (name != null) {
            attributes.put(NAME, name);
        }
        return true;
    }

    @Override
    public void afterHandshake(ServerHttpRequest request, ServerHttpResponse response, WebSocketHandler handler,
            Exception exception) {
    }

    /**
     * The query parameters with every value already checked to decode; empty when the query is malformed (bad
     * {@code %} escapes or characters a URI can't hold), which the container would otherwise fail on with a 500.
     */
    private static Optional<MultiValueMap<String, String>> queryParams(ServerHttpRequest request) {
        String raw = request instanceof ServletServerHttpRequest servlet
                ? servlet.getServletRequest().getQueryString()
                : request.getURI().getRawQuery();
        if (raw == null) {
            return Optional.of(new LinkedMultiValueMap<>());
        }
        try {
            new URI("ws://host/ws?" + raw);
            MultiValueMap<String, String> params = UriComponentsBuilder.newInstance().query(raw).build().getQueryParams();
            params.values().forEach(values -> values.forEach(TokenHandshakeInterceptor::decoded));
            return Optional.of(params);
        } catch (URISyntaxException | IllegalArgumentException e) {
            return Optional.empty();
        }
    }

    private static String decoded(String value) {
        return value == null ? null : URLDecoder.decode(value, StandardCharsets.UTF_8);
    }
}
