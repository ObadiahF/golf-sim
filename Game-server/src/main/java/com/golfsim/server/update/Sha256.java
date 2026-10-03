package com.golfsim.server.update;

import java.security.MessageDigest;
import java.security.NoSuchAlgorithmException;
import java.util.HexFormat;
import java.util.regex.Pattern;

/** SHA-256 digests as the update API writes them: 64 lowercase hex characters. */
public final class Sha256 {

    private static final Pattern HEX = Pattern.compile("[0-9a-f]{64}");

    private Sha256() {
    }

    /** True for a well-formed digest; anything else is never used to build a file name. */
    public static boolean isValid(String hex) {
        return hex != null && HEX.matcher(hex).matches();
    }

    public static MessageDigest digest() {
        try {
            return MessageDigest.getInstance("SHA-256");
        } catch (NoSuchAlgorithmException e) {
            throw new IllegalStateException(e);
        }
    }

    public static String hex(MessageDigest digest) {
        return HexFormat.of().formatHex(digest.digest());
    }
}
