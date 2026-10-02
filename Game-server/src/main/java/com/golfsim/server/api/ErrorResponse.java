package com.golfsim.server.api;

import java.time.Instant;
import java.util.Map;
import org.springframework.http.HttpStatus;
import org.springframework.http.HttpStatusCode;

/** Uniform JSON error body returned by {@link GlobalExceptionHandler} and the auth filter. */
public record ErrorResponse(
        Instant timestamp,
        int status,
        String error,
        String message,
        String path,
        Map<String, String> fieldErrors) {

    public static ErrorResponse of(
            Instant at, HttpStatusCode status, String message, String path, Map<String, String> fieldErrors) {
        HttpStatus resolved = HttpStatus.resolve(status.value());
        String reason = resolved != null ? resolved.getReasonPhrase() : "Error";
        return new ErrorResponse(at, status.value(), reason, message, path, fieldErrors);
    }
}
