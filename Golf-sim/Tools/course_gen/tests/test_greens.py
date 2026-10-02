"""Q5-1: greens must be puttable. Pond banks (~15 %) used to override the green pad and tilt the pin area."""
import json

import numpy as np
import pytest
import shapely

from dem_io import write_raw16
from generate import plan, plan_terrain, sculpt_layout, write_package
from hole_package import read_heights, read_hole
from priors import DEFAULT_PRIORS
from scan_playability import green_profile, scan
from style import PRESETS, Style
from terrain import Grid
from validate import GREEN_MAX_SLOPE, GREEN_PIN_SLOPE, green_problems, green_slopes

SPACING = 1.0   # coarser than the real 0.75 m so the sweep stays quick
# mountain_202_d16117 (generator v4): a pond bank tilted its green to 15.0 % at the pin.
BANKED_STYLE = {"preset": "mountain", "theme": "mountain", "par": 5, "params": {
    "length": 0.7174, "dogleg": 0.4125, "relief": 0.8197, "hilliness": 0.3018, "slope": 0.1542, "tree_density": 0.831,
    "water": 0.3056, "bunkers": 0.4265, "fairway_width": 0.6692, "green_size": 0.4278, "rough_width": 0.3423,
    "scrub": 0.1916}}
BANKED_SEED = 202


def _cases():
    """84 holes: 6 of every preset, 30 more lakes holes (the most ponds) and 18 more mountain holes (steepest)."""
    cases = [(preset, 900 + i) for preset in PRESETS for i in range(6)]
    return cases + [("lakes", 1000 + i) for i in range(30)] + [("mountain", 1100 + i) for i in range(18)]


def _tilted(heights: np.ndarray, size: float, grade: float) -> np.ndarray:
    """Heights plus a plane rising `grade` per meter to the east."""
    return heights + Grid(size, heights.shape[0]).x * grade


@pytest.mark.parametrize("preset,seed", _cases())
def test_green_is_puttable(preset, seed):
    style = PRESETS[preset].sample(np.random.default_rng(seed))
    layout, _, heights, attempts = plan_terrain(style, seed, SPACING, DEFAULT_PRIORS)
    pin, anywhere = green_slopes(layout.green, layout.pin, heights, layout.size)
    assert pin <= GREEN_PIN_SLOPE and anywhere <= GREEN_MAX_SLOPE, (pin, anywhere)
    # The shaping does the work: the first layout that passes the 2D checks already has a puttable green (it may
    # still be retried for another reason, e.g. a pond bank across a landing zone).
    first, first_attempts = plan(style, seed, DEFAULT_PRIORS)
    if first_attempts != attempts:
        _, first_heights = sculpt_layout(first, style, seed, SPACING)
        assert green_problems(first.green, first.pin, first_heights, first.size) == []


def test_pond_bank_no_longer_tilts_the_green():
    layout, _, heights, _ = plan_terrain(Style.from_json(BANKED_STYLE), BANKED_SEED, SPACING, DEFAULT_PRIORS)
    assert layout.water, "the regression needs this hole's pond"
    pin, anywhere = green_slopes(layout.green, layout.pin, heights, layout.size)
    assert pin < 0.03 and anywhere < GREEN_MAX_SLOPE, (pin, anywhere)


def test_validator_catches_a_steep_green():
    layout, _, heights, _ = plan_terrain(Style.from_json(BANKED_STYLE), BANKED_SEED, SPACING, DEFAULT_PRIORS)
    assert green_problems(layout.green, layout.pin, heights, layout.size) == []
    issues = green_problems(layout.green, layout.pin, _tilted(heights, layout.size, 0.15), layout.size)
    assert len(issues) == 2 and "at the pin" in issues[0] and "on the putting surface" in issues[1]
    # A steep patch away from the pin fails only the putting-surface cap.
    far = shapely.Point(layout.pin.x, layout.pin.y).buffer(4.0)
    patch = layout.green.buffer(-1.0).difference(far)
    bump = Grid(layout.size, heights.shape[0]).mask([patch]) * _tilted(np.zeros_like(heights), layout.size, 0.15)
    issues = green_problems(layout.green, layout.pin, heights + bump, layout.size)
    assert len(issues) == 1 and "on the putting surface" in issues[0]


def test_scan_reports_a_steep_green_on_disk(tmp_path):
    folder = write_package(Style.from_json(BANKED_STYLE), BANKED_SEED, tmp_path, spacing=2.0)
    assert scan(tmp_path)["failing"] == {}
    hole = read_hole(folder)
    lo, hi = write_raw16(_tilted(read_heights(folder, hole), hole["sizeMeters"], 0.15),
                         folder / hole["heightmap"]["file"])
    hole["heightmap"].update(minElevation=round(lo, 3), maxElevation=round(hi, 3))
    (folder / "hole.json").write_text(json.dumps(hole, indent=1))
    pin, _ = green_profile(folder)
    assert pin == pytest.approx(0.15, abs=0.03)  # the green's own 2.5 % tilt adds or subtracts, by its heading
    issues = scan(tmp_path)["failing"][folder.name]["issues"]
    assert any("at the pin" in i for i in issues)
