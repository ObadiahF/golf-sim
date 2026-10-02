# Bug hunt log: round 1

Split from [bug-hunt.md](bug-hunt.md), which has the newer rounds. Evidence: `/Users/obadiah/.claude/jobs/5fd17b43/tmp/bughunt/`.

---

## Round 1 (2026-10-02)

The Unity tests ran in the Editor in Play mode, driven by a dev script (`tmp/bughunt/BugHunt.cs`).
- The script calls `ShotPanel.Hit`, `GolfBall.Advance`, `NavInput.Push`, and the private `ShotPanel.ResetBall`, which is what the R key calls.
- The hole is the one in `HoleSimulator.unity`: `forest_7_e3914f`, par 4, 412 yd.
- The game server ran as an isolated compose project `bughunt` on port 18080. The repros below say `localhost:8080`; swap in your own port.

### Hole Simulator / ball

#### U-1 Reset (R) leaves the previous shot's tracer on screen
- **Status:** Fixed (2026-10-02). `GolfBall` raises a new `Placed` event whenever the ball is put down (reset, drop, next turn) and `BallTracer` clears the trace on it. Check: `BugFixCheck.TracerReset`.
- **Severity:** minor
- **Component:** `GolfSim/Ball/Runtime/BallTracer.cs`. The tracer only clears on `GolfBall.ShotStarted`, and `GolfBall.ResetToTee` raises no event.
- **Repro:**
  1. In the Hole Simulator, hit a Driver (Space).
  2. Press R, either mid-flight or after the ball stops.
- **Expected:** the trace is cleared when the ball goes back to the tee.
- **Actual:**
  - The old trace stays drawn until the next hit.
  - After R mid-flight, the LineRenderer still had 101 points. After R at rest, it had 237.
  - In the live game it was 35 points after R mid-flight, and the trace was drawn from the tee into the trees.
- **Evidence:** `bh_after_reset.png`. The ball is back on the tee and the yellow trace of the aborted shot is still shown.

#### U-2 Reset (R) mid-flight leaves the camera in chase mode
- **Status:** Fixed (2026-10-02). `ShotPanel.LineUp` (used by R) now calls `flyCam.StopFollowing()` and zeroes `followOffset` before jumping behind the ball. Check: `BugFixCheck.CameraReset`.
- **Severity:** minor
- **Component:** `ShotPanel.ResetBall` / `LineUp`. These call `flyCam.JumpTo` but never clear `HoleFlyCamera.trackTarget` or `followOffset`, which `ShotPanel.Hit` set.
- **Repro:**
  1. Hit a Driver with "Camera follows ball" on.
  2. Press R about 1.5 s into the flight.
  3. Wait 3 s.
- **Expected:** the camera stays at the line-up pose, 6 m behind the ball and looking down the target line, as it does after a normal shot.
- **Actual:**
  - Just after R, the camera is at the line-up pose (236.3, 13.8, 65.3) and looking down the line.
  - Three seconds later, it has drifted to the chase offset (237.1, 16.8, 57.2), 14 m back and 5 m up, and is staring down at the ball (ball at viewport 0.5, 0.5).
  - `trackTarget` is still "Golf Ball" and `followOffset` is still (2.59, 5.00, -14.08).

#### U-3 Pressing Space mid-flight changes the wind on the ball already in flight
- **Status:** Fixed (2026-10-02). `ShotPanel.Hit(ShotData)` returns before touching the wind or camera while the ball is moving. Check: `BugFixCheck.WindMidFlight`.
- **Severity:** minor
- **Component:** `ShotPanel.Hit(ShotData)`. It writes `ball.windSpeed` and `ball.windHeading` before `GolfBall.Hit`, and `GolfBall.Hit` then ignores the hit because the ball is moving.
- **Repro:**
  1. Set wind to 0 and hit a Driver.
  2. While it flies, move the Wind slider to 30 mph and Wind from to 90° (E), then press Space.
