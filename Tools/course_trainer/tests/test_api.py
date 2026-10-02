import json
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _paths  # noqa: E402,F401
import preference  # noqa: E402
from api import create_app  # noqa: E402
from fastapi.testclient import TestClient  # noqa: E402


@pytest.fixture
def env(tmp_path, monkeypatch):
    monkeypatch.setattr(preference, "RATINGS_PATH", tmp_path / "ratings.jsonl")
    monkeypatch.setattr(preference, "MODEL_PATH", tmp_path / "model.npz")
    dist = tmp_path / "dist"
    dist.mkdir()
    (dist / "index.html").write_text("<html>trainer</html>")
    client = TestClient(create_app(tmp_path / "holes", dist))
    return client, tmp_path


def generate(client, **body):
    r = client.post("/api/generate", json={"preset": "links", "seed": 3, **body})
    assert r.status_code == 200, r.text
    return r.json()


def test_presets_include_feedback_tags(env):
    client, _ = env
    data = client.get("/api/presets").json()
    assert {p["name"] for p in data["presets"]} >= {"forest", "lakes", "links"}
    assert "tree_density" in data["params"] and data["pars"] == [3, 4, 5]
    assert any(t["id"] == "more_trees" for t in data["feedback"])


def test_generate_serves_package_files(env):
    client, tmp = env
    hole = generate(client, par=3, overrides={"water": 0})
    assert hole["preset"] == "links" and hole["par"] == 3 and hole["params"]["water"] == 0
    assert hole["lengthMeters"] > 50 and hole["rating"] is None
    pkg = client.get(f"/api/holes/{hole['id']}/hole.json").json()
    raw = client.get(f"/api/holes/{hole['id']}/heightmap.raw")
    assert len(raw.content) == pkg["heightmapResolution"] ** 2 * 2
    assert client.get("/api/holes").json()["holes"][0]["id"] == hole["id"]


def test_rejects_bad_requests(env):
    client, _ = env
    assert client.post("/api/generate", json={"preset": "moon"}).status_code == 400
    assert client.post("/api/generate", json={"preset": "links", "overrides": {"lava": 1}}).status_code == 400
    assert client.post("/api/generate", json={"preset": "links", "par": 7}).status_code == 400
    assert client.get("/api/holes/nope/hole.json").status_code == 404
    assert client.get("/api/holes/..%2F..%2Fetc/hole.json").status_code == 404
    hole = generate(client)
    assert client.get(f"/api/holes/{hole['id']}/secret.txt").status_code == 404
    bad_tag = client.post("/api/rate", json={"id": hole["id"], "rating": "up", "tags": ["more_lava"]})
    assert bad_tag.status_code == 400
    assert client.post("/api/train").status_code == 409


def test_rate_with_comment_and_tags_then_train(env):
    client, tmp = env
    hole = generate(client)
    r = client.post("/api/rate", json={"id": hole["id"], "rating": "down", "comment": "too windy",
                                       "tags": ["fewer_bunkers"]})
    assert r.status_code == 200 and r.json()["status"]["ratings"] == 1
    entry = json.loads((tmp / "ratings.jsonl").read_text())
    assert entry["comment"] == "too windy" and entry["tags"] == ["fewer_bunkers"] and entry["rating"] == -1
    assert client.get(f"/api/holes/{hole['id']}").json()["rating"] == "down"
    trained = client.post("/api/train?preset=links").json()
    assert trained["trainedOn"] == 1 and trained["preset"] == "links"


def test_serves_built_frontend(env):
    client, _ = env
    assert "trainer" in client.get("/").text
