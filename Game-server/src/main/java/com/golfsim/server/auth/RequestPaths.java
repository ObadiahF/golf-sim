package com.golfsim.server.auth;

import jakarta.servlet.http.HttpServletRequest;
import java.nio.charset.StandardCharsets;
import java.util.Optional;
import org.springframework.web.util.UriUtils;

/**
 * Decodes the request path and refuses anything that is not already in normal form, so the path the auth filter
 * checks is the path the router uses. Refused: path parameters ({@code ;}), empty segments ({@code //}), {@code .}
 * and {@code ..} segments (plain or percent-encoded), encoded slashes, backslashes, control characters and malformed
 * percent escapes.
 */
final class RequestPaths {

    private RequestPaths() {
    }

    /** The decoded path within the application, or empty when the raw path is not normalised. */
    static Optional<String> normalised(HttpServletRequest request) {
        String raw = request.getRequestURI().substring(request.getContextPath().length());
        if (!raw.startsWith("/") || raw.contains(";") || raw.contains("//") || raw.contains("\\")) {
            return Optional.empty();
        }
        String decoded;
        try {
            decoded = UriUtils.decode(raw, StandardCharsets.UTF_8);
        } catch (IllegalArgumentException e) {
            return Optional.empty();
        }
        if (count(decoded, '/') != count(raw, '/') || decoded.contains("\\")
                || decoded.chars().anyMatch(Character::isISOControl)) {
            return Optional.empty();
        }
        for (String segment : decoded.split("/", -1)) {
            if (segment.equals(".") || segment.equals("..")) {
                return Optional.empty();
            }
        }
        return Optional.of(decoded);
    }

    private static long count(String s, char c) {
        return s.chars().filter(ch -> ch == c).count();
    }
}
