#!/usr/bin/env python3
"""Generate unlimited golf holes and learn from thumbs up / down.

  python gen_hole.py presets [--json]
  python gen_hole.py generate --preset forest [--seed 42] [--set tree_density=0.9 water=0] [--par 4]
  python gen_hole.py rate <package_dir> up|down [--comment "text"] [--tag more_trees too_long] [--user NAME]
  python gen_hole.py train [--ratings votes.jsonl] [--user NAME]
  python gen_hole.py status [--json] [--ratings votes.jsonl]

`generate` prints one JSON line last ({"id", "package", ...}) for the Unity editor to parse.
Votes come from data/ratings.jsonl, or from the shared trainer's Postgres when TRAINER_DATABASE_URL is set
(rating_store.py); `--ratings FILE` reads a ratings file instead (e.g. an export from the trainer).
"""
from __future__ import annotations

import argparse
import json
import random
import shutil
import time
from pathlib import Path

import numpy as np

import _prep  # noqa: F401
from _prep import PROJECT_ROOT
from generate import DEFAULT_SPACING, GEN_FILE, read_generator_info, write_package
from feedback import TAGS
from preference import MODEL_PATH, PreferenceModel, choose_style, summary, train
from rating_store import RatingStore, configured_store
from style import PARAMS, PRESETS, apply_overrides

DEFAULT_OUT = PROJECT_ROOT / "Assets" / "CourseData" / "generated"
KEEP_UNRATED = 12


def parse_overrides(items: list[str]) -> dict[str, float]:
    out = {}
    for item in items or []:
        name, _, value = item.partition("=")
        out[name.strip()] = float(value)
    return out


def prune_unrated(out_root: Path, keep: int, protect: Path, rated: set[str] | None = None,
                  grace_seconds: float = 0.0) -> list[str]:
    """Delete the oldest unrated generated holes beyond `keep` (rated ones are kept as training history).
    `rated`: ids never to delete (default: every hole with a vote in the configured store). Holes created within
    `grace_seconds` are kept too, so a shared server never deletes a hole someone has only just opened."""
    rated = configured_store().rated_ids() if rated is None else rated
    fresh = time.time() - grace_seconds
    unrated = [d for d in out_root.iterdir() if (d / GEN_FILE).exists() and d.name not in rated and d != protect]
    unrated.sort(key=lambda d: d.stat().st_mtime, reverse=True)
    removed = []
    for d in unrated[max(0, keep - 1):]:
        if grace_seconds and d.stat().st_mtime > fresh:
            continue
        shutil.rmtree(d)
        meta = d.with_name(d.name + ".meta")  # Unity's folder meta
        if meta.exists():
            meta.unlink()
        removed.append(d.name)
    return removed


def presets_info() -> dict:
    return {"presets": [{"name": p.name, "label": p.label, "theme": p.theme} for p in PRESETS.values()],
            "params": PARAMS}


def cmd_presets(args):
    if args.json:
        print(json.dumps(presets_info()))
        return
    for p in PRESETS.values():
        print(f"{p.name:10} {p.label:15} theme={p.theme}")


def generate_hole(preset: str, par: int | None = None, seed: int | None = None, overrides: dict | None = None,
                  out_root: Path = DEFAULT_OUT, spacing: float = DEFAULT_SPACING,
                  keep_unrated: int | None = KEEP_UNRATED, use_model: bool = True) -> tuple[dict, list[str]]:
    """Generate one package (steered by the learned taste) and prune old unrated ones (`keep_unrated=None`:
    the caller prunes). Returns (result, pruned ids); `result` is the JSON line the CLI prints for Unity."""
    seed = seed if seed is not None else random.randrange(1_000_000)
    rng = np.random.default_rng(seed)
    model = PreferenceModel.load() if use_model else None
    style, liked = choose_style(preset, rng, model, lambda s: apply_overrides(s, overrides or {}, par))
    out_root = Path(out_root)
    package = write_package(style, seed, out_root, spacing,
                            extra={"modelScore": None if liked is None else round(liked, 3)})
    removed = [] if keep_unrated is None else prune_unrated(out_root, keep_unrated, package)
    info = read_generator_info(package)
    return {"id": info["id"], "package": str(package), "par": style.par, "preset": style.preset,
            "theme": style.theme, "seed": seed, "modelScore": info["modelScore"],
            "attempts": info["attempts"]}, removed


