# Hole package format, version 2

The contract for how a golf hole is stored. Every producer writes it exactly this way; every consumer reads it
and renders/plays **exactly what it says**:

| Role | Who |
|---|---|
| Producers | `Tools/course_gen` (generated holes), `Tools/course_prep` (real holes from OSM + USGS) |
| Consumers | Unity Course Builder (the game), `Tools/course_trainer` (browser preview), later the game server |

The point: what you see and rate in the trainer is what the game builds. The layout, terrain, water, every tree,
shrub and rock, and where it stands, how tall it is and which way it faces, all come from the package. Consumers
choose only *how things look* (which 3D model represents a "conifer", textures, lighting), never *what is where*.

Machine-checkable parts: [`hole.schema.json`](hole.schema.json), [`gen.schema.json`](gen.schema.json).
Everything below is binding too.

## 1. Package folder

```
<id>/                 folder name == hole.json "id"
  hole.json           required  metadata, layout, water            (JSON, UTF-8)
  heightmap.raw       required  terrain elevation                  (binary, §4)
  objects.bin         required  trees, shrubs, rocks               (binary, §5)
  gen.json            generated holes only: seed, preset, knobs    (JSON, gen.schema.json)
  preview.png         optional  top-down picture for humans
```

- **Ids** are lowercase `[a-z0-9_-]`, 3 to 64 characters, unique across all holes. Generated: `<preset>_<seed>_<hash6>`.
  Real: `<course-slug>_<holeRef>`.
- **Immutable once kept.** After a package is rated, saved or used in a game, its files never change. Any change
  (re-generate, tweak, re-dress) produces a **new id**. Scores and ratings point at ids, so they stay valid.
- **Where packages live:** `Assets/CourseData/generated/` is scratch (unrated holes get pruned).
  `Assets/CourseData/Saved/` is kept for good. Anything else in a package folder (Unity's `TerrainData.asset`,
  `Water*.asset`, `Cup*.asset`, `.meta` files) is a derived build artefact: consumers may delete and rebuild it.

## 2. Coordinates and units

- One **square, north-up tile**, `sizeMeters` on a side. The origin is the **south-west corner**.
- `x` = meters **east**, `y` = meters **north**. (Unity maps `y` to its `z` axis.)
- Elevation in meters, same datum as `heightmap.minElevation` / `maxElevation`.
- Angles are degrees **clockwise from north**.
- Everything a hole uses (tee, pin, areas, objects) lies inside `[0, sizeMeters]` on both axes.

## 3. hole.json

See the schema for exact types. In short:

| Field | Meaning |
|---|---|
| `version` | `2` |
| `id`, `course`, `holeRef`, `par`, `handicap` | identity and scorecard data (`handicap` 0 = unknown) |
| `theme` | how to dress it: `coastal`, `parkland`, `forest`, `lakes`, `links`, `desert`, `mountain` |
| `source` | optional: `osm` (with original CRS and origin) or `generated` |
| `sizeMeters`, `heightmap` | tile size; heightmap file, resolution and elevation range (§4) |
| `tee`, `pin`, `holePath` | tee centre, cup position, playing line from tee to pin |
| `areas` | surface polygons (below) |
| `water` | water bodies: flat `level` + triangulated outline; terrain under them is pre-carved below the level |
| `objects` | `{ "file": "objects.bin", "count": N }` (§5) |

**Surfaces** and their **paint priority** (later wins where polygons overlap). Ground covered by no polygon is
`native`.

| Priority | Surface | Ball physics | Objects allowed |
|---|---|---|---|
| 0 | `native` | native / waste area | yes |
| 1 | `rough` | rough | yes (sparse) |
| 2 | `scrub` | native / waste area | yes |
| 3 | `woods` | rough under trees | yes (dense) |
| 4 | `fairway` | fairway | no |
| 5 | `tee` | tee | no |
| 6 | `green` | green | no |
| 7 | `bunker` | sand | no |
| 8 | `water` | hazard | no |

