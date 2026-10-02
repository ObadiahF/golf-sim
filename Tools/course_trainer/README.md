# course_trainer

A browser front end for training `course_gen`'s taste model, shared by several people. Log in, walk around
each hole in 3D (Minecraft creative-mode controls), rate it 👍 / 👎, add quick-feedback chips and a note, and
the next hole from the **shared pool** appears: you never see a hole twice, and friends rate the same holes
in different orders. Every vote lands in Postgres with the voter's name and a snapshot of the hole's style, so
the model learns from everyone's votes, the **Top holes** leaderboard ranks them, and the Unity game downloads
the most-liked ones through a read-only **Game API**.

Live at **https://golf-trainer.obadiahfusco.xyz** (homelab, Docker Compose, Cloudflare Tunnel).

## Deploy on the homelab

Runs on the R730xd (x86_64). **Build on the server itself**: the Mac is ARM, so an image built there is the
wrong architecture.

```sh
git clone <repo> && cd Golf-sim/Tools/course_trainer
cp .env.example .env        # set POSTGRES_PASSWORD and TRAINER_SESSION_SECRET (openssl rand -base64 32)
docker compose up --build -d
docker compose ps           # db and trainer should turn (healthy)
```

**Update** an existing deployment (on the server):

```sh
cd Golf-sim/Tools/course_trainer
git pull && docker compose up --build -d   # migrations (migrations/*.sql) apply on start; volumes are kept
docker compose logs -f trainer             # "applied migration 002_pool.sql", "pool: started batch 1 (100 holes)"
```

New settings since the first deploy (add to `.env`): `TRAINER_MAX_GENERATIONS=8`, `TRAINER_GAME_KEY=$(openssl
rand -hex 24)`, optionally `TRAINER_POOL_BATCH_SIZE` / `TRAINER_POOL_REFILL_AT` / `TRAINER_POOL_MAX_UNRATED` /
`TRAINER_API_DOCS`. Migration `003_sessions.sql` moves sessions server-side, so everyone logs in once after it.

The compose file builds from the repo root (the image needs `Tools/course_gen`, `Tools/course_prep`,
`Tools/course_trainer` and `Docs/hole-format`; `Dockerfile.dockerignore` sends only those). Services:

| Service | What |
|---|---|
| `db` | `postgres:16`, volume `db`, healthcheck, published on `127.0.0.1:5433` only (for psql / local dev) |
| `trainer` | the web UI + API on `:8765` as a non-root user; volumes `holes` (generated packages) and `model` (`preference_model.npz`); restarts unless stopped |
| `test` | profile `test`: pytest against the compose Postgres (`docker compose run --rm test`) |

On every start the trainer applies `migrations/*.sql`, creates the `TRAINER_SEED_USERS` that do not exist yet,
and imports `Tools/course_gen/data/ratings.jsonl` (if it was in the build) as votes by the `legacy` user.
All three steps are idempotent.

### Cloudflare Tunnel

TLS ends at Cloudflare and cloudflared forwards plain HTTP, so in `.env`:

```sh
TRAINER_COOKIE_SECURE=true   # the browser only sees https://, so the session cookie is Secure
TRAINER_TRUST_PROXY=true     # login throttle keys on CF-Connecting-IP (else X-Forwarded-For), not cloudflared's IP
TRAINER_BIND=127.0.0.1       # only cloudflared on this host can reach :8765, so those headers can't be forged
```

The ingress rule in the tunnel's `config.yml`, next to the game server's:

```yaml
ingress:
  - hostname: golf-server.obadiahfusco.xyz
    service: ...                                  # unchanged
  - hostname: golf-trainer.obadiahfusco.xyz
    service: http://localhost:8765
  - service: http_status:404
```

(If cloudflared runs in Docker on the same host, use `http://host.docker.internal:8765` or attach it to the
`course-trainer_default` network and use `http://trainer:8765`, with `TRAINER_BIND=0.0.0.0`.) Nothing depends
on the request scheme, so the `http` hop behind Cloudflare is fine. Cloudflare cuts requests after 100 s, so
pool batches generate in a background worker, never in a request; an ad-hoc Generate takes 1-10 s (queued
behind `TRAINER_MAX_GENERATIONS` others).

