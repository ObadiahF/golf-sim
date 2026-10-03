#!/bin/bash
# Builds the game and publishes it as a self-update release: publish.sh windows|macos [note] [--server URL]
# [--skip-build]. See publish.py and Game-server/docs/UPDATES.md.
exec python3 "$(dirname "$0")/publish.py" "$@"
