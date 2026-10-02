#!/usr/bin/env python3
"""List hole packages whose tee shot hits the ground (TH-5), using the generator's own launch check. Read-only.

  python scan_launch.py <holes_root> [--json]

Rated packages are immutable, so a hole generated before GENERATOR_VERSION 4 cannot be re-sculpted. This
lists the ones a low tee shot cannot clear, so the trainer can mark them unplayable / keep them out of the
top-holes ranking. Prints one line per failing hole, then a summary (or one JSON object with --json).
"""
from __future__ import annotations

import argparse
import json
from pathlib import Path

import numpy as np
from shapely.geometry import LineString

import _prep  # noqa: F401
from generate import GEN_FILE
from hole_package import read_heights, read_hole
from validate import launch_issues, launch_overshoot


def launch_profile(package_dir: Path) -> dict[str, tuple[np.ndarray, np.ndarray]]:
    """validate.launch_overshoot for one package on disk: {line name: (s, over)}."""
    hole = read_hole(package_dir)
    path = LineString(np.asarray(hole["holePath"]["points"], dtype=float).reshape(-1, 2))
    return launch_overshoot(path, read_heights(package_dir, hole), hole["sizeMeters"])


def scan_package(package_dir: Path) -> list[str]:
    """launch_problems for one package on disk (empty = the tee shot clears the ground)."""
    return launch_issues(launch_profile(package_dir))


def generator_version(package_dir: Path) -> int | None:
    gen = package_dir / GEN_FILE
    return json.loads(gen.read_text()).get("generatorVersion") if gen.exists() else None


def scan(root: Path) -> dict:
    failing, checked, unreadable = {}, 0, []
    for package_dir in sorted(d for d in root.iterdir() if (d / "hole.json").exists()):
        try:
            issues = scan_package(package_dir)
        except (OSError, KeyError, ValueError) as e:
            unreadable.append(f"{package_dir.name}: {e}")
            continue
        checked += 1
        if issues:
            failing[package_dir.name] = {"generatorVersion": generator_version(package_dir), "issues": issues}
    return {"checked": checked, "failing": failing, "unreadable": unreadable}


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("root", type=Path, help="folder of hole packages (e.g. the trainer's /data/holes)")
    p.add_argument("--json", action="store_true")
    args = p.parse_args()
    result = scan(args.root)
    if args.json:
        print(json.dumps(result, indent=1))
        return
    for hid, info in result["failing"].items():
        print(f"{hid} (v{info['generatorVersion']}): {info['issues'][0]}")
    for line in result["unreadable"]:
        print(f"unreadable {line}")
    print(f"{len(result['failing'])} of {result['checked']} holes fail the tee-shot check")


if __name__ == "__main__":
    main()
