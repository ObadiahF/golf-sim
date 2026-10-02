"""Surface names, paint priority and point lookup (contract: Docs/hole-format/README.md, section 3)."""
from __future__ import annotations

import numpy as np
import shapely
from shapely.geometry import Polygon
from shapely.ops import unary_union

# Paint priority: later wins where polygons overlap. Ground covered by no polygon is "native".
SURFACES = ("native", "rough", "scrub", "woods", "fairway", "tee", "green", "bunker", "water")
PRIORITY = {s: i for i, s in enumerate(SURFACES)}
BASE = "native"
# No objects stand on these (or within the producer's clearance margin of them).
KEEP_CLEAR = ("fairway", "tee", "green", "bunker", "water")


def classify(x: np.ndarray, y: np.ndarray, areas: list[tuple[str, Polygon]]) -> np.ndarray:
    """Surface name at each point (object array), honouring paint priority."""
    best = np.zeros(len(x), dtype=np.int16)  # 0 = native
    for surface, poly in areas:
        rank = PRIORITY[surface]
        inside = shapely.contains_xy(poly, x, y)
        best = np.where(inside & (rank > best), rank, best)
    return np.asarray(SURFACES, dtype=object)[best]


def keep_clear_zone(areas: list[tuple[str, Polygon]], margin: float):
    """Union of keep-clear surfaces grown by `margin` meters (None when there are none)."""
    polys = [p for s, p in areas if s in KEEP_CLEAR]
    if not polys:
        return None
    zone = unary_union(polys).buffer(margin)
    shapely.prepare(zone)
    return zone
