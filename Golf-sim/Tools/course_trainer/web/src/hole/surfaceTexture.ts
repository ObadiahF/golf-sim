import type { HoleLine, Polygon } from './geometry';
import { noiseCanvas } from './noise';
import { SURFACE_ORDER, type Surface, type Theme } from './theme';

/** Canvas covering the tile plus its apron; `origin`/`span` map package metres onto it (and onto UVs). */
export interface SurfaceMap {
  canvas: HTMLCanvasElement;
  origin: number; // package metres at the canvas's west / south edge
  span: number;   // metres covered per side
}

export interface SurfaceArea { surface: Surface; poly: Polygon }

const RESOLUTION = 4096;
const STRIPES: Partial<Record<Surface, number>> = { fairway: 9, tee: 2.5, green: 3 }; // mowing band width, m

export function paintSurfaces(areas: SurfaceArea[], size: number, apron: number, theme: Theme, line: HoleLine): SurfaceMap {
  const span = size + 2 * apron;
  const scale = RESOLUTION / span;
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = RESOLUTION;
  const ctx = canvas.getContext('2d')!;
  // Draw in package metres: x east -> right, north -> up.
  const toMetres = () => ctx.setTransform(scale, 0, 0, -scale, apron * scale, (size + apron) * scale);
  toMetres();

  const colors = theme.ground;
  ctx.fillStyle = colors.outside;
  ctx.fillRect(-apron, -apron, span, span);
  grain(ctx, span, apron, 3, 0.35, 160, 0.55);  // broad mottling of the unmown land

  const bySurface = (s: Surface) => areas.filter(a => a.surface === s);
  for (const surface of SURFACE_ORDER) {
    for (const { poly } of bySurface(surface)) {
      tracePolygon(ctx, poly);
      if (surface === 'fairway') outline(ctx, mix(colors.fairway, colors.rough, 0.55), 5);        // first cut
      if (surface === 'green') outline(ctx, mix(colors.green, colors.fairway, 0.7), 3.5);         // collar
      if (surface === 'tee') outline(ctx, mix(colors.tee, colors.rough, 0.5), 1.5);
      ctx.fillStyle = colors[surface];
      ctx.fill('evenodd');
      if (STRIPES[surface]) stripes(ctx, poly, line, STRIPES[surface]!, span);
      if (surface === 'bunker') innerShade(ctx, poly, 'rgba(110, 85, 45, 0.45)', 1.6);
      if (surface === 'water') innerShade(ctx, poly, 'rgba(20, 30, 20, 0.5)', 4);
      if (surface === 'woods') innerShade(ctx, poly, 'rgba(15, 25, 10, 0.35)', 6);
    }
    if (surface === 'woods') grain(ctx, span, apron, 7, 0.25, 40, 0.5); // mottle everything below fairways
  }
  grain(ctx, span, apron, 11, 0.12, 12, 0.35);  // fine grain over all
  ctx.setTransform(1, 0, 0, 1, 0, 0);
  return { canvas, origin: -apron, span };
}

function tracePolygon(ctx: CanvasRenderingContext2D, poly: Polygon) {
  ctx.beginPath();
  for (const ring of poly.rings) {
    ring.forEach(([x, z], i) => (i ? ctx.lineTo(x, z) : ctx.moveTo(x, z)));
    ctx.closePath();
  }
}

function outline(ctx: CanvasRenderingContext2D, color: string, width: number) {
  ctx.strokeStyle = color;
  ctx.lineWidth = width;
  ctx.lineJoin = 'round';
  ctx.stroke();
}

/** Darken just inside the edge (bunker lips, shorelines, woods margins). Expects the polygon path set. */
function innerShade(ctx: CanvasRenderingContext2D, poly: Polygon, color: string, width: number) {
  ctx.save();
  tracePolygon(ctx, poly);
  ctx.clip('evenodd');
  ctx.filter = `blur(${Math.max(1, width * 0.6 * ctx.getTransform().a)}px)`;
  outline(ctx, color, width * 2);
  ctx.restore();
}

/** Alternating light / dark mowing bands across the line of play. */
function stripes(ctx: CanvasRenderingContext2D, poly: Polygon, line: HoleLine, band: number, span: number) {
  const [x0, z0] = line.points[0];
  const [dx, dz] = line.startDir();
  ctx.save();
  tracePolygon(ctx, poly);
  ctx.clip('evenodd');
  ctx.translate(x0, z0);
  ctx.rotate(Math.atan2(dz, dx));
  for (let d = -span, i = 0; d < span; d += band, i++) {
    ctx.fillStyle = i % 2 ? 'rgba(255, 255, 240, 0.075)' : 'rgba(0, 20, 0, 0.06)';
    ctx.fillRect(d, -span, band, 2 * span);
  }
  ctx.restore();
}

/** Overlay tiling noise: `repeat` metres per tile, `alpha` strength. */
function grain(ctx: CanvasRenderingContext2D, span: number, apron: number, seed: number, alpha: number,
               repeat: number, contrast: number) {
  const tile = noiseCanvas(256, seed, contrast * 2);
  const pattern = ctx.createPattern(tile, 'repeat')!;
  pattern.setTransform(new DOMMatrix().scale(repeat / 256));
  ctx.save();
  ctx.globalAlpha = alpha;
  ctx.globalCompositeOperation = 'overlay';
  ctx.fillStyle = pattern;
  ctx.fillRect(-apron, -apron, span, span);
  ctx.restore();
}

/** Blend two #rrggbb colours. */
export function mix(a: string, b: string, t: number) {
  const pa = parseInt(a.slice(1), 16), pb = parseInt(b.slice(1), 16);
  const ch = (shift: number) => Math.round(((pa >> shift) & 255) * (1 - t) + ((pb >> shift) & 255) * t);
  return `rgb(${ch(16)}, ${ch(8)}, ${ch(0)})`;
}
