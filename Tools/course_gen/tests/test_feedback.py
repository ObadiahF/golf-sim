import json

import numpy as np
import pytest

from feedback import TAGS, comparisons, nudged, validate_tags
from preference import load_ratings, observations, record_rating, summary, train
from style import PARAM_NAMES, PRESETS


def sample_style(preset="parkland", seed=0):
    return PRESETS[preset].sample(np.random.default_rng(seed))


def test_every_tag_names_real_knobs():
    for tag in TAGS.values():
        assert tag.knobs and set(tag.knobs) <= set(PARAM_NAMES)
        assert set(tag.knobs.values()) <= {-1, 1}


def test_validate_tags_dedupes_and_rejects_unknown():
    assert validate_tags(["more_trees", "too_long", "more_trees"]) == ["more_trees", "too_long"]
    assert validate_tags(None) == []
    with pytest.raises(ValueError):
        validate_tags(["more_lava"])


def test_nudge_moves_only_the_tagged_knob_and_clips():
    style = sample_style()
    style.values["tree_density"] = 0.95
    more = nudged(style, "more_trees")
    assert more.values["tree_density"] == 1.0
    assert all(more.values[k] == style.values[k] for k in PARAM_NAMES if k != "tree_density")
    style.values["tree_density"] = 1.0
    assert comparisons(style, ["more_trees"]) == []  # already at the limit: no signal
    assert len(comparisons(style, ["fewer_trees", "retired_tag"])) == 1


def test_comparison_rows_cancel_bias_and_other_knobs():
    style = sample_style()
    entry = {"id": "a", "rating": 1, "style": style.to_json(), "tags": ["too_long"]}
    X, y, s = observations([entry])
    assert X.shape[0] == 2 and list(y) == [1, 1] and s[1] < s[0]
    diff = X[1]
    assert diff[0] == 0  # bias cancels
    assert np.count_nonzero(diff) == 4  # length linear + curvature, shared + preset block


def test_ratings_log_stores_comment_and_tags_and_reads_old_lines(tmp_path):
    path = tmp_path / "ratings.jsonl"
    old = {"id": "old", "rating": 1, "time": 0, "style": sample_style().to_json()}
    path.write_text(json.dumps(old) + "\n")
    info = {"id": "new", **sample_style(seed=1).to_json()}
    record_rating(info, -1, path, comment="  greens too small  ", tags=["fewer_trees"])
    record_rating({**info, "id": "plain"}, 1, path, comment="   ")
    by_id = {r["id"]: r for r in load_ratings(path)}
    assert by_id["new"]["comment"] == "greens too small" and by_id["new"]["tags"] == ["fewer_trees"]
    assert "comment" not in by_id["plain"] and "tags" not in by_id["plain"]
    train(list(by_id.values()))  # old and new lines train together


def test_tags_alone_teach_the_knob():
    """Coin-flip thumbs carry no signal; consistent 'more trees' tags must still be learned."""
    rng = np.random.default_rng(0)
    ratings = []
    for i in range(80):
        style = sample_style(seed=i)
        style.values["tree_density"] = rng.uniform(0.0, 0.6)
        ratings.append({"id": f"h{i}", "rating": int(rng.choice([-1, 1])), "style": style.to_json(),
                        "tags": ["more_trees"]})
    tagged = summary(train(ratings))
    untagged = summary(train([{k: v for k, v in r.items() if k != "tags"} for r in ratings]))
    assert "more tree_density" in tagged["likes"]
    assert "more tree_density" not in untagged["likes"]
