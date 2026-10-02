import json
import os
import time

from gen_hole import parse_overrides, prune_unrated


def make_hole(root, name, age):
    d = root / name
    d.mkdir()
    (d / "gen.json").write_text(json.dumps({"id": name}))
    (root / f"{name}.meta").write_text("folder meta")
    t = time.time() - age
    os.utime(d, (t, t))
    return d


def test_prune_keeps_newest_unrated(tmp_path):
    holes = [make_hole(tmp_path, f"h{i}", age=100 - i) for i in range(6)]  # h5 newest
    removed = prune_unrated(tmp_path, keep=3, protect=holes[-1])
    assert sorted(removed) == ["h0", "h1", "h2"]
    assert {d.name for d in tmp_path.iterdir() if d.is_dir()} == {"h3", "h4", "h5"}
    assert not (tmp_path / "h0.meta").exists()


def test_parse_overrides():
    assert parse_overrides(["tree_density=0.9", "water = 0"]) == {"tree_density": 0.9, "water": 0.0}
    assert parse_overrides(None) == {}
