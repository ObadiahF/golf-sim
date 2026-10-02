import json
import os
import time

import numpy as np

import gen_hole
from rating_store import DB_ENV, JsonlStore, configured_store, to_jsonl
from style import PRESETS


def info(hole_id="h1", seed=0):
    return {"id": hole_id, **PRESETS["links"].sample(np.random.default_rng(seed)).to_json()}


def test_jsonl_store_is_the_default_and_keeps_the_old_format(isolated_data, monkeypatch):
    monkeypatch.delenv(DB_ENV, raising=False)
    store = configured_store()
    assert isinstance(store, JsonlStore)
    store.record(info(), 1, comment="nice", tags=["more_trees"])
    store.record(info(), -1)
    line = json.loads((isolated_data / "ratings.jsonl").read_text().splitlines()[0])
    assert set(line) == {"id", "rating", "time", "style", "tags", "comment"}  # no user key unless given
    assert [r["rating"] for r in store.load()] == [-1]
    assert len(store.export(history=True)) == 2 and store.rated_ids() == {"h1"}


def test_latest_vote_is_per_user(tmp_path):
    store = JsonlStore(tmp_path / "votes.jsonl")
    store.record(info(), 1, user="obi")
    store.record(info(), -1, user="sam")
    store.record(info(), -1, user="obi")
    assert sorted((r["user"], r["rating"]) for r in store.load()) == [("obi", -1), ("sam", -1)]
    assert [r["rating"] for r in store.load("obi")] == [-1]
    assert to_jsonl(store.export(history=True)) == (tmp_path / "votes.jsonl").read_text()


def test_cli_trains_from_an_exported_file(isolated_data, monkeypatch, capsys):
    export = isolated_data / "votes.jsonl"
    store = JsonlStore(export)
    for i in range(4):
        store.record(info(f"h{i}", i), 1 if i % 2 else -1, user="obi" if i < 3 else "sam")
    monkeypatch.setattr("sys.argv", ["gen_hole.py", "train", "--ratings", str(export), "--user", "obi"])
    gen_hole.main()
    assert "Trained on 3 ratings" in capsys.readouterr().out
    assert gen_hole.status(store=configured_store(export))["ratings"] == 4
    assert not (isolated_data / "ratings.jsonl").exists()  # the local log was not touched


def test_prune_spares_rated_ids_and_fresh_holes(tmp_path):
    def hole(name, age):
        d = tmp_path / name
        d.mkdir()
        (d / "gen.json").write_text("{}")
        t = time.time() - age
        os.utime(d, (t, t))
        return d
    old, voted, fresh, newest = hole("old", 7200), hole("voted", 7300), hole("fresh", 60), hole("newest", 0)
    removed = gen_hole.prune_unrated(tmp_path, keep=1, protect=newest, rated={"voted"}, grace_seconds=3600)
    assert removed == ["old"] and voted.exists() and fresh.exists() and not old.exists()
