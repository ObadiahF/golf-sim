package com.golfsim.server.game;

import java.util.Locale;
import java.util.Optional;
import java.util.regex.Pattern;

/**
 * Room codes: one sim and its phones per room, so two sims on one server don't fight over the same phones.
 * A code is 4 to 8 letters or digits, case-insensitive (stored and sent upper case, trimmed). Clients that send no
 * room (older sims and apps) share the default room, {@value #DEFAULT} on the wire.
 */
public final class Rooms {

    public static final String DEFAULT = "";
    public static final int MAX_LENGTH = 8;
    public static final String RULE = "must be 4 to " + MAX_LENGTH + " letters or digits";

    private static final Pattern CODE = Pattern.compile("[A-Z0-9]{4," + MAX_LENGTH + "}");

    private Rooms() {
    }

    /** The normalised code ({@link #DEFAULT} for null or blank), or empty when {@code raw} is not a valid code. */
    public static Optional<String> parse(String raw) {
        String code = raw == null ? DEFAULT : raw.strip().toUpperCase(Locale.ROOT);
        return code.isEmpty() || CODE.matcher(code).matches() ? Optional.of(code) : Optional.empty();
    }

    /** "room K7QF", or "this server" for the default room; for messages such as "Another sim is already connected to ...". */
    public static String describe(String room) {
        return DEFAULT.equals(room) ? "this server" : "room " + room;
    }
}
