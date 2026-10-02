package com.golfsim.server.physics;

import com.fasterxml.jackson.databind.JsonNode;
import com.golfsim.server.api.InvalidFieldsException;
import java.util.ArrayList;
import java.util.HashSet;
import java.util.Iterator;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import java.util.Optional;
import java.util.Set;
import org.springframework.http.HttpStatus;
import org.springframework.web.server.ResponseStatusException;

/**
 * Reads a {@code PUT /api/physics} body, {@code {"surfaces": [{"surface": "green", "rolling": 0.07, "friction": null}]}},
 * into changes. A number sets the field, {@code null} clears it (back to the game's value), an absent field is left
 * as it is. Unknown keys, surfaces and fields are refused rather than ignored, so a typo can't silently do nothing;
 * every problem is reported at once in {@code fieldErrors}, keyed like {@code surfaces[0].rolling}.
 */
public final class PhysicsUpdate {

    private static final String SURFACES = "surfaces";
    private static final String SURFACE = "surface";

    private PhysicsUpdate() {
    }

    public static List<PhysicsProfile.Change> parse(JsonNode body) {
        if (body == null || !body.isObject()) {
            throw new ResponseStatusException(HttpStatus.BAD_REQUEST, "Expected a JSON object");
        }
        Map<String, String> errors = new LinkedHashMap<>();
        body.fieldNames().forEachRemaining(key -> {
            if (!key.equals(SURFACES)) {
                errors.put(key, "unknown field (allowed: " + SURFACES + ")");
            }
        });
        JsonNode surfaces = body.path(SURFACES);
        List<PhysicsProfile.Change> changes = new ArrayList<>();
        if (!surfaces.isArray()) {
            errors.put(SURFACES, surfaces.isMissingNode() || surfaces.isNull()
                    ? "must not be null (reset with DELETE /api/physics)" : "must be an array");
        } else {
            Set<String> seen = new HashSet<>();
            for (int i = 0; i < surfaces.size(); i++) {
                readSurface(surfaces.get(i), SURFACES + "[" + i + "]", seen, changes, errors);
            }
        }
        if (!errors.isEmpty()) {
            throw new InvalidFieldsException(errors);
        }
        return changes;
    }

    private static void readSurface(JsonNode entry, String path, Set<String> seen, List<PhysicsProfile.Change> changes,
            Map<String, String> errors) {
        if (!entry.isObject()) {
            errors.put(path, "must be an object");
            return;
        }
        Optional<String> surface = surfaceName(entry.path(SURFACE), path + "." + SURFACE, seen, errors);
        for (Iterator<Map.Entry<String, JsonNode>> it = entry.fields(); it.hasNext(); ) {
            Map.Entry<String, JsonNode> field = it.next();
            if (field.getKey().equals(SURFACE)) {
                continue;
            }
            String fieldPath = path + "." + field.getKey();
            Optional<PhysicsField> known = PhysicsField.parse(field.getKey());
            if (known.isEmpty()) {
                errors.put(fieldPath, "unknown field (allowed: surface, rolling, restitution, friction)");
                continue;
            }
            JsonNode value = field.getValue();
            if (value.isNull()) {
                surface.ifPresent(s -> changes.add(new PhysicsProfile.Change(s, known.get(), null)));
            } else if (!value.isNumber() || !Double.isFinite(value.doubleValue())) {
                errors.put(fieldPath, "must be a finite number or null");
            } else if (!known.get().accepts(value.doubleValue())) {
                errors.put(fieldPath, known.get().rangeMessage());
            } else {
                surface.ifPresent(s -> changes.add(new PhysicsProfile.Change(s, known.get(), value.doubleValue())));
            }
        }
    }

    private static Optional<String> surfaceName(JsonNode node, String path, Set<String> seen, Map<String, String> errors) {
        if (!node.isTextual()) {
            errors.put(path, "must be one of " + String.join(", ", PhysicsProfile.SURFACES));
            return Optional.empty();
        }
        String name = node.asText();
        if (!PhysicsProfile.SURFACES.contains(name)) {
            errors.put(path, "unknown surface '" + name + "' (one of " + String.join(", ", PhysicsProfile.SURFACES) + ")");
            return Optional.empty();
        }
        if (!seen.add(name)) {
            errors.put(path, "duplicate surface '" + name + "'");
            return Optional.empty();
        }
        return Optional.of(name);
    }
}
