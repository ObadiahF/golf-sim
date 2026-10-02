import { api, type HolePackage, type HoleSummary } from '../api';
import { HoleLine, Polygon, toVec2s, type Vec2 } from './geometry';
import { HeightField } from './heightField';
import { paintSurfaces, type SurfaceArea, type SurfaceMap } from './surfaceTexture';
import { surfaceOf, themeFor, type Theme } from './theme';
import { decodeObjects, type ObjectsByKind } from './objects';

/** Everything the scene, controls and HUD need for one hole, decoded once. */
export interface HoleData {
  summary: HoleSummary;
  pkg: HolePackage;
  field: HeightField;
  theme: Theme;
  line: HoleLine;
  tee: Vec2;
  pin: Vec2;
  areas: SurfaceArea[];
  surfaces: SurfaceMap;
  /** Every tree, shrub and rock, exactly as the package places them. */
  objects: ObjectsByKind;
  /** Walkable surface at package (x, north): terrain, or the water surface over a pond. */
  floorAt: (x: number, north: number) => number;
}

export async function loadHole(summary: HoleSummary, peek = false): Promise<HoleData> {
  const pkg = await api.packageJson(summary.id, peek);
  if (pkg.version !== 2) throw new Error(`hole.json version ${pkg.version}; this viewer reads version 2`);
  const [raw, objectsBin] = await Promise.all([api.binary(summary.id, pkg.heightmap.file),
                                               api.binary(summary.id, pkg.objects.file)]);
  const field = new HeightField(raw, pkg);
  const theme = themeFor(pkg.theme ?? summary.theme);
  const line = new HoleLine(toVec2s(pkg.holePath));
  const areas = pkg.areas
    .map(a => ({ surface: surfaceOf(a.surface), poly: new Polygon(a) }))
    .filter(a => a.poly.rings.length > 0);
  const ponds = waterSurfaces(pkg, areas);
  const floorAt = (x: number, north: number) => {
    const ground = field.heightAt(x, north);
    const pond = ponds.find(w => w.poly.contains(x, north));
    return pond ? Math.max(ground, pond.level) : ground;
  };
  return {
    summary, pkg, field, theme, line, areas, floorAt,
    tee: [pkg.tee.x, pkg.tee.y],
    pin: [pkg.pin.x, pkg.pin.y],
    surfaces: paintSurfaces(areas, field.size, field.apron, theme, line),
    objects: decodeObjects(objectsBin, pkg.sizeMeters, pkg.objects.count),
  };
}

/** Pair each water polygon with the level of the water body whose triangles lie inside it. */
function waterSurfaces(pkg: HolePackage, areas: SurfaceArea[]) {
  const polys = areas.filter(a => a.surface === 'water').map(a => a.poly);
  return pkg.water.flatMap(body => {
    const [x0, z0, x1, z1, x2, z2] = body.triangles.points;
    const poly = polys.find(p => p.contains((x0 + x1 + x2) / 3, (z0 + z1 + z2) / 3));
    return poly ? [{ poly, level: body.level - pkg.heightmap.minElevation }] : [];
  });
}

export const yards = (metres: number) => Math.round(metres * 1.09361);
