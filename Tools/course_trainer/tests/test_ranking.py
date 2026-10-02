from pathlib import Path

import pytest

import server
import users
from conftest import fill_pool, login, next_hole
from gen_hole import rate_package
from pg_store import PostgresStore
from ranking import liked, wilson_lower_bound

KEY = "game-key-for-tests"


def test_wilson_lower_bound_beats_the_raw_ratio():
    assert wilson_lower_bound(0, 0) == 0
    assert wilson_lower_bound(1, 0) == pytest.approx(0.2065, abs=1e-4)
    # 4 likes of 5 (80%) is more convincing than 1 of 1 (100%), and 9 of 10 more than either.
    assert wilson_lower_bound(9, 1) > wilson_lower_bound(4, 1) > wilson_lower_bound(1, 0) > wilson_lower_bound(1, 1)


@pytest.fixture
def ranked(make_client, clean_db):
    """A pool batch with votes: B 4 up 1 down, A 1 up, C 1 up 1 down, D 1 down. Returns (client, settings, ids)."""
    client, settings = make_client(game_key=KEY)
    obi = login(client)
    next_hole(obi)
    a, b, c, d = fill_pool(obi)
    store = PostgresStore(clean_db)
    for name in ("u1", "u2", "u3"):
        users.create_user(clean_db, name, "pw")

    def vote(hole_id, name, rating):
        rate_package(Path(settings.holes_dir) / hole_id, rating, store=store, user=name)

    vote(a, "obi", "down")
    vote(a, "obi", "up")  # only each person's latest vote counts
    for name in ("obi", "sam", "u1", "u2"):
        vote(b, name, "up")
    vote(b, "u3", "down")
    vote(c, "sam", "up")
    vote(c, "u1", "down")
    vote(d, "sam", "down")
    return obi, settings, {"a": a, "b": b, "c": c, "d": d}


def test_top_ranks_by_bayesian_score(ranked):
    obi, _, ids = ranked
    data = obi.get("/api/top?limit=20").json()
    holes = data["holes"]
    assert data["formula"] == "wilson-95"
    assert [h["id"] for h in holes] == [ids["b"], ids["a"]]  # raw ratio would put A (1/1) first; C, D not liked
    top = holes[0]
    assert (top["rank"], top["ups"], top["downs"], top["score"]) == (1, 4, 1, pytest.approx(0.3755, abs=1e-3))
    assert top["rating"] == "up" and holes[1]["rating"] == "up"  # your own vote, as in summaries
    assert top["previewUrl"] == f"/api/holes/{ids['b']}/preview.png" and top["par"] in (3, 4, 5)
    assert top["preset"] and top["lengthMeters"] > 50
    assert obi.get(top["previewUrl"]).headers["content-type"] == "image/png"
    assert [h["id"] for h in obi.get("/api/top?limit=1").json()["holes"]] == [ids["b"]]


def test_game_key_opens_only_the_game_routes(ranked):
    _, settings, ids = ranked
    from fastapi.testclient import TestClient
    game = TestClient(ranked[0].app)  # no session cookie
    auth = {"Authorization": f"Bearer {KEY}"}

    r = game.get("/api/game/top-holes?limit=2", headers=auth)
    assert r.status_code == 200 and r.headers["content-type"] == "application/json"
    holes = r.json()["holes"]
    assert [h["id"] for h in holes] == [ids["b"], ids["a"]]
    assert set(holes[0]) == {"rank", "id", "preset", "theme", "par", "lengthMeters", "ups", "downs", "score",
                             "previewUrl", "files"}
    assert holes[0]["files"]["hole.json"] == f"/api/game/holes/{ids['b']}/hole.json"
    assert set(holes[0]["files"]) == {"hole.json", "heightmap.raw", "objects.bin", "gen.json", "preview.png"}
    pkg = game.get(holes[0]["files"]["hole.json"], headers=auth)
    assert pkg.status_code == 200 and pkg.json()["id"] == ids["b"] and "private" in pkg.headers["cache-control"]
    raw = game.get(holes[0]["files"]["heightmap.raw"], headers=auth)
    assert len(raw.content) == pkg.json()["heightmap"]["resolution"] ** 2 * 2

    for headers in ({}, {"Authorization": "Bearer wrong"}, {"Authorization": KEY}):
        assert game.get("/api/game/top-holes", headers=headers).status_code == 401
        assert game.get(holes[0]["files"]["hole.json"], headers=headers).status_code == 401
    for path in ("/api/top", "/api/status", "/api/next", f"/api/holes/{ids['b']}/hole.json", "/api/export/votes.jsonl"):
        assert game.get(path, headers=auth).status_code == 401  # the key is not a session
    assert game.post("/api/rate", json={"id": ids["b"], "rating": "up"}, headers=auth).status_code == 401
    assert game.get(f"/api/game/holes/{ids['b']}/secret.txt", headers=auth).status_code == 404
    assert game.get("/api/game/holes/..%2F..%2Fetc/hole.json", headers=auth).status_code == 404


def test_game_api_is_off_without_a_key(make_client):
    client, _ = make_client()
    r = client.get("/api/game/top-holes", headers={"Authorization": "Bearer "})
    assert r.status_code == 404


def test_top_cli_prints_and_exports(ranked, monkeypatch, capsys, tmp_path):
    _, settings, ids = ranked
    monkeypatch.setenv("TRAINER_DATABASE_URL", settings.database_url)
    monkeypatch.setenv("TRAINER_HOLES_DIR", str(settings.holes_dir))
    out = tmp_path / "export"
    server.main(["top", "--limit", "2", "--export", str(out)])
    lines = capsys.readouterr().out.splitlines()
    assert len(lines) == 3 and lines[1].split()[-1] == ids["b"] and lines[2].split()[-1] == ids["a"]
    assert sorted(p.name for p in out.iterdir()) == sorted([ids["a"], ids["b"]])
    assert {p.name for p in (out / ids["b"]).iterdir()} == {"hole.json", "heightmap.raw", "objects.bin", "gen.json",
                                                           "preview.png"}


def test_liked_needs_more_likes_than_dislikes():
    assert liked({"ups": 1, "downs": 0}) and liked({"ups": 4, "downs": 3})
    assert not liked({"ups": 0, "downs": 0}) and not liked({"ups": 0, "downs": 2}) and not liked({"ups": 1, "downs": 1})
    assert not liked({"ups": 1, "downs": 0}, min_likes=2) and liked({"ups": 2, "downs": 1}, min_likes=2)


def test_top_holes_leave_out_disliked_holes(ranked, make_client):
    """G6-5: holes with only 👎 (D) or no net 👍 (C) are never in Top holes or the game's round, even when the game asks
    for more holes than qualify; TRAINER_TOP_MIN_LIKES raises the bar."""
    obi, _, ids = ranked
    from fastapi.testclient import TestClient
    auth = {"Authorization": f"Bearer {KEY}"}

    def game_ids(client):
        r = TestClient(client.app).get("/api/game/top-holes?limit=100", headers=auth)
        assert r.status_code == 200
        return [h["id"] for h in r.json()["holes"]]

    assert game_ids(obi) == [ids["b"], ids["a"]]  # 2 of the 100 asked for: what qualifies, no padding
    assert [h["id"] for h in obi.get("/api/top?limit=100").json()["holes"]] == [ids["b"], ids["a"]]
    strict = login(make_client(game_key=KEY, top_min_likes=2)[0])
    assert game_ids(strict) == [ids["b"]]  # A has 1 like only
    assert [h["id"] for h in strict.get("/api/top").json()["holes"]] == [ids["b"]]
