"""Playability of each hole (tee shot and green), recorded in `hole_checks` (migrations 004, 005).

Packages are immutable, so a hole made before the generator's launch check (GENERATOR_VERSION 3 and earlier) can
have ground rising into the tee shot, one made before v5 can have a green tilted ~15 % by a pond bank (Q5-1), and
one made before v6 can have trees right on the tee line (G6-3) or a landing zone on a steep side slope (G6-4).
Each hole is judged with course_gen's scanner (scan_playability.py, the generator's own validate checks):
unplayable when the ground rises more than `launch_near_max_m` above a low tee shot within `launch_near_m` of the
tee, or more than `launch_far_max_m` within `launch_far_m`, or the green is steeper than `green_pin_max_slope`
within validate.GREEN_PIN_RADIUS of the pin, or fewer than `tee_line_min_clear` of the tee-shot lines
(validate.TEE_FAN_DEG either side) miss every tree crown, or every line from a landing zone to its target is blocked,
or a landing zone leans more than `landing_max_side_slope` across the line (config.py).

- New pool holes are checked as they join the pool (pool_worker.py); they pass, the generator now rejects them.
- On startup a background thread backfills every pool or voted hole not checked yet, or checked by older rules
  (`check_version` < CHECK_VERSION), ~60 ms a hole.
- The leaderboard checks any unchecked candidate on the spot, so a bad hole never slips into the Top holes
  while the backfill is still running.
- Unplayable holes leave ranking.top_holes (/api/top, /api/game/top-holes, `top`), random_holes (/api/game/random-holes)
  and /api/next (pool.py).
  Their votes stay: training still learns from them.
"""
from __future__ import annotations

import math
import threading
import traceback
from dataclasses import dataclass

import numpy as np

import _paths  # noqa: F401
from config import Settings, log
from db import connect
from holes import HoleNotFound, HoleStore
from hole_package import read_hole
from scan_playability import generator_version, green_profile, landing_profile, launch_profile, tree_profile
from validate import green_issues, landing_issues, tree_line_issues

CHECK_VERSION = 3  # 1: tee shot (TH-5); 2: + the green's slope at the pin (Q5-1); 3: + trees on the shot lines (G6-3)
                   # and landing-zone side slopes (G6-4). Older verdicts are re-checked.


@dataclass(frozen=True)
class Limits:
    near_m: float = 60.0
    near_max_m: float = 1.0
    far_m: float = 100.0
    far_max_m: float = 2.5
    green_pin_max: float = 0.06
    tee_line_min_clear: float = 0.4
    landing_max_side_slope: float = 0.15

    @classmethod
    def from_settings(cls, s: Settings) -> "Limits":
        return cls(s.launch_near_m, s.launch_near_max_m, s.launch_far_m, s.launch_far_max_m, s.green_pin_max_slope,
                   s.tee_line_min_clear, s.landing_max_side_slope)


@dataclass(frozen=True)
class Verdict:
    hole_id: str
    generator_version: int | None
    playable: bool
    reason: str | None
    worst_overshoot_m: float | None
    green_pin_slope: float | None = None
    green_max_slope: float | None = None


def _worst(s: np.ndarray, over: np.ndarray, reach: float) -> tuple[float, float]:
    """(overshoot, distance) of the highest point within `reach` m of the tee."""
    inside = np.where(s <= reach, over, -np.inf)
    i = int(np.argmax(inside))
    return float(inside[i]), float(s[i])


def assess(profile: dict[str, tuple[np.ndarray, np.ndarray]], limits: Limits) -> tuple[str | None, float]:
    """(why the tee shot is unplayable or None, worst overshoot in m >= 0) for a scan_playability.launch_profile."""
    worst = max(_worst(s, over, limits.far_m)[0] for s, over in profile.values())
    for reach, cap in ((limits.near_m, limits.near_max_m), (limits.far_m, limits.far_max_m)):
        for name, (s, over) in profile.items():
            height, at = _worst(s, over, reach)
            if height > cap:
                return (f"ground {height:.1f} m above the tee shot {at:.0f} m out on {name} "
                        f"(limit {cap:g} m within {reach:g} m)"), round(max(worst, 0.0), 2)
    return None, round(max(worst, 0.0), 2)


