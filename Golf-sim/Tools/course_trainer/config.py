"""Server settings, all from environment variables (see .env.example). Tests build Settings directly."""
from __future__ import annotations

import os
import secrets
import sys
from dataclasses import dataclass, field
from pathlib import Path

import _paths  # noqa: F401
from gen_hole import DEFAULT_OUT
from generate import DEFAULT_SPACING
from rating_store import DB_ENV

TRUE = {"1", "true", "yes", "on"}
CHECK_LIMITS = ("launch_near_m", "launch_near_max_m", "launch_far_m", "launch_far_max_m", "green_pin_max_slope",
                "tee_line_min_clear", "landing_max_side_slope")


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)  # stdout stays clean for `export` / `top`


def _env(name: str, default: str = "") -> str:
    return os.environ.get(name, "").strip() or default


@dataclass(frozen=True)
class Settings:
    database_url: str
    session_secret: str = field(default_factory=lambda: secrets.token_urlsafe(32))
    cookie_secure: bool = False
    trust_proxy: bool = False        # take client IPs from CF-Connecting-IP / X-Forwarded-For (behind a proxy only)
    session_days: int = 30
    holes_dir: Path = DEFAULT_OUT
    max_generations: int = 2         # concurrent generations; more requests wait their turn
    keep_unrated: int = 40           # unrated holes kept on disk (rated ones are kept forever)
    view_grace_hours: float = 3.0    # a hole opened (or made) this recently is never pruned
    seed_users: str = ""             # "name:password,name:password", created on startup if missing
    legacy_ratings: Path | None = None  # ratings.jsonl imported as the `legacy` user (default: course_gen's)
    gen_spacing: float = DEFAULT_SPACING  # meters per heightmap sample (tests use a coarse one: fast)
    pool_batch_size: int = 100       # holes per shared pool batch
    pool_refill_at: float = 0.5      # next batch starts once someone has rated this share of the newest one
    pool_max_unrated: int = 300      # no new batch while this many pool holes have no vote from anyone
    pool_min_unseen: int = 15        # ... and the next starts when the user asking has fewer unseen holes ready
    pool_autorun: bool = True        # background thread + worker processes fill batches (tests: pool.run_pending())
    game_key: str = ""               # bearer key for the read-only /api/game routes (empty: disabled)
    api_docs: bool = False           # serve /docs, /redoc, /openapi.json (development only)
    # Playability (hole_checks.py): unplayable when the ground rises more than *_max_m above a low tee shot within
    # *_m of the tee, or the green is steeper than green_pin_max_slope (0.06 = 6 %) within 2 m of the pin, or trees
    # block more than 1 - tee_line_min_clear of the tee-shot lines (or every line of a later shot), or a landing zone
    # leans more than landing_max_side_slope across the line.
    # Changing them needs `check-holes --all` to re-judge checked holes.
    launch_near_m: float = 60.0
    launch_near_max_m: float = 1.0
    launch_far_m: float = 100.0      # the scanner looks no further than validate.LAUNCH_RUN (100 m)
    launch_far_max_m: float = 2.5
    green_pin_max_slope: float = 0.06  # the generator itself keeps the pin area under validate.GREEN_PIN_SLOPE (4 %)
    tee_line_min_clear: float = 0.4    # the generator itself keeps validate.TEE_FAN_CLEAR (60 %) of them clear
    landing_max_side_slope: float = 0.15  # the generator itself keeps them under validate.LANDING_CROSS (8 %)
    top_min_likes: int = 1           # Top holes / the game: only holes with more 👍 than 👎 and at least this many 👍

    @classmethod
    def from_env(cls, **overrides) -> "Settings":
        values = {
            "database_url": _env(DB_ENV),
            "cookie_secure": _env("TRAINER_COOKIE_SECURE").lower() in TRUE,
            "trust_proxy": _env("TRAINER_TRUST_PROXY").lower() in TRUE,
            "session_days": int(_env("TRAINER_SESSION_DAYS", "30")),
            "holes_dir": Path(_env("TRAINER_HOLES_DIR", str(DEFAULT_OUT))),
            "max_generations": int(_env("TRAINER_MAX_GENERATIONS", "2")),
            "keep_unrated": int(_env("TRAINER_KEEP_UNRATED", "40")),
            "view_grace_hours": float(_env("TRAINER_VIEW_GRACE_HOURS", "3")),
            "seed_users": _env("TRAINER_SEED_USERS"),
            "gen_spacing": float(_env("TRAINER_GEN_SPACING", str(DEFAULT_SPACING))),
            "pool_batch_size": int(_env("TRAINER_POOL_BATCH_SIZE", "100")),
            "pool_refill_at": float(_env("TRAINER_POOL_REFILL_AT", "0.5")),
            "pool_max_unrated": int(_env("TRAINER_POOL_MAX_UNRATED", "300")),
            "pool_min_unseen": int(_env("TRAINER_POOL_MIN_UNSEEN", "15")),
            "game_key": _env("TRAINER_GAME_KEY"),
            "api_docs": _env("TRAINER_API_DOCS").lower() in TRUE,
            **{name: float(_env(f"TRAINER_{name.upper()}", str(getattr(cls, name)))) for name in CHECK_LIMITS},
            "top_min_likes": int(_env("TRAINER_TOP_MIN_LIKES", "1")),
        }
        if _env("TRAINER_SESSION_SECRET"):
            values["session_secret"] = _env("TRAINER_SESSION_SECRET")
        if _env("TRAINER_LEGACY_RATINGS"):
            values["legacy_ratings"] = Path(_env("TRAINER_LEGACY_RATINGS"))
        values.update(overrides)
        if not values["database_url"]:
            sys.exit(f"{DB_ENV} is not set (e.g. postgresql://trainer:trainer@127.0.0.1:5433/trainer).")
        return cls(**values)
