"""Course types for the game: GET /api/game/random-holes?preset=, GET /api/game/presets, and the pool's per-preset
stock (TRAINER_POOL_MIN_STOCK: stock batches of the most-short preset, pool.start_stock / pool_worker.py)."""
from pathlib import Path

import pytest

import pool
import pool_worker
from conftest import login
from gen_hole import rate_package
from pg_store import PostgresStore
from random_holes import preset_counts
from style import PRESETS
from test_pool import batches
from test_random_holes import AUTH, KEY, pick
from test_votes import rows

STOCKED = ("desert", "links")  # the presets the worker stocks in these tests (all twelve would be slow)


def stock_batches(dsn):
    return [(r["preset"], r["size"]) for r in rows(dsn, "SELECT preset, size FROM batches ORDER BY id")]


def pool_presets(dsn):
    """preset -> its pool hole ids."""
    out = {}
    for r in rows(dsn, "SELECT hole_id, preset FROM pool_holes ORDER BY hole_id"):
        out.setdefault(r["preset"], []).append(r["hole_id"])
    return out


@pytest.fixture
def stocked(make_client, clean_db, monkeypatch):
    """Two holes each of desert and links from stock batches (no preference batch). Returns (game client, settings,
    {preset: [ids]})."""
    monkeypatch.setattr(pool_worker, "PRESETS", {name: PRESETS[name] for name in STOCKED})
    client, settings = make_client(game_key=KEY, pool_min_stock=2)
    assert client.app.state.pool.run_pending() == 4
    return client, settings, pool_presets(clean_db)


def dislike(clean_db, settings, hole_id):
    store = PostgresStore(clean_db)
    for name in ("obi", "sam"):
        rate_package(Path(settings.holes_dir) / hole_id, "down", store=store, user=name)


def test_shortfalls_most_short_first():
    order = ["parkland", "desert", "links", "winter"]
    counts = {"parkland": 30, "desert": 20, "links": 5}
    assert pool.shortfalls(counts, 27, order) == [("winter", 27), ("links", 22), ("desert", 7)]
    assert pool.shortfalls({}, 3, order) == [(name, 3) for name in order]  # ties: in the given order
    assert pool.shortfalls({name: 3 for name in order}, 3, order) == []


def test_stock_waits_for_generating_batches_and_skips_a_stuck_preset(clean_db):
    short = [("winter", 9), ("links", 4)]
    pool.ensure_batch(clean_db, 4)  # a preference batch is generating: no stock batch alongside it
    assert pool.start_stock(clean_db, short, 100) is None
    pool.mark_ready(clean_db, 1)
    winter = pool.start_stock(clean_db, short, 5)
    assert (winter.preset, winter.size) == ("winter", 5)  # at most a batch's size
    pool.mark_ready(clean_db, winter.id)  # every winter generation failed: no playable hole
    links = pool.start_stock(clean_db, short, 100)
    assert (links.preset, links.size) == ("links", 4)  # winter waits EMPTY_RETRY_MINUTES, the next preset goes
    pool.mark_ready(clean_db, links.id)
    with pool.connect(clean_db) as conn:
        conn.execute("UPDATE batches SET created_at = now() - interval '1 hour' WHERE preset = 'winter'")
    assert pool.start_stock(clean_db, short, 100).preset == "winter"


def test_worker_keeps_every_preset_stocked(stocked, clean_db):
    game, settings, ids = stocked
    assert stock_batches(clean_db) == [("desert", 2), ("links", 2)]  # most short first; ties in PRESETS order
    assert {p: len(h) for p, h in ids.items()} == {"desert": 2, "links": 2}
    assert all(h.startswith(p + "_") for p, hs in ids.items() for h in hs)
    worker = game.app.state.pool
    assert worker.run_pending() == 0 and len(batches(clean_db)) == 2  # stocked: nothing more
    dislike(clean_db, settings, ids["desert"][0])  # net-disliked: no longer a game hole, so desert is short again
    assert preset_counts(clean_db, worker.checks.holes) == {"desert": 1, "links": 2}
    assert worker.run_pending() == 1
    assert stock_batches(clean_db)[-1] == ("desert", 1)
    assert preset_counts(clean_db, worker.checks.holes) == {"desert": 2, "links": 2}


def test_stock_batches_do_not_count_toward_the_refill(make_client, clean_db):
    """The next preference batch still starts on its own share: rating all of a small stock batch doesn't start one."""
    obi = login(make_client()[0])
    pool.ensure_batch(clean_db, 4)
    worker = obi.app.state.pool
    assert worker.run_pending() == 4
    pool.start_stock(clean_db, [("links", 1)], 4)
    assert worker.run_pending() == 1
    (hole,) = rows(clean_db, "SELECT hole_id FROM pool_holes WHERE batch_id = 2")
    assert obi.post("/api/rate", json={"id": hole["hole_id"], "rating": "up"}).status_code == 200
    assert len(batches(clean_db)) == 2 and pool.maybe_refill(clean_db, 4, 0.5, 100) is None


def test_random_round_of_chosen_presets(stocked):
    game, _, ids = stocked
    data = pick(game, count=9, preset="desert")
    assert sorted(h["id"] for h in data["holes"]) == ids["desert"]
    assert data["presets"] == ["desert"] and data["matched"] == 2 and data["weighting"] == "1+likes-dislikes"
    both = pick(game, count=3, preset=" Desert ,links,desert")
    assert len(both["holes"]) == 3 and both["presets"] == ["desert", "links"] and both["matched"] == 4
    anything = pick(game, count=9, preset="")
    assert len(anything["holes"]) == 4 and anything["presets"] == [] and anything["matched"] == 4
    none = pick(game, count=9, preset="winter")
    assert none["holes"] == [] and none["matched"] == 0


def test_chosen_presets_keep_recently_played_holes_last(stocked):
    game, _, ids = stocked
    first, second = ids["links"]
    for _ in range(5):
        got = [h["id"] for h in pick(game, count=2, preset="links", exclude=first)["holes"]]
        assert got == [second, first]  # short of fresh links holes: the recently played one fills in
        assert [h["id"] for h in pick(game, count=1, preset="links", exclude=first)["holes"]] == [second]


def test_unknown_preset_is_a_400(stocked):
    game, _, _ = stocked
    r = game.get("/api/game/random-holes", params={"preset": "desert,moon"}, headers=AUTH)
    assert r.status_code == 400
    assert r.json()["detail"] == f"Unknown preset 'moon'. Available: {', '.join(PRESETS)}"


def test_presets_listing(stocked, clean_db):
    game, settings, ids = stocked
    r = game.get("/api/game/presets", headers=AUTH)
    assert r.status_code == 200, r.text
    data = r.json()
    assert data["stock"] == 2 and [p["id"] for p in data["presets"]] == list(PRESETS)
    by_id = {p["id"]: p for p in data["presets"]}
    assert by_id["desert"] == {"id": "desert", "name": "Desert", "theme": "desert", "playable": 2}
    assert by_id["canyon"] == {"id": "canyon", "name": "Red Rock Canyon", "theme": "canyon", "playable": 0}
    dislike(clean_db, settings, ids["links"][0])
    assert {p["id"]: p["playable"] for p in game.get("/api/game/presets", headers=AUTH).json()["presets"]}["links"] == 1
    assert game.get("/api/game/presets").status_code == 401
