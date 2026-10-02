# course_gen

Generates unlimited golf holes in a chosen style and learns your taste from 👍 / 👎.
It writes the **same hole package format** as `course_prep` (real OSM holes), so Unity's
Course Builder builds generated and real holes with one pipeline.

## Use it from Unity

**Golf > Course Generator**
1. Pick a preset (Parkland, Forest, Water / Lakes, Links, Desert, Mountain) and optionally a par.
2. Optional: open *Customize* and pin knobs (trees, water, bunkers, relief, dogleg ...).
3. **Generate Random Hole** creates the package under `Assets/CourseData/generated/<id>/` and builds it in the scene, dressed by the preset's theme.
4. Rate it 👍 / 👎. Press **Retrain From Ratings** now and then. With *Use learned taste* on, generation steers toward what you liked.

The window uses `Tools/course_prep/.venv/bin/python` by default (set another path in the window if needed).

## Use it from the command line

```sh
cd Tools/course_gen
PY=../course_prep/.venv/bin/python
$PY gen_hole.py presets
$PY gen_hole.py generate --preset forest --par 4 --set water=0 tree_density=0.95
$PY gen_hole.py rate ../../Assets/CourseData/generated/<id> up
$PY gen_hole.py train
$PY gen_hole.py status
$PY gallery.py --out /tmp/gallery          # 3 holes per preset on one PNG
$PY fetch_courses.py                       # fetch reference courses + refit priors
$PY -m pytest tests
```

## How it works

| Module | Role |
|---|---|
| `style.py` | 12 normalised knobs (0..1) and presets as distributions over them; each preset names a Unity theme |
| `priors.py` | real-course stats (length by par, fairway width, green size, bunkers) fitted from cached OSM data in `CourseSources/` → `data/priors.json` |
| `layout.py`, `shapes.py` | route (doglegs, landing zones), tees, green, fairway, bunkers, water (front carry, lateral lake, creek, pond), rough, then woods/scrub/trees |
| `validate.py` | rejects unplayable layouts (water on green/tees, long carries, hazards in landing zones, unreachable greens); the generator retries |
| `terrain.py`, `noise.py` | fractal noise relief + course shaping (smoothed corridor, raised green/tee pads, dug bunkers, pond beds with banks) |
| `generate.py` | writes `hole.json`, `heightmap.raw`, `gen.json`, `preview.png` (reuses `course_prep` water carving, RAW writer, preview) |
| `preference.py` | Bayesian logistic regression on style features, shared + per-preset; Thompson sampling over 48 candidates; 10% pure exploration; no model with no ratings |
| `gen_hole.py` | CLI used by Unity; prunes old unrated packages (keeps 12) |

Data: `data/ratings.jsonl` (append-only, latest vote per hole wins, commit it to keep your taste)
and `data/preference_model.npz` (derived, gitignored). Generated packages are gitignored.

## Adding assets (Unity side)

Themes pick assets by **category + tags** from the Asset Catalog, never by direct reference:

1. Select prefabs, TerrainLayers, water materials or whole folders in the Project window.
2. **Golf > Catalog > Add Selected Assets** auto-categorises (Tree, Shrub, Rock, GroundCover, GroundLayer, WaterMaterial) and auto-tags from names (conifer, deciduous, palm, cactus, grass, dry, fern, heather, rock + large/small ...).
3. Refine tags on the entries in `Assets/CourseBuilder/Settings/AssetCatalog.asset` (e.g. add `desert` to a cactus pack). Ground layers need their surface name as a tag (`fairway`, `rough`, `bunker`, `native` ...).

Every theme whose queries match picks the asset up on the next Generate. Themes live in
`Assets/CourseBuilder/Settings/Themes/`; `styleTags` rank matches without filtering. Custom water
tech: subclass `WaterProvider` and assign it to a theme.
