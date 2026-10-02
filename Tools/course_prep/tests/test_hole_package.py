"""The hole contract (Docs/hole-format): objects.bin round trip, vegetation rules, package validation."""
import json
import sys
from pathlib import Path

import numpy as np
import pytest
from shapely.geometry import box

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))

import objects_bin  # noqa: E402
from hole_package import read_heights, read_hole, read_objects, validate, write_package  # noqa: E402
from surfaces import KEEP_CLEAR, classify  # noqa: E402
from vegetation import plant  # noqa: E402

SIZE = 200.0


def _areas():
    return [("rough", box(0, 0, SIZE, SIZE)), ("woods", box(0, 0, 60, SIZE)), ("fairway", box(80, 0, 120, SIZE)),
            ("green", box(85, 170, 115, 195)), ("water", box(140, 20, 180, 60))]


def _hole(objects_count_hint=None):
    return {"id": "test_hole", "course": "Test", "holeRef": "1", "par": 4, "handicap": 0, "theme": "forest",
            "source": {"kind": "generated"}, "sizeMeters": SIZE,
            "holePath": {"points": [100, 10, 100, 180]}, "tee": {"x": 100, "y": 10}, "pin": {"x": 100, "y": 180},
            "areas": [{"surface": s, "sourceId": s, "rings": [{"points": [c for xy in list(p.exterior.coords)[:-1] for c in xy]}]}
                      for s, p in _areas()],
            "water": []}


def test_objects_round_trip_is_exact_to_quantisation(tmp_path):
    rng = np.random.default_rng(1)
    objs = np.zeros(500, objects_bin.PLACED)
    objs["kind"] = rng.integers(0, len(objects_bin.KINDS), 500)
    objs["x"], objs["y"] = rng.uniform(0, SIZE, 500), rng.uniform(0, SIZE, 500)
    objs["height"], objs["radius"] = rng.uniform(0.3, 25, 500), rng.uniform(0.1, 2, 500)
    objs["rotation"], objs["variant"] = rng.uniform(0, 360, 500), rng.uniform(0, 1, 500)
    path = tmp_path / "objects.bin"
    assert objects_bin.write(path, objs, SIZE) == 500
    assert path.stat().st_size == 16 + 12 * 500
    back = objects_bin.read(path, SIZE)
    order = np.lexsort((objs["x"], objs["y"], objs["kind"]))
    a = objs[order]
    assert (back["kind"] == a["kind"]).all()
    assert np.abs(back["x"] - a["x"]).max() < SIZE / 65535 + 1e-6
    assert np.abs(back["height"] - a["height"]).max() <= 0.005 + 1e-6
    assert np.abs((back["rotation"] - a["rotation"] + 180) % 360 - 180).max() <= 360 / 512 + 1e-4


def test_bad_objects_file_is_rejected(tmp_path):
    path = tmp_path / "objects.bin"
    path.write_bytes(b"NOPE" + bytes(12))
    with pytest.raises(ValueError, match="magic"):
        objects_bin.read_records(path)


def test_vegetation_respects_surfaces_and_is_deterministic():
    heights = np.zeros((129, 129))
    a = plant(_areas(), heights, SIZE, "forest", seed=3)
    b = plant(_areas(), heights, SIZE, "forest", seed=3)
    assert len(a) > 50 and (a == b).all()
    on = classify(a["x"], a["y"], _areas())
    assert not np.isin(on, KEEP_CLEAR).any()
    assert (on == "woods").sum() > (on == "rough").sum()  # woods are dense, rough sparse


def test_tree_density_scales_tree_count():
    heights = np.zeros((129, 129))
    def trees(d):
        o = plant(_areas(), heights, SIZE, "forest", seed=3, tree_density=d)
        return int(np.isin(o["kind"], [objects_bin.KIND_CODE["conifer"], objects_bin.KIND_CODE["deciduous"]]).sum())
    assert trees(1.4) > 1.5 * trees(0.6)


def test_written_package_validates_and_reads_back(tmp_path):
    heights = np.linspace(10, 20, 129)[None, :].repeat(129, 0)
    objs = plant(_areas(), heights, SIZE, "forest", seed=1)
    pkg = write_package(tmp_path / "test_hole", _hole(), heights, objs)
    folder = tmp_path / "test_hole"
    assert validate(folder) == []
    assert pkg["version"] == 2 and pkg["objects"]["count"] == len(objs)
    assert np.abs(read_heights(folder) - heights).max() < 0.01
    assert len(read_objects(folder)) == len(objs)
    assert read_hole(folder)["heightmap"]["resolution"] == 129


def test_validation_catches_contract_breaks(tmp_path):
    heights = np.zeros((129, 129))
    folder = tmp_path / "test_hole"
    write_package(folder, _hole(), heights, plant(_areas(), heights, SIZE, "forest", seed=1))

    hole = read_hole(folder)
    hole["pin"] = {"x": 160, "y": 40}  # in the water
    (folder / "hole.json").write_text(json.dumps(hole))
    assert any("water" in i for i in validate(folder))

    bad = np.zeros(1, objects_bin.PLACED)
    bad["x"], bad["y"], bad["height"], bad["radius"] = 100, 100, 10, 0.3  # on the fairway
    objects_bin.write(folder / "objects.bin", bad, SIZE)
    hole["pin"], hole["objects"]["count"] = {"x": 100, "y": 180}, 1
    (folder / "hole.json").write_text(json.dumps(hole))
    assert any("fairway" in i for i in validate(folder))

    del hole["theme"]
    (folder / "hole.json").write_text(json.dumps(hole))
    assert any("theme" in i for i in validate(folder))
