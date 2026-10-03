package com.golfsim.server;

import com.golfsim.server.update.UpdateProperties;
import java.io.IOException;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.Comparator;
import java.util.stream.Stream;
import org.junit.jupiter.api.BeforeEach;
import org.springframework.beans.factory.annotation.Autowired;
import org.springframework.boot.test.autoconfigure.web.servlet.AutoConfigureMockMvc;
import org.springframework.boot.test.context.SpringBootTest;
import org.springframework.jdbc.core.JdbcTemplate;

/**
 * Base for tests that need the full app and real Postgres (DB_URL; the compose {@code test} service points it at
 * the golf_test database). Every test starts with empty tables and an empty update store; publishing takes
 * {@link #UPDATE_TOKEN} and upload parts are small ({@link #MAX_PART} bytes) so limits are cheap to test.
 */
@SpringBootTest(webEnvironment = SpringBootTest.WebEnvironment.RANDOM_PORT, properties = {
        "updates.token=" + IntegrationTest.UPDATE_TOKEN,
        "updates.dir=${java.io.tmpdir}/golf-updates-test",
        "updates.max-part-bytes=" + IntegrationTest.MAX_PART})
@AutoConfigureMockMvc
public abstract class IntegrationTest {

    public static final String TOKEN = "golf-sim-dev-token";
    public static final String UPDATE_TOKEN = "test-update-token";
    public static final int MAX_PART = 64 * 1024;

    @Autowired
    protected JdbcTemplate jdbc;

    @Autowired
    protected UpdateProperties updateProperties;

    @BeforeEach
    void cleanDatabase() throws IOException {
        jdbc.execute("truncate hole_scores, game_players, games, players, physics_overrides, update_releases, "
                + "update_files restart identity cascade");
        Path updates = updateProperties.dir();
        try (Stream<Path> files = Files.walk(updates)) {
            for (Path file : (Iterable<Path>) files.sorted(Comparator.reverseOrder())::iterator) {
                if (!file.equals(updates) && Files.isRegularFile(file)) {
                    Files.delete(file);
                }
            }
        }
    }
}
