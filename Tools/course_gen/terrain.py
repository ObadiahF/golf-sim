"""Sculpt a heightmap for a layout: natural noise, then golf-course shaping on top.

Grid convention matches course_prep: heights[row, col] in meters, row 0 = south, col 0 = west,
sample spacing = size / (resolution - 1).
"""
from __future__ import annotations

import math

import numpy as np
from PIL import Image, ImageDraw
from scipy.ndimage import distance_transform_edt, gaussian_filter

from layout import Layout
from noise import Fbm
from shapes import polygons
from style import Style

BASE_ELEVATION = 100.0   # arbitrary datum so heights read like real meters above sea level
GREEN_RAISE = 0.45
TEE_RAISE = 0.5
BANK_WIDTH = 10.0        # meters of shore raised around ponds


class Grid:
    def __init__(self, size: float, resolution: int):
        self.size, self.n = size, resolution
        self.d = size / (resolution - 1)
        c = np.arange(resolution) * self.d
        self.x, self.z = np.meshgrid(c, c)  # [row, col], row = south..north

    def mask(self, geoms) -> np.ndarray:
        """Boolean raster of polygons (holes respected), same orientation as the heights."""
        img = Image.new("1", (self.n, self.n), 0)
        draw = ImageDraw.Draw(img)
        for g in geoms:
            for poly in polygons(g):
                draw.polygon([(x / self.d, z / self.d) for x, z in poly.exterior.coords], fill=1)
                for hole in poly.interiors:
                    draw.polygon([(x / self.d, z / self.d) for x, z in hole.coords], fill=0)
        return np.array(img, dtype=bool)  # image row y = z / d, so row 0 is south already

    def dist_outside(self, mask: np.ndarray) -> np.ndarray:
        """Meters from each cell to the nearest masked cell (0 inside)."""
        return distance_transform_edt(~mask) * self.d if mask.any() else np.full(mask.shape, 1e6)

    def dist_inside(self, mask: np.ndarray) -> np.ndarray:
        return distance_transform_edt(mask) * self.d


def smoothstep(t):
    t = np.clip(t, 0.0, 1.0)
    return t * t * (3 - 2 * t)


def sculpt(layout: Layout, style: Style, grid: Grid, seed: int) -> np.ndarray:
    relief = 1.0 + 44.0 * style.relief ** 1.4
    wavelength = 420 - 330 * style.hilliness
    noise = Fbm(seed, wavelength)
    h = noise(grid.x, grid.z, salt=2) * relief
    h += _tilt(layout, style, grid, relief)

    # Golf courses are shaped: smooth the corridor, smoother still on the fairway.
    rough = grid.mask([layout.rough])
    fairway = grid.mask([layout.fairway])
    soft = gaussian_filter(h, 22 / grid.d)
    blend = 0.65 * smoothstep(1 - grid.dist_outside(rough) / 25) + 0.25 * smoothstep(1 - grid.dist_outside(fairway) / 15)
    h = h + (soft - h) * np.clip(blend, 0, 0.9)
    if style.hilliness > 0.6:  # links: small dune bumps survive in the rough
        h += Fbm(seed + 1, 28)(grid.x, grid.z) * 0.9 * (style.hilliness - 0.5) * (1 - 0.8 * fairway)

    h = _pad(h, grid, layout.tees, TEE_RAISE, slope=0.0, transition=5.0)
    h = _pad(h, grid, [layout.green], GREEN_RAISE, slope=0.025, transition=7.0, layout=layout,
             undulation=Fbm(seed + 2, 14))
    for bunker in layout.bunkers:
        h = _dig(h, grid, bunker, depth=0.55 + 0.35 * (bunker.area > 200))
    for pond in layout.water:
        h = _pond_bed(h, grid, pond)
    return h + BASE_ELEVATION


def _tilt(layout: Layout, style: Style, grid: Grid, relief: float) -> np.ndarray:
    """Plane rising (or falling) from tee to green by the style's slope."""
    tee, pin = layout.path.coords[0], layout.path.coords[-1]
    dx, dz = pin[0] - tee[0], pin[1] - tee[1]
    length = math.hypot(dx, dz)
    change = (style.slope - 0.5) * 2 * (0.6 * relief + 3)
    return ((grid.x - tee[0]) * dx + (grid.z - tee[1]) * dz) / (length * length) * change


def _pad(h, grid: Grid, shapes, raise_m: float, slope: float, transition: float, layout: Layout | None = None,
         undulation: Fbm | None = None):
    """Flatten each shape into a plane (optionally tilted back-to-front) raised above its surroundings."""
    for shape in shapes:
        m = grid.mask([shape])
        if not m.any():
            continue
        base = float(np.median(h[m])) + raise_m
        surface = np.full_like(h, base)
        if layout is not None and slope:
            # Greens tilt toward the approach: back higher than front.
            a = layout.path.interpolate(layout.path.length - 20)
            b = layout.path.coords[-1]
            dx, dz = b[0] - a.x, b[1] - a.y
            norm = math.hypot(dx, dz) or 1
            c = shape.centroid
            surface += ((grid.x - c.x) * dx + (grid.z - c.y) * dz) / norm * slope
        if undulation is not None:
            surface += undulation(grid.x, grid.z) * 0.18
        w = smoothstep(1 - grid.dist_outside(m) / transition)
        h = h + (surface - h) * w
    return h


def _dig(h, grid: Grid, bunker, depth: float):
    m = grid.mask([bunker])
    if not m.any():
        return h
    return h - depth * smoothstep(grid.dist_inside(m) / 2.0)


def _pond_bed(h, grid: Grid, pond):
    """Flatten the pond to one water level and raise banks so water never spills over low ground.

    water.carve_water later takes the median height inside the pond (= this level) and digs the basin.
    """
    m = grid.mask([pond])
    if not m.any():
        return h
    level = float(np.percentile(h[m], 30))
    h = np.where(m, level, h)
    # Banks: at least slightly above the water near the shore, sloping away at ~15% beyond it.
    dist = grid.dist_outside(m)
    bank = level + 0.4 * smoothstep(dist / 2.0) - np.maximum(0.0, dist - BANK_WIDTH) * 0.15
    return np.where(m, h, np.maximum(h, bank))
