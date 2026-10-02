import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _prep  # noqa: E402,F401  (also puts course_prep on the path)