- **Expected:** a hit while the ball is moving is ignored entirely, and the shot keeps the wind it was launched with.
- **Actual:**
  - The second hit is ignored, but `ball.windSpeed` becomes 13.4 m/s and the current shot curves.
  - The result was 40.4 yd left, against 0.1 yd for the same shot without the extra press.
  - Remote shots are not affected, because `Shots.Hit` checks `InMotion` first.

#### U-4 Practice HUD shows a stale lie after Reset (R)
- **Status:** Fixed (2026-10-02). practice lie now follows `GolfBall.Placed` (`RoundDirector.OnBallPlaced` reads `GolfBall.Lie`), and a ball on the tee marker always reads "tee". Check: `BugFixCheck.PracticeLie`.
- **Severity:** polish
- **Component:** `RoundDirector.Play.cs`. `practiceLie` only updates in `OnShotFinished`, and R doesn't reset it.
- **Repro:**
  1. In the Hole Simulator, hit a Driver into the trees.
  2. Press R.
- **Expected:** the HUD shows LIE Tee.
- **Actual:** the HUD shows LIE Woods while the ball sits on the tee.
- **Evidence:** `bh_after_reset.png`, top-left panel.

#### U-5 Hang time and land angle read 0 when the ball leaves the map in the air
- **Status:** Fixed (2026-10-02). `Finish` sets `flightTime` when the ball leaves the map in the air and the new `ShotResult.landed` is false, so the panel shows land angle "n/a". Check: `BugFixCheck.OffMapInAir`.
- **Severity:** minor
- **Component:** `GolfBall.Simulate` / `Finish`. OutOfBounds while flying never sets `flightTime` or `landAngle`, and `carry` is set to `total`.
- **Repro:** `aimOffset = 180` (aim backwards), then hit 200 mph at 15° from the tee.
- **Expected:**
  - The ball leaves the map 34 m up after about 1 s.
  - Hang time is about 1.0 s, and land angle is blank or "n/a" rather than a number.
- **Actual:**
  - The panel shows Carry 78.1 yd, Hang time 0.00 s and Land angle 0.0°.
  - A 90° aim shows the same: carry 324 yd, hang 0.00, land 0.0.
  - Any big downwind drive can fly off the 531 m tile like this.

#### U-6 Tee position sits off the back tee box, on rough (generator)
- **Status: Fixed.** `layout.py` `tees()` nudges the back box so the tee point is >= 1.5 m (`TEE_MARGIN`) inside it (same RNG draws); `validate.py` rejects a layout whose tee is off the box; test over 42 holes; `GENERATOR_VERSION` 3.
- **Severity:** minor
- **Component:**
  - Root cause: `Tools/course_gen` (`layout.py` `tees()`; `generate.py` sets `"tee"` to `layout.path.coords[0]`).
  - Not caught by: `prep_hole.py validate`.
- **Repro:**
  - **In Unity:**
    1. Open the Hole Simulator and look at the tee.
    2. Hit the Putter from the tee.
    - The ball's lie reads "rough".
    - `TerrainSurfaceMap.SurfaceAt(hole.TeeWorld)` returns `rough`, and the tee box starts about 1.5 m ahead.
  - **Generator:**
    1. `cd Golf-sim/Tools && course_prep/.venv/bin/python course_gen/gen_hole.py generate --no-model --preset parkland --par 4 --seed 7 --out /tmp/x`.
    2. Classify the `tee` point against the areas.
- **Expected:** the tee point, documented as the "tee centre", lies inside a `tee` area.
- **Actual:**
  - `forest_7_e3914f`: the tee is (234.51, 71.30) and the first tee polygon spans y 70.67–83.21. The tee sits 0.6 m from the back edge and samples as rough.
  - In 27 of 81 generated holes (all presets and pars) the tee is outside the tee box. The worst is 2.0 m, in `desert_316_52c0a6`.
  - Cause: the tee is the start of the path, but the first box is centred 5 ± 2 m along it with a random 9–15 m length.
- **Evidence:**
  - `bh_round.png`: the ball sits on the rough strip behind the tee box.
  - `gen/tee_offbox_desert_316.png`
  - `gen/batch_check.txt`

