package com.golfsim.server.update;

import static org.assertj.core.api.Assertions.assertThat;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.get;
import static org.springframework.test.web.servlet.request.MockMvcRequestBuilders.post;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.content;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.header;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.jsonPath;
import static org.springframework.test.web.servlet.result.MockMvcResultMatchers.status;

import java.util.Arrays;
import java.util.Random;
import org.junit.jupiter.api.Test;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.http.MediaType;

/** Chunked uploads: missing-blob detection, parts, the size and sha256 check, limits, and Range downloads. */
class BlobUploadTest extends UpdateTestBase {

    @Autowired
    private BlobStore blobs;

    private static byte[] random(int size) {
        byte[] bytes = new byte[size];
        new Random(size).nextBytes(bytes);
        return bytes;
    }

    private void expectMissing(String json, String... missing) throws Exception {
        var result = admin(post("/api/updates/publish/blobs/missing").contentType(MediaType.APPLICATION_JSON).content(json))
                .andExpect(status().isOk()).andExpect(jsonPath("$.missing.length()").value(missing.length));
        for (int i = 0; i < missing.length; i++) {
            result.andExpect(jsonPath("$.missing[" + i + "]").value(missing[i]));
        }
    }

    @Test
    void uploadsInPartsAndReportsWhatIsMissing() throws Exception {
        byte[] big = random(MAX_PART * 2 + 1000);
        byte[] small = random(10);
        String bigSha = sha(big), smallSha = sha(small);
        expectMissing("{\"sha256\":[\"" + bigSha + "\",\"" + smallSha + "\",\"" + bigSha + "\"]}", bigSha, smallSha);

        assertThat(upload(big)).isEqualTo(3);
        expectMissing("{\"sha256\":[\"" + bigSha + "\",\"" + smallSha + "\"]}", smallSha);
        assertThat(blobs.size(bigSha)).isEqualTo(big.length);

        // Completing a stored blob again is harmless; so is an empty file.
        complete(bigSha, big.length, 3).andExpect(status().isOk());
        upload(new byte[0]);
        expectMissing("{\"sha256\":[\"" + sha(new byte[0]) + "\"]}");

        user(get("/api/updates/blobs/" + bigSha)).andExpect(status().isOk())
                .andExpect(header().longValue("Content-Length", big.length))
                .andExpect(header().string("Accept-Ranges", "bytes"))
                .andExpect(header().string("ETag", "\"" + bigSha + "\""))
                .andExpect(content().contentType(MediaType.APPLICATION_OCTET_STREAM))
                .andExpect(content().bytes(big));
    }

    @Test
    void aReSentPartReplacesTheEarlierOne() throws Exception {
        byte[] file = random(100);
        String sha = sha(file);
        putPart(sha, 0, random(7)).andExpect(status().isOk());
        putPart(sha, 0, file).andExpect(status().isOk()).andExpect(jsonPath("$.size").value(100));
        complete(sha, 100, 1).andExpect(status().isOk());
        assertThat(blobs.size(sha)).isEqualTo(100);
    }

    @Test
    void wrongContentSizeOrMissingPartsAreRefused() throws Exception {
        byte[] file = random(MAX_PART + 10);
        String sha = sha(file);
        putPart(sha, 0, Arrays.copyOf(file, MAX_PART)).andExpect(status().isOk());
        complete(sha, file.length, 2).andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Part 1 has not been uploaded"));
        putPart(sha, 1, Arrays.copyOfRange(file, MAX_PART, file.length)).andExpect(status().isOk());
        complete(sha, file.length + 1, 2).andExpect(status().isBadRequest());
        assertThat(blobs.size(sha)).isEqualTo(-1);

        // A mismatch throws the parts away: the publisher starts the file again.
        byte[] part = random(50);
        String other = sha(random(5));
        putPart(other, 0, part).andExpect(status().isOk());
        complete(other, part.length, 1).andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("The parts hash to " + sha(part) + ", not " + other + "; upload the file again"));
        complete(other, part.length, 1).andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.message").value("Part 0 has not been uploaded"));
        assertThat(blobs.size(other)).isEqualTo(-1);
        assertThat(blobs.size(sha)).isEqualTo(-1);
    }

    @Test
    void limitsAndNamesAreChecked() throws Exception {
        String sha = sha(random(1));
        putPart(sha, 0, random(MAX_PART + 1)).andExpect(status().isPayloadTooLarge());
        putPart(sha, -1, random(1)).andExpect(status().isBadRequest());
        putPart(sha, UpdateRequests.MAX_PARTS, random(1)).andExpect(status().isBadRequest());
        putPart(sha.toUpperCase(), 0, random(1)).andExpect(status().isBadRequest());
        putPart("abc", 0, random(1)).andExpect(status().isBadRequest());
        complete(sha, 1, 0).andExpect(status().isBadRequest());
        complete("..", 1, 1).andExpect(status().isBadRequest());
        admin(post("/api/updates/publish/blobs/missing").contentType(MediaType.APPLICATION_JSON)
                .content("{\"sha256\":[\"../../etc/passwd\"]}"))
                .andExpect(status().isBadRequest())
                .andExpect(jsonPath("$.fieldErrors['sha256[0]']").value("must be 64 lowercase hex characters"));
        user(get("/api/updates/blobs/" + sha)).andExpect(status().isNotFound());
        user(get("/api/updates/blobs/nothex")).andExpect(status().isBadRequest());
    }

    @Test
    void downloadsHonourRanges() throws Exception {
        byte[] file = random(1000);
        String sha = sha(file);
        upload(file);
        String url = "/api/updates/blobs/" + sha;
        user(get(url).header("Range", "bytes=10-19")).andExpect(status().isPartialContent())
                .andExpect(header().string("Content-Range", "bytes 10-19/1000"))
                .andExpect(header().longValue("Content-Length", 10))
                .andExpect(content().bytes(Arrays.copyOfRange(file, 10, 20)));
        // Resuming: everything from byte 600.
        user(get(url).header("Range", "bytes=600-")).andExpect(status().isPartialContent())
                .andExpect(header().string("Content-Range", "bytes 600-999/1000"))
                .andExpect(content().bytes(Arrays.copyOfRange(file, 600, 1000)));
        user(get(url).header("Range", "bytes=1000-")).andExpect(status().isRequestedRangeNotSatisfiable())
                .andExpect(header().string("Content-Range", "bytes */1000"));
    }
}