def assess_green(slopes: tuple[float, float] | None, limits: Limits) -> str | None:
    """Why the green is unplayable or None, for a scan_playability.green_profile (None: no green, nothing to
    judge). Only the pin area counts: a steep patch elsewhere can be putted around."""
    issues = green_issues(*slopes, pin_cap=limits.green_pin_max, max_cap=math.inf) if slopes else []
    return issues[0] if issues else None


def assess_lines(clearance: list, slopes: list[tuple[float, float]], limits: Limits) -> str | None:
    """Why the shots are unplayable or None, for a scan_playability.tree_profile and landing_profile: trees on the
    tee-shot lines or every later line, or a landing zone on a side slope (the grade along the line is not judged)."""
    issues = (tree_line_issues(clearance, tee_min=limits.tee_line_min_clear)
              + landing_issues(slopes, cap=limits.landing_max_side_slope, along_cap=math.inf))
    return issues[0] if issues else None


def judge(package_dir, hole_id: str, limits: Limits) -> Verdict:
    """The verdict on one package on disk (an unreadable package is unplayable: the game could not load it)."""
    try:
        hole = read_hole(package_dir)
        profile = launch_profile(package_dir, hole)
        slopes = green_profile(package_dir, hole)
        clearance = tree_profile(package_dir, hole)
        landing = landing_profile(package_dir, hole)
        version = generator_version(package_dir)
    except (OSError, KeyError, ValueError) as e:
        return Verdict(hole_id, None, False, f"unreadable package: {e}", None)
    reason, worst = assess(profile, limits)
    reason = reason or assess_green(slopes, limits) or assess_lines(clearance, landing, limits)
    pin_slope, max_slope = (round(v, 4) for v in slopes) if slopes else (None, None)
    return Verdict(hole_id, version, reason is None, reason, worst, pin_slope, max_slope)


def record(dsn: str, v: Verdict) -> None:
    with connect(dsn) as conn:
        conn.execute("""INSERT INTO hole_checks (hole_id, generator_version, playable, reason, worst_overshoot_m,
                                                 green_pin_slope, green_max_slope, check_version)
                        VALUES (%s, %s, %s, %s, %s, %s, %s, %s)
                        ON CONFLICT (hole_id) DO UPDATE SET generator_version = EXCLUDED.generator_version,
                            playable = EXCLUDED.playable, reason = EXCLUDED.reason,
                            worst_overshoot_m = EXCLUDED.worst_overshoot_m, green_pin_slope = EXCLUDED.green_pin_slope,
                            green_max_slope = EXCLUDED.green_max_slope, check_version = EXCLUDED.check_version,
                            checked_at = now()""",
                     [v.hole_id, v.generator_version, v.playable, v.reason, v.worst_overshoot_m, v.green_pin_slope,
                      v.green_max_slope, CHECK_VERSION])


def verdicts(dsn: str) -> dict[str, bool]:
    """hole id -> playable, for every hole checked by the current rules (an older verdict counts as unchecked)."""
    with connect(dsn) as conn:
        rows = conn.execute("SELECT hole_id, playable FROM hole_checks WHERE check_version >= %s", [CHECK_VERSION])
        return {r["hole_id"]: r["playable"] for r in rows}


def failures(dsn: str) -> list[dict]:
    """Every hole checked unplayable: {hole_id, generator_version, reason, worst_overshoot_m}, worst first."""
    with connect(dsn) as conn:
        return conn.execute("""SELECT hole_id, generator_version, reason, worst_overshoot_m FROM hole_checks
                               WHERE NOT playable ORDER BY worst_overshoot_m DESC NULLS LAST, hole_id""").fetchall()


def candidates(dsn: str, recheck: bool = False) -> list[str]:
    """Pool holes and voted holes (the ones that can be served or ranked), only the ones not checked by the current
    rules (CHECK_VERSION) unless `recheck`."""
    unchecked = "" if recheck else ("WHERE NOT EXISTS (SELECT 1 FROM hole_checks c WHERE c.hole_id = h.hole_id"
                                    " AND c.check_version >= %(version)s)")
    with connect(dsn) as conn:
        rows = conn.execute(f"""SELECT hole_id FROM (SELECT hole_id FROM pool_holes UNION SELECT hole_id FROM votes) h
                                {unchecked} ORDER BY hole_id""", {"version": CHECK_VERSION})
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
        """Check every unchecked or outdated (`recheck`: every) pool or voted hole on disk; returns the verdicts."""
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
