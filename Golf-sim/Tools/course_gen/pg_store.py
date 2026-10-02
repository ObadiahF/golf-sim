"""Votes in Postgres: the shared Course Trainer's store (see rating_store.py for the interface).

Tables come from Tools/course_trainer/migrations: `users`, `votes` (append-only: every vote ever cast, with a
snapshot of the hole's style) and the `latest_votes` view (newest row per user and hole). psycopg is imported
lazily, so the Unity window and the CLI never need it.
"""
from __future__ import annotations

from preference import make_entry

_COLUMNS = """SELECT v.hole_id AS id, v.rating, extract(epoch FROM v.created_at)::float8 AS time, v.preset, v.theme,
                     v.par, v.params, v.tags, v.comment, u.name AS user_name
              FROM {table} v JOIN users u ON u.id = v.user_id"""


def connect(dsn: str):
    """One short-lived connection; `with connect(dsn) as conn:` commits on success. Rows come back as dicts."""
    import psycopg
    from psycopg.rows import dict_row
    return psycopg.connect(dsn, row_factory=dict_row)


def _jsonb(value):
    from psycopg.types.json import Jsonb
    return Jsonb(value)


def _entry(row: dict) -> dict:
    entry = make_entry(row, row["rating"], comment=row["comment"], user=row["user_name"], when=row["time"])
    if row["tags"]:  # not re-validated: a tag later removed from feedback.TAGS is ignored by training, not fatal
        entry["tags"] = row["tags"]
    return entry


class PostgresStore:
    def __init__(self, dsn: str):
        self.dsn = dsn

    def record(self, gen_info, rating, *, comment=None, tags=None, user=None) -> dict:
        """Append a vote by `user` (a user name) with a snapshot of the hole's style; returns the entry."""
        return self._insert(user, make_entry(gen_info, rating, comment=comment, tags=tags, user=user), gen_info)

    def load(self, user=None) -> list[dict]:
        return self._select("latest_votes", user)

    def export(self, history=False) -> list[dict]:
        return self._select("votes" if history else "latest_votes")

    def rated_ids(self) -> set[str]:
        with connect(self.dsn) as conn:
            return {r["hole_id"] for r in conn.execute("SELECT DISTINCT hole_id FROM votes")}

    def import_entries(self, entries: list[dict], user: str) -> int:
        """Copy ratings.jsonl lines in as `user`'s votes, skipping ones already imported. Returns the count added."""
        added = 0
        for e in entries:
            added += self._insert(user, e, e["style"], skip_existing=True) is not None
        return added

    def _insert(self, user: str | None, entry: dict, info: dict, skip_existing: bool = False) -> dict | None:
        if not user:
            raise ValueError("Votes in Postgres need a user name")
        style = entry["style"]
        args = {"user": user, "hole": entry["id"], "rating": entry["rating"], "comment": entry.get("comment"),
                "tags": _jsonb(entry.get("tags", [])), "preset": style["preset"], "theme": style["theme"],
                "par": style["par"], "params": _jsonb(style["params"]), "gv": info.get("generatorVersion"),
                "seed": info.get("seed"), "score": info.get("modelScore"), "time": entry["time"]}
        guard = ("AND NOT EXISTS (SELECT 1 FROM votes WHERE user_id = u.id AND hole_id = %(hole)s"
                 " AND created_at = to_timestamp(%(time)s))") if skip_existing else ""
        with connect(self.dsn) as conn:
            if conn.execute("SELECT 1 FROM users WHERE lower(name) = lower(%(user)s)", args).fetchone() is None:
                raise ValueError(f"No user '{user}'")
            row = conn.execute(f"""
                INSERT INTO votes (user_id, hole_id, rating, comment, tags, preset, theme, par, params,
                                   generator_version, seed, model_score, created_at)
                SELECT u.id, %(hole)s, %(rating)s, %(comment)s, %(tags)s, %(preset)s, %(theme)s, %(par)s, %(params)s,
                       %(gv)s, %(seed)s, %(score)s, to_timestamp(%(time)s)
                FROM users u WHERE lower(u.name) = lower(%(user)s) {guard}
                RETURNING id""", args).fetchone()
        return None if row is None else entry

    def _select(self, table: str, user: str | None = None) -> list[dict]:
        where, args = ("WHERE lower(u.name) = lower(%s)", [user]) if user else ("", [])
        with connect(self.dsn) as conn:
            rows = conn.execute(f"{_COLUMNS.format(table=table)} {where} ORDER BY v.created_at, v.id", args)
            return [_entry(r) for r in rows]
