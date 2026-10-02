"""Makes the sibling course_prep modules importable (they use flat imports, like this package)."""
import sys
from pathlib import Path

TOOLS_DIR = Path(__file__).resolve().parents[1]
PROJECT_ROOT = TOOLS_DIR.parent
PREP_DIR = TOOLS_DIR / "course_prep"
GEN_DIR = Path(__file__).resolve().parent
DATA_DIR = GEN_DIR / "data"

if str(PREP_DIR) not in sys.path:
    sys.path.insert(0, str(PREP_DIR))
