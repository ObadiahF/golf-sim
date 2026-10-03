"""Read-only API for the Unity game: the top holes, a random round of holes and their package files, behind a key.

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
from random_holes import WEIGHTING, random_holes
from ranking import FORMULA, top_holes

PREFIX = "/api/game"
MAX_LIMIT = 100
MAX_EXCLUDE = 200  # recently played ids the game may send
FIELDS = ("rank", "id", "preset", "theme", "par", "lengthMeters", "ups", "downs", "score", "previewUrl", "files")


def game_file_url(hole_id: str, name: str) -> str:
    return f"{PREFIX}/holes/{hole_id}/{name}"


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
    def random_round(count: int = 9, exclude: str = ""):
        """`count` different holes drawn at random (random_holes.py); `exclude`: comma-separated recently played ids."""
        recent = [h.strip() for h in exclude.split(",") if h.strip()][:MAX_EXCLUDE]
        picked = random_holes(settings.database_url, holes, checks, max(1, min(count, MAX_LIMIT)), game_file_url,
                              exclude=recent)
        return game_list(picked, weighting=WEIGHTING)

    @router.get("/holes/{hole_id}/{name}")
    def package_file(hole_id: str, name: str):
        try:
            path = holes.file(hole_id, name)
        except HoleNotFound:
            raise HTTPException(404, f"No hole file {hole_id}/{name}") from None
        # private: Cloudflare must not cache a keyed response for others. Packages never change, so a day is safe.
        return FileResponse(path, headers={"Cache-Control": "private, max-age=86400, immutable"})

    return router
