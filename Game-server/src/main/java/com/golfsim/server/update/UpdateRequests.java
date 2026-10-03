package com.golfsim.server.update;

import com.golfsim.server.game.GameRequests;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import jakarta.validation.constraints.NotNull;
import jakarta.validation.constraints.Pattern;
import jakarta.validation.constraints.PositiveOrZero;
import jakarta.validation.constraints.Size;
import java.util.List;

/** Request bodies of the publish endpoints ({@code /api/updates/publish/...}, admin token). */
public final class UpdateRequests {

    /** Highest part number + 1 of one blob upload. */
    public static final int MAX_PARTS = 10_000;

    static final String SHA256 = "[0-9a-f]{64}";
    static final String SHA256_MESSAGE = "must be 64 lowercase hex characters";

    private UpdateRequests() {
    }

    /** One file of a release, relative to the game folder ({@link ManifestRules} checks the path). */
    public record ReleaseFile(
            @NotNull String path,
            @NotNull @PositiveOrZero Long size,
            @NotNull @Pattern(regexp = SHA256, message = SHA256_MESSAGE) String sha256,
            Boolean executable) {
    }

    /** {@code POST /api/updates/publish/releases}: becomes the platform's latest release. */
    public record Publish(
            @NotNull @Pattern(regexp = ManifestRules.PLATFORM, message = "must be like windows-x64 or macos") String platform,
            @NotNull @Pattern(regexp = ManifestRules.VERSION, message = "must be 1-64 of A-Z a-z 0-9 . _ + -")
            String version,
            @NotNull @Min(1) Long build,
            @Size(max = 500) @Pattern(regexp = GameRequests.PLAIN_TEXT, message = "must not contain control or invalid characters")
            String note,
            @NotNull @Size(min = 1, max = ManifestRules.MAX_FILES) List<@NotNull @Valid ReleaseFile> files) {
    }

    /** {@code POST /api/updates/publish/blobs/missing}: which of these the server doesn't have yet. */
    public record MissingBlobs(
            @NotNull @Size(max = ManifestRules.MAX_FILES)
            List<@NotNull @Pattern(regexp = SHA256, message = SHA256_MESSAGE) String> sha256) {
    }

    /** {@code POST /api/updates/publish/blobs/{sha256}/complete}: parts 0..parts-1 make a file of this size. */
    public record CompleteBlob(
            @NotNull @PositiveOrZero Long size,
            @NotNull @Min(1) @Max(MAX_PARTS) Integer parts) {
    }
}
