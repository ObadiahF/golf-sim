import numpy as np
import pytest
from shapely import affinity
from shapely.geometry import Point, box

from generate import plan, write_package
from hole_package import read_hole, read_objects, validate
from objects_bin import KIND_CODE
from layout import TEE_MARGIN, Layout
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


def test_tee_point_is_inside_a_tee_box():
    """U-6: the hole's `tee` (path start) must sit on a tee box, TEE_MARGIN from its edges."""
    for i in range(42):
        preset = list(PRESETS)[i % len(PRESETS)]
        seed = 300 + i
        style = PRESETS[preset].sample(np.random.default_rng(seed))
        layout, _ = plan(style, seed, DEFAULT_PRIORS)
        tee = Point(layout.path.coords[0])
        assert any(t.buffer(-TEE_MARGIN + 0.01).contains(tee) for t in layout.tees), f"{preset} seed {seed}"


def test_validation_catches_tee_off_the_box():
    style = PRESETS["parkland"].sample(np.random.default_rng(1))
    layout, _ = plan(style, 1, DEFAULT_PRIORS)
    moved = Layout(**{**layout.__dict__, "tees": [affinity.translate(t, 30, 30) for t in layout.tees]})
    assert "tee point is off the tee box" in problems(moved)


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


def test_package_meets_the_hole_contract(tmp_path):
    style = PRESETS["lakes"].sample(np.random.default_rng(5))
    pkg_dir = write_package(style, 5, tmp_path, spacing=1.5)
    assert validate(pkg_dir) == []
    pkg = read_hole(pkg_dir)
    assert pkg["version"] == 2 and pkg["theme"] == "lakes" and pkg["id"] == pkg_dir.name
    assert {a["surface"] for a in pkg["areas"]} >= {"rough", "green", "tee"}
    for water in pkg["water"]:
        assert pkg["heightmap"]["minElevation"] <= water["level"] <= pkg["heightmap"]["maxElevation"]
        assert len(water["triangles"]["points"]) % 6 == 0
    assert pkg["objects"]["count"] > 100
    assert (pkg_dir / "preview.png").exists() and (pkg_dir / "gen.json").exists()


def test_packages_are_reproducible(tmp_path):
    style = PRESETS["forest"].sample(np.random.default_rng(2))
    a = write_package(style, 2, tmp_path / "a", spacing=2.0)
    b = write_package(style, 2, tmp_path / "b", spacing=2.0)
    for name in ("hole.json", "heightmap.raw", "objects.bin"):
        assert (a / name).read_bytes() == (b / name).read_bytes(), name


def test_tree_density_knob_plants_more_trees(tmp_path):
    def trees(value):
        style = apply_overrides(PRESETS["parkland"].sample(np.random.default_rng(4)), {"tree_density": value})
        objects = read_objects(write_package(style, 4, tmp_path / str(value), spacing=2.0))
        return int(np.isin(objects["kind"], [KIND_CODE["conifer"], KIND_CODE["deciduous"]]).sum())
    assert trees(0.95) > 2 * trees(0.15)
