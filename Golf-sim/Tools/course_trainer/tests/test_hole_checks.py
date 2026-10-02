"""Playability (hole_checks.py, tee shot and green): unplayable holes leave Top holes, the game API and /api/next."""
import json
import math
from pathlib import Path

import numpy as np
import pytest
from fastapi.testclient import TestClient
from shapely.geometry import LineString

import hole_checks
import server
import users
from conftest import fill_pool, login, next_hole
from dem_io import write_raw16
from gen_hole import rate_package
from hole_checks import CHECK_VERSION, Limits, assess, assess_green
from hole_package import read_heights, read_hole
from pg_store import PostgresStore
from terrain import Grid
from test_votes import rows

KEY = "game-key-for-tests"
VOTERS = tuple(f"u{i}" for i in range(1, 8))


def edit_heights(folder: Path, edit) -> None:
    """Rewrite a package's heightmap as edit(hole, heights, grid) (a legacy-style package, for the checks)."""
    hole = read_hole(folder)
    heights = read_heights(folder, hole)
    heights = edit(hole, heights, Grid(hole["sizeMeters"], heights.shape[0]))
    lo, hi = write_raw16(heights, folder / hole["heightmap"]["file"])
    hole["heightmap"].update(minElevation=round(lo, 3), maxElevation=round(max(hi, lo + 0.001), 3))
    (folder / "hole.json").write_text(json.dumps(hole, indent=1))


def raise_wall(folder: Path, at: float = 30.0, over: float = 6.0) -> None:
    """Ground `over` m above an 8 deg tee shot `at` m out on the hole line."""
    def edit(hole, heights, grid):
        path = LineString(np.asarray(hole["holePath"]["points"], dtype=float).reshape(-1, 2))
        tee, spot = path.coords[0], path.interpolate(at)
        tee_height = heights.flat[np.argmin(np.hypot(grid.x - tee[0], grid.z - tee[1]))]
        top = tee_height + math.tan(math.radians(8)) * at + over
        disk = np.hypot(grid.x - spot.x, grid.z - spot.y) < 8.0
        return np.where(disk, np.maximum(heights, top), heights)
    edit_heights(folder, edit)


def tilt_green(folder: Path, grade: float = 0.15) -> None:
    """The green area tilted `grade` (the pond-bank tilt of Q5-1): a plane through the pin, 25 m around it."""
    def edit(hole, heights, grid):
        pin = hole["pin"]
        near = np.hypot(grid.x - pin["x"], grid.z - pin["y"]) < 25.0
        return np.where(near, heights + (grid.x - pin["x"]) * grade, heights)
    edit_heights(folder, edit)


@pytest.fixture
def pooled(make_client, clean_db):
    """A filled pool batch, every hole voted (A best ... D worst), and hole B turned into an unplayable legacy hole
    that was never checked. Returns (logged-in client, settings, {"a".."d": id})."""
    client, settings = make_client(game_key=KEY)
    obi = login(client)
    next_hole(obi)
    ids = fill_pool(obi)
    store = PostgresStore(clean_db)
    for name in VOTERS:
        users.create_user(clean_db, name, "pw")
    for i, hole_id in enumerate(ids):  # first hole: 7 likes, then 6, 5, 4 (all liked, so all can rank: G6-5)
        for j, name in enumerate(VOTERS):
            rate_package(Path(settings.holes_dir) / hole_id, "up" if j < 7 - i else "down", store=store, user=name)
    raise_wall(Path(settings.holes_dir) / ids[1])
    with hole_checks.connect(clean_db) as conn:
        conn.execute("DELETE FROM hole_checks")  # as if made before hole_checks existed
    return obi, settings, dict(zip("abcd", ids))


def top_ids(obi):
    return [h["id"] for h in obi.get("/api/top?limit=20").json()["holes"]]


def game_top_ids(obi):
    r = TestClient(obi.app).get("/api/game/top-holes?limit=20", headers={"Authorization": f"Bearer {KEY}"})
    assert r.status_code == 200, r.text
    return [h["id"] for h in r.json()["holes"]]


def checks(dsn):
    return {r["hole_id"]: r for r in rows(dsn, "SELECT * FROM hole_checks")}


def test_new_pool_holes_are_checked_as_they_join(make_client, clean_db):
    obi = login(make_client()[0])
    next_hole(obi)
    ids = fill_pool(obi)
    recorded = checks(clean_db)
    assert sorted(recorded) == ids and all(r["playable"] and r["reason"] is None for r in recorded.values())
    assert all(r["generator_version"] and r["worst_overshoot_m"] <= 1.0 for r in recorded.values())
    assert all(r["check_version"] == CHECK_VERSION and r["green_pin_slope"] <= 0.04 for r in recorded.values())


def test_backfill_marks_the_bad_package_and_only_it(pooled, clean_db):
    obi, _, ids = pooled
    with obi:  # app startup runs the backfill in the background
        obi.app.state.checks.backfill_thread.join(timeout=60)
    recorded = checks(clean_db)
    assert sorted(recorded) == sorted(ids.values())
    bad = recorded[ids["b"]]
    assert not bad["playable"] and bad["worst_overshoot_m"] > 2.5 and "on the hole line" in bad["reason"]
    assert all(recorded[ids[k]]["playable"] for k in "acd")
    assert obi.app.state.checks.backfill() == []  # one-time: nothing left unchecked


