package com.golfsim.server.auth;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.golfsim.server.api.ErrorResponse;
import com.golfsim.server.update.UpdateToken;
import jakarta.servlet.FilterChain;
import jakarta.servlet.ServletException;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.servlet.http.HttpServletResponse;
import java.io.IOException;
import java.time.Clock;
import java.time.Instant;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import org.springframework.http.HttpHeaders;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.stereotype.Component;
import org.springframework.web.filter.OncePerRequestFilter;

/**
 * Deny by default: every request needs {@code Authorization: Bearer <token>} (401 JSON otherwise) except the
 * {@link #OPEN_PATHS} allow-list. Paths that are not in normal form are refused with 400 before anything else, so
 * tricks like {@code /api;/players} or {@code /x/../api} can never reach a controller unchecked. Everything under
 * {@link #PUBLISH_PATH} takes the admin {@link UpdateToken} instead (the shared token never works there), and is
 * 404 while no admin token is configured.
 */
@Component
public class ApiTokenFilter extends OncePerRequestFilter {

    /** Exact, decoded paths that need no header. {@code /ws} checks its own {@code ?token=} in the handshake. */
    static final Set<String> OPEN_PATHS = Set.of(
            "/actuator/health", "/actuator/health/liveness", "/actuator/health/readiness", "/ws");

    /** Publishing self-update releases: admin token only. */
    public static final String PUBLISH_PATH = "/api/updates/publish";

    private static final String BEARER = "Bearer ";

    private final ApiToken token;
    private final UpdateToken updateToken;
    private final ObjectMapper objectMapper;
    private final Clock clock;

    public ApiTokenFilter(ApiToken token, UpdateToken updateToken, ObjectMapper objectMapper, Clock clock) {
        this.token = token;
        this.updateToken = updateToken;
        this.objectMapper = objectMapper;
        this.clock = clock;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        Optional<String> path = RequestPaths.normalised(request);
        if (path.isEmpty()) {
            reject(request, response, HttpStatus.BAD_REQUEST, "Malformed request path");
        } else if (isPublishPath(path.get())) {
            if (!updateToken.enabled()) {
                reject(request, response, HttpStatus.NOT_FOUND, "Publishing is disabled (no GOLF_UPDATE_TOKEN)");
            } else if (updateToken.matches(bearer(request))) {
                chain.doFilter(request, response);
            } else {
                reject(request, response, HttpStatus.UNAUTHORIZED, "Missing or invalid update token");
            }
        } else if (OPEN_PATHS.contains(path.get()) || token.matches(bearer(request))) {
            chain.doFilter(request, response);
        } else {
            reject(request, response, HttpStatus.UNAUTHORIZED, "Missing or invalid API token");
        }
    }

    static boolean isPublishPath(String path) {
        return path.equals(PUBLISH_PATH) || path.startsWith(PUBLISH_PATH + "/");
    }

    /** The bearer token of the request, or null. */
    private static String bearer(HttpServletRequest request) {
        String header = request.getHeader(HttpHeaders.AUTHORIZATION);
        return header != null && header.startsWith(BEARER) ? header.substring(BEARER.length()).trim() : null;
    }

    private void reject(HttpServletRequest request, HttpServletResponse response, HttpStatus status, String message)
            throws IOException {
        response.setStatus(status.value());
        response.setContentType(MediaType.APPLICATION_JSON_VALUE);
        objectMapper.writeValue(response.getOutputStream(),
                ErrorResponse.of(Instant.now(clock), status, message, request.getRequestURI(), Map.of()));
    }
}
