"""Learn what the user likes from thumbs up / down, and steer generation toward it.

Model: Bayesian logistic regression over style features (Laplace approximation), trained on an
append-only ratings log. Features are shared across presets *and* duplicated per preset
("feature augmentation"), so global taste ("I like trees") transfers between presets while each
preset can still learn its own quirks. Generation uses Thompson sampling: draw one plausible
weight vector from the posterior, score a batch of candidate styles, keep the best. That explores
naturally while ratings are scarce and exploits once the model is confident. With no ratings the
model is skipped and styles are sampled straight from the preset. Quick-feedback tags on a vote
("more trees") add weighted paired-comparison rows to the same fit (see feedback.py).
"""
from __future__ import annotations

import json
import time
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from _prep import DATA_DIR
from feedback import TAG_WEIGHT, comparisons, validate_tags
from style import PARAM_NAMES, PARS, PRESETS, Style

RATINGS_PATH = DATA_DIR / "ratings.jsonl"
MODEL_PATH = DATA_DIR / "preference_model.npz"
PRIOR_STD_SHARED = 1.5
PRIOR_STD_PRESET = 0.8
CANDIDATES = 48
EXPLORE = 0.1          # chance of ignoring the model entirely (keeps variety)


# ---- features ---------------------------------------------------------------------------------
def base_features(style: Style) -> np.ndarray:
    v = np.array([style.values[n] for n in PARAM_NAMES])
    curvature = 4 * (v - 0.5) ** 2                     # lets the model like "medium", not just "more"
    par = np.array([style.par == p for p in PARS], dtype=float)
    return np.concatenate([[1.0], v - 0.5, curvature - 1 / 3, par])


BASE_DIM = 1 + 2 * len(PARAM_NAMES) + len(PARS)
PRESET_NAMES = list(PRESETS)


def features(style: Style) -> np.ndarray:
    base = base_features(style)
    blocks = [base] + [base if style.preset == p else np.zeros_like(base) for p in PRESET_NAMES]
    return np.concatenate(blocks)


def prior_precision() -> np.ndarray:
    std = np.concatenate([np.full(BASE_DIM, PRIOR_STD_SHARED)] + [np.full(BASE_DIM, PRIOR_STD_PRESET)] * len(PRESET_NAMES))
    return 1.0 / std ** 2


# ---- ratings log (paths default to RATINGS_PATH / MODEL_PATH at call time so tools and tests can redirect) ----
def record_rating(gen_info: dict, rating: int, path: Path | None = None, *,
                  comment: str | None = None, tags=None) -> dict:
    """Append one vote. Optional free-text `comment` and quick-feedback `tags` (see feedback.py) are
    only written when given, so older readers and lines without them keep working."""
    path = path or RATINGS_PATH
    path.parent.mkdir(parents=True, exist_ok=True)
    entry = {"id": gen_info["id"], "rating": int(np.sign(rating)), "time": time.time(),
             "style": {k: gen_info[k] for k in ("preset", "theme", "par", "params")}}
    tags = validate_tags(tags)
    if tags:
        entry["tags"] = tags
    if comment and comment.strip():
        entry["comment"] = comment.strip()
    with path.open("a") as f:
        f.write(json.dumps(entry) + "\n")
    return entry


def load_ratings(path: Path | None = None) -> list[dict]:
    """Latest rating per hole id (re-rating a hole replaces the earlier vote)."""
    path = path or RATINGS_PATH
    if not path.exists():
        return []
    latest = {}
    for line in path.read_text().splitlines():
        if line.strip():
            entry = json.loads(line)
            latest[entry["id"]] = entry
    return list(latest.values())


def rated_ids(path: Path | None = None) -> set[str]:
    return {r["id"] for r in load_ratings(path)}


