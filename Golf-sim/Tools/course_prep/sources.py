"""Fetch raw inputs: OSM golf features (Overpass) and USGS 3DEP 1m DEM tile URLs (TNM API)."""
from __future__ import annotations

import json
import urllib.parse
import urllib.request

USER_AGENT = "golf-sim-course-prep/0.1"
OVERPASS_MIRRORS = [
    "https://overpass-api.de/api/interpreter",
    "https://overpass.kumi.systems/api/interpreter",
    "https://overpass.private.coffee/api/interpreter",
]
TNM_PRODUCTS = "https://tnmaccess.nationalmap.gov/api/v1/products"
DEM_1M_DATASET = "Digital Elevation Model (DEM) 1 meter"


def _get(url: str, data: bytes | None = None, timeout: int = 120) -> bytes:
    req = urllib.request.Request(url, data=data, headers={"User-Agent": USER_AGENT, "Accept": "application/json"})
    with urllib.request.urlopen(req, timeout=timeout) as resp:
        return resp.read()


def overpass_query(south: float, west: float, north: float, east: float) -> str:
    bbox = f"({south},{west},{north},{east})"
    return (
        "[out:json][timeout:90];("
        f'nwr["golf"]{bbox};'
        f'nwr["leisure"="golf_course"]{bbox};'
        f'nwr["natural"~"^(water|wood|sand|scrub|heath|tree|tree_row)$"]{bbox};'
        f'nwr["landuse"="forest"]{bbox};'
        ");out geom;"
    )


def fetch_osm(south: float, west: float, north: float, east: float) -> dict:
    """Return raw Overpass JSON, trying mirrors until one answers with JSON."""
    body = urllib.parse.urlencode({"data": overpass_query(south, west, north, east)}).encode()
    errors = []
    for url in OVERPASS_MIRRORS:
        try:
            raw = _get(url, body)
            return json.loads(raw)
        except Exception as exc:  # mirrors fail often (busy, HTML error page); try the next
            errors.append(f"{url}: {exc}")
    raise RuntimeError("All Overpass mirrors failed:\n  " + "\n  ".join(errors))


def find_dem_urls(west: float, south: float, east: float, north: float) -> list[str]:
    """1m DEM tile URLs covering the bbox, keeping only the newest project per tile."""
    query = urllib.parse.urlencode({
        "datasets": DEM_1M_DATASET,
        "bbox": f"{west},{south},{east},{north}",
        "outputFormat": "JSON",
        "max": 100,
    })
    items = json.loads(_get(f"{TNM_PRODUCTS}?{query}"))["items"]
    newest: dict[str, dict] = {}
    for item in items:
        # Titles look like "USGS 1 Meter 11 x47y364 <project>"; the x/y token identifies the tile.
        tile = next((tok for tok in item["title"].split() if tok.startswith("x") and "y" in tok), item["title"])
        if tile not in newest or item.get("publicationDate", "") > newest[tile].get("publicationDate", ""):
            newest[tile] = item
    return [i["downloadURL"] for i in newest.values()]
