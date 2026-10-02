"""Writes, reads and validates hole packages (contract: Docs/hole-format/README.md). Shared by every producer."""
from __future__ import annotations

import json
import re
from pathlib import Path

import numpy as np
from shapely.geometry import Point, Polygon

import objects_bin
from dem_io import write_raw16
from surfaces import KEEP_CLEAR, classify

PACKAGE_VERSION = 2
HOLE_FILE = "hole.json"
HEIGHTMAP_FILE = "heightmap.raw"
SCHEMA_DIR = Path(__file__).resolve().parents[2] / "Docs" / "hole-format"
_ID = re.compile(r"^[a-z0-9][a-z0-9_-]{2,63}$")


def flat_points(coords) -> list[float]:
    return [round(v, 3) for xy in coords for v in xy[:2]]


def xz_json(p: Point) -> dict:
    return {"x": round(p.x, 3), "y": round(p.y, 3)}


def area_json(surface: str, source_id: str, poly: Polygon) -> dict:
    """One hole.json area: outline ring first, then holes (rings close implicitly)."""
    rings = [poly.exterior, *poly.interiors]
    return {"surface": surface, "sourceId": source_id,
            "rings": [{"points": flat_points(list(r.coords)[:-1])} for r in rings]}


def make_id(text: str) -> str:
    slug = re.sub(r"[^a-z0-9]+", "_", text.lower()).strip("_")
    return (slug or "hole")[:64].rstrip("_")


def seed_from_id(hole_id: str) -> int:
    """Deterministic seed for holes without a generator seed."""
    return int.from_bytes(hole_id.encode()[:8], "little")


def write_package(out_dir: Path, hole: dict, heights: np.ndarray, objects: np.ndarray) -> dict:
    """Writes heightmap.raw, objects.bin and hole.json (completing its heightmap / objects / version fields),
    then validates the result. `hole` holds every other hole.json field; `heights` are meters [row, col],
    row 0 = south; `objects` are decoded (objects_bin.PLACED)."""
    out_dir.mkdir(parents=True, exist_ok=True)
    lo, hi = write_raw16(heights, out_dir / HEIGHTMAP_FILE)
    count = objects_bin.write(out_dir / objects_bin.FILE_NAME, objects, hole["sizeMeters"])
    package = {"version": PACKAGE_VERSION, **hole,
               "heightmap": {"file": HEIGHTMAP_FILE, "resolution": int(heights.shape[0]),
                             "minElevation": round(lo, 3), "maxElevation": round(max(hi, lo + 0.001), 3)},
               "objects": {"file": objects_bin.FILE_NAME, "count": count}}
    (out_dir / HOLE_FILE).write_text(json.dumps(package, indent=1))
    issues = validate(out_dir)
    if issues:
        raise ValueError(f"{out_dir} breaks the hole contract:\n  " + "\n  ".join(issues))
    return package


def read_hole(package_dir: Path) -> dict:
    return json.loads((Path(package_dir) / HOLE_FILE).read_text())


def read_heights(package_dir: Path, hole: dict | None = None) -> np.ndarray:
    """Heightmap in meters, [row, col], row 0 = south."""
    hole = hole or read_hole(package_dir)
    hm = hole["heightmap"]
    n = hm["resolution"]
    raw = np.fromfile(Path(package_dir) / hm["file"], dtype="<u2").reshape(n, n)
    return hm["minElevation"] + raw / 65535.0 * (hm["maxElevation"] - hm["minElevation"])


def read_objects(package_dir: Path, hole: dict | None = None) -> np.ndarray:
    hole = hole or read_hole(package_dir)
    return objects_bin.read(Path(package_dir) / hole["objects"]["file"], hole["sizeMeters"])


def area_polygons(hole: dict) -> list[tuple[str, Polygon]]:
    out = []
    for a in hole["areas"]:
        rings = [np.asarray(r["points"], dtype=np.float64).reshape(-1, 2) for r in a["rings"]]
        poly = Polygon(rings[0], rings[1:]).buffer(0)
        if not poly.is_empty:
            out.append((a["surface"], poly))
    return out


def validate(package_dir: Path) -> list[str]:
    """Every way this package breaks the contract (empty list = valid)."""
    package_dir = Path(package_dir)
    try:
        hole = read_hole(package_dir)
    except (OSError, ValueError) as e:
        return [f"{HOLE_FILE}: {e}"]
    issues = _schema_issues(hole, "hole.schema.json")
    if issues:
        return issues
    if hole["id"] != package_dir.name:
        issues.append(f"id '{hole['id']}' != folder name '{package_dir.name}'")
    hm, size = hole["heightmap"], hole["sizeMeters"]
    expected = hm["resolution"] ** 2 * 2
    actual = (package_dir / hm["file"]).stat().st_size if (package_dir / hm["file"]).exists() else -1
    if actual != expected:
        issues.append(f"{hm['file']}: {actual} bytes, expected {expected}")
    if hm["maxElevation"] <= hm["minElevation"]:
        issues.append("heightmap maxElevation must be above minElevation")

    try:
        records = objects_bin.read_records(package_dir / hole["objects"]["file"])
        if len(records) != hole["objects"]["count"]:
            issues.append(f"objects.bin has {len(records)} records, hole.json says {hole['objects']['count']}")
        if len(records) and records["kind"].max() >= len(objects_bin.KINDS):
            issues.append("objects.bin has unknown kinds")
        objects = objects_bin.decode(records, size)
    except (OSError, ValueError) as e:
        issues.append(str(e))
        objects = objects_bin.empty()

    areas = area_polygons(hole)
    points = [hole["tee"], hole["pin"]]
    pts = np.array([[p["x"], p["y"]] for p in points])
    if (pts < 0).any() or (pts > size).any():
        issues.append("tee or pin outside the tile")
    if (classify(pts[:, 0], pts[:, 1], areas) == "water").any():
        issues.append("tee or pin in water")
    if len(objects):
        on = classify(objects["x"], objects["y"], areas)
        bad = np.isin(on, KEEP_CLEAR)
        if bad.any():
            issues.append(f"{int(bad.sum())} objects on {sorted(set(on[bad]))}")
    if (path := (package_dir / "gen.json")).exists():
        issues.extend(f"gen.json: {i}" for i in _schema_issues(json.loads(path.read_text()), "gen.schema.json"))
    return issues


def _schema_issues(doc: dict, schema_name: str) -> list[str]:
    import jsonschema  # only needed when validating
    schema = json.loads((SCHEMA_DIR / schema_name).read_text())
    validator = jsonschema.Draft202012Validator(schema)
    return [f"{'/'.join(map(str, e.path)) or '(root)'}: {e.message}" for e in validator.iter_errors(doc)][:20]


def summary(package: dict) -> str:
    """Human-readable lines describing a written package."""
    from collections import Counter
    hm = package["heightmap"]
    areas = Counter(a["surface"] for a in package["areas"])
    return "\n".join([
        f"  {package['id']}: par {package['par']}, theme {package['theme']}, {package['sizeMeters']:.0f} m square",
        f"  heightmap {hm['resolution']}px ({package['sizeMeters'] / (hm['resolution'] - 1):.2f} m/px), "
        f"elevation {hm['minElevation']:.1f} .. {hm['maxElevation']:.1f} m",
        "  areas: " + ", ".join(f"{k}={v}" for k, v in sorted(areas.items())),
        f"  objects: {package['objects']['count']}, water bodies: {len(package['water'])}",
    ])
