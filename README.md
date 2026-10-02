# SwingRemote

Swing your iPhone like a golf club and the golf sim on your PC hits the shot. With the game
server running, one phone is also the TV remote and the controller for a round with friends. The phone measures
how fast it's rotating at impact and where the face points. It sends ball speed, launch, direction
and spin to the Unity sim over UDP, and the sim replies with "received" and then the carry and
total once the ball stops.

## Setup

1. Open `Golf-app.xcodeproj` in Xcode 26 (iOS 17+ deployment target).
2. Select the **Golf-app** target, go to **Signing & Capabilities** and pick your Team (a free
   Apple ID works). If the bundle id `com.obadiahfusco.Golf-app` is taken, change it.
3. Plug in the iPhone, choose it as the run destination and press Run. On the phone the first
   time, trust the developer under Settings › General › VPN & Device Management, and turn on
   Developer Mode if iOS asks.
4. Put the phone and PC on **the same Wi-Fi**, or connect the PC to the phone's Personal Hotspot.
   Guest and corporate Wi-Fi often block device-to-device traffic.
5. On the PC, start the sim and press Play in a scene with a hole and a `GolfBall`. The
   `UdpShotReceiver` attaches itself to the ball and logs `Listening for SwingRemote on UDP 4242`.
6. **Firewall:** allow inbound UDP 4242 for Unity (or your built player).
   - Windows: allow Unity Editor on *Private* networks when prompted, or run
     `netsh advfirewall firewall add rule name="SwingRemote" dir=in action=allow protocol=UDP localport=4242`.
   - macOS: System Settings › Network › Firewall: allow incoming connections for Unity.
7. Launch SwingRemote and allow **Local Network** access when asked (required, or nothing gets
   through) and **Motion & Fitness**. The status pill turns green with the PC's name once it finds
   the sim. If it doesn't, open Settings (gear icon) and type the PC's IP address.

## How to swing

1. Pick a club.
2. Tap **Address**, take your grip with the phone held like the club grip (screen facing you),
   address the ball and **hold still**. After about 2 seconds it buzzes: the address pose is set.
3. Swing. You feel a heavy tap when the shot is sent and a light one when the sim got it.
4. After each shot, **return to address and hold still** for half a second to re-arm. Swinging
   again without returning is ignored, so a waggle or the follow-through can't fire a second shot.
5. Tap **Re-address** whenever you change stance or grip.

**Half swings:** for safety indoors, swing at half speed and raise **Swing scale** (1.0–2.5×). It
multiplies the measured clubhead speed.

