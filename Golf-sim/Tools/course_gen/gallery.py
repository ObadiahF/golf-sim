#!/usr/bin/env python3
"""Generate a few holes per preset and tile their previews into one PNG for eyeballing.

  python gallery.py --out /tmp/gallery [--seeds 3]
"""
from __future__ import annotations

import argparse
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

import _prep  # noqa: F401
from generate import write_package
from style import PRESETS

TILE = 300


def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--out", required=True)
    p.add_argument("--seeds", type=int, default=3)
    p.add_argument("--spacing", type=float, default=1.5, help="coarser = faster previews")
    args = p.parse_args()
    out = Path(args.out)

    rows = []
    for name, preset in PRESETS.items():
        row = []
        for seed in range(args.seeds):
            style = preset.sample(np.random.default_rng(1000 + seed))
            pkg = write_package(style, 1000 + seed, out, args.spacing)
            row.append((pkg / "preview.png", f"{name} par {style.par}"))
        rows.append(row)

    sheet = Image.new("RGB", (TILE * args.seeds, (TILE + 16) * len(rows)), "white")
    draw = ImageDraw.Draw(sheet)
    for r, row in enumerate(rows):
        for c, (path, label) in enumerate(row):
            im = Image.open(path)
            im.thumbnail((TILE, TILE))
            sheet.paste(im, (c * TILE, r * (TILE + 16)))
            draw.text((c * TILE + 4, r * (TILE + 16) + TILE + 2), label, fill="black")
    sheet.save(out / "gallery.png")
    print(out / "gallery.png")


if __name__ == "__main__":
    main()
