# Bug hunt log

QA findings, newest round first. Each entry has a severity (blocker / major / minor / polish), the component, repro steps, expected vs actual, and evidence.
Evidence files live in the tester's tmp folder (round 1: `/Users/obadiah/.claude/jobs/5fd17b43/tmp/bughunt/`, round 2: `…/tmp/bughunt2/`) unless noted.

---

## Round 2 (2026-10-02)

This round covered the two parts nobody else was editing: the Course Trainer website and the iPhone app.
- **Trainer:** the local compose project `e2etrainer` on http://localhost:8765, with `TRAINER_POOL_BATCH_SIZE=12` and `TRAINER_MAX_GENERATIONS=6`.
  - Test users `qa1`–`qa5` and `QaMixed` were added with `adduser`.
  - It was driven with httpx scripts and the Playwright browser: desktop at 1440×900 and 1280×650, and a phone context (390×844, touch, `pointer: coarse`).
- **App:** see the iPhone app section below.
- Evidence is in `/Users/obadiah/.claude/jobs/5fd17b43/tmp/bughunt2/`: `trainer/` holds the scripts, `trainer/shots/` the screenshots, and `app/` the app evidence.

### Course Trainer

#### T-1 Phone in landscape: the Submit button is off screen, so you can't rate
- **Fixed** (2026-10-02): `mobile.css`: the phone layout also applies at `max-height: 500px`. In landscape, the rating card is a full-height side panel that scrolls, with the touch controls beside it. Checked at 844×390 and 390×844: Submit, chips and Log out are hit-testable, and the touch controls overlap no card.
- **Severity:** major
- **Component:** `web/src/mobile.css`. The phone layout only applies at `max-width: 760px`. A landscape phone (844×390, 932×430) gets the desktop layout, plus the touch controls from `@media (pointer: coarse)`.
- **Repro:**
  1. On a phone, or in a touch emulator at 844×390, log in and rotate to landscape.
- **Expected:** a usable layout with Submit reachable, like portrait.
- **Actual:**
  - The rate card is 436 px tall in a 390 px viewport. The Submit button's bottom edge is at y = 455, and nothing scrolls (`body { overflow: hidden }`, and the card has no scroll container). There is no Enter key on a phone, so a vote can't be submitted.
  - The Tee / Green / Overhead buttons sit under the top bar (Log out, Top holes). `elementFromPoint` there returns the top-bar button.
  - The move stick covers the "Advanced" and "Revisit a recent hole" rows of the hole card, and the ▲/▼ buttons cover the chips.
- **Evidence:** `trainer/shots/bh2_phone_landscape.png`.

#### T-2 The "Generating new holes…" overlay blocks the whole HUD
- **Fixed** (2026-10-02): the waiting overlay is now `hud/WaitingCard.tsx`, a centred card. The top bar, Skip, Advanced, history and Like stay clickable (`elementFromPoint` checked).
- **Severity:** minor
- **Component:** `hud/Hud.tsx` and the `.loading.waiting` style (`position: absolute; inset: 0; pointer-events: auto; z-index: 20`).
- **Repro:**
  1. See every ready pool hole so the app shows the waiting card. (In the test, `/api/next` was stubbed to `generating`, because real batches of 12 finish in about 10 s.)
  2. Try to click Log out, Skip, Advanced › Generate, "Revisit a recent hole", or Like.
- **Expected:** the waiting card is a card, and the rest of the HUD still works. Advanced › Generate is exactly what you'd want while you wait.
- **Actual:**
  - `elementFromPoint` at each of those controls returns the `.loading waiting` overlay.
  - Only "Browse the top holes meanwhile" and the keyboard (N, L) work. On a phone, you can't even log out.
- **Evidence:** `trainer/shots/bh2_waiting.png`. Everything behind the card is dimmed and unclickable.

#### T-3 A lost session leaves a zombie `/api/next` poller that later eats a pool hole
- **Fixed** (2026-10-02): `useTrainer` stops polling and drops late responses once unmounted. `api.ts` aborts the session's in-flight requests on logout or 401. No `/api/next` calls on the login screen.
- **Severity:** minor
- **Component:** `useTrainer.ts` `next(poll)`.
  - The 401 dispatches `UNAUTHORIZED`, which unmounts `TrainerView`, and its cleanup clears `pollTimer`.
  - The rejected promise then resolves to `undefined`. Because `poll` is true, it schedules a new timer, and nothing ever clears that one.
