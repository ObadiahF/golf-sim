"""Water bodies: find each pond's level, carve a basin under it, and triangulate its surface.

3DEP DEMs are hydro-flattened (lakes are flat at water level), so the median DEM height inside a
water polygon is its surface level. The terrain is carved below that so the water has visible depth.
"""
from __future__ import annotations

import numpy as np
import shapely
from shapely.geometry import Polygon

from geo import HoleFrame

MAX_DEPTH = 1.5        # meters at the middle of the pond
SHORE_RAMP = 4.0       # meters from the shoreline to full depth
SHORE_DROP = 0.15      # terrain sits at least this far below the surface right at the shore


def carve_water(heights: np.ndarray, water_polys: list[Polygon], frame: HoleFrame) -> list[dict]:
    """Lowers `heights` (south-first rows, meters) in place under each pond. Returns surface meshes."""
    n, d = frame.resolution, frame.spacing
    bodies = []
    for poly in water_polys:
        minx, miny, maxx, maxy = poly.bounds
        c0, c1 = max(0, int(minx // d)), min(n - 1, int(maxx // d) + 1)
        r0, r1 = max(0, int(miny // d)), min(n - 1, int(maxy // d) + 1)
        cols, rows = np.meshgrid(np.arange(c0, c1 + 1), np.arange(r0, r1 + 1))
        xs, zs = cols * d, rows * d
        inside = shapely.contains_xy(poly, xs, zs)
        if not inside.any():
            continue

        block = heights[r0:r1 + 1, c0:c1 + 1]
        level = float(np.median(block[inside]))
        shore_dist = shapely.distance(poly.exterior, shapely.points(xs[inside], zs[inside]))
        for hole in poly.interiors:  # islands count as shoreline too
            shore_dist = np.minimum(shore_dist, shapely.distance(hole, shapely.points(xs[inside], zs[inside])))
        ramp = np.clip(shore_dist / SHORE_RAMP, 0, 1)
        floor = level - SHORE_DROP - (MAX_DEPTH - SHORE_DROP) * ramp
        block[inside] = np.minimum(block[inside], floor)

        bodies.append({"level": round(level, 3), "triangles": {"points": _triangles(poly)}})
    return bodies


def _triangles(poly: Polygon) -> list[float]:
    """Flat [x0,z0, x1,z1, x2,z2, ...] for a constrained triangulation that respects holes."""
    out = []
    for tri in shapely.constrained_delaunay_triangles(poly).geoms:
        for x, z in list(tri.exterior.coords)[:3]:
            out.extend((round(x, 3), round(z, 3)))
    return out
