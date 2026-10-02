"""Postgres for the trainer: schema migrations, who-viewed-what, and the training log. Votes themselves go
through course_gen's PostgresStore (pg_store.py), which shares `connect`."""
from __future__ import annotations

from pathlib import Path

import _paths  # noqa: F401
from pg_store import connect

MIGRATIONS = Path(__file__).resolve().parent / "migrations"
_LOCK = 7_406_221  # pg_advisory_lock key: two containers starting at once migrate one after the other

__all__ = ["connect", "migrate", "touch_view", "recent_hole_ids", "viewed_since", "log_training"]


def migrate(dsn: str) -> list[str]:
    """Apply migrations/*.sql not applied yet, in name order. Returns the names applied."""
    applied = []
    with connect(dsn) as conn:  # one transaction: all pending migrations apply, or none
        conn.execute("SELECT pg_advisory_xact_lock(%s)", [_LOCK])
        conn.execute("CREATE TABLE IF NOT EXISTS schema_migrations"
                     " (name text PRIMARY KEY, applied_at timestamptz NOT NULL DEFAULT now())")
        done = {r["name"] for r in conn.execute("SELECT name FROM schema_migrations")}
        for path in sorted(MIGRATIONS.glob("*.sql")):
            if path.name not in done:
                conn.execute(path.read_text())
                conn.execute("INSERT INTO schema_migrations (name) VALUES (%s)", [path.name])
                applied.append(path.name)
    return applied


def touch_view(dsn: str, user_id: int, hole_id: str) -> None:
    with connect(dsn) as conn:
        conn.execute("""INSERT INTO hole_views (user_id, hole_id) VALUES (%s, %s)
                        ON CONFLICT (user_id, hole_id) DO UPDATE SET last_seen = now()""", [user_id, hole_id])


def recent_hole_ids(dsn: str, user_id: int, limit: int) -> list[str]:
    """This user's holes, most recently opened first."""
    with connect(dsn) as conn:
        rows = conn.execute("SELECT hole_id FROM hole_views WHERE user_id = %s ORDER BY last_seen DESC LIMIT %s",
                            [user_id, limit])
        return [r["hole_id"] for r in rows]


def viewed_since(dsn: str, hours: float) -> set[str]:
    """Holes anyone opened in the last `hours` (treated as 'being looked at' when pruning)."""
    with connect(dsn) as conn:
        rows = conn.execute("SELECT DISTINCT hole_id FROM hole_views WHERE last_seen > now() - make_interval(secs => %s)",
                            [hours * 3600])
        return {r["hole_id"] for r in rows}


def log_training(dsn: str, user_id: int | None, only_user: str | None, vote_count: int, summary: dict) -> None:
    from psycopg.types.json import Jsonb
    with connect(dsn) as conn:
        conn.execute("""INSERT INTO training_runs (user_id, only_user, vote_count, preset, likes, dislikes)
                        VALUES (%s, %s, %s, %s, %s, %s)""",
                     [user_id, only_user, vote_count, summary["preset"], Jsonb(summary["likes"]),
                      Jsonb(summary["dislikes"])])
