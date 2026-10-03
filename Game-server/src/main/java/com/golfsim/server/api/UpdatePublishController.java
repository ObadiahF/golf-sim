package com.golfsim.server.api;

import com.golfsim.server.update.BlobStore;
import com.golfsim.server.update.ReleaseView;
import com.golfsim.server.update.UpdateRequests;
import com.golfsim.server.update.UpdateService;
import jakarta.servlet.http.HttpServletRequest;
import jakarta.validation.Valid;
import jakarta.validation.constraints.Max;
import jakarta.validation.constraints.Min;
import java.io.IOException;
import java.util.List;
import java.util.Map;
import org.springframework.http.HttpStatus;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.PostMapping;
import org.springframework.web.bind.annotation.PutMapping;
import org.springframework.web.bind.annotation.RequestBody;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.ResponseStatus;
import org.springframework.web.bind.annotation.RestController;

/**
 * Self-update, the publisher's side: admin token only ({@code GOLF_UPDATE_TOKEN}, checked by the auth filter for
 * everything under this path; 404 while it isn't set). Upload flow: ask which files are missing, PUT each missing
 * file in parts (each at most {@code updates.max-part-bytes}, under Cloudflare's 100 MB body limit), complete it,
 * then create the release.
 */
@RestController
@RequestMapping("/api/updates/publish")
public class UpdatePublishController {

    private final UpdateService updates;
    private final BlobStore blobs;

    public UpdatePublishController(UpdateService updates, BlobStore blobs) {
        this.updates = updates;
        this.blobs = blobs;
    }

    /** Which of these files the server doesn't have yet: {@code {"missing": [...]}}. */
    @PostMapping("/blobs/missing")
    public Map<String, List<String>> missing(@Valid @RequestBody UpdateRequests.MissingBlobs request) {
        return Map.of("missing", updates.missing(request.sha256()));
    }

    /** One part of a file, as the raw request body (any content type); re-sending a part replaces it. */
    @PutMapping("/blobs/{sha256}/parts/{part}")
    public Map<String, Object> part(@PathVariable String sha256,
            @PathVariable @Min(0) @Max(UpdateRequests.MAX_PARTS - 1) int part, HttpServletRequest request)
            throws IOException {
        long size = blobs.writePart(sha256, part, request.getContentLengthLong(), request.getInputStream());
        return Map.of("sha256", sha256, "part", part, "size", size);
    }

    /** Joins the parts and checks size and sha256; the file is then stored. */
    @PostMapping("/blobs/{sha256}/complete")
    public Map<String, Object> complete(@PathVariable String sha256,
            @Valid @RequestBody UpdateRequests.CompleteBlob request) throws IOException {
        blobs.complete(sha256, request.size(), request.parts());
        return Map.of("sha256", sha256, "size", request.size());
    }

    /** Creates the release (every file must be uploaded); it is the platform's latest from now on. */
    @PostMapping("/releases")
    @ResponseStatus(HttpStatus.CREATED)
    public ReleaseView publish(@Valid @RequestBody UpdateRequests.Publish request) {
        return updates.publish(request);
    }
}
