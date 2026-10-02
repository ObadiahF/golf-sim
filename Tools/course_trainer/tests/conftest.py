"""Fixtures: a throwaway Postgres database (TRAINER_TEST_DATABASE_URL; `docker compose run --rm test` sets it,
locally point it at the compose db, e.g. postgresql://trainer:trainer@127.0.0.1:5433/trainer_test) and an
app client with two users. Without the variable the database tests are skipped."""
import os
import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _paths  # noqa: E402,F401
import preference  # noqa: E402

TABLES = "users, votes, hole_views, training_runs, batches, pool_holes, sessions, hole_checks"
PASSWORDS = {"obi": "pw-obi", "sam": "pw-sam"}


@pytest.fixture(scope="session")
def dsn():
    url = os.environ.get("TRAINER_TEST_DATABASE_URL")
    if not url:
        pytest.skip("TRAINER_TEST_DATABASE_URL not set (run `docker compose run --rm test`)")
    import psycopg
    from psycopg.conninfo import conninfo_to_dict, make_conninfo
    name = conninfo_to_dict(url)["dbname"]
    with psycopg.connect(make_conninfo(url, dbname="postgres"), autocommit=True) as conn:
        if conn.execute("SELECT 1 FROM pg_database WHERE datname = %s", [name]).fetchone() is None:
            conn.execute(f'CREATE DATABASE "{name}"')
    with psycopg.connect(url, autocommit=True) as conn:  # fresh schema each session
        conn.execute("DROP SCHEMA public CASCADE; CREATE SCHEMA public")
    import db
    db.migrate(url)
    return url


@pytest.fixture
def clean_db(dsn, tmp_path, monkeypatch):
    import db
    with db.connect(dsn) as conn:
        conn.execute(f"TRUNCATE {TABLES} RESTART IDENTITY CASCADE")
    monkeypatch.setattr(preference, "RATINGS_PATH", tmp_path / "ratings.jsonl")
    monkeypatch.setattr(preference, "MODEL_PATH", tmp_path / "model.npz")
    return dsn


@pytest.fixture
def make_client(clean_db, tmp_path):
    """make_client(**settings) -> (TestClient, Settings); users obi and sam exist. Generation is coarse (fast) and
    the pool has no background worker: tests fill it with `client.app.state.pool.run_pending()`. Batches start on the
    rated / seen share only (no unseen buffer, pool_min_unseen=0) unless a test sets it."""
    from fastapi.testclient import TestClient

    import users
    from api import create_app
    from config import Settings

    for name, pw in PASSWORDS.items():
        users.create_user(clean_db, name, pw)
    dist = tmp_path / "dist"
    dist.mkdir(exist_ok=True)
    (dist / "index.html").write_text("<html>trainer</html>")

    def make(**overrides):
        settings = Settings(**{"database_url": clean_db, "session_secret": "test-secret",
                               "holes_dir": tmp_path / "holes", "gen_spacing": 3.0, "pool_batch_size": 4,
                               "pool_min_unseen": 0,
                               "pool_autorun": False, **overrides})
        return TestClient(create_app(settings, dist)), settings
    return make


def login(client, name="obi", password=None):
    r = client.post("/api/login", json={"name": name, "password": password or PASSWORDS[name.lower()]})
    assert r.status_code == 200, r.text
    return client


@pytest.fixture
def client(make_client):
    """Logged in as obi."""
    return login(make_client()[0])


def generate(client, **body):
    r = client.post("/api/generate", json={"preset": "links", "seed": 3, **body})
    assert r.status_code == 200, r.text
    return r.json()


def next_hole(client):
    r = client.get("/api/next")
    assert r.status_code == 200, r.text
    return r.json()


def fill_pool(client) -> list[str]:
    """Generate every batch still filling (what the background worker does); returns all pool hole ids."""
    import pool
    worker = client.app.state.pool
    worker.run_pending()
    return sorted(pool.hole_ids(worker.dsn))
