package com.golfsim.server.update;

import com.golfsim.server.auth.Secrets;
import org.springframework.stereotype.Component;

/**
 * The admin token that may publish releases ({@code updates.token}, GOLF_UPDATE_TOKEN). Deliberately separate from
 * the shared API token, which ships inside the phone app and the sim. Unset means publishing is disabled.
 */
@Component
public class UpdateToken {

    private final String token;

    public UpdateToken(UpdateProperties properties) {
        this.token = properties.publishingEnabled() ? properties.token().trim() : null;
    }

    public boolean enabled() {
        return token != null;
    }

    public boolean matches(String presented) {
        return enabled() && Secrets.matches(token, presented);
    }
}