def test_unplayable_hole_never_ranked_or_served(pooled, clean_db):
    obi, _, ids = pooled
    playable = [ids["a"], ids["c"], ids["d"]]
    assert top_ids(obi) == playable  # unchecked: checked on the spot, before the backfill gets to it
    assert not checks(clean_db)[ids["b"]]["playable"]
    assert game_top_ids(obi) == playable
    u1 = login(TestClient(obi.app), "u1", "pw")
    served = [next_hole(u1)["hole"] for _ in range(4)]
    assert sorted(h["id"] for h in served[:3]) == sorted(playable) and served[3] is None
    # The unplayable hole counts toward nothing: u1 has seen the whole batch, so the next one started.
    assert [r["id"] for r in rows(clean_db, "SELECT id FROM batches ORDER BY id")] == [1, 2]
    # Votes on it stay for training.
    assert len(rows(clean_db, "SELECT * FROM latest_votes WHERE hole_id = %s", ids["b"])) == len(VOTERS)


def test_unchecked_bad_pool_hole_is_skipped_by_next(pooled, clean_db):
    obi, _, ids = pooled
    sam = login(TestClient(obi.app), "sam")
    served = [h["id"] for h in (next_hole(sam)["hole"] for _ in range(4)) if h]
    assert sorted(served) == sorted([ids["a"], ids["c"], ids["d"]])  # checked as it came up, then skipped
    assert not checks(clean_db)[ids["b"]]["playable"]


def test_cli_top_and_check_holes(pooled, monkeypatch, capsys):
    _, settings, ids = pooled
    monkeypatch.setenv("TRAINER_DATABASE_URL", settings.database_url)
    monkeypatch.setenv("TRAINER_HOLES_DIR", str(settings.holes_dir))
    server.main(["check-holes"])
    out = capsys.readouterr().out.splitlines()
    assert len(out) == 1 and out[0].startswith(f"{ids['b']}  v") and "on the hole line" in out[0]
    server.main(["top", "--limit", "9"])
    listed = [line.split()[-1] for line in capsys.readouterr().out.splitlines()[1:]]
    assert listed == [ids["a"], ids["c"], ids["d"]]
    monkeypatch.setenv("TRAINER_LAUNCH_FAR_MAX_M", "100")  # looser limits: --all re-judges every hole
    monkeypatch.setenv("TRAINER_LAUNCH_NEAR_MAX_M", "100")
    server.main(["check-holes", "--all"])
    assert capsys.readouterr().out == ""


def profile(at: float, over: float):
    s = np.arange(1.0, 101.0)
    return {"the hole line": (s, np.where(s == at, over, -3.0)), "the left edge": (s, np.full_like(s, -1.0))}


@pytest.mark.parametrize("at,over,playable", [
    (40, 1.0, True), (40, 1.2, False), (60, 1.2, False), (61, 1.2, True), (90, 2.5, True), (90, 2.6, False),
    (5, -0.5, True)])
def test_cutoffs(at, over, playable):
    reason, worst = assess(profile(at, over), Limits())
    assert (reason is None) == playable and worst == max(over, 0.0)
    if not playable:
        assert f"{at} m out on the hole line" in reason


@pytest.mark.parametrize("pin,playable", [(0.03, True), (0.06, True), (0.061, False), (0.15, False)])
def test_green_cutoff(pin, playable):
    reason = assess_green((pin, 0.2), Limits())  # a steep patch away from the pin never counts
    assert (reason is None) == playable
    if not playable:
        assert reason == f"green slopes {pin:.1%} at the pin (limit 6%)"
    assert assess_green((pin, 0.2), Limits(green_pin_max=0.2)) is None and assess_green(None, Limits()) is None


def test_steep_green_leaves_top_holes_and_the_game(pooled, clean_db):
    obi, settings, ids = pooled
    tilt_green(Path(settings.holes_dir) / ids["c"])
    assert top_ids(obi) == [ids["a"], ids["d"]] and game_top_ids(obi) == [ids["a"], ids["d"]]
    bad = checks(clean_db)[ids["c"]]
    assert not bad["playable"] and bad["reason"].startswith("green slopes 15") and bad["green_pin_slope"] > 0.14


def test_older_checks_are_redone(pooled, clean_db, monkeypatch, capsys):
    """A hole judged before the green check (check_version 1, 'playable') is re-checked: on startup, on the spot,
    and by check-holes without --all."""
    obi, settings, ids = pooled
    obi.app.state.checks.backfill()
    tilt_green(Path(settings.holes_dir) / ids["a"])
    with hole_checks.connect(clean_db) as conn:
        conn.execute("UPDATE hole_checks SET check_version = 1, playable = true, reason = NULL WHERE hole_id = %s",
                     [ids["a"]])
    assert hole_checks.candidates(clean_db) == [ids["a"]] and ids["a"] not in hole_checks.verdicts(clean_db)
    assert top_ids(obi) == [ids["c"], ids["d"]]  # the outdated verdict is not trusted: checked on the spot
    assert checks(clean_db)[ids["a"]]["check_version"] == CHECK_VERSION
    with hole_checks.connect(clean_db) as conn:
        conn.execute("UPDATE hole_checks SET check_version = 1, playable = true WHERE hole_id = %s", [ids["a"]])
    monkeypatch.setenv("TRAINER_DATABASE_URL", settings.database_url)
    monkeypatch.setenv("TRAINER_HOLES_DIR", str(settings.holes_dir))
    server.main(["check-holes"])
    out = capsys.readouterr().out
    assert f"{ids['a']}  v" in out and "green slopes" in out and not hole_checks.candidates(clean_db)
    monkeypatch.setenv("TRAINER_GREEN_PIN_MAX_SLOPE", "0.5")  # a looser cutoff: --all re-judges it playable
    server.main(["check-holes", "--all"])
    assert ids["a"] not in capsys.readouterr().out
