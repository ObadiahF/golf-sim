"""JSON API for the course trainer. Thin layer over course_gen (gen_hole / preference / feedback), with
logins (auth.py) and every vote in Postgres (course_gen's PostgresStore) so several people can train one model.
The shared hole pool (pool.py, filled by pool_worker.py) serves each user holes they have not seen; ranking.py
ranks holes by everyone's votes; game.py is the read-only, key-protected API for the Unity game."""
from __future__ import annotations

import threading
from contextlib import asynccontextmanager
from pathlib import Path
from typing import Literal

from fastapi import APIRouter, Depends, FastAPI, HTTPException
from fastapi.responses import FileResponse, Response
from fastapi.staticfiles import StaticFiles
from pydantic import BaseModel, Field

import _paths
import db
import inputs
import pool
from auth import auth_router
from config import Settings
from feedback import catalog
from game import game_router
from gen_hole import generate_hole, presets_info, prune_unrated, rate_package, status
from holes import HoleNotFound, HoleStore
from inputs import FiniteFloat, HoleId, Text
from pg_store import PostgresStore
from pool_worker import PoolWorker, train_and_log
from ranking import FORMULA, top_holes
from rating_store import to_jsonl
from style import PARAMS, PARS, PRESETS
from users import User


class GenerateRequest(BaseModel):
    preset: Text = "parkland"
    par: int | None = None
    seed: int | None = Field(default=None, ge=0, le=2 ** 63 - 1)  # votes.seed is a bigint
    overrides: dict[Text, FiniteFloat] = Field(default_factory=dict)  # NaN / Infinity: 422
    useModel: bool = True


class RateRequest(BaseModel):
    id: HoleId
    rating: Literal["up", "down"]
    comment: Text | None = Field(default=None, max_length=4000)
    tags: list[Text] = Field(default_factory=list, max_length=50)


def holes_file_url(hole_id: str, name: str) -> str:
    return f"/api/holes/{hole_id}/{name}"


