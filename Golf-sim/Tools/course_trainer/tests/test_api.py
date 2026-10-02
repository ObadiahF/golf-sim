import json

import pytest

from conftest import generate, login


def test_presets_include_feedback_tags(client):
    data = client.get("/api/presets").json()
    assert {p["name"] for p in data["presets"]} >= {"forest", "lakes", "links"}
    assert "tree_density" in data["params"] and data["pars"] == [3, 4, 5]
    assert any(t["id"] == "more_trees" for t in data["feedback"])


def test_generate_serves_package_files(client):
    hole = generate(client, par=3, overrides={"water": 0})
    assert hole["preset"] == "links" and hole["par"] == 3 and hole["params"]["water"] == 0
    assert hole["lengthMeters"] > 50 and hole["rating"] is None
    pkg = client.get(f"/api/holes/{hole['id']}/hole.json").json()
    raw = client.get(f"/api/holes/{hole['id']}/heightmap.raw")
    assert len(raw.content) == pkg["heightmap"]["resolution"] ** 2 * 2
    assert client.get(f"/api/holes/{hole['id']}/objects.bin").status_code == 200
    assert client.get("/api/holes").json()["holes"][0]["id"] == hole["id"]


def test_rejects_bad_requests(client):
    assert client.post("/api/generate", json={"preset": "moon"}).status_code == 400
    assert client.post("/api/generate", json={"preset": "links", "overrides": {"lava": 1}}).status_code == 400
    assert client.post("/api/generate", json={"preset": "links", "par": 7}).status_code == 400
    assert client.post("/api/generate", json={"preset": "links", "seed": 2 ** 64}).status_code == 422
    assert client.get("/api/holes/nope/hole.json").status_code == 404
    assert client.get("/api/holes/..%2F..%2Fetc/hole.json").status_code == 404
    hole = generate(client)
    assert client.get(f"/api/holes/{hole['id']}/secret.txt").status_code == 404
    bad_tag = client.post("/api/rate", json={"id": hole["id"], "rating": "up", "tags": ["more_lava"]})
    assert bad_tag.status_code == 400
    assert client.post("/api/train").status_code == 409


def test_rate_with_comment_and_tags_then_train(client):
    hole = generate(client)
    r = client.post("/api/rate", json={"id": hole["id"], "rating": "down", "comment": "too windy",
                                       "tags": ["fewer_bunkers"]})
    assert r.status_code == 200 and r.json()["status"]["ratings"] == 1
    entry = r.json()["entry"]
    assert entry["comment"] == "too windy" and entry["tags"] == ["fewer_bunkers"] and entry["rating"] == -1
    assert entry["user"] == "obi"
    assert client.get(f"/api/holes/{hole['id']}").json()["rating"] == "down"
    trained = client.post("/api/train?preset=links").json()
    assert trained["trainedOn"] == 1 and trained["preset"] == "links"


def test_serves_built_frontend_without_login(make_client):
    client, _ = make_client()
    assert "trainer" in client.get("/").text
    assert client.get("/healthz").json() == {"ok": True}


BAD_TEXT = ["a\u0000b", "\ud800"]


def post_raw(client, url, body):
    """JSON with \\u escapes, as a browser sends it (httpx can't encode a lone surrogate itself)."""
    return client.post(url, content=json.dumps(body), headers={"Content-Type": "application/json"})


@pytest.mark.parametrize("bad", BAD_TEXT)
def test_nul_and_lone_surrogates_are_rejected_cleanly(client, bad):
    """T-9: Postgres can't store NUL, UTF-8 can't encode a lone surrogate: 422, never 500."""
    hole = generate(client)
    for body in ({"id": hole["id"], "rating": "up", "comment": bad},
                 {"id": hole["id"], "rating": "up", "tags": [bad]},
                 {"id": bad, "rating": "up"}):
        assert post_raw(client, "/api/rate", body).status_code == 422
    assert post_raw(client, "/api/generate", {"preset": bad}).status_code == 422
    assert post_raw(client, "/api/generate", {"preset": "links", "overrides": {bad: 0.5}}).status_code == 422
    assert post_raw(client, "/api/login", {"name": bad, "password": "x"}).status_code == 422
    assert client.get("/api/status?preset=%ED%A0%80%00").status_code == 400  # echoed back, ASCII-escaped


def test_overlong_ids_are_404_not_500(make_client):
    client, _ = make_client(game_key="k" * 24)
    login(client)
    long_id = "a" * 300
    assert client.get(f"/api/holes/{long_id}").status_code == 404
    assert client.get(f"/api/holes/{long_id}/hole.json").status_code == 404
    assert client.get(f"/api/game/holes/{long_id}/hole.json",
                      headers={"Authorization": f"Bearer {'k' * 24}"}).status_code == 404
    assert client.post("/api/rate", json={"id": long_id, "rating": "up"}).status_code == 422


@pytest.mark.parametrize("value", ["NaN", "Infinity", "-Infinity"])
def test_non_finite_overrides_are_rejected(client, value):
    body = '{"preset": "links", "overrides": {"water": %s}}' % value
    r = client.post("/api/generate", content=body, headers={"Content-Type": "application/json"})
    assert r.status_code == 422


def test_course_gen_system_exit_becomes_400(client, monkeypatch):
    """course_gen rejects bad input CLI-style (SystemExit); inside a request that must be a 400."""
    import api

    def cli_reject(*_args, **_kwargs):
        raise SystemExit("Parameter 'water' needs a finite number in 0..1")
    monkeypatch.setattr(api, "generate_hole", cli_reject)
    r = client.post("/api/generate", json={"preset": "links"})
    assert r.status_code == 400 and "finite" in r.json()["detail"]


def test_contradictory_tags_are_rejected(client):
    """T-14: opposites come from course_gen's feedback table, and the server refuses both at once."""
    tags = {t["id"]: t["excludes"] for t in client.get("/api/presets").json()["feedback"]}
    assert tags["too_long"] == ["too_short"]
    hole = generate(client)
    r = client.post("/api/rate", json={"id": hole["id"], "rating": "up", "tags": ["too_long", "too_short"]})
    assert r.status_code == 400 and "Contradictory" in r.json()["detail"]


def test_status_tally_is_per_preset(client):
    """T-15: the taste card's tally counts only the named preset's votes."""
    links = generate(client)
    lakes = generate(client, preset="lakes")
    client.post("/api/rate", json={"id": links["id"], "rating": "up"})
    client.post("/api/rate", json={"id": lakes["id"], "rating": "down"})
    assert client.get("/api/status?preset=links").json()["presetVotes"] == {"up": 1, "down": 0}
    assert client.get("/api/status?preset=lakes").json()["presetVotes"] == {"up": 0, "down": 1}
    assert client.get("/api/status").json()["presetVotes"] == {"up": 1, "down": 1}


def test_api_docs_only_when_enabled(make_client):
    """T-13: /docs, /redoc and /openapi.json are off unless TRAINER_API_DOCS is set."""
    off, _ = make_client()
    on, _ = make_client(api_docs=True)
    for path in ("/docs", "/redoc", "/openapi.json"):
        assert off.get(path).status_code == 404
        assert on.get(path).status_code == 200
