package com.golfsim.server.api;

import com.golfsim.server.stats.StatsService;
import com.golfsim.server.stats.StatsViews.Leaderboard;
import com.golfsim.server.stats.StatsViews.PlayerDetail;
import com.golfsim.server.stats.StatsViews.PlayerStats;
import java.util.List;
import org.springframework.web.bind.annotation.GetMapping;
import org.springframework.web.bind.annotation.PathVariable;
import org.springframework.web.bind.annotation.RequestMapping;
import org.springframework.web.bind.annotation.RestController;

/** Player history and the leaderboard. */
@RestController
@RequestMapping("/api")
public class PlayerController {

    private final StatsService stats;

    public PlayerController(StatsService stats) {
        this.stats = stats;
    }

    @GetMapping("/players")
    public List<PlayerStats> players() {
        return stats.players();
    }

    @GetMapping("/players/{name}")
    public PlayerDetail player(@PathVariable String name) {
        return stats.player(name);
    }

    @GetMapping("/leaderboard")
    public Leaderboard leaderboard() {
        return stats.leaderboard();
    }
}
