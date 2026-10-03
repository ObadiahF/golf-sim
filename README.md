# Golf Sim

| Folder | What |
|---|---|
| `Golf-sim/` | Unity 6 (URP) golf simulator, course tools (`Tools/`), course trainer website, docs |
| `Golf-app/` | iPhone app: swing remote, TV-remote, players, putting meter |
| `Game-server/` | Spring Boot + Postgres multiplayer backend (docker compose) |

See `Golf-sim/Docs/multiplayer.md` for how they fit together. The sim updates itself from the game server
(`Game-server/docs/UPDATES.md`; publish with `Golf-sim/Tools/publish/publish.sh windows`).
