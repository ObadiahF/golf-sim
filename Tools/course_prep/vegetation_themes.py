"""How densely each theme is planted, and with what. One table for every producer of hole packages.

Densities are per hectare of matching ground; Unity used to hold these (DefaultThemes.cs) and now only
chooses which 3D models represent each kind.
"""
from __future__ import annotations

from dataclasses import dataclass, field


@dataclass(frozen=True)
class Rule:
    name: str
    kinds: dict[str, float]        # object kind -> weight
    surfaces: tuple[str, ...]
    per_hectare: float
    clumping: float = 0.5          # 0 = even spread, 1 = tight groves with wide gaps
    slope: tuple[float, float] = (0.0, 35.0)  # allowed terrain slope, degrees
    trees: bool = False            # scaled by the hole's tree density


@dataclass(frozen=True)
class Theme:
    name: str
    trees: dict[str, float]        # tree species mix for woods, native and specimen trees
    woods: float = 250
    native_trees: float = 8
    shrubs: float = 90
    boulders: float = 60
    rocks: float = 25
    rough_trees: float = 0         # scattered single trees in the rough
    extra: tuple[Rule, ...] = field(default_factory=tuple)

    def rules(self) -> list[Rule]:
        native = ("native", "scrub")
        return [
            Rule("Woods", self.trees, ("woods",), self.woods, 0.3, trees=True),
            Rule("Native trees", self.trees, native, self.native_trees, 0.7, trees=True),
            Rule("Rough trees", self.trees, ("rough",), self.rough_trees, 0.8, trees=True),
            Rule("Shrubs", {"shrub": 1}, native, self.shrubs, 0.6, (0, 40)),
            Rule("Slope boulders", {"boulder": 1}, native, self.boulders, 0.5, (25, 90)),
            Rule("Loose rocks", {"rock": 1}, native, self.rocks, 0.4),
            *self.extra,
        ]


THEMES: dict[str, Theme] = {t.name: t for t in [
    Theme("coastal", {"conifer": 0.75, "deciduous": 0.25}),
    Theme("parkland", {"deciduous": 0.75, "conifer": 0.25}, native_trees=14, shrubs=50, boulders=15, rocks=8,
          rough_trees=1.5),
    Theme("forest", {"conifer": 0.8, "deciduous": 0.2}, woods=340, native_trees=30, shrubs=70, boulders=25, rocks=15,
          rough_trees=1.0),
    Theme("lakes", {"deciduous": 0.7, "conifer": 0.3}, woods=220, native_trees=10, shrubs=40, boulders=10, rocks=10,
          rough_trees=1.0),
    Theme("links", {"conifer": 0.5, "deciduous": 0.5}, woods=120, native_trees=1, shrubs=40, boulders=5, rocks=5),
    Theme("desert", {"cactus": 0.6, "palm": 0.4}, woods=60, native_trees=5, shrubs=120, boulders=90, rocks=60),
    Theme("mountain", {"conifer": 0.9, "deciduous": 0.1}, woods=300, native_trees=20, shrubs=60, boulders=120,
          rocks=50),
]}
DEFAULT_THEME = "coastal"

# Height range (m) and collision radius as a fraction of height, per kind.
SHAPES: dict[str, tuple[tuple[float, float], float]] = {
    "conifer": ((12.0, 24.0), 0.022),
    "deciduous": ((9.0, 17.0), 0.03),
    "palm": ((8.0, 14.0), 0.02),
    "cactus": ((2.0, 6.0), 0.08),
    "shrub": ((0.8, 2.2), 0.4),
    "boulder": ((1.2, 3.2), 0.55),
    "rock": ((0.25, 0.9), 0.6),
}
# Crown (canopy) radius as a fraction of height, for the kinds a ball can hit in flight (shot-line checks).
CROWNS: dict[str, float] = {"conifer": 0.40, "deciduous": 0.45, "palm": 0.30, "cactus": 0.15}


def theme(name: str | None) -> Theme:
    return THEMES.get(name or DEFAULT_THEME, THEMES[DEFAULT_THEME])


def tree_density_scale(knob: float) -> float:
    """Generator knob tree_density (0..1) -> multiplier on every tree rule's density (0.6x .. 1.4x).
    The knob also grows the woods areas themselves (course_gen layout), so it compounds."""
    return 0.6 + 0.8 * knob
