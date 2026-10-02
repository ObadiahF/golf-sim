# Bug hunt log: round 5

Moved from [bug-hunt.md](bug-hunt.md). Evidence: `/Users/obadiah/.claude/jobs/5fd17b43/tmp/qa5/`.

## Round 5 (regression sweep, 2026-10-02)

A quick re-test of rounds 1–4 after their fixes, looking for regressions and new bugs.
- **Server:** `docker compose -p qa5` on port 18095 (torn down with `down -v` afterwards). The sim was pointed at it at runtime, and at the local trainer (`e2etrainer`, rebuilt first). `GolfServer.asset` was restored before stopping Play, and `git diff` on it is empty.
- **App:** a Debug build on the iPhone 17 Pro and iPhone SE (3rd gen) simulators, driven with `idb`. Settings › Game server was `http://127.0.0.1:18095`, and has been cleared on both.
- **Playthrough:** game 1, 2 players (Obadiah, White Hayden), 3 holes, on the trainer's top 3 holes (`forest_621272063_f250a1`, `forest_563021450_515b36`, `lakes_189033389_fb40ac`). It ran from REST because the app only offers 9 or 18 holes.
  - Tee shots, the club wheel, putts, Replay, Mulligan, Pick up, pause, Sound and the scorecards were driven from the phone.
  - Approaches were placed or hit by `run_script` probes.
  - Games 2–6 covered End game, a new game over a game in progress, the dev-script checks and the iPhone SE.
- **Logging:** a passive WebSocket logger wrote every relayed message to `…/tmp/qa5/ws.log`.
- **Evidence:** `/Users/obadiah/.claude/jobs/5fd17b43/tmp/qa5/`.

### Automated suites

