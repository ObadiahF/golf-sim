"""A random round for the game: a weighted pick from every playable hole friends haven't turned down.

Pool: every pool hole (pool_holes) and every voted hole that is still on disk, playable (hole_checks.py; an unchecked
one is checked when it comes up) and not net-disliked (more 👎 than 👍). Unrated holes are in. Ad hoc holes made
with Generate and never rated are not: they are someone's experiment, not part of the course.
`presets` (the course types the player chose) narrows the pool to holes of those presets; nothing else fills in.
Weight = 1 + likes - dislikes (so at least 1): an unrated or evenly split hole weighs 1, a hole with 3 net likes 4,
so liked holes come up more often but every playable hole gets played. The pick is a weighted draw without
replacement (Efraimidis-Spirakis: each hole gets the key u^(1/weight), u uniform in (0, 1), highest keys first),
so a response never repeats a hole. `exclude` (the game's recently played ids) goes last: those holes are drawn only
when the rest of the pool can't fill the round.
`preset_counts` counts that pool per preset (GET /api/game/presets, and the pool's stock, pool_worker.py).
"""
from __future__ import annotations

import random
from collections import Counter

from db import connect
from hole_checks import HoleChecks, verdicts
from holes import HoleStore
from ranking import collect, tallies

WEIGHTING = "1+likes-dislikes"


def weight(tally: dict) -> int:
    """1 + net likes, floored at 1. Callers leave net-disliked holes out first (`eligible`)."""
    return max(1, 1 + tally["ups"] - tally["downs"])


def eligible(tally: dict) -> bool:
    """Not net-disliked: at least as many likes as dislikes (no votes counts)."""
    return tally["ups"] >= tally["downs"]


def pool_tallies(dsn: str) -> list[dict]:
    """[{id, preset, ups, downs, score}] for every voted or pool hole (unrated: 0 / 0 / 0), ordered by id."""
    voted = {t["id"]: t for t in tallies(dsn)}
    with connect(dsn) as conn:
        presets = {r["hole_id"]: r["preset"]
                   for r in conn.execute("SELECT hole_id, preset FROM votes UNION SELECT hole_id, preset FROM pool_holes")}
    unrated = {h: {"id": h, "ups": 0, "downs": 0, "score": 0.0} for h in presets if h not in voted}
    return sorted(({**t, "preset": presets[t["id"]]} for t in [*voted.values(), *unrated.values()]),
                  key=lambda t: t["id"])


def candidates(dsn: str, holes: HoleStore, presets=None) -> list[dict]:
    """The pool (module doc) as pool_tallies: holes on disk, not net-disliked, of `presets` (None: any), and not
    checked unplayable (an unchecked hole counts: it is checked when drawn)."""
    known = verdicts(dsn)
    return [t for t in pool_tallies(dsn) if eligible(t) and known.get(t["id"], True)
            and (presets is None or t["preset"] in presets) and holes.exists(t["id"])]


def preset_counts(dsn: str, holes: HoleStore) -> Counter:
    """preset -> holes in the pool (`candidates`)."""
    return Counter(t["preset"] for t in candidates(dsn, holes))


def weighted_order(tallies_: list[dict], rng: random.Random) -> list[dict]:
    """The tallies in a weighted random order (heavier first more often), each once."""
    keyed = [(rng.random() ** (1.0 / weight(t)), t) for t in tallies_]
    return [t for _, t in sorted(keyed, key=lambda kt: kt[0], reverse=True)]


def random_holes(dsn: str, holes: HoleStore, checks: HoleChecks, count: int, file_url, exclude=(), presets=None,
                 rng: random.Random | None = None) -> tuple[list[dict], int]:
    """(up to `count` different holes from the pool of `presets` (see module doc) in the order drawn, as
    ranking.top_holes entries (rank = draw order); how many holes that pool has). Holes in `exclude` come only after
    every other candidate."""
    rng = rng or random.Random()
    skip = set(exclude)
    pool = candidates(dsn, holes, presets)
    fresh = weighted_order([t for t in pool if t["id"] not in skip], rng)
    recent = weighted_order([t for t in pool if t["id"] in skip], rng)
    return collect(fresh + recent, dsn, holes, checks, count, file_url), len(pool)
