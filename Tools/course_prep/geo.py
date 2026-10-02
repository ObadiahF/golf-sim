"""Hole framing and coordinate conversion: lon/lat -> UTM -> local terrain meters."""
from __future__ import annotations

import math
from dataclasses import dataclass

from pyproj import Transformer
from shapely.geometry import box
from shapely.ops import transform

HEIGHTMAP_SIZES = (257, 513, 1025, 2049, 4097)


def utm_epsg(lon: float, lat: float) -> int:
    zone = int((lon + 180) // 6) + 1
    return (32600 if lat >= 0 else 32700) + zone


@dataclass
class HoleFrame:
    """A north-up square terrain tile. Local coords: x = east, z = north, origin at SW corner."""
    epsg: int
    origin_x: float  # UTM easting of SW corner
    origin_y: float  # UTM northing of SW corner
    size: float      # meters per side
    resolution: int  # heightmap samples per side (2^n + 1)

    @property
    def spacing(self) -> float:
        return self.size / (self.resolution - 1)

    @property
    def utm_box(self):
        return box(self.origin_x, self.origin_y, self.origin_x + self.size, self.origin_y + self.size)

    def to_local(self, geom):
        return transform(lambda x, y, z=None: (x - self.origin_x, y - self.origin_y), geom)


class Projector:
    def __init__(self, epsg: int):
        self.epsg = epsg
        self._fwd = Transformer.from_crs(4326, epsg, always_xy=True)
        self._inv = Transformer.from_crs(epsg, 4326, always_xy=True)

    def to_utm(self, geom):
        return transform(self._fwd.transform, geom)

    def to_lonlat(self, geom):
        return transform(self._inv.transform, geom)


def pick_resolution(size: float, max_spacing: float) -> int:
    for n in HEIGHTMAP_SIZES:
        if size / (n - 1) <= max_spacing:
            return n
    return HEIGHTMAP_SIZES[-1]


def frame_hole(hole_line_utm, epsg: int, margin: float, max_spacing: float) -> HoleFrame:
    minx, miny, maxx, maxy = hole_line_utm.buffer(margin).bounds
    size = math.ceil(max(maxx - minx, maxy - miny))
    cx, cy = (minx + maxx) / 2, (miny + maxy) / 2
    return HoleFrame(epsg, cx - size / 2, cy - size / 2, size, pick_resolution(size, max_spacing))
