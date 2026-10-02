import { useMemo, useRef } from 'react';
import type { Player } from '../controls/player';
import { northOf } from '../hole/heightField';
import { yards, type HoleData } from '../hole/loadHole';
import { usePlayerTick } from './usePlayerTick';

const SIZE = 250; // CSS pixels

/** North-up map of the tile (from the same painted surface map as the terrain) with the player on it. */
export function Minimap({ hole, player }: { hole: HoleData; player: Player }) {
  const canvas = useRef<HTMLCanvasElement>(null);
  const readout = useRef<HTMLDivElement>(null);
  const dpr = Math.min(window.devicePixelRatio || 1, 2);

  const base = useMemo(() => {
    const { canvas: src, origin, span } = hole.surfaces;
    const size = hole.field.size;
    const px = src.width / span;
    const c = document.createElement('canvas');
    c.width = c.height = SIZE * dpr;
    const ctx = c.getContext('2d')!;
    ctx.imageSmoothingQuality = 'high';
    // Crop the tile (the canvas's north edge is at the top).
    ctx.drawImage(src, (0 - origin) * px, (origin + span - size) * px, size * px, size * px, 0, 0, c.width, c.height);
    return c;
  }, [hole, dpr]);

  usePlayerTick(() => {
    const ctx = canvas.current?.getContext('2d');
    if (!ctx) return;
    const w = SIZE * dpr, size = hole.field.size, s = w / size;
    const at = (x: number, north: number): [number, number] => [x * s, (size - north) * s];
    ctx.drawImage(base, 0, 0);

    ctx.setLineDash([4 * dpr, 4 * dpr]);
    ctx.strokeStyle = 'rgba(255, 255, 255, 0.75)';
    ctx.lineWidth = 1.2 * dpr;
    ctx.beginPath();
    hole.line.points.forEach(([x, n], i) => (i ? ctx.lineTo(...at(x, n)) : ctx.moveTo(...at(x, n))));
    ctx.stroke();
    ctx.setLineDash([]);

    const dot = (p: [number, number], r: number, fill: string) => {
      ctx.beginPath(); ctx.arc(p[0], p[1], r * dpr, 0, Math.PI * 2);
      ctx.fillStyle = fill; ctx.fill();
      ctx.strokeStyle = '#1b1d14'; ctx.lineWidth = 1.2 * dpr; ctx.stroke();
    };
    dot(at(...hole.tee), 3.5, '#2f6fe0');
    dot(at(...hole.pin), 4, '#e0302a');

    // Player arrow (clamped to the map edge when outside the tile).
    const north = northOf(player.position.z);
    const [px, py] = at(player.position.x, north).map(v => Math.min(Math.max(v, 6 * dpr), w - 6 * dpr));
    ctx.save();
    ctx.translate(px, py);
    ctx.rotate(-player.yaw);
    ctx.beginPath();
    ctx.moveTo(0, -9 * dpr); ctx.lineTo(6 * dpr, 7 * dpr); ctx.lineTo(0, 3.5 * dpr); ctx.lineTo(-6 * dpr, 7 * dpr);
    ctx.closePath();
    ctx.fillStyle = '#ffd23f'; ctx.fill();
    ctx.strokeStyle = '#1b1d14'; ctx.lineWidth = 1.4 * dpr; ctx.stroke();
    ctx.restore();

    if (readout.current) {
      const toPin = Math.hypot(player.position.x - hole.pin[0], north - hole.pin[1]);
      const above = player.position.y - hole.floorAt(player.position.x, north);
      const mode = player.flying ? `Flying ${Math.round(player.flySpeed)} m/s` : 'Walking';
      readout.current.textContent = `${yards(toPin)} yd to pin · ${mode}${player.sprinting ? ' · sprint' : ''}` +
        (player.flying ? ` · ${Math.round(above)} m up` : '');
    }
  }, 24);

  return (
    <div className="panel minimap">
      <canvas ref={canvas} width={SIZE * dpr} height={SIZE * dpr} style={{ width: SIZE, height: SIZE }} />
      <div className="minimap-readout" ref={readout} />
    </div>
  );
}
