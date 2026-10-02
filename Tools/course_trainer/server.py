#!/usr/bin/env python3
"""Course Trainer server: walk generated holes in the browser, rate them, retrain the taste model.

  python server.py [--port 8765] [--host 127.0.0.1] [--out DIR] [--open]   serve (migrates + seeds first)
  python server.py seed                       apply migrations, create TRAINER_SEED_USERS, import ratings.jsonl
  python server.py adduser NAME [--password PW]   (prompts when no --password)
  python server.py passwd NAME [--password PW]
  python server.py export [--history] > votes.jsonl

Needs TRAINER_DATABASE_URL (Postgres); other settings come from TRAINER_* variables (see .env.example).
Serves the JSON API under /api and the built web UI (web/dist) at /. For UI development run `npm run dev`
in web/ as well; Vite proxies /api to this server.
"""
from __future__ import annotations

import argparse
import getpass
import os
import sys
import threading
import webbrowser
from pathlib import Path

import _paths
import db
import users
from config import Settings
from pg_store import PostgresStore
from rating_store import to_jsonl


def log(message: str) -> None:
    print(message, file=sys.stderr, flush=True)  # stdout stays clean for `export`


def setup(settings: Settings) -> None:
    for name in db.migrate(settings.database_url):
        log(f"applied migration {name}")
    for line in users.seed(settings.database_url, settings.seed_users, settings.legacy_ratings):
        log(line)


def cmd_serve(args, settings: Settings):
    import uvicorn
    from api import create_app

    setup(settings)
    if not os.environ.get("TRAINER_SESSION_SECRET"):
        log("TRAINER_SESSION_SECRET is not set: using a random one (everyone is logged out on restart).")
    if not (_paths.WEB_DIST / "index.html").is_file():
        log("web/dist not built: only the API is served. Run ./run.sh (or `npm run build` in web/).")
    url = f"http://{args.host}:{args.port}/"
    log(f"Course Trainer on {url}  (holes -> {Path(settings.holes_dir).resolve()})")
    if args.open:
        threading.Timer(1.0, webbrowser.open, [url]).start()
    # proxy_headers off: the scheme stays "http" behind TLS-terminating proxies and nothing relies on it;
    # client IPs for the login throttle come from auth.client_ip (TRAINER_TRUST_PROXY).
    uvicorn.run(create_app(settings), host=args.host, port=args.port, log_level="warning", proxy_headers=False)


def ask_password(args) -> str:
    if args.password:
        return args.password
    if not sys.stdin.isatty():
        return sys.stdin.readline().rstrip("\n")
    first = getpass.getpass("Password: ")
    if first != getpass.getpass("Again: "):
        sys.exit("Passwords differ.")
    return first


def cmd_seed(args, settings: Settings):
    setup(settings)


def cmd_adduser(args, settings: Settings):
    db.migrate(settings.database_url)
    password = ask_password(args)
    if not password:
        sys.exit("Empty password.")
    try:
        user = users.create_user(settings.database_url, args.name, password)
    except ValueError as e:
        sys.exit(str(e))
    log(f"Created user {user.name}")


def cmd_passwd(args, settings: Settings):
    password = ask_password(args)
    if not password:
        sys.exit("Empty password.")
    try:
        users.set_password(settings.database_url, args.name, password)
    except ValueError as e:
        sys.exit(str(e))
    log(f"Password changed for {args.name} (their old sessions are logged out)")


def cmd_export(args, settings: Settings):
    sys.stdout.write(to_jsonl(PostgresStore(settings.database_url).export(args.history)))


def main(argv: list[str] | None = None):
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--host", default=os.environ.get("TRAINER_HOST", "127.0.0.1"))
    p.add_argument("--port", type=int, default=int(os.environ.get("TRAINER_PORT", "8765")))
    p.add_argument("--out", help="where hole packages are written (default: TRAINER_HOLES_DIR, else Unity's)")
    p.add_argument("--open", action="store_true", help="open the browser once the server is up")
    p.set_defaults(func=cmd_serve)
    sub = p.add_subparsers(dest="cmd")
    sub.add_parser("serve", help="run the server (the default)").set_defaults(func=cmd_serve)
    sub.add_parser("seed", help="migrate, create seed users, import ratings.jsonl").set_defaults(func=cmd_seed)
    for name, func, text in (("adduser", cmd_adduser, "create a login"), ("passwd", cmd_passwd, "change a password")):
        s = sub.add_parser(name, help=text)
        s.add_argument("name")
        s.add_argument("--password", help="(else prompted, or read from stdin when it is not a terminal)")
        s.set_defaults(func=func)
    e = sub.add_parser("export", help="print votes as ratings.jsonl lines (+ user) to stdout")
    e.add_argument("--history", action="store_true", help="every vote ever, not just each person's latest")
    e.set_defaults(func=cmd_export)

    args = p.parse_args(argv)
    settings = Settings.from_env(**({"holes_dir": Path(args.out)} if args.out else {}))
    args.func(args, settings)


if __name__ == "__main__":
    main()
