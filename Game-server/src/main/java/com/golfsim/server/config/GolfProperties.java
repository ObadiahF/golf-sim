package com.golfsim.server.config;

import jakarta.validation.constraints.NotBlank;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.validation.annotation.Validated;

/** Golf settings bound from {@code golf.*}; {@code apiToken} is the shared secret for REST and WebSocket clients. */
@Validated
@ConfigurationProperties(prefix = "golf")
public record GolfProperties(@NotBlank String apiToken) {
}