def create_app(settings: Settings, web_dist: Path | None = _paths.WEB_DIST) -> FastAPI:
    dsn = settings.database_url
    out_root = Path(settings.holes_dir)
    store = PostgresStore(dsn)
    holes = HoleStore(out_root)
    generations = threading.BoundedSemaphore(settings.max_generations)  # ad hoc + pool; the rest queue here
    prune_lock = threading.Lock()
    train_lock = threading.Lock()  # one retrain at a time (they all write the one model file)
    worker = PoolWorker(settings, store, generations, train_lock)

    @asynccontextmanager
    async def lifespan(_app: FastAPI):
        worker.start()  # first batch if there is none; resumes batches a restart interrupted
        yield
        worker.stop()

    docs = settings.api_docs  # /docs, /redoc, /openapi.json: development only (TRAINER_API_DOCS)
    app = FastAPI(title="Course Trainer", lifespan=lifespan, default_response_class=inputs.AsciiJSONResponse,
                  docs_url="/docs" if docs else None, redoc_url="/redoc" if docs else None,
                  openapi_url="/openapi.json" if docs else None)
    app.state.pool = worker
    inputs.install(app)  # malformed input: 4xx, never 500

    login_routes, current_user = auth_router(settings)
    app.include_router(login_routes)
    app.include_router(game_router(settings, holes))  # bearer game key, not the session cookie
    api = APIRouter(prefix="/api", dependencies=[Depends(current_user)])  # everything below needs a login

    def hole_or_404(fn, *args):
        try:
            return fn(*args)
        except HoleNotFound as e:
            raise HTTPException(404, f"No generated hole {e}") from None

    def my_votes(user: User) -> dict[str, dict]:
        return {e["id"]: e for e in store.load(user.name)}

    def my_status(user: User, preset: str | None) -> dict:
        """`presetVotes`: everyone's likes / dislikes on `preset` (all presets when None), the taste card's tally."""
        votes = [e for e in store.load() if preset is None or e["style"]["preset"] == preset]
        up = sum(e["rating"] > 0 for e in votes)
        return {**status(preset, store), "yours": len(store.load(user.name)),
                "presetVotes": {"up": up, "down": len(votes) - up}}

    def refill() -> None:
        """Start the next pool batch if it is due (pool.refill_due)."""
        if pool.maybe_refill(dsn, settings.pool_batch_size, settings.pool_refill_at, settings.pool_max_unrated):
            worker.kick()

    def prune(package: Path) -> list[str]:
        """Old unrated holes go, except pool holes and ones anyone voted on or opened / generated within the grace
        period."""
        grace = settings.view_grace_hours
        with prune_lock:
            keep = store.rated_ids() | db.viewed_since(dsn, grace) | pool.hole_ids(dsn)
            return prune_unrated(out_root, settings.keep_unrated, package, keep, grace * 3600)

    @app.get("/healthz", include_in_schema=False)
    def health():
        with db.connect(dsn) as conn:
            conn.execute("SELECT 1")
        return {"ok": True}

    @api.get("/presets")
    def presets():
        return {**presets_info(), "pars": list(PARS), "feedback": catalog()}

    @api.get("/status")
    def get_status(preset: str | None = None, user: User = Depends(current_user)):
        if preset is not None and preset not in PRESETS:
            raise HTTPException(400, f"Unknown preset '{preset}'")
        return my_status(user, preset)

    @api.get("/holes")
    def recent(limit: int = 20, user: User = Depends(current_user)):
        ids = db.recent_hole_ids(dsn, user.id, max(1, min(limit, 100)))
        return {"holes": holes.summaries(ids, my_votes(user))}

    @api.get("/holes/{hole_id}")
    def describe(hole_id: str, user: User = Depends(current_user)):
        """Never marks the hole seen: only opening its hole.json without `peek` (or rating it) does."""
        return hole_or_404(holes.describe, hole_id, my_votes(user))

    @api.get("/holes/{hole_id}/{name}")
    def package_file(hole_id: str, name: str, peek: bool = False, user: User = Depends(current_user)):
        """`?peek=true` (opening a leaderboard hole) does not count as seeing it."""
        path = hole_or_404(holes.file, hole_id, name)
        if name == "hole.json" and not peek:  # the viewer opening this hole: seen, and off the prune list a while
            db.touch_view(dsn, user.id, hole_id)
        return FileResponse(path, headers={"Cache-Control": "private, no-cache"})

    @api.post("/generate")
    def generate(req: GenerateRequest, user: User = Depends(current_user)):
        if req.preset not in PRESETS:
            raise HTTPException(400, f"Unknown preset '{req.preset}'")
        if req.par is not None and req.par not in PARS:
            raise HTTPException(400, f"Par must be one of {PARS}")
        unknown = set(req.overrides) - set(PARAMS)
        if unknown:
            raise HTTPException(400, f"Unknown knob(s): {', '.join(sorted(unknown))}")
        with generations:
            try:
                result, _ = generate_hole(req.preset, req.par, req.seed, req.overrides, out_root,
                                          settings.gen_spacing, keep_unrated=None, use_model=req.useModel)
            except RuntimeError as e:  # no playable layout for these pinned knobs
                raise HTTPException(422, str(e)) from None
            except SystemExit as e:  # course_gen's CLI-style rejection of a bad preset / override
                raise HTTPException(400, str(e.code)) from None
        db.touch_view(dsn, user.id, result["id"])
        pruned = prune(Path(result["package"]))
        return {**holes.describe(result["id"], my_votes(user)), "attempts": result["attempts"], "pruned": pruned}

    @api.post("/rate")
    def rate(req: RateRequest, user: User = Depends(current_user)):
        folder = hole_or_404(holes.folder, req.id)
        try:
            entry = rate_package(folder, req.rating, req.comment, req.tags, store, user.name)
        except ValueError as e:
            raise HTTPException(400, str(e)) from None
        db.touch_view(dsn, user.id, req.id)  # rated = seen (e.g. a peeked leaderboard hole)
        refill()  # ratings are what start the next batch
        return {"entry": entry, "status": my_status(user, entry["style"]["preset"])}

    @api.post("/train")
    def retrain(preset: str | None = None, only: str | None = None, user: User = Depends(current_user)):
        """Retrain from everyone's latest votes, or with `?only=<name>` from one person's."""
        trained = train_and_log(dsn, store, train_lock, user.id, only)
        if trained is None:
            raise HTTPException(409, f"No ratings{f' by {only}' if only else ''} yet: rate some holes first.")
        return my_status(user, preset if preset in PRESETS else None)

    @api.get("/next")
    def next_hole(user: User = Depends(current_user)):
        """The next pool hole you have not seen (recorded as seen), newest batch first in your own shuffled order;
        `state: "generating"` while none is ready yet (poll). Starts the next batch when it is due."""
        while (hole_id := pool.serve_next(dsn, user.id)) is not None:
            try:
                hole = holes.describe(hole_id, my_votes(user))
                break
            except (HoleNotFound, FileNotFoundError):  # deleted by hand: counted as seen, try the next one
                continue
        else:
            hole = None
        refill()
        return {"state": "ready" if hole else "generating", "hole": hole,
                "pool": pool.progress(dsn, user.id, settings.pool_max_unrated)}

    @api.get("/top")
    def leaderboard(limit: int = 20, user: User = Depends(current_user)):
        """Holes ranked by everyone's latest votes (Wilson lower bound, see ranking.py); `rating` is your vote."""
        ranked = top_holes(dsn, holes, max(1, min(limit, 100)), holes_file_url, my_votes(user))
        return {"formula": FORMULA, "holes": ranked}

    @api.get("/export/votes.jsonl")
    def export(history: bool = False):
        """Every person's latest vote per hole (`?history=true`: every vote ever), ratings.jsonl lines + user."""
        return Response(to_jsonl(store.export(history)), media_type="application/x-ndjson",
                        headers={"Content-Disposition": 'attachment; filename="votes.jsonl"'})

    app.include_router(api)
    if web_dist is not None and (web_dist / "index.html").is_file():
        app.mount("/", StaticFiles(directory=web_dist, html=True), name="web")
    return app
