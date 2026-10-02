import type { HolePackage } from '../api';

/**
 * Package coordinates: x = east, z = north, metres from the tile's SW corner; heightmap row 0 = south edge.
 * World (three.js): X = east, Y = up (metres above the package's minElevation), Z = -north.
 */
export const worldZ = (north: number) => -north;
export const northOf = (worldZ: number) => -worldZ;

/** Heights from heightmap.raw (16-bit LE, normalised to min..maxElevation), bilinear sampled. */
export class HeightField {
  readonly n: number;
  readonly size: number;
  readonly spacing: number;
  readonly range: number;
  /** Metres above minElevation, row-major, row 0 = south. */
  readonly heights: Float32Array;
  /** Average height along the tile border: what the land beyond the tile fades to. */
  readonly edgeHeight: number;
  /** How far beyond the tile the terrain mesh continues; heights reach edgeHeight halfway out. */
  readonly apron: number;

  constructor(raw: ArrayBuffer, pkg: HolePackage) {
    const n = pkg.heightmapResolution;
    if (raw.byteLength !== n * n * 2) throw new Error(`heightmap: expected ${n * n * 2} bytes, got ${raw.byteLength}`);
    this.n = n;
    this.size = pkg.sizeMeters;
    this.spacing = this.size / (n - 1);
    this.range = Math.max(pkg.maxElevation - pkg.minElevation, 0.01);
    const view = new DataView(raw);
    this.heights = new Float32Array(n * n);
    for (let i = 0; i < n * n; i++) this.heights[i] = (view.getUint16(i * 2, true) / 65535) * this.range;

    let sum = 0;
    for (let i = 0; i < n; i++) sum += this.at(i, 0) + this.at(i, n - 1) + this.at(0, i) + this.at(n - 1, i);
    this.edgeHeight = sum / (4 * n);
    this.apron = Math.max(250, this.size * 0.45);
  }

  /** Distance beyond the tile edge where the land is fully flat (at edgeHeight). */
  get flatFrom() { return this.apron * 0.5; }

  private at(col: number, row: number) {
    return this.heights[row * this.n + col];
  }

  /** Height inside the tile (clamped to its edge), bilinear. */
  private tileHeight(x: number, north: number) {
    const max = this.n - 1;
    const fx = Math.min(Math.max(x / this.spacing, 0), max);
    const fz = Math.min(Math.max(north / this.spacing, 0), max);
    const c = Math.min(Math.floor(fx), max - 1), r = Math.min(Math.floor(fz), max - 1);
    const tx = fx - c, tz = fz - r;
    const h00 = this.at(c, r), h10 = this.at(c + 1, r), h01 = this.at(c, r + 1), h11 = this.at(c + 1, r + 1);
    return (h00 * (1 - tx) + h10 * tx) * (1 - tz) + (h01 * (1 - tx) + h11 * tx) * tz;
  }

  /** Terrain height at package (x, north); beyond the tile it eases out to the flat edge height. */
  heightAt(x: number, north: number) {
    const h = this.tileHeight(x, north);
    const dx = Math.max(-x, x - this.size, 0), dz = Math.max(-north, north - this.size, 0);
    const outside = Math.hypot(dx, dz);
    if (outside <= 0) return h;
    const t = Math.min(outside / this.flatFrom, 1);
    const s = t * t * (3 - 2 * t);
    return h + (this.edgeHeight - h) * s;
  }

  /** Same, taking three.js world X / Z. */
  heightAtWorld(wx: number, wz: number) {
    return this.heightAt(wx, northOf(wz));
  }
}