| Suite | How | Result |
|---|---|---|
| course_gen | `.venv` pytest `course_gen/tests` | **118 passed** |
| course_prep | `.venv` pytest `course_prep/tests` | **6 passed** |
| Game server | `docker compose -p qa5 run --rm test` | **143 run, 0 failures** (the first try, as `-p qa5test` with an empty `m2` volume, failed: the container couldn't resolve repo.maven.apache.org. That was a transient Docker DNS problem; the retry passed.) |
| Trainer | `docker compose -p e2etrainer run --rm test` | **118 + 71 passed** (course_gen + trainer) |
| iOS app | `xcodebuild test`, iPhone 17 Pro | **96 passed, 0 failed** (`TEST SUCCEEDED`) |
| `UdpShotReceiverCheck.Run` | edit mode | **ALL PASSED** (16 checks) |
| `PuttingCheck.Flat` / `.Green` | edit mode / Play, HoleSimulator | **8/8** / **ALL PASS (22)**. One 6 m putt lipped in: "holed mismatch", tolerated |
| `BugFixCheck.All` | Play, HoleSimulator practice | U-2, U-3, U-4, U-5 and U-9 **PASS**. U-1 throws `InvalidCastException`: a stale script (Q5-11). A patched copy passes U-1. `Menu` (M-1) was not run. |
| `ObstacleCheck` | Play, HoleSimulator | `All` **times out** (the pipeline's 30 s main-thread limit). All 9 entries run one by one **PASS**: TreeLine, OverTree, RockRoll, Canopy, Replay, Lies, Crowns (4519/4519), Drive (0 hits off drawn trees) and Stress (200 shots, 0 NaN/stuck/underground). |
| `ReplayCheck.All` | Play, HoleSimulator | Drive, approach, chip, tree and putt were filmed (no asserts; frames in `…/tmp/replay/`). Water: "no water found" on the built-in hole. |
| `E2EFixCheck` | Play, a round | **PASS:** E1Fit, E2UpSelect (×2), E2LeadIn, E3Badge, E4PickUp, E5Prompt, E12Check, E13Sound, E14Volume, E15Scores. **FAIL:** E2Lost (a harness problem, Q5-11); E11Putting 2 FAIL / 2 PASS on top hole #3 (Q5-1). |
| `RoundFixCheck` | Play | **PASS:** R3Restart, R7Holed, R8Names (R-8, R-9). **FAIL:** M3Description and S1Menu. Both are stale scripts (Q5-11); the same steps done from the phone work. |
| Generator | 30 new holes: 6 presets × 5 seeds, 10 each of par 3/4/5 | `prep_hole.py validate`: **30/30 ok**. `scan_launch.py`: **0 of 30 fail**. Green slope at the pin is at most 4.9 %. |

### Regressions and incomplete fixes

#### Q5-2 The putting meter still shows the last putt's strength for the same player's next putt (E-9)
- **Severity:** minor
- **Component:** `Golf-app/Session/PuttMeter.swift`, `SwingSession.swift` (`meter.reset()` runs on `turn` and on Address only).
- **Repro:** default turn order (each player plays the whole hole). Putt from the phone and miss, then look at the putting view for your next putt.
- **Expected:** an empty meter (E-9's fix).
- **Actual:** the meter still reads "6.4 m", filled to the old level, and the ball shows RE-ADDRESS, so the player never addresses again.
  - The sim sends no `turn` between one player's own strokes, so E-9's reset never runs.
  - E-9 was only fixed for a change of player.
- **Evidence:** `09-putt1-phone.png`, `10-phone-meter-stale-same-player.png` ("Putted 5.9 m of 11.0 m", meter 6.4 m, target 7.1).
- **Status:** Fixed (2026-10-02): `PuttMeter.follow` also notices a new stroke: when the sim can take a swing again (`canShoot` not false) with a different player, hole or `strokes` than at the last one, the meter empties (the last putt's "Putted … of …" stays). While the putt rolls (`canShoot: false`) it keeps the struck value. Checked: tests `RegressionFixTests.theSamePlayersNextPuttStartsEmpty` / `theSessionEmptiesTheMeterForTheNextStroke`, and on the 17 Pro with a fake sim (putt, then `strokes` 1→2 with no `turn`: meter 0.0 m, "Putted 5.9 m of 11.0 m"), `…/tmp/appfix5/q5-2-*.png`.

#### Q5-3 With the Sound panel open, the phone's remote shows the main menu's hint (E-13, phone side)
- **Severity:** minor
- **Component:** `Golf-app/Views/Game/RemoteView.swift` `hint(_:)` / `screenTitle(_:)` (neither has a case for `settings`).
- **Repro:** in a round, Menu → Down, Down → OK (Sound). The sim sends `screen:"settings"`, as E-13 now does.
- **Expected:** "Up/Down to choose, Left/Right to change, Back to close", as on the TV.
- **Actual:**
  - The title is "Settings".
  - The hint is "Left/right to choose, OK to play. Add players and start a round in Players.", which is the main menu's text.
  - The same happens from the main menu's Sound card.
- **Evidence:** `22-sound.png` (TV), `22-phone-sound.png`.
- **Status:** Fixed (2026-10-02): `RemoteView.hint` / `screenTitle` cover every screen in PROTOCOL.md (`menu`, `courseSelect`, `loading`, `game`, `paused`, `settings`, `replay`, `holeComplete`, `scorecard`, `results`), and an unknown screen gets a general D-pad hint, not the menu's. `settings` is titled "Sound" with "Up/Down choose · Left/Right change · Back close". Checked: `RegressionFixTests` (hints per screen), `q5-3-sound-*.png`.

### New bugs

#### Q5-1 Top hole #3 has a green tilted 15 % at the pin: putts can't stop, and "plays like" reads 0.0 m
- **Severity:** major (every 9-hole round plays this hole, and two of the top 13 are affected)
- **Component:** generator v3 legacy packages; the trainer's `hole_checks.py` / `check-holes` (they check only the tee shot); `PuttModel` "plays as".
- **Repro:** play a round with the top holes, so hole 3 is `lakes_189033389_fb40ac` (2 👍 0 👎). Get on the green near the pin.
- **Expected:** a puttable green (generator v4 greens: 2–5 %), or the hole kept out of Top holes like the tee-shot failures.
- **Actual:**
  - At 4 m: "42 cm downhill, plays like **0.0 m**". The phone's meter target is 0.0 and the TV's putt card says 0.0 m.
  - The break line curls away from the cup.
  - A putt struck at the meter's read (speed 0) rolled 7.99 m off a 4 m putt. Scripted 2 m putts for both players ran off until par + 5.
  - `E2EFixCheck.E11Putting` fails on this hole ("green 4.0 m from the pin: putting True, playsAs 0").
- **Measured from the packages** (`…/tmp/qa5/green_slope.py`, heightmap gradient inside the green polygon):
  - **Hole 3:** slope at the pin 15.0 %, and 42 % of the green is steeper than 8 %. The Unity sample agrees: median 4.2 %, p90 15.0 %.
  - **Trainer volume:** 10 of 417 holes have greens at a flat 15.0 %, all generator v3: `lakes_189033389_fb40ac` and `mountain_972730698_505a54` (both in `/api/game/top-holes`, ranks 3 and 13), plus `forest_407456023_69fce0`, `forest_572870181_15d132`, `mountain_109178238_9d4698`, `mountain_169653423_3cf127`, `mountain_425418887_7836c7`, `mountain_520846937_5c6070`, `mountain_648737113_f418bf` and `parkland_275656861_11828c` (pool holes, still served).
  - The exact 15.0 % matches `terrain._pond_bed`'s bank, which slopes "away at ~15 % beyond" a pond and takes `max(h, bank)` over the green pad. That is a likely cause, not confirmed.
  - The 30 new v4 holes are fine (pin slope at most 4.9 %), so this is legacy data with no guard.
- **Suggested:** add a green-slope check to `hole_checks` / `check-holes` (and `validate.py`), so steep greens leave Top holes. Show "—" rather than "0.0 m" when no speed stops near the hole.
- **Evidence:** `27-tv-playsas-zero.png`, `27-phone-playsas-zero.png`, `trainer_green_slopes.txt`, `gen2_slopes.txt`.
- **Status:** Fixed (2026-10-02), generator and trainer. The "plays like 0.0 m" display was not changed.
  - **Cause (confirmed):** `terrain.sculpt` dug the pond beds last, and `_pond_bed` raised everything below its bank plane (water level + 0.4 m, then falling 15 % per meter beyond 10 m) with `max(h, bank)`. A green lower than the bank of a nearby pond was replaced by the bank's 15 % plane. Re-sculpting `mountain_202_d16117` without the banks gives 2.8 % at the pin instead of 15.0 %. v4 still did this: QA's own v4 holes `mountain_202_d16117` (15.0 % at the pin), `mountain_500005_e41a4d`, `lakes_202_a3a919` and `forest_500005_6b92be` all have 15 % on the green.
  - **Generator v5** (`GENERATOR_VERSION` 5):
    - Ponds are shaped before the tee and green pads, which now win and never touch pond cells.
    - The green pad reaches 1 m (plus one sample) past the green, so its slopes start off the putting surface.
    - The roll on the green is gentler (2 octaves, 0.12 m) and fades out within 1.5–12 m of the pin, which sits on the plain 2.5 % tilt.
    - Bunkers no longer dig into the green on coarse grids.
    - A new `validate.green_problems` retries the layout when the green is over 4 % within 2 m of the pin or over 6 % anywhere more than 1 m inside its edge.
    - Sweep of 240 holes (6 presets × 40): at most 2.8 % at the pin and 5.0 % anywhere. Before: up to 15 %.
  - **Scanner:** `scan_playability.py` runs the launch and green checks (`green_profile`). `scan_launch.py` is the same scan under its old CLI name and exports. It lists 21 of QA's 30 v4 holes against the strict generator caps.
  - **Trainer:**
    - `hole_checks` also judges the green: a hole is unplayable when it is over `TRAINER_GREEN_PIN_MAX_SLOPE` (default 0.06) within 2 m of the pin.
    - Migration 005 adds `check_version`, `green_pin_slope` and `green_max_slope` to `hole_checks`. Verdicts older than `CHECK_VERSION` 2 are re-checked on startup, and on the spot when ranking or serving.
  - **Local stack:** the rebuilt `e2etrainer` re-checked 408 holes and found 22 unplayable (4 tee shot, 18 green):
    - The 10 listed above.
    - Two more pond-tilted v3 holes the pin-cell sample missed: `lakes_298869228_10ab41` (15.0 %) and `lakes_100362255_c47b1c` (10.8 %).
    - Six v3 holes at 6.1–6.8 % near the pin: `forest_914646951_b31e15`, `lakes_83407493_c6c380`, `forest_874551526_a0a9b7`, `lakes_181870223_fcdee0`, `forest_526293665_baeb16` and `forest_210394811_4a0620`. A cutoff of 0.07 would keep these.
    - `/api/game/top-holes` no longer has `lakes_189033389_fb40ac` or `mountain_972730698_505a54`.
  - **Tests:** `course_gen/tests/test_greens.py` (87: 84 seeds across all presets, 30 of them lakes, each must pass on its first 2D-valid layout; the banked-pond regression; the validator; the scanner on disk). New `test_hole_checks` cases cover the green cutoff, a steep green leaving Top holes and the game API, and outdated checks being redone.

#### Q5-4 The phone's remote doesn't say that Up replays on the scorecard
- **Severity:** polish
- **Component:** `RemoteView` (holeComplete / results with `canReplay: true`).
- **Actual:** the TV shows "▲ Replay", but the phone says only "Press OK for the next hole." Up does work.
- **Evidence:** `15-hole1-scorecard.png`, `15-phone-hole1-scorecard.png`.
- **Status:** Fixed (2026-10-02): on `holeComplete` / `results` with `state.canReplay: true` the hint adds "▲ Replay last shot" and a **Replay last shot** button (the gameplay view's, now a shared `ReplayButton`) sits beside Next hole/OK and sends `nav up`. These buttons show on a scorecard screen even before the scorecard has loaded. Checked: `RegressionFixTests.scorecardOffersReplayOnlyWhenTheSimSays`, a tap logged `nav up` at the fake sim, `q5-4-*-scorecard-replay.png`.

#### Q5-5 The Scores tab opens on an empty abandoned game, and holes 7–9 hide behind TOT
- **Severity:** polish
- **Component:** `Golf-app` Scores › Scorecard.
- **Actual:**
  - After ending game #2 with End game (no holes played), Scores shows "ENDED EARLY · GAME #2" with all dots and 0 E. The finished game #1 isn't shown.
  - A 9-hole card shows holes 1–6. Holes 7–9 sit under the pinned TOT/± columns until you swipe the row sideways, and there is no scroll hint.
  - "White H…" is truncated on the 3-hole cards, though half the row is empty.
- **Evidence:** `31-phone-scores.png`, `32b-phone-scores-swiped.png`, `28-phone-final.png`.
- **Status:** Fixed (2026-10-02): Scores opens on the game in progress, else the latest finished game (from the last 10), else the latest game (`ScoresView.shownGame`). The hole columns narrow (28 down to 20 pt) so a whole nine, TOT and ± fit on a 17 Pro, and names get the room a short card leaves (`ScorecardTable.Columns`); only an 18-hole nine with its OUT/IN, or a narrower phone, still scrolls. Checked: `RegressionFixTests` (scores default, column fit), `q5-5-17pro-scores.png` (finished game #1 over abandoned #2, holes 1–9 shown), `q5-5-se-scores.png`.

#### Q5-6 The pick-up toast overlaps the HUD card
- **Severity:** polish
- **Component:** `RoundHud` toast vs the stats card.
- **Actual:** with a long lie ("Woods −15%") the card ends at about x = 405 of 1280, and the centred "Obadiah picks up: 10" toast starts at about x = 385.
- **Evidence:** `25-pickup-hud.png`.
- **Status:** Fixed (2026-10-02): the stats card, the toast and the turn badge share one top row (`hud-top` in `RoundHud.uxml` / `.uss`). The toast sits in the space between the card and the badge, centred there, at most 760 px wide, and wraps if it needs to. The putt card (`PuttingHud`) is unchanged. Checked: the new `Tools/unity_scripts/HudLayoutCheck.cs` builds the HUD off screen at 1920×1080, 1280×720, 1920×1200, 1280×800 and 2560×1600, with "Woods −15%", a long name on the badge, a short toast and a two-line toast. All 10 pass, and the old layout fails all 10. A real pick-up in a 2-player round looked right too.

#### Q5-7 On the iPhone SE the Last shot / Last putt card is cut off behind the pinned controls
- **Severity:** polish
- **Component:** `ScreenScaffold` (E-7's pinned `bottom:` slot).
- **Actual:**
  - Only the top few points of "LAST SHOT" / "LAST PUTT" show above Menu / Mulligan / Pick up.
  - It does scroll into view, but nothing hints that it can.
  - Every control stays reachable, so E-7 holds.
- **Evidence:** `50-se-state.png`, `52-se-putting.png`.
- **Status:** Fixed (2026-10-02): `ScreenFit.levels` go below `small` (down to -1) and at those levels the aim control goes compact (shorter buttons, no "tap to reset"), so on the SE the content fits above the pinned controls, including while the Replay button shows. The scroll fallback is only for a banner on top of that, and it uses the smallest sizes. Checked: `q5-7-gameplay-se.png`, `q5-7-putting-se.png`, `q5-7-se-*-replay.png` (Last shot / Last putt fully visible); the 17 Pro keeps full sizes (`q5-7-*-17pro.png`).

#### Q5-8 The first Simulate swing tap only addresses (Debug only)
- **Severity:** polish (Debug tool)
- **Component:** `SwingSession.simulateSwing()`. From idle it calls `address()` and plays the synthetic swing straight away, while the session is still `settling`, so the swing is swallowed.
- **Actual:** the first tap of a session leaves "Ready. Swing away" and sends nothing; the second tap hits.
- **Evidence:** `04-phone-after-swing.png`; `ws.log` has no state change until the second tap.
- **Status:** Fixed (2026-10-02): when not addressed yet, `simulateSwing()` addresses and queues a still lead-in (settle delay + hold) before the synthetic swing (`SimulatedMotionSource.play(_:after:)`), so the address pose is set before the swing plays. Checked: one tap sends one `shot` on the 17 Pro simulator (about 3.5 s later).

#### Q5-9 A WebSocket `NaN` error message exposes Jackson internals
- **Severity:** polish
- **Component:** `Game-server` WS validation.
- **Actual:** `{"type":"aim","delta":NaN}` → `error "Invalid JSON: Non-standard token 'NaN': enable JsonReadFeature.ALLOW_NON_NUMERIC_NUMBERS to allow"`. It is refused correctly (GS-10 holds), but the text is a library hint, not a user message.
- **Status:** Fixed (2026-10-02): `JsonChecks.parseError` maps parse errors to clean messages. `NaN` / `Infinity` / `-Infinity` give `Invalid number`, and any other syntax error gives `Invalid JSON`. Duplicate keys keep the documented `Invalid JSON: Duplicate field '<key>'`. `GameSocketHandler` uses it, and PROTOCOL.md lists the messages. Checked: `GameSocketInputTest.parseErrorsAreRefusedWithoutLibraryText` (7 cases, no Jackson text, nothing relayed); `docker compose -p q5fix run --rm test`: 150 run, 0 failures.

#### Q5-10 Trainer: Esc doesn't close Top holes, and the open dialog blocks Log out
- **Severity:** polish
- **Component:** `Tools/course_trainer/web` Top holes dialog.
- **Actual:** Esc leaves it open. Only L or Close shut it, while its backdrop swallows clicks on the HUD.
- **Evidence:** `41-trainer-top-landscape.png`.
- **Status:** Fixed (2026-10-02): Esc closes Top holes and Controls (`TrainerView`). The top bar (name, Log out, Top holes, Controls) sits above the overlays' backdrop (z 45 > 40). On a landscape phone, the leaderboard sheet starts below the top bar instead of covering it. Checked with Playwright at :8765 as `e2e1`:
  - At 1280×800, 844×390 and 390×844, Esc closes the dialog, and Log out and Close are both hit-testable with it open.
  - At 844×390, a real click on Log out with the dialog open logged out.

#### Q5-11 Dev scripts have gone stale
- **Severity:** tooling
- **Component:** `Tools/unity_scripts/`.
- **Details:**
  - `BugFixCheck.TracerReset` casts `BallTracer.line` to `LineRenderer`, but it is a `TracerLine` now (`line.Count`).
  - `ObstacleCheck.All` takes longer than the CLI's 30 s main-thread limit.
  - `RoundFixCheck.M3Description` and `S1Menu` push `NavInput` and read the result in the same call, but the menu handles input on its next frame now.
  - `E2EFixCheck.E2Lost` checks the HUD hidden in the same call that queued the replay, but `RoundHud.Render` hides it on the next frame.
  - `E2EFixCheck.FinishHole` places the ball and putts with a stale `AimDirection` (`PlaceOnGround` doesn't re-aim, and after a holed putt the aim points away), so its "2 m putts" miss until par + 5. Scripted scores (9/10/8) mean nothing.
  - None of these is a game bug: the behaviour each checks was confirmed by hand from the phone.
- **Status:** Fixed (2026-10-02). Each script compiles and prints PASS / FAIL. They were run in Play mode against a local server (`-p unityq5`, port 18098) with a 2-player, 3-hole game and a solo round.
  - **`BugFixCheck`:** U-1 reads `TracerLine.Count`. `All` gives U-1, U-2, U-3, U-4, U-5 and U-9 **PASS**, and `Menu` (M-1) gives **PASS** (1 scene load).
  - **`ObstacleCheck`:** `Drive` is now split into `Drive` and `DriveIrons`, and `Stress` into `Stress1`–`Stress4` (50 shots each). Each entry can be run on its own. `All` runs a few tests per call (about 5 s) and keeps its progress in `SessionState`: run it again until it prints the summary (`AllReset` starts over). It took 7 calls and all 13 **PASS**. The Mac was under heavy load (Docker), and one part still took 23 s.
  - **`RoundFixCheck`:** `M3Description` and `S1Menu` failed when an overlay was still open over the main menu. Scores, left open by `E2EFixCheck.E15Scores`, takes every key, which reproduces the failure. They now close any overlay with Back first, as the remote would, and select the card by kind. Results: M-3, S-1 (main menu and pause), S-2, R-3, R-6, R-7, R-8 and R-9 **PASS**.
  - **`E2EFixCheck`:**
    - `E2Lost`, `E2UpSelect` and `E3Badge` wait for the replay to start (`TickIntoReplay`). A shot into trouble queues its own replay after a lead-in.
    - `HudShown` reads the `hud--hidden` class.
    - `FinishHole` puts each player 2 m from the pin once, then reads every putt like a player. `ReadPutt` takes the aim the putting preview says holes it, at its solved strength. Example: "White Hayden 2.0 m aim +3.0° → holed". Scores are now strokes taken plus putts.
    - `E1Fit` says WAIT once the 1.5 s banner has gone.
    - E1Fit (turn and win), E2UpSelect (×2), E2LeadIn, E2Lost, E3Badge, E4PickUp, E5Prompt, E11Putting (6), E12Check, E13Sound, E14Volume and E15Scores all **PASS**.
  - **`ReplayCheck`:**
    - Each scenario now checks that the replay starts and ends, the camera stays at least 0.1 m above the terrain on every frame, the ball is inside the letterboxed frame on at least 70 % of frames (with a breakdown per camera shot), and nothing logs an error.
    - Frames now go to the project's `Temp/ReplayCheck/`. `All` only checks and doesn't render, so it stays under 30 s.
    - `Water` returns SKIP at once on a hole with no water.
    - Results: drive, approach, chip and putt **PASS**, water SKIP. **Tree FAIL:** the ball is in view on only 44 % of frames (down the line 36 %, tree camera 47 %). A short drive into the trees leaves the down-the-line frame, then bounces back toward the tree camera. This is a real framing finding, not a script problem. The 35° `Woods` shot passes at 100 %.
  - **A game bug the fixed `E2Lost` found:** after any replay, the HUD stayed hidden.
    - `RoundHud.Render` reset `hud-root`'s inline `display` with `StyleKeyword.Null`, and in this Unity (6000.6.4f1) that never re-resolves away from `None`.
    - Watched in real time: the HUD stayed hidden for over 30 s after a replay ended.
    - `Render` now toggles the `hud--hidden` class instead. The same check shows it back as soon as the replay ends.
    - The old harness missed this because it never let a frame pass.

### Re-checked and holding (rounds 1–4)
- **Round 4:**
  - E-1: "WHITE HAYDEN'S TURN" in full on a real turn, and `E1Fit` with "WHITE HAYDEN WINS!" (box 1472 for 1470).
  - E-2 and E-5: Up+OK quickly on the phone keeps the scorecard. A full replay from the scorecard hides the HUD and brings it back. No "▲ Replay" while the next hole loads.
  - E-3: after auto and phone replays the badge and HUD are back.
  - E-4: SCORE 10 and "Picked up: 10".
  - E-6 and `state.canReplay`:
    - The sim publishes `canReplay:true` only in the between-shots window (about 2 s) and on scorecards. It is false during auto-replays (tree, water), after a mulligan and while loading.
    - The phone's **Replay last shot** button showed exactly when it was true, and tapping it played the replay.
  - E-7: controls pinned on the SE.
  - E-8: End game confirm. Dismissing keeps the game; confirming abandons it.
  - E-11: rough is not putting mode. Short grass near the green and the green are putting mode.
  - E-12, E-13 (TV side), E-14, E-15: pass.
- **Round 3:** R-1 (new game during a round: game 4 ABANDONED, game 5 started), R-3, R-7, R-8/R-9, M-3 and S-1 (by hand), TH (top holes downloaded from the local trainer), C-1 (Crowns/Drive).
- **Rounds 1–2, server:**
  - GS-1: `/api;/…` and `//` → 400, `/%61pi/…` and `/API/…` → 401.
  - GS-5: a 40-character name with an emoji gets `hello`.
  - GS-7, GS-8: 405 / 400 bodies.
  - GS-9: 20 KB frames are relayed.
  - GS-10: NaN refused.
- **Rounds 1–2, trainer:**
  - T-1: Submit visible in phone landscape (844×390).
  - T-6: an old cookie after logout → 401.
  - T-8: a bad cookie → 401.
  - T-13: `/docs` → 404.
  - The Game API without a key → 401.
- **Trainer rules:**
  - `/api/game/top-holes?limit=100` returns 22 holes, none of them among the 4 `hole_checks` unplayable ones. `check-holes` and `check-holes --all` re-check 408 holes and list the same 4.
  - The refill rule (`pool._used_up`: one user has rated `ceil(size × 0.5)` of the newest finished batch, or seen all of it; unplayable holes don't count) matches the README. It is covered by `test_pool`. It wasn't triggered live, so as not to add votes to the local data.
- **Stats:** after game 1 (a 3-hole tie at 27), REST `/api/players` and `/api/leaderboard` show 1 win each and no Best 9/18 (3-hole rounds aren't rated). The phone and TV final cards match.
- **Console:** no Editor errors during the session. The only warning is the hosted-server retry.

### Not tested / notes (round 5)
- The hosted server still answers `404` on `/actuator/health`, and the app reports that as "Server refused the connection (token?)" (round 4 note). The hosted trainer answers `/healthz` 200.
- Not covered: a real iPhone, the trainer-down fallback (TH-4), a server kill mid-hole (done in round 4), and `BugFixCheck.Menu` (M-1).
- A temporary trainer user (`qa5tester`) was used for the login/logout checks and then deleted with its session and its one hole view. No votes were cast.