#### U-7 Lie has no effect on the shot (bunker, woods and rough play like the fairway)
- **Status:** Fixed (2026-10-02). `GolfBall.Hit` applies `BallPhysicsSettings.lies` (rough −12%, woods −15%, bunker −25% for full shots; less for short shots; less spin, a little more launch) for every input, and the HUD and panel show it ("Rough −10%"). Check: `ObstacleCheck.Lies`.
- **Severity:** minor (may be a missing feature rather than a bug)
- **Component:** `GolfBall.Hit` / `BallPhysics.Launch`. There is no lie-based adjustment to ball speed or spin.
- **Repro:** place the ball about 153 yd from the pin on fairway, rough, bunker, native and woods, and hit a 7 Iron preset from each.
- **Expected:** reduced distance or spin from bunker, rough and woods (GSPro-style lie penalties).
- **Actual:**
  - Carry is 173.1, 172.2, 172.3, 169.5 and 174.9 yd respectively, with identical apex (30.8 yd).
  - A full 7 iron from inside the woods flies as far as one from the fairway.

#### U-8 Ball flies through trees
- **Status:** Fixed (2026-10-02). new `ObstacleField` (grid of `HoleInfo.obstacles`) gives trunks, shrubs and rocks solid rebounds and makes canopy hits a matter of chance, with a per-shot seed. `GolfBall.HitObstacle` and `ShotResult.hitTree`/`hitRock` report the hits. Check: `ObstacleCheck.All`.
- **Severity:** minor (missing feature)
- **Component:** `GolfBall` / `BallPhysics`. Flight only tests against the terrain heightmap, never against trees or objects.
- **Repro:** from the tee, hit a 3 Wood or Driver straight at the pin. The line runs through the forest on this dogleg.
- **Expected:** the ball hits trees and drops.
- **Actual:** it passes straight through and lands in "woods" 250+ yd away. For example, the Driver lands at (241.6, 320.1) with lie woods.

#### U-9 Fly-camera help box is drawn under the Practice HUD panel
- **Status:** Fixed (2026-10-02). the fly-camera help is off whenever the Round HUD exists (H still toggles it). `HoleFlyCamera` is CourseBuilder code, so the fix is in `RoundDirector.Bind`. Check: `BugFixCheck.HelpOverlay`.
- **Severity:** polish
- **Component:** `HoleFlyCamera.OnGUI` (the box at 10, 10) and the Round HUD panel (top-left).
- **Repro:** open the Hole Simulator in practice (help is on by default) and look at the top-left corner.
- **Expected:** the two overlays don't overlap.
- **Actual:** the IMGUI help text ("Hole forest_7… Right mouse: look…") shows through around the edges of the PRACTICE panel.
- **Evidence:** `bh_after_reset.png`.

### Menus

#### M-1 Select or click twice during the fade loads the scene twice
- **Status:** Fixed (2026-10-02). `ScreenFade.LoadScene` ignores requests until the requested scene has loaded (static `ScreenFade.Loading`), and `MainMenu.Play` and `RoundDirector.PlayRound` check it too. Check: `BugFixCheck.Menu` (1 scene load for 2 Restarts).
- **Severity:** minor
- **Component:** `ScreenFade.LoadScene`, used by `HomeMenu` and `MainMenu`. Nothing guards against a second call while the 450 ms fade is running.
- **Repro:**
  - **Pause menu:** Esc, Down (Restart), then Enter twice quickly.
  - **Main menu:** Enter twice quickly on Play.
- **Expected:** one scene load.
- **Actual:**
  - `sceneLoaded` fires twice, about 0.4 s apart: HoleSimulator at t = 332.26 and 332.64.
  - `[UdpShotReceiver] Listening…` is logged twice.
  - On "Play a Round", `PlayRound` runs twice: the round starts, then is restarted by the second load.
  - The same applies to a mouse double-click on a card or button.

### Game server

