package com.golfsim.server.auth;

import com.golfsim.server.config.GolfProperties;
import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;
import org.springframework.stereotype.Component;

/** Checks a presented token against the shared {@code golf.api-token} (constant-time compare). */
@Component
public class ApiToken {

    private final byte[] expected;

    public ApiToken(GolfProperties properties) {
        this.expected = properties.apiToken().getBytes(StandardCharsets.UTF_8);
    }

    public boolean matches(String presented) {
        return presented != null && MessageDigest.isEqual(expected, presented.getBytes(StandardCharsets.UTF_8));
    }
}
