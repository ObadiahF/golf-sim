package com.golfsim.server.game;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.EnumType;
import jakarta.persistence.Enumerated;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.JoinTable;
import jakarta.persistence.ManyToMany;
import jakarta.persistence.OrderColumn;
import jakarta.persistence.Table;
import java.time.Instant;
import java.util.ArrayList;
import java.util.List;
import java.util.Optional;
import org.hibernate.annotations.CreationTimestamp;

/** One round. Players are kept in turn order via {@code game_players.turn_order} (0-based). */
@Entity
@Table(name = "games")
public class Game {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @Enumerated(EnumType.STRING)
    @Column(nullable = false, length = 16)
    private GameStatus status = GameStatus.IN_PROGRESS;

    @Column(name = "holes_count", nullable = false)
    private int holesCount;

    @Column(name = "course_name", length = 100)
    private String courseName;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    @Column(name = "finished_at")
    private Instant finishedAt;

    @ManyToMany
    @JoinTable(name = "game_players",
            joinColumns = @JoinColumn(name = "game_id"),
            inverseJoinColumns = @JoinColumn(name = "player_id"))
    @OrderColumn(name = "turn_order")
    private List<Player> players = new ArrayList<>();

    protected Game() {
    }

    public Game(int holesCount, String courseName, List<Player> players) {
        this.holesCount = holesCount;
        this.courseName = courseName;
        this.players = new ArrayList<>(players);
    }

    /** Moves the game out of IN_PROGRESS. */
    public void end(GameStatus newStatus, Instant at) {
        this.status = newStatus;
        this.finishedAt = at;
    }

    public Optional<Player> findPlayer(String name) {
        String key = Names.key(name);
        return players.stream().filter(p -> p.getNameKey().equals(key)).findFirst();
    }

    public boolean isInProgress() {
        return status == GameStatus.IN_PROGRESS;
    }

    public Long getId() {
        return id;
    }

    public GameStatus getStatus() {
        return status;
    }

    public int getHolesCount() {
        return holesCount;
    }

    public String getCourseName() {
        return courseName;
    }

    public Instant getCreatedAt() {
        return createdAt;
    }

    public Instant getFinishedAt() {
        return finishedAt;
    }

    public List<Player> getPlayers() {
        return players;
    }
}