#### GS-1 Auth bypass: `/api;/…` and `/%61pi/…` skip the token check
- **Status:** Fixed (2026-10-02). `ApiTokenFilter` is now deny-by-default: every path needs the Bearer token except the exact allow-list `/actuator/health{,/liveness,/readiness}` and `/ws` (own `?token=`), and non-normalised paths (`;`, `//`, `.`/`..` incl. `%2e`, encoded slashes, backslashes, control chars, bad escapes) get 400 first. Check: `AuthBypassTest` (44 cases incl. every QA variant, `/API/`, `//`, `..`, `%2F`).
- **Severity:** blocker
- **Component:** `auth/ApiTokenFilter.shouldNotFilter`. It checks the raw `getRequestURI()`, but Spring routes on the normalised path.
- **Repro (no token):**
  - `curl -s -w '%{http_code}\n' http://localhost:8080/api/players` gives 401. That is correct.
  - `curl -s -w '%{http_code}\n' 'http://localhost:8080/api;/players'` gives **200** with the full list.
  - `curl -s -w '%{http_code}\n' 'http://localhost:8080/%61pi/leaderboard'` gives **200**.
  - `curl -X POST -H 'Content-Type: application/json' -d '{"players":["Intruder"],"holes":1}' 'http://localhost:8080/api;/games'` gives **201**. The game is created and the current one is abandoned.
  - `curl -X POST 'http://localhost:8080/api;/games/2/end'` gives **200**.
- **Expected:** 401 for every `/api/**` route.
- **Actual:** every endpoint, writes included, works without a token.

#### GS-2 Simultaneous final `holeScore`s leave the game IN_PROGRESS forever (no `gameFinished`)
- **Status:** Fixed (2026-10-02). `recordScore` locks the game row (`SELECT … FOR UPDATE`) before upserting, so scores for one game are serialised and exactly one sees the card complete; `end` and `start` lock it too. Check: `ConcurrencyTest`, `GameSocketInputTest.simultaneousFinalHoleScoresBroadcastGameFinishedOnce`; `race.py`/`ws2.py` now 0/10 stuck.
- **Severity:** major
- **Component:** `GameService.recordScore`. The `countByGame >= players*holes` check runs per READ COMMITTED transaction, so concurrent transactions can't see each other's inserts.
- **Repro:**
  1. `POST /api/games {"players":["W1","W2","W3","W4"],"holes":1}`.
  2. Over 4 `role=sim` sockets, send `holeScore` for W1–W4 at the same moment. Alternatively, send 4 parallel `POST /api/games/<id>/scores`.
  3. `GET /api/games/<id>`.
- **Expected:** FINISHED, winners set, and one `gameFinished`.
- **Actual:**
  - All players have `holesPlayed: 1`, but the status stays `IN_PROGRESS` and no `gameFinished` is sent.
  - This happened in 10/10 WS trials and 9/10 REST trials (scripts `ws2.py`, `race.py`).

#### GS-3 Concurrent requests return 500 instead of a clean result
- **Status:** Fixed (2026-10-02). Same row lock makes the score upsert race-free (all 200, last write wins); `start` takes a Postgres advisory lock so concurrent starts each get 201 and the last stays IN_PROGRESS; any leftover `DataIntegrityViolationException` maps to 409 JSON. Check: `ConcurrencyTest`.
- **Severity:** major
- **Component:** `GameService.recordScore` (a check-then-insert upsert) and `GameService.start`. Neither handles `DataIntegrityViolationException`.
- **Repro:** `race.py`.
  - (a) Send 6 parallel identical `POST /api/games/<id>/scores {"player":"D1","hole":1,"par":4,"strokes":3}`.
    - One returns 200 and five return **500**.
    - Log: `duplicate key … hole_scores_game_id_player_id_hole_number_key`.
    - PROTOCOL.md calls this an "idempotent upsert; safe to resend".
  - (b) Send 6 parallel `POST /api/games`.
    - One returns 201 and five return **500**.
    - Log: `games_single_in_progress_uk`.
- **Expected:**
  - Scores: 200, last write wins.
  - Game starts: serialised, or a 409.
- **Actual:** 500s, plus ERROR stack traces in the log.

