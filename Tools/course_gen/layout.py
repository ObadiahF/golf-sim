"""Plan a hole's 2D layout from a style: route, tees, fairway, green, bunkers, water, woods, trees.

Everything is built in "hole space" (back tee at the origin, playing north), then rotated to a
random heading and shifted into a square terrain tile with its origin at the south-west corner.
"""
from __future__ import annotations

import math
from dataclasses import dataclass, field

import numpy as np
from shapely import affinity
from shapely.geometry import LineString, Point, Polygon, box
from shapely.ops import unary_union

from priors import lerp_range
from shapes import area_radius, blob, heading_at, noise_1d, offset_point, polygons, rect, smooth, tube
from style import Style

MAX_DOGLEG_DEG = 50.0
FRAME_MARGIN = 45.0       # meters of terrain beyond the course area
CELL = 12.0               # woods/scrub mask cell size (meters)


@dataclass
class Layout:
    par: int
    path: LineString                       # back tee -> pin
    tees: list[Polygon]
    green: Polygon
    pin: Point
    fairway: Polygon
    rough: Polygon                         # course corridor; everything else is native ground
    bunkers: list[Polygon] = field(default_factory=list)
    water: list[Polygon] = field(default_factory=list)
    woods: list[Polygon] = field(default_factory=list)
    scrub: list[Polygon] = field(default_factory=list)
    trees: list[Point] = field(default_factory=list)
    landings: list[Point] = field(default_factory=list)
    size: float = 0.0                      # square tile side, set by frame()

    def transformed(self, fn) -> "Layout":
        """Apply a shapely affine function to every geometry."""
        def each(items):
            return [fn(g) for g in items]
        return Layout(self.par, fn(self.path), each(self.tees), fn(self.green), fn(self.pin), fn(self.fairway),
                      fn(self.rough), each(self.bunkers), each(self.water), each(self.woods), each(self.scrub),
                      each(self.trees), each(self.landings), self.size)


