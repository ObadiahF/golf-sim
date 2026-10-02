"""Places every tree, shrub and rock of a hole (the objects.bin contents) from its surfaces and terrain.

Rules per theme live in vegetation_themes.py. Placement: a jittered grid per rule (even coverage, no
overlaps within a rule), thinned by clumping noise into groves, kept to the rule's surfaces and slope, and
kept `CLEAR_MARGIN` meters away from fairways, tees, greens, bunkers and water. Specimen trees (OSM-mapped
or placed by the generator) are kept as given unless they stand on a keep-clear surface. An optional `tree_clear`
zone (the generator's shot lines) is kept free of every tree.
"""
from __future__ import annotations

import numpy as np
import shapely
from shapely.geometry import Polygon

from noise import Fbm
from objects_bin import KIND_CODE, PLACED
from surfaces import KEEP_CLEAR, classify, keep_clear_zone
from vegetation_themes import CROWNS, SHAPES, Rule, theme as get_theme

CLEAR_MARGIN = 4.0      # meters between scattered objects and keep-clear surfaces
GROVE_SIZE = 40.0       # meters; wavelength of the clumping noise


class Terrain:
    """Slope lookup from a heightmap in meters ([row, col], row 0 = south)."""

    def __init__(self, heights: np.ndarray, size: float):
        self.n = heights.shape[0]
        self.size = size
        spacing = size / (self.n - 1)
        dz_dy, dz_dx = np.gradient(heights.astype(np.float64), spacing)
        self.slope = np.degrees(np.arctan(np.hypot(dz_dx, dz_dy)))

    def slope_at(self, x: np.ndarray, y: np.ndarray) -> np.ndarray:
        scale = (self.n - 1) / self.size
        col = np.clip(np.round(x * scale).astype(int), 0, self.n - 1)
        row = np.clip(np.round(y * scale).astype(int), 0, self.n - 1)
        return self.slope[row, col]


def plant(areas: list[tuple[str, Polygon]], heights: np.ndarray, size: float, theme_name: str | None, seed: int,
          specimens: list[tuple[float, float]] = (), tree_density: float = 1.0,
          tree_clear: Polygon | None = None) -> np.ndarray:
    """All objects for a hole, decoded (objects_bin.PLACED). `tree_density` scales every tree rule; no tree stands
    in `tree_clear`."""
    theme = get_theme(theme_name)
    rng = np.random.default_rng([seed, 7193])
    noise = Fbm(seed * 13 + 5, GROVE_SIZE, octaves=2)
    terrain = Terrain(heights, size)
    zone = keep_clear_zone(areas, CLEAR_MARGIN)

    parts = [_specimens(specimens, areas, theme.trees, rng)]
    for i, rule in enumerate(theme.rules()):
        density = rule.per_hectare * (tree_density if rule.trees else 1.0)
        parts.append(_scatter(rule, density, areas, terrain, zone, size, rng, noise, salt=i))
    objects = np.concatenate(parts) if parts else np.zeros(0, PLACED)
    if tree_clear is None or tree_clear.is_empty:
        return objects
    trees = np.isin(objects["kind"], [KIND_CODE[k] for k in CROWNS])
    return objects[~(trees & shapely.contains_xy(tree_clear, objects["x"], objects["y"]))]


def _scatter(rule: Rule, per_hectare: float, areas, terrain: Terrain, zone, size: float,
             rng: np.random.Generator, noise: Fbm, salt: int) -> np.ndarray:
    if per_hectare <= 0:
        return np.zeros(0, PLACED)
    cell = np.sqrt(10000.0 / per_hectare)
    count = int(np.ceil(size / cell))
    gx, gy = np.meshgrid(np.arange(count), np.arange(count))
    x = (gx.ravel() + rng.random(gx.size)) * cell
    y = (gy.ravel() + rng.random(gy.size)) * cell
    keep = (x < size) & (y < size)
    if rule.clumping > 0:
        n = np.clip((noise(x, y, salt=salt) + 1) / 2, 0, 1)
        chance = n ** (1 + rule.clumping * 4) * (1 + rule.clumping * 3)
        keep &= rng.random(x.size) < chance
    x, y = x[keep], y[keep]
    keep = np.isin(classify(x, y, areas), rule.surfaces)
    if zone is not None:
        keep &= ~shapely.contains_xy(zone, x, y)
    x, y = x[keep], y[keep]
    slope = terrain.slope_at(x, y)
    keep = (slope >= rule.slope[0]) & (slope <= rule.slope[1])
    return _objects(x[keep], y[keep], rule.kinds, rng)


def _specimens(points, areas, kinds: dict[str, float], rng: np.random.Generator) -> np.ndarray:
    if len(points) == 0:
        return np.zeros(0, PLACED)
    x, y = np.asarray(points, dtype=np.float64).T
    keep = ~np.isin(classify(x, y, areas), KEEP_CLEAR)  # trust the source, except on a green, bunker ...
    return _objects(x[keep], y[keep], kinds, rng)


def _objects(x: np.ndarray, y: np.ndarray, kinds: dict[str, float], rng: np.random.Generator) -> np.ndarray:
    out = np.zeros(len(x), PLACED)
    if len(x) == 0:
        return out
    names = list(kinds)
    weights = np.array([kinds[k] for k in names], dtype=np.float64)
    picked = rng.choice(len(names), size=len(x), p=weights / weights.sum())
    out["x"], out["y"] = x, y
    out["rotation"] = rng.random(len(x)) * 360
    out["variant"] = rng.random(len(x))
    t = rng.random(len(x))
    for i, name in enumerate(names):
        sel = picked == i
        (lo, hi), radius = SHAPES[name]
        out["kind"][sel] = KIND_CODE[name]
        out["height"][sel] = lo + (hi - lo) * t[sel]
        out["radius"][sel] = out["height"][sel] * radius
    return out
