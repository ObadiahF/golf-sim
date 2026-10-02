"""TH-5: the tee shot must clear the ground; the line of play is graded and tee boxes never form terraces."""
import numpy as np
import pytest
import shapely

from generate import plan, plan_terrain, write_package
from grading import TEE_DECK, TEE_GRADE, TEE_RUN, TEE_UPHILL, UPHILL, DOWNHILL, grade_profile
from priors import DEFAULT_PRIORS
from scan_launch import scan
from style import PRESETS, Style
from terrain import Grid
from validate import launch_problems

SPACING = 1.0   # coarser than the real 0.75 m so the sweep stays quick; tee pads are still several samples wide
TH5_STYLE = {"preset": "mountain", "theme": "mountain", "par": 3, "params": {
    "length": 0.4217, "dogleg": 0.4007, "relief": 1.0, "hilliness": 0.2376, "slope": 0.7124, "tree_density": 0.7833,
    "water": 0.3662, "bunkers": 0.4055, "fairway_width": 0.4538, "green_size": 0.7278, "rough_width": 0.5278,
    "scrub": 0.0558}}
TH5_SEED = 25039489


def _profile(layout, heights, distances):
    grid = Grid(layout.size, heights.shape[0])
    pts = shapely.line_interpolate_point(layout.path, np.asarray(distances, dtype=float))
    return grid.sample(heights, shapely.get_x(pts), shapely.get_y(pts))


def _cases():
    """66 holes: 6 of every preset, plus 30 more mountain holes (steepest relief)."""
    cases = [(preset, 500 + i) for preset in PRESETS for i in range(6)]
    return cases + [("mountain", 600 + i) for i in range(30)]


@pytest.mark.parametrize("preset,seed", _cases())
def test_tee_shot_clears_the_ground(preset, seed):
    style = PRESETS[preset].sample(np.random.default_rng(seed))
    layout, _, heights, attempts = plan_terrain(style, seed, SPACING, DEFAULT_PRIORS)
    assert launch_problems(layout.path, heights, layout.size) == []
    # The grading does the work: the first layout that passes the 2D checks is already playable.
    assert attempts == plan(style, seed, DEFAULT_PRIORS)[1]
    rise = np.diff(_profile(layout, heights, np.arange(0, min(TEE_RUN, layout.path.length), 5.0))) / 5
    assert rise.max() < 0.2, f"{rise.max():.2f} grade within {TEE_RUN:.0f} m of the tee"


def test_th5_hole_has_no_wall_in_front_of_the_tee():
    """mountain_25039489_bd20eb: the ground rose 1.16 m at 4 m and 1.70 m at 6 m (generator v3)."""
    style = Style.from_json(TH5_STYLE)
    layout, _, heights, _ = plan_terrain(style, TH5_SEED, SPACING, DEFAULT_PRIORS)
    rise = _profile(layout, heights, [0, 4, 6, 20, 100]) - _profile(layout, heights, [0])[0]
    assert rise[1] < 0.4 and rise[2] < 0.5 and rise[3] < 1.2 and rise[4] < 12, np.round(rise, 2)


def test_validator_catches_a_wall_in_front_of_the_tee():
    style = Style.from_json(TH5_STYLE)
    layout, _, heights, _ = plan_terrain(style, TH5_SEED, SPACING, DEFAULT_PRIORS)
    grid = Grid(layout.size, heights.shape[0])
    wall = shapely.Point(layout.path.interpolate(6)).buffer(2.5)
    walled = heights + grid.mask([wall]) * 2.0
    issues = launch_problems(layout.path, walled, layout.size)
    assert issues and "on the hole line" in issues[0]


def test_grade_profile_respects_the_caps():
    s = np.arange(0.0, 400.0, 1.0)
    rng = np.random.default_rng(0)
    raw = np.cumsum(rng.normal(0, 0.6, s.size)) + 0.3 * s  # a steep, jagged climb
    graded = np.diff(grade_profile(raw, s))
    for lo, hi, cap in ((0, TEE_DECK, TEE_GRADE), (TEE_DECK, TEE_RUN, TEE_UPHILL), (TEE_RUN, s[-1], UPHILL)):
        part = graded[(s[1:] > lo) & (s[1:] <= hi)]
        assert part.max() <= cap + 1e-9 and part.min() >= -DOWNHILL - 1e-9


def test_scan_reads_packages_from_disk(tmp_path):
    write_package(Style.from_json(TH5_STYLE), TH5_SEED, tmp_path, spacing=2.0)
    assert scan(tmp_path) == {"checked": 1, "failing": {}, "unreadable": []}
