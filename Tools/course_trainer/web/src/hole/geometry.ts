import type { HoleArea, PointList } from '../api';

export type Vec2 = [number, number]; // (x = east, z = north) in package metres

export const toVec2s = (list: PointList): Vec2[] => {
  const out: Vec2[] = [];
  for (let i = 0; i + 1 < list.points.length; i += 2) out.push([list.points[i], list.points[i + 1]]);
  return out;
};

export interface Bounds { minX: number; minZ: number; maxX: number; maxZ: number }

export function boundsOf(points: Vec2[]): Bounds {
  const b = { minX: Infinity, minZ: Infinity, maxX: -Infinity, maxZ: -Infinity };
  for (const [x, z] of points) {
    b.minX = Math.min(b.minX, x); b.maxX = Math.max(b.maxX, x);
    b.minZ = Math.min(b.minZ, z); b.maxZ = Math.max(b.maxZ, z);
  }
  return b;
}

/** A surface polygon: outer ring + holes, with a bounding box for cheap rejection. */
export class Polygon {
  readonly rings: Vec2[][];
  readonly bounds: Bounds;
  readonly area: number;

  constructor(area: HoleArea) {
    this.rings = area.rings.map(toVec2s).filter(r => r.length >= 3);
    this.bounds = boundsOf(this.rings[0] ?? []);
    this.area = this.rings.reduce((sum, r, i) => sum + (i === 0 ? 1 : -1) * Math.abs(ringArea(r)), 0);
  }

  contains(x: number, z: number): boolean {
    const b = this.bounds;
    if (x < b.minX || x > b.maxX || z < b.minZ || z > b.maxZ) return false;
    let inside = false; // even-odd over all rings handles holes
    for (const ring of this.rings) {
      for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) {
        const [xi, zi] = ring[i], [xj, zj] = ring[j];
        if ((zi > z) !== (zj > z) && x < ((xj - xi) * (z - zi)) / (zj - zi) + xi) inside = !inside;
      }
    }
    return inside;
  }
}

function ringArea(ring: Vec2[]) {
  let a = 0;
  for (let i = 0, j = ring.length - 1; i < ring.length; j = i++) a += (ring[j][0] + ring[i][0]) * (ring[j][1] - ring[i][1]);
  return a / 2;
}

/** The hole's centre line (tee -> green) with distance helpers. */
export class HoleLine {
  readonly points: Vec2[];
  readonly cumulative: number[];

  constructor(points: Vec2[]) {
    this.points = points.length >= 2 ? points : [points[0] ?? [0, 0], points[0] ?? [0, 0]];
    this.cumulative = [0];
    for (let i = 1; i < this.points.length; i++) {
      this.cumulative.push(this.cumulative[i - 1] + dist(this.points[i - 1], this.points[i]));
    }
  }

  get length() { return this.cumulative[this.cumulative.length - 1]; }

  /** Point `d` metres along the line (clamped to its ends). */
  at(d: number): Vec2 {
    const c = this.cumulative;
    if (d <= 0) return this.points[0];
    for (let i = 1; i < c.length; i++) {
      if (d <= c[i]) {
        const t = (d - c[i - 1]) / Math.max(c[i] - c[i - 1], 1e-6);
        const [a, b] = [this.points[i - 1], this.points[i]];
        return [a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t];
      }
    }
    return this.points[this.points.length - 1];
  }

  /** Unit direction of travel at the start / end of the line. */
  startDir(): Vec2 { return unit(this.points[0], this.at(Math.min(30, this.length))); }
  endDir(): Vec2 { return unit(this.at(Math.max(0, this.length - 30)), this.points[this.points.length - 1]); }
}

export const dist = (a: Vec2, b: Vec2) => Math.hypot(b[0] - a[0], b[1] - a[1]);

export function unit(a: Vec2, b: Vec2): Vec2 {
  const d = dist(a, b) || 1;
  return [(b[0] - a[0]) / d, (b[1] - a[1]) / d];
}
