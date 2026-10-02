package com.golfsim.server.physics;

import java.util.EnumMap;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import org.springframework.context.ApplicationEventPublisher;
import org.springframework.jdbc.core.JdbcTemplate;
import org.springframework.stereotype.Service;
import org.springframework.transaction.annotation.Transactional;

/**
 * The global ball-physics profile in Postgres ({@code physics_overrides}). Every change publishes {@link Changed},
 * which the WebSocket relay sends to every sim and remote once the transaction commits.
 */
@Service
@Transactional
public class PhysicsService {

    /** Advisory-lock id for changes, so each broadcast profile includes every change committed before it. */
    static final long CHANGE_LOCK = 0x9_4751_C5L;

    /** Published after every change, with the profile as it now is. */
    public record Changed(PhysicsProfile profile) {
    }

    private final JdbcTemplate jdbc;
    private final ApplicationEventPublisher events;

    public PhysicsService(JdbcTemplate jdbc, ApplicationEventPublisher events) {
        this.jdbc = jdbc;
        this.events = events;
    }

    @Transactional(readOnly = true)
    public PhysicsProfile current() {
        Map<String, EnumMap<PhysicsField, Double>> overrides = new HashMap<>();
        jdbc.query("select surface, field, value from physics_overrides", row -> {
            String surface = row.getString("surface");
            double value = row.getDouble("value");
            PhysicsField.parse(row.getString("field")).ifPresent(field -> overrides
                    .computeIfAbsent(surface, s -> new EnumMap<>(PhysicsField.class)).put(field, value));
        });
        return PhysicsProfile.of(overrides);
    }

    /** Applies the changes (already validated by {@link PhysicsUpdate}) and returns the new profile. */
    public PhysicsProfile update(List<PhysicsProfile.Change> changes) {
        lock();
        for (PhysicsProfile.Change change : changes) {
            if (change.value() == null) {
                jdbc.update("delete from physics_overrides where surface = ? and field = ?",
                        change.surface(), change.field().wireName());
            } else {
                jdbc.update("""
                        insert into physics_overrides (surface, field, value) values (?, ?, ?)
                        on conflict (surface, field) do update set value = excluded.value, updated_at = now()
                        """, change.surface(), change.field().wireName(), change.value());
            }
        }
        return publish();
    }

    /** Clears every override: the sims go back to their built-in values. */
    public PhysicsProfile reset() {
        lock();
        jdbc.update("delete from physics_overrides");
        return publish();
    }

    private void lock() {
        jdbc.queryForObject("select 1 from pg_advisory_xact_lock(?)", Integer.class, CHANGE_LOCK);
    }

    private PhysicsProfile publish() {
        PhysicsProfile profile = current();
        events.publishEvent(new Changed(profile));
        return profile;
    }
}
