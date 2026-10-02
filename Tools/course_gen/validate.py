"""Playability checks: reject layouts no golfer would accept."""
from __future__ import annotations

from shapely.geometry import Point, box
from shapely.ops import unary_union

from layout import TEE_MARGIN, Layout

MAX_SHOT = 265.0          # longest shot asked of the player into the green (m)
MAX_CARRY = 170.0         # longest continuous water carry along the line of play (m)
MIN_FAIRWAY = {3: 0.0, 4: 2500.0, 5: 3500.0}


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