**Putting:** the putter uses much lower thresholds, so a gentle stroke counts. Hold very still at
address. When the sim says you're putting (`state.putting`), the Play tab switches to the putting view:
distance in metres and feet, the slope to the hole, and a power meter that fills live as you stroke
(rotation rate × putter radius × **Putt scale** × smash → ball speed → roll on this green's Stimp,
`Model/PuttModel.swift`, the same function as the sim's `PuttModel.cs`). The white mark is the sim's read
(`puttPlaysAs`, slope included). After the putt it shows "Putted 4.2 m of 5.0 m". A gentle stroke rolls
about 2–3 m and a firm one about 10 m; raise **Putt scale** (0.5–2.0×, Settings) if a normal stroke comes up
short.

**Safety:** grip firmly, use a wrist strap or lanyard, and keep clear of people, lamps and screens.

The screen stays awake while the app is in front. Motion stops when it goes to the background.

In the **simulator** (no motion sensors) a Debug-only **Simulate swing** button plays a
synthetic swing through the same detector, shot and network code. The simulator can reach a sim
running on the same Mac (it also tries `127.0.0.1` during discovery).

## Play a round

One phone drives everything; players take turns passing it around. Scores are kept per player
name on the game server (Spring Boot + Postgres in `../Game-server`).

1. **The server:** the app uses the hosted game server, `https://golf-server.obadiahfusco.xyz`
   (WebSocket `wss://golf-server.obadiahfusco.xyz/ws`), unless Settings › Game server names another.
   To run your own on the PC instead: `cd Game-server && docker compose up --build -d` (port 8080),
   allow inbound **TCP 8080** in the PC's firewall, e.g. on Windows
   `netsh advfirewall firewall add rule name="Golf server" dir=in action=allow protocol=TCP localport=8080`,
   and type its address under Settings › Game server (e.g. `192.168.1.20:8080`; an invalid address
   is refused with a message). Allow inbound **UDP 4242** too, for the direct swing link.
2. **Start the sim** (Unity) on the PC. It connects to the server as the `sim`.
3. **Open the app.** It connects to the game server, and finds the PC for the direct swing link (UDP
   discovery, or the IP you type in Settings). The header pill says **Sim connected** when the
   server and the sim are both up. If the server drops mid-hole, a "Disconnected — reconnecting…"
   banner shows and the controls that need it grey out until it's back.
4. **Players tab:** add everyone's name (up to 8), drag to set the turn order, pick **9 or 18
   holes** and tap **Start Game**. The sim loads hole 1. Starting a new game ends one in progress;
   **End game** abandons it (after a confirmation).
5. **Play tab, remote mode** (any sim screen except a hole): a TV-remote D-pad. Arrows move,
   **OK** selects, **Back** goes back. Between holes the scorecard shows on the phone too; press OK
   to go on. During an instant replay (`screen: "replay"`) OK or Back skips it.
6. **Play tab, gameplay mode** (switches automatically while the sim plays a hole): the header
   names the player up, with hole, par, strokes, distance and lie.
   - **Club wheel:** tap a club, or touch the ring and drag round to it. The sim suggests a club
     each turn and the wheel follows it.
   - **Aim:** hold the rotate buttons to turn the aim 1° per tick; tap the middle to aim at the pin.
   - **Swing:** the Address button in the middle of the wheel works as in practice. Shots go to the
     sim through the server; if the server is down they fall back to direct UDP. Between shots the
     sim says it can't take a swing (`state.canShoot: false`): the swing area dims, the reason
     (`state.waitReason`) replaces the instruction and swings aren't sent. If a shot still arrives too
     early, the sim's `shotRejected` reason shows in the Last shot strip.
   - **Menu** pauses the sim, **Mulligan** retakes the last shot, **Pick up** ends the hole for the
     player at the maximum score. These stay pinned above the tab bar; the rest scrolls if it doesn't fit.
   - **Replay last shot** shows between turns while the TV offers "▲ Replay" and sends `nav up`. The sim's
     `state.canReplay` decides when it is present; otherwise `canShoot: false` with
     `waitReason: "Wait for the next turn"` does.
   - **Putting:** the power meter starts empty for each putt (new turn, new hole, Address) and clears on
     leaving the putting view.
7. **Scores tab:** the live or last scorecard (front 9 / OUT, back 9 / IN, total) and the
   leaderboard (handicap, average to par per 18 holes, wins, birdies, aces; Best 9 / Avg 9 /
   Best 18 / Avg 18; the best 9-hole and best 18-hole rounds as separate lists). Scores come from complete 9- and 18-hole rounds only; shorter games still
   count as rounds and wins.

**Practice tab:** the single-player swing screen, as before.

### Game server messages

REST under `/api` with `Authorization: Bearer golf-sim-dev-token` (the token is in
`AppConfig.swift`): `POST /api/games {players, holes}`, `GET /api/games/current`,
`GET /api/games?limit=1`, `POST /api/games/{id}/end`, `GET /api/players`, `GET /api/leaderboard`.

WebSocket `ws(s)://<server>/ws?token=…&role=remote&name=<device>` (query values percent-encoded, `+` as `%2B`), JSON text frames. The phone sends
`nav {key}`, `club {club}`, `aim {delta}`, `aimReset`, `mulligan`, `skip`, `shot` (the UDP shot
fields) and `ping` every 20 s. It shows `hello`, `simStatus`, `state` (its `screen` picks remote or
gameplay mode), `shotResult`, `shotRejected`, `turn`, `scorecard`, `gameFinished` and `error`. It reconnects after
1, 2, then every 5 s. The full contract is `Game-server/docs/PROTOCOL.md`.

## Protocol (v2)

JSON in UDP datagrams. The phone sends to the PC's port **4242** from an ephemeral port, and the
sim replies to whatever address and port the datagram came from. All fields are optional unless
noted. Unknown fields are ignored on both sides.

### Phone → sim

**discover** sent during discovery and as a heartbeat every 2 s:
```json
{"v": 2, "type": "discover", "app": "SwingRemote"}
```

**shot**:
```json
{"v": 2, "type": "shot", "id": 4123001, "club": "Driver",
 "speed": 68.7, "launch": 12.0, "azimuth": -1.5, "back": 2600, "side": -360}
```
| field | unit | meaning |
|---|---|---|
| `speed` (required) | m/s | ball speed |
| `launch` | deg | vertical launch angle |
| `azimuth` | deg | start direction, **+ right** |
| `back` | rpm | backspin |
| `side` | rpm | sidespin, **+ curves right** |
| `id` | int | increases with every shot. The phone retries up to 3 times, 350 ms apart, with the same id until it gets an ack |
| `club`, `v` | | informational |

**v1 compatibility:** a datagram without `type` is a shot, so the original sample's
`{"speed","launch","azimuth","back","side"}` still works. v1 receivers ignore the new fields.
Without an `id`, retries can't be detected.

### Sim → phone

Unity's `JsonUtility` writes every field of the reply class, so unused fields show up as `""`/`0`.

**hello** (reply to discover):
```json
{"v": 2, "type": "hello", "name": "GOLF-PC", "status": "ready"}
```
`status` is `ready` or `busy` (ball in motion).

**ack** (reply to every shot):
```json
{"v": 2, "type": "ack", "id": 4123001, "status": "ok", "message": ""}
```
| `status` | meaning |
|---|---|
| `ok` | the ball was hit |
| `busy` | the ball was still moving, so the shot was ignored |
| `error` | bad datagram or no hole loaded; `message` says why (`id` is 0 if unreadable) |

A retry with the same `id` from the same address within 10 s gets the original ack again and is
not hit twice.

**result** sent once the ball from an `ok` shot comes to rest:
```json
{"v": 2, "type": "result", "id": 4123001, "carry": 223.0, "total": 259.8,
 "offline": 12.7, "surface": "fairway", "outcome": "Stopped"}
```
`carry`, `total` and `offline` (+ right) are in **yards**. `surface` is the resting lie (green,
fairway, rough, bunker, …). `outcome` is `Stopped`, `Holed`, `InWater` or `OutOfBounds`.

### Discovery

iOS needs a special Apple entitlement to send UDP broadcasts, so the phone sends a unicast
`discover` to each address on its Wi-Fi or hotspot subnet instead (narrowed to the /24 around the
phone on bigger networks), paced in batches of 64. The first `hello` wins, and that IP is saved
for next time. On launch the app tries the saved host first and sweeps only if it doesn't answer
within 2.5 s. Settings has **Find PC automatically** and manual IP/host-name entry.

## Tuning

**Club table:** `Golf-app/Model/Club.swift`. Per club:
- `radius`: swing radius in metres (hands' pivot to clubhead). Clubhead speed = rotation rate ×
  radius × swing scale. Raise it if your shots come out short across the board.
- `smash`: ball speed / clubhead speed.
- `launch`, `backspin`: fixed per club (the phone can't measure them).
- `faceToStart`: how much of the face angle becomes start direction.
- `detection`: `SwingThresholds`. Start rate, the peak needed, how far the backswing must go
  (`awayAngle`), how close to address impact must be (`impactAngle`), and the re-arm angle and
  stillness. Make the putter's rates lower if gentle putts don't register, and higher if tremor
  triggers it.

**Face direction:** if fades come out as draws (it depends on which way the screen faces in your
grip), toggle **Flip face direction** in Settings. Face angle is clamped to ±15°. Sidespin is
180 rpm per degree of face, and the putter gets no sidespin.

**Detector timing:** `Golf-app/Swing/SwingDetector.swift` (debounce samples, pre-roll, cooldown,
re-arm hold, waggle timeout).

## Code map

| | |
|---|---|
| `Model/Club.swift` | club table and detection thresholds |
| `Model/Shot.swift` | impact → shot (speed, direction, spin) |
| `Model/PuttModel.swift` | green speed (Stimp) → putt roll distance, shared constants with the sim |
| `Session/PuttMeter.swift` | the power meter's live value, the putt sent and its result; which Play screen to show |
| `Swing/SwingDetector.swift` | pure swing state machine (unit tested) |
| `Swing/SyntheticSwing.swift` | synthetic swing streams for tests and the simulator |
| `Swing/MotionSource.swift` | CoreMotion at 100 Hz, plus the Debug simulator source |
| `Network/SimProtocol.swift` | wire format |
| `Network/UDPSocket.swift`, `LocalSubnet.swift` | BSD UDP socket, subnet sweep |
| `Network/SimLink.swift` | discovery, heartbeat, shot retries, acks, results |
| `AppConfig.swift` | server token, hosted server, default port, round lengths, address checks |
| `Network/GameProtocol.swift` | game server WebSocket messages, scorecard and stats types |
| `Network/GameLink.swift` | WebSocket to the game server (remote role): reconnect, ping, sim state |
| `Network/GameAPI.swift` | REST client: start/end games, scorecards, players, leaderboard |
| `Model/Roster.swift` | player-name rules |
| `Views/Game/` | remote D-pad, gameplay (club wheel, aim), putting (power meter), players, scorecard, leaderboard |
| `Session/` | settings (UserDefaults), haptics, the session tying it together |
| `Views/` | SwiftUI screens |
| `Golf-appTests/` | Swift Testing: detector, shot math, protocols, subnet, REST stub, loopback UDP and WebSocket end to end |

Run the tests with:
```
xcodebuild test -project Golf-app.xcodeproj -scheme Golf-app -destination 'platform=iOS Simulator,name=iPhone 17 Pro'
```
Add `TEST_RUNNER_GOLF_LIVE_SERVER=127.0.0.1:8080` in front to also run read-only checks against a
running game server.

## Limits

A phone is not a launch monitor. The speed comes from the hand's rotation rate times an assumed
radius, so it ignores wrist release and shaft lag, and the effective radius differs from golfer
to golfer. Expect ±10–20 % and calibrate with the radius or the swing scale. Launch and spin come
from the club table and assume a centred strike. Face angle comes from yaw relative to the address
pose, so it drifts if you re-grip without re-addressing. At 100 Hz the impact sample can be up to
~10 ms from true impact on fast swings.
