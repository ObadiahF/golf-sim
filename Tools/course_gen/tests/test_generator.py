import json

import numpy as np
import pytest
from shapely.geometry import box

from generate import plan, write_package
from layout import Layout
from priors import DEFAULT_PRIORS, lerp_range
from style import PRESETS, Style, apply_overrides
from terrain import Grid
from validate import problems

SEEDS = range(6)


def test_lerp_range_passes_through_median():
    assert lerp_range([10, 20, 40], 0.0) == 10
    assert lerp_range([10, 20, 40], 0.5) == 20
    assert lerp_range([10, 20, 40], 1.0) == 40


@pytest.mark.parametrize("preset", list(PRESETS))
def test_every_preset_plans_playable_holes(preset):
    for seed in SEEDS:
        style = PRESETS[preset].sample(np.random.default_rng(seed))
        layout, attempts = plan(style, seed, DEFAULT_PRIORS)
        assert problems(layout) == []
        assert attempts <= 40
        tile = box(0, 0, layout.size, layout.size)
        assert tile.contains(layout.green)
        assert layout.green.distance(layout.pin) < 15


def test_plan_is_deterministic():
    style = PRESETS["lakes"].sample(np.random.default_rng(3))
    a, _ = plan(style, 3, DEFAULT_PRIORS)
    b, _ = plan(style, 3, DEFAULT_PRIORS)
    assert a.green.equals(b.green) and a.path.equals(b.path) and len(a.water) == len(b.water)


def _mean_coverage(preset, param, value, attr):
    areas = []
    for seed in SEEDS:
        style = apply_overrides(PRESETS[preset].sample(np.random.default_rng(seed)), {param: value})
        layout, _ = plan(style, seed, DEFAULT_PRIORS)
        areas.append(sum(p.area for p in getattr(layout, attr)) / layout.size ** 2)
    return np.mean(areas)


def test_style_knobs_change_the_hole():
    assert _mean_coverage("parkland", "tree_density", 0.95, "woods") > 2 * _mean_coverage("parkland", "tree_density", 0.15, "woods")
    assert _mean_coverage("parkland", "water", 0.95, "water") > _mean_coverage("parkland", "water", 0.0, "water") == 0


def test_validation_catches_water_on_the_green():
    style = apply_overrides(PRESETS["parkland"].sample(np.random.default_rng(1)), {"water": 0.0})
    layout, _ = plan(style, 1, DEFAULT_PRIORS)
    flooded = Layout(**{**layout.__dict__, "water": [layout.green.buffer(10)]})
    assert "water on the green" in problems(flooded)


def test_grid_mask_orientation_matches_heightmap_rows():
    grid = Grid(100.0, 101)
    m = grid.mask([box(0, 0, 100, 20)])  # southern strip
    assert m[:15].all() and not m[30:].any()  # row 0 = south


def test_package_matches_unity_format(tmp_path):
    style = PRESETS["lakes"].sample(np.random.default_rng(5))
    pkg_dir = write_package(style, 5, tmp_path, spacing=1.5)
    pkg = json.loads((pkg_dir / "hole.json").read_text())
    n = pkg["heightmapResolution"]
    assert (pkg_dir / "heightmap.raw").stat().st_size == n * n * 2
    assert pkg["version"] == 1 and pkg["theme"] == "lakes"
    assert {a["surface"] for a in pkg["areas"]} >= {"rough", "green", "tee"}
    for water in pkg["water"]:
        assert pkg["minElevation"] <= water["level"] <= pkg["maxElevation"]
        assert len(water["triangles"]["points"]) % 6 == 0
    assert (pkg_dir / "preview.png").exists() and (pkg_dir / "gen.json").exists()
