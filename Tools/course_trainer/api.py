"""JSON API for the course trainer. Thin layer over course_gen (gen_hole / preference / feedback)."""
from __future__ import annotations

import threading
from pathlib import Path
from typing import Literal

from fastapi import FastAPI, HTTPException
from fastapi.responses import FileResponse
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

import _paths  # noqa: F401
from feedback import catalog
from gen_hole import DEFAULT_OUT, generate_hole, presets_info, rate_package, status, train_and_save
from holes import HoleNotFound, HoleStore
from style import PARAMS, PARS, PRESETS


class GenerateRequest(BaseModel):
    preset: str = "parkland"
    par: int | None = None
    seed: int | None = Field(default=None, ge=0)
    overrides: dict[str, float] = Field(default_factory=dict)
    useModel: bool = True


class RateRequest(BaseModel):
    id: str
    rating: Literal["up", "down"]
    comment: str | None = Field(default=None, max_length=4000)
    tags: list[str] = Field(default_factory=list)


def create_app(out_root: Path = DEFAULT_OUT, web_dist: Path | None = _paths.WEB_DIST) -> FastAPI:
    app = FastAPI(title="Course Trainer")
    store = HoleStore(out_root)
    lock = threading.Lock()  # one generate / rate / train at a time (pruning and the ratings log are shared)

    def hole_or_404(fn, *args):
        try:
            return fn(*args)
        except HoleNotFound as e:
            raise HTTPException(404, f"No generated hole {e}") from None

    @app.get("/api/presets")
    def presets():
        return {**presets_info(), "pars": list(PARS), "feedback": catalog()}

    @app.get("/api/status")
    def get_status(preset: str | None = None):
        if preset is not None and preset not in PRESETS:
            raise HTTPException(400, f"Unknown preset '{preset}'")
        return status(preset)

    @app.get("/api/holes")
    def recent(limit: int = 20):
        return {"holes": store.recent(max(1, min(limit, 100)))}

    @app.get("/api/holes/{hole_id}")
    def describe(hole_id: str):
        return hole_or_404(store.describe, hole_id)

    @app.get("/api/holes/{hole_id}/{name}")
    def package_file(hole_id: str, name: str):
        return FileResponse(hole_or_404(store.file, hole_id, name), headers={"Cache-Control": "no-cache"})

    @app.post("/api/generate")
    def generate(req: GenerateRequest):
        if req.preset not in PRESETS:
            raise HTTPException(400, f"Unknown preset '{req.preset}'")
        if req.par is not None and req.par not in PARS:
            raise HTTPException(400, f"Par must be one of {PARS}")
        unknown = set(req.overrides) - set(PARAMS)
        if unknown:
            raise HTTPException(400, f"Unknown knob(s): {', '.join(sorted(unknown))}")
        with lock:
            try:
                result, pruned = generate_hole(req.preset, req.par, req.seed, req.overrides, out_root,
                                               use_model=req.useModel)
            except RuntimeError as e:  # no playable layout for these pinned knobs
                raise HTTPException(422, str(e)) from None
        return {**store.describe(result["id"]), "attempts": result["attempts"], "pruned": pruned}

    @app.post("/api/rate")
    def rate(req: RateRequest):
        folder = hole_or_404(store.folder, req.id)
        with lock:
            try:
                entry = rate_package(folder, req.rating, req.comment, req.tags)
            except ValueError as e:
                raise HTTPException(400, str(e)) from None
        return {"entry": entry, "status": status(entry["style"]["preset"])}

    @app.post("/api/train")
    def retrain(preset: str | None = None):
        with lock:
            trained = train_and_save()
        if trained is None:
            raise HTTPException(409, "No ratings yet: rate some holes first.")
        return status(preset if preset in PRESETS else None)

    if web_dist is not None and (web_dist / "index.html").is_file():
        app.mount("/", StaticFiles(directory=web_dist, html=True), name="web")
    return app
