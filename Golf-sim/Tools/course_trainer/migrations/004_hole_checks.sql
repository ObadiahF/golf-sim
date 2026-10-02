-- Tee-shot playability of each hole (hole_checks.py, course_gen's scan_launch): packages are immutable, so a hole
-- generated before the launch check (generator v3 and earlier) can have ground rising into the tee shot. Checked
-- once when it joins the pool, plus a startup backfill; unplayable holes leave the Top holes ranking, the game's
-- top-holes API and /api/next. Their votes stay (training still uses them).

CREATE TABLE hole_checks (
    hole_id           text PRIMARY KEY,
    generator_version integer,
    playable          boolean NOT NULL,
    reason            text,                     -- why not (NULL when playable)
    worst_overshoot_m real,                     -- highest the ground rises above the tee shot (0: it clears)
    checked_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX hole_checks_unplayable ON hole_checks (hole_id) WHERE NOT playable;
