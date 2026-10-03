# Multiplayer rounds

Friends play a 9- or 18-hole round on the sim with **one iPhone** (the SwingRemote app) passed around.
The game server keeps the scores in Postgres.

```
 iPhone (SwingRemote)                 Game server (Spring Boot + Postgres)         PC (Unity sim)
 ───────────────────                  ────────────────────────────────────         ──────────────
 Players, Start Game ── REST ───────▶ POST /api/games ── gameStarted ──── WS ────▶ RoundDirector: loads hole 1
 D-pad (nav), club, aim, shot ─ WS ─▶ relays remote -> sim ───────────── WS ────▶ NavInput / Shots / aim / club
 gameplay or remote mode  ◀──── WS ── relays sim -> remotes ◀─────────── WS ───── state, turn, shotResult
 scorecard, leaderboard ◀─ REST/WS ── stores holeScore, broadcasts scorecard ◀─── holeScore (per player per hole)
 swing shot (fallback) ─────────────────────────── UDP 4242 ───────────────────▶ UdpShotReceiver (LAN only)
```

The wire contract is `Game-server/docs/PROTOCOL.md` (source of truth: `WsMessage.java`); live ball physics is in
`Game-server/docs/PROTOCOL-physics.md`.

## Where to find the code (Unity)

| | |
|---|---|
| `GolfSim/Net/Runtime/ServerConfig.cs` | server URL + token. Asset: `GolfSim/Net/Resources/GolfServer.asset` |
| `GolfSim/Net/Runtime/SimConnection.cs` | `ClientWebSocket` as `role=sim`: background receive, main-thread events, ping, reconnect with backoff, reliable `holeScore` queue |
| `GolfSim/Net/Runtime/SimMessages.cs` | every WebSocket message class (phone shots reuse `Ball/RemoteShotMessage`) |
| `GolfSim/Net/Runtime/GameApi.cs` | REST GETs (stats for the Scores screen) |
| `GolfSim/Net/Runtime/TrainerHoles.cs` | downloads the Course Trainer's top holes into a cache, one fetch at a time; saves each round's hole list |
| `GolfSim/Ball/Runtime/Shots.cs` | the one shot path for UDP, WebSocket and the on-screen panel: game gate, retry de-duplication, hit |
| `GolfSim/Ball/Runtime/BallPhysicsProfile.cs` | the server's live physics profile on top of `BallPhysics.asset` (runtime copies, from the next shot) |
| `GolfSim/Ball/Runtime/Clubs.cs` | the club table (same names as the app), carries, club suggestion |
| `GolfSim/Ball/Runtime/AimLine.cs` | the aim arrow on the ground; `GolfBall.aimOffset` turns the shot |
| `GolfSim/Game/Runtime/NavInput.cs` | one input path for keyboard, gamepad and the phone's D-pad |
| `GolfSim/Game/Runtime/RoundDirector*.cs` | the session across scenes: connection wiring, rounds, holes, turns, scoring, `state`; `.Server.cs` keeps the round in step with the server's game, `.Holes.cs` gets the round's holes |
| `GolfSim/Game/Runtime/Round.cs` | the scorecard and turn rules (no scene code) |
| `GolfSim/Game/Runtime/RoundHud.cs`, `TurnBanner.cs`, `ScoreTable.cs` | HUD, turn announcement and badge, scorecard |
| `GolfSim/Game/Runtime/Map/` | the course map: `CourseMap` (the panel), `HoleMapPainter` (the hole's picture), `MapOverlay` (aim line, balls, distances); wired in `RoundDirector.Map.cs` |
| `GolfSim/Game/Runtime/Practice/` | the practice facilities: `DrivingRange`, `PuttingGreen` (built by `FacilityGround`), `PracticeHud`; wired in `RoundDirector.Practice.cs` |
| `GolfSim/Game/Runtime/ScoresScreen.cs` | the main menu's Scores leaderboard |
| `GolfSim/Game/Runtime/Audio/AudioSettingsPanel.cs` | the Sound settings (main menu card and pause menu) |
| `GolfSim/Game/Resources/CourseRound.asset` | hole scenes, holes, turn order, max over par, HUD assets |

`RoundDirector` creates itself when Play starts (`RuntimeInitializeOnLoadMethod`) and lives across scenes
with the connection and the HUD. Any scene with a `HoleInfo` and a `GolfBall` works as a hole.

## Rules

- **Turn order: whole hole per player** (Wii Sports style, the default). Player 1 plays the hole until it is
  holed or picked up, then player 2, and so on, with one ball on the course. `CourseRound.turnOrder` can be
  set to `FarthestFirst` (everyone tees off, then farthest from the pin plays).
- Strokes count when the ball stops. **Water or out of bounds**: one penalty stroke, and the shot is
  replayed from where it was hit (stroke and distance).
- **Pick-up**: at par + 5 (`maxOverPar`) without holing out, the player scores par + 5. The phone's
  **Pick up** (`skip`) does the same at any time.
- **Mulligan**: takes back the last shot (ball, strokes and score), and that player hits again.
- **The server's game wins.** A new game from the app replaces the round in progress. A game ended from the app
  stops the round: ABANDONED goes back to the menu, FINISHED shows the final scorecard. After a reconnect, the
  `hello` game is played if it differs from the sim's (or the round stops if its game is over). Scores are only
  sent to a game the server says is in progress, and Restart Hole is unavailable on the scorecards.
- A player's `holeScore` goes to the server as soon as they finish the hole. Scores are sent reliably:
  if the connection is down they wait for the next connection. The server upserts them, so resending is safe.
- Par comes from the hole scene's `HoleInfo.par` (3 to 6), else `CourseRound.defaultPar`.
- **Holes:** `CourseRound.holeScenes` repeats to fill 9 or 18 holes. Add more hole scenes (in the build
  settings) to the list and rounds use them in order.
- **Top holes** (`ServerConfig.useTopHoles`): rounds play the Course Trainer's top-rated holes, downloaded into
  `<persistentDataPath>/holes/<id>/` and built at runtime. The hole list of each server game is saved in
  `holes/rounds/`, so a resumed game replays the same holes without the trainer; with the trainer down, a new round
  plays the last saved list, else the built-in holes. A LAN trainer may use `http://` (Player Settings › Allow
  downloads over HTTP is "Always allowed").

## Flows

1. **Menu:** the D-pad's Left/Right picks a card and Select plays it. The cards are **Play a Round**,
   Hole Simulator, **Driving Range**, **Putting Green** (see Practice below) and **Scores**. On Play a Round, Up/Down picks 9 or 18 holes for a solo round. If the
   server has a game in progress, the card resumes it at the first unfinished hole instead.
   Scores shows a ranked table from `GET /api/players`, ranked by handicap, average to par per 18 holes,
   best 9-hole and 18-hole rounds, wins, birdies or aces (use Left/Right to change). It opens on the first of
   those that ranks somebody (the handicap needs 3 rounds). Back closes it. **Sound**
   opens the volume settings (also in the pause menu; screen `settings`): Up/Down picks master, effects, crowd,
   ambience or interface, Left/Right moves it to the next 10 % mark, Back closes. The volumes are saved.
   While a round's top holes download, an overlay shows "Downloading hole 3 of 9…" and takes every key;
   Back cancels and the round never starts.
2. **Start:** the app's Start Game calls `POST /api/games`, the server sends `gameStarted`, and the sim
   loads hole 1 for the first player.
3. **Turn:** "OBI'S TURN" shows big in the centre, then shrinks into the player badge in the top right.
   This is driven by the same event that sends `turn`. The sim sends `state` (screen `game`, player, hole,
   par, strokes, club, aim, distance, lie). The app shows its gameplay mode with the club wheel, aim
   buttons and swing. `club` and `aim` change the HUD, the aim arrow and the on-screen panel, and a `shot`
   is hit with the aim applied.
4. **After each shot:** the sim sends `shotResult`, then after 2.5 s the next shot or player. Until then
   `state` keeps `screen: "game"` with `canShoot: false` and a `waitReason`; during an instant replay the
   screen is `replay`. A swing that arrives anyway is refused with `shotRejected {reason, id}` to the phones.
5. **Hole complete:** the scorecard appears (screen `holeComplete`), and Select loads the next hole.
6. **End:** a winner banner with confetti, then the final scorecard (screen `results`). Select returns to
   the menu, ready for the next game.
7. **Pause:** Back (or Esc) opens the pause menu (screen `paused`, shots refused, game sounds paused). Restart
   Hole replays the hole (not on the scorecards). Sound opens the volumes. Main Menu leaves the round, which can
   be resumed from Play a Round.

Keyboard and gamepad do the same: arrows (Left/Right aim and Up/Down club in game), Enter (or A) for
Select, Esc (or B) for Back. Space hits with the on-screen panel (Tab shows it). M (or Y) opens the course map.

## Course map

**M** (gamepad **Y**, or the app's **Map** button beside the aim control: `map {show}`) opens a top-down map of
the hole on the right of the TV while a hole is being played and its ball is at rest (a round or practice, between
shots too). `HoleMapPainter` paints it once per hole from the hole's own data, turned so the tee is at the bottom
and the pin at the top: the terrain's surface paint as flat colours with mowing stripes and a light hill shade, the
ponds, and every tree, shrub and rock as a canopy with its shadow. Over it `MapOverlay` draws the aim line from the
ball to the selected club's carry with a target there, distance arcs every 50 yd, the pin (yards from the ball) and
the tee, and every ball in play (the player up ringed, in their badge colour); under it: to the pin, the club's
carry, what that leaves to the pin, and the aim. Left/Right aim and Up/Down club redraw it live, so you can aim from
the map. It closes when the ball is hit, on Back, and on any other screen (pause, replay, scorecard, loading), and
sits under the turn banner, the scorecard and the fade. `state.mapOpen` lights the app's Map button.
`Tools/unity_scripts/CourseMapCheck.cs` opens it on the hole in Play mode, aims, hits and saves screenshots.

## Putting

With the putter on the green (or on short grass within 3 m of it: the fringe) the sim is in **putting mode**
(`RoundDirector.Putting.cs`): the camera drops low behind the ball looking at the cup, the HUD shows the putt
card (feet and metres, the rise or fall in cm, how long it plays, the Stimp and the read level), and `state`
carries `putting: true`, `puttDistance` (m), `elevation` (m, + uphill), `stimp` (ft), `puttPlaysAs` (m) and
`puttingAssist`. The app then shows its putting view with a power meter. Shots stay ordinary `shot` messages.
From the rough the putter is an ordinary shot (no putting mode, `puttPlaysAs` 0): the rough's rolling resistance
(over 10x the green's) and the lie's speed loss make the roll too far from the meter's flat-green scale to read.

- **Break preview** (`Ball/Runtime/PuttPreview.cs`, `PuttPredictor.cs`, `GreenReading.cs`): rolls a copy of the
  putt over the terrain with the ball's own physics at the speed that finishes 40 cm past the hole along the aim,
  and draws it as dots with slope arrows. **P** (or the panel's Assist button) cycles Full / Partial (the line
  fades out after `revealFraction`, 60 %) / Off. It re-solves when the ball or aim moves.
- **Distance** (`Ball/Runtime/PuttModel.cs`, and `Model/PuttModel.swift` in the app with the same constants):
  roll = Stimp (m) × (ball speed / 1.83 m/s)². The green is Stimp 9.3. `puttPlaysAs` is the flat-green roll of
  the putt the preview solved, so the meter's target already includes the slope.
- **Keyboard:** with the Putter preset the shot panel shows a distance slider in metres (Use the read loads
  `puttPlaysAs`). After each putt the HUD shows a strength bar against the read.
- `Tools/unity_scripts/PuttingCheck.cs` checks the preview against the real ball and saves screenshots.

## Practice: driving range and putting green

The **Driving Range** and **Putting Green** cards (`GameMode.practice`) load the practice scene (HoleSimulator) with a
facility built at runtime in place of its hole (`RoundDirector.Practice.cs`): `FacilityGround` makes a hole package in
code (heights from a function, surface polygons, trees) and the hole builder dresses it with a course theme like any
downloaded hole, then adds the extra pins and the yardage boards. Practice rules apply: nothing is sent to the
server's games (no `holeScore`, `gameId` 0); `shotResult` goes to the phones as player "Practice". After each shot the
facility records it and, 2 to 3 s later, sets up the next ball (`canShoot: false`, `waitReason` "Teeing up the next
ball" / "Next putt coming up" meanwhile). No instant replays. `state` carries `practice` (`range` or `puttingGreen`),
`attempts` and `made`; the HUD's stats card counts the shots, and a card on the right keeps the session.

- **Driving range:** a tee box at one end of a wide, nearly flat range; target greens with flags at 50, 100, 150, 200,
  250 and 300 yd (each exactly that far from the tee, a board beside it), more boards along both edges every 50 yd.
  The flag nearest the club's typical carry is the pin the aim points at (`distanceToPin`), so Left/Right aim from it.
  Every club works; the toast and the card show carry, total and offline, the card each club's average carry and total.
  The ball goes back to the tee after every shot (aim kept). The phone stays in its gameplay view (Shots, Flag).
- **Putting green:** a big contoured green (tilt, ridge, back tier, swale) with six cups; a cycle of eight putts
  (short, medium, long and breaking, uphill, downhill and sidehill, each spot worked out from the slope at its cup) is
  played with the putter only, in putting mode with the break line and the phone's power meter; the Stimp is the
  current physics, live profile included. Only the cup in play shows a flag. After each putt, holed or not, the ball
  goes to the next one. **Mulligan** replays the last putt; **Up/Down** step to the previous / next putt. The card
  shows made / attempts by kind; the phone's putt card shows "Made 3 of 7" where the Chip button would be.
- Pause menu: Restart starts a fresh session, Main Menu goes back to the cards. Keyboard: as on the practice hole
  (Space hits with the shot panel, Tab shows it).
- `Tools/unity_scripts/PracticeModesCheck.cs` sets up the cards (`Modes`, banners from `Banner`), opens a facility from
  the menu like the phone's D-pad, and checks each: flags at their distances, every club teed up again after its
  shot, aim; putts across the cups, the ball moving on, mulligan, made / attempts.

## Live ball physics

The ground response (rolling resistance, bounce, bounce friction per surface) can be tuned while playing, without
a rebuild: the app's Settings > Course physics saves a profile on the server (`PUT /api/physics`), the server sends
it to the sim (`physics`, and in `hello` when the sim connects), and `SimConnection` hands it to
`BallPhysicsProfile`. `GolfBall.Settings` is then a runtime copy of `BallPhysics.asset` with the overrides on top
(the asset is never changed); `GolfBall.ShotSettings` is what the current shot was hit with, so a change applies
from the next shot and replays match the shot. The green's Stimp (HUD, `state.stimp`, the preview and the meter)
follows at once. The sim logs `[BallPhysics] Physics profile applied from the next shot: ...` on every change;
offline it plays on the asset. Reset (or `DELETE /api/physics`) goes back to the asset.

The loop: change a slider, Save, hit a shot, read the log or `RolloutCheck`. `Tools/unity_scripts/RolloutCheck.cs`:
`Run` reports rollout per surface with the live profile applied, `Sample` applies a sample profile and checks the
asset stays untouched. When a tuning is right, copy it into `BallPhysics.asset` (and `DefaultSurfaces()` and the
app's `Model/CoursePhysics.swift`), then reset the profile.

## Sound and instant replay

Both only listen to the ball's and the round's events (`RoundDirector` adds them to its object); no gameplay code
plays a sound or moves a replay camera.

- **Sound** (`Game/Runtime/Audio/`): `GameAudio` plays `AudioCatalog` sounds (`Game/Resources/AudioCatalog.asset`:
  clips, volume and pitch ranges, 3D or 2D, bus) with master / SFX / crowd / UI / ambience volumes saved in
  PlayerPrefs (`GameAudio.MasterVolume`, `GameAudio.SetVolume`). `ShotSounds` does the strike (driver, iron or
  putter by the club, louder with ball speed), landings by surface, trees, rocks, the splash and the cup;
  `CrowdReactions` the gallery (roar, cheer, applause, "ooh", groan, from the score and the shot); `UiSounds` the
  D-pad ticks, the turn banner swoosh and the replay sting. Clips: `Game/Audio/CC0/` (CC0 recordings, sources in
  its `SOURCES.txt`) and `Game/Audio/Synth/` (`Tools/audio/synth_sounds.py`). `Tools/unity_scripts/SetupAudio.cs`
  rebuilds the catalog's clip lists from those folders.
- **Replay** (`Game/Runtime/Replay/`): `ShotRecorder` re-simulates each finished shot from the same spot and seed
  into a `ShotRecording` (path every 4 ms plus its events). In a round, `ReplayDirector` replays shots that
  `ReplaySettings.Reason` finds interesting (drives over 250 yd, approaches from 30 m+ finishing within 3 m, holed
  shots from off the green, holed putts over 6 m, trees, water), or the last shot when a player presses **Up**
  between turns. It holds the next turn (`RoundDirector.Hold`) and **Select or Back skips it** (its lead-in too;
  the HUD hides itself on the `replay` screen, so it always comes back, and leaving the hole drops the replay and the
  last shot). `ReplayCameraman`
  cuts it like TV: down-the-line on a long lens, a tower beside the flight with lead room, a landing-zone camera
  looking back at the ball dropping in (slow motion), a tree camera (square to a long rebound, wide enough for the
  hit and the bounce-back, panning after the ball), a low cup camera for holed putts and a
  blimp shot of the whole tracer after long shots. Cameras are kept out of the terrain and trees with a clear view
  (`CameraSpots`). Tuning: the `settings` on `ReplayDirector` (Golf Game object in Play mode).
  `Tools/unity_scripts/ReplayCheck.cs` renders frames of each camera for a drive, an approach, a chip, a tree hit
  and a holed putt, and checks the round flow.

## Running it

1. **Server.** The sim uses the hosted server by default, `wss://golf-server.obadiahfusco.xyz`
   (REST on `https://golf-server.obadiahfusco.xyz/api`), with the secret token from
   `Assets/GolfSim/Net/Resources/ServerToken.txt` (gitignored; the app bundles the same token from
   `Golf-app/Golf-app/ServerToken.txt`, and the live server reads it as `GOLF_API_TOKEN` from `Game-server/.env`).
   Without those files both fall back to `golf-sim-dev-token`, which only a local dev server accepts.
   For a local server: `cd Game-server && docker compose up --build -d` (port 8080).
2. **Sim.** Open `Assets/Scenes/MainMenu.unity` and press Play. The console shows
   `[SimConnection] Connected to ...`. To use a local server, select `GolfSim/Net/Resources/GolfServer.asset`,
   tick **Use Local Server** and set **Local Server Url** (`ws://localhost:8080` on the same PC). Server
   errors appear in the console as `[SimConnection] Server error: ...`.
3. **App.** The app talks to the game server on the PC its UDP discovery finds (port 8080), or the address
   typed in Settings › Game server. To use the hosted server, type `https://golf-server.obadiahfusco.xyz`
   there. The app and the sim must use the same server. See `Golf-app/README.md`. The direct UDP swing link
   (port 4242) is still used on the LAN when the WebSocket is down.
4. **Firewall** (local server only): allow inbound TCP 8080 for Docker, and inbound UDP 4242 for Unity (the
   LAN fallback).

## Testing without a phone

With the sim in Play mode, run these dev scripts with the Unity CLI (`~/.unity/bin/unity`):

```
unity command run_script --file Tools/unity_scripts/RoundPlayTest.cs --entry RoundPlayTest.Status
```

| Entry | Does |
|---|---|
| `Status` | the connection, the scene, the `state` the phones get, and the round's scores |
| `Solo` | starts a solo round, as Play a Round on the menu does |
| `Shot` / `ShotWild` | the current player hits the suggested club (a computed putt on the green) / a 60° slice |
| `Settle` | delivers queued server messages (a phone's shot), runs the ball to rest, then the next turn |
| `PlayHole` | shots until the hole is complete |
| `Continue`, `NavSelect`, `NavBack`, `NavLeft`, `NavRight`, `NavUp`, `NavDown` | Select on the scorecard, remote keys |

These scripts don't need the Editor to tick: they call `GolfBall.Advance` and `RoundDirector.RunPending`.
A fake phone can drive the sim through the real server:

```
cd Game-server
docker compose run --rm wsclient --role remote --send "nav select" --send "club 7I" --send "aim -2" \
  --send "shot 50 16 0 7000 0 7I" --wait 2
curl -X POST -H "Authorization: Bearer golf-sim-dev-token" -H "Content-Type: application/json" \
  -d '{"players":["Obi","Sam"],"holes":9}' http://localhost:8080/api/games
```

`Tools/unity_scripts/SetupRound.cs` recreates the assets and menu cards. `UdpShotReceiverCheck.cs` tests the
UDP link.
