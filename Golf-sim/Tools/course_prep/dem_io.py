"""Resample one or more DEM GeoTIFFs (local paths or COG URLs) onto a HoleFrame grid."""
from __future__ import annotations

import numpy as np
import rasterio
from rasterio.fill import fillnodata
from rasterio.merge import merge
from rasterio.transform import from_origin
from rasterio.warp import Resampling, reproject, transform_bounds

from geo import HoleFrame

MAX_MISSING_FRACTION = 0.02


def sample_dem(sources: list[str], frame: HoleFrame) -> np.ndarray:
    """Return heights in meters, shape (res, res), row 0 = south edge (Unity heightmap order)."""
    n, d = frame.resolution, frame.spacing
    # Pixel centers land exactly on grid points: shift the raster corner by half a cell.
    dst_transform = from_origin(frame.origin_x - d / 2, frame.origin_y + frame.size + d / 2, d, d)
    heights = np.full((n, n), np.nan, dtype=np.float32)

    # Mosaic first, then resample once: resampling tiles separately leaves seams at tile edges.
    for mosaic, src_transform, src_crs in _mosaics(sources, frame):
        tile = np.full((n, n), np.nan, dtype=np.float32)
        reproject(
            source=mosaic,
            destination=tile,
            src_transform=src_transform,
            src_crs=src_crs,
            src_nodata=np.nan,
            dst_transform=dst_transform,
            dst_crs=f"EPSG:{frame.epsg}",
            dst_nodata=np.nan,
            resampling=Resampling.bilinear,
        )
        fill = np.isnan(heights) & ~np.isnan(tile)
        heights[fill] = tile[fill]

    missing = np.isnan(heights)
    if missing.mean() > MAX_MISSING_FRACTION:
        raise RuntimeError(
            f"DEM covers only {100 * (1 - missing.mean()):.1f}% of the hole. "
            "Add the neighbouring tiles with more --dem arguments."
        )
    if missing.any():
        heights = fillnodata(np.nan_to_num(heights), mask=(~missing).astype(np.uint8), max_search_distance=50)

    return np.flipud(heights)


def _mosaics(sources: list[str], frame: HoleFrame):
    """Yield (array, transform, crs) per source CRS, each merged over just the hole's footprint."""
    by_crs: dict[str, list] = {}
    for path in sources:
        src = rasterio.open(path)
        by_crs.setdefault(src.crs.to_string(), []).append(src)

    for crs, group in by_crs.items():
        pad = 4 * frame.spacing + 2.0
        x0, y0, x1, y1 = transform_bounds(f"EPSG:{frame.epsg}", crs, *frame.utm_box.bounds, densify_pts=21)
        if group[0].crs.is_geographic:
            pad /= 111_000.0  # meters -> degrees, close enough for padding
        try:
            data, transform = merge(group, bounds=(x0 - pad, y0 - pad, x1 + pad, y1 + pad),
                                    nodata=np.nan, dtype="float32")
            yield data[0], transform, group[0].crs
        finally:
            for src in group:
                src.close()


def write_raw16(heights: np.ndarray, path) -> tuple[float, float]:
    """Write a little-endian 16-bit RAW normalised to [min, max]. Returns (min, max) elevation."""
    lo, hi = float(heights.min()), float(heights.max())
    span = max(hi - lo, 1e-3)
    norm = np.clip((heights - lo) / span, 0, 1)
    (norm * 65535 + 0.5).astype("<u2").tofile(path)
    return lo, hi
