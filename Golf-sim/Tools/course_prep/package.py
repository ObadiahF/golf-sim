"""Select a hole from OSM features and write a hole package (Docs/hole-format)."""
from __future__ import annotations

import json
from dataclasses import dataclass
from pathlib import Path

from shapely.geometry import LineString, MultiPolygon, Point, Polygon

from dem_io import sample_dem
from geo import HoleFrame, Projector, frame_hole, utm_epsg
from hole_package import area_json, flat_points, make_id, seed_from_id, write_package, xz_json
from osm_io import Feature
from vegetation import plant
from water import carve_water

DEFAULT_THEME = "coastal"
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


def _trees(features: list[Feature], frame: HoleFrame, proj: Projector) -> list[tuple[float, float]]:
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
    return [(q.x, q.y) for q in (frame.to_local(p) for p in points if tile.contains(p))]


def _pin(features: list[Feature], line_end_utm: Point, proj: Projector) -> Point:
    pins = [proj.to_utm(f.geom) for f in features if f.tags.get("golf") == "pin" and isinstance(f.geom, Point)]
    near = [p for p in pins if p.distance(line_end_utm) <= PIN_SEARCH_RADIUS]
    return min(near, key=line_end_utm.distance) if near else line_end_utm


def package_id(hole: HoleChoice) -> str:
    return make_id(f"{hole.course or 'course'}_{hole.ref.zfill(2)}")


def build_package(features: list[Feature], hole: HoleChoice, dem_sources: list[str], out_dir: Path,
                  margin: float, max_spacing: float, theme: str = DEFAULT_THEME) -> dict:
    """Writes the package into out_dir, whose name must be package_id(hole)."""
    lon, lat = hole.line.geom.coords[0][:2]
    proj = Projector(utm_epsg(lon, lat))
    line_utm = proj.to_utm(hole.line.geom)
    frame = frame_hole(line_utm, proj.epsg, margin, max_spacing)

    areas = _local_areas(features, frame, proj)
    heights = sample_dem(dem_sources, frame)
    water = carve_water(heights, [poly for f, poly in areas if f.surface == "water"], frame)
    surfaces = [(f.surface, poly) for f, poly in areas]
    hole_id = out_dir.name
    objects = plant(surfaces, heights, frame.size, theme, seed=seed_from_id(hole_id),
                    specimens=_trees(features, frame, proj))

    pin_utm = _pin(features, Point(line_utm.coords[-1]), proj)
    tags = hole.line.tags
    return write_package(out_dir, {
        "id": hole_id,
        "course": hole.course,
        "holeRef": hole.ref,
        "par": int(tags["par"]) if str(tags.get("par", "")).isdigit() else 4,
        "handicap": int(tags["handicap"]) if str(tags.get("handicap", "")).isdigit() else 0,
        "theme": theme,
        "source": {"kind": "osm", "crs": f"EPSG:{frame.epsg}", "originEasting": round(frame.origin_x, 3),
                   "originNorthing": round(frame.origin_y, 3)},
        "sizeMeters": frame.size,
        "holePath": {"points": flat_points(frame.to_local(line_utm).coords)},
        "tee": xz_json(frame.to_local(Point(line_utm.coords[0]))),
        "pin": xz_json(frame.to_local(pin_utm)),
        "areas": [area_json(f.surface, f.osm_id, poly) for f, poly in areas],
        "water": water,
    }, heights, objects)