#### GS-4 A game ended early as FINISHED crowns the player with the fewest holes played
- **Status:** Fixed (2026-10-02). Rule: winners and stats only count complete cards (a score on every hole) in FINISHED games; ending early as FINISHED is still allowed (no winner if nobody completed). Documented in PROTOCOL.md. Check: `StatsServiceTest.partialCardsNeverWinOrFeedStats`, `earlyFinishWithNoCompleteCardHasNoWinner`.
- **Severity:** major
- **Component:** `GameView.of` / `Scoring.winners` and `StatsService` (bestTotal, averages, bestRounds, handicap). They rank on raw strokes and ignore `holesPlayed`.
- **Repro:**
  1. `POST /api/games {"players":["Pro","Quitter"],"holes":9}`.
  2. Post Pro 3 strokes on par 4 for holes 1–9, and Quitter 8 strokes on hole 1 only.
  3. `POST /api/games/<id>/end {"status":"FINISHED"}`.
- **Expected:** Pro wins (27, -9). Alternatively, incomplete cards are excluded, or FINISHED is refused.
- **Actual:**
  - `winners: ["Quitter"]`.
  - `/api/players/quitter` shows `wins:1`, `bestTotal:8` and `averageTotal:8.0` for a "9-hole" round.
  - Partial rounds also feed the handicap, which scales by `holesCount`, not `holesPlayed`.

#### GS-5 A 40-char WS device name ending in an emoji stops every client from getting `hello`
- **Status:** Fixed (2026-10-02). Device names go through `Names.deviceName`: NFKC, control/invisible chars dropped, cut to 40 code points (never inside a surrogate pair), blank falls back to the default. Check: `NamesTest`, `GameSocketInputTest.emojiDeviceNameAtTheLimitDoesNotBreakHelloForAnyone`; `ws_surrogate.py` all get `hello`.
- **Severity:** major
- **Component:** `TokenHandshakeInterceptor`. `name.substring(0, 40)` splits a surrogate pair, and every `hello` then fails to encode. `WsHub.write` swallows the failure.
- **Repro:** `ws_surrogate.py`.
  1. Keep `ws://…/ws?token=…&role=remote&name=` + 39×"A" + `%F0%9F%98%80` open.
  2. Connect a sim and another remote.
- **Expected:** the name is truncated on a code-point boundary and everyone gets `hello`.
- **Actual:**
  - No one gets `hello`.
  - Log: `WARN WsHub: Send to REMOTE AAAA…? failed: Encoding error [MALFORMED[1]]`.
  - It recovers only when that remote disconnects.

#### GS-6 A player named "İvan" locks out "ivan" and "IVAN" with a 500
- **Status:** Fixed (2026-10-02). New stored `players.name_key` (Flyway `V2__player_name_key`, Java-computed full case fold incl. İ/ı/ß) with a unique index replaces `lower(name)`; lookups use the same key, so İvan/ivan/IVAN are one player. Check: `RequestErrorsTest.turkishDottedCapitalIMatchesTheSamePlayer`, `NamesTest`.
- **Severity:** minor
- **Component:** `PlayerRepository.findByNameIgnoreCase` uses `upper()`, but the unique index uses `lower()`.
- **Repro:**
  1. `POST /api/games {"players":["İvan"],"holes":1}` returns 201.
  2. Repeat with `"ivan"` or `"IVAN"`. It returns **500**: `duplicate key … players_name_ci_uk`.
- **Expected:** it matches the existing player, or returns a clean 400.

#### GS-7 Wrong HTTP method or content type returns 500 and logs an ERROR stack trace
- **Status:** Fixed (2026-10-02). `GlobalExceptionHandler` returns any Spring `ErrorResponse` exception with its own status and headers: 405 (with `Allow`) and 415 JSON, no ERROR log. Check: `RequestErrorsTest.wrongMethodIs405WithAllowHeader`, `wrongContentTypeIs415`.
- **Severity:** minor
- **Component:** `GlobalExceptionHandler`. Its catch-all swallows Spring's 405 and 415 exceptions.
- **Repro:**
  - `curl -X DELETE -H 'Authorization: Bearer golf-sim-dev-token' localhost:8080/api/games/1` gives 500. PUT, PATCH and `POST /api/ping` do the same.
  - `-X POST -H 'Content-Type: text/plain' -d x …/api/games` gives 500.
