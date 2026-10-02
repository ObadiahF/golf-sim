package com.golfsim.server.game;

import java.util.Collection;
import java.util.List;
import java.util.Optional;
import org.springframework.data.jpa.repository.JpaRepository;

public interface HoleScoreRepository extends JpaRepository<HoleScore, Long> {

    Optional<HoleScore> findByGameAndPlayerAndHoleNumber(Game game, Player player, int holeNumber);

    List<HoleScore> findByGameIn(Collection<Game> games);

    List<HoleScore> findByGame(Game game);

    long countByGame(Game game);
}
