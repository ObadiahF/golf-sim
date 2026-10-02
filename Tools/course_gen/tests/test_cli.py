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


def test_cli_generate_then_rate_with_comment_and_tags(isolated_data, monkeypatch, capsys):
    import gen_hole

    out = isolated_data / "holes"
    monkeypatch.setattr("sys.argv", ["gen_hole.py", "generate", "--preset", "links", "--seed", "2",
                                     "--spacing", "3", "--out", str(out)])
    gen_hole.main()
    result = json.loads(capsys.readouterr().out.strip().splitlines()[-1])
    assert result["preset"] == "links" and "attempts" not in result

    monkeypatch.setattr("sys.argv", ["gen_hole.py", "rate", result["package"], "up", "--comment", "nice dunes",
                                     "--tag", "too_long", "more_bunkers"])
    gen_hole.main()
    entry = json.loads((isolated_data / "ratings.jsonl").read_text())
    assert entry["id"] == result["id"] and entry["rating"] == 1 and entry["comment"] == "nice dunes"
    assert entry["tags"] == ["too_long", "more_bunkers"]
    assert gen_hole.status()["ratings"] == 1