- **Expected:** 405 and 415.

#### GS-8 Malformed input gives 500 instead of 400
- **Status:** Fixed (2026-10-02). NUL and other control chars fail the new `@PlayerName` check (400); the WS handshake validates the raw query (URI syntax and every `%` escape) and refuses with 400. Check: `RequestErrorsTest.badNamesAre400`, `GameSocketInputTest.malformedQueryIs400AndHarmsNoOne`.
- **Severity:** minor
- **Component:** name validation and `TokenHandshakeInterceptor`.
- **Repro:**
  - `POST /api/games {"players":["a\u0000b"]}` gives 500 (`invalid byte sequence for encoding "UTF8": 0x00`).
  - The WS upgrade `?token=golf-sim-dev-token&role=remote&name=%zz` gives HTTP 500 (`URISyntaxException: Malformed escape pair`).
- **Expected:** 400 in both cases.

#### GS-9 WS frames over about 8 KB close the connection (1009)
- **Status:** Fixed (2026-10-02). `GameSocketHandler` takes partial frames and joins them in `MessageAssembler`, capped at 65,536 chars; a longer message gets `error "Message too large (max 65536 characters)"` and the connection stays open. Documented. Check: `GameSocketInputTest.oversizedMessagesGetAnErrorAndTheConnectionStaysOpen`.
- **Severity:** minor
- **Component:** the WebSocket container's text buffer, left at the 8 KB default.
- **Repro:** as a sim, send `{"type":"state","screen":"<8,200 chars>"}`.
- **Expected:** PROTOCOL.md says invalid input gets an `error` and "the connection stays open". Alternatively, document the limit.
- **Actual:**
  - The connection closes with 1009, and remotes get `simStatus connected:false`.
  - An 8,000-character screen is fine.

#### GS-10 Relay validation lets non-finite and wrong-typed values through to the sim
- **Status:** Fixed (2026-10-02). Every WS message is checked for non-finite numbers anywhere in the tree (`JsonChecks`), and Jackson is strict: `accept-float-as-int: false`, `allow-coercion-of-scalars: false`, `strict-duplicate-detection: true` (REST too). Check: `GameSocketInputTest.nonFiniteAndWrongTypedValuesAreNotRelayed`, `duplicateKeysAreRejected`, `RequestErrorsTest`.
- **Severity:** minor
- **Component:** `WsMessage` validation plus the byte-for-byte relay.
- **Repro:** the remote sends any of these:
  - `{"type":"shot","speed":1e400,…}`. It parses as +Infinity, passes `@Positive`, and is relayed. `{"type":"aim","delta":1e400}` is relayed too.
  - `"id":1.9`. It is relayed.
  - `{"type":"type","type":"nav","key":"up"}`. The last key is validated, but the raw text is relayed.
- **Expected:** rejected with `error`.
- **Actual:** all of these reach the sim.
  - The Unity sim's `RemoteShotMessage.TryParse` rejects non-finite shot values, so the ball is safe.
  - Other clients are not protected.

#### GS-11 Hard-coded `container_name` stops a second compose project from running
- **Status:** Fixed (2026-10-02). Removed both `container_name`s and the fixed `image:` tags from `Game-server/docker-compose.yml`; nothing else referenced them. README shows `PORT=8081 docker compose -p gsfix up`. Verified with `-p gsfix` on port 18080.
- **Severity:** polish
- **Component:** `Game-server/docker-compose.yml`.
- **Repro:** `docker compose -p bughunt config | grep -E 'container_name|image:'` shows `game-server`, `golf-postgres` and `golfsim/game-server:local`.
- **Expected:** `-p` gives an isolated stack.
- **Actual:** the container names clash and the build re-tags the shared image. An override file was needed.

