#!/usr/bin/env python3
"""Course prep CLI: fetch OSM data, list holes, and build Unity hole packages.

  python prep_hole.py fetch --bbox 32.888,-117.258,32.912,-117.238 --out ../../CourseSources/torrey_pines/osm.json
  python prep_hole.py list  --osm ../../CourseSources/torrey_pines/osm.json
  python prep_hole.py build --osm ../../CourseSources/torrey_pines/osm.json --hole 3 --course South
  python prep_hole.py migrate <package folder> ...   (version 1 -> 2)
  python prep_hole.py validate <package folder> ...
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

from osm_io import load_features
from hole_package import summary, validate
from migrate import migrate_v1
from package import DEFAULT_THEME, build_package, list_holes, package_id, select_hole
from preview import render_preview
from sources import fetch_osm, find_dem_urls

PROJECT_ROOT = Path(__file__).resolve().parents[2]
DEFAULT_OUT_ROOT = PROJECT_ROOT / "Assets" / "CourseData"


def cmd_fetch(args):
    south, west, north, east = (float(v) for v in args.bbox.split(","))
    data = fetch_osm(south, west, north, east)
    out = Path(args.out)
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps(data))
    print(f"Wrote {len(data['elements'])} OSM elements to {out}")


def cmd_list(args):
    for hole in list_holes(load_features(args.osm)):
        print(hole.describe())


def cmd_build(args):
    features = load_features(args.osm)
    hole = select_hole(features, args.hole, args.course)
    print(f"Building {hole.describe()}")

    dem = args.dem
    if not dem:
        lon_lat = hole.line.geom.buffer(0.01).bounds  # ~1 km pad in degrees; tiles are 10 km
        dem = find_dem_urls(*lon_lat)
        print(f"Auto DEM: {len(dem)} USGS 1m tile(s), streamed remotely")
        for url in dem:
            print(f"  {url}")

    out_dir = Path(args.out or DEFAULT_OUT_ROOT) / package_id(hole)
    pkg = build_package(features, hole, dem, out_dir, args.margin, args.max_spacing, args.theme)

    print(f"Wrote {out_dir}\n{summary(pkg)}\n  preview: {render_preview(out_dir)}")


def cmd_migrate(args):
    for folder in args.packages:
        pkg = migrate_v1(Path(folder), args.theme)
        print(f"Migrated {folder} to version 2\n{summary(pkg)}\n  preview: {render_preview(Path(folder))}")


def cmd_validate(args):
    bad = 0
    for folder in args.packages:
        issues = validate(Path(folder))
        bad += bool(issues)
        print(f"{folder}: " + ("ok" if not issues else "\n  " + "\n  ".join(issues)))
    raise SystemExit(1 if bad else 0)


def cmd_preview(args):
    print(render_preview(Path(args.package)))


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    f = sub.add_parser("fetch", help="Download OSM golf features for a bbox via Overpass")
    f.add_argument("--bbox", required=True, help="south,west,north,east in degrees")
    f.add_argument("--out", required=True)
    f.set_defaults(func=cmd_fetch)

    l = sub.add_parser("list", help="List holes found in an OSM file")
    l.add_argument("--osm", required=True, help="GeoJSON (overpass-turbo export) or Overpass JSON")
    l.set_defaults(func=cmd_list)

    b = sub.add_parser("build", help="Build a Unity hole package")
    b.add_argument("--osm", required=True, help="GeoJSON (overpass-turbo export) or Overpass JSON")
    b.add_argument("--hole", required=True, help="hole number (OSM ref)")
    b.add_argument("--course", help="substring of the course name when several share hole numbers")
    b.add_argument("--dem", nargs="*", help="DEM GeoTIFF paths or URLs; omit to auto-find USGS 1m tiles")
    b.add_argument("--out", help=f"parent folder; the package folder is named by its id (default {DEFAULT_OUT_ROOT})")
    b.add_argument("--theme", default=DEFAULT_THEME, help="look and vegetation rules (see vegetation_themes.py)")
    b.add_argument("--margin", type=float, default=50.0, help="meters of terrain around the hole line")
    b.add_argument("--max-spacing", type=float, default=0.5, help="max meters per heightmap sample")
    b.set_defaults(func=cmd_build)

    v = sub.add_parser("preview", help="Render hillshade + outlines PNG for a built package")
    v.add_argument("package", help="hole package folder containing hole.json")
    v.set_defaults(func=cmd_preview)

    m = sub.add_parser("migrate", help="Upgrade version 1 packages in place to version 2 (plants objects.bin)")
    m.add_argument("packages", nargs="+", help="package folders")
    m.add_argument("--theme", help="theme for packages that don't name one (default: theirs, else coastal)")
    m.set_defaults(func=cmd_migrate)

    c = sub.add_parser("validate", help="Check packages against the hole contract (Docs/hole-format)")
    c.add_argument("packages", nargs="+", help="package folders")
    c.set_defaults(func=cmd_validate)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