# ---- model -------------------------------------------------------------------------------------
@dataclass
class PreferenceModel:
    mean: np.ndarray
    cov: np.ndarray
    n_ratings: int
    preset_counts: dict[str, int]

    def score(self, style: Style) -> float:
        return float(features(style) @ self.mean)

    def probability(self, style: Style) -> float:
        return float(1 / (1 + np.exp(-self.score(style))))

    def draw(self, rng: np.random.Generator) -> np.ndarray:
        return rng.multivariate_normal(self.mean, self.cov, method="cholesky")

    def save(self, path: Path | None = None) -> None:
        path = path or MODEL_PATH
        path.parent.mkdir(parents=True, exist_ok=True)
        np.savez(path, mean=self.mean, cov=self.cov, n=self.n_ratings, presets=np.array(PRESET_NAMES), dim=BASE_DIM,
                 counts=np.array([self.preset_counts.get(p, 0) for p in PRESET_NAMES]))

    @staticmethod
    def load(path: Path | None = None) -> "PreferenceModel | None":
        path = path or MODEL_PATH
        if not path.exists():
            return None
        data = np.load(path)
        if list(data["presets"]) != PRESET_NAMES or int(data["dim"]) != BASE_DIM:
            return None  # style space changed since training; retrain
        counts = dict(zip(PRESET_NAMES, map(int, data["counts"])))
        return PreferenceModel(data["mean"], data["cov"], int(data["n"]), counts)


def observations(ratings: list[dict]) -> tuple[np.ndarray, np.ndarray, np.ndarray]:
    """Design matrix, labels and weights: one row per vote plus one paired comparison per feedback tag."""
    rows, labels, weights = [], [], []
    for r in ratings:
        style = Style.from_json(r["style"])
        rows.append(features(style))
        labels.append(float(r["rating"] > 0))
        weights.append(1.0)
        for better, rated in comparisons(style, r.get("tags")):
            rows.append(features(better) - features(rated))
            labels.append(1.0)
            weights.append(TAG_WEIGHT)
    dim = len(prior_precision())
    return np.array(rows).reshape(len(rows), dim), np.array(labels), np.array(weights)


def train(ratings: list[dict], iterations: int = 30) -> PreferenceModel:
    """Weighted MAP fit by Newton's method; covariance from the Hessian at the optimum (Laplace)."""
    X, y, s = observations(ratings)
    precision = prior_precision()
    w = np.zeros(len(precision))
    for _ in range(iterations):
        p = 1 / (1 + np.exp(-(X @ w)))
        grad = X.T @ (s * (p - y)) + precision * w
        hess = (X * (s * p * (1 - p))[:, None]).T @ X + np.diag(precision)
        step = np.linalg.solve(hess, grad)
        w -= step
        if np.abs(step).max() < 1e-6:
            break
    p = 1 / (1 + np.exp(-(X @ w)))
    hess = (X * (s * p * (1 - p))[:, None]).T @ X + np.diag(precision)
    cov = np.linalg.inv(hess)
    counts = {p: sum(r["style"]["preset"] == p for r in ratings) for p in PRESET_NAMES}
    return PreferenceModel(w, (cov + cov.T) / 2, len(ratings), counts)


def choose_style(preset_name: str, rng: np.random.Generator, model: PreferenceModel | None,
                 adjust=None) -> tuple[Style, float | None]:
    """Sample candidate styles from the preset and pick one by Thompson sampling.

    `adjust(style)` applies user overrides (sliders) to every candidate before scoring.
    Returns the style and the model's predicted like-probability (None when the model was not used).
    """
    preset = PRESETS[preset_name]
    adjust = adjust or (lambda s: s)
    if model is None or model.n_ratings == 0 or rng.random() < EXPLORE:
        return adjust(preset.sample(rng)), None
    candidates = [adjust(preset.sample(rng)) for _ in range(CANDIDATES)]
    w = model.draw(rng)
    best = max(candidates, key=lambda s: features(s) @ w)
    return best, model.probability(best)


def summary(model: PreferenceModel | None, preset: str | None = None, top: int = 4) -> dict:
    """Human-readable taste for one preset (default: the most-rated one): clear likes and dislikes."""
    if model is None:
        return {"ratings": 0, "preset": None, "likes": [], "dislikes": []}
    preset = preset or max(model.preset_counts, key=model.preset_counts.get)
    shared = np.arange(1, 1 + len(PARAM_NAMES))
    own = shared + BASE_DIM * (1 + PRESET_NAMES.index(preset))
    # Effect for this preset = shared + preset-specific weight; z = mean / std of that sum.
    mean = model.mean[shared] + model.mean[own]
    var = np.diag(model.cov)[shared] + np.diag(model.cov)[own] + 2 * model.cov[shared, own]
    z = mean / np.sqrt(var)
    order = np.argsort(z)
    return {
        "ratings": model.n_ratings,
        "preset": preset,
        "likes": [f"more {PARAM_NAMES[i]}" for i in order[::-1][:top] if z[i] > 1.5],
        "dislikes": [f"more {PARAM_NAMES[i]}" for i in order[:top] if z[i] < -1.5],
    }
