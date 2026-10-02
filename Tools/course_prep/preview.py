"""Render a hole package to PNG (hillshade + surface outlines) to eyeball DEM/OSM alignment."""
from __future__ import annotations

import json
from pathlib import Path

import numpy as np
from PIL import Image, ImageDraw

OUTLINE_COLORS = {
    "rough": (90, 140, 60), "fairway": (120, 220, 90), "green": (40, 255, 120), "tee": (255, 255, 255),
    "bunker": (250, 220, 140), "water": (60, 140, 255), "woods": (20, 90, 30), "scrub": (190, 150, 90),
}
# Translucent fills so coverage (woods density, water) reads at a glance; outlines stay for everything.
FILL_SURFACES = {"woods": 110, "water": 150, "scrub": 90, "bunker": 160, "green": 120, "fairway": 70}


def hillshade(h: np.ndarray, spacing: float, azimuth=315.0, altitude=45.0) -> np.ndarray:
    dy, dx = np.gradient(h, spacing)
    slope = np.arctan(np.hypot(dx, dy))
    aspect = np.arctan2(-dx, dy)
    az, alt = np.radians(azimuth), np.radians(altitude)
    shade = np.sin(alt) * np.cos(slope) + np.cos(alt) * np.sin(slope) * np.cos(az - aspect)
    return np.clip(shade, 0, 1)


def render_preview(package_dir: Path, out_path: Path | None = None) -> Path:
    pkg = json.loads((package_dir / "hole.json").read_text())
    n = pkg["heightmapResolution"]
    raw = np.fromfile(package_dir / pkg["heightmapFile"], dtype="<u2").reshape(n, n)
    span = pkg["maxElevation"] - pkg["minElevation"]
    heights = raw.astype(np.float32) / 65535 * span

    spacing = pkg["sizeMeters"] / (n - 1)
    gray = (hillshade(heights, spacing) * 255).astype(np.uint8)
    img = Image.fromarray(np.flipud(gray)).convert("RGB")  # flip: row 0 is south, images are top-down
    draw = ImageDraw.Draw(img)
    scale = (n - 1) / pkg["sizeMeters"]

    def px(x, z):
        return x * scale, (n - 1) - z * scale

    overlay = Image.new("RGBA", img.size, (0, 0, 0, 0))
    fill = ImageDraw.Draw(overlay)
    for area in pkg["areas"]:
        alpha = FILL_SURFACES.get(area["surface"])
        if alpha:
            rings = [[px(r["points"][i], r["points"][i + 1]) for i in range(0, len(r["points"]), 2)] for r in area["rings"]]
            fill.polygon(rings[0], fill=(*OUTLINE_COLORS[area["surface"]], alpha))
            for hole in rings[1:]:
                fill.polygon(hole, fill=(0, 0, 0, 0))
    img = Image.alpha_composite(img.convert("RGBA"), overlay).convert("RGB")
    draw = ImageDraw.Draw(img)

    for area in pkg["areas"]:
        color = OUTLINE_COLORS.get(area["surface"], (255, 0, 255))
        for ring in area["rings"]:
            p = ring["points"]
            draw.line([px(p[i], p[i + 1]) for i in range(0, len(p), 2)], fill=color, width=2)

    for t in pkg.get("trees", []):
        x, y = px(t["x"], t["y"])
        draw.ellipse([x - 3, y - 3, x + 3, y + 3], fill=(20, 110, 30))

    path = pkg["holePath"]["points"]
    draw.line([px(path[i], path[i + 1]) for i in range(0, len(path), 2)], fill=(255, 60, 60), width=2)
    for key, color in (("tee", (255, 255, 0)), ("pin", (255, 0, 0))):
        x, y = px(pkg[key]["x"], pkg[key]["y"])
        draw.ellipse([x - 6, y - 6, x + 6, y + 6], outline=color, width=3)

    out_path = out_path or package_dir / "preview.png"
    img.save(out_path)
    return out_path
