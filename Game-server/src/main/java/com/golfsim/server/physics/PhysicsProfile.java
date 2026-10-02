package com.golfsim.server.physics;

import com.fasterxml.jackson.annotation.JsonInclude;
import java.util.EnumMap;
import java.util.List;
import java.util.Map;

/**
 * The global ball-physics profile: per surface, the fields that override the sim's built-in ground response. A field
 * that is absent uses the game's own value, so the server never needs to know the defaults. Returned by
 * {@code /api/physics}, sent in the WebSocket {@code physics} message and in {@code hello.physics}.
 *
 * @param surfaces every tunable surface, in {@link #SURFACES} order, each with only its overridden fields
 */
public record PhysicsProfile(List<SurfaceValues> surfaces) {

    /** The sim's tunable surfaces ({@code BallPhysicsSettings.DefaultSurfaces()}; water is a hazard, not tunable). */
    public static final List<String> SURFACES =
            List.of("green", "fairway", "tee", "rough", "native", "scrub", "woods", "bunker");

    /** One surface's overrides; null fields are left out of the JSON (Unity's JsonUtility reads null as 0). */
    @JsonInclude(JsonInclude.Include.NON_NULL)
    public record SurfaceValues(String surface, Double rolling, Double restitution, Double friction) {

        static SurfaceValues of(String surface, Map<PhysicsField, Double> values) {
            return new SurfaceValues(surface, values.get(PhysicsField.ROLLING), values.get(PhysicsField.RESTITUTION),
                    values.get(PhysicsField.FRICTION));
        }
    }

    /** A profile from stored overrides keyed by surface; surfaces without any are listed with no fields. */
    static PhysicsProfile of(Map<String, EnumMap<PhysicsField, Double>> overrides) {
        return new PhysicsProfile(SURFACES.stream()
                .map(s -> SurfaceValues.of(s, overrides.getOrDefault(s, new EnumMap<>(PhysicsField.class))))
                .toList());
    }

    /** One change from {@code PUT /api/physics}: set {@code field} of {@code surface}, or clear it when value is null. */
    public record Change(String surface, PhysicsField field, Double value) {
    }
}
