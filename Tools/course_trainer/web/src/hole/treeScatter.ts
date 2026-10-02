import type { XZ } from '../api';
import type { Polygon } from './geometry';
import { rng } from './noise';
import type { SurfaceArea } from './surfaceTexture';
import type { Theme, TreeKind } from './theme';

export interface TreeInstance { x: number; north: number; height: number; width: number; rotation: number; tint: number }
export type Forest = Record<TreeKind, TreeInstance[]>;

const MAX_TREES = 9000;
/** Typical height (m) and crown width / height ratio per kind. */
const SHAPES: Record<TreeKind, { height: [number, number]; width: [number, number] }> = {
  conifer: { height: [12, 24], width: [0.32, 0.42] },
  deciduous: { height: [9, 17], width: [0.65, 0.95] },
  palm: { height: [8, 14], width: [0.45, 0.6] },
  bush: { height: [1.2, 2.6], width: [1.1, 1.7] },
};

/**
 * Plants the specimen trees from hole.json plus generated woods and scrub vegetation.
 * Woods / scrub areas carry no individual trees in the package (Unity spawns them from the theme),
 * so we fill them with a jittered grid. Deterministic per hole seed.
 */
export function plantForest(areas: SurfaceArea[], specimens: XZ[], theme: Theme, seed: number): Forest {
  const random = rng(seed * 7919 + 17);
  const forest: Forest = { conifer: [], deciduous: [], palm: [], bush: [] };
  const kinds = Object.entries(theme.woods) as [TreeKind, number][];
  const total = kinds.reduce((s, [, w]) => s + w, 0);
  const pickKind = (): TreeKind => {
    let r = random() * total;
    for (const [kind, w] of kinds) if ((r -= w) <= 0) return kind;
    return kinds[0][0];
  };
  const plant = (kind: TreeKind, x: number, north: number, sizeBoost = 1) => {
    const s = SHAPES[kind];
    const height = lerp(s.height, random()) * sizeBoost;
    forest[kind].push({ x, north, height, width: height * lerp(s.width, random()),
                        rotation: random() * Math.PI * 2, tint: random() });
  };

  for (const t of specimens) {
    const kind = pickKind();
    plant(kind === 'bush' ? 'deciduous' : kind, t.x, t.y, 1.1); // a specimen tree is never a shrub
  }
  const budget = () => MAX_TREES - Object.values(forest).reduce((s, a) => s + a.length, 0);

  for (const { surface, poly } of areas) {
    if (surface === 'woods') scatter(poly, theme.woodsSpacing, random, budget, (x, z) => plant(pickKind(), x, z));
    if (surface === 'scrub') scatter(poly, theme.scrubSpacing, random, budget, (x, z) => plant('bush', x, z, 0.8));
  }
  return forest;
}

function scatter(poly: Polygon, spacing: number, random: () => number, budget: () => number,
                 place: (x: number, z: number) => void) {
  const b = poly.bounds;
  for (let z = b.minZ; z < b.maxZ; z += spacing) {
    for (let x = b.minX; x < b.maxX; x += spacing) {
      if (budget() <= 0) return;
      const px = x + random() * spacing * 0.9, pz = z + random() * spacing * 0.9;
      if (random() < 0.85 && poly.contains(px, pz)) place(px, pz);
    }
  }
}

const lerp = ([a, b]: [number, number], t: number) => a + (b - a) * t;
