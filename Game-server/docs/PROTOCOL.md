# Golf Sim Game Server: Protocol

This is the contract between the game server, the iPhone app (the "remote") and the Unity sim.
The wire schema lives in one place in code: `src/main/java/com/golfsim/server/ws/WsMessage.java`
(WebSocket) and `game/GameRequests.java`, `game/GameView.java`, `stats/StatsViews.java` (REST).
Live ball physics (`/api/physics`, the `physics` message, `hello.physics`) is in `PROTOCOL-physics.md`.
Rooms (one sim and its phones per room: `?room=`, `&id=`, `hello.room`, `game.room`) are in `PROTOCOL-rooms.md`.

- Base URL: `http://<server-lan-ip>:8080` (the PC running `docker compose up`)
- WebSocket: `ws://<server-lan-ip>:8080/ws?token=<token>&role=sim|remote&name=<device name>&room=<code>` (+ `&id=<install id>` for sims)
- Token: one shared secret, server env `GOLF_API_TOKEN` (`golf-sim-dev-token` by default, for local dev only; a public server sets its own)
- All bodies are JSON (UTF-8). Timestamps are ISO-8601 UTC strings, e.g. `"2026-10-02T07:43:47.309802Z"`.
- JSON input is strict, on REST and WebSocket alike: integer fields take JSON integers only (`1.7` and `9.0` are
  refused, as is `"9"` for a number), numbers must be finite (`1e400` overflows to infinity and is refused), and a
  key may appear only once per object. Nothing may follow the JSON value (`{...}garbage` is refused).
- Units: shot speed m/s, angles degrees (+ is right), spin rpm (+ side curves right), distances yards.

## 1. Auth

| Channel   | How                                                   | On failure                         |
|-----------|-------------------------------------------------------|------------------------------------|
| REST      | header `Authorization: Bearer <token>` on every request except the open paths below | `401` + error JSON (below) |
| WebSocket | query param `?token=<token>` on the upgrade request  | upgrade refused with HTTP `401`    |

The token check is deny-by-default: it applies to every path, including unknown ones (they get `401`, not `404`,
without a token). Only these exact paths are open: `/actuator/health`, `/actuator/health/liveness`,
`/actuator/health/readiness`, and `/ws` (which checks `?token=` itself).

Request paths must be in normal form, or the server answers `400` ("Malformed request path") before anything else:
no `;` path parameters, no empty segments (`//`), no `.` or `..` segments (plain or `%2e`-encoded), no encoded
slashes or backslashes, no control characters, no malformed `%` escapes. Percent-encoding other characters is fine
(e.g. `/api/players/Obi%20Smith`). Paths are case-sensitive: `/API/players` is not `/api/players`.

## 2. REST

### Errors

Every error uses the same body:

```json
{
  "timestamp": "2026-10-02T07:43:47.309Z",
  "status": 400,
  "error": "Bad Request",
  "message": "Validation failed",
  "path": "/api/games",
  "fieldErrors": { "players": "must not be empty" }
}
```

| Status | When |
|--------|------|
| 400 | validation failure (`fieldErrors` filled, e.g. `{"limit": "must be greater than or equal to 1"}` for `?limit=0`), malformed JSON (`"Malformed JSON request body"`), a value of the wrong type (`"Invalid value for 'holes'"`), unknown player for a game, hole out of range, duplicate names, a non-normalised path (`"Malformed request path"`) |
| 401 | missing or wrong token: `"message": "Missing or invalid API token"` |
| 404 | unknown game id, unknown player name, unknown route (with a valid token) |
| 405 | wrong HTTP method for the route (e.g. `DELETE /api/games/1`, `POST /api/ping`); the `Allow` header lists the right ones |
| 409 | ending a game that is not IN_PROGRESS; scoring an ABANDONED game; (rare) a conflicting concurrent change, safe to retry |
| 415 | a body that is not `Content-Type: application/json` |

### The game object (`GameView`)

Returned by every `/api/games` endpoint and embedded in WebSocket messages (`hello.game`,
`gameStarted.game`, `scorecard.game`, `gameFinished.game`).

```json
{
  "id": 1,
  "status": "IN_PROGRESS",
  "holesCount": 2,
  "courseName": "Demo Links",
  "room": "K7QF",
  "createdAt": "2026-10-02T07:43:47.309802Z",
  "finishedAt": null,
  "pars": [4, null],
  "players": [
    { "name": "Obi", "turnOrder": 1, "strokes": [4, null], "holesPlayed": 1, "total": 4, "par": 4, "toPar": 0 },
    { "name": "Sam", "turnOrder": 2, "strokes": [5, null], "holesPlayed": 1, "total": 5, "par": 4, "toPar": 1 }
  ],
  "winners": []
}
```

