# Bug hunt log

QA findings, newest round first. Each entry has a severity (blocker / major / minor / polish), the component, repro steps, expected vs actual, and evidence.
Evidence files live in the tester's tmp folder (round 1: `/Users/obadiah/.claude/jobs/5fd17b43/tmp/bughunt/`, round 2: `…/tmp/bughunt2/`, round 3: `…/tmp/bughunt3/`, round 4: `…/tmp/report/e2e/`) unless noted.

---

## Round 4 (end-to-end, 2026-10-02)

This round played the game as a group would, end to end, with three players (Obadiah, Reed, White Hayden): the real iPhone app → the game server → the Unity sim, on the trainer's top holes.
- **Server:** `docker compose -p e2e` on port 18090 (torn down afterwards). The sim was pointed at it at runtime (`useLocalServer`, `localServerUrl`) and at the local trainer (`e2etrainer`, http://localhost:8765). All three values were restored before stopping Play.
- **App:** a Debug build on the iPhone 17 Pro simulator, driven with `idb`. Settings › Game server was `http://127.0.0.1:18090`. Swings used the Debug **Simulate swing** button.
- **Sim:** the Editor in Play mode from `MainMenu.unity`. To move rounds along, `run_script` probes hit approach shots and putts through the same `Shots` path, using sized pitches and computed putts.
- **Logging:** a passive websockets client ("E2E logger") recorded every sim → phone message to `…/tmp/report/e2e-ws.log`.
- **Evidence:** `…/tmp/report/e2e/`. Numbered files are the walkthrough; `bug-*.png` files go with the entries below.

### What was played
- **Game 1 (9 holes, FINISHED):**
  - Holes 1–4 were played from the phone: club wheel, aim arrows, Simulate swing and phone putts, plus a mulligan and a pick-up.
  - Holes 5–9 were scripted.
  - White Hayden won with 68 (+34). Postgres, REST, the sim's Scores screen and the phone's Scores tab all agree.
- **Game 2 (9 holes, ABANDONED):** pause, Sound, an app kill and relaunch, and a server kill mid-hole. It was replaced by game 3.
- **Game 3 (18 holes, ABANDONED):** started from the app over game 2, then ended from the app.
- **Game 4 (9 holes, FINISHED):** scripted, to capture the winner banner.

### Rounds and HUD

#### E-1 The turn and winner banners are cut off with "…" for every name, even short ones
- **Severity:** major (the headline announcement of every turn)
- **Component:** `Game/UI/RoundHud.uss` `.turn-banner__title` (`white-space: nowrap; text-overflow: ellipsis; letter-spacing: 4px`), `TurnBanner.cs`.
- **Repro:** start any game and watch the announcement when a turn starts, and at the end of a round.
- **Expected:** "REED'S TURN", "WHITE HAYDEN'S TURN", "WHITE HAYDEN WINS".
- **Actual:** "REED'S TU…", "WHITE HAYDEN'S TU…", "WHITE HAYDEN WIN…", although the screen has plenty of room. The card shrinks with `scale`, which doesn't re-layout, so this is the label's own measured width. The label seems to be measured without the 4 px letter spacing, so the last characters always overflow into the ellipsis.
- **Evidence:** `bug-turn-banner-truncated.png`, `03-turn-banner-and-phone-gameplay.png`, `14b-winner-banner-confetti.png`, `14c-winner-sequence.png`.
- **Status:** Fixed (2026-10-02): `TurnBanner.FitTitle` sizes the title itself: the width counts the letter-spacing that layout leaves out, a long name shrinks the font (down to 60 px), and only a name that doesn't fit even then wraps onto two lines at a size they hold. The title no longer ellipsises. Checked: "WHITE HAYDEN'S TURN", "WHITE HAYDEN WINS!" and a 44-character name (`E2EFixCheck.E1Fit`, screenshots in `…/tmp/e2efix/`).

#### E-2 Select right after Up on a scorecard skips to the next hole, and the HUD then stays hidden for good (no scorecard, badge or banner on the TV)
- **Severity:** major (rare trigger, but the round is unplayable on the TV afterwards, across games, until a full replay happens)
- **Component:** `Game/Runtime/Replay/ReplayDirector.cs` `OnNav` (Select/Back during `Stage.Waiting` returns false) and `ScreenState.Hide/Restore`.
- **Repro:**
  1. Finish a hole so the hole-complete scorecard shows.
  2. Press Up (replay the last shot), then Select before the replay starts. Two remote messages handled in the same frame do it: here, `NavInput.Push(Up)` then `NavInput.Push(Select)` in one `run_script` call. A phone tapping ▲ then OK quickly while the sim stutters does the same.
- **Expected:** Select skips the queued replay, or is ignored until it plays.
- **Actual:**
  - The Select falls through to the scorecard: `select handled True screen loading`, and the next hole loads with the replay still queued.
  - The replay then hides `hud-root` on the new hole, and its `Restore` never runs. The new hole has no hole card, no turn banner, no badge and no hint bar, and still has none after shots.
  - It survives starting a new game: game 3 (18 holes) began with no HUD. At its hole complete the phone said "Hole complete" while the TV showed only the green, with no scorecard.
  - Only a complete instant replay (Up on the next scorecard) brings the HUD back.
- **Related:** during play, Select in the 1.1 s lead-in of an automatic replay is ignored (`handled False`) rather than skipping it. The docs say Select or Back skips a replay.
- **Evidence:** `bug-hud-missing-after-select-in-replay-leadin.png`, `bug-no-scorecard-on-tv-hud-hidden-18hole.png`, then `22-18hole-scorecard.png` after a full replay.
- **Status:** Fixed (2026-10-02): the HUD's visibility has one source, the state: `RoundHud.Render` hides `hud-root` only on the `replay` screen, so whatever ends a replay (including one dropped because its camera went with a scene) shows it again; `ScreenState` no longer touches the HUD. Select or Back during a replay's lead-in (or right after Up) skips it and is never passed on to the scorecard, and leaving the hole (`LoadScene`, `EndRound`, any scene load) drops the replay and the last shot. Checked: Up+Select in one call on the scorecard (stays, no replay), a burst from a fake phone, a full replay from the scorecard (HUD hidden, then back), Select in an automatic replay's lead-in, and a replay dropped mid-play (`E2EFixCheck.E2*`).

#### E-3 The turn badge disappears after an instant replay and stays hidden until the next player's turn
- **Severity:** minor
- **Component:** `TurnBanner` / `ScreenState.Restore` (the badge's `turn-badge--shown` state after the HUD is shown again).
- **Repro:** hit a shot that triggers a replay (a tree hit, or a long drive), let it play out, and look at the top right.
- **Expected:** the "Reed · Hole 1 · Par 4 · Stroke 2" badge comes back with the HUD.
- **Actual:** the hole card returns but the badge doesn't, for the rest of that player's hole. It reappears on the next turn. Seen in game 1 for Reed, and in game 2 for Obadiah, after pause and Sound too.
- **Evidence:** `bug-badge-gone-after-replay.png` (Reed before his tree replay, the replay, then no badge for his next shots).
- **Status:** Fixed (2026-10-02): another screen now covers the badge (`TurnBanner.Cover`) instead of ending the turn, so it comes back with the HUD after a replay (and after the pause or Sound panel). Checked: badge shown, hidden during the replay, back after it (`E2EFixCheck.E3Badge`).

#### E-4 After a pick-up the HUD says "Stroke 10"
- **Severity:** polish
- **Component:** `RoundHud` stroke field.
- **Repro:** reach par + 5 (or tap Pick up after 3 shots) on a par 4.
- **Expected:** the HUD shows the score of 9, or clears.
- **Actual:** "STROKE 10" shows under the "Reed picks up: 9" toast until the next turn starts.
- **Evidence:** `bug-hud-stroke10-after-pickup.png`.
- **Status:** Fixed (2026-10-02): once the player's hole is over the HUD shows SCORE with the hole's score, and the badge "Picked up: 9" (or "Holed: 4"). Checked: `E2EFixCheck.E4PickUp`, `pickup-hud.png`.

#### E-5 The "▲ Replay" hint shows on the main menu while the next game's holes download
- **Severity:** polish
- **Component:** `ReplayDirector` / the replay hint (`CanReplay` still true for the previous game's last shot).
- **Repro:** finish a game, return to the menu, and start a new game from the app (cold cache).
- **Actual:** "▲ Replay" sits in the bottom-right corner under the "Getting the course" overlay.
- **Evidence:** `bug-replay-hint-on-menu-during-download.png`.
- **Status:** Fixed (2026-10-02): leaving the hole drops the last shot (`ReplayDirector.Forget`), and Replay is only offered on the game, hole-complete and results screens, never while a scene loads. Checked: offered on the scorecard, gone as soon as Select loads the next hole; none on the menu after the game (`E2EFixCheck.E5Prompt`).

### iPhone app

#### E-6 Gameplay mode has no way to ask for the replay the TV offers between shots
- **Severity:** minor
- **Component:** `Golf-app/Views/Game/GameplayView.swift`.
- **Repro:** after a shot that didn't auto-replay, the sim shows "▲ Replay" (Up). Look at the phone.
- **Expected:** a Replay button, or the D-pad's Up, as on the scorecard.
- **Actual:** gameplay mode has only the club wheel, aim, Menu, Mulligan and Pick up. Up only reaches the sim from remote mode (scorecards), so "replay the last shot between turns" can't be done mid-hole.
- **Evidence:** `bug-phone-no-replay-control-in-gameplay.png`.
- **Status:** Fixed (2026-10-02, app): gameplay and putting modes show a **Replay last shot** button above Menu / Mulligan / Pick up while the TV offers a replay; it sends `nav {key:"up"}` (`GameLink.replay`). The sim doesn't say so in `state` yet, so the app infers it from `screen:"game"`, `canShoot:false` and `waitReason:"Wait for the next turn"` (the sim's `BetweenShots` phase, when `ReplayDirector.CanReplay` holds). `SimState.canReplay` (optional bool) is already decoded and wins over that guess: **sim to-do:** publish `state.canReplay = ReplayDirector.CanReplay && stage == Idle` (and republish when it changes) so the button never shows when Up would fall through to RoundDirector's club change (e.g. after a mulligan, when `LastIsCurrent` is false). Checked: shown/hidden by state on the iPhone 17 Pro and SE, a tap reached a fake sim as `nav up`; `EndToEndFixTests.replay*`. Screenshots: `…/tmp/appfix4/pro-gameplay-replay-offered.png`, `se-gameplay-replay-offered.png`.

#### E-7 Bottom controls slide under the tab bar (disconnect banner, 18-hole remote)
- **Severity:** minor
- **Component:** `GameplayView` and `RemoteView` layout (not scrollable).
- **Repro and actual:**
  1. With the "Disconnected — reconnecting…" banner showing (stop the server mid-hole), Menu, Mulligan and Pick up move to y = 806 and Simulate swing to y = 847, under the tab bar at y = 832. They can't be reached, and swiping doesn't scroll.
  2. On an 18-hole game's hole-complete screen, the Front 9 / Back 9 picker pushes the remote's Back button under the tab bar.
- **Expected:** the content scrolls or compresses so every control stays tappable.
- **Evidence:** `bug-phone-controls-under-tabbar-when-disconnected.png`, `bug-phone-back-under-tabbar-18hole.png`.
- **Status:** Fixed (2026-10-02, app): `ScreenScaffold` takes a `bottom:` slot pinned above the tab bar: Replay / Menu / Mulligan / Pick up and Simulate swing on the gameplay and putting screens, Back on the remote. The content between the header and those controls gets the biggest control sizes that fit (`ViewThatFits` over `ScreenFit.levels`), and scrolls at the smallest when even they don't. The D-pad under a scorecard is 200 pt. Checked with the banner showing (server stopped) and on an 18-hole hole-complete scorecard, iPhone 17 Pro and SE: every control above the tab bar. Screenshots: `…/tmp/appfix4/{pro,se}-gameplay-disconnected.png`, `{pro,se}-putting-disconnected.png`, `{pro,se}-remote-18hole-scorecard.png`.

#### E-8 End game abandons the round with one tap, no confirmation
- **Severity:** minor
- **Component:** `Golf-app` Players tab (`End game`).
- **Repro:** start an 18-hole game, open Players, and tap End game.
- **Expected:** a confirmation, as Pick up has ("Pick up on this hole?").
- **Actual:** the game is ABANDONED immediately and the sim drops to the menu. An accidental tap ends an 18-hole round.
- **Evidence:** `23-18hole-ended-from-app.png`, `06-pickup-confirm.png`.
- **Status:** Fixed (2026-10-02, app): End game asks first ("End game #N?", "The 18-hole round is abandoned and its scores won't count. The sim goes back to the menu.", destructive End game). Checked: the dialog shows on the iPhone 17 Pro and confirming abandons the game on the server (`…/tmp/appfix4/pro-end-game-confirm.png`).

#### E-9 The putting meter keeps the last putt's strength until the next stroke
- **Severity:** polish
- **Component:** `Golf-app/Views/Game/PuttingView.swift`.
- **Repro:** putt once, then look at the putting view for the next putt.
- **Actual:** the meter still reads "6.4 m" with the fill at the old level before the new stroke starts. It reads like a preset strength.
- **Evidence:** `11-putting-green-meter-breakline.png`.
- **Status:** Fixed (2026-10-02, app): `PuttMeter.reset` empties the meter on a new turn (`turn`), on Address, and on leaving the putting view; a new hole also clears the "Last putt" result (`PuttMeter.follow`). A putt still waiting for its result keeps waiting. Checked: putt struck at 8.4 m, then `shotResult` + `turn` → the meter reads 0.0 m with "Putted 5.9 m of 4.6 m" kept (`…/tmp/appfix4/pro-putting-new-turn-meter-reset.png`); `EndToEndFixTests` meter tests.

#### E-10 "Best rounds · per 18 holes" lists raw 9-hole totals
- **Severity:** polish
- **Component:** `Golf-app/Views/Game/LeaderboardView.swift`.
- **Actual:** under that title the rows read "68 (+34) · 9 holes". The list is ranked per 18 (`toPar` doubled), but the numbers are the round's own, so the title reads as if they were per-18 scores. "Best rounds (ranked per 18 holes)", or showing the per-18 figure, would be clearer.
- **Evidence:** `16-phone-scores-leaderboard.png`.
- **Status (app side):** Fixed (2026-10-02): the leaderboard lists "Best 9-hole rounds" and "Best 18-hole rounds" separately (the server's `bestRounds` split by `holesCount`, server order kept), each row the round's total and score to par with its game; the per-player Best 9 / Best 18 columns (from `/api/players` `best9` / `best18`) are unchanged. An 18-hole list shows only once someone has an 18-hole round. Checked: `…/tmp/appfix4/pro-leaderboard-best-rounds.png`, `EndToEndFixTests.bestRoundsAreListedPerLength`.

### Putting

#### E-11 A putt from the rough near the green plays far shorter than the read and the meter say
- **Severity:** minor (needs a look)
- **Component:** `PuttModel` / `puttPlaysAs` when the lie isn't the green (putting mode within 3 m of the fringe).
- **Repro:** hole 1, ball in the rough 15.9 m from the pin, Putter selected. Putting mode shows "36 cm downhill, plays like 20.4 m". Stroke at a strength of 6.4 m on the meter.
- **Expected:** a roll of roughly 5 m, scaled from the read.
- **Actual:** "Putted 0.8 m of 15.9 m". The "plays like" figure doesn't seem to reflect how much the rough slows the ball.
- **Evidence:** `10-putting-chip-from-rough.png`, `bug-rough-putt-meter-6.4m-rolled-0.8m.png`.
- **Status:** Fixed (2026-10-02): the read was right: `PuttPredictor` rolls the putt with the lie's speed loss and each surface's rolling resistance, and the real ball finished exactly where it said (green, fringe and rough, 0.00 m apart). But in the rough (7x the green's rolling resistance) the roll is far from the meter's flat-green scale, so a third of the read rolls much less than a third of the way. Putting mode is now the green, or short grass (rolling ≤ 0.12, the fairway's fringe) within 3 m of it; from the rough the putter is an ordinary shot and `puttPlaysAs` is 0. Checked: green 4.0 m and fringe 7.0 m in putting mode, the meter at the read finished 0.42 / 0.44 m past; rough not putting mode (`E2EFixCheck.E11Putting`).

### Menus and sound

#### E-12 The pause menu's subtitle runs past the card
- **Severity:** polish
- **Component:** pause menu UXML/USS.
- **Actual:** "Esc or Back to resume · Up/Down and Select to choose" overflows the card's right edge.
- **Evidence:** `bug-pause-subtitle-overflow.png`, `17-pause-menu.png`.
- **Status:** Fixed (2026-10-02): the pause subtitle is shorter ("Back: resume · Up/Down, Select: choose") and menu subtitles wrap inside their card. Checked: `E2EFixCheck.E12Check`, `pause-menu.png`.

#### E-13 The Sound panel over the pause menu: the menu bleeds through, and the hints are wrong
- **Severity:** polish
- **Component:** `AudioSettingsPanel` (pause instance), `RoundHud` hint bar, the app's paused text.
- **Actual:**
  - "Paused", "Resume", "Sound" and "Main Menu" show through the translucent Sound card.
  - The bottom hint bar still says "Left/Right: aim · Up/Down: club · Esc: menu".
  - The phone still says "Paused: … Back to resume", while Back closes the Sound panel.
- **Evidence:** `bug-sound-panel-bleed-through.png`, `18-sound-panel-changed.png`.
- **Status:** Fixed (2026-10-02): the Sound card is opaque, the HUD's stats and key hints hide while it is open, and `state.screen` is `settings` while a Sound panel is open (from the pause menu or the main menu), back to `paused` when it closes (the app shows its remote for it). Checked: pause → Sound → Back → Back gives paused, settings, paused, game on a fake phone (`E2EFixCheck.E13Sound`, `sound-panel-over-pause.png`).

#### E-14 Volume steps are uneven from the default levels
- **Severity:** polish
- **Component:** `AudioSettingsPanel.Set` (`Mathf.Round(value / 0.1) * 0.1`) with the defaults crowd 75 % and ambience 35 %.
- **Actual:**
  - Crowd 75 % → Left → 60 % (−15), then 50 %.
  - From 75 %, Right gives 80 % (+5).
  - `Mathf.Round` rounds halves to even, and the defaults are off the 10 % grid.
- **Expected:** ±10 per press, or defaults on the grid.
- **Evidence:** `18-sound-panel-changed.png` (Crowd at 50 % after two presses); `GameAudio.GetVolume` read back `Crowd=0.5`.
- **Status:** Fixed (2026-10-02): Left/Right go to the next 10 % mark (75 → 70 → 60, 75 → 80), and a click rounds halves up. Checked: Crowd 75 → 70 → 60 → 70 → 80 → 90 (`E2EFixCheck.E14Volume`).

#### E-15 The Scores screen opens on the Handicap tab with nobody ranked
- **Severity:** polish
- **Component:** `ScoresScreen`.
- **Actual:** until someone has 3 rounds, the default tab ranks nobody, and the rows are in turn order (the winner last). Best 9 ranks them properly.
- **Expected:** open on a tab that has data, or fall back to Avg /18.
- **Evidence:** `15b-scores-sim-handicap-tab-unranked.png`, `15-scores-sim-best9-phone-scorecard.png`.
- **Status:** Fixed (2026-10-02): Scores opens on the first tab that ranks somebody, so Handicap only when someone has one. Checked: two players with a 2-hole round opened on Wins (`E2EFixCheck.E15Scores`, `scores-screen.png`).

### Tested and working (round 4)
- **Start:** Players tab, 3 players, 9 holes, Start Game.
  - The sim downloads the top holes ("Getting the course · Downloading hole 3 of 9…"; under 5 s from the local trainer) and loads hole 1.
  - The phone switches to gameplay by itself.
  - Starting while a game is in progress says "Game #2 is in progress. Starting a new one ends it."
- **Turns:**
  - The banner flies into the badge, and the phone header follows the player up.
  - Club and aim from the phone update the sim's HUD and aim line (`state.club`, `state.aim`).
  - Simulate swing → the sim hits it → tracer, follow camera, a "187 yd carry · Fairway" toast, then `shotResult`.
  - The phone dims the swing area with "Wait for the ball to stop" and "Wait for the next turn".
- **Replays:**
  - Tree hits, water and long drives auto-replay, letterboxed with the REPLAY bug, and the phone flips to "Instant replay · Press OK to skip it".
  - Up on the scorecard replays the last shot.
- **Mulligan and pick-up:**
  - Mulligan put the ball back and took strokes from 3 to 2.
  - Pick up asks to confirm, then scores par + 5.
  - par + 5 also picks up automatically (water ×4 on the par 3s).
- **Putting:**
  - The phone's putting view shows distance, slope, plays-like, Stimp, a meter with the read mark and "Putted x m of y m".
  - The sim draws the dotted break line, slope arrows and the putt card.
  - The Chip button is there.
- **Scorecards:**
  - The hole scorecard shows on the TV and the phone; OK on the phone goes to the next hole.
  - The final scorecard has the winner, with confetti (game 4); OK returns to the menu.
  - 18-hole cards: OUT/IN on the sim, and a Front 9 / Back 9 picker on the phone.
- **Stats:**
  - The sim's Scores (Avg /18, Best 9, Wins, Rounds) and the phone's Scores tab (Best 9 / Avg 9, best rounds, most wins) match `GET /api/players` and `/api/leaderboard`.
  - Postgres `hole_scores` match every scorecard (games 1–3). Abandoned games count in `gamesPlayed` only.
- **App killed and relaunched mid-hole:** it comes straight back into gameplay for the player up, and the next swing works.
- **Server killed mid-hole (`docker stop`):**
  - The phone shows "Disconnected — reconnecting…" and the sim logs a retry.
  - Obadiah finished the hole offline, and his `holeScore` (7) was delivered after `docker start`.
  - Both ends came back on Reed's turn.
- **New game over a game in progress** (round 3's R-1): game 2 → ABANDONED, and game 3 started at hole 1.
- **Pause:** the phone's Menu → `paused` on both ends; Back resumes.
- **Sound:** the panel opens from pause; changes from the phone's D-pad are saved (`master=0.9 Crowd=0.5`).
- **Console:** no errors in the Editor console during the session (only the two expected reconnect warnings).

### Not tested / notes (round 4)
- **Simulate swing is random** (full swings at 16–26 rad/s, putts at 0.8–2.4). The 5 iron carried 85–156 yd, so the 169–183 yd water par 3s (holes 3–4) were never reached, and putts rarely drop. Approach shots inside 55 m and most holing putts were scripted. This is a debug-tool limit, not a game bug.
- **The first two top holes are tight forest:** most simulated swings ended in the woods (tree collisions are round 3's C-1).
- **The hosted server was not answering (deployment, not code):**
  - `https://golf-server.obadiahfusco.xyz/actuator/health` and `/api/ping` returned `404 {"detail":"Not Found"}` (another service answers on that host).
  - The sim logged "Unable to connect" to `wss://golf-server.obadiahfusco.xyz` at Play start.
  - With Settings › Game server cleared, the app says "Server refused the connection (token?)".
  - So the default (hosted) setup can't play right now.
- **Not covered:** a real iPhone (motion, UDP 4242 fallback on the LAN), listening to the sound, and the Restart Hole menu item.

---

## Round 3

Moved to [bug-hunt-round3.md](bug-hunt-round3.md) to keep this file short.

## Round 2

Moved to [bug-hunt-round2.md](bug-hunt-round2.md) to keep this file short.

## Round 1

Moved to [bug-hunt-round1.md](bug-hunt-round1.md) to keep this file short.
