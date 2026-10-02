-- Server-side sessions (auth.py): the cookie names a row here, so logging out (or `passwd`) really ends it.
-- Cookies from before this migration carry no session id and simply need a fresh login.

CREATE TABLE sessions (
    id         text PRIMARY KEY,                -- random, also inside the signed cookie
    user_id    integer NOT NULL REFERENCES users (id) ON DELETE CASCADE,
    created_at timestamptz NOT NULL DEFAULT now(),
    expires_at timestamptz NOT NULL
);
CREATE INDEX sessions_user ON sessions (user_id);
CREATE INDEX sessions_expires ON sessions (expires_at);