| Field | Meaning |
|-------|---------|
| `status` | `IN_PROGRESS`, `FINISHED` (every player has a score on every hole, or ended as finished) or `ABANDONED` |
| `players[].holesPlayed` | holes with a score; the card is *complete* when it equals `holesCount` |
| `courseName` | free text or `null` |
| `room` | the room the game was started in (`PROTOCOL-rooms.md`); `""` for the default room |
| `pars` | par per hole, index 0 = hole 1; `null` until a score for that hole has been recorded |
| `players` | in turn order; `turnOrder` is 1-based |
| `players[].strokes` | strokes per hole, index 0 = hole 1, `null` = not played yet; always `holesCount` long |
| `players[].total` / `par` / `toPar` | sums over the holes played so far; `toPar` = total - par (negative is under par) |
| `winners` | once `FINISHED`: names with the lowest total among **complete cards** (ties share the win); a player who has not scored every hole can never win. `[]` while in progress, when abandoned, or when nobody has a complete card |

### Endpoints

#### `POST /api/games?room=K7QF`: start a game

```json
{ "players": ["Obi", "Sam"], "holes": 9, "courseName": "Pebble" }
```

- `players`: 1..8 names, unique ignoring case. Turn order = list order.
- `holes`: integer 1..18, default 9. `courseName`: optional, max 100, no control characters (tab, newline, NUL...).
- `room` (query, optional): the room to start it in; absent or empty = the default room. Malformed: `400` with
  `fieldErrors.room`.
- The room's game still IN_PROGRESS is set to `ABANDONED` first (WS: `gameFinished` for it); other rooms' games
  are untouched. Simultaneous starts are handled one after another: each gets `201`, and the last one is the game
  left IN_PROGRESS.

Player names:

- Normalised first: Unicode NFKC (so fullwidth `Ｏｂｉ` is `Obi`), trimmed, runs of spaces become one space.
- 1..40 characters, counted as Unicode code points (an emoji is 1).
- No control characters (tab, newline, NUL...) and no invisible/format characters (zero-width space, BOM,
  direction overrides...), except the zero-width joiner inside emoji sequences. At least one visible character,
  so `" "` or `"\u200B"` are refused. Errors: `400` with
  `fieldErrors["players[0]"]: "must be 1 to 40 characters with at least one visible character and no control or invisible characters"`.
- Players are matched by a full case fold, so `İvan`, `ivan`, `IVAN` and `ıvan` are one player, as are `Straße`
  and `STRASSE`. An existing player keeps the original spelling.
- Response `201` with the game. WS: `gameStarted` is broadcast to the sim and remotes in the game's room.

#### `GET /api/games/current?room=K7QF`

`200` with the room's IN_PROGRESS game (`room` absent or empty = the default room), or `204 No Content` (empty
body) when none.

#### `GET /api/games/{id}`

`200` with the game (the scorecard). `404` if unknown.

#### `GET /api/games?limit=10`

`200` with an array of games, newest first. `limit` 1..100, default 10.

#### `POST /api/games/{id}/end`

```json
{ "status": "ABANDONED" }
```

Body optional; `status` is `ABANDONED` (default) or `FINISHED`. The game must be IN_PROGRESS (`409` otherwise).
Response `200` with the game. WS: `gameFinished` broadcast.

Ending early as `FINISHED` is allowed, but only complete cards count: winners are picked among players who scored
every hole (none, if nobody did), and incomplete cards never count toward wins, totals, averages, best rounds or
the handicap. Use `ABANDONED` when nobody finished.

#### `POST /api/games/{id}/scores`: record a hole score (idempotent upsert)

```json
{ "player": "Obi", "hole": 1, "par": 4, "strokes": 5 }
```

- `player`: a name in this game (case-insensitive). `hole`: 1..holesCount. `par`: 1..10. `strokes`: 1..99.
- Sending the same (player, hole) again replaces par and strokes. Allowed while IN_PROGRESS or FINISHED
  (corrections, retries); `409` for ABANDONED games.
- When every player has a score for every hole, the game becomes `FINISHED`.
- Scores for one game are applied one at a time, so simultaneous sends (several sims, retries) all succeed, the
  last write wins for the same hole, and exactly one of them finishes the game.
