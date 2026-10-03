package com.golfsim.server.update;

import com.fasterxml.jackson.annotation.JsonInclude;
import java.time.Instant;
import java.util.List;

/**
 * A release as the sim reads it ({@code GET /api/updates/latest}). {@code files} is the whole game folder: the
 * updater downloads the files whose sha256 differs from what is installed and deletes the ones not listed. The
 * release list ({@code GET /api/updates/releases}) leaves {@code files} out.
 */
@JsonInclude(JsonInclude.Include.NON_NULL)
public record ReleaseView(
        String platform,
        String version,
        long build,
        String note,
        Instant createdAt,
        long totalSize,
        int fileCount,
        List<FileView> files) {

    /** One file; {@code executable} only matters on macOS (the updater sets the x bit). */
    public record FileView(String path, long size, String sha256, boolean executable) {
    }
}
