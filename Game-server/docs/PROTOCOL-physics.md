# Golf Sim Game Server: Live ball physics

Part of the protocol (see `PROTOCOL.md` for auth, errors and the WebSocket rules). One global ball-physics
profile lives on the server, so ground friction can be tuned from the phone app without rebuilding the sim.
Code: `physics/PhysicsProfile.java`, `PhysicsField.java`, `PhysicsUpdate.java`, `api/PhysicsController.java`.

## The profile

The profile only holds **overrides** of the sim's built-in ground response (`BallPhysicsSettings` /
`BallPhysics.asset` in Unity). A field that is absent means "use the game's own value"; the server doesn't know
the defaults. Every tunable surface is always listed, in this order, each with only its overridden fields:

```json
{
  "surfaces": [
    { "surface": "green", "rolling": 0.07 },
    { "surface": "fairway" },
    { "surface": "tee" },
    { "surface": "rough", "restitution": 0.5, "friction": 0.6 },
    { "surface": "native" }, { "surface": "scrub" }, { "surface": "woods" }, { "surface": "bunker" }
  ]
}
```

Fields are never sent as `null` (Unity's JsonUtility would read null as 0); absent is the only "default".

| Field | Meaning | Accepted |
|-------|---------|----------|
| `rolling` | rolling resistance, fraction of g; lower rolls farther. Green 0.06 is Stimp 9.3: Stimp ft = 1.83² / (2 × rolling × 9.81) / 0.3048 | 0.02 .. 3 |
| `restitution` | bounce energy kept relative to a firm green (1) | 0 .. 1.2 |
| `friction` | sliding friction during a bounce: how much the turf grabs the ball | 0 .. 1.5 |

Surfaces: `green`, `fairway`, `tee`, `rough`, `native`, `scrub`, `woods`, `bunker` (water is a hazard, not
tunable). Ranges are inclusive. Built-in values (sim): green 1 / 0.4 / 0.06 (restitution / friction / rolling),
fairway and tee 0.9 / 0.45 / 0.11, rough 0.45 / 0.7 / 0.7, native 0.5 / 0.7 / 0.8, scrub and woods 0.4 / 0.75 / 1.0,
bunker 0.2 / 0.8 / 1.5.

## REST (token required, like every `/api` path)

#### `GET /api/physics`

`200` with the profile.

#### `PUT /api/physics`: partial update

```json
{ "surfaces": [ { "surface": "green", "rolling": 0.07, "friction": null }, { "surface": "rough", "friction": 0.6 } ] }
```

- A number sets the field, `null` clears it (back to the game's value), an absent field or surface is unchanged.
  `{"surfaces": []}` changes nothing. `surfaces` is required: `PUT {}` is refused (reset is `DELETE`).
- Unknown keys, surfaces and fields are refused, never ignored; nothing is saved unless the whole body is valid.
  `400` `"Validation failed"` with `fieldErrors`, keyed by position:

```json
{ "status": 400, "message": "Validation failed", "path": "/api/physics",
  "fieldErrors": { "surfaces[0].rolling": "must be between 0.02 and 3",
                   "surfaces[1].surface": "unknown surface 'lava' (one of green, fairway, tee, rough, native, scrub, woods, bunker)",
                   "surfaces[1].spin": "unknown field (allowed: surface, rolling, restitution, friction)",
                   "surfaces[2].surface": "duplicate surface 'green'",
                   "surfaces[3].friction": "must be a finite number or null",
                   "reset": "unknown field (allowed: surfaces)" } }
```

  A body that is not a JSON object gets `400 "Expected a JSON object"`; malformed JSON the usual `400`.
- Response `200` with the whole new profile. WS: `physics` broadcast to everyone.

#### `DELETE /api/physics`: reset to defaults

Clears every override. `200` with the (empty) profile. WS: `physics` broadcast.

## WebSocket

`hello` carries the current profile for every role, so a sim that connects later gets it:

```json
{ "type": "hello", "role": "sim", "...": "...", "physics": { "surfaces": [ { "surface": "green", "rolling": 0.07 }, "..." ] } }
```

After every `PUT` or `DELETE` (once committed) the server sends everyone (sims and remotes):

```json
{ "type": "physics", "profile": { "surfaces": [ { "surface": "green", "rolling": 0.07 }, "..." ] } }
```

`physics` is server-only: a client sending it gets `error "'physics' is sent by the server only"`.

## What the sim does with it

`GolfSim/Ball/Runtime/BallPhysicsProfile.cs`: the profile is applied on top of `BallPhysics.asset` in a runtime
copy (the asset is never modified), from the **next shot**: a shot in flight keeps the settings it was hit with,
and its replay too. The putting preview, the HUD's Stimp and `state.stimp` follow the new green at once. The sim
logs one line per change, e.g. `[BallPhysics] Physics profile applied from the next shot: green rolling 0.07
(Stimp 8.0 ft), rough friction 0.6`. Without a server (or with an empty profile) it plays on the asset's values;
it keeps the last profile through a disconnect.

## Iterating

1. App: Settings > Course physics. Move a slider (green Roll is shown as Stimp), Save.
2. The sim gets `physics` and logs the line; hit a shot. Repeat.
3. Reset to defaults (or `DELETE /api/physics`) to go back to the asset. To make a tuning permanent, copy the
   numbers into `BallPhysics.asset` (and `BallPhysicsSettings.DefaultSurfaces()`, and the app's
   `Model/CoursePhysics.swift` defaults table), then reset the profile.

From a shell: `curl -X PUT -H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json'
-d '{"surfaces":[{"surface":"rough","friction":0.6}]}' http://<server>:8080/api/physics`. In the Editor,
`Tools/unity_scripts/RolloutCheck.cs` (`Run`) reports rollout per surface with the live profile applied.