- Response `200` with the updated game. WS: `scorecard` broadcast, then `gameFinished` (exactly once) if this
  score finished the game.

#### `GET /api/players`

All players, sorted by name. `finishedRounds` and `wins` count *counted rounds*: complete cards (a score on every
hole) in FINISHED games, of any length. Bests, averages and the handicap count only *rated rounds*: counted rounds
of 9 or 18 holes, so a 1-hole game never beats or averages with a real round.

```json
[
  {
    "name": "Obi", "gamesPlayed": 3, "finishedRounds": 2, "wins": 1,
    "best9": 36, "best18": null, "avg9": 38.5, "avg18": null, "averageToPar": 5.0, "recentAverageToPar": 5.0,
    "handicap": null,
    "holes": { "played": 27, "holesInOne": 0, "eagles": 0, "birdies": 3, "pars": 14, "bogeys": 7, "doubleBogeysOrWorse": 3 },
    "lastPlayedAt": "2026-10-02T07:43:47.309802Z"
  }
]
```

`best9` / `best18` are the lowest total of a rated 9-hole / 18-hole round and `avg9` / `avg18` the average total of
those rounds; each is `null` without such a round. `averageToPar` and `recentAverageToPar` (last 5 rated rounds) are
per 18 holes: a 9-hole round's score to par counts double; `null` with no rated rounds. Averages and the handicap
have one decimal, rounded half away from zero, the same both sides of par: `2.75` is `2.8` and `-2.75` is `-2.8`.
`handicap` is a World-Handicap-System style index without course ratings: each rated round's score to par scaled
to 18 holes, best 8 of the last 20 (fewer, with the WHS adjustment, for short records); `null` until 3 rated
rounds. `holes` tallies every hole played in any game; its categories don't
overlap and add up to `played`: a hole in one counts only as `holesInOne`, whatever the par (a par-1 ace is not
also a par, nor a par-2 ace a birdie).

#### `GET /api/players/{name}`

Name is case-insensitive (URL-encode spaces). `404` if unknown. Up to 20 most recent games:

```json
{
  "stats": { "name": "Obi", "gamesPlayed": 3, "finishedRounds": 2, "wins": 1, "best9": 36, "best18": null,
             "avg9": 38.5, "avg18": null, "averageToPar": 5.0, "recentAverageToPar": 5.0, "handicap": null,
             "holes": { "played": 27, "holesInOne": 0, "eagles": 0, "birdies": 3, "pars": 14, "bogeys": 7,
                        "doubleBogeysOrWorse": 3 },
             "lastPlayedAt": "2026-10-02T07:43:47.309802Z" },
  "recentGames": [
    { "player": "Obi", "gameId": 7, "status": "FINISHED", "courseName": "Pebble", "holesCount": 9,
      "playedAt": "2026-10-02T07:43:47.309802Z", "holesPlayed": 9, "total": 36, "toPar": 0, "won": true }
  ]
}
```

#### `GET /api/leaderboard`

Top 10 of each, counted rounds only (complete cards in FINISHED games); `bestRounds`, `bestAverageToPar` and
`lowestHandicap` use rated rounds only (9 or 18 holes, see `GET /api/players`).

```json
{
  "bestRounds": [ { "player": "Sam", "gameId": 7, "status": "FINISHED", "courseName": "Pebble", "holesCount": 9,
                    "playedAt": "...", "holesPlayed": 9, "total": 34, "toPar": -2, "won": true } ],
  "mostWins": [ { "player": "Obi", "wins": 4, "finishedRounds": 6 } ],
  "bestAverageToPar": [ { "player": "Sam", "finishedRounds": 5, "averageToPar": 2.8 } ],
  "lowestHandicap": [ { "player": "Sam", "finishedRounds": 5, "handicap": 3.2 } ],
  "mostBirdies": [ { "player": "Obi", "birdiesOrBetter": 9, "holesPlayed": 54 } ]
}
```

`mostBirdies` counts birdies, eagles and holes-in-one across every game.