#### GS-12 Name validation counts UTF-16 units and accepts invisible or control-character names
- **Status:** Fixed (2026-10-02). `@PlayerName` (`Names`): NFKC, 1–40 code points, no control/invisible chars, at least one visible char; message states the real minimum. `holes: 1.7` and `"9"` are 400 "Invalid value for 'holes'". Check: `NamesTest`, `RequestErrorsTest`.
- **Severity:** polish
- **Component:** `GameRequests.StartGame`.
- **Repro:**
  - 21 emoji give 400 "size must be between 0 and 40".
  - `"​"` and `" "` give 201, and the players look blank.
  - `"tab\tname"` and `"new\nline"` are stored.
  - `{"holes":1.7}` is accepted as 1 hole.
  - The error text says the minimum is 0, but the documented minimum is 1.
- **Expected:** count code points, and reject blank-looking or control-character names and fractional holes.

#### GS-13 Stats: par-1 hole-in-one counted twice, and negative averages round toward zero
- **Status:** Fixed (2026-10-02). Tallies are disjoint (eagle, birdie and par need strokes > 1), so a par-1/par-2 ace is only an ace; averages and the handicap round with `BigDecimal` HALF_UP (halves away from zero: -2.75 → -2.8). Documented. Check: `StatsServiceTest.aceOnAnyParCountsOnlyAsAnAce`, `negativeAveragesRoundLikePositiveOnes`, `HandicapTest.negativeIndexRoundsHalfAwayFromZero`.
- **Severity:** polish
- **Component:** `RoundRepository.findHoleTallies` and `StatsService.average`.
- **Repro:**
  - **Double count:**
    - A `{"par":1,"strokes":1}` score counts as both `holesInOne` and `pars`.
    - With 8 holes played, the categories sum to 9.
    - `mostBirdies` also counts it as a birdie.
  - **Rounding:** 1-hole rounds at -3, -3, -3, -2 give `averageToPar -2.7`, while +3, +3, +3, +2 give `2.8` (`Math.round` rounds half-up).

### Python generator

#### PY-1 `gen_hole.py` crashes on a negative seed, and the Unity Generator window allows one
- **Status: Fixed.** `--seed` must be >= 0 (clean argparse error); `generate_hole` raises ValueError on a negative seed; the Generator window clamps its seed field to >= 0.
- **Severity:** minor
- **Component:** `Tools/course_gen/gen_hole.py`, and `CourseGeneratorWindow.cs`, whose seed `IntField` has no clamp.
- **Repro:** `course_prep/.venv/bin/python course_gen/gen_hole.py generate --no-model --seed -5 --out /tmp/x`
- **Expected:** a clean argparse error, or a clamp.
- **Actual:** a traceback, `ValueError: expected non-negative integer` (numpy bit_generator).

#### PY-2 `--set water=nan`, a non-numeric value or a missing value crashes with a raw traceback
- **Status: Fixed.** `--set` items are parsed by an argparse type built on `style.override_value` (shared with `apply_overrides`): unknown names, missing / non-numeric / NaN / inf values are clean errors; finite values are clamped to 0..1 as before.
- **Severity:** polish
- **Component:** `gen_hole.py` `parse_overrides` and `style.py` `apply_overrides`. `np.clip` passes NaN through.
- **Repro:**
  - `--set water=nan` gives `ValueError: cannot convert float NaN to integer`.
  - `--set tree_density=abc` and `--set water` also give tracebacks.
- **Expected:** a clean error, like the one for an unknown parameter name.

#### PY-3 `--keep-unrated 0` or a negative value silently deletes every other unrated hole
- **Status: Fixed.** `--keep-unrated` must be >= 1 (it counts the new hole); `prune_unrated` raises ValueError for keep < 1 instead of deleting.
- **Severity:** polish
- **Component:** `gen_hole.py` `prune_unrated` (`unrated[max(0, keep - 1):]`).
- **Repro:**
  1. Generate 3 holes into an empty `--out`.
  2. Generate a 4th with `--keep-unrated 0`.
