"""Real-course statistics (hole lengths, fairway widths, green sizes, bunker counts) learned from OSM.

`fit_priors` measures every mapped hole in the cached OSM files; `load_priors` falls back to
sensible built-in numbers when no fitted file exists, so generation never depends on a fetch.
"""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from shapely.geometry import LineString, Point

import _prep  # noqa: F401  (adds course_prep to sys.path)
from _prep import DATA_DIR, PROJECT_ROOT
from geo import Projector, utm_epsg
from osm_io import load_features

PRIORS_PATH = DATA_DIR / "priors.json"
COURSE_SOURCES = PROJECT_ROOT / "CourseSources"
GREEN_SEARCH = 35.0    # meters from the hole line's end
BUNKER_SEARCH = 45.0   # meters either side of the hole line
FAIRWAY_SEARCH = 25.0

DEFAULT_PRIORS = {
    "source": "built-in defaults",
    "holes": 0,
    "par": {
        "3": {"length": [135, 165, 205], "bunkers": 2.5, "fairway_width": [25, 32, 40], "water_share": 0.25},
        "4": {"length": [325, 375, 430], "bunkers": 4.0, "fairway_width": [27, 35, 46], "water_share": 0.2},
        "5": {"length": [470, 520, 570], "bunkers": 5.5, "fairway_width": [28, 36, 48], "water_share": 0.2},
    },
    "green_area": [420, 600, 860],
}


def load_priors(path: Path = PRIORS_PATH) -> dict:
    if path.exists():
        return json.loads(path.read_text())
    return DEFAULT_PRIORS


def lerp_range(p10_50_90: list[float], t: float) -> float:
    """Map 0..1 onto a p10/p50/p90 triple, passing exactly through the median at 0.5."""
    lo, mid, hi = p10_50_90
    return lo + (mid - lo) * t * 2 if t <= 0.5 else mid + (hi - mid) * (t - 0.5) * 2


def _percentiles(values: list[float]) -> list[float] | None:
    if len(values) < 3:
        return None
    return [round(float(v), 1) for v in np.percentile(values, [10, 50, 90])]


def _fairway_width(poly, line) -> float | None:
    """Average width = fairway area / length of hole line running through it."""
    run = line.intersection(poly).length
    return poly.area / run if run > 40 else None


def measure_course(osm_path: Path) -> list[dict]:
    """Per-hole measurements for one OSM file."""
    features = load_features(osm_path)
    holes = [f for f in features if f.tags.get("golf") == "hole" and isinstance(f.geom, LineString)]
    if not holes:
        return []
    lon, lat = holes[0].geom.coords[0][:2]
    proj = Projector(utm_epsg(lon, lat))

    def utm_areas(*surfaces):
        return [proj.to_utm(f.geom) for f in features if f.is_area and f.surface in surfaces]

    greens, fairways, bunkers, water = (utm_areas("green"), utm_areas("fairway"),
                                        utm_areas("bunker"), utm_areas("water"))
    out = []
    for hole in holes:
        par = str(hole.tags.get("par", ""))
        if par not in ("3", "4", "5"):
            continue
        line = proj.to_utm(hole.geom)
        end = Point(line.coords[-1])
        corridor = line.buffer(BUNKER_SEARCH)
        green = min((g for g in greens if g.distance(end) < GREEN_SEARCH), key=lambda g: g.distance(end), default=None)
        fw = [f.intersection(line.buffer(FAIRWAY_SEARCH * 3)) for f in fairways if f.distance(line) < FAIRWAY_SEARCH]
        widths = [w for w in (_fairway_width(f, line) for f in fw if f.area > 500) if w]
        out.append({
            "par": par,
            "length": line.length,
            "green_area": green.area if green is not None else None,
            "fairway_width": float(np.median(widths)) if widths else None,
            "bunkers": sum(1 for b in bunkers if b.intersects(corridor)),
            "water": any(w.distance(line) < 60 for w in water),
        })
    return out


def fit_priors(osm_paths: list[Path]) -> dict:
    holes = [h for p in osm_paths for h in measure_course(p)]
    priors = json.loads(json.dumps(DEFAULT_PRIORS))
    priors["source"] = [str(p.relative_to(PROJECT_ROOT)) for p in osm_paths]
    priors["holes"] = len(holes)

    for par in ("3", "4", "5"):
        hs = [h for h in holes if h["par"] == par]
        entry = priors["par"][par]
        entry["holes"] = len(hs)
        entry["length"] = _percentiles([h["length"] for h in hs]) or entry["length"]
        entry["fairway_width"] = _percentiles([h["fairway_width"] for h in hs if h["fairway_width"]]) or entry["fairway_width"]
        if hs:
            entry["bunkers"] = round(float(np.mean([h["bunkers"] for h in hs])), 2)
            entry["water_share"] = round(float(np.mean([h["water"] for h in hs])), 2)
    priors["green_area"] = _percentiles([h["green_area"] for h in holes if h["green_area"]]) or priors["green_area"]
    return priors


def cached_courses() -> list[Path]:
    return sorted(COURSE_SOURCES.glob("*/osm.json"))
