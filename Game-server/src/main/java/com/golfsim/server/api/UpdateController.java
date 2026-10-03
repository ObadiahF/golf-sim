package com.golfsim.server.api;

import com.golfsim.server.update.BlobStore;
import com.golfsim.server.update.ManifestRules;
import com.golfsim.server.update.ReleaseView;
import com.golfsim.server.update.UpdateService;
import jakarta.validation.constraints.Pattern;
import java.nio.file.Path;
import java.time.Duration;
import java.util.List;
import org.springframework.core.io.FileSystemResource;
import org.springframework.core.io.Resource;
import org.springframework.http.CacheControl;
import org.springframework.http.HttpStatus;
import org.springframework.http.MediaType;
import org.springframework.http.ResponseEntity;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RequestParam;
import org.springframework.web.bind.annotation.RestController;
import org.springframework.web.server.ResponseStatusException;

/**
 * Self-update, the sim's side (shared token): the latest release of a platform and its files. Blob downloads honour
 * {@code Range} (206, or 416 past the end), so an interrupted download resumes where it stopped.
 */
@RestController
@RequestMapping("/api/updates")
public class UpdateController {

    private static final String PLATFORM_MESSAGE = "must be like windows-x64 or macos";

    private final UpdateService updates;
    private final BlobStore blobs;

    public UpdateController(UpdateService updates, BlobStore blobs) {
        this.updates = updates;
        this.blobs = blobs;
    }

    /** The newest release with its manifest, or 204 No Content when the platform has none. */
    @GetMapping("/latest")
    public ResponseEntity<ReleaseView> latest(
            @RequestParam @Pattern(regexp = ManifestRules.PLATFORM, message = PLATFORM_MESSAGE) String platform) {
        return updates.latest(platform).map(ResponseEntity::ok).orElseGet(() -> ResponseEntity.noContent().build());
    }

    /** The kept releases, newest first, without manifests. */
    @GetMapping("/releases")
    public List<ReleaseView> releases(
            @RequestParam @Pattern(regexp = ManifestRules.PLATFORM, message = PLATFORM_MESSAGE) String platform) {
        return updates.releases(platform);
    }

    /** One file by its sha256; the content never changes, so it may be cached for good. */
    @GetMapping("/blobs/{sha256}")
    public ResponseEntity<Resource> blob(@PathVariable String sha256) {
        Path file = blobs.find(sha256)
                .orElseThrow(() -> new ResponseStatusException(HttpStatus.NOT_FOUND, "No such file"));
        return ResponseEntity.ok()
                .contentType(MediaType.APPLICATION_OCTET_STREAM)
                .eTag(sha256)
                .cacheControl(CacheControl.maxAge(Duration.ofDays(365)).cachePrivate().immutable())
                .body(new FileSystemResource(file));
    }
}
