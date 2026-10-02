import numpy as np

from preference import (PreferenceModel, choose_style, features, load_ratings, record_rating, summary, train)
from style import PRESETS


def simulated_user(style, rng):
    """Likes woods, dislikes water."""
    logit = 5 * (style.tree_density - 0.5) - 5 * (style.water - 0.5)
    return 1 if rng.random() < 1 / (1 + np.exp(-logit)) else -1


def rate_samples(n, preset="parkland", seed=0):
    rng = np.random.default_rng(seed)
    ratings = []
    for i in range(n):
        style = PRESETS[preset].sample(rng)
        # Wider than the preset so the user sees the full range of trees and water.
        style.values["tree_density"], style.values["water"] = rng.random(), rng.random()
        ratings.append({"id": f"h{i}", "rating": simulated_user(style, rng), "style": style.to_json()})
    return ratings


def mean_choice(model, preset, n=300, seed=1):
    rng = np.random.default_rng(seed)
    chosen = [choose_style(preset, rng, model)[0] for _ in range(n)]
    return np.mean([s.tree_density for s in chosen]), np.mean([s.water for s in chosen])


def test_no_ratings_means_plain_sampling():
    style, score = choose_style("forest", np.random.default_rng(4), None)
    assert score is None
    assert style.values == PRESETS["forest"].sample(np.random.default_rng(4)).values
    assert features(style).ndim == 1


def test_model_learns_the_simulated_taste():
    s = summary(train(rate_samples(150)))
    assert s["preset"] == "parkland"
    assert "more tree_density" in s["likes"] and "more water" in s["dislikes"]


def test_ratings_shift_sampling_toward_liked_holes():
    model = train(rate_samples(60))
    plain_trees, plain_water = mean_choice(None, "parkland")
    guided_trees, guided_water = mean_choice(model, "parkland")
    assert guided_trees > plain_trees + 0.05
    assert guided_water < plain_water - 0.05


def test_shared_taste_transfers_to_unrated_presets():
    model = train(rate_samples(60, preset="parkland"))
    plain_trees, _ = mean_choice(None, "lakes")
    guided_trees, _ = mean_choice(model, "lakes")
    assert guided_trees > plain_trees


def test_ratings_log_keeps_latest_vote(tmp_path):
    path = tmp_path / "ratings.jsonl"
    info = {"id": "x", **PRESETS["links"].sample(np.random.default_rng(0)).to_json()}
    record_rating(info, 1, path)
    record_rating(info, -1, path)
    ratings = load_ratings(path)
    assert len(ratings) == 1 and ratings[0]["rating"] == -1


def test_model_round_trips(tmp_path):
    model = train(rate_samples(10))
    path = tmp_path / "m.npz"
    model.save(path)
    loaded = PreferenceModel.load(path)
    assert loaded.n_ratings == 10 and np.allclose(loaded.mean, model.mean)


def test_no_preset_picks_among_all_presets():
    rng = np.random.default_rng(5)
    assert len({choose_style(None, rng, None)[0].preset for _ in range(60)}) == len(PRESETS)
    model = train(rate_samples(60))
    style, score = choose_style(None, np.random.default_rng(6), model)
    assert style.preset in PRESETS and (score is None or 0 < score < 1)
