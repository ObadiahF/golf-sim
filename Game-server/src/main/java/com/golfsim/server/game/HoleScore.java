package com.golfsim.server.game;

import jakarta.persistence.Column;
import jakarta.persistence.Entity;
import jakarta.persistence.FetchType;
import jakarta.persistence.GeneratedValue;
import jakarta.persistence.GenerationType;
import jakarta.persistence.Id;
import jakarta.persistence.JoinColumn;
import jakarta.persistence.ManyToOne;
import jakarta.persistence.Table;
import java.time.Instant;
import org.hibernate.annotations.CreationTimestamp;
import org.hibernate.annotations.UpdateTimestamp;

/** Strokes taken by one player on one hole of one game. */
@Entity
@Table(name = "hole_scores")
public class HoleScore {

    @Id
    @GeneratedValue(strategy = GenerationType.IDENTITY)
    private Long id;

    @ManyToOne(fetch = FetchType.LAZY, optional = false)
    @JoinColumn(name = "game_id")
    private Game game;

    @ManyToOne(optional = false)
    @JoinColumn(name = "player_id")
    private Player player;

    @Column(name = "hole_number", nullable = false)
    private int holeNumber;

    @Column(nullable = false)
    private int par;

    @Column(nullable = false)
    private int strokes;

    @CreationTimestamp
    @Column(name = "created_at", nullable = false, updatable = false)
    private Instant createdAt;

    @UpdateTimestamp
    @Column(name = "updated_at", nullable = false)
    private Instant updatedAt;

    protected HoleScore() {
    }

    public HoleScore(Game game, Player player, int holeNumber) {
        this.game = game;
        this.player = player;
        this.holeNumber = holeNumber;
    }

    public void update(int par, int strokes) {
        this.par = par;
        this.strokes = strokes;
    }

    public Game getGame() {
        return game;
    }

    public Player getPlayer() {
        return player;
    }

    public int getHoleNumber() {
        return holeNumber;
    }

    public int getPar() {
        return par;
    }

    public int getStrokes() {
        return strokes;
    }
}
