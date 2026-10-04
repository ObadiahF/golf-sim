# course_gen

Generates unlimited golf holes in a chosen style and learns your taste from 👍 / 👎.
It writes the **same hole package format** as `course_prep` (real OSM holes), so Unity's
Course Builder builds generated and real holes with one pipeline.

## Use it from Unity

**Golf > Course Generator**
1. Pick a preset (Parkland, Forest, Water / Lakes, Links, Desert, Mountain, and the scenic Autumn Parkland, Tropical Island,
   Red Rock Canyon, Winter, Heathland) and optionally a par. Each names a Unity theme of the same name (ground, models;
   its sky and air are `ThemeScenery.cs`, the time of day the game's, see `Assets/CourseBuilder/Runtime/Scenery`).
2. Optional: open *Customize* and pin knobs (trees, water, bunkers, relief, dogleg ...).
3. **Generate Random Hole** creates the package under `Assets/CourseData/generated/<id>/` and builds it in the scene, dressed by the preset's theme.
4. Rate it 👍 / 👎. Press **Retrain From Ratings** now and then. With *Use learned taste* on, generation steers toward what you liked.

The window uses `Tools/course_prep/.venv/bin/python` by default (set another path in the window if needed).

## Use it from the browser

`Tools/course_trainer/run.sh` serves a 3D walk-through of each generated hole with 👍 / 👎,
quick-feedback chips and notes (see `Tools/course_trainer/README.md`).

## Use it from the command line

```sh
cd Tools/course_gen
PY=../course_prep/.venv/bin/python
$PY gen_hole.py presets
$PY gen_hole.py generate --preset forest --par 4 --set water=0 tree_density=0.95
$PY gen_hole.py rate ../../Assets/CourseData/generated/<id> up [--comment "nice dunes"] [--tag too_long more_trees]
$PY gen_hole.py train                      # [--ratings votes.jsonl] [--user obi]: e.g. the trainer's export
$PY gen_hole.py status
$PY gallery.py --out /tmp/gallery          # 3 holes per preset on one PNG
$PY scan_playability.py <holes_dir>        # list existing packages whose tee shot hits the ground or the trees,
                                           # whose landing zones are too steep or whose green is too steep
                                           # (read-only; scan_launch.py is the old name)
$PY fetch_courses.py                       # fetch reference courses + refit priors
$PY -m pytest tests
```

## How it works

| Module | Role |
|---|---|
| `style.py` | 12 normalised knobs (0..1) and presets as distributions over them; each preset names a Unity theme |
| `priors.py` | real-course stats (length by par, fairway width, green size, bunkers) fitted from cached OSM data in `CourseSources/` → `data/priors.json` |
| `layout.py`, `shapes.py` | route (doglegs, landing zones: the drive lands 215-250 m out; par 5s lay up 130-230 m and bend at the first corner, the second, both or neither), tees, green, fairway, bunkers, water (front carry, lateral lake, creek, pond), rough, then woods/scrub/trees, never in the shot cones (`shot_zone`: 12° either side of the tee shot, 8° of each later shot) |
| `validate.py` | rejects unplayable layouts (water on green/tees, long carries, hazards in landing zones, unreachable greens) and, after sculpting, tee shots that hit the ground ahead (`launch_problems`) greens too steep to putt (`green_problems`: over 4 % within 2 m of the pin or 6 % anywhere 1 m inside the edge) and landing zones on a slope (`landing_problems`: over 8 % across the line or 10 % along it); the generator retries. After planting, `tree_line_problems` (60 % of the tee-shot lines 8° either side, and one line of each later shot, must miss every tree crown under a rough ball flight) drops any tree still in the way |
| `grading.py` | grade-limits the line of play from the tee (flat tee deck, gentle first 120 m, 6 % at the landing zones), tilts the corridor back to 8 % across the line (4 % at the landing zones), and levels the tee boxes so they never form terraces. Each cell takes the correction at its projected arc length (interpolated), blurred 5 m, so it leaves no seams |
| `terrain.py` (+ `course_prep/noise.py`) | fractal noise relief + course shaping (smoothed corridor, pond beds with banks, then raised tee/green pads that win over the banks, dug bunkers). Greens: a 2.5 % back-to-front tilt, a gentle roll that fades out near the pin |
| `generate.py` | writes the package per the hole contract (`Docs/hole-format`): `hole.json`, `heightmap.raw`, `objects.bin`, `gen.json`, `preview.png`. Trees, shrubs and rocks are planted by `course_prep/vegetation.py` (`tree_density` scales them), so Unity and the trainer show the same objects |
| `preference.py` | Bayesian logistic regression on style features, shared + per-preset; Thompson sampling over 48 candidates; 10% pure exploration; no model with no ratings |
| `feedback.py` | quick-feedback tags ("more trees", "too long" ...) -> knob + direction; each tag on a vote adds a weighted paired-comparison row to training |
| `rating_store.py`, `pg_store.py` | where votes live: `JsonlStore` (`data/ratings.jsonl`, default) or `PostgresStore` (the shared trainer, when `TRAINER_DATABASE_URL` is set); same entries either way |
| `gen_hole.py` | CLI used by Unity; prunes old unrated packages (keeps 12) |

Data: `data/ratings.jsonl` (append-only, latest vote per hole (per user, for trainer exports) wins, commit it to keep your taste)
and `data/preference_model.npz` (derived, gitignored). Generated packages are gitignored.

## Adding assets (Unity side)

Themes pick assets by **category + tags** from the Asset Catalog, never by direct reference:

1. Select prefabs, TerrainLayers, water materials or whole folders in the Project window.
2. **Golf > Catalog > Add Selected Assets** auto-categorises (Tree, Shrub, Rock, GroundCover, GroundLayer, WaterMaterial) and auto-tags from names (conifer, deciduous, palm, cactus, grass, dry, fern, heather, rock + large/small ...).
3. Refine tags on the entries in `Assets/CourseBuilder/Settings/AssetCatalog.asset` (e.g. add `desert` to a cactus pack). Ground layers need their surface name as a tag (`fairway`, `rough`, `bunker`, `native` ...).

Every theme whose queries match picks the asset up on the next Generate. Themes live in
`Assets/CourseBuilder/Settings/Themes/`; `styleTags` rank matches without filtering. Custom water
tech: subclass `WaterProvider` and assign it to a theme.
