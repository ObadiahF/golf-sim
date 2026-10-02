package com.golfsim.server.auth;

import com.fasterxml.jackson.databind.ObjectMapper;
import com.golfsim.server.api.ErrorResponse;
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
 * tricks like {@code /api;/players} or {@code /x/../api} can never reach a controller unchecked.
 */
@Component
public class ApiTokenFilter extends OncePerRequestFilter {

    /** Exact, decoded paths that need no header. {@code /ws} checks its own {@code ?token=} in the handshake. */
    static final Set<String> OPEN_PATHS = Set.of(
            "/actuator/health", "/actuator/health/liveness", "/actuator/health/readiness", "/ws");

    private static final String BEARER = "Bearer ";

    private final ApiToken token;
    private final ObjectMapper objectMapper;
    private final Clock clock;

    public ApiTokenFilter(ApiToken token, ObjectMapper objectMapper, Clock clock) {
        this.token = token;
        this.objectMapper = objectMapper;
        this.clock = clock;
    }

    @Override
    protected void doFilterInternal(HttpServletRequest request, HttpServletResponse response, FilterChain chain)
            throws ServletException, IOException {
        Optional<String> path = RequestPaths.normalised(request);
        if (path.isEmpty()) {
            reject(request, response, HttpStatus.BAD_REQUEST, "Malformed request path");
        } else if (OPEN_PATHS.contains(path.get()) || hasValidToken(request)) {
            chain.doFilter(request, response);
        } else {
            reject(request, response, HttpStatus.UNAUTHORIZED, "Missing or invalid API token");
        }
    }

    private boolean hasValidToken(HttpServletRequest request) {
        String header = request.getHeader(HttpHeaders.AUTHORIZATION);
        return header != null && header.startsWith(BEARER) && token.matches(header.substring(BEARER.length()).trim());
    }

    private void reject(HttpServletRequest request, HttpServletResponse response, HttpStatus status, String message)
            throws IOException {
        response.setStatus(status.value());
        response.setContentType(MediaType.APPLICATION_JSON_VALUE);
        objectMapper.writeValue(response.getOutputStream(),
                ErrorResponse.of(Instant.now(clock), status, message, request.getRequestURI(), Map.of()));
    }
}