`bestRounds` is sorted by score to par per 18 holes (a 9-hole round's `toPar` counts double), then `total`, then
oldest first; `toPar` itself is the round's own. `bestAverageToPar` ranks the per-18 `averageToPar` and leaves out
players without one; `lowestHandicap` leaves out players without a handicap.

#### `GET /api/ping`

`{"service": "game-server", "version": "0.0.1-SNAPSHOT", "status": "ok", "serverTime": "..."}`; handy to test the token.

## 3. WebSocket

### Connecting

```
ws://192.168.1.50:8080/ws?token=golf-sim-dev-token&role=remote&name=Obi%27s%20iPhone
```

| Param | Required | Values |
|-------|----------|--------|
| `token` | yes | the shared token (`401` on the upgrade otherwise) |
| `role` | yes | `sim` (Unity) or `remote` (iPhone app); `400` on the upgrade otherwise |
| `name` | no | device name shown to others (default `sim-xxxxxx` / `remote-xxxxxx`) |
| `room` | no | room code, 4-8 letters/digits, case-insensitive; absent or empty = the default room (`PROTOCOL-rooms.md`); `400` when malformed |
| `id` | sims | the sim's permanent install id, 1-64 of `A-Z a-z 0-9 _ -`; `400` when malformed |

URL-encode query values (UTF-8). A query with malformed `%` escapes refuses the upgrade with `400`.
Device names are cleaned rather than refused: normalised like player names, control and invisible characters
dropped, cut to 40 code points (never inside an emoji); a name with nothing visible left gets the default.
Everything below is scoped to the client's room: relays, `simStatus`, `hello` and game events stay inside it (only
`physics` reaches everyone). A room holds one sim and any number of remotes; a second sim is refused (or replaces
the first when it has the same install `id`), see `PROTOCOL-rooms.md`.

### Framing and rules

- Every frame is one JSON text frame: an object with a string `type` plus that type's fields, and nothing after it.
  A binary frame gets `error {"message": "Binary messages are not supported"}`; the connection stays open.
- Field names are case-sensitive camelCase; unknown extra fields are ignored by validation.
- Relayed messages (remote -> sim, sim -> remotes) are validated, then forwarded **byte-for-byte unchanged**,
  so extra fields reach the other side. The sender gets nothing back on success.
- Anything invalid gets an `error` sent back to the sender only; the connection stays open.
- Messages may be up to 65,536 characters (64 KiB of ASCII). A longer one is dropped and the sender gets
  `error {"message": "Message too large (max 65536 characters)"}`; the connection stays open.
- Numbers must be finite and integer fields integers (see the JSON rules at the top), anywhere in the message,
  extra fields included, because relayed messages are forwarded as sent. Duplicate keys are refused.
- Keepalive: send `{"type":"ping"}` every ~20 s, whether or not you are sending other traffic; the server answers
  `{"type":"pong"}`. Treat the server as gone only after ~60 s without receiving anything (a `pong` counts).
  The server closes a connection it has received nothing from for 60 s (a frozen or vanished client), so a
  listener that never sends must still ping.
  Reconnect on close (back off 1 s, 2 s, 5 s ...); the `hello` you get after reconnecting has everything you need
  to resync.

### Who sends what

