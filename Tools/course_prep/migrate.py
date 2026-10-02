"""Upgrades version 1 hole packages to version 2 (Docs/hole-format/README.md, section 6).

The folder keeps its name, which becomes the id (sanitised names must already match). Mapped trees become
specimen objects and the theme's vegetation is planted, so this is the one time a kept package's content
changes: version 1 never stored its trees and rocks.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from hole_package import HOLE_FILE, area_polygons, make_id, seed_from_id, write_package
from vegetation import plant
from vegetation_themes import DEFAULT_THEME, tree_density_scale

_DROP = ("version", "heightmapFile", "heightmapResolution", "minElevation", "maxElevation", "trees", "crs",
         "originEasting", "originNorthing")


def migrate_v1(folder: Path, theme: str | None = None) -> dict:
    old = json.loads((folder / HOLE_FILE).read_text())
    if old.get("version") != 1:
        raise SystemExit(f"{folder}: version {old.get('version')}, only version 1 packages can be migrated")
    if make_id(folder.name) != folder.name:
        raise SystemExit(f"{folder}: folder name isn't a valid id; rename it to '{make_id(folder.name)}' first")

    n = old["heightmapResolution"]
    raw = np.fromfile(folder / old["heightmapFile"], dtype="<u2").reshape(n, n)
    heights = old["minElevation"] + raw / 65535.0 * (old["maxElevation"] - old["minElevation"])
    areas = [{"surface": a["surface"], "sourceId": a.get("osmId", ""), "rings": [{"points": _open(r["points"])}
             for r in a["rings"]]} for a in old["areas"]]
    polygons = area_polygons({"areas": areas})
    gen_file = folder / "gen.json"
    generated = gen_file.exists()
    info = json.loads(gen_file.read_text()) if generated else {}

    theme_name = old.get("theme") or theme or DEFAULT_THEME
    density = tree_density_scale(info.get("params", {}).get("tree_density", 0.5)) if generated else 1.0
    objects = plant(polygons, heights, old["sizeMeters"], theme_name, seed=info.get("seed", seed_from_id(folder.name)),
                    specimens=[(t["x"], t["y"]) for t in old.get("trees", [])], tree_density=density)

    hole = {k: v for k, v in old.items() if k not in _DROP}
    hole.update(id=folder.name, theme=theme_name, areas=areas, par=old["par"] or 4)
    if generated:
        hole["source"] = {"kind": "generated"}
        _migrate_gen(gen_file, info)
    else:
        hole["source"] = {"kind": "osm", "crs": old.get("crs", ""), "originEasting": old.get("originEasting", 0.0),
                          "originNorthing": old.get("originNorthing", 0.0)}
    return write_package(folder, hole, heights, objects)


def _migrate_gen(path: Path, info: dict) -> None:
    info["generatorVersion"] = info.get("generatorVersion", info.get("version", 1))
    info["version"] = 2
    path.write_text(json.dumps(info, indent=1))


def _open(points: list[float]) -> list[float]:
    """Version 1 repeated the first point at the end of each ring; version 2 doesn't."""
    return points[:-2] if len(points) >= 4 and points[:2] == points[-2:] else points