### Users

There is no sign-up page. Accounts come from `TRAINER_SEED_USERS` (first start) or:

```sh
docker compose run --rm trainer adduser sam     # prompts for the password (or --password PW)
docker compose run --rm trainer passwd sam      # also logs sam out everywhere
```

Names are case-insensitive. Sessions live in Postgres (`sessions`) and last `TRAINER_SESSION_DAYS` (30); Log out
ends that session on the server, `passwd` ends all of the user's. After 5 failed logins from one IP or for one
name, further tries wait 2, 4, 8 s ... (max 15 min), in memory. The IP and name counters are independent, each
forgets one failure every 3 minutes, and a successful login clears only its own name's counter.

### Backups

Everything that matters is in Postgres (users, votes, training log); holes can be regenerated and the model
retrained. Back up the database:

```sh
docker compose exec -T db pg_dump -U trainer -Fc trainer > trainer-$(date +%F).dump
# restore: docker compose exec -T db pg_restore -U trainer -d trainer --clean < trainer-2026-10-02.dump
```

Or snapshot the `course-trainer_db` volume with the stack stopped. `docker compose down` keeps the volumes;
`down -v` deletes them.

### Settings (`.env`)

| Variable | Default | |
|---|---|---|
| `POSTGRES_PASSWORD` | `trainer` | bundled db's password (used to build `TRAINER_DATABASE_URL`) |
| `TRAINER_DATABASE_URL` | the bundled db | point at another Postgres |
| `TRAINER_SESSION_SECRET` | random per start | signs cookies; set it or everyone is logged out on restart |
| `TRAINER_COOKIE_SECURE` | `false` | `true` behind HTTPS |
| `TRAINER_TRUST_PROXY` | `false` | client IP from `CF-Connecting-IP` / `X-Forwarded-For` |
| `TRAINER_PORT`, `TRAINER_BIND` | `8765`, `0.0.0.0` | published port and interface |
| `TRAINER_SEED_USERS` | | `name:pw,name:pw` |
| `TRAINER_MAX_GENERATIONS` | `2` | concurrent generations (pool worker processes + ad-hoc Generate); the rest queue. `8` on the R730xd |
| `TRAINER_POOL_BATCH_SIZE` | `100` | holes per pool batch |
| `TRAINER_POOL_REFILL_AT` | `0.5` | the next batch starts once anyone has *rated* this share of the newest batch (see Hole pool) |
| `TRAINER_POOL_MAX_UNRATED` | `300` | no new batch while this many pool holes have no vote from anyone |
| `TRAINER_API_DOCS` | `false` | serve `/docs`, `/redoc`, `/openapi.json` (`run.sh` turns it on for local dev) |
| `TRAINER_GAME_KEY` | | bearer key for the Game API (`openssl rand -hex 24`); empty: Game API off |
| `TRAINER_GEN_SPACING` | `0.75` | meters per heightmap sample (bigger: faster, coarser; tests use 3) |
| `TRAINER_KEEP_UNRATED` | `40` | unrated holes kept on disk |
| `TRAINER_VIEW_GRACE_HOURS` | `3` | holes opened or made this recently are never pruned |
| `TRAINER_SESSION_DAYS` | `30` | |

## Hole pool

Everyone rates holes from one shared pool (`pool.py`, filled by `pool_worker.py`):

1. **Batches.** A batch retrains the model on everyone's latest votes (if there are any; recorded as
   `batches.trained_on_votes` and in `training_runs`), then generates `TRAINER_POOL_BATCH_SIZE` holes. Each hole
   has a fixed seed per (batch, position) and lets the model pick preset and style (`choose_style` across all
   presets, Thompson sampling), so each batch leans toward what people liked while still exploring (10% of holes
   ignore the model, and the posterior draw varies per hole).
2. **In the background.** A worker thread fills batches with up to `TRAINER_MAX_GENERATIONS` holes at a time in
   worker processes, sharing the generation semaphore with ad-hoc Generate. Holes join `pool_holes` as they
   finish, so people can start while the batch is still generating. A restart resumes unfinished batches.
