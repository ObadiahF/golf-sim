package com.golfsim.server.game;

import java.text.Normalizer;
import java.util.Locale;
import java.util.Optional;

/**
 * Rules for player and device names, shared by REST, WebSocket and storage.
 *
 * <ul>
 *   <li>Names are NFKC-normalised, trimmed, and runs of spaces become one space.</li>
 *   <li>Length is counted in Unicode code points (an emoji counts as 1), {@value #MAX_LENGTH} max.</li>
 *   <li>Control, format (invisible), private-use, unassigned and lone surrogate code points are not allowed,
 *       except the zero-width joiner inside emoji sequences; a name needs at least one visible character.</li>
 *   <li>Two names are the same player when their {@link #key} matches: a full case fold, so "İvan", "ivan",
 *       "IVAN" and "ıvan" are one player, as are "Straße" and "STRASSE".</li>
 * </ul>
 */
public final class Names {

    public static final int MAX_LENGTH = 40;
    public static final String RULE = "must be 1 to " + MAX_LENGTH
            + " characters with at least one visible character and no control or invisible characters";

    private static final int ZERO_WIDTH_JOINER = 0x200D;

    private Names() {
    }

    /** NFKC, trimmed, runs of space characters collapsed to one space. Null stays null. */
    public static String normalize(String raw) {
        if (raw == null) {
            return null;
        }
        return Normalizer.normalize(raw, Normalizer.Form.NFKC).strip().replaceAll("\\p{Zs}+", " ");
    }

    /** Why {@code raw} is not a valid player name, or empty when it is. */
    public static Optional<String> problem(String raw) {
        String name = normalize(raw);
        if (name == null || name.isEmpty() || name.codePointCount(0, name.length()) > MAX_LENGTH
                || name.codePoints().anyMatch(cp -> !allowed(cp)) || name.codePoints().noneMatch(Names::visible)) {
            return Optional.of(RULE);
        }
        return Optional.empty();
    }

    /** Case-insensitive identity of a name; stored in {@code players.name_key} (unique). */
    public static String key(String name) {
        String folded = normalize(name).toUpperCase(Locale.ROOT).toLowerCase(Locale.ROOT);
        // Lower-casing "İ" gives "i" + combining dot above; fold it to a plain "i".
        return Normalizer.normalize(folded.replace("i̇", "i"), Normalizer.Form.NFKC);
    }

    /**
     * A WebSocket device name: normalised, disallowed code points dropped, cut to {@value #MAX_LENGTH} code points
     * (never inside a surrogate pair). Null when nothing visible is left.
     */
    public static String deviceName(String raw) {
        String name = normalize(raw);
        if (name == null) {
            return null;
        }
        StringBuilder out = new StringBuilder();
        name.codePoints().filter(Names::allowed).limit(MAX_LENGTH).forEach(out::appendCodePoint);
        String cleaned = normalize(out.toString());
        return cleaned.codePoints().anyMatch(Names::visible) ? cleaned : null;
    }

    private static boolean allowed(int cp) {
        return switch (Character.getType(cp)) {
            case Character.CONTROL, Character.PRIVATE_USE, Character.UNASSIGNED, Character.SURROGATE,
                 Character.LINE_SEPARATOR, Character.PARAGRAPH_SEPARATOR -> false;
            case Character.FORMAT -> cp == ZERO_WIDTH_JOINER;
            default -> true;
        };
    }

    /** Letters, digits, punctuation and symbols (emoji); not spaces or combining marks alone. */
    private static boolean visible(int cp) {
        return switch (Character.getType(cp)) {
            case Character.SPACE_SEPARATOR, Character.NON_SPACING_MARK, Character.ENCLOSING_MARK,
                 Character.COMBINING_SPACING_MARK, Character.FORMAT, Character.CONTROL -> false;
            default -> allowed(cp);
        };
    }
}
