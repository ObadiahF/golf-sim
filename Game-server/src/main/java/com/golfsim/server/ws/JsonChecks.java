package com.golfsim.server.ws;

import com.fasterxml.jackson.core.JsonProcessingException;
import com.fasterxml.jackson.databind.JsonNode;
import java.util.Iterator;
import java.util.Map;
import java.util.Optional;

/** Checks on a parsed message that bean validation can't express, and client-safe parse error messages. */
final class JsonChecks {

    private static final String NON_NUMERIC = "Non-standard token";
    private static final String DUPLICATE = "Duplicate field";

    private JsonChecks() {
    }

    /**
     * A parse error worded for the client without Jackson internals: {@code Invalid number} for {@code NaN} /
     * {@code Infinity} tokens, {@code Invalid JSON: Duplicate field '<key>'}, otherwise {@code Invalid JSON}.
     */
    static String parseError(JsonProcessingException e) {
        String detail = String.valueOf(e.getOriginalMessage());
        if (detail.startsWith(NON_NUMERIC)) {
            return "Invalid number";
        }
        return detail.startsWith(DUPLICATE) ? "Invalid JSON: " + detail : "Invalid JSON";
    }

    /**
     * The dotted path of the first number that overflowed to infinity (e.g. {@code 1e400}), or empty. Such
     * values would pass {@code @Positive} and reach the other side through the byte-for-byte relay.
     */
    static Optional<String> nonFiniteNumber(JsonNode node) {
        return find(node, "");
    }

    private static Optional<String> find(JsonNode node, String path) {
        if (node.isNumber()) {
            return Double.isFinite(node.doubleValue()) ? Optional.empty() : Optional.of(path);
        }
        if (node.isArray()) {
            for (int i = 0; i < node.size(); i++) {
                Optional<String> found = find(node.get(i), path + "[" + i + "]");
                if (found.isPresent()) {
                    return found;
                }
            }
        }
        for (Iterator<Map.Entry<String, JsonNode>> it = node.fields(); it.hasNext(); ) {
            Map.Entry<String, JsonNode> field = it.next();
            Optional<String> found = find(field.getValue(), path.isEmpty() ? field.getKey() : path + "." + field.getKey());
            if (found.isPresent()) {
                return found;
            }
        }
        return Optional.empty();
    }
}
