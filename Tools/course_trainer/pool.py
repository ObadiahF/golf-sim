"""The shared hole pool in Postgres: batches, their holes, and serving each user the next hole they have not seen.

- A batch is `size` holes generated in the background (pool_worker.py). Holes join `pool_holes` as they finish.
- Seen = a row in `hole_views` (shown by /api/next, opened by link, generated ad hoc, or rated).
- Serving: unseen pool holes, newest batch first, in a per-user shuffled order (md5 of hole id + user id), so
  friends rate different holes first and the ranking gets broad coverage.
- Refill: once anyone has seen `refill_at` of the newest batch, the next batch starts. Batch creation runs under
  an advisory lock and only ever compares against the newest batch, so it is idempotent and fires once per batch.
"""
from __future__ import annotations

import math
from dataclasses import dataclass

import numpy as np

from db import connect

_LOCK = 7_406_222  # pg_advisory_xact_lock key for batch creation (db._LOCK + 1)
MAX_SEED = 1_000_000_000


@dataclass(frozen=True)
class Batch:
    id: int
    size: int
    status: str
    trained_on_votes: int | None


def hole_seed(batch_id: int, position: int, attempt: int = 0) -> int:
    """A fixed seed per (batch, position, retry): a resumed batch regenerates the same holes."""
    return int(np.random.SeedSequence([batch_id, position, attempt]).generate_state(1)[0]) % MAX_SEED


def _batch(row) -> Batch | None:
    return None if row is None else Batch(row["id"], row["size"], row["status"], row["trained_on_votes"])


def _newest(conn) -> Batch | None:
    return _batch(conn.execute("SELECT * FROM batches ORDER BY id DESC LIMIT 1").fetchone())


def _create(conn, size: int) -> Batch:
    return _batch(conn.execute("INSERT INTO batches (size) VALUES (%s) RETURNING *", [size]).fetchone())


def ensure_batch(dsn: str, size: int) -> tuple[Batch, bool]:
    """The newest batch, creating the first one if there is none. Returns (batch, created)."""
    with connect(dsn) as conn:
        conn.execute("SELECT pg_advisory_xact_lock(%s)", [_LOCK])
        newest = _newest(conn)
        return (newest, False) if newest else (_create(conn, size), True)


def maybe_refill(dsn: str, size: int, refill_at: float) -> Batch | None:
    """Start the next batch if anyone has seen `refill_at` of the newest one. Returns the new batch, else None."""
    with connect(dsn) as conn:
        conn.execute("SELECT pg_advisory_xact_lock(%s)", [_LOCK])
        newest = _newest(conn)
        if newest is None:
            return _create(conn, size)
        ready = conn.execute("SELECT count(*) AS n FROM pool_holes WHERE batch_id = %s", [newest.id]).fetchone()["n"]
        need = max(1, math.ceil(newest.size * refill_at))
        if newest.status == "ready":  # some positions may have failed: seeing everything there is counts
            need = max(1, min(need, ready))
        seen = conn.execute("""SELECT coalesce(max(n), 0) AS n FROM (
                                   SELECT count(*) AS n FROM hole_views v JOIN pool_holes p USING (hole_id)
                                   WHERE p.batch_id = %s GROUP BY v.user_id) per_user""", [newest.id]).fetchone()["n"]
        return _create(conn, size) if seen >= need else None


def serve_next(dsn: str, user_id: int) -> str | None:
    """The next pool hole this user has not seen, recorded as seen. None when nothing unseen is ready."""
    with connect(dsn) as conn:
        for _ in range(3):  # ON CONFLICT: the same user's other tab took that hole a moment ago
            row = conn.execute("""
                WITH pick AS (
                    SELECT p.hole_id FROM pool_holes p
                    WHERE NOT EXISTS (SELECT 1 FROM hole_views v WHERE v.user_id = %(u)s AND v.hole_id = p.hole_id)
                    ORDER BY p.batch_id DESC, md5(p.hole_id || ':' || %(u)s::text)
                    LIMIT 1)
                INSERT INTO hole_views (user_id, hole_id) SELECT %(u)s, hole_id FROM pick
                ON CONFLICT DO NOTHING RETURNING hole_id""", {"u": user_id}).fetchone()
            if row:
                return row["hole_id"]
    return None


def progress(dsn: str, user_id: int) -> dict | None:
    """The newest batch as the UI shows it: {batch, size, status, ready, seen (by this user)}."""
    with connect(dsn) as conn:
        newest = _newest(conn)
        if newest is None:
            return None
        row = conn.execute("""SELECT count(*) AS ready, count(v.user_id) AS seen
                              FROM pool_holes p LEFT JOIN hole_views v ON v.hole_id = p.hole_id AND v.user_id = %s
                              WHERE p.batch_id = %s""", [user_id, newest.id]).fetchone()
    return {"batch": newest.id, "size": newest.size, "status": newest.status, "ready": row["ready"],
            "seen": row["seen"]}


def hole_ids(dsn: str) -> set[str]:
    with connect(dsn) as conn:
        return {r["hole_id"] for r in conn.execute("SELECT hole_id FROM pool_holes")}


def generating(dsn: str) -> list[Batch]:
    """Batches still filling, oldest first (a restart resumes them)."""
    with connect(dsn) as conn:
        return [_batch(r) for r in conn.execute("SELECT * FROM batches WHERE status = 'generating' ORDER BY id")]


def missing_positions(dsn: str, batch: Batch) -> list[int]:
    with connect(dsn) as conn:
        done = {r["position"] for r in conn.execute("SELECT position FROM pool_holes WHERE batch_id = %s", [batch.id])}
    return [i for i in range(batch.size) if i not in done]


def set_trained(dsn: str, batch_id: int, votes: int) -> None:
    with connect(dsn) as conn:
        conn.execute("UPDATE batches SET trained_on_votes = %s WHERE id = %s", [votes, batch_id])


def add_hole(dsn: str, batch_id: int, position: int, hole_id: str) -> None:
    with connect(dsn) as conn:
        conn.execute("INSERT INTO pool_holes (hole_id, batch_id, position) VALUES (%s, %s, %s) ON CONFLICT DO NOTHING",
                     [hole_id, batch_id, position])


def mark_ready(dsn: str, batch_id: int) -> None:
    with connect(dsn) as conn:
        conn.execute("UPDATE batches SET status = 'ready' WHERE id = %s", [batch_id])
