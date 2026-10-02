package com.golfsim.server.game;

import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;

public interface PlayerRepository extends JpaRepository<Player, Long> {

    Optional<Player> findByNameKey(String nameKey);

    /** The player with this name, matched by {@link Names#key}. */
    default Optional<Player> findByName(String name) {
        return findByNameKey(Names.key(name));
    }
}