Polygons are flat `[x0, y0, x1, y1, ...]` rings; the first ring is the outline, the rest are holes; rings close
implicitly (don't repeat the first point).

## 4. heightmap.raw

- `resolution × resolution` samples (2^k + 1: `33` up to `4097`; real holes use 1025 or 2049), **unsigned 16-bit little-endian**,
  no header. File size = `resolution² × 2` bytes.
- Row 0 is the **south** edge, column 0 the **west** edge; samples span the whole tile edge to edge, so
  sample spacing = `sizeMeters / (resolution − 1)`.
- `elevation = minElevation + value / 65535 × (maxElevation − minElevation)`.

## 5. objects.bin

Every tree, shrub and rock, as fixed-size little-endian records after a 16-byte header.

**Header (16 bytes)**

| Offset | Type | Value |
|---|---|---|
| 0 | 4 × char | magic `GOBJ` |
| 4 | u16 | format version, `1` |
| 6 | u16 | record size in bytes, `12` (readers skip any bytes beyond the fields they know) |
| 8 | u32 | record count (must equal `hole.json` `objects.count`) |
| 12 | u32 | reserved, `0` |

**Record (12 bytes)**

| Offset | Type | Field | Decoding |
|---|---|---|---|
| 0 | u8 | `kind` | enum below |
| 1 | u8 | `variant` | 0 to 255; picks which model of the kind. Same value = same model, everywhere |
| 2 | u8 | `rotation` | `value × 360 / 256` degrees clockwise from north (1.4° steps) |
| 3 | u8 | reserved | `0` |
| 4 | u16 | `x` | `value / 65535 × sizeMeters` meters east (about 1 cm steps on a 600 m tile) |
| 6 | u16 | `y` | `value / 65535 × sizeMeters` meters north |
| 8 | u16 | `height` | centimeters from the ground to the top of the object |
| 10 | u16 | `radius` | centimeters: solid footprint for ball collision (trunk, rock body, shrub) |

**`kind` enum.** Append-only: never renumber or reuse a value. Readers treat unknown kinds as "skip, but warn".

| Value | Kind | Typical height |
|---|---|---|
| 0 | `conifer` | 10 to 25 m |
| 1 | `deciduous` | 8 to 18 m |
| 2 | `palm` | 7 to 14 m |
| 3 | `cactus` | 1.5 to 6 m |
| 4 | `shrub` | 0.6 to 2.5 m |
| 5 | `boulder` | 1 to 3.5 m |
| 6 | `rock` | 0.2 to 1 m |

Records are sorted by `kind`, then `y`, then `x`, so identical holes produce identical bytes.

**Consumers must:**

- place every record, at its `x`/`y` with the base on the terrain surface, rotated by `rotation`;
- scale the chosen model so its top is `height` above the ground (uniform scale, from the model's own bounds);
- pick the model with `variant` deterministically (e.g. index = `variant × modelCount / 256`);
- use `radius` for ball collisions.

**What isn't in the file:** grass tufts, ferns, flowers and other ground cover under 0.5 m with no gameplay effect.
A consumer may add those however it likes. Everything else comes from `objects.bin`.

## 6. Versioning

- `hole.json` `version` changes when a consumer would misread the new data. Consumers refuse versions they
  don't know and name the tool that upgrades them.
- New optional JSON fields don't bump the version, but `additionalProperties: false` in the schema means
  they're added to the schema first.
- `objects.bin` has its own version in its header; new fields go at the end of the record and grow the
  record size, so old readers keep working.
- Version 1 packages (inline `trees`, no `objects.bin`, top-level heightmap fields) are upgraded by
  re-running the producer or the migration command; consumers read version 2 only.

## 7. Validation

Producers validate every package they write: JSON against the schemas, plus these checks:

- heightmap file size;
- objects header magic, version and count;
- everything inside the tile;
- no objects on `fairway`, `tee`, `green`, `bunker` or `water`;
- the tee and pin are not in water.

Tests run the same validator over sample packages.
