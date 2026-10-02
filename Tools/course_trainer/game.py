"""Read-only API for the Unity game: the top holes and their package files, behind a bearer key.

`Authorization: Bearer <TRAINER_GAME_KEY>` opens only these two routes (everything else still needs a login
session); without TRAINER_GAME_KEY they answer 404. The key is compared in constant time.
"""
from __future__ import annotations

import hmac

from fastapi import APIRouter, Depends, HTTPException, Request
from fastapi.responses import FileResponse

from config import Settings
from holes import HoleNotFound, HoleStore
from ranking import FORMULA, top_holes

PREFIX = "/api/game"
MAX_LIMIT = 100


def game_file_url(hole_id: str, name: str) -> str:
    return f"{PREFIX}/holes/{hole_id}/{name}"


def game_router(settings: Settings, holes: HoleStore) -> APIRouter:
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
        ranked = top_holes(settings.database_url, holes, max(1, min(limit, MAX_LIMIT)), game_file_url)
        return {"formula": FORMULA, "holes": [
            {k: h[k] for k in ("rank", "id", "preset", "theme", "par", "lengthMeters", "ups", "downs", "score",
                               "previewUrl", "files")} for h in ranked]}

    @router.get("/holes/{hole_id}/{name}")
    def package_file(hole_id: str, name: str):
        try:
            path = holes.file(hole_id, name)
        except HoleNotFound:
            raise HTTPException(404, f"No hole file {hole_id}/{name}") from None
        # private: Cloudflare must not cache a keyed response for others. Packages never change, so a day is safe.
        return FileResponse(path, headers={"Cache-Control": "private, max-age=86400, immutable"})

    return router
