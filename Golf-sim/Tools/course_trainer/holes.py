"""Generated hole packages on disk: safe lookup and the per-hole summary the web UI shows."""
from __future__ import annotations

import json
import math
import re
from pathlib import Path

import _paths  # noqa: F401
from generate import GEN_FILE, read_generator_info
from inputs import MAX_ID

PACKAGE_FILES = {"hole.json", "heightmap.raw", "objects.bin", "gen.json", "preview.png"}
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
        if len(hole_id) > MAX_ID or not _ID.match(hole_id) or hole_id.startswith("."):
            raise HoleNotFound(hole_id[:MAX_ID])
        folder = self.root / hole_id
        if not (folder / GEN_FILE).is_file():
            raise HoleNotFound(hole_id)
        return folder

    def file(self, hole_id: str, name: str) -> Path:
        path = self.folder(hole_id) / name
        if name not in PACKAGE_FILES or not path.is_file():
            raise HoleNotFound(f"{hole_id}/{name[:40]}")
        return path

    def describe(self, hole_id: str, votes: dict[str, dict] | None = None) -> dict:
        """Summary of one hole; `rating` is the vote in `votes` (hole id -> entry, e.g. the viewer's own)."""
        folder = self.folder(hole_id)
        info = read_generator_info(folder)
        hole = json.loads((folder / "hole.json").read_text())
        vote = (votes or {}).get(hole_id)
        return {
            "id": hole_id, "preset": info["preset"], "theme": info["theme"], "par": info["par"],
            "seed": info["seed"], "params": info["params"], "modelScore": info.get("modelScore"),
            "lengthMeters": round(path_length(hole["holePath"]["points"]), 1),
            "created": folder.stat().st_mtime,
            "rating": None if vote is None else ("up" if vote["rating"] > 0 else "down"),
        }

    def summaries(self, hole_ids: list[str], votes: dict[str, dict] | None = None) -> list[dict]:
        """Summaries in the given order, skipping holes no longer on disk (pruned)."""
        out = []
        for hole_id in hole_ids:
            try:
                out.append(self.describe(hole_id, votes))
            except (HoleNotFound, FileNotFoundError):  # pruned meanwhile
                pass
        return out
