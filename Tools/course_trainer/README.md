# course_trainer

A browser front end for training `course_gen`'s taste model. Walk around each generated hole in 3D
(Minecraft creative-mode controls), rate it 👍 / 👎, add quick-feedback chips and a note, and the next
hole generates automatically. Ratings go to the same `Tools/course_gen/data/ratings.jsonl` the Unity
Course Generator window uses, and holes are written to the same `Assets/CourseData/generated/` folder,
so a hole you like can be opened in Unity straight away.

## Run it

```sh
Tools/course_trainer/run.sh            # then open http://127.0.0.1:8765/   (add --open to launch the browser)
```

`run.sh` installs the Python web deps into `Tools/course_prep/.venv` if missing (`fastapi`, `uvicorn`;
listed in `Tools/course_prep/requirements.txt`), runs `npm install` the first time, rebuilds the UI
when its sources changed, and starts the server. Needs Node 18+ and the course_prep venv
(`cd Tools/course_prep && python3 -m venv .venv && .venv/bin/pip install -r requirements.txt`).

Server options: `--port 8765`, `--host 127.0.0.1`, `--out <dir>` (where packages are written).

### UI development

```sh
cd Tools/course_trainer
../course_prep/.venv/bin/python server.py      # API on :8765
cd web && npm run dev                          # Vite on :5173, proxies /api to :8765 (TRAINER_API overrides)
npm run typecheck                              # / npm run build
```

### Tests

```sh
cd Tools/course_trainer && ../course_prep/.venv/bin/python -m pytest tests
cd Tools/course_gen && ../course_prep/.venv/bin/python -m pytest tests
```

## Controls

| Key | Action |
|---|---|
| Click the view / Esc | capture / release the mouse |
| Mouse | look |
| W A S D | move horizontally, relative to where you face (pitch never changes altitude) |
| Space / Shift | fly up / down |
| Space twice | toggle flying ↔ walking (walking has gravity, follows the ground, Space jumps) |
| Ctrl, or W twice | sprint |
| Scroll wheel | flying speed (2 – 220 m/s) |
| T / G / O | teleport to the tee · the green approach · an overhead view |
| 1 / 2 | 👍 / 👎 |
| F | jump to the feedback box (typing never moves you or triggers hotkeys) |
| Enter (Ctrl/⌘+Enter in the box) | submit and generate the next hole |
| N | skip: new hole without rating |
| H | controls overlay |

You start hovering behind the tee, looking down the hole. The camera never goes below the terrain
(or below a pond's surface). On Windows/Linux Ctrl+W would close the tab, so while the mouse is
captured the page asks before unloading.

## Architecture

```
course_trainer/
  run.sh            one-command launcher
  server.py         CLI entry: uvicorn + the built UI
  api.py            FastAPI app (create_app) - thin layer over course_gen
  holes.py          hole packages on disk: safe lookup, listing, per-hole summary
  _paths.py         puts course_gen / course_prep on sys.path
  tests/test_api.py
  web/              Vite + React + TypeScript + three.js (@react-three/fiber, drei)
    src/api.ts              typed API client + hole.json types
    src/useTrainer.ts       app state: load / generate / rate / retrain
    src/hole/               package decoding: heightField (RAW16, row 0 = south), geometry (polygons,
                            hole line), surfaceTexture (areas -> 4096² canvas: mowing stripes, collars,
                            bunker lips, grain), treeScatter (specimen trees + woods/scrub fill, seeded),
                            theme (per-theme palette, tree mix, sky), views (start/tee/green/overhead)
    src/scene/              HoleScene (sky, IBL from the sky, sun + shadows, fog), Terrain (decimated
                            400² mesh + apron fading to the horizon), Trees (instanced low-poly),
                            Water (flat bodies at their level, ripple normals), Markers (flag, cup, tees)
    src/controls/           Player state + PlayerController (pointer lock, fly / walk physics)
    src/hud/                HolePanel, RatePanel, TastePanel, Minimap, Help, Hud layout
```

Everything is reused from `course_gen` rather than re-implemented: `gen_hole.generate_hole` (style
sampling with Thompson sampling, writing, pruning old unrated holes), `gen_hole.rate_package`,
`gen_hole.train_and_save`, `gen_hole.status`, `feedback.catalog`. The Unity window keeps calling
`gen_hole.py` exactly as before.

Coordinates: package `x` = east, `z` = north (metres from the SW corner); three.js uses `X = x`,
`Y = up` (metres above `minElevation`), `Z = -north`. Heights are `u16 / 65535 * (max - min)`.

## API

| Method | Path | Body / query | Returns |
|---|---|---|---|
| GET | `/api/presets` | | `{presets, params, pars, feedback}` (feedback = chip table) |
| GET | `/api/status` | `?preset=` | `{ratings, up, trainedOn, preset, likes, dislikes}` |
| GET | `/api/holes` | `?limit=20` | `{holes: [summary]}` newest first |
| GET | `/api/holes/{id}` | | summary: `id, preset, theme, par, seed, params, modelScore, lengthMeters, created, rating` |
| GET | `/api/holes/{id}/{file}` | `hole.json`, `heightmap.raw`, `gen.json`, `preview.png` | package file |
| POST | `/api/generate` | `{preset, par?, seed?, overrides?: {knob: 0..1}, useModel?: true}` | summary + `attempts`, `pruned` |
| POST | `/api/rate` | `{id, rating: "up"\|"down", comment?, tags?: [chip id]}` | `{entry, status}` |
| POST | `/api/train` | `?preset=` | status (409 when there are no ratings) |

## Feedback chips and training

Chips live in one table, `Tools/course_gen/feedback.py` (`TAGS`): each maps to knobs and a
direction (`more_trees` → `tree_density +1`, `too_long` → `length −1`, `boring` → `dogleg +1, relief +1`,
…). They are stored on the rating line (`"tags": [...]`, plus `"comment"` for the note; both optional,
so older lines and the Unity window are unaffected) and **used in training**: a tag on a rated hole
becomes a paired comparison "this hole with that knob nudged by 0.2 would be better", i.e. one extra
logistic observation on the feature difference `f(nudged) − f(rated)` labelled 1, weighted 0.5
(Bradley–Terry). The bias and all other knobs cancel, so a chip only teaches its own knob, globally
and for the hole's preset, and it works on 👍 and 👎 alike. Chips at a knob's limit add nothing. The
CLI accepts them too: `gen_hole.py rate <pkg> up --comment "..." --tag more_trees too_long`.

Free-text comments are stored only (they are for you, or a future text model).
