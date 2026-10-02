package com.golfsim.server.physics;

import java.util.Arrays;
import java.util.Optional;

/**
 * A tunable part of a surface's ground response (the sim's {@code BallPhysicsSettings.SurfaceResponse}) and the range
 * the server accepts for it. The ranges only keep values sane; the sim's built-in values sit well inside them.
 */
public enum PhysicsField {
    /** Rolling resistance as a fraction of g (green 0.06 is about Stimp 9.3; lower rolls farther). */
    ROLLING(0.02, 3.0),
    /** Bounce energy kept relative to a firm green (1). */
    RESTITUTION(0.0, 1.2),
    /** Sliding friction during a bounce: how much the turf grabs the ball. */
    FRICTION(0.0, 1.5);

    private final double min;
    private final double max;

    PhysicsField(double min, double max) {
        this.min = min;
        this.max = max;
    }

    public String wireName() {
        return name().toLowerCase();
    }

    public boolean accepts(double value) {
        return value >= min && value <= max;
    }

    /** The validation message for a value outside the range, e.g. {@code must be between 0.02 and 3}. */
    public String rangeMessage() {
        return "must be between " + format(min) + " and " + format(max);
    }

    public static Optional<PhysicsField> parse(String wireName) {
        return Arrays.stream(values()).filter(f -> f.wireName().equals(wireName)).findFirst();
    }

    private static String format(double value) {
        return value == Math.rint(value) ? String.valueOf((long) value) : String.valueOf(value);
    }
}