3. **Startup.** If there is no batch yet, the first one starts. Batch creation holds a Postgres advisory lock
   and only ever compares against the newest batch, so it is idempotent and race-safe.
4. **Serving** (`GET /api/next`): the next pool hole you have not seen (shown, rated or skipped; ad-hoc holes count
   too, peeked links do not), newest batch first, in your own shuffled order (`md5(hole id : user id)`), so
   friends start on different holes and the leaderboard gets broad coverage. It is recorded as seen right away.
   Nothing unseen ready yet: `state: "generating"` and the UI shows a "Generating new holes… N of M ready" card
   (the rest of the HUD stays usable) and polls every 3 s, until the view unmounts (logout, 401).
5. **Refill** (`pool.refill_due`, checked on `/api/next` and on every rating). The next batch starts when the
   newest batch has **finished** generating (so one batch generates at a time), someone has **rated**
   `TRAINER_POOL_REFILL_AT` (50%) of it **or seen all of it** (skips only count once the batch is used up, so
   skip-spam earns at most one batch per finished batch), and fewer than `TRAINER_POOL_MAX_UNRATED` pool holes
   have no vote from anyone. At the cap (`pool.capped`) the waiting card asks people to rate holes they skipped.
   A finished batch with 0 holes (every generation failed) counts as used up after 10 minutes, so it can't wedge
   the refill. While a batch generates, unseen holes from older batches are served.
6. Pool holes are **never pruned**. **Submit** and **Skip** (N) both go to the next pool hole. The old ad-hoc
   Generate (preset / par) is under "Advanced" on the hole card.

### Leaderboard (Top holes)

`GET /api/top?limit=20` and the **Top holes** button (L) rank every voted hole still on disk by everyone's
latest vote per hole. Score = the **Wilson score lower bound** (95%, z = 1.96) of the like share:

```
n = up + down,  p = up / n
score = (p + z²/2n − z·sqrt(p(1−p)/n + z²/4n²)) / (1 + z²/n)        (0 when n = 0)
```

That is the like share the hole has at least, given how few votes it has: 1 👍 0 👎 scores 0.21, 2 👍 0.34,
4 👍 1 👎 0.38, 9 👍 1 👎 0.60, so one lucky like never beats a hole many people liked (the raw ratio would rank
1/1 = 100% first). Ties: more likes, then id. Opening a leaderboard hole (`hole.json?peek=true`) does not mark
it seen, so it can still be served to you later; rating it does. The same goes for any `?hole=` link that is not
already in your recent holes (a shared link, or reloading on a peeked hole).

### Game API

Read-only, for the Unity game to download and build the top holes at runtime. Every request needs
`Authorization: Bearer <TRAINER_GAME_KEY>` (compared in constant time); without the right key: 401; with
`TRAINER_GAME_KEY` unset: 404. The key opens only these two routes; everything else still needs a login
session. URLs in responses are paths: prefix them with the base URL.

```sh
KEY=...   # TRAINER_GAME_KEY from .env
curl -H "Authorization: Bearer $KEY" https://golf-trainer.obadiahfusco.xyz/api/game/top-holes?limit=9
curl -H "Authorization: Bearer $KEY" -O https://golf-trainer.obadiahfusco.xyz/api/game/holes/<id>/hole.json
```

`GET /api/game/top-holes?limit=9` (1-100, default 9), best first:

```json
{
  "formula": "wilson-95",
  "holes": [
    {
      "rank": 1,
      "id": "lakes_10755824_2eca01",
      "preset": "lakes",
      "theme": "lakes",
      "par": 3,
      "lengthMeters": 187.7,
      "ups": 2,
      "downs": 0,
      "score": 0.3424,
      "previewUrl": "/api/game/holes/lakes_10755824_2eca01/preview.png",
      "files": {
        "gen.json": "/api/game/holes/lakes_10755824_2eca01/gen.json",
        "heightmap.raw": "/api/game/holes/lakes_10755824_2eca01/heightmap.raw",
        "hole.json": "/api/game/holes/lakes_10755824_2eca01/hole.json",
        "objects.bin": "/api/game/holes/lakes_10755824_2eca01/objects.bin",
        "preview.png": "/api/game/holes/lakes_10755824_2eca01/preview.png"
      }
    }
  ]
}
```

