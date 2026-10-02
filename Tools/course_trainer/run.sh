#!/usr/bin/env bash
# One command: install web deps if missing, rebuild the UI if its sources changed, start the server.
#   Tools/course_trainer/run.sh [--port 8765] [--open] [--out DIR]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
PY="${PYTHON:-$HERE/../course_prep/.venv/bin/python}"
WEB="$HERE/web"

if [ ! -x "$PY" ]; then
  echo "No Python at $PY. Create it: cd Tools/course_prep && python3 -m venv .venv && .venv/bin/pip install -r requirements.txt" >&2
  exit 1
fi
"$PY" -c "import fastapi, uvicorn" 2>/dev/null || "$PY" -m pip install -q -r "$HERE/../course_prep/requirements.txt"

if [ ! -d "$WEB/node_modules" ]; then
  (cd "$WEB" && npm install --no-fund --no-audit)
fi
if [ ! -f "$WEB/dist/index.html" ] || [ -n "$(find "$WEB/src" "$WEB/index.html" "$WEB/package.json" -newer "$WEB/dist/index.html" -print -quit)" ]; then
  (cd "$WEB" && npm run build)
fi

cd "$HERE"
exec "$PY" server.py "$@"