| type | from | to | purpose |
|------|------|----|---------|
| `hello` | server | the new connection | first message after connecting |
| `simStatus` | server | remotes | the room's sim connected / disconnected |
| `ping` / `pong` | any / server | sender | keepalive |
| `error` | server | sender | rejected message |
| `nav` | remote | sim | D-pad: menu navigation |
| `club` | remote | sim | club picked |
| `aim` | remote | sim | turn aim by a delta |
| `aimReset` | remote | sim | aim back at the pin/default line |
| `shot` | remote | sim | swing detected: hit the ball |
| `mulligan` | remote | sim | retake the last shot (optional for the sim) |
| `skip` | remote | sim | skip / pick up on this hole (optional for the sim) |
| `map` | remote | sim | show or hide the course map on the TV (optional for the sim) |
| `state` | sim | remotes | what is on screen (drives the app's mode) |
| `shotResult` | sim | remotes | where the shot ended up |
| `turn` | sim | remotes | whose turn it is now |
| `shotRejected` | sim | remotes | a `shot` arrived while the sim couldn't hit it |
| `holeScore` | sim | server | persist a player's strokes on a hole |
| `gameStarted` | server | the game's room | a game was started (`POST /api/games`) |
| `scorecard` | server | the game's room | full game state after any score is recorded |
| `gameFinished` | server | the game's room | the game left IN_PROGRESS (finished or abandoned) |
| `physics` | server | everyone, every room | the ball-physics profile changed (`PROTOCOL-physics.md`) |

Sending a message as the wrong role, or a server-only type, gets an `error`.

### Server -> clients

#### `hello`

```json
{
  "type": "hello",
  "role": "remote",
  "room": "K7QF",
  "simConnected": true,
  "remotes": ["Obi's iPhone", "Sams-iPhone"],
  "game": { "id": 1, "status": "IN_PROGRESS", "...": "GameView, or null when no game is in progress" },
  "state": { "type": "state", "screen": "game", "currentPlayer": "Obi", "hole": 1, "par": 4 },
  "physics": { "surfaces": [ { "surface": "green", "rolling": 0.07 }, { "surface": "fairway" } ] }
}
```

`role` echoes yours, `room` your normalised room code (`""` for the default room). `remotes` lists the device names
of the remotes in your room (including you if you are one). `game` is your room's game in progress.
`state` is the last `state` message your room's sim sent (verbatim), or `null` (no sim / none sent yet).
`physics` is the live ball-physics profile (`GET /api/physics`).

#### `simStatus`

```json
{ "type": "simStatus", "connected": false }
```

Sent to the room's remotes when its sim connects or disconnects (not when a sim is refused or replaced).

#### `gameStarted`

```json
{ "type": "gameStarted", "game": { "id": 2, "status": "IN_PROGRESS", "holesCount": 9, "courseName": "Pebble",
  "players": [ { "name": "Obi", "turnOrder": 1, "strokes": [null, null, null, null, null, null, null, null, null], "...": "..." },
               { "name": "Sam", "turnOrder": 2, "...": "..." } ], "...": "full GameView" } }
```

The sim loads the course and starts the round with `game.id`, `game.players` (turn order),
`game.holesCount` and `game.courseName`.

#### `scorecard`

```json
{ "type": "scorecard", "game": { "...": "full GameView" } }
```

#### `gameFinished`

```json
{ "type": "gameFinished", "game": { "id": 1, "status": "FINISHED", "winners": ["Obi"], "...": "full GameView" } }
```

`game.status` is `FINISHED` (all scores in, or ended as finished) or `ABANDONED` (ended, or replaced by a new game).

#### `pong`, `error`

```json
{ "type": "pong" }
{ "type": "error", "message": "Unknown message type 'teleport'" }
```

Error messages you may see: `Missing "type"`, `Unknown message type '<t>'`, `Expected a JSON object`,
`Invalid JSON` (also for data after the object), `Invalid JSON: Duplicate field '<key>'`, `Binary messages are not supported`, `Invalid number` (`NaN` / `Infinity` tokens), `Invalid value for '<field>'`,
`Invalid value for '<field>': numbers must be finite`, `Message too large (max 65536 characters)`, `<type>: <field> <constraint>` (e.g. `club: club must not be blank`),
`'<type>' must be sent by a sim|remote`, `'<type>' is sent by the server only`, `no sim connected` (in your room),
`Another sim is already connected to room <CODE>` / `... to this server` and `Replaced by a new connection from the
same sim` (each followed by the server closing the connection, `PROTOCOL-rooms.md`),
and the REST messages for `holeScore` (e.g. `Game 9 not found`, `hole must be between 1 and 9`).

### Remote -> sim (relayed)

If no sim is connected in the remote's room it gets `error {"message": "no sim connected"}` and nothing is relayed.

```json
{ "type": "nav", "key": "up" }
```
`key`: `up` | `down` | `left` | `right` | `select` | `back`.

```json
{ "type": "club", "club": "7I" }
```
`club`: non-blank string. Suggested names: `Driver`, `3W`, `5W`, `4H`, `4I`..`9I`, `PW`, `GW`, `SW`, `LW`, `Putter`.

```json
{ "type": "aim", "delta": -2.0 }
```
`delta`: degrees to turn the aim, + is right. Required number.

```json
{ "type": "aimReset" }
{ "type": "mulligan" }
{ "type": "skip" }
```

```json
{ "type": "map", "show": true }
```
`show`: required boolean, open (`true`) or close the course map on the TV. The sim opens it only while a hole is being
played with the ball at rest and closes it itself when the ball is hit; `state.mapOpen` says whether it is up.

```json
{ "type": "shot", "speed": 65.2, "launch": 12.5, "azimuth": -1.5, "back": 2600, "side": -300, "club": "7I", "id": 7 }
```

Same fields and units as the phone's UDP shot datagram:

| Field | Required | Meaning |
|-------|----------|---------|
| `speed` | yes, > 0 | ball speed, m/s |
| `launch` | yes | launch angle, degrees |
| `azimuth` | yes | start direction, degrees, + right |
| `back` | yes | backspin, rpm |
| `side` | yes | sidespin, rpm, + curves right |
| `club` | no | club used |
| `id` | no | integer shot id from the phone (for de-duplicating retries); `1.9` or `"7"` is refused |

### Sim -> remotes (relayed)

```json
{ "type": "state", "screen": "game", "gameId": 1, "currentPlayer": "Obi", "hole": 3, "par": 4, "strokes": 1,
  "club": "7I", "aim": -2.0, "distanceToPin": 152.3, "lie": "fairway" }
```

Only `screen` is required (non-blank). Send it whenever any of these change. The app uses `screen` to pick
its mode: `"game"` = gameplay mode (club picker, aim buttons, swing); anything else = remote mode (D-pad), so a
screen the app doesn't know degrades to the remote.
Suggested screens: `menu`, `courseSelect`, `loading`, `game`, `paused`, `settings`, `replay`, `holeComplete`,
`scorecard`, `results`. `replay`: an instant replay is on the TV; the phone shows the remote and OK/Back skip it.
`settings`: the Sound panel is open (from the menu or the pause menu); Up/Down choose, Left/Right change, Back closes.
`aim` degrees (+ right), `distanceToPin` yards, `strokes` = strokes taken on this hole so far.

Optional fields (relayed unchanged; older sims leave them out):

| Field | Meaning |
|-------|---------|
| `canShoot` | bool: a swing would be hit now. Absent = `true`. Between shots (ball moving, next player up) the sim keeps `screen:"game"` with `canShoot:false`, so the phone stays in gameplay mode without flickering |
| `waitReason` | why not, e.g. `"Wait for the next turn"`; `""` while `canShoot` |
| `canReplay` | the sim offers an instant replay of the last shot now; `nav {key:"up"}` plays it |
| `mapOpen` | bool: the course map is up on the TV (the app's Map button is lit; `map` changes it) |
| `wind` | wind speed in mph, `0` when calm (each hole of a round has its own; the range and putting green are calm) |
| `windAngle` | where the wind blows relative to the aim, degrees clockwise: `0` helping, `90` left to right, `180` into the player |
| `putting` | bool: the current player is putting (the app shows its putting view) |
| `puttDistance` | metres to the pin |
| `elevation` | metres the pin sits above (+) or below (-) the ball |
| `stimp` | green speed, Stimpmeter feet |
| `puttPlaysAs` | metres the putt plays as on a flat green (the app's power-meter target) |
| `puttingAssist` | `full`, `partial` or `off`: how much of the break line the sim draws |
| `practice` | `range` (driving range) or `puttingGreen` while a practice facility is on the TV, else `""`; `gameId` is 0 and no `holeScore` is sent |
| `attempts`, `made` | the facility's shots (putts) this session, and how many were holed |

```json
{ "type": "shotResult", "player": "Obi", "carry": 231.4, "total": 248.0, "lie": "fairway", "holed": false, "strokes": 1 }
```

`player` required. `carry`/`total` yards, `lie` e.g. `tee`, `fairway`, `rough`, `bunker`, `green`, `water`, `ob`,
`holed` true when the ball went in, `strokes` on this hole including this shot.

```json
{ "type": "turn", "player": "Sam", "hole": 3, "strokes": 0 }
```

`player` and `hole` required: whose turn it is now; `strokes` taken on this hole so far.

```json
{ "type": "shotRejected", "reason": "Wait for the next turn", "id": 7 }
```

Sent when a phone's `shot` arrives while the sim can't hit it (between turns, ball still moving, replay...).
`reason` required non-blank string, shown to the player; `id` optional integer: the rejected `shot`'s `id`, echoed.

### Sim -> server (persisted)

```json
{ "type": "holeScore", "gameId": 1, "player": "Obi", "hole": 1, "par": 4, "strokes": 5 }
```

Same rules as `POST /api/games/{gameId}/scores` (idempotent upsert; safe to resend). On success the server
broadcasts `scorecard` to the game's room (the sender included when it is that room's sim), then `gameFinished`
if that completed the game. Scores are recorded by `gameId`, whatever room the sim is in.
On failure the sim gets `error`.

## 4. Flows

Step-by-step flows (menus, starting a game, one turn, finishing) are in [PROTOCOL-flows.md](PROTOCOL-flows.md).
