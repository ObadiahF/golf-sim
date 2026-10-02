package com.golfsim.server.ws;

import com.fasterxml.jackson.annotation.JsonValue;
import java.util.Arrays;
import java.util.Optional;

/** Who is on the other end of a WebSocket: the Unity sim or a phone remote. */
public enum Role {
    SIM,
    REMOTE;

    @JsonValue
    public String wireName() {
        return name().toLowerCase();
    }

    public static Optional<Role> parse(String value) {
        return Arrays.stream(values()).filter(r -> r.wireName().equalsIgnoreCase(value)).findFirst();
    }
}
