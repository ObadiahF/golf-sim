package com.golfsim.server.auth;

import java.nio.charset.StandardCharsets;
import java.security.MessageDigest;

/** Constant-time comparison of a presented secret with the expected one. */
public final class Secrets {

    private Secrets() {
    }

    public static boolean matches(String expected, String presented) {
        return expected != null && presented != null && MessageDigest.isEqual(
                expected.getBytes(StandardCharsets.UTF_8), presented.getBytes(StandardCharsets.UTF_8));
    }
}
