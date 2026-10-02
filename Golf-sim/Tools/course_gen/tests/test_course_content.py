"""QA round 6 course content: smooth grading (G6-1), varied par 5s (G6-2), open shot lines (G6-3) and level landing
zones (G6-4)."""
import math
from collections import Counter

import numpy as np
import pytest
import shapely
from shapely.ops import unary_union

import terrain
from generate import furnish, plan, plan_terrain, sculpt_layout, write_package
from hole_package import read_hole, read_objects
from layout import DRIVE, PAR5_LAYUP, LayoutBuilder, shot_zone
from objects_bin import KIND_CODE, PLACED, write as write_objects
from priors import DEFAULT_PRIORS
from scan_playability import hole_path, scan
from style import PRESETS
from terrain import Grid
from validate import (LANDING_ALONG, LANDING_CROSS, TEE_FAN_CLEAR, landing_problems, landing_slopes,
                      tree_line_clearance, tree_line_problems)

SPACING = 0.75      # the trainer's spacing: crease sizes depend on it
CREASE = 0.15       # meters of second difference between neighbouring samples (QA's notches.py)


def _hilly():
    """24 hilly holes: mountain and links, every par."""
    return [(preset, par, 700 + 10 * i + par) for preset in ("mountain", "links") for par in (3, 4, 5)
            for i in range(4)]


def _style(preset, par, seed):
    style = PRESETS[preset].sample(np.random.default_rng(seed))
    style.par = par
    return style


def creases(layout, heights, grid) -> int:
    """Cells within 25 m of the hole line (away from pads, bunkers and water) where the ground creases."""
    c2 = np.zeros_like(heights)
    c2[:, 1:-1] = np.abs(heights[:, 2:] - 2 * heights[:, 1:-1] + heights[:, :-2])
    c2[1:-1, :] = np.maximum(c2[1:-1, :], np.abs(heights[2:, :] - 2 * heights[1:-1, :] + heights[:-2, :]))
    keep_out = unary_union([g.buffer(4) for g in [*layout.tees, *layout.bunkers]]
                           + [g.buffer(5) for g in [layout.green, *layout.water]])
    zone = shapely.contains_xy(layout.path.buffer(25).difference(keep_out), grid.x, grid.z)
    return int((c2[zone] > CREASE).sum())


@pytest.mark.parametrize("preset,par,seed", _hilly())
def test_grading_adds_no_seams(preset, par, seed, monkeypatch):
    """G6-1: the nearest-sample correction cut stair steps (mountain_6398: 2417 crease cells, 13 ungraded)."""
    style = _style(preset, par, seed)
    layout, _ = plan(style, seed, DEFAULT_PRIORS)
    _, graded = sculpt_layout(layout, style, seed, SPACING)
    monkeypatch.setattr(terrain, "grade_corridor", lambda h, *args, **kwargs: h)
    _, natural = sculpt_layout(layout, style, seed, SPACING)
    grid = Grid(layout.size, graded.shape[0])
    assert creases(layout, graded, grid) <= creases(layout, natural, grid) + 8


def test_par5_routes_vary():
    """G6-2: every par 5 used to be 275 m / ~100 m / 120 m with its corner at the longest drive."""
    drives, layups, into, shapes = [], [], [], Counter()
    for seed in range(200):
        style = _style(list(PRESETS)[seed % len(PRESETS)], 5, seed)
        path, landings = LayoutBuilder(style, DEFAULT_PRIORS, np.random.default_rng(seed)).route()
        c = path.coords
        legs = [math.dist(a, b) for a, b in zip(c, c[1:])]
        headings = [math.atan2(b[1] - a[1], b[0] - a[0]) for a, b in zip(c, c[1:])]
        turns = [abs(math.degrees((h2 - h1 + math.pi) % (2 * math.pi) - math.pi)) > 8
                 for h1, h2 in zip(headings, headings[1:])]
        drives.append(legs[0])
        layups.append(legs[1])
        into.append(legs[2])
        shapes[tuple(turns)] += 1
        assert landings == pytest.approx([legs[0], legs[0] + legs[1]])
        assert 430 <= path.length <= 600
    assert DRIVE[0] <= min(drives) and max(drives) <= DRIVE[2] and np.ptp(drives) > 30 and np.median(drives) < 250
    assert PAR5_LAYUP[0] <= min(layups) and max(layups) <= PAR5_LAYUP[1] + 1e-6 and np.ptp(layups) > 60
    assert min(into) >= 50 and np.ptp(into) > 50
    assert len(shapes) == 4 and min(shapes.values()) >= 10, shapes  # straight, first, second and both corners