class LayoutBuilder:
    def __init__(self, style: Style, priors: dict, rng: np.random.Generator):
        self.s = style
        self.rng = rng
        self.par_priors = priors["par"][str(style.par)]
        self.green_area = lerp_range(priors["green_area"], style.green_size)
        median_width = self.par_priors["fairway_width"][1]
        self.fairway_half = 0.5 * lerp_range([median_width * 0.75, median_width, median_width * 1.8], style.fairway_width)
        self.rough_width = 8 + 32 * style.rough_width

    # ---- route -------------------------------------------------------------------------------
    def route(self) -> tuple[LineString, list[float]]:
        """Centre line and the distances of the landing zones along it."""
        length = lerp_range(self.par_priors["length"], self.s.length)
        par, rng = self.s.par, self.rng
        if par == 3:
            return LineString([(0, 0), (0, length)]), []

        side = rng.choice([-1, 1])
        bend = math.radians(self.s.dogleg * MAX_DOGLEG_DEG) * side
        d1 = float(np.clip(length * rng.uniform(0.56, 0.66), 200, 275))
        if par == 4:
            legs = [(d1, 0.0), (length - d1, bend)]
            landings = [d1]
        else:
            d2 = float(np.clip(d1 + rng.uniform(190, 230), 0, length - 120))
            second = bend * rng.uniform(-0.6, 0.6)
            legs = [(d1, 0.0), (d2 - d1, bend), (length - d2, bend + second)]
            landings = [d1, d2]

        points, x, y, h = [(0.0, 0.0)], 0.0, 0.0, math.pi / 2
        for dist, turn in legs:
            h = math.pi / 2 + turn
            x, y = x + math.cos(h) * dist, y + math.sin(h) * dist
            points.append((x, y))
        return LineString(points), landings

    # ---- features ----------------------------------------------------------------------------
    def tees(self, path: LineString) -> list[Polygon]:
        count = int(self.rng.integers(2, 5))
        step = 9 if self.s.par == 3 else 14
        out = []
        for i in range(count):
            s = 5 + i * step + self.rng.uniform(-2, 2)
            centre = offset_point(path, s, self.rng.uniform(-3, 3))
            out.append(rect(centre, self.rng.uniform(9, 15), self.rng.uniform(7, 10), heading_at(path, s)))
        return out

    def green(self, path: LineString) -> tuple[Polygon, Point]:
        pin = Point(path.coords[-1])
        approach = heading_at(path, path.length)
        aspect = self.rng.uniform(1.0, 1.45)
        radius = area_radius(self.green_area, aspect)
        # Greens are usually deeper than wide: long axis along the line of play.
        centre = offset_point(path, path.length, 0)
        shift = self.rng.uniform(-0.35, 0.35, 2) * radius
        green = blob(Point(centre.x + shift[0], centre.y + shift[1]), radius, self.rng, aspect=aspect,
                     angle_deg=math.degrees(approach), wobble=0.14)
        return green, pin

    def fairway(self, path: LineString, landings: list[float], green: Polygon) -> Polygon:
        rng, wobble = self.rng, noise_1d(self.rng)
        if self.s.par == 3:
            if rng.random() > 0.35:
                return Polygon()
            start, end = path.length - rng.uniform(45, 70), path.length - 8
        else:
            start = float(np.clip(path.length * rng.uniform(0.22, 0.32), 85, 150))
            end = path.length - area_radius(self.green_area) * 0.6
        widest = landings[0] if landings else end

        def half_width(s):
            taper = min(1.0, (s - start) / 35 + 0.45)                     # narrow where the fairway begins
            bulge = 1.0 + 0.15 * math.exp(-((s - widest) / 60) ** 2)      # roomier at the landing zone
            approach = 0.75 if path.length - s < 40 else 1.0              # pinches toward the green
            return self.fairway_half * taper * bulge * approach * (1 + 0.18 * wobble(s))
        fw = tube(path, start, end, half_width)
        return smooth(fw, 6).simplify(0.4)

    def bunkers(self, path: LineString, landings: list[float], green: Polygon, fairway: Polygon) -> list[Polygon]:
        rng = self.rng
        count = int(round(self.par_priors["bunkers"] * 2 * self.s.bunkers * rng.uniform(0.75, 1.25)))
        fairway_share = 0.0 if self.s.par == 3 or fairway.is_empty else rng.uniform(0.3, 0.5)
        n_fairway = int(round(count * fairway_share))
        out = []
        approach = heading_at(path, path.length)
        for _ in range(count - n_fairway):
            # Greenside: anywhere except straight behind; most often front-left / front-right.
            ang = approach + math.pi + rng.choice([-1, 1]) * rng.uniform(0.35, 2.4)
            area = rng.uniform(70, 260)
            aspect = rng.uniform(1.4, 2.6)
            r = area_radius(area, aspect)
            edge = _ray_exit(green, ang)
            centre = Point(edge.x + math.cos(ang) * (r + 1.5), edge.y + math.sin(ang) * (r + 1.5))
            out.append(blob(centre, r, rng, aspect, math.degrees(ang) + 90, wobble=0.22))
        for i in range(n_fairway):
            s = (landings[i % len(landings)] if landings else path.length * 0.6) + rng.uniform(-25, 30)
            side = rng.choice([-1, 1])
            area = rng.uniform(120, 380)
            aspect = rng.uniform(1.8, 3.2)
            r = area_radius(area, aspect)
            centre = offset_point(path, s, side * (self.fairway_half * 0.9 + r * 0.6))
            out.append(blob(centre, r, rng, aspect, math.degrees(heading_at(path, s)), wobble=0.25))
        keep_out = green.buffer(1.5)
        return [smooth(b.difference(keep_out), 1.0) for b in out if not b.is_empty]

    def water(self, path: LineString, landings: list[float], green: Polygon) -> list[Polygon]:
        w, rng = self.s.water, self.rng
        if w < 0.15:
            return []
        count = 1 + int(w * 2.4 * rng.random() + (w > 0.75))
        kinds = ["front", "lateral", "creek", "pond"]
        weights = np.array([1.2 if self.s.par == 3 else 0.6, 1.0, 0.5 if self.s.par > 3 else 0.1, 0.6])
        out = []
        for _ in range(count):
            kind = rng.choice(kinds, p=weights / weights.sum())
            out.append(getattr(self, f"_water_{kind}")(path, landings, green))
        tees = unary_union(self.tee_shapes).buffer(12)
        merged = unary_union(out).difference(green.buffer(5)).difference(tees)  # overlapping ponds become one
        return [g for g in polygons(merged) if g.area > 150]

    def _water_front(self, path, landings, green):
        r_green = area_radius(self.green_area)
        s = path.length - r_green - self.rng.uniform(18, 40)
        area = self.rng.uniform(1800, 6000)
        aspect = self.rng.uniform(1.3, 2.2)
        r = area_radius(area, aspect)
        centre = offset_point(path, s - r * 0.4, self.rng.uniform(-8, 8))
        return blob(centre, r, self.rng, aspect, math.degrees(heading_at(path, s)) + 90, wobble=0.18)

    def _water_lateral(self, path, landings, green):
        s = path.length * self.rng.uniform(0.35, 0.8)
        side = self.rng.choice([-1, 1])
        area = self.rng.uniform(3000, 12000)
        aspect = self.rng.uniform(2.0, 3.5)
        r = area_radius(area, aspect)
        centre = offset_point(path, s, side * (self.fairway_half + r * 0.9 + self.rng.uniform(0, 8)))
        return blob(centre, r, self.rng, aspect, math.degrees(heading_at(path, s)), wobble=0.2)

    def _water_creek(self, path, landings, green):
        s = (landings[0] + self.rng.uniform(45, 80)) if landings else path.length * 0.6
        s = min(s, path.length - 60)
        across = heading_at(path, s) + math.pi / 2 + self.rng.uniform(-0.4, 0.4)
        c, span = path.interpolate(s), self.fairway_half + self.rough_width + 70
        pts = [(c.x + math.cos(across) * t + self.rng.normal(0, 4), c.y + math.sin(across) * t + self.rng.normal(0, 4))
               for t in np.linspace(-span, span, 9)]
        return LineString(pts).buffer(self.rng.uniform(4, 7), quad_segs=QUAD_SEGS_CREEK)

    def _water_pond(self, path, landings, green):
        s = path.length * self.rng.uniform(0.15, 0.9)
        side = self.rng.choice([-1, 1])
        r = area_radius(self.rng.uniform(900, 3500))
        centre = offset_point(path, s, side * (self.fairway_half + self.rough_width * 0.6 + r))
        return blob(centre, r, self.rng, self.rng.uniform(1, 1.6), self.rng.uniform(0, 180), wobble=0.2)

    # ---- assembly ----------------------------------------------------------------------------
    def build(self) -> Layout:
        path, landing_s = self.route()
        self.tee_shapes = self.tees(path)
        green, pin = self.green(path)
        fairway = self.fairway(path, landing_s, green)
        water = self.water(path, landing_s, green)
        wet = unary_union(water).buffer(4) if water else Polygon()
        fairway = smooth(fairway.difference(wet), 3) if not fairway.is_empty else fairway
        bunkers = [b for poly in self.bunkers(path, landing_s, green, fairway)
                   for b in polygons(poly.difference(wet)) if b.area > 25]

        corridor_half = self.fairway_half + self.rough_width
        rough = unary_union([path.buffer(corridor_half * 0.8, quad_segs=QUAD_SEGS_CREEK),
                             fairway.buffer(self.rough_width), green.buffer(self.rough_width * 0.8 + 6),
                             unary_union(self.tee_shapes).buffer(10)])
        layout = Layout(self.s.par, path, self.tee_shapes, green, pin, fairway, smooth(rough, 10),
                        bunkers, water, landings=[path.interpolate(s) for s in landing_s])
        return frame(layout, self.rng)


