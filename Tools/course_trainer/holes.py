"""Generated hole packages on disk: lookup, listing and the per-hole summary the web UI shows."""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

import _paths  # noqa: F401
from generate import GEN_FILE, read_generator_info
from preference import load_ratings

PACKAGE_FILES = {"hole.json", "heightmap.raw", "gen.json", "preview.png"}
_ID = re.compile(r"^[A-Za-z0-9_.-]+$")


class HoleNotFound(LookupError):
    pass


def path_length(points: list[float]) -> float:
    """Length of a flat [x0, z0, x1, z1, ...] polyline in meters."""
    xy = list(zip(points[0::2], points[1::2]))
    return sum(math.dist(a, b) for a, b in zip(xy, xy[1:]))


class HoleStore:
    def __init__(self, root: Path):
        self.root = Path(root)

    def folder(self, hole_id: str) -> Path:
        folder = self.root / hole_id
        if not _ID.match(hole_id) or hole_id.startswith(".") or not (folder / GEN_FILE).is_file():
            raise HoleNotFound(hole_id)
        return folder

    def file(self, hole_id: str, name: str) -> Path:
        path = self.folder(hole_id) / name
        if name not in PACKAGE_FILES or not path.is_file():
            raise HoleNotFound(f"{hole_id}/{name}")
        return path

    def describe(self, hole_id: str, ratings: dict[str, dict] | None = None) -> dict:
        folder = self.folder(hole_id)
        info = read_generator_info(folder)
        hole = json.loads((folder / "hole.json").read_text())
        vote = (ratings if ratings is not None else _ratings_by_id()).get(hole_id)
        return {
            "id": hole_id, "preset": info["preset"], "theme": info["theme"], "par": info["par"],
            "seed": info["seed"], "params": info["params"], "modelScore": info.get("modelScore"),
            "lengthMeters": round(path_length(hole["holePath"]["points"]), 1),
            "created": folder.stat().st_mtime,
            "rating": None if vote is None else ("up" if vote["rating"] > 0 else "down"),
        }

    def recent(self, limit: int = 20) -> list[dict]:
        if not self.root.is_dir():
            return []
        folders = [d for d in self.root.iterdir() if (d / GEN_FILE).is_file()]
        folders.sort(key=lambda d: d.stat().st_mtime, reverse=True)
        ratings = _ratings_by_id()
        return [self.describe(d.name, ratings) for d in folders[:limit]]


def _ratings_by_id() -> dict[str, dict]:
    return {r["id"]: r for r in load_ratings()}
