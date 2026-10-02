-- Course Trainer schema. Applied once by db.migrate() (tracked in schema_migrations); add 002_*.sql for changes.

CREATE TABLE users (
    id            serial PRIMARY KEY,
    name          text NOT NULL CHECK (name <> ''),
    password_hash text,                      -- NULL: cannot log in (the `legacy` user that owns imported votes)
    created_at    timestamptz NOT NULL DEFAULT now(),
    last_login    timestamptz
);
CREATE UNIQUE INDEX users_name_ci ON users (lower(name));

-- Append-only: every vote ever cast, with a snapshot of the hole's style so training never needs the package.
CREATE TABLE votes (
    id                bigserial PRIMARY KEY,
    user_id           integer NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    hole_id           text NOT NULL,
    rating            smallint NOT NULL CHECK (rating IN (-1, 1)),
    comment           text,
    tags              jsonb NOT NULL DEFAULT '[]',
    preset            text NOT NULL,
    theme             text NOT NULL,
    par               smallint NOT NULL,
    params            jsonb NOT NULL,
    generator_version integer,
    seed              bigint,
    model_score       real,
    created_at        timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX votes_user_hole ON votes (user_id, hole_id, created_at DESC, id DESC);
CREATE INDEX votes_hole ON votes (hole_id);

-- What training uses: each person's latest vote per hole (re-voting replaces, history stays in votes).
CREATE VIEW latest_votes AS
    SELECT DISTINCT ON (user_id, hole_id) *
    FROM votes
    ORDER BY user_id, hole_id, created_at DESC, id DESC;

-- Who has opened which hole, and when: each user's "recent holes" list, and pruning never deletes a hole
-- someone opened within the grace period.
CREATE TABLE hole_views (
    user_id    integer NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    hole_id    text NOT NULL,
    first_seen timestamptz NOT NULL DEFAULT now(),
    last_seen  timestamptz NOT NULL DEFAULT now(),
    PRIMARY KEY (user_id, hole_id)
);
CREATE INDEX hole_views_last_seen ON hole_views (last_seen);

CREATE TABLE training_runs (
    id          serial PRIMARY KEY,
    user_id     integer REFERENCES users (id) ON DELETE SET NULL,   -- who pressed Retrain (NULL: CLI)
    only_user   text,                                               -- votes filtered to this user, NULL: everyone
    vote_count  integer NOT NULL,
    preset      text,
    likes       jsonb NOT NULL DEFAULT '[]',
    dislikes    jsonb NOT NULL DEFAULT '[]',
    created_at  timestamptz NOT NULL DEFAULT now()
);
