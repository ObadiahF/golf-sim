"""Style -> playable layout -> sculpted terrain -> hole package (same format as real OSM holes)."""
from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
from shapely.geometry import Point

import _prep  # noqa: F401
from geo import HoleFrame, pick_resolution
from hole_package import area_json, flat_points, write_package as write_hole, xz_json
from layout import Layout, LayoutBuilder, dress
from noise import Fbm
from preview import render_preview
from priors import load_priors
from style import Style
from terrain import Grid, sculpt
from validate import green_problems, launch_problems, problems
from vegetation import plant
from vegetation_themes import tree_density_scale
from water import carve_water

GENERATOR_VERSION = 5   # 2: trees, shrubs and rocks planted here (objects.bin), not by Unity; 3: tee point always on the back tee box
                        # 4: graded line of play + level tee boxes, tee shots checked for clearance (TH-5)
                        # 5: pond banks never tilt a tee or the green, calm pin area, greens checked for slope (Q5-1)
GEN_FORMAT = 2          # gen.json format (Docs/hole-format/gen.schema.json)
GEN_FILE = "gen.json"
MAX_ATTEMPTS = 40
DEFAULT_SPACING = 0.75  # meters per heightmap sample (0.5 matches real holes but is ~4x slower in Unity)

# hole.json area order; Unity paints by layer priority, so this only affects readability.
AREA_KINDS = ("rough", "scrub", "woods", "fairway", "tee", "green", "bunker", "water")


def hole_id(style: Style, seed: int) -> str:
    digest = hashlib.sha1(json.dumps(style.to_json(), sort_keys=True).encode()).hexdigest()[:6]
    return f"{style.preset}_{seed}_{digest}"


def _layouts(style: Style, seed: int, priors: dict | None):
    """Every candidate layout for this style and seed, in order: (layout, attempt number, problems)."""
    priors = priors or load_priors()
    for attempt in range(MAX_ATTEMPTS):
        rng = np.random.default_rng([seed, attempt])
        layout = LayoutBuilder(style, priors, rng).build()
        layout = dress(layout, style, rng, Fbm(seed * 31 + attempt, 140, octaves=3))
        yield layout, attempt + 1, problems(layout)


def _give_up(issues: list[str]):
    raise RuntimeError(f"No playable layout in {MAX_ATTEMPTS} attempts; last issues: {', '.join(issues)}")


def plan(style: Style, seed: int, priors: dict | None = None) -> tuple[Layout, int]:
    """First playable layout for this style and seed (2D checks only). Returns (layout, attempts used)."""
    issues: list[str] = []
    for layout, attempt, issues in _layouts(style, seed, priors):
        if not issues:
            return layout, attempt
    _give_up(issues)


def plan_terrain(style: Style, seed: int, spacing: float = DEFAULT_SPACING, priors: dict | None = None):
    """First layout whose 2D plan and sculpted terrain are both playable (the tee shot clears the ground and the
    green is puttable). Returns (layout, frame, heights, attempts used)."""
    issues: list[str] = []
    for layout, attempt, issues in _layouts(style, seed, priors):
        if issues:
            continue
        frame = HoleFrame(0, 0.0, 0.0, layout.size, pick_resolution(layout.size, spacing))
        heights = sculpt(layout, style, Grid(frame.size, frame.resolution), seed)
        issues = (launch_problems(layout.path, heights, layout.size)
                  + green_problems(layout.green, layout.pin, heights, layout.size))
        if not issues:
            return layout, frame, heights, attempt
    _give_up(issues)


def _areas(layout: Layout) -> list[tuple[dict, object]]:
    """(hole.json area, polygon) pairs."""
    groups = {"rough": [layout.rough], "scrub": layout.scrub, "woods": layout.woods, "fairway": [layout.fairway],
              "tee": layout.tees, "green": [layout.green], "bunker": layout.bunkers, "water": layout.water}
    out = []
    for kind in AREA_KINDS:
        for i, shape in enumerate(groups[kind]):
            for j, poly in enumerate(getattr(shape, "geoms", [shape])):
                if not poly.is_empty and poly.area > 1:
                    out.append((area_json(kind, f"gen/{kind}/{i}.{j}", poly), poly))
    return out


def write_package(style: Style, seed: int, out_root: Path, spacing: float = DEFAULT_SPACING,
                  extra: dict | None = None) -> Path:
    layout, frame, heights, attempts = plan_terrain(style, seed, spacing)
    hid = hole_id(style, seed)
    out_dir = out_root / hid
    out_dir.mkdir(parents=True, exist_ok=True)

    water = carve_water(heights, layout.water, frame)
    areas = _areas(layout)
    objects = plant([(a["surface"], poly) for a, poly in areas], heights, frame.size, style.theme, seed,
                    specimens=[(t.x, t.y) for t in layout.trees],
                    tree_density=tree_density_scale(style.values["tree_density"]))

    # gen.json first, so the package validation below checks it too.
    generator = {"version": GEN_FORMAT, "id": hid, "generatorVersion": GENERATOR_VERSION, "seed": seed,
                 "attempts": attempts, **style.to_json(), **(extra or {})}
    (out_dir / GEN_FILE).write_text(json.dumps(generator, indent=1))
    write_hole(out_dir, {
        "id": hid,
        "course": f"Generated ({style.preset})",
        "holeRef": hid,
        "par": style.par,
        "handicap": 0,
        "theme": style.theme,
        "source": {"kind": "generated"},
        "sizeMeters": frame.size,
        "holePath": {"points": flat_points(layout.path.coords)},
        "tee": xz_json(Point(layout.path.coords[0])),
        "pin": xz_json(layout.pin),
        "areas": [a for a, _ in areas],
        "water": water,
    }, heights, objects)
    render_preview(out_dir)
    return out_dir


def read_generator_info(package_dir: Path) -> dict:
    return json.loads((package_dir / GEN_FILE).read_text())
