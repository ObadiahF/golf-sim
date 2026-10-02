"""Select a hole from OSM features and write a Unity hole package (hole.json + heightmap.raw)."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from shapely.geometry import LineString, MultiPolygon, Point, Polygon

from dem_io import sample_dem, write_raw16
from geo import HoleFrame, Projector, frame_hole, utm_epsg
from osm_io import Feature
from water import carve_water

PACKAGE_VERSION = 1
PIN_SEARCH_RADIUS = 40.0  # meters from the hole line's end
MIN_AREA = 1.0            # m^2; drop slivers left after clipping
TREE_ROW_SPACING = 8.0    # meters between trees sampled along a tree_row


@dataclass
class HoleChoice:
    line: Feature
    course: str

    @property
    def ref(self) -> str:
        return self.line.tags.get("ref", "?")

    def describe(self) -> str:
        t = self.line.tags
        return f"hole {self.ref:>2}  par {t.get('par', '?')}  course '{self.course or '?'}'  ({self.line.osm_id})"


def list_holes(features: list[Feature]) -> list[HoleChoice]:
    courses = [f for f in features if f.tags.get("leisure") == "golf_course" and f.is_area]
    holes = []
    for f in features:
        if f.tags.get("golf") == "hole" and isinstance(f.geom, LineString):
            home = next((c for c in courses if c.geom.intersects(Point(f.geom.coords[0]))), None)
            holes.append(HoleChoice(f, home.tags.get("name", "") if home else ""))
    return sorted(holes, key=lambda h: (h.course, int(h.ref) if h.ref.isdigit() else 999))


def select_hole(features: list[Feature], ref: str, course: str | None) -> HoleChoice:
    matches = [h for h in list_holes(features) if h.ref == ref]
    if course:
        matches = [h for h in matches if course.lower() in h.course.lower()]
    if len(matches) == 1:
        return matches[0]
    detail = "\n  ".join(h.describe() for h in matches) or "(none)"
    hint = "Pass --course to pick one." if matches else "Run 'list' to see available holes."
    raise SystemExit(f"Expected exactly one hole {ref}, found {len(matches)}:\n  {detail}\n{hint}")


def flat_points(coords) -> list[float]:
    return [round(v, 3) for xy in coords for v in xy[:2]]


def _polygons(geom) -> list[Polygon]:
    if isinstance(geom, Polygon):
        return [geom]
    if isinstance(geom, MultiPolygon):
        return list(geom.geoms)
    return [g for part in getattr(geom, "geoms", []) for g in _polygons(part)]


def _local_areas(features: list[Feature], frame: HoleFrame, proj: Projector) -> list[tuple[Feature, Polygon]]:
    """Surface polygons clipped to the tile, in local terrain meters."""
    tile = frame.utm_box
    out = []
    for f in features:
        if f.is_area and f.surface:
            clipped = proj.to_utm(f.geom).intersection(tile)
            out.extend((f, poly) for poly in _polygons(frame.to_local(clipped)) if poly.area >= MIN_AREA)
    return out


def area_json(surface: str, source_id: str, poly: Polygon) -> dict:
    """One hole.json area entry; shared by real (OSM) and generated holes."""
    rings = [poly.exterior, *poly.interiors]
    return {"surface": surface, "osmId": source_id, "rings": [{"points": flat_points(r.coords)} for r in rings]}


def _area_json(f: Feature, poly: Polygon) -> dict:
    return area_json(f.surface, f.osm_id, poly)


def _trees(features: list[Feature], frame: HoleFrame, proj: Projector) -> list[dict]:
    """Individually mapped trees (natural=tree) plus points sampled along natural=tree_row lines."""
    tile = frame.utm_box
    points = []
    for f in features:
        kind = f.tags.get("natural")
        if kind == "tree" and isinstance(f.geom, Point):
            points.append(proj.to_utm(f.geom))
        elif kind == "tree_row" and isinstance(f.geom, LineString):
            row = proj.to_utm(f.geom)
            count = max(1, int(row.length // TREE_ROW_SPACING))
            points.extend(row.interpolate(i / count, normalized=True) for i in range(count + 1))
    return [xz_json(frame.to_local(p)) for p in points if tile.contains(p)]


def _pin(features: list[Feature], line_end_utm: Point, proj: Projector) -> Point:
    pins = [proj.to_utm(f.geom) for f in features if f.tags.get("golf") == "pin" and isinstance(f.geom, Point)]
    near = [p for p in pins if p.distance(line_end_utm) <= PIN_SEARCH_RADIUS]
    return min(near, key=line_end_utm.distance) if near else line_end_utm


def xz_json(p: Point) -> dict:
    return {"x": round(p.x, 3), "y": round(p.y, 3)}


def build_package(features: list[Feature], hole: HoleChoice, dem_sources: list[str], out_dir: Path,
                  margin: float, max_spacing: float) -> dict:
    lon, lat = hole.line.geom.coords[0][:2]
    proj = Projector(utm_epsg(lon, lat))
    line_utm = proj.to_utm(hole.line.geom)
    frame = frame_hole(line_utm, proj.epsg, margin, max_spacing)

    areas = _local_areas(features, frame, proj)
    out_dir.mkdir(parents=True, exist_ok=True)
    heights = sample_dem(dem_sources, frame)
    water = carve_water(heights, [poly for f, poly in areas if f.surface == "water"], frame)
    lo, hi = write_raw16(heights, out_dir / "heightmap.raw")

    pin_utm = _pin(features, Point(line_utm.coords[-1]), proj)
    tags = hole.line.tags
    package = {
        "version": PACKAGE_VERSION,
        "course": hole.course,
        "holeRef": hole.ref,
        "par": int(tags["par"]) if str(tags.get("par", "")).isdigit() else 0,
        "handicap": int(tags["handicap"]) if str(tags.get("handicap", "")).isdigit() else 0,
        "crs": f"EPSG:{frame.epsg}",
        "originEasting": round(frame.origin_x, 3),
        "originNorthing": round(frame.origin_y, 3),
        "sizeMeters": frame.size,
        "heightmapFile": "heightmap.raw",
        "heightmapResolution": frame.resolution,
        "minElevation": round(lo, 3),
        "maxElevation": round(hi, 3),
        "holePath": {"points": flat_points(frame.to_local(line_utm).coords)},
        "tee": xz_json(frame.to_local(Point(line_utm.coords[0]))),
        "pin": xz_json(frame.to_local(pin_utm)),
        "areas": [_area_json(f, poly) for f, poly in areas],
        "trees": _trees(features, frame, proj),
        "water": water,
    }
    (out_dir / "hole.json").write_text(json.dumps(package, indent=1))
    return package
