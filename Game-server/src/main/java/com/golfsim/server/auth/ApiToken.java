package com.golfsim.server.auth;

import com.golfsim.server.config.GolfProperties;
import org.springframework.stereotype.Component;

/** Checks a presented token against the shared {@code golf.api-token} (constant-time compare). */
@Component
public class ApiToken {

    private final String expected;

    public ApiToken(GolfProperties properties) {
        this.expected = properties.apiToken();
    }

    public boolean matches(String presented) {
        return Secrets.matches(expected, presented);
    }
}