QUAD_SEGS_CREEK = 4


def _ray_exit(poly: Polygon, angle: float) -> Point:
    """Where a ray from the polygon's centroid at `angle` leaves the polygon."""
    c = poly.centroid
    far = Point(c.x + math.cos(angle) * 500, c.y + math.sin(angle) * 500)
    hit = LineString([c, far]).intersection(poly.exterior)
    pts = [g for g in getattr(hit, "geoms", [hit]) if isinstance(g, Point)]
    return max(pts, key=c.distance) if pts else c


def frame(layout: Layout, rng: np.random.Generator) -> Layout:
    """Rotate to a random heading, then shift into a square tile with the origin at its SW corner."""
    angle = rng.uniform(0, 360)
    rotated = layout.transformed(lambda g: affinity.rotate(g, angle, origin=(0, 0)))
    content = unary_union([rotated.rough, *rotated.water])
    minx, miny, maxx, maxy = content.buffer(FRAME_MARGIN).bounds
    size = math.ceil(max(maxx - minx, maxy - miny))
    dx = -(minx + maxx) / 2 + size / 2
    dy = -(miny + maxy) / 2 + size / 2
    framed = rotated.transformed(lambda g: affinity.translate(g, dx, dy))
    framed.size = float(size)
    return framed


def dress(layout: Layout, style: Style, rng: np.random.Generator, noise) -> Layout:
    """Woods, scrub and specimen trees: everything that needs the final tile to exist."""
    tile = box(0, 0, layout.size, layout.size)
    open_ground = (tile.difference(layout.rough.buffer(4))
                   .difference(unary_union(layout.water).buffer(6) if layout.water else Polygon())
                   .difference(layout.green.buffer(28)).difference(unary_union(layout.tees).buffer(14)))

    woods_mask = _noise_cells(layout.size, noise, style.tree_density ** 0.8, salt=0)
    layout.woods = [p for p in polygons(smooth(woods_mask, 9).simplify(1.0).intersection(open_ground)) if p.area > 300]
    scrub_ground = open_ground.difference(unary_union(layout.woods)) if layout.woods else open_ground
    scrub_mask = _noise_cells(layout.size, noise, style.scrub * 0.9, salt=1)
    layout.scrub = [p for p in polygons(smooth(scrub_mask, 9).simplify(1.0).intersection(scrub_ground)) if p.area > 200]
    layout.trees = _specimen_trees(layout, style, rng)
    return layout


