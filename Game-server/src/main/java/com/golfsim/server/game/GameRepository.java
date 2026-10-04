package com.golfsim.server.game;

import jakarta.persistence.LockModeType;
import java.util.List;
import java.util.Optional;
import org.springframework.data.domain.Limit;
import org.springframework.data.jpa.repository.JpaRepository;
import org.springframework.data.jpa.repository.Lock;
import org.springframework.data.jpa.repository.Query;

public interface GameRepository extends JpaRepository<Game, Long> {

    Optional<Game> findFirstByRoomAndStatus(String room, GameStatus status);

    List<Game> findAllByOrderByCreatedAtDescIdDesc(Limit limit);

    /** {@code SELECT ... FOR UPDATE}: serialises every change to one game (scores, completion, end). */
    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select g from Game g where g.id = :id")
    Optional<Game> findByIdForUpdate(long id);

    @Lock(LockModeType.PESSIMISTIC_WRITE)
    @Query("select g from Game g where g.room = :room and g.status = :status")
    List<Game> findByRoomAndStatusForUpdate(String room, GameStatus status);

    /** Transaction-scoped Postgres advisory lock; serialises game starts (and the player rows they create). */
    @Query(value = "select 1 from pg_advisory_xact_lock(:key)", nativeQuery = true)
    Integer advisoryLock(long key);
}
