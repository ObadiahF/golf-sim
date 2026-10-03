package com.golfsim.server.update;

import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import java.nio.file.Path;
import java.time.Duration;
import org.springframework.boot.context.properties.ConfigurationProperties;
import org.springframework.validation.annotation.Validated;

/**
 * Self-update settings bound from {@code updates.*}.
 *
 * @param token        admin token for publishing (GOLF_UPDATE_TOKEN); blank disables every publish endpoint
 * @param dir          where blobs and in-progress uploads are kept (a docker volume)
 * @param keepReleases releases kept per platform; older ones are deleted and their unreferenced blobs collected
 * @param maxPartBytes largest upload chunk (Cloudflare refuses request bodies over 100 MB)
 * @param maxBlobBytes largest file of a release
 * @param gcGrace      an unreferenced blob younger than this survives garbage collection (it may belong to a
 *                     release that is still being published)
 * @param uploadExpiry an unfinished upload untouched for this long is deleted
 */
@Validated
@ConfigurationProperties(prefix = "updates")
public record UpdateProperties(
        String token,
        @NotNull Path dir,
        @Min(1) int keepReleases,
        @Min(1) long maxPartBytes,
        @Min(1) long maxBlobBytes,
        @NotNull Duration gcGrace,
        @NotNull Duration uploadExpiry) {

    public boolean publishingEnabled() {
        return token != null && !token.isBlank();
    }
}
