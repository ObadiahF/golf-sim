"""Tee-shot playability (hole_checks.py): unplayable holes leave Top holes, the game API and /api/next."""
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
from hole_checks import Limits, assess
from hole_package import read_heights, read_hole
from pg_store import PostgresStore
from terrain import Grid
from test_votes import rows

KEY = "game-key-for-tests"
VOTERS = ("u1", "u2", "u3")


def raise_wall(folder: Path, at: float = 30.0, over: float = 6.0) -> None:
    """Make a legacy-style package: ground `over` m above an 8 deg tee shot `at` m out on the hole line."""
    hole = read_hole(folder)
    heights = read_heights(folder, hole)
    grid = Grid(hole["sizeMeters"], heights.shape[0])
    path = LineString(np.asarray(hole["holePath"]["points"], dtype=float).reshape(-1, 2))
    tee, spot = path.coords[0], path.interpolate(at)
    tee_height = heights.flat[np.argmin(np.hypot(grid.x - tee[0], grid.z - tee[1]))]
    top = tee_height + math.tan(math.radians(8)) * at + over
    disk = np.hypot(grid.x - spot.x, grid.z - spot.y) < 8.0
    heights = np.where(disk, np.maximum(heights, top), heights)
    lo, hi = write_raw16(heights, folder / hole["heightmap"]["file"])
    hole["heightmap"].update(minElevation=round(lo, 3), maxElevation=round(max(hi, lo + 0.001), 3))
    (folder / "hole.json").write_text(json.dumps(hole, indent=1))


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
    for i, hole_id in enumerate(ids):  # first hole: 3 likes, then 2, 1, 0
        for j, name in enumerate(VOTERS):
            rate_package(Path(settings.holes_dir) / hole_id, "up" if j < 3 - i else "down", store=store, user=name)
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
