"""Leaderboard: holes ranked by everyone's latest vote (latest_votes), best first.

Score = the Wilson score lower bound (95%) of the like share up / (up + down): the like share the hole has at
least, given how few votes it has. 1 like and 0 dislikes scores 0.21, 4 likes and 1 dislike 0.38, 9 and 1 0.60,
so a hole needs several votes to beat a well-liked one, unlike the raw ratio (1/1 = 100%). No votes: 0.
Holes whose tee shot is unplayable (hole_checks.py) are left out; their votes still count for training.
"""
from __future__ import annotations

import math

from db import connect
from hole_checks import HoleChecks, verdicts
from holes import PACKAGE_FILES, HoleNotFound, HoleStore

Z = 1.96  # 95% confidence
FORMULA = "wilson-95"


def wilson_lower_bound(up: int, down: int, z: float = Z) -> float:
    n = up + down
    if n == 0:
        return 0.0
    p = up / n
    centre = p + z * z / (2 * n)
    spread = z * math.sqrt((p * (1 - p) + z * z / (4 * n)) / n)
    return (centre - spread) / (1 + z * z / n)


def tallies(dsn: str) -> list[dict]:
    """[{id, ups, downs, score}] for every voted hole, best first (ties: more likes, then id)."""
    with connect(dsn) as conn:
        rows = conn.execute("""SELECT hole_id, count(*) FILTER (WHERE rating > 0) AS ups,
                                      count(*) FILTER (WHERE rating < 0) AS downs
                               FROM latest_votes GROUP BY hole_id""").fetchall()
    out = [{"id": r["hole_id"], "ups": r["ups"], "downs": r["downs"],
            "score": round(wilson_lower_bound(r["ups"], r["downs"]), 4)} for r in rows]
    return sorted(out, key=lambda t: (-t["score"], -t["ups"], t["id"]))


def top_holes(dsn: str, holes: HoleStore, checks: HoleChecks, limit: int, file_url,
              votes: dict[str, dict] | None = None) -> list[dict]:
    """The `limit` best playable holes still on disk: the hole summary (holes.describe; `rating` from `votes`)
    plus rank, ups, downs, score, previewUrl and `files` ({name: url}); `file_url(id, name)` builds the URLs.
    An unchecked candidate is checked on the spot (checks.playable)."""
    out = []
    known = verdicts(dsn)
    for tally in tallies(dsn):
        if len(out) >= limit:
            break
        try:
            summary = holes.describe(tally["id"], votes)
        except (HoleNotFound, FileNotFoundError):  # e.g. legacy votes on holes never on this server
            continue
        if not checks.playable(tally["id"], known):
            continue
        files = {name: file_url(tally["id"], name) for name in sorted(PACKAGE_FILES)}
        out.append({**summary, **tally, "rank": len(out) + 1, "previewUrl": files["preview.png"], "files": files})
    return out
