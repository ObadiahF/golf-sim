"""Where votes live. Every store hands training the same entries as data/ratings.jsonl lines:
{"id", "rating", "time", "style": {preset, theme, par, params}, "tags"?, "comment"?, "user"?}.

- JsonlStore: data/ratings.jsonl, append-only (the Unity window and the CLI; no database needed).
- PostgresStore (pg_store.py): the shared Course Trainer's `votes` table, chosen when TRAINER_DATABASE_URL is set.

`load()` returns the latest vote per (user, hole); `export(history=True)` every vote ever cast.
"""
from __future__ import annotations

import json
import os
from pathlib import Path
from typing import Protocol

import preference

DB_ENV = "TRAINER_DATABASE_URL"


class RatingStore(Protocol):
    def record(self, gen_info: dict, rating: int, *, comment: str | None = None, tags=None,
               user: str | None = None) -> dict: ...

    def load(self, user: str | None = None) -> list[dict]: ...

    def rated_ids(self) -> set[str]: ...

    def export(self, history: bool = False) -> list[dict]: ...


class JsonlStore:
    """The append-only ratings log. `path=None` follows preference.RATINGS_PATH at call time (tests redirect it)."""

    def __init__(self, path: Path | None = None):
        self.path = Path(path) if path else None

    def record(self, gen_info, rating, *, comment=None, tags=None, user=None) -> dict:
        return preference.record_rating(gen_info, rating, self.path, comment=comment, tags=tags, user=user)

    def load(self, user=None) -> list[dict]:
        return [r for r in preference.load_ratings(self.path) if user is None or r.get("user") == user]

    def rated_ids(self) -> set[str]:
        return preference.rated_ids(self.path)

    def export(self, history=False) -> list[dict]:
        return preference.read_ratings_log(self.path) if history else self.load()


def configured_store(ratings_file: str | Path | None = None) -> RatingStore:
    """An explicit ratings file wins; else Postgres when TRAINER_DATABASE_URL is set; else data/ratings.jsonl."""
    if ratings_file is None and os.environ.get(DB_ENV):
        from pg_store import PostgresStore  # psycopg is only needed on this path
        return PostgresStore(os.environ[DB_ENV])
    return JsonlStore(ratings_file)


def to_jsonl(entries: list[dict]) -> str:
    """Entries as ratings.jsonl text (one JSON object per line), e.g. for exports."""
    return "".join(json.dumps(e) + "\n" for e in entries)
