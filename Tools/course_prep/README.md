# course_prep

Turns real-world data into Unity hole packages that **Golf > Course Builder** generates into a scene.

- **Layout:** OpenStreetMap `golf=*` features (Overpass API, or a GeoJSON export from overpass-turbo.eu)
- **Elevation:** USGS 3DEP 1 m DEM. Tiles are found automatically and only the hole's footprint is
  streamed, or pass local GeoTIFFs from apps.nationalmap.gov with `--dem`.

## Setup (once)

```sh
cd Tools/course_prep
python3 -m venv .venv && .venv/bin/pip install -r requirements.txt
```

## Make a hole

```sh
# 1. OSM features for a box around the course (south,west,north,east)
.venv/bin/python prep_hole.py fetch --bbox 32.888,-117.258,32.912,-117.238 --out ../../CourseSources/torrey_pines/osm.json

# 2. See which holes are mapped
.venv/bin/python prep_hole.py list --osm ../../CourseSources/torrey_pines/osm.json

# 3. Build a package (writes Assets/CourseData/<course>/hole_NN/)
.venv/bin/python prep_hole.py build --osm ../../CourseSources/torrey_pines/osm.json --hole 3 --course South
```

Then in Unity: **Golf > Course Builder**, pick the package, and click **Generate Hole**.

Check `preview.png` in the package folder: it shows shaded relief with the OSM outlines on top, so
you can confirm the elevation and layout line up. Open the files in QGIS if you want a closer look.

## Package format (`hole.json`)

Local meters on a north-up square: `x` = east, `y`/`z` = north, origin at the south-west corner.
`heightmap.raw` is 16-bit little-endian, row 0 = south, normalised to `minElevation..maxElevation`.
Each `areas[]` entry has a `surface` name (rough, fairway, green, tee, bunker, water, woods) and
rings (outer ring first, then holes). Surface names must match the entries in Unity's `SurfaceLayerSet`.

## Options

| flag | default | meaning |
|---|---|---|
| `--margin` | 50 | meters of terrain kept around the hole line |
| `--max-spacing` | 0.5 | max meters per heightmap sample (picks 513/1025/2049/4097) |
| `--dem` | auto | local GeoTIFF paths or URLs instead of auto-finding USGS tiles |
| `--course` | | course-name substring when several courses share hole numbers |