- **Repro:**
  1. Reach the waiting state.
  2. Lose the session. Clear the cookie, run `passwd` for the user, or let the session expire.
  3. Wait on the login screen, then log in again.
- **Expected:** polling stops when the view unmounts.
- **Actual:**
  - The login screen keeps calling `GET /api/next` every 3 s. That was 5 × 401 in 15 s, each logged as a console error.
  - After logging back in, the old poller's next call succeeds. `/api/next` marks a hole as seen (`forest_341233168_a7b069`), and the dead instance calls `history.replaceState` with that id.
  - The screen shows a different hole (`links_213223419_58e6c5`) from the URL. The served hole was never shown, but it counts as seen.

#### T-4 Reloading after opening a Top hole marks it as seen
- **Fixed** (2026-10-02): a `?hole=` that isn't in your recent holes opens as a peek, and `GET /api/holes/{id}` no longer marks a hole seen. After a reload, `hole_views` has no row.
- **Severity:** minor
- **Component:**
  - `useTrainer.ts` start-up: `?hole=` that isn't in your recent list calls `api.hole(id)`.
  - `api.py` `describe`: `GET /api/holes/{id}` calls `db.touch_view`.
  - Then `open(start)` fetches `hole.json` without `peek`.
- **Repro:**
  1. As `qa4`, press L and open a pool hole you haven't seen (`forest_621272063_f250a1`). At this point `hole_views` has no row for it, which is correct.
  2. Reload the page.
- **Expected:** a peeked hole still isn't seen. The README says "it can still be served to you later".
- **Actual:**
  - After the reload, `hole_views` has `(qa4, forest_621272063_f250a1)`, so it will never be served to `qa4`.
  - The same happens when a leaderboard hole's URL is shared.

#### T-5 The previous user's `?hole=` carries over to the next login, and it's marked seen for them
- **Fixed** (2026-10-02): ending a session (logout or 401) clears `?hole=` and unmounts all per-user state. A shared link opened while logged out is kept.
- **Severity:** minor
- **Component:** `useSession.logout` / `useTrainer` start-up. Logout leaves `?hole=<id>` in the URL, and the next login opens it.
- **Repro:**
  1. Log in as `qa4` and look at a hole.
  2. Log out, and log in as `qa5` in the same tab.
- **Expected:** `qa5` starts on their own next pool hole.
- **Actual:**
  - `qa5` starts on `qa4`'s hole (`forest_563021450_515b36`), and it's recorded as seen for `qa5` through `GET /api/holes/{id}`.
  - Also, Back after Log out leaves the app (`about:blank`), because the app only uses `replaceState`.

#### T-6 Logout doesn't end the session server-side
- **Fixed** (2026-10-02): server-side `sessions` table (`003_sessions.sql`). Logout deletes the session and `passwd` deletes all of the user's sessions. A replayed cookie gets 401.
- **Severity:** minor (security)
- **Component:** `auth.py`. The signed cookie is stateless, and `/api/logout` only tells the browser to delete it.
- **Repro:** `trainer/auth5.py`.
  1. Log in and copy the `trainer_session` value.
  2. `POST /api/logout`.
  3. `GET /api/me` with the copied value.
- **Expected:** 401.
- **Actual:** 200 `{"name":"qa2"}`. The cookie stays valid for `TRAINER_SESSION_DAYS` (30 days) unless the password changes. On a shared or borrowed computer, "Log out" doesn't really log out.

#### T-7 Any account holder can bypass the per-IP login throttle
- **Fixed** (2026-10-02): the IP and name counters are independent and each forgets one failure every 3 min. A successful login clears only its own name's counter. `auth3.py` now gets 429 after 5 failures.
- **Severity:** minor (security)
- **Component:** `auth.py` `login`. `throttle.succeeded(keys)` clears the `ip:` key on any successful login, including the attacker's own.
- **Repro:** `trainer/auth3.py`.
  - Try 30 wrong passwords against 30 different names, logging into your own account after every 4th try.