`GET /api/game/holes/{id}/{file}`, file one of `hole.json`, `heightmap.raw`, `objects.bin`, `gen.json`,
`preview.png`: the package file as stored ([`Docs/hole-format`](../../Docs/hole-format/README.md), v2).
Save them as `<dir>/<id>/<file>` and the folder is a normal hole package for the Course Builder. Packages are
immutable, so responses carry `Cache-Control: private, max-age=86400, immutable` (private: Cloudflare never
caches a keyed response for others). 404 for unknown holes or other file names.

### `top` command (fallback for the game)

```sh
docker compose run --rm -T trainer top --limit 9                    # print the leaderboard
mkdir -p ~/top-holes
docker compose run --rm -T -v ~/top-holes:/export --user "$(id -u):$(id -g)" \
  trainer top --limit 9 --export /export                             # + copy the top 9 packages
```

`--export DIR` copies each top hole's contract files (the five above, no Unity artefacts) to `DIR/<id>/`; copy
those folders into Unity's `Assets/CourseData/Saved/`. (`--user`: so the files belong to you, not the image's
`trainer` user.)

## Votes, training and fine-tuning

- `votes` is append-only: re-voting adds a row and the `latest_votes` view keeps each person's newest vote per
  hole. Training uses `latest_votes`, so two people's votes on one hole are two observations. Each row snapshots
  the hole's style (preset, theme, par, knobs, generator version, seed, model score) plus comment and tags.
- **Retrain** (or `POST /api/train`) fits the model on everyone's votes; `POST /api/train?only=sam` uses one
  person's. Each run is logged in `training_runs` (who, filter, vote count, likes / dislikes). The model is
  shared: whoever retrains last sets the taste generation steers toward.
- Votes go through `course_gen/rating_store.py`: `JsonlStore` (`data/ratings.jsonl`, used by Unity and the CLI)
  or `PostgresStore` (`pg_store.py`, chosen when `TRAINER_DATABASE_URL` is set). Both produce the same entries,
  so `preference.train()` is unchanged.

**Export** (same lines as `ratings.jsonl`, plus `"user"`):

```sh
docker compose run --rm -T trainer export > votes.jsonl            # latest vote per person and hole
docker compose run --rm -T trainer export --history > all.jsonl    # every vote ever
curl -b cookies.txt https://golf-trainer.obadiahfusco.xyz/api/export/votes.jsonl   # or the API, logged in
```

(`-T`: no TTY, so the file gets plain `\n` line endings.) Then on the dev machine:

```sh
cd Tools/course_gen
../course_prep/.venv/bin/python gen_hole.py train --ratings votes.jsonl [--user obi]   # -> data/preference_model.npz for Unity
```

`--ratings` also works for `status`. Appending the export to `data/ratings.jsonl` works too: lines are keyed
by (user, hole). To put a model trained elsewhere on the server: `docker compose cp preference_model.npz
trainer:/data/model/`.

## Run locally (without the trainer container)

The server needs Postgres. The easiest is the compose one: `docker compose up -d db` (on `127.0.0.1:5433`, user /
password `trainer`). Or any Postgres via `TRAINER_DATABASE_URL`.

```sh
Tools/course_trainer/run.sh adduser obi       # once
Tools/course_trainer/run.sh                   # then open http://127.0.0.1:8765/  (add --open to launch the browser)
```