- **Expected:** negative values are rejected, and the meaning of 0 is documented.
- **Actual:**
  - The output says "Pruned 3 old unrated hole(s)".
  - `-1` and `1` behave the same as 0.
  - The default `--out` is `Assets/CourseData/generated`, so a typo wipes that folder.

### Tested, no bugs found (round 1)

- **Club presets from the tee:** all 7 are sensible.
  - Driver 269 yd carry, 34 yd apex, 6.8 s hang.
  - Wedge 134 yd, 32 yd apex.
  - No NaN. The ball never ends below the terrain (rolling clearance = ball radius).
- **Extreme shot values:**
  - Speeds of 0, 2, 200 and 1000 mph.
  - Launch angles of -90, -5, 60 and 90°.
  - 0 and 12k rpm backspin, ±3000 rpm sidespin, ±15° direction.
  - Negative speed and NaN are unreachable: remote shots are validated and clamped, and the sliders stop at 2 mph.
- **Wind:** 30 mph from all 8 compass points plus 359°.
  - The direction convention is correct: headwind shortens, and wind from E pushes the ball left on this north-running hole.
- **Stress run:** 300 random shots and putts from random spots, aims and winds.
  - No NaN, no ball under the terrain, no simulation over 12.3 s.
  - Results were 252 Stopped and 48 OutOfBounds.
- **Lies:** hits from fairway, rough, bunker, native, scrub, woods and tee all complete normally. See U-7 for the lack of a lie penalty.
- **Putts:** 0.5–25 m putts.
  - Rolling distance scales with speed (Stimp-like), and putts break on slope.
  - Holing out works: status Holed, ball dropped in the cup. The next hit restarts from the tee.
- **OOB:** off the tile in the air or rolling gives OutOfBounds. The next hit restarts from the tee.
- **Pause:**
  - Esc mid-flight freezes the ball (timeScale 0), disables the shot panel and fly camera, and gates remote shots ("The sim is paused").
  - Resuming finishes the shot at exactly the same spot as an unpaused shot.
- **Menu flow:** main menu → Hole Simulator → Esc → Restart (3×) → Main Menu → back, repeated.
  - No leaks: 7 GameObjects on the menu and 22 on the hole every time, with one RoundDirector, one camera and one tracer.
  - timeScale is back to 1 and NavInput still works.
  - No console errors or exceptions during the whole session.
- **Game server:** see the fork report. Covered:
  - Auth (no, wrong or lowercase token, on REST and WS).
  - REST validation: bad JSON, players and holes limits, names, case-insensitive duplicates, `limit`, unknown ids and routes.
  - Score validation, ending a game twice (409), 18-hole games with unicode names, auto-FINISH.
  - Stats maths checked by hand: handicap 7.0 and 8.0, averages, wins, mostBirdies.
  - Every documented WS error, ping/pong, simStatus, `hello` on reconnect, duplicate remote names, and 12 rapid holeScores from one sim.
- **Python generator:**
  - pytest: 43 passed, 24 skipped. The skipped tests need `TRAINER_TEST_DATABASE_URL`.
  - All 6 presets at par 3, 4 and 5, plus a 54-hole batch and 9 extreme-knob holes, all pass `prep_hole.py validate`.
  - Lengths are sensible.
  - In all 81 holes:
    - The pin is always on the green.
    - Water never overlaps the green, tee, fairway or bunkers.
    - No NaN heights.
    - Tee and pin are at least 20 m from the tile edge.
    - No objects sit on playing surfaces.
  - Generation is deterministic per seed.
  - `tree_density=1 water=1`, all knobs at 0 and all at 1 give sane holes.

### Not tested this round
- **Water hazard in Unity:** the Hole Simulator's hole has no water, and regenerating the scene's hole was off-limits.
- **Real keyboard input:** `simulate_key` events didn't reach the game while the Editor was unfocused, so input was driven through `NavInput.Push` and direct `ShotPanel` calls. Tab and keyboard Space/R were checked by code reading only.
- **Generator `rate`, `train` and `status`, and the Postgres trainer.**
