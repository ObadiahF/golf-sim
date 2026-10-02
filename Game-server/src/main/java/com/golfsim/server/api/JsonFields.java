package com.golfsim.server.api;

import com.fasterxml.jackson.databind.JsonMappingException;
import java.util.Objects;
import java.util.stream.Collectors;

/** Shared by REST and WebSocket error messages. */
public final class JsonFields {

    private JsonFields() {
    }

    /** Dotted JSON field path of a mapping error, e.g. {@code holes} or {@code game.id}; empty when unknown. */
    public static String path(JsonMappingException e) {
        return e.getPath().stream().map(JsonMappingException.Reference::getFieldName)
                .filter(Objects::nonNull).collect(Collectors.joining("."));
    }

    /** {@code Invalid value for '<path>'}, or the fallback when the path is unknown. */
    public static String invalidValue(JsonMappingException e, String fallback) {
        String path = path(e);
        return path.isEmpty() ? fallback : "Invalid value for '" + path + "'";
    }
}
