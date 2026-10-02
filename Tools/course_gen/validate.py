"""Playability checks: reject layouts no golfer would accept."""
from __future__ import annotations

import math

import numpy as np
import shapely
from shapely.geometry import LineString, Point, box
from shapely.ops import unary_union

from layout import TEE_MARGIN, Layout
from terrain import Grid

MAX_SHOT = 265.0          # longest shot asked of the player into the green (m)
MAX_CARRY = 170.0         # longest continuous water carry along the line of play (m)
MIN_FAIRWAY = {3: 0.0, 4: 2500.0, 5: 3500.0}

# Tee shot clearance (TH-5): the ground ahead of the tee must stay under a low launch line.
LAUNCH_DEG = 8.0          # a driver launches at ~10-12 deg, a 5 iron at ~14; anything steeper ahead is a wall
LAUNCH_RUN = 100.0        # meters of the line of play checked from the tee
LAUNCH_SPREAD_DEG = 6.0   # also check straight lines this far left and right of the opening line
LAUNCH_SLACK = 0.15       # meters of tolerance (grass, tee pad edge)


def problems(layout: Layout) -> list[str]:
    """Empty list = playable."""
    issues = []
    tile = box(0, 0, layout.size, layout.size)
    water = unary_union(layout.water) if layout.water else None
    hazards = unary_union([*layout.bunkers, *layout.water, *layout.woods]) if (
        layout.bunkers or layout.water or layout.woods) else None

    if not tile.contains(layout.green) or not tile.contains(layout.rough.buffer(-1)):
        issues.append("course leaves the terrain tile")
    tee = Point(layout.path.coords[0])
    if not any(t.buffer(-TEE_MARGIN * 0.5).contains(tee) for t in layout.tees):
        issues.append("tee point is off the tee box")
    if water is not None:
        if water.intersects(layout.green):
            issues.append("water on the green")
        if any(water.intersects(t) for t in layout.tees):
            issues.append("water on a tee")
        crossing = layout.path.intersection(water)
        longest = max((g.length for g in getattr(crossing, "geoms", [crossing])), default=0.0)
        if longest > MAX_CARRY:
            issues.append(f"{longest:.0f} m water carry")
    if hazards is not None:
        for i, p in enumerate(layout.landings):
            if hazards.contains(p):
                issues.append(f"landing zone {i + 1} sits in a hazard")
        if layout.woods and unary_union(layout.woods).intersects(layout.path.buffer(8)):
            issues.append("woods block the line of play")
    if layout.fairway.area < MIN_FAIRWAY[layout.par]:
        issues.append(f"fairway too small ({layout.fairway.area:.0f} m2)")
    if layout.par > 3 and layout.landings and not layout.fairway.buffer(3).contains(layout.landings[0]):
        issues.append("first landing zone is off the fairway")

    stops = [layout.path.coords[0], *[(p.x, p.y) for p in layout.landings]]
    last = stops[-1]
    pin = layout.pin
    into_green = ((pin.x - last[0]) ** 2 + (pin.y - last[1]) ** 2) ** 0.5
    if into_green > MAX_SHOT:
        issues.append(f"{into_green:.0f} m shot into the green")
    return issues


def launch_overshoot(path: LineString, heights: np.ndarray, size: float) -> dict[str, tuple[np.ndarray, np.ndarray]]:
    """How far the ground rises above a low tee shot: {line name: (s, over)}, `over[i]` meters at `s[i]` m from
    the tee (> 0: the ball hits the ground), for the first LAUNCH_RUN m along the hole line and along straight
    lines LAUNCH_SPREAD_DEG either side of it. `heights` is the tile heightmap (row 0 south)."""
    grid = Grid(size, heights.shape[0])
    tee = path.coords[0]
    s = np.arange(1.0, min(LAUNCH_RUN, path.length) + 0.5, 1.0)
    ceiling = grid.sample(heights, *tee)[0] + LAUNCH_SLACK + math.tan(math.radians(LAUNCH_DEG)) * s
    ahead = path.interpolate(min(LAUNCH_RUN * 0.5, path.length))
    heading = math.atan2(ahead.y - tee[1], ahead.x - tee[0])
    along = shapely.line_interpolate_point(path, s)
    lines = {"the hole line": (shapely.get_x(along), shapely.get_y(along))}
    for side, sign in (("left", 1), ("right", -1)):
        a = heading + sign * math.radians(LAUNCH_SPREAD_DEG)
        lines[f"the {side} edge"] = (tee[0] + np.cos(a) * s, tee[1] + np.sin(a) * s)
    return {name: (s, grid.sample(heights, xs, zs) - ceiling) for name, (xs, zs) in lines.items()}


def launch_issues(profile: dict[str, tuple[np.ndarray, np.ndarray]]) -> list[str]:
    """The worst point of each launch_overshoot line that does not clear the ground, as problems."""
    issues = []
    for name, (s, over) in profile.items():
        i = int(np.argmax(over))
        if over[i] > 0:
            issues.append(f"ground {over[i]:.1f} m above a {LAUNCH_DEG:.0f} deg tee shot {s[i]:.0f} m out on {name}")
    return issues


def launch_problems(path: LineString, heights: np.ndarray, size: float) -> list[str]:
    """Empty list = a low tee shot clears the ground (see launch_overshoot)."""
    return launch_issues(launch_overshoot(path, heights, size))
