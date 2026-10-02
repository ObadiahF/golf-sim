# Bug hunt log: round 3

Split from [bug-hunt.md](bug-hunt.md), which has the newer rounds. Evidence: `/Users/obadiah/.claude/jobs/5fd17b43/tmp/bughunt3/`.

---

## Round 3 (2026-10-02)

This round covered the Unity game's new features: multiplayer rounds against a real game server, instant replay, putting, tree and rock collisions, sound, top holes built at runtime, and the menus.
- **Server:** a private game server (`docker compose -p bughunt3`, port 18083, torn down afterwards).
  - The sim was pointed at it at runtime (`ServerConfig.useLocalServer`, `localServerUrl`), and the trainer at the local `e2etrainer` (http://localhost:8765). Both values were restored before stopping Play mode.
  - The phone was faked with Python websockets clients: `phone.py` sends nav, club, aim, shot, mulligan and skip, and `logger.py` records every relayed message to `ws.log`. Games were started and ended over REST.
- **Sim:** driven in the Editor with run_script probes, kept outside the repo:
  - `QA.cs`: rounds, shots, nav and pause.
  - `QAReplay.cs`: camera sweeps that judge each replay frame and render it.
  - `QAPutt.cs`: preview against the real ball, and putting-camera scans.
  - `QACol.cs`: colliders against the drawn trees.
  - `QAAudio.cs` (voices) and `QAMem.cs` (asset counts).
- **Evidence** (in `…/tmp/bughunt3/`): screenshots in `shots/`, replay renders in `replay/`, and the phone log in `ws.log`.

### Rounds and the game server

#### R-1 A new game from the app during a round drops the sim to the main menu, and the new game never starts
- **Severity:** major
- **Component:** `RoundDirector.cs`: `OnGameFinished`, `StartRound` and `OnSceneLoaded`.
- **Repro:**
  1. Start a game (`POST /api/games`) and hit a shot or two.
  2. Start another game from the app (`POST /api/games`). The server abandons the first game.
- **Expected:** the sim leaves the old round and loads hole 1 of the new game.
- **Actual:**
  - `gameFinished` (ABANDONED) ends the round and starts loading the menu. `gameStarted` then starts the new round and its top-holes fetch.
  - When the menu scene loads, `OnSceneLoaded` sees no ball and calls `EndRound()`, so `round` becomes null.
  - The fetch then finishes and throws `NullReferenceException` at `RoundDirector.cs:199` (`round.FirstUnfinishedHole()`).
  - The sim sits on the menu (state `menu`, gameId 0). The phone thinks the game started. The only way back is Play a Round → Resume.
  - Reproduced 3 times (games 1→2, 3→4, 5→6). It doesn't happen when the old game was already FINISHED.
- **Evidence:** the console NRE stack, and `ws.log` (03:33:53: `gameFinished` 1 then `gameStarted` 2).
- **Status:** Fixed (2026-10-02): a new game replaces the round in progress (`RoundDirector.StartRound` ends it first). A menu scene loading while the new round's holes download no longer ends it, a hole load requested during another scene's fade is queued, and a fetch that finishes for a replaced round is dropped. Checked: games 1→2 mid-hole, no exception, hole 1 of game 2 loads (`RoundFixCheck`).

#### R-2 A game ended from the app as FINISHED keeps playing on the sim, which keeps writing scores into it
- **Severity:** minor
- **Component:** `RoundDirector.OnGameFinished`: it only reacts to `ABANDONED`.
- **Repro:**
  1. Mid-round, end the game: `POST /api/games/2/end {"status":"FINISHED"}`.
  2. Finish the hole on the sim.
- **Expected:** the sim ends the round (a toast and the results, or the menu), as it does for ABANDONED.
- **Actual:** the round goes on with no notice, and the sim's `holeScore`s are stored in the finished game: Ann and Bob got `[6]` on hole 1 after the end. If the card is completed this way, it starts counting toward the stats.
- **Status:** Fixed (2026-10-02): `gameFinished` FINISHED for the sim's round, ended early from the app, stops play and shows the server's card as the final scorecard (toast "Game ended from the app"). Scores only go to a game the server reports in progress (`ServerTakesScores`). Checked: game 3 ended after one shot, the sim refuses shots, its scores stay empty.

#### R-3 Restart Hole on the final scorecard replays the last hole and overwrites a finished game's score
- **Severity:** minor
- **Component:** `HomeMenu` (Restart Hole) and `RoundDirector.OnSceneLoaded` → `StartHole`.
- **Repro:**
  1. Finish a 1-hole server game (game 7: picked up for 9; the server says FINISHED, winner Fin Test).
  2. On the final scorecard, press Back, Down, Select (Restart Hole), and play the hole again.
- **Expected:** Restart is unavailable or does nothing after the round is over. A finished game's result doesn't change.
- **Actual:** hole 1 restarts with the score cleared. The new score (6) is sent, and Postgres now has 6 for the FINISHED game. Anyone can rewrite a finished result, and the stats with it. Restart from a hole-complete scorecard also replays a completed hole for everyone.
- **Status:** Fixed (2026-10-02): Restart Hole is disabled (and skipped by Up/Down) on the hole-complete and final scorecards (`HomeMenu.CanRestart`). A hole scene reloaded after a finished round plays as practice. Checked on both scorecards; the finished game's scores were unchanged.

#### R-4 After a reconnect, the sim ignores a new game in `hello` and keeps playing the abandoned one
- **Severity:** minor
- **Component:** `RoundDirector.Awake` (`HelloReceived` only stores `serverGame`).
- **Repro:**
  1. Play game 8.
  2. Disconnect the sim (`Connection.Disconnect()`; in real life, the PC's Wi-Fi drops).
  3. Start game 9 from the app, then reconnect.
- **Expected:** the protocol says `hello` "has everything you need to resync": the sim should switch to game 9, or at least stop game 8.
- **Actual:** the sim keeps playing game 8. Every `holeScore` is rejected (`Server error: Game 8 was abandoned`), so the scores are lost, and game 9 never starts.
- **Status:** Fixed (2026-10-02): on `hello` the server's game wins (`RoundDirector.OnHello`): a different game in progress is started, a game that ended while offline stops the round (toast, menu). Checked: game 4 → 5 while disconnected (switched), and game 5 abandoned while disconnected (back to the menu, no rejected scores).

#### R-5 A healthy connection is dropped as "server stopped answering" after a minute of sim-only traffic
- **Severity:** minor
- **Component:** `SimConnection.SendLoop`.
  - It pings only after 20 s with nothing to send, and only then checks the silence (60 s since the last received frame).
  - The server never answers `state`/`turn`/`shotResult`. So while the sim keeps sending (aiming, turns, replays), no ping goes out, and the first 20 s lull trips the check.
- **Repro:** with no phone traffic, change the aim on the sim every 9 s for 70 s (`QA.AimNudge`), then leave it idle.
- **Expected:** the connection stays up.
- **Actual:**
  - 11 s into the idle period: `[SimConnection] Not connected …: server stopped answering. Retrying`, then a reconnect.
  - The phone sees `simStatus false/true`. A phone message in that second gets "no sim connected", and anything left in the outbox is discarded.
  - Reproduced twice (10:38:17 and 10:45:15 UTC).
- **Status:** Fixed (2026-10-02): `SimConnection.SendLoop` pings every `pingInterval` whatever else it sends, and gives up only after 3 intervals with nothing received (pongs count). Checked with a 2 s interval: 25 s of aim traffic then 12 s idle, the connection stayed up.

#### R-6 Between shots and during replays the sim says `screen: game`, so a swing from the phone silently disappears
- **Severity:** minor
- **Component:** `RoundDirector.ScreenName`: `BetweenShots` maps to `game`. The shot path also has no reply to the phone.
- **Repro:** send a phone shot that hits a tree, then swing again 1 s after its `shotResult`, while the 2.5 s delay and the 10 s replay run (`phone3.py`).
- **Expected:** the phone leaves swing mode (another screen, e.g. `replay` or `waiting`), or gets an error back.
- **Actual:** `state` stays `screen=game` the whole time. The second swing is refused with a toast on the TV only ("Wait for the next turn"), and the phone gets nothing.
- **Status:** Fixed (2026-10-02): `state` has `canShoot` and `waitReason` (between shots the screen stays `game` with `canShoot: false`), the screen is `replay` during an instant replay, and a refused phone shot gets `shotRejected {reason, id}` (new sim → remotes message in the server and PROTOCOL.md). The app dims its swing area with the reason, sends no swing while waiting, shows `shotRejected`, and shows the remote with "Instant replay" for `replay`. Checked with a fake phone: a swing during the tree replay got `shotRejected` "Wait for the replay to finish".

#### R-7 After holing out, `state` says putting with a 253 m "plays as"
- **Severity:** minor
- **Component:** `RoundDirector.Putting.cs`: `IsPutting`/`FillPutting`. The lie is `holed`/`""`, which isn't `tee`, and the ball sits in the cup, "on" the green.
- **Repro:** hole a putt.
- **Expected:** `putting: false` once the ball is in.
- **Actual:**
  - Every hole-out sends `{"screen":"game","lie":"holed","putting":true,"puttDistance":0,"puttPlaysAs":253.27}` during the turn delay.
  - `holeComplete` and `results` carry the same values.
  - The phone's power meter would target 253 m. Seen on every holed putt (`ws.log`).
- **Status:** Fixed (2026-10-02): `BuildState` only fills the putting fields while the player is still on the hole (not once holed or picked up, nor on the scorecards). Checked: a holed 2 m putt sends `lie: holed, putting: false, puttPlaysAs: 0`.

#### R-8 Player names are rendered as rich text
- **Severity:** minor
- **Component:** the UI Toolkit labels for names: the turn badge and banner, the scorecard, the menu's Resume line, and the Scores table (`enableRichText` is on).
- **Repro:** start a game with the players `<size=90>Huge`, `<color=red>Red</color>`, `a<br>b` and `O'Brien <b>&amp;`. The server accepts all four.
- **Expected:** names are shown literally.
- **Actual:**
  - The badge shows a giant "Huge".
  - The Scores table shows red text, a giant name and a line break.
  - `<b>` is swallowed ("O'Brien &amp;").
  - A friend can break the layout from the app.
- **Evidence:** `shots/rich_banner.png`, `shots/scores_best2.png`, `shots/menu_resume.png`.
- **Status:** Fixed (2026-10-02): rich text is off for every HUD, scorecard, banner, toast and menu label, and the Scores cells (`PlainText`). Checked: `<size=90>Huge` and `<color=red>…` show literally on the badge and the final scorecard.

#### R-9 Long names overlap the score columns
- **Severity:** polish
- **Component:** `ScoreTable` / `Menu.uss` (`sc-cell--name`).
- **Repro:** use a 40-character player name (the server allows 40).
- **Actual:**
  - The hole-complete scorecard draws the name across the hole-1 score.
  - The Scores table draws it across the Handicap/Average columns.
  - Names aren't clipped or ellipsized.
- **Evidence:** `shots/h1_scorecard.png`, `shots/scores_handicap.png`.
- **Status:** Fixed (2026-10-02): name cells, the turn badge and the banner title are clipped with an ellipsis; a long tie subtitle wraps. Checked: a 40-character name ends in "…" within its 200 px column.

### Top holes at runtime

#### TH-1 An `http://` trainer URL that isn't localhost makes Play a Round do nothing (no fallback)
- **Severity:** major
- **Component:** `TrainerHoles.FetchTop` and `RoundDirector.FetchCourseHoles`.
  - `UnityWebRequest.SendWebRequest()` throws `InvalidOperationException: Insecure connection not allowed` (the project allows HTTP to localhost only). The coroutine dies, and neither `done` nor `error` runs.
  - Any other synchronous throw, such as a bad URL, does the same.
- **Repro:** set `trainerUrl` to `http://10.255.255.1:8765` (any LAN trainer over HTTP), then Play a Round.
- **Expected:** "Couldn't reach the trainer", and the built-in holes, as with a refused connection.
- **Actual:**
  - The menu stays up, with the state stuck at `loading` and `round` set.
  - Pressing Play again repeats the failure.
  - Choosing Hole Simulator then starts the pending round in that scene.
- **Status:** Fixed (2026-10-02): the downloads run through `SafeCoroutine`, so a throw (an insecure-HTTP refusal, a bad URL) ends in the error callback and the fallback. Player Settings › Allow downloads over HTTP is now "Always allowed", for a trainer or game server on the LAN. Checked with HTTP refused again: "Insecure connection not allowed", then the cached holes.

#### TH-2 No feedback while the top holes download; the menu stays live
- **Severity:** minor
- **Component:** `RoundDirector.FetchCourseHoles` / `MainMenu`.
- **Repro:** Play a Round with a slow or unreachable trainer.
- **Expected:** a loading indicator, and the menu locked until the hole loads.
- **Actual:**
  - The "Getting the top-rated holes…" toast is on the round HUD and isn't visible on the menu (`shots/blackhole_wait.png`, 5 s in).
  - The menu cards still work. Hole Simulator during the fetch starts the round (TH-1).
  - Each request has a 60 s timeout, so an unreachable https trainer can leave the TV idle for a minute (inferred from `req.timeout = 60`; not measured).
- **Status:** Fixed (2026-10-02): a loading overlay over the menu ("Downloading hole 3 of 9…" with a bar) takes every key while the holes download; Back cancels, and the cancelled round never starts. The top-list request times out after 10 s (files 60 s).

#### TH-3 Double Select on Play a Round with a cold cache corrupts the download; hole 1 falls back to the built-in hole
- **Severity:** minor
- **Component:** `RoundDirector.PlayRound` (only guarded by `ScreenFade.Loading`, which isn't set during the fetch) and `TrainerHoles.Download` (both fetches share `<id>.partial`).
- **Repro:**
  1. Move the cache (`~/Library/Application Support/DefaultCompany/Golf-sim/holes`) aside.
  2. Press Select twice on Play a Round in one frame (`QA.DoubleSelect`).
- **Expected:** one fetch.
- **Actual:**
  - `DirectoryNotFoundException …forest_621272063_f250a1.partial`.
  - The surviving fetch reports success, but hole 1's folder was deleted: "Couldn't build …: No hole package". Hole 1 is played on the built-in 412 yd hole.
  - Only 8 of the 9 top holes are left in the cache.
  - With a warm cache, the double press still runs two fetches. Only one scene load wins.
- The original cache was restored after the test.
- **Status:** Fixed (2026-10-02): Play is ignored while a round's holes download, `TrainerHoles.FetchTop` runs one fetch at a time (a second waits), and each hole downloads into its own `<id>.partial-<guid>` folder that is moved into place only when complete. Checked: two fetches at once with a cold hole, both got 9 holes, the package was identical, no partial folders left.

#### TH-4 With the trainer down, the round ignores the cached top holes
- **Severity:** minor
- **Component:** `TrainerHoles.FetchTop`: the top list must come from the network.
- **Repro:** with all top holes cached, set an unreachable trainer URL (`http://localhost:9`) and resume a game.
- **Expected:** play the cached holes (the last known top list).
- **Actual:**
  - "Couldn't reach the trainer", and every hole becomes the built-in HoleSimulator hole.
  - A resumed game switches course mid-round: holes 1–4 were top holes, hole 5 is the built-in hole.
- **Status:** Fixed (2026-10-02): each server game's hole list is saved in `holes/rounds/<server>-game-<id>.json`, and a resumed game builds from it without the network. With the trainer down, a new round plays the last saved top list. Checked: game 7 resumed with the trainer at `localhost:9`, cached list, no built-in hole.

#### TH-5 Top hole #5 (mountain_25039489_bd20eb): the tee sits below a terrace, so Driver and the suggested 5 Iron go 3–4 yd
- **Severity:** major (the hole is unplayable for the default club)
- **Component:** the hole package / generator (the terrain under the tee boxes). The package is cached and immutable.
- **Repro:** play hole 5 of the top-holes round and hit the suggested 5 Iron or Driver from the tee.
- **Expected:** a normal tee shot.
- **Actual:**
  - The ground rises 0.54 m at 3 m, 1.13 m at 4 m and 1.68 m at 6 m in front of the tee (stepped tee boxes).
  - The 5 Iron carries 3–4 yd at every aim from −10° to +10°. Driver carries 3 yd. 9 Iron and Wedge get over.
- **Evidence:** `shots/h5_tee.png`, `QAProfile.Line`. It may be the same cause as round 1's U-6 in a package generated before that fix.
- **Status:** Fixed (2026-10-02) in the generator, `GENERATOR_VERSION` 4. Not U-6: each tee box was padded to its own median ground on a 20–48% mountain slope, so four boxes 9 m apart became terraces, and nothing limited the climb from the tee.
  - `course_gen/grading.py` re-profiles the hole line (cut/fill as a function of distance along the path, full strength in the rough, fading out 35 m beyond, so off-corridor relief is unchanged). Uphill caps: 4% over the 60 m tee deck, 10% to 120 m, 20% beyond; downhill 25%. Forward tee boxes stand at most 4% above the back box (the back box may be built up 1.5 m).
  - `validate.launch_problems` checks that the ground in the first 100 m stays under an 8° launch line from the tee (on the hole line and ±6°). `generate.plan_terrain` retries the layout if it fails.
  - Same style and seed now: +0.26 m at 4 m, +0.35 m at 6 m, +0.69 m at 20 m, +5.75 m at 100 m (was +1.16, +1.70, +2.94, +18.96).
  - Tests: `course_gen/tests/test_playability.py` covers 66 holes (6 of every preset plus 30 more mountain). It checks that none fails, that no retry is needed, and that no 5 m grade reaches 20% within 120 m.
  - Existing packages are immutable. `course_gen/scan_launch.py <holes_dir>` (read-only) lists the ones that fail: 7 of 417 in the local trainer pool, this hole included.

### Collisions

#### C-1 Forest trees collide much wider than they are drawn: shots hit invisible leaves
- **Severity:** major
- **Component:** the forest theme's `ScatterSet` (which prefab draws each object kind) against `ObstacleSettings.canopies`.
  - Deciduous objects (kind 1, crown radius 0.40 × height) are drawn with pine and conifer prefabs.
  - Conifers drawn as Pine_A/Pine_B have collision cones about twice their drawn width.
- **Repro:** on top hole #1 (`forest_621272063_f250a1`), hit the straight Driver from the tee, the default shot.
- **Expected:** the ball hits a tree only where one is drawn.
- **Actual:**
  - The ball "hits leaves" 4.4 m from the nearest tree, a 14.8 m Pine_A with a drawn half-width of 1.6 m. That object is kind 1, with a 5.9 m collision crown.
  - On that hole, 1,724 of about 3,900 trees have collision crowns 1.6–4× their drawn half-width. For example, deciduous drawn as Pine_B is 4.0×.
  - 10 of 112 tree hits in a fan of tee shots were outside every drawn tree's bounding box.
  - Every player's opening drive stops at 75 yd, and every forest tee shot triggers a "hit a tree" replay.
- **Evidence:** `QACol.KindVsDrawn`, `QACol.HitsVsDrawn`, and `shots/ghosthit.png` (red marker at the hit point, beside a thin pine top).
- **Status:** Fixed (2026-10-02): collisions use the crown each tree's model draws. At build time `ModelShape` measures each prefab's LOD0 foliage (every submesh but the bark) and fits a cone or ellipsoid around the trunk (85 % of the foliage inside). `TreeScatterer` then writes each object's crown (radius, bottom, top, shape, and the kind of tree drawn) into `HoleInfo.Obstacle`. `ObstacleField` and the replay's `CameraSpots` read it through `ObstacleSettings.CrownOf`, falling back to the per-kind shapes for holes built before this change. Pine_A's crown is 0.087 × its height (the deciduous default was 0.40). Conifer leaf density went from 0.35 to 0.20 hits/m, because pine crowns are porous. The Hole Simulator hole was rebuilt and saved. `ObstacleCheck` gained `Crowns` and `Drive`, which compare against the terrain's drawn trees:
  - Hole Simulator, before: collision crowns averaged 1.64× the drawn half-width (worst 3.99×, 3,317 of 4,519 wider), and 25 of 76 hits in a fan of 63 tee shots were off every drawn tree. The opening drive hit leaves 3.7 m from a drawn Conifer.
  - Hole Simulator, after: 4,519 of 4,519 trees have a measured crown, at 0.84× on average (worst 1.11×, a Cypress cone base). 0 of 78 fan hits are off a drawn tree. The opening drive still clips a real pine on the dogleg (`replayfix/opening_drive_hit_*.png`).
  - Forest top hole #1, after: 0 of 100 fan hits are off a drawn tree (round 3 found 10 of 112). The opening drive carries 225 yd, not 75, and finishes in the leaves of a drawn tree.
  - The other `ObstacleCheck` tests and Stress (200 shots) pass.

### Instant replay

#### RP-1 The opening camera never shows the strike: the ball is below the frame during the pre-roll
- **Severity:** minor
- **Component:** `ReplayCameraman.DownTheLine` / `BehindPutter`. The look target is 90 m ahead and 6 m up with `track` 0.6, on a 22° lens, so the ball at address is framed off the bottom.
- **Repro:** replay any shot (16 shots on 2 holes checked frame by frame).
- **Expected:** "holds on the strike": the ball visible at address and as it leaves.
- **Actual:**
  - For the whole 0.7 s pre-roll and the strike, the ball's viewport y is −0.04 (off screen, even before the 11 % letterbox). It appears 0.12 s after impact.
  - All 21 pre-roll frames in every shot. Behind-the-putter cut the ball off the same way (24 frames).
- **Evidence:** `QAReplay3.FirstShotViewport`, `replay/*_down-the-line_-0.67.png`.
- **Status:** Fixed (2026-10-02): until 0.02 s after the strike, the down-the-line and behind-the-putter cameras frame the ball itself in the lower third (`ReplayShot.holdUntil`/`holdFrame`). They then pan to their usual framing. Checked from the pre-roll to 0.3 s: Driver (twice), a 9 iron, a chip and a putt each show the ball in 30 of 30 frames (viewport y 0.33–0.88, inside the letterbox). Frames: `…/tmp/replayfix/``drive_00…`, `putt_00…`.

#### RP-2 Restart Hole during the replay lead-in replays the old shot on the restarted hole
- **Severity:** minor
- **Component:** `ReplayDirector`: a `Waiting` replay survives the scene load.
- **Repro:**
  1. Hit a shot that auto-replays (a tree).
  2. Within the 1.1 s lead-in, press Back, Down, Select (Restart Hole).
- **Expected:** the replay is dropped with the shot.
- **Actual:** the hole rebuilds, then "REPLAY … Driver · 75 yd · Hit a tree" plays for 7.4 s on the new hole, holding the first turn.
- **Status:** Fixed (2026-10-02): a waiting replay is dropped when its shot is no longer the last one with the ball still where it finished (`ReplayDirector.Current`). A restart places the ball (or loads a new scene, which clears the recorder), so this covers it. Checked: rebuilding the hole during the lead-in leaves nothing playing and the turn not held.

#### RP-3 A mulligan during the lead-in doesn't cancel the replay of the taken-back shot
- **Severity:** minor
- **Component:** `ReplayDirector`: `OnBallPlaced` is only subscribed once playback has begun.
- **Repro:** hit a tree shot, then send `mulligan` within the lead-in (`QA.MulliganDuringLeadIn`).
- **Expected:** no replay, as a mulligan during playback already does (that path restores the camera, HUD, ball and aim line correctly).
- **Actual:** strokes go back to 0 and the ball is back on the tee, then the mulliganed shot replays anyway.
- **Status:** Fixed (2026-10-02), the same check as RP-2. Checked: `RoundDirector.Mulligan` during the lead-in puts the ball back and no replay plays. Without the take-back, the replay still starts.

#### RP-4 The tree camera in dense woods films a wall of foliage
- **Severity:** minor
- **Component:** `CameraSpots`/`ReplayCameraman.Tree`. Views are checked against the collision crowns, which don't match the drawn trees (C-1). Cypress, for example, is drawn wider than its collider.
- **Repro:** on forest hole #1, hit a 5 Iron 35° left (`QAReplay6.HitWildMinus35`) and watch the replay.
- **Actual:** for 4.6 s the "tree" camera shows only leaves and trunks close up. The ball is visible in 0 of 203 frames, and the tower shot loses the tracer in the canopy for 59 of 63 frames.
- **Evidence:** `shots/g_wild35.png`.
- **Status:** Fixed (2026-10-02): views are tested against the drawn crowns (C-1), with their cone or ellipsoid shape. The tree camera must see two of three subjects: the hit, the ball 0.25 s before it, and where the ball stops. It tries the usual low ring first, then a wider ring 10 m above the local treetops (`CameraSpots.TreetopsAt`). If neither works, there's no tree cut and the previous camera holds. When the drop is hidden, the camera stays on the hit. Frames (`…/tmp/replayfix/`): `woods_06…08` (5 Iron 35° left on forest #1) look down over the treetops at the tracer turning in a crown. `woods_plus35_03…05` (35° right) see the ball path through the trunks.

#### RP-5 A water replay ends on empty water
- **Severity:** polish
- **Component:** `ReplayCameraman.Landing` (and no splash effect).
- **Actual:** the last 1.5 s of the landing shot frame plain water around the sunk ball: no ball, splash or tracer.
- **Evidence:** `shots/g_water.png`, frame `water_07`.
- **Status:** Fixed (2026-10-02): the recording finds where the ball went through the water's surface (`ShotRecording.SplashTime`/`SplashPoint`). The replay stops the ghost ball and tracer there, the landing camera frames that point, rings ripple out from it (`ReplaySplash`), and the replay holds 2 s on them. Frames: `…/tmp/replayfix/``water_06…08` (top hole `lakes_834126987_d28453`).

### Putting

#### P-1 Putting camera goes underground, then pops up with the ball out of view
- **Severity:** minor
- **Component:** `RoundDirector.PuttCameraPose` (2.6 m back, 1.5 m up, no terrain check) and `ShotPanel.LineUp` → `HoleFlyCamera.JumpTo` (no ground clamp).
- **Repro:**
  1. On top hole #5's green, putt from a spot with rising ground behind the ball (`QAPutt.PlaceWorst`).
  2. Or scan the putting spots on and around the green (`QAPutt.CameraScan`): 16 of 389 put the camera under the terrain, including one on the green.
- **Expected:** the camera stays above ground with the ball in view.
- **Actual:**
  - The camera is placed 0.97 m under the terrain.
  - On the next frame, `KeepAboveGround` lifts it to 1.5 m clearance without re-aiming, leaving the ball at viewport y −0.39, below the screen.
- **Evidence:** `shots/putt_cam_worst_after.png`. The ball should be at the bottom centre.
- **Status:** Fixed (2026-10-02): `PuttCameraPose` keeps the camera 1.5 m above the terrain at six points from the ball back to the camera, so the fly camera never needs to lift it. If the ball would then sit more than 30 % of the view below the centre, the camera tilts down to keep it in frame. Checked with the real pose at every putting spot on top hole #5 (389 spots) and the Hole Simulator (354): none under the terrain, lowest clearance 1.50 m, ball always on screen (lowest viewport y 0.22). `PuttingCheck` Flat and Green pass, and Setup, Render and Stroke behave as before.

### Sound

#### S-1 Players can't change the volume
- **Severity:** minor
- **Component:** `GameAudio` (`SetVolume`/`MasterVolume` save to PlayerPrefs, but nothing in the game calls them).
- **Actual:** there's no volume setting in the main menu or the pause menu. The saved volumes only change from code.
- **Status:** Fixed (2026-10-02): a Sound card on the main menu and Sound in the pause menu open `AudioSettingsPanel`: master, effects, crowd, ambience and interface, Up/Down and Left/Right in 10 % steps, saved through `GameAudio`. (There is no music bus; ambience is the bed.)

#### S-2 The pause menu doesn't pause sound
- **Severity:** polish
- **Component:** `HomeMenu` (sets `Time.timeScale` only) / `GameAudio`.
- **Actual:** with the pause menu open, a crowd roar and applause play to the end (3.2 s later still playing), along with the ambience.
- **Status:** Fixed (2026-10-02): the pause menu sets `AudioListener.pause`; interface sounds ignore it, so the menu still ticks.

### Menus

#### M-2 Scores ranks "Best" and "Average" across rounds of different lengths
- **Severity:** minor
- **Component:** the server stats (`bestTotal`, `averageTotal`/`averageToPar` over all counted rounds) shown by `ScoresScreen`.
- **Actual:** a 1-hole game counts as a round. Fin Test and Solo Sam lead "Best" with 6, which any 9- or 18-hole round can never beat, and a 1-hole +2 averages like a 9-hole +2.
- **Evidence:** `shots/scores_best2.png`.
- **Status:** Fixed (2026-10-02) in the game server: bests and averages count complete 9- and 18-hole rounds only, per length (`best9`, `best18`, `avg9`, `avg18`); `averageToPar`, `recentAverageToPar` and the handicap use 9/18-hole rounds scaled to 18; `bestTotal`/`averageTotal` were removed. PROTOCOL.md and the tests were updated. The Scores screen ranks Handicap, Avg /18, Best 9, Best 18, Wins, Birdies and Aces; the app's leaderboard shows the same.

#### M-3 The Play a Round card says "9 holes" while 18 is selected
- **Severity:** polish
- **Component:** `MainMenu` (static `GameMode.description`).
- **Evidence:** `shots/menu_18.png`.
- **Status:** Fixed (2026-10-02): the card's description is a template (`{holes} holes for up to 8 players…`) filled with the 9/18 choice.

### Tested, no bugs found (round 3)
- **Rounds:** 4 players with odd names (emoji ZWJ sequence, CJK, `'`, 40 characters), 9 holes.
  - Phone nav, club, aim, aimReset, shot, mulligan and skip over the server.
  - 1-hole, 3-hole and 18-hole games. 8 players × 18 holes (the scorecard fits).
  - A solo 18-hole round from the menu (18 runtime holes built).
  - Resume from the menu at the first unfinished hole.
  - **Postgres matches the sim's scores** for games 1, 2, 6 and 7 (`hole_scores`).
- **Disconnects:**
  - Server frozen (`docker pause`) for 70 s while a hole finished: the sim timed out and reconnected, and all scores arrived.
  - Server stopped for a whole hole, then started: the queued `holeScore`s were delivered.
  - Ended from REST as ABANDONED: toast and menu.
- **Pause:**
  - Mid-flight it freezes the ball and refuses phone shots. Restart Hole mid-flight rebuilds the runtime hole.
  - Pause on hole-complete and results, Resume, and Main Menu work.
  - Back during a replay skips it (by design), and the next Back pauses.
- **Replay:**
  - Auto triggers: tree, water, long putt holed, close approach.
  - Up between turns, and on the hole-complete and final scorecards (the scorecard returns after).
  - Select/Back skip, and a mulligan during playback.
  - 6,930 replays in a row: the camera pose, FOV and near plane are restored exactly, with no extra objects, tracers or audio voices.
  - Frame-by-frame sweeps of 16 shots on 2 forest holes: no camera underground or inside a collider.
- **Putting:**
  - Preview against the real ball for 171 putts (1.5–16 m, aim ±3°): 0 holed/missed mismatches, worst end error 5 cm.
  - Putting mode switches with the club, and fringe putts within 3 m are covered.
  - No green on the 18 cached holes has a tree within 2.5 m, so "under a tree" can't happen.
- **Collisions:**
  - Obstacles match drawn objects 1:1 (4,022/4,022).
  - A ball against a trunk is playable at any aim.
  - Chips hit rocks and bounce off. No ball came to rest inside a trunk or rock in 123 shots.
  - Lie effects are shown and applied (Rough −10 %, Woods −13 %, Native −12 %, Scrub −13 %).
- **Sound:**
  - The catalog is complete.
  - Live events play the right categories (driver strike, leaves, grass landing, crowd "ooh", replay sting, flat replay strike).
  - The listener is on the main camera.
  - 26 sources over the whole session, with no leaks.
- **Top holes:**
  - Download and build; a lakes hole with water (penalty, replay).
  - The cache is reused: the folders were untouched across rounds.
  - Fallback on a refused connection.
  - Memory: 9 restarts of a runtime hole showed no TerrainData, mesh or texture growth. Repeated builds in one scene keep cup and water meshes until the next scene load unloads them, which is harmless.
- **Menus:** the Scores tabs and Back/Select close, 9/18 with Up/Down, and the D-pad on every screen.

### Not tested (round 3)
- **The real iPhone app:** another agent was fixing it, so it was faked with websockets clients.
- **A real Wi-Fi drop:** `docker pause` keeps the kernel's TCP buffers, so it isn't a true half-open link.
- **Listening:** sound was checked by inspection only.
- **Other inputs and modes:** the `FarthestFirst` turn order, the UDP 4242 fallback, and a physical gamepad.
- **Builds and performance:** a standalone build (TH-1's insecure-HTTP rule) and frame rate. The unfocused Editor ran at about 3 fps.
- **Trainer timeouts:** an https trainer that times out (TH-2's 60 s wait is inferred from the code).

