from conftest import generate


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
