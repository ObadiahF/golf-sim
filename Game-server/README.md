# Golf Sim Game Server

Spring Boot 3 (Java 21) backend for the party-style golf game: friends play on the Unity sim (PC + TV) and use
one iPhone app as the controller. The server stores players, games and hole scores in Postgres and relays
real-time messages between the app ("remote") and the sim over a WebSocket.

The REST + WebSocket contract is in **[docs/PROTOCOL.md](docs/PROTOCOL.md)**. The server also hosts the sim's
self-updates (releases built and published from the Mac, downloaded by the laptop): **[docs/UPDATES.md](docs/UPDATES.md)**.

## Everything runs in Docker Compose

Nothing needs Java or Maven on the host.

```bash
docker compose up --build -d                    # postgres + game-server on http://localhost:8080
docker compose logs -f game-server
docker compose run --rm test                    # mvn test against real Postgres (golf_test database)
docker compose exec postgres psql -U golf golf  # SQL shell
docker compose down                             # stop; data stays in the pgdata and updates volumes
docker compose down -v                          # stop and delete all data
```

| Service | Purpose |
|---------|---------|
| `postgres` | `postgres:16`, volume `pgdata`, healthcheck. Databases `golf` (app) and `golf_test` (tests, created on first start of the volume) |
| `game-server` | the app, port 8080, starts once postgres is healthy; Flyway migrates the schema on start. Volume `updates` (`/data/updates`): the self-update files |
| `test` | profile `test`: `mvn test` in `maven:3.9-eclipse-temurin-21`, project mounted, Maven repo cached in volume `m2` |
| `wsclient` | profile `tools`: fake sim/remote WebSocket client (Python + websockets) |

`docker compose run` starts postgres if needed and leaves it running; `docker compose down` stops it.

No container or image names are hard-coded, so a second, isolated stack can run next to the default one with its
own project name and port (containers `gsfix-*`, volumes `gsfix_*`):

```bash
PORT=8081 docker compose -p gsfix up --build -d
PORT=8081 docker compose -p gsfix down -v
```

## Configuration

Set in the shell or a `.env` file next to `docker-compose.yml`.

| Env var | Default | Meaning |
|---------|---------|---------|
| `GOLF_API_TOKEN` | `golf-sim-dev-token` | shared token for the app and the sim (REST `Authorization: Bearer`, WS `?token=`). The default is public: on a public server set a random one in `.env` (gitignored) and put the same value in the sim's and app's `ServerToken.txt` |
| `GOLF_UPDATE_TOKEN` | *(unset)* | admin token for publishing sim updates (`Authorization: Bearer`, only on `/api/updates/publish/...`); unset disables publishing. Generate with `openssl rand -hex 32`, never reuse `GOLF_API_TOKEN` (see [docs/UPDATES.md](docs/UPDATES.md)) |
| `PORT` | `8080` | host port published by compose |
| `POSTGRES_USER` / `POSTGRES_PASSWORD` | `golf` / `golf` | database credentials (used by postgres, the app and the tests) |

The app itself reads `DB_URL`, `DB_USER`, `DB_PASSWORD`, `GOLF_API_TOKEN`, `GOLF_UPDATE_TOKEN` and `UPDATES_DIR`
(compose sets them). Change the token
for anything beyond your own LAN, and set the same value in the app and the sim.

## Pointing the app and the sim at the server

1. Find the PC's LAN IP (Windows: `ipconfig`; macOS: `ipconfig getifaddr en0`), e.g. `192.168.1.50`.
   Allow inbound TCP 8080 in the PC's firewall.
2. iPhone app: REST base `http://192.168.1.50:8080`, WebSocket
   `ws://192.168.1.50:8080/ws?token=<token>&role=remote&name=<device name>`.
   iOS App Transport Security blocks plain `http`/`ws` by default: allow local networking
   (`NSAllowsLocalNetworking`) and add `NSLocalNetworkUsageDescription`.
3. Unity sim (usually on the same PC): `ws://localhost:8080/ws?token=<token>&role=sim&name=TV`.

Check from any machine: `curl http://192.168.1.50:8080/actuator/health`.

## Trying it out

```bash
TOKEN=golf-sim-dev-token
curl -X POST localhost:8080/api/games -H "Authorization: Bearer $TOKEN" \
     -H 'Content-Type: application/json' -d '{"players":["Obi","Sam"],"holes":9,"courseName":"Pebble"}'
curl localhost:8080/api/games/current -H "Authorization: Bearer $TOKEN"
curl localhost:8080/api/leaderboard -H "Authorization: Bearer $TOKEN"

# Interactive fake clients (one per terminal). Type shorthands or raw JSON; run with --help for the list.
docker compose run --rm wsclient --role sim --name TV          # prints relayed nav/club/aim/shot
docker compose run --rm wsclient --role remote --name Phone    # then type: nav up, club 7I, aim -2, shot 65 12 -1 2600 -300

# Scripted: send messages, listen 2 s, exit
docker compose run --rm -T wsclient --role sim --send "state game Obi 1 4" --send "score 1 Obi 1 4 5" --wait 2
```

The fake client connects to `ws://game-server:8080/ws` inside the compose network (override with `--url`).

## Endpoints (summary)

Every request except `/actuator/health` and the `/ws` upgrade needs `Authorization: Bearer <token>`; details and payloads in [docs/PROTOCOL.md](docs/PROTOCOL.md).
`/api/updates/publish/...` takes `GOLF_UPDATE_TOKEN` instead ([docs/UPDATES.md](docs/UPDATES.md)).

| Method | Path | Description |
|--------|------|-------------|
| POST | `/api/games` | start a game (abandons any game in progress) |
| GET | `/api/games/current` | the game in progress (204 if none) |
| GET | `/api/games/{id}` | scorecard |
| GET | `/api/games?limit=10` | recent games |
| POST | `/api/games/{id}/end` | finish or abandon |
| POST | `/api/games/{id}/scores` | upsert a hole score |
| GET | `/api/players`, `/api/players/{name}` | player stats and history |
| GET | `/api/leaderboard` | best rounds, most wins, best average |
| GET | `/api/updates/latest?platform=`, `/api/updates/releases?platform=` | the sim's latest release (manifest), the kept releases |
| GET | `/api/updates/blobs/{sha256}` | a release file (Range for resuming) |
| POST/PUT | `/api/updates/publish/...` | admin token: missing blobs, chunked upload, create a release |
| GET | `/api/ping` | service info |
| GET | `/actuator/health` | health (no token) |
| WS | `/ws?token=&role=sim\|remote&name=` | real-time relay |

## Layout

```
src/main/java/com/golfsim/server/
  api/      REST controllers, error handling
  auth/     shared-token filter (REST; the admin token on /api/updates/publish) and handshake interceptor (WS)
  config/   properties, clock, WebSocket registration
  game/     entities, repositories, GameService, GameView (scorecard)
  stats/    player stats and leaderboard
  update/   self-update releases: UpdateService (Postgres), BlobStore (files by sha256), ManifestRules
  ws/       WsMessage (the wire schema), WsHub (connections), GameSocketHandler, GameEventRelay
src/main/resources/db/migration/   Flyway SQL
docker/postgres/                   init script (creates golf_test)
tools/wsclient/                    fake WebSocket client
docs/PROTOCOL.md                   the contract for the app and the sim
docs/UPDATES.md                    self-updates: API, laptop flow, publishing, security
```
