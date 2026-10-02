"""Server settings, all from environment variables (see .env.example). Tests build Settings directly."""
from __future__ import annotations

import os
import secrets
import sys
from dataclasses import dataclass, field
from pathlib import Path

import _paths  # noqa: F401
from gen_hole import DEFAULT_OUT
from rating_store import DB_ENV

TRUE = {"1", "true", "yes", "on"}


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
        }
        if _env("TRAINER_SESSION_SECRET"):
            values["session_secret"] = _env("TRAINER_SESSION_SECRET")
        if _env("TRAINER_LEGACY_RATINGS"):
            values["legacy_ratings"] = Path(_env("TRAINER_LEGACY_RATINGS"))
        values.update(overrides)
        if not values["database_url"]:
            sys.exit(f"{DB_ENV} is not set (e.g. postgresql://trainer:trainer@127.0.0.1:5433/trainer).")
        return cls(**values)
