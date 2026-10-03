"""Random rounds for the game (random_holes.py, GET /api/game/random-holes): every playable, not net-disliked pool or
voted hole, weighted 1 + likes - dislikes, no repeats, recently played holes last."""
import random
from collections import Counter
from pathlib import Path

import pytest
from fastapi.testclient import TestClient

import hole_checks
import users
from conftest import fill_pool, generate, login, next_hole
from game import FIELDS
from gen_hole import rate_package
from pg_store import PostgresStore
from random_holes import eligible, weight, weighted_order
from test_hole_checks import raise_wall

KEY = "game-key-for-tests"
AUTH = {"Authorization": f"Bearer {KEY}"}


@pytest.fixture
def pool6(make_client, clean_db):
    """A pool batch of six: A 3 likes, B 1 like 1 dislike, C 2 dislikes, D and F unrated, E unrated and unplayable
    (never checked), plus an unrated ad hoc hole. Returns (game client, {"a".."f", "adhoc": id})."""
    client, settings = make_client(game_key=KEY, pool_batch_size=6)
    obi = login(client)
    next_hole(obi)
    ids = dict(zip("abcdef", fill_pool(obi)))
    ids["adhoc"] = generate(obi)["id"]
    store = PostgresStore(clean_db)
    for name in ("u1", "u2"):
        users.create_user(clean_db, name, "pw")

    def vote(key, name, rating):
        rate_package(Path(settings.holes_dir) / ids[key], rating, store=store, user=name)

    for name in ("obi", "sam", "u1"):
        vote("a", name, "up")
    vote("b", "obi", "up")
    vote("b", "sam", "down")
    vote("c", "obi", "down")
    vote("c", "sam", "down")
    raise_wall(Path(settings.holes_dir) / ids["e"])
    with hole_checks.connect(clean_db) as conn:
        conn.execute("DELETE FROM hole_checks")  # as if made before hole_checks existed
    return TestClient(obi.app), ids


def pick(game, **query):
    r = game.get("/api/game/random-holes", params=query, headers=AUTH)
    assert r.status_code == 200, r.text
    return r.json()


def test_weight_and_eligibility():
    assert weight({"ups": 0, "downs": 0}) == 1 and weight({"ups": 3, "downs": 0}) == 4
    assert weight({"ups": 2, "downs": 2}) == 1 and weight({"ups": 5, "downs": 2}) == 4
    assert eligible({"ups": 0, "downs": 0}) and eligible({"ups": 1, "downs": 1})
    assert not eligible({"ups": 0, "downs": 1}) and not eligible({"ups": 2, "downs": 3})


def test_weighted_order_favours_liked_holes_but_plays_everything():
    tallies = [{"id": "liked", "ups": 3, "downs": 0}, *({"id": f"new{i}", "ups": 0, "downs": 0} for i in range(3))]
    rng = random.Random(7)
    firsts = Counter()
    for _ in range(4000):
        order = weighted_order(tallies, rng)
        assert sorted(t["id"] for t in order) == sorted(t["id"] for t in tallies)  # each once
        firsts[order[0]["id"]] += 1
    assert firsts["liked"] / 4000 == pytest.approx(4 / 7, abs=0.03)  # weight 4 of 4 + 1 + 1 + 1
    assert all(firsts[f"new{i}"] / 4000 == pytest.approx(1 / 7, abs=0.03) for i in range(3))


def test_random_round_is_the_playable_not_disliked_pool(pool6):
    game, ids = pool6
    data = pick(game, count=20)
    got = [h["id"] for h in data["holes"]]
    assert len(got) == len(set(got))  # no repeats
    assert sorted(got) == sorted(ids[k] for k in "abdf")  # not C (disliked), E (unplayable), the ad hoc hole
    assert not hole_checks.verdicts(game.app.state.checks.dsn)[ids["e"]]  # E was checked when it came up
    assert data["formula"] == "wilson-95" and data["weighting"] == "1+likes-dislikes"
    assert [h["rank"] for h in data["holes"]] == [1, 2, 3, 4]
    first = data["holes"][0]
    assert set(first) == set(FIELDS) and first["files"]["hole.json"] == f"/api/game/holes/{first['id']}/hole.json"
    assert game.get(first["files"]["hole.json"], headers=AUTH).status_code == 200
    assert len(pick(game, count=2)["holes"]) == 2
    for headers in ({}, {"Authorization": "Bearer wrong"}):
        assert game.get("/api/game/random-holes", headers=headers).status_code == 401


def test_recently_played_holes_come_last(pool6):
    game, ids = pool6
    recent = f"{ids['a']}, {ids['d']},unknown-hole"
    for _ in range(5):
        assert sorted(h["id"] for h in pick(game, count=2, exclude=recent)["holes"]) == sorted([ids["b"], ids["f"]])
        got = [h["id"] for h in pick(game, count=3, exclude=recent)["holes"]]
        assert sorted(got[:2]) == sorted([ids["b"], ids["f"]]) and got[2] in (ids["a"], ids["d"])  # pool too small


def test_liked_holes_come_up_more_often(pool6):
    game, ids = pool6
    firsts = Counter(pick(game, count=1)["holes"][0]["id"] for _ in range(150))
    # A weighs 4 of 4 + 1 + 1 + 1 (B, D, F): ~86 of 150, the others ~21 each.
    assert firsts[ids["a"]] > 60 and all(firsts[ids[k]] > 5 for k in "bdf")