def _noise_cells(size: float, noise, coverage: float, salt: int):
    """Union of grid cells where smooth noise falls under the coverage quantile."""
    if coverage <= 0.02:
        return Polygon()
    n = int(math.ceil(size / CELL))
    centres = (np.arange(n) + 0.5) * CELL
    xs, ys = np.meshgrid(centres, centres)
    values = noise(xs, ys, salt)
    cut = np.quantile(values, min(coverage, 1.0))
    cells = [box(x - CELL / 2, y - CELL / 2, x + CELL / 2, y + CELL / 2)
             for x, y, v in zip(xs.ravel(), ys.ravel(), values.ravel()) if v <= cut]
    return unary_union(cells)


def _specimen_trees(layout: Layout, style: Style, rng: np.random.Generator) -> list[Point]:
    """Individual trees dotted through the rough, lining the hole."""
    count = int(style.tree_density * layout.path.length / 14 * rng.uniform(0.7, 1.3))
    blocked = unary_union([layout.fairway.buffer(6), layout.green.buffer(18), unary_union(layout.tees).buffer(12),
                           *[b.buffer(4) for b in layout.bunkers], *[w.buffer(5) for w in layout.water]])
    band = layout.rough.difference(blocked)
    if band.is_empty:
        return []
    minx, miny, maxx, maxy = band.bounds
    trees: list[Point] = []
    for _ in range(count * 20):
        if len(trees) >= count:
            break
        p = Point(rng.uniform(minx, maxx), rng.uniform(miny, maxy))
        if band.contains(p) and all(p.distance(t) > 9 for t in trees):
            trees.append(p)
    return trees
