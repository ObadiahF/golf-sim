package com.golfsim.server.api;

import java.time.Instant;

/** Response body for {@code GET /api/ping}. */
public record PingResponse(String service, String version, String status, Instant serverTime) {
}
