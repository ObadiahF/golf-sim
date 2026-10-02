"""Small geometry helpers shared by the layout generator."""
from __future__ import annotations

import math

import numpy as np
from shapely import affinity
from shapely.geometry import LineString, MultiPolygon, Point, Polygon
from shapely.ops import unary_union

QUAD_SEGS = 6


def polygons(geom) -> list[Polygon]:
    """Flatten any geometry into its non-empty polygons."""
    if geom is None or geom.is_empty:
        return []
    if isinstance(geom, Polygon):
        return [geom]
    if isinstance(geom, MultiPolygon):
        return list(geom.geoms)
    return [p for g in getattr(geom, "geoms", []) for p in polygons(g)]


def blob(center, radius: float, rng: np.random.Generator, aspect: float = 1.0, angle_deg: float = 0.0,
         wobble: float = 0.12, points: int = 40) -> Polygon:
    """Organic closed shape: an ellipse whose radius wobbles with a few random harmonics."""
    phi = np.linspace(0, 2 * math.pi, points, endpoint=False)
    r = np.ones_like(phi)
    for k in (2, 3, 4, 5):
        r += rng.uniform(0, wobble) / (k - 1) * np.cos(k * phi + rng.uniform(0, 2 * math.pi))
    x = radius * aspect * r * np.cos(phi)
    y = radius * r * np.sin(phi)
    shape = Polygon(zip(x, y)).buffer(0)
    shape = affinity.rotate(shape, angle_deg, origin=(0, 0))
    return affinity.translate(shape, *(center.x, center.y) if isinstance(center, Point) else center)


def area_radius(area: float, aspect: float = 1.0) -> float:
    """Minor radius of an ellipse with the given area and aspect ratio."""
    return math.sqrt(area / (math.pi * aspect))


def smooth(geom, radius: float):
    """Round off corners and fuse near-touching parts (closing then opening)."""
    if geom.is_empty:
        return geom
    return geom.buffer(radius, quad_segs=QUAD_SEGS).buffer(-2 * radius, quad_segs=QUAD_SEGS).buffer(radius, quad_segs=QUAD_SEGS)


def tube(path: LineString, start: float, end: float, half_width, step: float = 4.0):
    """Union of discs along `path` between two distances; half_width(s) gives the radius at s."""
    start, end = max(0.0, start), min(path.length, end)
    if end - start < step:
        return Polygon()
    discs = [path.interpolate(s).buffer(half_width(s), quad_segs=QUAD_SEGS)
             for s in np.arange(start, end + 1e-6, step)]
    return unary_union(discs)


def heading_at(path: LineString, s: float, ds: float = 3.0) -> float:
    """Direction of travel (radians, math convention) at distance s along the path."""
    a = path.interpolate(max(0.0, s - ds))
    b = path.interpolate(min(path.length, s + ds))
    return math.atan2(b.y - a.y, b.x - a.x)


def offset_point(path: LineString, s: float, side: float) -> Point:
    """Point at distance s along the path, shifted `side` meters to the left (negative = right)."""
    p = path.interpolate(s)
    h = heading_at(path, s)
    return Point(p.x - math.sin(h) * side, p.y + math.cos(h) * side)


def noise_1d(rng: np.random.Generator, wavelengths=(90.0, 45.0, 22.0)):
    """Smooth random function of distance, roughly in -1..1."""
    phases = rng.uniform(0, 2 * math.pi, len(wavelengths))
    weights = np.array([1.0, 0.5, 0.25])[: len(wavelengths)]
    weights /= weights.sum()

    def f(s: float) -> float:
        return float(sum(w * math.sin(2 * math.pi * s / wl + ph) for w, wl, ph in zip(weights, wavelengths, phases)))
    return f


def rect(center: Point, length: float, width: float, heading_rad: float) -> Polygon:
    box = Polygon([(-width / 2, -length / 2), (width / 2, -length / 2), (width / 2, length / 2), (-width / 2, length / 2)])
    box = affinity.rotate(box, math.degrees(heading_rad) - 90, origin=(0, 0))
    return affinity.translate(box, center.x, center.y)
