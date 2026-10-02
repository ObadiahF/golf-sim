import json

import pytest

import db
import preference
import server
import users
from conftest import generate, login
from gen_hole import train_and_save
from pg_store import PostgresStore


def vote(client, hole_id, rating, **extra):
    r = client.post("/api/rate", json={"id": hole_id, "rating": rating, **extra})
    assert r.status_code == 200, r.text
    return r.json()


def rows(dsn, sql, *args):
    with db.connect(dsn) as conn:
        return conn.execute(sql, args).fetchall()


def test_latest_vote_per_user_wins_and_history_is_kept(make_client, clean_db):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    hole = generate(obi)
    vote(obi, hole["id"], "up", comment="lovely", tags=["more_trees"])
    vote(obi, hole["id"], "down", tags=["too_long", "boring"])
    vote(sam, hole["id"], "up")

    store = PostgresStore(clean_db)
    latest = {e["user"]: e for e in store.load()}
    assert latest["obi"]["rating"] == -1 and latest["obi"]["tags"] == ["too_long", "boring"]
    assert "comment" not in latest["obi"] and latest["sam"]["rating"] == 1
    assert len(store.export(history=True)) == 3 and len(store.export()) == 2
    assert [e["rating"] for e in store.load("SAM")] == [1]

    snap = rows(clean_db, "SELECT preset, theme, par, params, seed, generator_version, model_score, tags"
                          " FROM votes ORDER BY id")[0]
    assert snap["preset"] == "links" and snap["seed"] == 3 and snap["params"] == hole["params"]
    assert snap["par"] == hole["par"] and snap["generator_version"] >= 2 and snap["tags"] == ["more_trees"]

    assert obi.get(f"/api/holes/{hole['id']}").json()["rating"] == "down"  # each sees their own vote
    assert sam.get(f"/api/holes/{hole['id']}").json()["rating"] == "up"
    status = obi.get("/api/status").json()
    assert status["ratings"] == 2 and status["up"] == 1 and status["yours"] == 1


def test_recent_holes_are_per_user(make_client):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    mine = generate(obi, seed=11)
    theirs = generate(sam, seed=12)
    assert [h["id"] for h in obi.get("/api/holes").json()["holes"]] == [mine["id"]]
    assert [h["id"] for h in sam.get("/api/holes").json()["holes"]] == [theirs["id"]]
    obi.get(f"/api/holes/{theirs['id']}/hole.json")  # opening a shared link adds it to my list
    assert [h["id"] for h in obi.get("/api/holes").json()["holes"]] == [theirs["id"], mine["id"]]


def test_train_from_postgres_logs_the_run(make_client, clean_db):
    obi = login(make_client()[0])
    sam = login(make_client()[0], "sam")
    a, b = generate(obi, seed=21), generate(obi, seed=22)
    vote(obi, a["id"], "up")
    vote(sam, a["id"], "down")
    vote(sam, b["id"], "down")

    assert obi.post("/api/train").json()["trainedOn"] == 3
    assert obi.post("/api/train?only=sam").json()["trainedOn"] == 2
    assert obi.post("/api/train?only=nobody").status_code == 409
    runs = rows(clean_db, "SELECT u.name, r.only_user, r.vote_count FROM training_runs r"
                          " JOIN users u ON u.id = r.user_id ORDER BY r.id")
    assert [(r["name"], r["only_user"], r["vote_count"]) for r in runs] == [("obi", None, 3), ("obi", "sam", 2)]
    assert preference.MODEL_PATH.exists()
    # The model input format is unchanged: the same entries train directly too.
    assert train_and_save(PostgresStore(clean_db))["ratings"] == 3


def test_export_endpoint_and_cli(make_client, clean_db, capsys, monkeypatch):
    obi = login(make_client()[0])
    hole = generate(obi)
    vote(obi, hole["id"], "up", comment="nice")
    vote(obi, hole["id"], "down")

    r = obi.get("/api/export/votes.jsonl")
    assert r.status_code == 200 and "attachment" in r.headers["content-disposition"]
    lines = [json.loads(line) for line in r.text.splitlines()]
    assert len(lines) == 1 and lines[0]["user"] == "obi" and lines[0]["rating"] == -1
    assert set(lines[0]) >= {"id", "rating", "time", "style"} and set(lines[0]["style"]) == {
        "preset", "theme", "par", "params"}
    assert len(obi.get("/api/export/votes.jsonl?history=true").text.splitlines()) == 2

    monkeypatch.setenv("TRAINER_DATABASE_URL", clean_db)
    server.main(["export", "--history"])
    exported = [json.loads(line) for line in capsys.readouterr().out.splitlines()]
    assert [e["rating"] for e in exported] == [1, -1] and exported[0]["comment"] == "nice"

    # An export is a valid ratings file for the CLI / Unity side.
    path = preference.RATINGS_PATH
    path.write_text(r.text)
    assert preference.load_ratings(path) == lines


def test_legacy_ratings_import_once(clean_db, tmp_path):
    log = tmp_path / "old.jsonl"
    style = {"preset": "links", "theme": "links", "par": 4, "params": {"water": 0.2}}
    log.write_text("".join(json.dumps({"id": f"h{i}", "rating": 1, "time": 1700000000.5 + i, "style": style}) + "\n"
                           for i in range(3)))
    assert users.seed(clean_db, "", log)[-1].startswith("imported 3 legacy vote(s)")
    assert users.seed(clean_db, "", log) == []
    entries = PostgresStore(clean_db).load("legacy")
    assert [e["id"] for e in entries] == ["h0", "h1", "h2"] and entries[0]["time"] == pytest.approx(1700000000.5)


def test_prune_keeps_voted_and_recently_viewed_holes(make_client):
    client = login(make_client(keep_unrated=1, view_grace_hours=0)[0])
    voted = generate(client, seed=31)
    vote(client, voted["id"], "up")
    old = generate(client, seed=32)
    newest = generate(client, seed=33)
    assert old["id"] in newest["pruned"] and voted["id"] not in newest["pruned"]
    assert client.get(f"/api/holes/{voted['id']}").status_code == 200

    client = login(make_client(keep_unrated=1, view_grace_hours=1)[0])
    viewed = generate(client, seed=34)
    assert generate(client, seed=35)["pruned"] == []
    assert client.get(f"/api/holes/{viewed['id']}").status_code == 200
