"""Read-only API for the Unity game: the top holes, a random round of holes (of the course types the player chose),
the course types with how many holes each has, and the holes' package files, behind a key.

`Authorization: Bearer <TRAINER_GAME_KEY>` opens only these routes (everything else still needs a login
session); without TRAINER_GAME_KEY they answer 404. The key is compared in constant time.
"""
from __future__ import annotations

import hmac

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import FileResponse

from config import Settings
from hole_checks import HoleChecks
from holes import HoleNotFound, HoleStore
from random_holes import WEIGHTING, preset_counts, random_holes
from ranking import FORMULA, top_holes
from style import PRESETS

PREFIX = "/api/game"
MAX_LIMIT = 100
MAX_EXCLUDE = 200  # recently played ids the game may send
FIELDS = ("rank", "id", "preset", "theme", "par", "lengthMeters", "ups", "downs", "score", "previewUrl", "files")


def game_file_url(hole_id: str, name: str) -> str:
    return f"{PREFIX}/holes/{hole_id}/{name}"


def split_ids(text: str) -> list[str]:
    """A comma-separated query value as a list (blanks dropped, duplicates kept once, in order)."""
    return list(dict.fromkeys(part.strip() for part in text.split(",") if part.strip()))


def preset_filter(text: str) -> list[str]:
    """The preset ids in a `preset` query value ([]: any preset); 400 for an unknown one."""
    names = split_ids(text.lower())
    unknown = [name for name in names if name not in PRESETS]
    if unknown:
        raise HTTPException(400, f"Unknown preset '{unknown[0][:40]}'. Available: {', '.join(PRESETS)}")
    return names


def game_list(holes: list[dict], **extra) -> dict:
    """The response both hole lists share: {formula, ..., holes: [FIELDS of each]}."""
    return {"formula": FORMULA, **extra, "holes": [{k: h[k] for k in FIELDS} for h in holes]}


def game_router(settings: Settings, holes: HoleStore, checks: HoleChecks) -> APIRouter:
    key = settings.game_key.encode()

    def game_key(request: Request) -> None:
        if not key:
            raise HTTPException(404, "Game API disabled (TRAINER_GAME_KEY is not set)")
        scheme, _, token = request.headers.get("authorization", "").partition(" ")
        if scheme.lower() != "bearer" or not hmac.compare_digest(token.strip().encode(), key):
            raise HTTPException(401, "Bad or missing game key", headers={"WWW-Authenticate": "Bearer"})

    router = APIRouter(prefix=PREFIX, dependencies=[Depends(game_key)])

    @router.get("/top-holes")
    def top(limit: int = 9):
        ranked = top_holes(settings.database_url, holes, checks, max(1, min(limit, MAX_LIMIT)), game_file_url,
                           min_likes=settings.top_min_likes)
        return game_list(ranked)

    @router.get("/random-holes")
    def random_round(count: int = 9, exclude: str = "", preset: str = ""):
        """`count` different holes drawn at random (random_holes.py); `exclude`: comma-separated recently played ids;
        `preset`: comma-separated course types to draw from (default any). `matched`: how many holes those have."""
        presets = preset_filter(preset)
        picked, matched = random_holes(settings.database_url, holes, checks, max(1, min(count, MAX_LIMIT)),
                                       game_file_url, exclude=split_ids(exclude)[:MAX_EXCLUDE],
                                       presets=presets or None)
        return game_list(picked, weighting=WEIGHTING, presets=presets, matched=matched)

    @router.get("/presets")
    def presets():
        """Every course type (style.PRESETS order) with how many holes random-holes can draw from it, and the stock
        the pool keeps of each (TRAINER_POOL_MIN_STOCK)."""
        counts = preset_counts(settings.database_url, holes)
        return {"stock": settings.pool_min_stock,
                "presets": [{"id": p.name, "name": p.label, "theme": p.theme, "playable": counts[p.name]}
                            for p in PRESETS.values()]}

    @router.get("/holes/{hole_id}/{name}")
    def package_file(hole_id: str, name: str):
        try:
            path = holes.file(hole_id, name)
        except HoleNotFound:
            raise HTTPException(404, f"No hole file {hole_id}/{name}") from None
        # private: Cloudflare must not cache a keyed response for others. Packages never change, so a day is safe.
        return FileResponse(path, headers={"Cache-Control": "private, max-age=86400, immutable"})

    return router
