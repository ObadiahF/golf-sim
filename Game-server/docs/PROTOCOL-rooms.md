# Golf Sim Game Server: Rooms

Part of the protocol (see `PROTOCOL.md` for auth, errors and the WebSocket rules). A room is one sim and the phones
that play on it, so two sims on one server (a second PC, a Unity editor in Play mode) don't make every phone's club
and screen flip between them. Code: `game/Rooms.java`, `ws/WsHub.java`, `auth/TokenHandshakeInterceptor.java`.

Pairing is by code only: the TV shows its room code (main menu, pause menu), and each phone types it once in
Settings, where it is saved. The server never hands out or discovers codes.

## Room codes

- 4 to 8 characters, `A-Z` and `0-9`, case-insensitive. The server trims and upper-cases what it receives, so
  ` k7qf ` is `K7QF`; clients should normalise the same way before showing or saving a code.
- The sim makes its code once (5 characters from `ABCDEFGHJKMNPQRSTUVWXYZ23456789`: no `0`/`O`/`1`/`I`/`L`) and
  keeps it for good, next to a permanent random **install id** (a GUID or 16+ hex characters).
- **The default room**: a client that sends no room, or an empty one, is in the default room, `""` on the wire.
  The sims and apps published before rooms all meet there and keep working together exactly as before (with one
  sim at a time, below).

## WebSocket

```
ws://<server>:8080/ws?token=<token>&role=sim&name=Living%20Room%20TV&room=K7QF&id=3f2a9c0e5b7d4e18a6c2
ws://<server>:8080/ws?token=<token>&role=remote&name=Obi%27s%20iPhone&room=K7QF
```

| Param | Required | Values |
|-------|----------|--------|
| `room` | no | room code; absent or empty = the default room |
| `id` | no (sims should send it) | the sim's install id, 1-64 characters `A-Z a-z 0-9 _ -`; absent or empty = none. Remotes may leave it out (it is checked, but unused, if they send one) |

A malformed `room` or `id` refuses the upgrade with HTTP `400`, like a bad `role`.

### Everything is scoped to the room

- Remote commands (`nav`, `club`, `shot`, ...) go only to the sim in the sender's room. With no sim in the room the
  remote gets `error {"message": "no sim connected"}` (also when a sim is connected in another room).
- Sim updates (`state`, `turn`, `shotResult`, `shotRejected`, ...) go only to the remotes in the sim's room.
- `simStatus` goes to the room's remotes when its sim connects or disconnects.
- `hello` describes the client's room: `room` (normalised code, `""` for the default room), `simConnected`,
  `remotes`, `game` (the room's game in progress) and `state` (the last `state` from the room's sim).
  `physics` is server-wide.
- `gameStarted`, `scorecard` and `gameFinished` go to the sim and remotes in the game's room (`game.room`).
- `physics` (`PROTOCOL-physics.md`) still goes to everyone in every room: there is one profile per server.
- `holeScore` is recorded by `gameId`, whatever room the sim is in (unchanged).

```json
{ "type": "hello", "role": "remote", "room": "K7QF", "simConnected": true, "remotes": ["Obi's iPhone"],
  "game": null, "state": { "type": "state", "screen": "menu" }, "physics": { "surfaces": [ "..." ] } }
```

### One sim per room

When a sim connects to a room that already has one:

| Case | What happens |
|------|--------------|
| same install `id` (the TV reconnecting before the server noticed its old socket died) | the **old** connection gets `error {"message": "Replaced by a new connection from the same sim"}` and is closed (close code `1008`); the new one gets `hello` and stays. Remotes see no `simStatus` change |
| a different `id`, or either sim sent no `id` | the **new** connection gets `error {"message": "Another sim is already connected to room K7QF"}` (`"Another sim is already connected to this server"` in the default room), no `hello`, and is closed with close code `4009`. The sim already there is untouched |

A refused sim should say so on the TV and retry slowly (e.g. every 30 s), not in a tight reconnect loop.
Remotes are not limited: any number may join a room.

## REST

`POST /api/games` (start) and `GET /api/games/current` take an optional `?room=CODE` (absent or empty = the
default room; normalised like the WebSocket parameter):

- `POST /api/games?room=K7QF` starts a game in that room and abandons only that room's game in progress
  (`gameFinished` for it, to that room). Other rooms keep their games.
- `GET /api/games/current?room=K7QF` returns that room's game in progress, or `204`.
- A malformed room is `400 "Validation failed"` with `fieldErrors: {"room": "must be 4 to 8 letters or digits"}`.

Every game object carries `room`: the room it was started in, `""` for the default room. The other game endpoints
(get by id, list, end, scores) are unchanged and work across rooms.

## Storage

`games.room` (`varchar(8)`, not null, default `''`; migration `V5__game_rooms.sql`). Games from before rooms are in
the default room. At most one game is IN_PROGRESS per room (a partial unique index on `room`).
