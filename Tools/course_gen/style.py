"""Style vectors: the knobs that describe a generated hole, and named presets over them.

Every parameter is normalised to 0..1 so presets, sliders and the preference model share one space.
Turning a parameter into meters/degrees happens in layout.py / terrain.py.
"""
from __future__ import annotations

from dataclasses import dataclass, field

import numpy as np

PARAMS: dict[str, str] = {
    "length":        "hole length within the par's typical range (short .. long)",
    "dogleg":        "how hard the hole bends (straight .. 50 degrees)",
    "relief":        "terrain relief (flat .. dramatic, ~1 .. 45 m)",
    "hilliness":     "terrain feature size (broad swells .. tight bumps)",
    "slope":         "tee to green elevation change (downhill .. uphill)",
    "tree_density":  "woods and specimen trees (open .. dense forest)",
    "water":         "ponds, lakes and creeks (dry .. lots of water)",
    "bunkers":       "bunker count (few .. many)",
    "fairway_width": "fairway width (~22 .. 60 m)",
    "green_size":    "green area within the typical range",
    "rough_width":   "rough band beside the fairway (~8 .. 40 m)",
    "scrub":         "native scrub / waste areas outside the rough",
}
PARAM_NAMES = list(PARAMS)
PARS = (3, 4, 5)


@dataclass
class Preset:
    name: str
    label: str
    theme: str                      # Unity CourseTheme name used to dress the hole
    means: dict[str, float]
    par_weights: tuple[float, float, float] = (0.2, 0.6, 0.2)
    spread: float = 0.12            # std dev in normalised units
    spreads: dict[str, float] = field(default_factory=dict)  # per-parameter override

    def sample(self, rng: np.random.Generator) -> "Style":
        values = {}
        for name in PARAM_NAMES:
            mean = self.means.get(name, 0.5)
            values[name] = float(np.clip(rng.normal(mean, self.spreads.get(name, self.spread)), 0.0, 1.0))
        par = int(rng.choice(PARS, p=np.asarray(self.par_weights) / sum(self.par_weights)))
        return Style(self.name, self.theme, par, values)


@dataclass
class Style:
    preset: str
    theme: str
    par: int
    values: dict[str, float]

    def __getattr__(self, name: str) -> float:
        try:
            return self.__dict__["values"][name]
        except KeyError:
            raise AttributeError(name) from None

    def to_json(self) -> dict:
        return {"preset": self.preset, "theme": self.theme, "par": self.par,
                "params": {k: round(v, 4) for k, v in self.values.items()}}

    @staticmethod
    def from_json(d: dict) -> "Style":
        values = {name: float(d["params"].get(name, 0.5)) for name in PARAM_NAMES}
        return Style(d["preset"], d.get("theme", d["preset"]), int(d["par"]), values)


_BASE = dict(length=0.5, dogleg=0.35, relief=0.3, hilliness=0.4, slope=0.5, tree_density=0.5, water=0.2,
             bunkers=0.5, fairway_width=0.5, green_size=0.5, rough_width=0.5, scrub=0.15)


def _preset(name, label, theme, **overrides) -> Preset:
    par_weights = overrides.pop("par_weights", (0.2, 0.6, 0.2))
    spreads = overrides.pop("spreads", {})
    return Preset(name, label, theme, {**_BASE, **overrides}, par_weights, spreads=spreads)


PRESETS: dict[str, Preset] = {p.name: p for p in [
    _preset("parkland", "Parkland", "parkland", tree_density=0.55),
    _preset("forest", "Forest", "forest", tree_density=0.92, fairway_width=0.35, rough_width=0.3,
            dogleg=0.55, relief=0.45, water=0.1, bunkers=0.35),
    _preset("lakes", "Water / Lakes", "lakes", water=0.88, tree_density=0.35, relief=0.2, bunkers=0.45,
            par_weights=(0.3, 0.5, 0.2)),
    _preset("links", "Links", "links", tree_density=0.04, water=0.08, bunkers=0.85, fairway_width=0.7,
            relief=0.35, hilliness=0.85, rough_width=0.7, scrub=0.7, dogleg=0.3,
            spreads={"tree_density": 0.04}),
    _preset("desert", "Desert", "desert", tree_density=0.1, water=0.15, bunkers=0.4, fairway_width=0.4,
            relief=0.4, rough_width=0.15, scrub=0.92, spreads={"tree_density": 0.05}),
    _preset("mountain", "Mountain", "mountain", relief=0.95, hilliness=0.35, tree_density=0.65, water=0.2,
            bunkers=0.35, dogleg=0.5, spreads={"slope": 0.3}),
]}


def get_preset(name: str) -> Preset:
    if name in PRESETS:
        return PRESETS[name]
    raise SystemExit(f"Unknown preset '{name}'. Available: {', '.join(PRESETS)}")


def apply_overrides(style: Style, overrides: dict[str, float], par: int | None = None) -> Style:
    """Pin parameters chosen by the user (sliders) on top of a sampled style."""
    for name, value in overrides.items():
        if name not in PARAMS:
            raise SystemExit(f"Unknown parameter '{name}'. Available: {', '.join(PARAM_NAMES)}")
        style.values[name] = float(np.clip(value, 0.0, 1.0))
    if par is not None:
        style.par = par
    return style
