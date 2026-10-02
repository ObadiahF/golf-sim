package com.golfsim.server.config;

import org.springframework.boot.context.properties.ConfigurationProperties;

/** Application-level settings bound from the {@code app.*} keys in application.yml. */
@ConfigurationProperties(prefix = "app")
public record AppProperties(String name, String version) {
}
