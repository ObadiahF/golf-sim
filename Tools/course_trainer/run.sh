#!/usr/bin/env bash
# Local dev without the trainer container: install deps if missing, rebuild the UI if its sources changed,
# start the server. Needs Postgres: by default the compose one (`docker compose up -d db`, on 127.0.0.1:5433).
#   Tools/course_trainer/run.sh [--port 8765] [--open] [--out DIR]
#   Tools/course_trainer/run.sh adduser <name>        (any server.py subcommand)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PY="${PYTHON:-$HERE/../course_prep/.venv/bin/python}"
WEB="$HERE/web"

if [ ! -x "$PY" ]; then
  echo "No Python at $PY. Create it: cd Tools/course_prep && python3 -m venv .venv && .venv/bin/pip install -r requirements.txt" >&2
  exit 1
fi
"$PY" -c "import fastapi, uvicorn, psycopg" 2>/dev/null || "$PY" -m pip install -q -r "$HERE/requirements.txt"
export TRAINER_DATABASE_URL="${TRAINER_DATABASE_URL:-postgresql://trainer:trainer@127.0.0.1:5433/trainer}"

if [ ! -d "$WEB/node_modules" ]; then
  (cd "$WEB" && npm install --no-fund --no-audit)
fi
if [ ! -f "$WEB/dist/index.html" ] || [ -n "$(find "$WEB/src" "$WEB/index.html" "$WEB/package.json" -newer "$WEB/dist/index.html" -print -quit)" ]; then
  (cd "$WEB" && npm run build)
fi

cd "$HERE"
exec "$PY" server.py "$@"
