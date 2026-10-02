"""Load OSM golf features from GeoJSON (overpass-turbo export) or raw Overpass JSON."""
from __future__ import annotations

import json
from dataclasses import dataclass, field
from pathlib import Path

from shapely.geometry import LineString, MultiPolygon, Point, Polygon, shape
from shapely.ops import linemerge, unary_union

# Surface names are the contract with Unity's SurfaceLayerSet. Keep them in sync.
GOLF_SURFACES = {
    "fairway": "fairway",
    "green": "green",
    "tee": "tee",
    "bunker": "bunker",
    "rough": "rough",
    "water_hazard": "water",
    "lateral_water_hazard": "water",
}
NATURAL_SURFACES = {"water": "water", "wood": "woods", "sand": "bunker", "scrub": "scrub", "heath": "scrub"}
LANDUSE_SURFACES = {"forest": "woods"}
COURSE_SURFACE = "rough"


@dataclass
class Feature:
    tags: dict
    geom: object  # shapely geometry in lon/lat
    osm_id: str = ""
    extra: dict = field(default_factory=dict)

    @property
    def surface(self) -> str | None:
        t = self.tags
        if t.get("leisure") == "golf_course":
            return COURSE_SURFACE
        return (GOLF_SURFACES.get(t.get("golf"))
                or NATURAL_SURFACES.get(t.get("natural"))
                or LANDUSE_SURFACES.get(t.get("landuse")))

    @property
    def is_area(self) -> bool:
        return isinstance(self.geom, (Polygon, MultiPolygon))


def load_features(path: str | Path) -> list[Feature]:
    data = json.loads(Path(path).read_text())
    if data.get("type") == "FeatureCollection":
        return _from_geojson(data)
    if "elements" in data:
        return _from_overpass(data)
    raise ValueError(f"{path}: not GeoJSON or Overpass JSON")


def _from_geojson(data: dict) -> list[Feature]:
    out = []
    for f in data["features"]:
        if not f.get("geometry"):
            continue
        props = dict(f.get("properties") or {})
        tags = props.pop("tags", None) or {k: v for k, v in props.items() if not k.startswith("@")}
        out.append(Feature(tags=tags, geom=shape(f["geometry"]), osm_id=str(f.get("id", props.get("@id", "")))))
    return out


def _coords(geometry: list[dict]) -> list[tuple[float, float]]:
    return [(p["lon"], p["lat"]) for p in geometry if p]


def _is_area_way(tags: dict, coords: list) -> bool:
    closed = len(coords) >= 4 and coords[0] == coords[-1]
    linear = tags.get("golf") in ("hole", "cartpath", "path") or tags.get("natural") == "tree_row"
    return closed and not linear and tags.get("area") != "no"


def _stitch_rings(lines: list[list[tuple]]) -> list[Polygon]:
    merged = linemerge([LineString(l) for l in lines if len(l) >= 2])
    parts = getattr(merged, "geoms", [merged])
    return [Polygon(p.coords) for p in parts if p.is_ring]


def _relation_polygon(el: dict) -> object | None:
    outers = [_coords(m["geometry"]) for m in el.get("members", []) if m.get("role") == "outer" and "geometry" in m]
    inners = [_coords(m["geometry"]) for m in el.get("members", []) if m.get("role") == "inner" and "geometry" in m]
    outer_polys = _stitch_rings(outers)
    if not outer_polys:
        return None
    area = unary_union(outer_polys)
    for hole in _stitch_rings(inners):
        area = area.difference(hole)
    return area


def _from_overpass(data: dict) -> list[Feature]:
    out = []
    for el in data["elements"]:
        tags = el.get("tags")
        if not tags:
            continue
        osm_id = f"{el['type']}/{el['id']}"
        if el["type"] == "node":
            geom = Point(el["lon"], el["lat"])
        elif el["type"] == "way":
            coords = _coords(el.get("geometry", []))
            if len(coords) < 2:
                continue
            geom = Polygon(coords) if _is_area_way(tags, coords) else LineString(coords)
        elif el["type"] == "relation" and tags.get("type") in ("multipolygon", "boundary"):
            geom = _relation_polygon(el)
            if geom is None:
                continue
        else:
            continue
        if not geom.is_valid:
            geom = geom.buffer(0)
        out.append(Feature(tags=tags, geom=geom, osm_id=osm_id))
    return out
