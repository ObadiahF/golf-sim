# Golf Sim protocol: flows

Worked message sequences for [PROTOCOL.md](PROTOCOL.md).

## Remote navigation (menus)

```
app                              server                         sim
 |-- connect role=remote ------->|                               |
 |<-- hello {simConnected,state}-|                               |
 |-- nav {key:"down"} ---------->|-- nav {key:"down"} ---------->|  moves menu focus
 |-- nav {key:"select"} -------->|-- nav {key:"select"} -------->|  opens item
 |                               |<-- state {screen:"menu"} -----|  (whenever the screen changes)
 |<-- state {screen:"menu"} -----|                               |
```

The app shows the D-pad whenever the latest `state.screen` is not `"game"` (or there is no sim / no state).

## Starting a game

```
app                              server                         sim
 |-- POST /api/games {players} ->|  abandons any IN_PROGRESS game (gameFinished, status ABANDONED)
 |<-- 201 GameView --------------|-- gameStarted {game} ------->|  loads course, hole 1, first player
 |<-- gameStarted {game} --------|                               |
 |                               |<-- state {screen:"game",...} -|
 |<-- state {screen:"game"} -----|<-- turn {player,hole:1} ------|
 |<-- turn ----------------------|                               |
```

## One turn

```
app                              server                         sim
 |-- club {club:"7I"} ---------->|-- club ---------------------->|  selects club
 |-- aim {delta:-2} ------------>|-- aim ----------------------->|  rotates aim 2 degrees left
 |-- shot {speed,launch,...} --->|-- shot ---------------------->|  hits the ball
 |                               |<-- shotResult {...} ----------|  ball at rest
 |<-- shotResult ----------------|<-- state {strokes, lie,...} --|
 |<-- state ---------------------|                               |
   ... repeat until the ball is holed (holed:true) ...
 |                               |<-- holeScore {gameId,player,hole,par,strokes}
 |<-- scorecard {game} ----------|-- scorecard {game} ---------->|  (persisted in Postgres)
 |                               |<-- turn {player:"Sam",...} ---|  next player
 |<-- turn ----------------------|                               |
```

The sim decides turn order within a hole (Wii-style: e.g. farthest from the hole, or strictly in
`players` order) and sends `holeScore` once a player has holed out (or picked up after `skip`).

## Finishing a game

```
sim -- holeScore (last player, last hole) --> server
server --> everyone: scorecard {game status FINISHED} then gameFinished {game, winners}
sim shows the final scorecard and sends state {screen:"results"}; the app goes back to remote mode.
```

To quit early, the app calls `POST /api/games/{id}/end` (`ABANDONED` by default, or `{"status":"FINISHED"}`);
everyone gets `gameFinished`. Starting a new game also abandons the current one. A game finished early only crowns
players with complete cards, and only complete cards count in stats (see `POST /api/games/{id}/end`).
