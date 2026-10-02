package com.golfsim.server.ws;

/**
 * Joins the partial text frames of one WebSocket message, keeping at most {@link #MAX_CHARS} characters, so a large
 * message is answered with an {@code error} instead of the container closing the connection (1009). One per session;
 * the container delivers a session's frames one at a time, in order.
 */
final class MessageAssembler {

    static final int MAX_CHARS = 64 * 1024;

    private final StringBuilder buffer = new StringBuilder();
    private boolean tooLarge;

    /** Adds one part; true when it was the last part and {@link #take} has the whole message. */
    boolean add(String part, boolean last) {
        if (!tooLarge && buffer.length() + part.length() <= MAX_CHARS) {
            buffer.append(part);
        } else {
            tooLarge = true;
            buffer.setLength(0);
        }
        return last;
    }

    /** The whole message, or null when it was longer than {@link #MAX_CHARS}; resets for the next message. */
    String take() {
        String message = tooLarge ? null : buffer.toString();
        buffer.setLength(0);
        tooLarge = false;
        return message;
    }
}