def _wooded():
    """Tree-heavy holes (forest and parkland) of every par."""
    return [(preset, par, 800 + 10 * i + par) for preset in ("forest", "parkland", "lakes") for par in (3, 4, 5)
            for i in range(2)]


@pytest.mark.parametrize("preset,par,seed", _wooded())
def test_shot_lines_are_open_and_landing_zones_level(preset, par, seed):
    """G6-3: single trees stood on the tee line (forest_6146: a 21 m tree 42 m out, 2.7 m off the line).
    G6-4: landing zones sat on 10-23 % side slopes."""
    style = _style(preset, par, seed)
    layout, frame, heights, _ = plan_terrain(style, seed, 1.0, DEFAULT_PRIORS)
    _, _, objects = furnish(layout, style, seed, frame, heights)
    clearance = tree_line_clearance(layout.path, objects, heights, layout.size)
    assert clearance[0][1] >= TEE_FAN_CLEAR and all(clear > 0 for _, clear, _ in clearance[1:]), clearance
    trees = np.isin(objects["kind"], [KIND_CODE["conifer"], KIND_CODE["deciduous"], KIND_CODE["palm"]])
    assert not shapely.contains_xy(shot_zone(layout.path), objects["x"][trees], objects["y"][trees]).any()
    for side, grade in landing_slopes(layout.path, heights, layout.size):
        assert side <= LANDING_CROSS and grade <= LANDING_ALONG


def _tree_at(path, s: float, off: float, height: float = 24.0) -> np.ndarray:
    """A conifer `off` m left of the hole line, `s` m along it."""
    p = path.interpolate(s)
    q = path.interpolate(s + 1)
    ux, uz = q.x - p.x, q.y - p.y
    tree = np.zeros(1, PLACED)
    tree["kind"], tree["height"], tree["radius"] = KIND_CODE["conifer"], height, 0.5
    tree["x"], tree["y"] = p.x - uz * off, p.y + ux * off
    return tree


def test_validators_catch_a_tree_on_the_line_and_a_tilted_landing_zone():
    style = _style("parkland", 4, 3)
    layout, frame, heights, _ = plan_terrain(style, 3, 2.0, DEFAULT_PRIORS)
    _, _, objects = furnish(layout, style, 3, frame, heights)
    assert tree_line_problems(layout.path, objects, heights, layout.size) == []
    blocked = np.concatenate([objects, _tree_at(layout.path, 40, 1.0)])
    issues = tree_line_problems(layout.path, blocked, heights, layout.size)
    assert issues and "the tee shot" in issues[0]
    assert landing_problems(layout.path, heights, layout.size) == []
    tilted = heights + Grid(layout.size, heights.shape[0]).x * 0.2   # every direction leans by up to 20 %
    assert any("landing zone 1" in i for i in landing_problems(layout.path, tilted, layout.size))


def test_scan_finds_a_tree_on_the_tee_line(tmp_path):
    folder = write_package(_style("forest", 3, 11), 11, tmp_path, spacing=2.0)
    assert scan(tmp_path)["failing"] == {}
    hole = read_hole(folder)
    objects = read_objects(folder, hole)
    write_objects(folder / "objects.bin", np.concatenate([objects, _tree_at(hole_path(hole), 30, 0.5)]),
                  hole["sizeMeters"])
    issues = scan(tmp_path)["failing"][folder.name]["issues"]
    assert any("the tee shot" in i for i in issues)
