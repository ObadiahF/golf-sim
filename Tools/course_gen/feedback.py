"""Quick-feedback tags ("more trees", "too long" ...) that ride along with a 👍 / 👎.

Each tag names knobs and the direction the user wanted them to move. A tag on a rated hole becomes a
*paired comparison* for training: "this hole with the knob nudged that way would be better than this
hole". In the Bradley-Terry form that is one extra logistic observation on the feature difference,
P(shifted > original) = sigmoid((f(shifted) - f(original)) . w), labelled 1. The bias and every
untouched knob cancel, so a tag only teaches the knob it names (linear and curvature terms), for the
hole's preset and globally, exactly like a rating does. Tags therefore work on 👍 and 👎 alike.
"""
from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from style import PARAM_NAMES, Style


@dataclass(frozen=True)
class Tag:
    label: str
    knobs: dict[str, int]  # knob -> +1 (wanted more) / -1 (wanted less)


TAGS: dict[str, Tag] = {
    "more_trees":    Tag("More trees", {"tree_density": +1}),
    "fewer_trees":   Tag("Fewer trees", {"tree_density": -1}),
    "more_water":    Tag("More water", {"water": +1}),
    "less_water":    Tag("Less water", {"water": -1}),
    "more_bunkers":  Tag("More bunkers", {"bunkers": +1}),
    "fewer_bunkers": Tag("Fewer bunkers", {"bunkers": -1}),
    "too_long":      Tag("Too long", {"length": -1}),
    "too_short":     Tag("Too short", {"length": +1}),
    "too_flat":      Tag("Too flat", {"relief": +1}),
    "too_hilly":     Tag("Too hilly", {"relief": -1}),
    "too_narrow":    Tag("Too narrow", {"fairway_width": +1}),
    "too_wide":      Tag("Too wide", {"fairway_width": -1}),
    "too_straight":  Tag("Too straight", {"dogleg": +1}),
    "too_bendy":     Tag("Too bendy", {"dogleg": -1}),
    "more_scrub":    Tag("More native scrub", {"scrub": +1}),
    "less_scrub":    Tag("Less native scrub", {"scrub": -1}),
    # No single knob means "boring"; read it as "wanted more character": shape and terrain.
    "boring":        Tag("Boring", {"dogleg": +1, "relief": +1}),
}

NUDGE = 0.2          # how far (normalised units) the imagined better hole sits from the rated one
TAG_WEIGHT = 0.5     # a tag counts as half a rating: it is a hint, the thumbs are the verdict


def validate_tags(tags) -> list[str]:
    tags = list(dict.fromkeys(tags or []))  # de-duplicate, keep order
    unknown = [t for t in tags if t not in TAGS]
    if unknown:
        raise ValueError(f"Unknown feedback tag(s) {', '.join(unknown)}. Available: {', '.join(TAGS)}")
    return tags


def nudged(style: Style, tag: str, step: float = NUDGE) -> Style:
    values = dict(style.values)
    for knob, direction in TAGS[tag].knobs.items():
        values[knob] = float(np.clip(values[knob] + direction * step, 0.0, 1.0))
    return Style(style.preset, style.theme, style.par, values)


def comparisons(style: Style, tags) -> list[tuple[Style, Style]]:
    """(preferred, rated) style pairs implied by the tags; tags already at a knob's limit add nothing."""
    pairs = []
    for tag in tags or []:
        if tag in TAGS:  # tags removed from the table later are ignored, not fatal
            better = nudged(style, tag)
            if any(better.values[k] != style.values[k] for k in PARAM_NAMES):
                pairs.append((better, style))
    return pairs


def catalog() -> list[dict]:
    """Tag table for UIs."""
    return [{"id": tid, "label": t.label, "knobs": t.knobs} for tid, t in TAGS.items()]
