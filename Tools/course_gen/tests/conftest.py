import sys
from pathlib import Path

import pytest

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
import _prep  # noqa: E402,F401  (also puts course_prep on the path)


@pytest.fixture
def isolated_data(tmp_path, monkeypatch):
    """Point the ratings log and model file at a temp dir so tests never touch data/."""
    import preference
    monkeypatch.setattr(preference, "RATINGS_PATH", tmp_path / "ratings.jsonl")
    monkeypatch.setattr(preference, "MODEL_PATH", tmp_path / "preference_model.npz")
    return tmp_path