`run.sh` defaults `TRAINER_DATABASE_URL` to the compose db, installs the Python deps into
`Tools/course_prep/.venv` if missing (`requirements.txt` here: course_prep's plus `psycopg`), runs
`npm install` the first time, rebuilds the UI when its sources changed, and starts the server. Holes go to
Unity's `Assets/CourseData/generated/` (override with `--out` or `TRAINER_HOLES_DIR`) and the model to
`course_gen/data/preference_model.npz`, so Unity sees both. The Unity window and `gen_hole.py rate/train`
never need Postgres: without `TRAINER_DATABASE_URL` they use `data/ratings.jsonl` as before.

Server commands: `server.py [--port 8765] [--host 127.0.0.1] [--out DIR] [--open]`, `seed`,
`adduser NAME`, `passwd NAME`, `export [--history]`, `top [--limit 9] [--export DIR]`.

### UI development

```sh
cd Tools/course_trainer
./run.sh                                       # API on :8765
cd web && npm run dev                          # Vite on :5173, proxies /api to :8765 (TRAINER_API overrides)
npm run typecheck                              # / npm run build
```

### Tests

```sh
docker compose run --rm test                   # everything, against the compose Postgres
# or locally, with the compose db up:
cd Tools/course_gen && ../course_prep/.venv/bin/python -m pytest tests
cd Tools/course_trainer && TRAINER_TEST_DATABASE_URL=postgresql://trainer:trainer@127.0.0.1:5433/trainer_test \
  ../course_prep/.venv/bin/python -m pytest tests   # database tests skip without the variable
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
| Enter (Ctrl/⌘+Enter in the box) | submit and go to the next pool hole (not while a button or a ▸ section is focused) |
| N | skip: next pool hole without rating |
| L | Top holes (leaderboard) |
| H | controls overlay |

You start hovering behind the tee, looking down the hole. The camera never goes below the terrain
(or below a pond's surface). On Windows/Linux Ctrl+W would close the tab, so while the mouse is
captured the page asks before unloading.

## Architecture

```
course_trainer/
  run.sh            local launcher (Postgres from compose)
  server.py         CLI: serve (migrate + seed first), seed, adduser, passwd, export, top
  api.py            FastAPI app (create_app): thin layer over course_gen; generation semaphore, pruning, /next, /top
  pool.py           shared pool in Postgres: batches, serving unseen holes, refill trigger (advisory lock)
  pool_worker.py    background batch filler: retrain, then generate in worker processes
  ranking.py        leaderboard: Wilson lower bound over latest_votes
  game.py           read-only Game API behind TRAINER_GAME_KEY
  auth.py           signed session cookie, login / logout / me, login throttle, client IP behind a proxy
  users.py          scrypt password hashes, accounts, server-side sessions, seed (users + legacy ratings.jsonl)
  inputs.py         input hygiene: text without NUL / lone surrogates, ASCII-safe JSON errors (4xx, never 500)
  db.py             migrations runner, hole views (recent holes, prune protection), training log
  config.py         Settings from TRAINER_* environment variables
  holes.py          hole packages on disk: safe lookup, per-hole summary
  migrations/       001_init.sql (users, votes + latest_votes view, hole_views, training_runs),
                    002_pool.sql (batches, pool_holes), 003_sessions.sql (sessions)
  Dockerfile, Dockerfile.dockerignore, docker-compose.yml, .env.example, requirements.txt
  tests/            test_api, test_auth, test_votes, test_pool, test_ranking (+ conftest: throwaway database)
  web/              Vite + React + TypeScript + three.js (@react-three/fiber, drei)
    src/App.tsx             login gate -> TrainerView (3D scene + HUD)
    src/Login.tsx, src/useSession.ts   login card; session state (any 401 shows the login again)
    src/api.ts              typed API client + hole.json types
    src/useTrainer.ts       app state: next pool hole (polls while generating) / generate / rate / retrain
    src/hole/               package decoding (heightmap, objects.bin, surfaces, theme, views)
    src/scene/              HoleScene, Terrain, Trees, Water, Markers
    src/controls/           Player state, PlayerController (pointer lock, fly / walk), TouchControls
    src/hud/                HolePanel, RatePanel, TastePanel, TopPanel (leaderboard), Minimap, Help, Hud
```

Everything is reused from `course_gen` rather than re-implemented: `gen_hole.generate_hole` (style sampling
with Thompson sampling, writing), `gen_hole.prune_unrated`, `gen_hole.rate_package`, `gen_hole.train_and_save`,
`gen_hole.status` (all take a rating store), `feedback.catalog`. The Unity window keeps calling `gen_hole.py`
exactly as before.

Multi-user: at most `TRAINER_MAX_GENERATIONS` generations run at once (pool and ad hoc; the rest wait), one
retrain at a time. Pruning (keep `TRAINER_KEEP_UNRATED` unrated holes) never deletes a pool hole, a hole anyone
voted on, or one anyone opened or generated within `TRAINER_VIEW_GRACE_HOURS`. "Recent holes" are per user
(`hole_views`); a shared `?hole=` link opens someone else's hole as a peek (rate it to add it to your list).

Coordinates: package `x` = east, `z` = north (metres from the SW corner); three.js uses `X = x`,
`Y = up` (metres above the minimum elevation), `Z = -north`.

## API

All `/api` routes except `login` / `logout` and the [Game API](#game-api) (`/api/game/*`, bearer key) need the
session cookie (401 otherwise; a malformed cookie is just 401). `/healthz` is public. Malformed input (NUL, lone
surrogates, ids over 200 characters, NaN / Infinity) is a 4xx with a message, never a 500.

| Method | Path | Body / query | Returns |
|---|---|---|---|
| POST | `/api/login` | `{name, password}` | `{name}` + cookie; 401 wrong, 429 throttled |
| POST | `/api/logout` | | clears the cookie |
| GET | `/api/me` | | `{name}` |
| GET | `/api/presets` | | `{presets, params, pars, feedback}` (feedback = chip table) |
| GET | `/api/status` | `?preset=` | `{ratings, up, yours, trainedOn, preset, likes, dislikes, presetVotes: {up, down}}` (ratings = everyone's; presetVotes = everyone's on `preset`) |
| GET | `/api/holes` | `?limit=20` | `{holes: [summary]}`: your holes, most recently opened first |
| GET | `/api/holes/{id}` | | summary: `id, preset, theme, par, seed, params, modelScore, lengthMeters, created, rating` (your vote); never marks it seen |
| GET | `/api/holes/{id}/{file}` | `hole.json`, `heightmap.raw`, `objects.bin`, `gen.json`, `preview.png`; `?peek=true` | package file (opening `hole.json` marks the hole seen unless `peek`) |
| GET | `/api/next` | | `{state: "ready"\|"generating", hole: summary\|null, pool: {batch, size, status, ready, seen, capped}}`; marks the hole seen |
| GET | `/api/top` | `?limit=20` | `{formula, holes: [summary + rank, ups, downs, score, previewUrl, files]}` |
| POST | `/api/generate` | `{preset, par?, seed?, overrides?: {knob: 0..1}, useModel?: true}` | summary + `attempts`, `pruned` |
| POST | `/api/rate` | `{id, rating: "up"\|"down", comment?, tags?: [chip id]}` | `{entry, status}`; 400 for contradictory chips |
| POST | `/api/train` | `?preset=&only=<user>` | status (409 when there are no ratings) |
| GET | `/api/export/votes.jsonl` | `?history=true` | ratings.jsonl lines + `user` |

## Feedback chips and training

Chips live in one table, `Tools/course_gen/feedback.py` (`TAGS`). Chips that pull a knob opposite ways (Too long /
Too short, More / Fewer trees, Boring / Too hilly ...) are mutually exclusive: `feedback.opposites` derives them
from the table, the catalog lists them as `excludes` (the UI drops the opposite chip), and `validate_tags` rejects
both together. Each chip maps to knobs and a
direction (`more_trees` → `tree_density +1`, `too_long` → `length −1`, `boring` → `dogleg +1, relief +1`,
…). They are stored on the rating line (`"tags": [...]`, plus `"comment"` for the note; both optional,
so older lines and the Unity window are unaffected) and **used in training**: a tag on a rated hole
becomes a paired comparison "this hole with that knob nudged by 0.2 would be better", i.e. one extra
logistic observation on the feature difference `f(nudged) − f(rated)` labelled 1, weighted 0.5
(Bradley–Terry). The bias and all other knobs cancel, so a chip only teaches its own knob, globally
and for the hole's preset, and it works on 👍 and 👎 alike. Chips at a knob's limit add nothing. The
CLI accepts them too: `gen_hole.py rate <pkg> up --comment "..." --tag more_trees too_long`.

Free-text comments are stored only (they are for you, or a future text model).
