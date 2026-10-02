-- Shared hole pool (pool.py). A batch = retrain on everyone's latest votes, then generate `size` holes in the
-- background; each user is served unseen pool holes (hole_views = seen), newest batch first.

CREATE TABLE batches (
    id               serial PRIMARY KEY,
    created_at       timestamptz NOT NULL DEFAULT now(),
    size             integer NOT NULL CHECK (size > 0),
    status           text NOT NULL DEFAULT 'generating' CHECK (status IN ('generating', 'ready')),
    trained_on_votes integer                   -- NULL until the batch's retrain ran (0: there were no votes)
);

-- Pool holes are never pruned (api.prune keeps them).
CREATE TABLE pool_holes (
    hole_id    text PRIMARY KEY,
    batch_id   integer NOT NULL REFERENCES batches (id) ON DELETE CASCADE,
    position   integer NOT NULL,
    created_at timestamptz NOT NULL DEFAULT now(),
    UNIQUE (batch_id, position)
);

-- "Who has seen what in this batch" counts join hole_views on hole_id.
CREATE INDEX hole_views_hole ON hole_views (hole_id);
