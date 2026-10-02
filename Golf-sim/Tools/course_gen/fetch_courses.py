#!/usr/bin/env python3
"""Download OSM golf data for a few varied reference courses (cached; polite to Overpass).

  python fetch_courses.py          # fetch missing courses, then refit data/priors.json
"""
from __future__ import annotations

import json
import time

import _prep  # noqa: F401
from priors import COURSE_SOURCES, PRIORS_PATH, cached_courses, fit_priors
from sources import fetch_osm

PAUSE_SECONDS = 15  # between Overpass requests

# (folder, style, south, west, north, east)
COURSES = [
    ("augusta_national", "parkland/forest", 33.493, -82.032, 33.508, -82.014),
    ("pebble_beach", "coastal", 36.560, -121.955, 36.575, -121.933),
    ("tpc_sawgrass", "water", 30.188, -81.405, 30.205, -81.385),
    ("bethpage", "forest", 40.735, -73.470, 40.758, -73.438),
    ("whistling_straits", "links", 43.840, -87.745, 43.862, -87.718),
    ("tpc_scottsdale", "desert", 33.628, -111.925, 33.652, -111.898),
]


def main():
    fetched = 0
    for name, style, s, w, n, e in COURSES:
        out = COURSE_SOURCES / name / "osm.json"
        if out.exists():
            continue
        if fetched:
            time.sleep(PAUSE_SECONDS)
        print(f"Fetching {name} ({style}) ...", flush=True)
        try:
            data = fetch_osm(s, w, n, e)
        except RuntimeError as exc:
            print(f"  skipped: {exc}")
            continue
        out.parent.mkdir(parents=True, exist_ok=True)
        out.write_text(json.dumps(data))
        holes = sum(1 for el in data["elements"] if el.get("tags", {}).get("golf") == "hole")
        print(f"  {len(data['elements'])} elements, {holes} hole lines")
        fetched += 1

    priors = fit_priors(cached_courses())
    PRIORS_PATH.parent.mkdir(parents=True, exist_ok=True)
    PRIORS_PATH.write_text(json.dumps(priors, indent=1))
    print(f"Fitted priors from {priors['holes']} holes -> {PRIORS_PATH}")


if __name__ == "__main__":
    main()
