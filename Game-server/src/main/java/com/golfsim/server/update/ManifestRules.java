package com.golfsim.server.update;

import java.util.HashMap;
import java.util.HashSet;
import java.util.List;
import java.util.Locale;
import java.util.Map;
import java.util.Set;
import java.util.regex.Pattern;

/**
 * What a release may contain. A manifest path is applied by the updater on the player's PC under the game folder,
 * so anything that could land outside it, or mean different files on Windows and macOS, is refused: absolute paths,
 * drive letters, backslashes, {@code .} / {@code ..} / empty segments, characters Windows forbids (including
 * {@code :} for alternate data streams), segments ending in a dot or space (Windows drops them), reserved device
 * names, and two paths that differ only in case or where one is a folder of the other.
 */
public final class ManifestRules {

    public static final int MAX_FILES = 20_000;
    public static final int MAX_PATH = 400;
    /** Platform names, e.g. windows-x64 or macos. */
    public static final String PLATFORM = "[a-z0-9][a-z0-9-]{0,31}";
    /** Version strings, e.g. 2026.10.02-1754-2135252. */
    public static final String VERSION = "[A-Za-z0-9][A-Za-z0-9._+-]{0,63}";

    private static final Pattern FORBIDDEN = Pattern.compile("[\\\\:*?\"<>|\\p{Cc}\\p{Cs}]");
    private static final Pattern RESERVED = Pattern.compile("(con|prn|aux|nul|com[0-9]|lpt[0-9])(\\..*)?");

    private ManifestRules() {
    }

    /** Why this path can't be in a manifest, or null when it is fine. */
    public static String pathProblem(String path) {
        if (path == null || path.isEmpty()) {
            return "must not be empty";
        }
        if (path.length() > MAX_PATH) {
            return "must be at most " + MAX_PATH + " characters";
        }
        if (path.startsWith("/")) {
            return "must be relative to the game folder";
        }
        if (FORBIDDEN.matcher(path).find()) {
            return "must use / and no \\ : * ? \" < > | or control characters";
        }
        for (String segment : path.split("/", -1)) {
            if (segment.isEmpty() || segment.equals(".") || segment.equals("..")) {
                return "must not contain empty, . or .. segments";
            }
            if (segment.endsWith(".") || segment.endsWith(" ") || segment.startsWith(" ")) {
                return "segments must not start with a space or end with a dot or space";
            }
            if (RESERVED.matcher(segment.toLowerCase(Locale.ROOT)).matches()) {
                return "must not use a reserved Windows name";
            }
        }
        return null;
    }

    /**
     * Problems with the paths taken together, keyed by index: a path equal to another ignoring case (the same file
     * on Windows and macOS), or a path that is also used as a folder of another path.
     */
    public static Map<Integer, String> conflicts(List<String> paths) {
        Map<Integer, String> problems = new HashMap<>();
        Map<String, Integer> seen = new HashMap<>();
        Set<String> folders = new HashSet<>();
        for (int i = 0; i < paths.size(); i++) {
            String key = paths.get(i).toLowerCase(Locale.ROOT);
            Integer first = seen.putIfAbsent(key, i);
            if (first != null) {
                problems.put(i, "duplicate of files[" + first + "] (paths are compared ignoring case)");
            }
            for (int slash = key.indexOf('/'); slash >= 0; slash = key.indexOf('/', slash + 1)) {
                folders.add(key.substring(0, slash));
            }
        }
        seen.forEach((key, index) -> {
            if (folders.contains(key)) {
                problems.putIfAbsent(index, "is a file and also a folder of another path");
            }
        });
        return problems;
    }
}
