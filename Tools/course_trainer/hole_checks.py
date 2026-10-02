"""Tee-shot playability of each hole, recorded in `hole_checks` (migration 004).

Packages are immutable, so a hole made before the generator's launch check (GENERATOR_VERSION 3 and earlier) can
have ground rising into the tee shot. Each hole is judged once with course_gen's scanner (scan_launch.py, the
generator's own validate.launch_overshoot): unplayable when the ground rises more than `launch_near_max_m` above a
low tee shot within `launch_near_m` of the tee, or more than `launch_far_max_m` within `launch_far_m` (config.py).

- New pool holes are checked as they join the pool (pool_worker.py); they pass, the generator now rejects them.
- On startup a background thread backfills every pool or voted hole not checked yet (~50 ms a hole).
- The leaderboard checks any unchecked candidate on the spot, so a bad hole never slips into the Top holes
  while the backfill is still running.
- Unplayable holes leave ranking.top_holes (/api/top, /api/game/top-holes, `top`) and /api/next (pool.py).
  Their votes stay: training still learns from them.
"""
from __future__ import annotations

import threading
import traceback
from dataclasses import dataclass

import numpy as np

import _paths  # noqa: F401
from config import Settings, log
from db import connect
from holes import HoleNotFound, HoleStore
from scan_launch import generator_version, launch_profile


@dataclass(frozen=True)
class Limits:
    near_m: float = 60.0
    near_max_m: float = 1.0
    far_m: float = 100.0
    far_max_m: float = 2.5

    @classmethod
    def from_settings(cls, s: Settings) -> "Limits":
        return cls(s.launch_near_m, s.launch_near_max_m, s.launch_far_m, s.launch_far_max_m)


@dataclass(frozen=True)
class Verdict:
    hole_id: str
    generator_version: int | None
    playable: bool
    reason: str | None
    worst_overshoot_m: float | None


def _worst(s: np.ndarray, over: np.ndarray, reach: float) -> tuple[float, float]:
    """(overshoot, distance) of the highest point within `reach` m of the tee."""
    inside = np.where(s <= reach, over, -np.inf)
    i = int(np.argmax(inside))
    return float(inside[i]), float(s[i])


def assess(profile: dict[str, tuple[np.ndarray, np.ndarray]], limits: Limits) -> tuple[str | None, float]:
    """(why the tee shot is unplayable or None, worst overshoot in m >= 0) for a scan_launch.launch_profile."""
    worst = max(_worst(s, over, limits.far_m)[0] for s, over in profile.values())
    for reach, cap in ((limits.near_m, limits.near_max_m), (limits.far_m, limits.far_max_m)):
        for name, (s, over) in profile.items():
            height, at = _worst(s, over, reach)
            if height > cap:
                return (f"ground {height:.1f} m above the tee shot {at:.0f} m out on {name} "
                        f"(limit {cap:g} m within {reach:g} m)"), round(max(worst, 0.0), 2)
    return None, round(max(worst, 0.0), 2)


def judge(package_dir, hole_id: str, limits: Limits) -> Verdict:
    """The verdict on one package on disk (an unreadable package is unplayable: the game could not load it)."""
    try:
        profile = launch_profile(package_dir)
        version = generator_version(package_dir)
    except (OSError, KeyError, ValueError) as e:
        return Verdict(hole_id, None, False, f"unreadable package: {e}", None)
    reason, worst = assess(profile, limits)
    return Verdict(hole_id, version, reason is None, reason, worst)


def record(dsn: str, v: Verdict) -> None:
    with connect(dsn) as conn:
        conn.execute("""INSERT INTO hole_checks (hole_id, generator_version, playable, reason, worst_overshoot_m)
                        VALUES (%s, %s, %s, %s, %s)
                        ON CONFLICT (hole_id) DO UPDATE SET generator_version = EXCLUDED.generator_version,
                            playable = EXCLUDED.playable, reason = EXCLUDED.reason,
                            worst_overshoot_m = EXCLUDED.worst_overshoot_m, checked_at = now()""",
                     [v.hole_id, v.generator_version, v.playable, v.reason, v.worst_overshoot_m])


def verdicts(dsn: str) -> dict[str, bool]:
    """hole id -> playable, for every checked hole."""
    with connect(dsn) as conn:
        return {r["hole_id"]: r["playable"] for r in conn.execute("SELECT hole_id, playable FROM hole_checks")}


def failures(dsn: str) -> list[dict]:
    """Every hole checked unplayable: {hole_id, generator_version, reason, worst_overshoot_m}, worst first."""
    with connect(dsn) as conn:
        return conn.execute("""SELECT hole_id, generator_version, reason, worst_overshoot_m FROM hole_checks
                               WHERE NOT playable ORDER BY worst_overshoot_m DESC NULLS LAST, hole_id""").fetchall()


def candidates(dsn: str, recheck: bool = False) -> list[str]:
    """Pool holes and voted holes (the ones that can be served or ranked), only the unchecked ones unless
    `recheck`."""
    unchecked = "" if recheck else "WHERE NOT EXISTS (SELECT 1 FROM hole_checks c WHERE c.hole_id = h.hole_id)"
    with connect(dsn) as conn:
        rows = conn.execute(f"""SELECT hole_id FROM (SELECT hole_id FROM pool_holes UNION SELECT hole_id FROM votes) h
                                {unchecked} ORDER BY hole_id""")
        return [r["hole_id"] for r in rows]


class HoleChecks:
    def __init__(self, dsn: str, holes: HoleStore, limits: Limits):
        self.dsn = dsn
        self.holes = holes
        self.limits = limits
        self.backfill_thread: threading.Thread | None = None

    @classmethod
    def from_settings(cls, settings: Settings, holes: HoleStore) -> "HoleChecks":
        return cls(settings.database_url, holes, Limits.from_settings(settings))

    def check(self, hole_id: str) -> Verdict | None:
        """Judge one hole and record it. None: no such package on disk (nothing to serve or rank)."""
        try:
            folder = self.holes.folder(hole_id)
        except HoleNotFound:
            return None
        verdict = judge(folder, hole_id, self.limits)
        record(self.dsn, verdict)
        return verdict

    def playable(self, hole_id: str, known: dict[str, bool] | None = None) -> bool:
        """The recorded verdict (from `known`, else the table), checking the hole now if it has none yet."""
        ok = (known if known is not None else verdicts(self.dsn)).get(hole_id)
        if ok is None:
            verdict = self.check(hole_id)
            ok = verdict is None or verdict.playable
        return ok

    def backfill(self, recheck: bool = False) -> list[Verdict]:
        """Check every unchecked (`recheck`: every) pool or voted hole on disk; returns the verdicts."""
        return [v for hole_id in candidates(self.dsn, recheck) if (v := self.check(hole_id)) is not None]

    def start_backfill(self) -> threading.Thread:
        """backfill() in a daemon thread, so startup and requests never wait for it."""
        def run():
            try:
                done = self.backfill()
                if done:
                    bad = [v.hole_id for v in done if not v.playable]
                    log(f"hole checks: backfilled {len(done)} hole(s), {len(bad)} unplayable"
                        + (f": {', '.join(bad)}" if bad else ""))
            except Exception:  # never take the server down; the next start retries
                log(f"hole checks: backfill failed\n{traceback.format_exc()}")

        self.backfill_thread = threading.Thread(target=run, name="hole-checks", daemon=True)
        self.backfill_thread.start()
        return self.backfill_thread
