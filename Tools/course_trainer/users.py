"""Accounts: scrypt password hashes (stdlib, no extra deps), create / change password, and the startup seed
(users from TRAINER_SEED_USERS, plus the old ratings.jsonl imported as the `legacy` user's votes)."""
from __future__ import annotations

import base64
import hashlib
import hmac
import secrets
from dataclasses import dataclass
from pathlib import Path

import _paths  # noqa: F401
import preference
from db import connect
from pg_store import PostgresStore

LEGACY_USER = "legacy"
_N, _R, _P = 2 ** 15, 8, 1           # scrypt cost: ~50 ms and 32 MB per check
_MAXMEM = 64 * 1024 * 1024
MAX_NAME = 40


@dataclass(frozen=True)
class User:
    id: int
    name: str
    password_hash: str | None


def _b64(raw: bytes) -> str:
    return base64.b64encode(raw).decode()


def hash_password(password: str) -> str:
    salt = secrets.token_bytes(16)
    key = hashlib.scrypt(password.encode(), salt=salt, n=_N, r=_R, p=_P, maxmem=_MAXMEM)
    return f"scrypt${_N}${_R}${_P}${_b64(salt)}${_b64(key)}"


def verify_password(password: str, stored: str | None) -> bool:
    """Constant-time check. With no stored hash it still does the work, so unknown names take as long."""
    try:
        _, n, r, p, salt, key = (stored or DUMMY_HASH).split("$")
        test = hashlib.scrypt(password.encode(), salt=base64.b64decode(salt), n=int(n), r=int(r), p=int(p),
                              maxmem=_MAXMEM)
    except ValueError:
        return False
    return stored is not None and hmac.compare_digest(test, base64.b64decode(key))


DUMMY_HASH = hash_password(secrets.token_urlsafe(12))


def valid_name(name: str) -> str:
    name = name.strip()
    if not name or len(name) > MAX_NAME or any(c in name for c in ":,") or not name.isprintable():
        raise ValueError(f"User names are 1-{MAX_NAME} printable characters, without ':' or ','")
    return name


def find_user(dsn: str, name: str | None = None, user_id: int | None = None) -> User | None:
    """By name (case-insensitive) or by id."""
    where, arg = ("lower(name) = lower(%s)", name) if user_id is None else ("id = %s", user_id)
    with connect(dsn) as conn:
        row = conn.execute(f"SELECT id, name, password_hash FROM users WHERE {where}", [arg]).fetchone()
    return None if row is None else User(**row)


def create_user(dsn: str, name: str, password: str | None) -> User:
    """`password=None` makes an account that cannot log in. Raises ValueError if the name is taken."""
    name = valid_name(name)
    if find_user(dsn, name):
        raise ValueError(f"User '{name}' already exists")
    with connect(dsn) as conn:
        row = conn.execute("INSERT INTO users (name, password_hash) VALUES (%s, %s) RETURNING id, name, password_hash",
                           [name, hash_password(password) if password else None]).fetchone()
    return User(**row)


def set_password(dsn: str, name: str, password: str) -> None:
    with connect(dsn) as conn:
        if conn.execute("UPDATE users SET password_hash = %s WHERE lower(name) = lower(%s)",
                        [hash_password(password), name]).rowcount == 0:
            raise ValueError(f"No user '{name}'")


def mark_login(dsn: str, user_id: int) -> None:
    with connect(dsn) as conn:
        conn.execute("UPDATE users SET last_login = now() WHERE id = %s", [user_id])


def parse_seed_users(spec: str) -> list[tuple[str, str]]:
    """'obi:changeme, sam:pw' -> [('obi', 'changeme'), ('sam', 'pw')]."""
    pairs = []
    for item in filter(None, (s.strip() for s in spec.split(","))):
        name, sep, password = item.partition(":")
        if not sep or not name.strip() or not password:
            raise ValueError(f"TRAINER_SEED_USERS entry '{name}' must look like name:password")
        pairs.append((name.strip(), password))
    return pairs


def seed(dsn: str, seed_users: str = "", legacy_file: Path | None = None) -> list[str]:
    """Idempotent: creates missing seed users (existing passwords are left alone; use `passwd`), the `legacy`
    user, and imports ratings.jsonl lines not imported yet. Returns what it did, for the log."""
    done = []
    for name, password in parse_seed_users(seed_users):
        if find_user(dsn, name) is None:
            create_user(dsn, name, password)
            done.append(f"created user {name}")
    if find_user(dsn, LEGACY_USER) is None:
        create_user(dsn, LEGACY_USER, None)
    entries = preference.read_ratings_log(legacy_file)
    if entries:
        added = PostgresStore(dsn).import_entries(entries, LEGACY_USER)
        if added:
            done.append(f"imported {added} legacy vote(s) from {legacy_file or preference.RATINGS_PATH}")
    return done