- **Expected:** throttled after 5 failures from one IP.
- **Actual:**
  - All 30 tries return 401 and none returns 429, so unlimited password spraying (one guess per name) is possible.
  - The per-name limit still works: five failures for one name give 429 with `Retry-After: 2`, which was checked.

#### T-8 A non-ASCII session cookie returns 500 on every route
- **Fixed** (2026-10-02): `SessionSigner.read` treats non-ASCII or malformed cookies as no session: 401.
- **Severity:** minor
- **Component:** `auth.py` `SessionSigner.read`. `hmac.compare_digest(sig, …)` raises `TypeError: comparing strings with non-ASCII characters is not supported`.
- **Repro:** `curl -H $'Cookie: trainer_session=abc.\xc3\xa9' localhost:8765/api/next` gives **500** and a stack trace in the log. `/api/me` does the same.
- **Expected:** 401.
- **Actual:**
  - 500 on every route that checks the session, with a traceback logged each time.
  - In production, a parent-domain cookie with this name set by any app on a sibling `*.obadiahfusco.xyz` host could break the trainer for that visitor. This is by reasoning only and wasn't tested.

#### T-9 Malformed input gives 500 instead of 400/404
- **Fixed** (2026-10-02): `inputs.py`: NUL and lone surrogates give 422, ids over 200 characters give 404/422, and NaN/Infinity overrides give 422. Errors are ASCII-safe JSON. A `SystemExit` from course_gen becomes 400, and seeds above 2^63 give 422.
- **Severity:** minor
- **Component:** `api.py` handlers, `holes.HoleStore.folder`, and `style.apply_overrides`.
- **Repro:** `trainer/inputs1.py`, `inputs2.py`, `gen1.py`. Each of these was re-run.
  - `POST /api/rate` with `"comment":"a\u0000b"` gives 500: `psycopg.DataError: PostgreSQL text fields cannot contain NUL`.
    - The 500 also drops the keep-alive connection.
  - `"comment":"\ud800"` (a lone surrogate) gives 500. So does a lone surrogate in a tag or in `id`.
    - `UnicodeEncodeError` is raised while encoding the error message or the stored value.
  - A hole id longer than 255 characters gives 500 (`OSError: [Errno 36] File name too long`, from `Path.is_file()`). This happens on:
    - `GET /api/holes/<id>` and `/api/holes/<id>/hole.json`
    - `POST /api/rate`
    - the Game API: `GET /api/game/holes/<id>/hole.json`
  - `POST /api/generate {"preset":"parkland","overrides":{"water":NaN}}` (or `Infinity`, which Python's JSON parser accepts) gives 500.
    - `style.apply_overrides` turns the `ValueError` into `SystemExit` inside the request thread.
    - The server survives, but this is the round-1 PY-2 fix leaking into the web API.
- **Expected:** 400, 404 or 422 with a message.

#### T-10 Enter on a focused `<summary>` submits your vote and moves on
- **Fixed** (2026-10-02): Enter submits only when focus isn't on a button, summary, link or form field (`isInteractive`).
- **Severity:** minor
- **Component:** `TrainerView.tsx` hotkeys. Enter submits unless the target is a `BUTTON` or `isTyping`, and `<summary>` is neither.
- **Repro:**
  1. Press 2 (dislike).
  2. Tab to "Advanced: generate a specific hole" (or "Style knobs") and press Enter to expand it.
- **Expected:** the section expands.
- **Actual:** it expands, and it also sends `POST /api/rate` then `GET /api/next`. The vote is saved and you're moved to a new hole.

#### T-11 Each hole leaks a WebGL texture
- **Fixed** (2026-10-02): `Terrain` disposes the detail texture with the material. `renderer.info.memory.textures` stayed at 9 over 20 holes.
- **Severity:** polish
- **Component:** `scene/Terrain.tsx`. The material's `onBeforeCompile` creates `detail = new THREE.CanvasTexture(noiseCanvas(…))` per hole, but the cleanup only disposes `material.map` and `material`.
- **Repro:** wrap `createTexture` / `deleteTexture` with an init script, then press N 8 or 20 times.
- **Expected:** a flat texture count.
- **Actual:**
  - Live textures went 13 → 33 over 20 holes and 13 → 21 over 8 holes: exactly +1 per hole.
  - Buffers were flat (80–89), and the JS heap was flat (55–73 MB).
  - It's small (a 128² texture), but it grows for as long as someone rates.

#### T-12 Skip spam grows the pool without limit
- **Fixed** (2026-10-02): a new batch starts only when the newest batch is finished, someone has *rated* 50% of it or seen all of it, and fewer than `TRAINER_POOL_MAX_UNRATED` (300) pool holes are unrated. A batch with 0 holes is retried after 10 min. `pool3.py` created 0 batches.
- **Severity:** minor
- **Component:** `pool.maybe_refill` / `api.next_hole`.
  - Skips count toward the refill threshold.
  - There's no cap on batches or disk space.
  - Pool holes are never pruned.
- **Repro:** `trainer/pool3.py`. One user calls `/api/next` in a loop for 40 s.
- **Expected:** a rate limit, or a cap on unrated batches or pool size.
- **Actual:**
  - 207 holes were served, and 9 new batches (108 holes) were generated in 40 s.
  - After the session, the DB has 29 batches and `/data/holes` holds about 250 MB for 132 holes, about 1.9 MB each.
  - In production (100 per batch, never pruned), a looping script or a stuck key fills the disk and keeps the CPU busy.

#### T-13 `/docs`, `/redoc` and `/openapi.json` are public
- **Fixed** (2026-10-02): `/docs`, `/redoc` and `/openapi.json` are off unless `TRAINER_API_DOCS=true` (`run.sh` sets it).
- **Severity:** polish
- **Component:** `api.py` `FastAPI(...)`. The default docs URLs are on.
- **Repro:** `curl localhost:8765/openapi.json` with no session gives 200, the full route and schema list, including the Game API.
- **Expected:** off in production (`docs_url=None, redoc_url=None, openapi_url=None`) or behind a login.

#### T-14 Contradictory feedback chips are accepted together
- **Fixed** (2026-10-02): `feedback.opposites` derives the opposite chips from `TAGS`. The UI drops the opposite chip, and `validate_tags` rejects contradictory pairs with 400.
- **Severity:** polish
- **Component:** `RatePanel.tsx` chips and `feedback.validate_tags`.
- **Repro:** select Too long and Too short (or More trees and Fewer trees) and submit. The API accepts `"tags":["too_long","too_short"]` too.
- **Expected:** opposite chips are exclusive, as with the thumbs.
- **Actual:** both are stored and both become training pairs that cancel out.

#### T-15 The "Our taste · Links 26👍 12👎" tally is everyone's votes on every preset
- **Fixed** (2026-10-02): `/api/status` returns `presetVotes`, and the tally counts only that preset's votes.
- **Severity:** polish
- **Component:** `hud/TastePanel.tsx`. `status.up` / `ratings` are global, but the header names the hole's preset.
- **Repro:** open any hole and compare the header tally with `/api/status?preset=links`.
- **Actual:** the header shows the same 27/13 on a Lakes, Links or Parkland hole.
- **Evidence:** `bh2_desk_first.png` and `bh2_desk_1280x650.png`.

### Tested, no bugs found (round 2, trainer)
- **Auth:**
  - Name case and whitespace variants (`QA1`, ` qa1 `, `qamixed` → `QaMixed`) log in. The password is case-sensitive.
  - A zero-width-space name fails.
  - The throttle locks after 5 failures (429, `Retry-After: 2`) and also blocks the correct password, as designed.
- **Cookies:** a missing signature, a swapped payload, garbage, empty or trailing data, and an extra segment all give 401. With duplicate cookies, the first one wins. The cookie is `HttpOnly; SameSite=lax`.
- **Every `/api` route and hole file without a session:** 401.
- **Game key:**
  - It opens only `/api/game/*`. With the key, `/api/me`, `/api/next`, `/api/top`, `/api/export` and `/api/holes/...` give 401, and a session can't open game routes.
  - `bearer` in lower case is accepted. A wrong key, the key ±1 character, `Basic`, `Token`, or the bare key give 401.
  - `limit` is clamped to 1..100.
  - Game files are `private, max-age=86400, immutable`.
- **Path tricks:** none got past auth.
  - Tried: `/api;/`, `/%61pi/`, `//api`, `/api//`, `/api/./`, `/x/../api`, `/api%2f`, `/API/`, `/api/game/../next`, `/api/game/%2e%2e/next`, `..` / `%2e%2e` / `%2f` hole ids and file names, `/.env`, `../etc/passwd`.
  - Each gives 401 or 404.
- **Pool:**
  - 6 threads across 2 users made 48 `/api/next` calls with 0 repeats per user.
  - A 40 s Skip loop and a 324-hole drain had 0 repeats.
  - Skip and Submit both count as seen.
  - The refill fires once per batch at 6/12.
  - The waiting state appears when everything ready has been seen, and the next hole appears on its own.
  - `/api/next`, `/api/top` and `/api/status` stayed under 0.45 s while batches generated. `/api/presets` peaked at 1.4 s during 6 concurrent retrains.
- **Top holes:** the order matches the Wilson lower bound computed from `latest_votes`, including re-votes and ties (more likes, then id), across 20 holes. Opening one with `peek` doesn't mark it seen (but see T-4).
- **Rate:**
  - Unknown tags give 400 and duplicate tags are de-duplicated.
  - A bad, numeric or missing rating gives 422, and an unknown, empty or traversal id gives 404.
  - Comments of 4000 characters and 4000 emoji are fine, and 4001 characters gives 422.
  - HTML in a comment is stored as text and never rendered by the UI.
  - Rating any hole on disk is allowed: holes are shared by design.
- **Double submit:**
  - Five Enters gave 1 rate and 1 next.
  - Ten N presses gave 1 next.
  - The buttons are disabled while busy.
- **Export:**
  - 41 lines equal 41 `latest_votes`, and `?history=true` gives 49, equal to 49 `votes`.
  - Every line has `user`, there are no duplicate (user, hole) pairs, and re-votes show the latest.
  - A comment with a newline, quotes, HTML and emoji round-trips.
- **Retrain during a batch:** 6 parallel `POST /api/train` calls (all users, `only=qa1`, `only=QA3`) gave 200, `only=nobody` gave 409, and there were no errors in the log.
- **XSS:** `?hole=<img src=x onerror=alert(1)>` raised no dialog and injected no element. Comments and names render as text.
- **3D controls:**
  - Positions below the terrain, a million metres out, 1e9 high, and walking off the tile edge are all clamped back onto the tile and above the ground.
  - The fly speed clamps at 2–220.
- **Touch:**
  - The move stick moves the player and stops on release.
  - Holding ▲ climbs, and it stops even when the finger slides off before lifting.
  - A one-finger drag looks around. A two-finger swap and a later drag still work.
  - The Top panel list scrolls, and tapping a row opens it (peek).
  - Portrait 390×844 has no horizontal overflow, and the comment box is 16 px (no iOS zoom).
- **Desktop:** at 1024×700, 1280×650 and 1366×768 every control is on screen.
- **Console:** the only errors are the expected 401s (`/api/me` before login, a wrong password) and a `THREE.Clock` deprecation warning.

### Not tested (round 2, trainer)
- **Real iOS Safari:** the phone tests used Chromium touch emulation, so pointer lock, `100vh` and Safari bottom-bar behaviour weren't checked.
- **Behind Cloudflare** (`TRAINER_TRUST_PROXY`, `CF-Connecting-IP` throttle keys): the local stack has the proxy off.
- **NaN player position** (from the console, `courseTrainer.player.position.x = NaN`): the camera stays NaN until T / G / O. Normal input can't produce this, so it isn't filed.
- **A batch that ends `ready` with 0 holes** (fixed with T-12): by code reading, `maybe_refill` would need 1 seen of 0 and never refill, but this needs every generation to fail and wasn't reproduced.

### iPhone app (SwingRemote, `Golf-app`)

How it was tested:
- **Build:** built with `xcodebuild` and run on the iPhone 17 Pro simulator, plus a temporary iPhone SE (3rd gen) simulator.
- **Driving it:** `idb` for taps, typing and the accessibility tree.
- **Server:** an isolated game server, compose project `bughunt2` on port 18081, torn down with `down -v` at the end.
- **Fake sim:** a scripted role=sim client (`app/sim.py`) that logs every frame to `app/sim.log`.
- **Evidence:** in `app/`.

#### A-1 Aim buttons keep sending `aim` forever if the screen changes while one is held
- **Fixed** (2026-10-02): `Views/HoldRepeatButton.swift`: the repeat is a `.task` keyed on a `@GestureState` press, the enabled state and the scene phase, so it stops on release, a cancelled gesture, the view disappearing, the app leaving the foreground or the controls being disabled. Aim is also disabled as soon as the sim leaves the hole. Re-run on the SE: 0 `aim` frames after `holeComplete` (none in the 10 s after release), and a tap still sends one.
- **Severity:** major
- **Component:** `Golf-app/Views/RingSegment.swift`, `HoldRepeatButton`.
  - The repeat `Task` lives in `@State` and is cancelled only in `DragGesture.onEnded`.
  - When `GameplayView` disappears mid-press (`PlayView` swaps to `RemoteView` on any non-`game` screen), `onEnded` never fires.
  - The task, which holds `session.game`, then runs forever.
- **Repro:**
  1. The sim is on screen `game`, so the phone shows gameplay mode.
  2. Hold Aim left or Aim right.
  3. While holding, the sim sends `{"type":"state","screen":"paused"}` or `"holeComplete"`. In real play this is the hole ending, or Esc on the PC.
  4. Release.
- **Expected:** the repeat stops on release, or when the view goes away.
- **Actual:**
  - `{"type":"aim","delta":±1}` keeps arriving about 8 times a second indefinitely: 32 → 112 messages in the 10 s after release, and 190 → 270 in another run.
  - It survives going back to gameplay. Only killing the app stops it.
  - Reproduced 3 times on both simulators.
- **Evidence:** `app/sim.log` (from 02:15:13), `aim_runaway_paused.png`, `aim_runaway2.png`.

#### A-2 Gameplay and Practice overflow on iPhone SE: Menu / Mulligan / Pick up and the header can't be reached
- **Fixed** (2026-10-02): new `Views/ScreenScaffold.swift`: Gameplay, Putting, Practice and Remote keep the header pinned and scroll the rest only when it doesn't fit, and short screens get a smaller wheel, Address button, meter and D-pad. Between holes the card is full height, with a **Next hole** button under it. On the SE, the header is at y = 28, and Menu / Mulligan / Pick up sit above the tab bar.
- **Severity:** major
- **Component:** `Views/Game/GameplayView.swift` and Practice's `ContentView`.
  - Both are fixed `VStack`s with no `ScrollView`.
  - `ClubWheel` is a fixed 320 pt and `AddressButton` 168 pt.
  - `RemoteView`'s scorecard has the same problem.
- **Repro:** on an iPhone SE (3rd gen), with the sim on `game`, open the Play tab, then the Practice tab.
- **Expected:** every control and the header (status pill, Settings gear) are visible and tappable.
- **Actual:**
  - **Gameplay:**
    - The header is laid out at y = -42.
    - Menu, Mulligan and Pick up sit at y 578–623, under the tab bar (y 584). Tapping "Mulligan" switched to the Players tab and sent nothing.
    - "Simulate swing" is also under the tab bar.
  - **Practice:** the header and gear are at y = -51, and the Swing scale slider is under the tab bar.
  - **Remote on `holeComplete`:** the scorecard collapses to zero height.
  - On the 17 Pro, gameplay only just fits.
- **Evidence:** `se_gameplay.png`, `se_practice.png`, `se_holecomplete.png`. For comparison on the 17 Pro: `gameplay_17pro.png`, `holecomplete.png`.

#### A-3 When the server drops mid-hole, the phone stays in gameplay mode and every button silently does nothing
- **Fixed** (2026-10-02): `DisconnectedBanner` ("Disconnected — reconnecting…" and the reason) shows on Gameplay and Putting while the link is down, and Aim, Menu, Mulligan and Pick up are greyed out and disabled. They come back on their own after the reconnect `hello`. Checked with `docker stop` and `docker start`.
- **Severity:** minor
- **Component:** `Network/GameLink.swift`.
  - After a receive failure, `run()` only resets `simConnected`. `state` is cleared only on `simStatus false`, and `stop()` doesn't clear it either.
  - `send()` returns false silently.
  - `AppHeader.statusText` falls back to the UDP label.
- **Repro:**
  1. The sim is on `game`.
  2. `docker stop <project>-game-server-1`.
  3. Wait 15 s, then tap Aim, Menu or Mulligan.
- **Expected:** the phone leaves gameplay mode, or disables the controls and says "Server not reachable".
- **Actual:**
  - The stale gameplay screen stays, and the pill says "No PC found".
  - Taps are dropped: 0 frames reached the sim after it reconnected.
  - It recovers within the 5 s backoff once the server is back.
- **Evidence:** `server_down_game.png`, `server_down_game2.png`.

#### A-4 Leaderboard table: one long name pushes every stat column off-screen
- **Fixed** (2026-10-02): `PlayerTable`: the stat columns are fixed width, and the name fills the rest and truncates. The leaders lists truncate names too. With a 40-character name on the SE, every column is visible.
- **Severity:** minor
- **Component:** `Views/Game/LeaderboardView.swift`, `PlayerTable`. The name `Text` has no width cap and sits in a horizontal `ScrollView` with hidden indicators.
- **Repro:** finish a round with a 22× "😀" or 40-character name (both accepted), then open Scores › Leaderboard.
- **Expected:** names are truncated, and HCP / Avg / Best / Wins / Bird / Aces stay visible.
- **Actual:**
  - The name column is 479 pt wide, and HCP starts at x = 522 on a 402 pt screen.
  - Only names are visible, with no hint that the table scrolls.
  - In "Most birdies" the name wraps under the tab bar.
- **Evidence:** `scores_leader2.png`.

#### A-5 Scorecard hides hole 9, OUT/IN, TOT and ± off-screen
- **Fixed** (2026-10-02): `ScorecardTable`: names are pinned on the left and TOT / ± on the right, and the holes plus OUT/IN scroll between them, scrolled to the current hole. 18 holes get a Front 9 / Back 9 picker. An 8-player card fits on the 17 Pro.
- **Severity:** minor
- **Component:** `Views/Game/ScorecardTable.swift`.
  - Each nine needs about 456 pt and is its own hidden-indicator `ScrollView`.
  - `RemoteView` caps the card at 220 pt.
- **Repro:** open Scores › Scorecard on any game, or the Play tab on `holeComplete`.
- **Expected:** the total and ± are visible at a glance.
- **Actual:**
  - Only holes 1–8 are visible, and on 18 holes each nine scrolls separately.
  - Between holes on the Play tab, only 2 player rows fit.
- **Evidence:** `scores_card.png`, `scores_18.png`, `holecomplete.png`.

#### A-6 Leaderboard doesn't refresh when a game finishes
- **Fixed** (2026-10-02): `GameLink.scoresRevision` goes up on `gameStarted`, `scorecard` and `gameFinished`, and `ScoresView` reloads on it. The leaderboard filled in on its own when a game finished.
- **Severity:** minor
- **Component:** `Views/Game/ScoresView.swift`. It loads only on `.task(id: page)` and pull-to-refresh, and ignores the WebSocket `gameFinished`.
- **Repro:** with Scores › Leaderboard open, finish a game.
- **Expected:** the leaderboard updates.
- **Actual:**
  - It showed "No finished rounds yet" after 4 games had finished.
  - Kim's row (rank 1 → 2, HCP -2.0 → -1.0) changed only after pull-to-refresh.
- **Evidence:** `lb_before.txt`, `lb_after.txt`, `scores_leader_empty.png`.

#### A-7 Port shown as "8,080" in Settings
- **Fixed** (2026-10-02): the port is shown as plain text (`String(port)`, `Text(verbatim:)`): "192.168.1.20:8080" and "TCP 8080".
- **Severity:** polish
- **Component:** `Views/SettingsView.swift`. The `Int` port is locale-formatted inside a `LocalizedStringKey`.
- **Repro:** open Settings › Game server.
- **Expected:** 8080 everywhere.
- **Actual:**
  - The placeholder reads "127.0.0.1:8,080", so copying it gives an invalid address.
  - The footer reads "port 8,080" and "TCP 8,080".
- **Evidence:** `settings_8080.png`.

#### A-8 An invalid Game server address is silently ignored
- **Fixed** (2026-10-02): `AppConfig.serverAddressProblem`: Reconnect or Return with `ftp://bad host` shows an inline error and keeps the old server, and the error clears when you edit the field.
- **Severity:** polish
- **Component:** `SettingsView.applyServer` returns early when `AppConfig.serverURL` is nil.
- **Repro:** type `ftp://bad host` and tap Reconnect.
- **Expected:** a validation error.
- **Actual:**
  - Nothing happens: the bad text stays in the field and the app stays on the old server.
  - Reopening Settings silently reverts the field.
- **Evidence:** `settings_invalid.png`.

#### A-9 "Up to 8 players" error stays after deleting a player
- **Fixed** (2026-10-02): `PlayersView` clears the error whenever the player list changes (delete, move or add).
- **Severity:** polish
- **Component:** `Views/Game/PlayersView.swift`. `.onDelete` doesn't clear `problem`.
- **Repro:** with 8 players, try to add a 9th, then delete one.
- **Expected:** the error clears.
- **Actual:** the error stays under a 7-player list until the next add.
- **Evidence:** `players_full.png`.

### Tested, no bugs found (round 2, app)
- **Remote D-pad:** all six keys send the right `nav`. Status titles and hints are right for menu, game and holeComplete, and for the not-connected and sim-not-running states.
- **Club wheel:** tap and drag send `club` with the full name, and the phone follows the sim's suggested club.
- **Aim:**
  - A tap sends ±1.
  - Holding repeats at about 8 per second and stops on release when the screen doesn't change.
  - The label follows the sim's aim.
- **Simulate swing:**
  - It sends `shot` over the WebSocket; the first tap only addresses, which is how it's built.
  - With the server down it falls back to UDP ("No PC connected").
- **Shot result and pause buttons:**
  - `shotResult` shows in the Last shot strip.
  - Menu sends `nav back` and Mulligan sends `mulligan`.
  - Pick up confirms, then sends `skip`.
- **Players:**
  - Add, the case-insensitive duplicate check and the 8-player limit work.
  - Reorder and delete work.
  - Emoji, 40-character and accented names work.
  - 9 and 18 holes work, and Start Game works with 3 and 8 players.
  - The in-progress banner and End game work (the game is set to ABANDONED).
  - Start Game is disabled with 0 players (checked in code).
  - The server accepted a 22-emoji name, so the GS-12 length limit looks fixed.
- **Scores:** the live scorecard updates over the WebSocket, the 18-hole, 8-player card renders, and the leaderboard stats over 5 games are right.
- **Connection:**
  - Server stop and start: the phone reconnects and `hello` restores state.
  - Background for 65 s, then foreground: it picks up the state the sim sent meanwhile.
- **Stability:**
  - No crashes, and no app errors in the log.
  - The unit tests passed 3 out of 3 runs.
  - The 4 older `Golf-app-*.ips` crash reports are test-runner crashes at `GameLinkTests.swift:101` (`server.received[0]` out of range). They didn't reproduce; it may be a race in `FakeServer`.
- **SE layout:** the Players and Remote screens fit on the SE.

### Not tested (round 2, app)
- **Rotation:** the app is portrait-only, so it doesn't apply.
- **Real swing detection and haptics:** these need a physical device.
- **UDP discovery, the 4242 fallback against a real sim, and real Unity replies:** the Unity Editor was off-limits this round.
- **Zero-width names through the UI:** idb can't type them.
- **VoiceOver:** not checked properly. From the accessibility tree, the delete (minus) buttons have no accessibility element, and the reorder handles read "Reorder 1" instead of the player's name.
- **Plain-http server on a non-local hostname:** not tried.

---

## Round 1

Moved to [bug-hunt-round1.md](bug-hunt-round1.md) to keep this file short.
