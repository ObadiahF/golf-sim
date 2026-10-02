#!/usr/bin/env python3
"""Course Trainer server: walk generated holes in the browser, rate them, retrain the taste model.

  python server.py [--port 8765] [--host 127.0.0.1] [--out ../../Assets/CourseData/generated] [--open]

Serves the JSON API under /api and the built web UI (web/dist) at /. For UI development run
`npm run dev` in web/ as well; Vite proxies /api to this server.
"""
from __future__ import annotations

import argparse
import threading
import webbrowser
from pathlib import Path

import uvicorn

import _paths
from api import create_app
from gen_hole import DEFAULT_OUT


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    p.add_argument("--host", default="127.0.0.1")
    p.add_argument("--port", type=int, default=8765)
    p.add_argument("--out", default=str(DEFAULT_OUT), help="where hole packages are written (default: Unity's)")
    p.add_argument("--open", action="store_true", help="open the browser once the server is up")
    args = p.parse_args()

    url = f"http://{args.host}:{args.port}/"
    if not (_paths.WEB_DIST / "index.html").is_file():
        print("web/dist not built: only the API is served. Run ./run.sh (or `npm run build` in web/).")
    print(f"Course Trainer on {url}  (holes -> {Path(args.out).resolve()})")
    app = create_app(Path(args.out))
    if args.open:
        threading.Timer(1.0, webbrowser.open, [url]).start()
    uvicorn.run(app, host=args.host, port=args.port, log_level="warning")


if __name__ == "__main__":
    main()