def rate_package(package: Path, rating: str, comment: str | None = None, tags=None,
                 store: RatingStore | None = None, user: str | None = None) -> dict:
    """Record 'up' / 'down' (plus optional comment and feedback tags) for a generated package."""
    info = read_generator_info(Path(package))
    return (store or configured_store()).record(info, {"up": 1, "down": -1}[rating], comment=comment, tags=tags,
                                                user=user)


def train_and_save(store: RatingStore | None = None, user: str | None = None) -> dict | None:
    """Retrain from every vote in the store (or only `user`'s) and save the model. None when there are no votes."""
    ratings = (store or configured_store()).load(user)
    if not ratings:
        return None
    model = train(ratings)
    model.save()
    return summary(model)


def status(preset: str | None = None, store: RatingStore | None = None) -> dict:
    ratings = (store or configured_store()).load()
    s = summary(PreferenceModel.load(), preset)
    return {"ratings": len(ratings), "up": sum(r["rating"] > 0 for r in ratings),
            "trainedOn": s["ratings"], "preset": s["preset"], "likes": s["likes"], "dislikes": s["dislikes"]}


def cmd_generate(args):
    result, removed = generate_hole(args.preset, args.par, args.seed, parse_overrides(args.set), Path(args.out),
                                    args.spacing, args.keep_unrated, not args.no_model)
    print(f"Generated {result['id']} (par {result['par']}, {result.pop('attempts')} layout attempt(s))")
    if removed:
        print(f"Pruned {len(removed)} old unrated hole(s)")
    print(json.dumps(result))


def cmd_rate(args):
    store = configured_store()
    entry = rate_package(Path(args.package), args.rating, args.comment, args.tag, store, args.user)
    print(f"Recorded {args.rating} for {entry['id']} ({len(store.load())} rated holes)")


def cmd_train(args):
    s = train_and_save(configured_store(args.ratings), args.user)
    if s is None:
        print("No ratings yet: rate some generated holes first.")
        return
    print(f"Trained on {s['ratings']} ratings -> {MODEL_PATH}")
    print("  likes: " + (", ".join(s["likes"]) or "(nothing clear yet)"))
    print("  dislikes: " + (", ".join(s["dislikes"]) or "(nothing clear yet)"))


def cmd_status(args):
    st = status(store=configured_store(args.ratings))
    print(json.dumps(st) if args.json else
          f"{st['ratings']} ratings ({st['up']} up); model trained on {st['trainedOn']}\n"
          f"  likes: {', '.join(st['likes']) or '-'}\n  dislikes: {', '.join(st['dislikes']) or '-'}")


def main():
    p = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    sub = p.add_subparsers(dest="cmd", required=True)

    ps = sub.add_parser("presets", help="List style presets")
    ps.add_argument("--json", action="store_true")
    ps.set_defaults(func=cmd_presets)

    g = sub.add_parser("generate", help="Generate a hole package")
    g.add_argument("--preset", default="parkland", choices=list(PRESETS))
    g.add_argument("--seed", type=int)
    g.add_argument("--par", type=int, choices=[3, 4, 5])
    g.add_argument("--set", nargs="*", metavar="PARAM=0..1", help=f"pin parameters: {', '.join(PARAMS)}")
    g.add_argument("--no-model", action="store_true", help="ignore learned preferences")
    g.add_argument("--out", default=str(DEFAULT_OUT))
    g.add_argument("--spacing", type=float, default=DEFAULT_SPACING, help="max meters per heightmap sample")
    g.add_argument("--keep-unrated", type=int, default=KEEP_UNRATED)
    g.set_defaults(func=cmd_generate)

    r = sub.add_parser("rate", help="Thumbs up / down for a generated hole")
    r.add_argument("package", help="generated hole folder (contains gen.json)")
    r.add_argument("rating", choices=["up", "down"])
    r.add_argument("--comment", help="free-text feedback stored with the vote")
    r.add_argument("--tag", nargs="*", choices=list(TAGS), help="quick-feedback tags (nudge training)")
    r.add_argument("--user", help="who voted (required when votes go to Postgres)")
    r.set_defaults(func=cmd_rate)

    t = sub.add_parser("train", help="Retrain the preference model from all ratings")
    t.add_argument("--ratings", help="train from this ratings file (e.g. the trainer's votes.jsonl export)")
    t.add_argument("--user", help="only this user's votes")
    t.set_defaults(func=cmd_train)

    st = sub.add_parser("status", help="Ratings count and learned taste")
    st.add_argument("--json", action="store_true")
    st.add_argument("--ratings", help="count votes in this ratings file instead")
    st.set_defaults(func=cmd_status)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
