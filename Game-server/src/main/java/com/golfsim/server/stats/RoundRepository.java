package com.golfsim.server.stats;

import com.golfsim.server.game.Game;
import java.util.List;
import org.springframework.data.jpa.repository.Query;
import org.springframework.data.repository.Repository;

public interface RoundRepository extends Repository<Game, Long> {

    /** Every (player, game) pair with summed strokes and par, newest game first. */
    @Query("""
            select new com.golfsim.server.stats.PlayerRound(
                p.name, g.id, g.status, g.courseName, g.holesCount, g.createdAt,
                count(s.id), coalesce(sum(s.strokes), 0L), coalesce(sum(s.par), 0L))
            from Game g join g.players p
            left join HoleScore s on s.game = g and s.player = p
            group by p.name, g.id, g.status, g.courseName, g.holesCount, g.createdAt
            order by g.createdAt desc, g.id desc
            """)
    List<PlayerRound> findAllRounds();

    /**
     * Per player: holes played and how many were aces, eagles, birdies, pars, bogeys, double bogeys or worse.
     * The categories don't overlap: a hole in one is only an ace, even on a par 1 or par 2.
     */
    @Query("""
            select new com.golfsim.server.stats.HoleTally(
                p.name, count(s.id),
                sum(case when s.strokes = 1 then 1L else 0L end),
                sum(case when s.strokes > 1 and s.strokes - s.par <= -2 then 1L else 0L end),
                sum(case when s.strokes > 1 and s.strokes - s.par = -1 then 1L else 0L end),
                sum(case when s.strokes > 1 and s.strokes = s.par then 1L else 0L end),
                sum(case when s.strokes - s.par = 1 then 1L else 0L end),
                sum(case when s.strokes - s.par >= 2 then 1L else 0L end))
            from HoleScore s join s.player p
            group by p.name
            """)
    List<HoleTally> findHoleTallies();
}
