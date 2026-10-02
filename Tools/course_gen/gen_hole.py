#!/usr/bin/env python3
"""Generate unlimited golf holes and learn from thumbs up / down.

  python gen_hole.py presets [--json]
  python gen_hole.py generate --preset forest [--seed 42] [--set tree_density=0.9 water=0] [--par 4]
  python gen_hole.py rate <package_dir> up|down
  python gen_hole.py train
  python gen_hole.py status [--json]

`generate` prints one JSON line last ({"id", "package", ...}) for the Unity editor to parse.
"""
from __future__ import annotations

import argparse
import json
import random
import shutil
from pathlib import Path

import numpy as np

import _prep  # noqa: F401
from _prep import PROJECT_ROOT
from generate import DEFAULT_SPACING, GEN_FILE, read_generator_info, write_package
from preference import (MODEL_PATH, PreferenceModel, choose_style, load_ratings, rated_ids, record_rating,
                        summary, train)
from style import PARAMS, PRESETS, apply_overrides

DEFAULT_OUT = PROJECT_ROOT / "Assets" / "CourseData" / "generated"
KEEP_UNRATED = 12


def parse_overrides(items: list[str]) -> dict[str, float]:
    out = {}
    for item in items or []:
        name, _, value = item.partition("=")
        out[name.strip()] = float(value)
    return out


def prune_unrated(out_root: Path, keep: int, protect: Path) -> list[str]:
    """Delete the oldest unrated generated holes beyond `keep` (rated ones are kept as training history)."""
    rated = rated_ids()
    unrated = [d for d in out_root.iterdir() if (d / GEN_FILE).exists() and d.name not in rated and d != protect]
    unrated.sort(key=lambda d: d.stat().st_mtime, reverse=True)
    removed = []
    for d in unrated[max(0, keep - 1):]:
        shutil.rmtree(d)
        meta = d.with_name(d.name + ".meta")  # Unity's folder meta
        if meta.exists():
            meta.unlink()
        removed.append(d.name)
    return removed


def cmd_presets(args):
    if args.json:
        print(json.dumps({"presets": [{"name": p.name, "label": p.label, "theme": p.theme} for p in PRESETS.values()],
                          "params": PARAMS}))
        return
    for p in PRESETS.values():
        print(f"{p.name:10} {p.label:15} theme={p.theme}")


def cmd_generate(args):
    seed = args.seed if args.seed is not None else random.randrange(1_000_000)
    rng = np.random.default_rng(seed)
    overrides = parse_overrides(args.set)
    model = None if args.no_model else PreferenceModel.load()
    style, liked = choose_style(args.preset, rng, model, lambda s: apply_overrides(s, overrides, args.par))
    out_root = Path(args.out)
    package = write_package(style, seed, out_root, args.spacing,
                            extra={"modelScore": None if liked is None else round(liked, 3)})
    removed = prune_unrated(out_root, args.keep_unrated, package)
    info = read_generator_info(package)
    print(f"Generated {info['id']} (par {style.par}, {info['attempts']} layout attempt(s))")
    if removed:
        print(f"Pruned {len(removed)} old unrated hole(s)")
    print(json.dumps({"id": info["id"], "package": str(package), "par": style.par, "preset": style.preset,
                      "theme": style.theme, "seed": seed, "modelScore": info["modelScore"]}))


def cmd_rate(args):
    info = read_generator_info(Path(args.package))
    rating = {"up": 1, "down": -1}[args.rating]
    record_rating(info, rating)
    print(f"Recorded {args.rating} for {info['id']} ({len(load_ratings())} rated holes)")


def cmd_train(args):
    ratings = load_ratings()
    if not ratings:
        print("No ratings yet: rate some generated holes first.")
        return
    model = train(ratings)
    model.save()
    s = summary(model)
    print(f"Trained on {s['ratings']} ratings -> {MODEL_PATH}")
    print("  likes: " + (", ".join(s["likes"]) or "(nothing clear yet)"))
    print("  dislikes: " + (", ".join(s["dislikes"]) or "(nothing clear yet)"))


def cmd_status(args):
    ratings = load_ratings()
    s = summary(PreferenceModel.load())
    status = {"ratings": len(ratings), "up": sum(r["rating"] > 0 for r in ratings),
              "trainedOn": s["ratings"], "likes": s["likes"], "dislikes": s["dislikes"]}
    print(json.dumps(status) if args.json else
          f"{status['ratings']} ratings ({status['up']} up); model trained on {status['trainedOn']}\n"
          f"  likes: {', '.join(s['likes']) or '-'}\n  dislikes: {', '.join(s['dislikes']) or '-'}")


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
    r.set_defaults(func=cmd_rate)

    t = sub.add_parser("train", help="Retrain the preference model from all ratings")
    t.set_defaults(func=cmd_train)

    st = sub.add_parser("status", help="Ratings count and learned taste")
    st.add_argument("--json", action="store_true")
    st.set_defaults(func=cmd_status)

    args = p.parse_args()
    args.func(args)


if __name__ == "__main__":
    main()
