"""Makes Tools/course_gen (and through it Tools/course_prep) importable; the trainer reuses them directly."""
import sys
from pathlib import Path

TRAINER_DIR = Path(__file__).resolve().parent
GEN_DIR = TRAINER_DIR.parent / "course_gen"
WEB_DIST = TRAINER_DIR / "web" / "dist"

if str(GEN_DIR) not in sys.path:
    sys.path.insert(0, str(GEN_DIR))

import _prep  # noqa: E402,F401  (course_gen's own path shim: adds course_prep)
