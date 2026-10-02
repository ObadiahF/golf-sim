import type { Insets } from '../hud/insets';
import { dist, type Vec2 } from './geometry';
import { worldZ } from './heightField';
import type { HoleData } from './loadHole';

export type ViewName = 'start' | 'tee' | 'green' | 'overhead';

/** A camera placement in world space. Yaw 0 looks toward -Z (north); pitch < 0 looks down. */
export interface View { x: number; y: number; z: number; yaw: number; pitch: number; flying: boolean }

export const EYE_HEIGHT = 1.7;
export const FOV = 65;
const OVERHEAD_MARGIN = 35;  // metres of ground shown around the hole line (tee box, green, bunkers)
const MIN_FREE_PX = 120;     // a free area smaller than this (an odd window): frame on the whole screen instead

/** Yaw that faces from package point a toward b. */
export const yawToward = (a: Vec2, b: Vec2) => Math.atan2(-(b[0] - a[0]), b[1] - a[1]);

/** `insets`: the screen the HUD covers (hud/insets measureInsets); Overhead frames the hole in what is left. */
export function viewFor(hole: HoleData, name: ViewName, insets?: Insets): View {
  const { field, line, tee, pin } = hole;
  const ground = (p: Vec2) => field.heightAt(p[0], p[1]);
  const place = (p: Vec2, height: number, target: Vec2, targetHeight: number, flying: boolean): View => {
    const y = ground(p) + height;
    const pitch = -Math.atan2(y - targetHeight, Math.max(dist(p, target), 1));
    return { x: p[0], y, z: worldZ(p[1]), yaw: yawToward(p, target), pitch, flying };
  };

  switch (name) {
    case 'start': { // hovering behind the tee, looking down the first leg
      const [dx, dz] = line.startDir();
      const p: Vec2 = [tee[0] - dx * 30, tee[1] - dz * 30];
      const target = line.at(Math.min(line.length * 0.6, 260));
      return place(p, 32, target, ground(target), true);
    }
    case 'tee': {
      const [dx, dz] = line.startDir();
      const p: Vec2 = [tee[0] - dx * 2, tee[1] - dz * 2];
      const target = line.at(Math.min(line.length, 200));
      return place(p, EYE_HEIGHT, target, ground(target) + EYE_HEIGHT, false);
    }
    case 'green': { // short of the green on the approach, looking at the flag
      const [dx, dz] = line.endDir();
      const p: Vec2 = [pin[0] - dx * 28, pin[1] - dz * 28];
      return place(p, EYE_HEIGHT, pin, ground(pin) + 1, false);
    }
    case 'overhead':
      return overhead(hole, insets);
  }
}

/** Straight down over the whole hole, tee at the bottom, framed inside the part of the screen the HUD leaves free. */
function overhead(hole: HoleData, insets?: Insets): View {
  const { field, line, tee, pin } = hole;
  const width = insets?.width ?? window.innerWidth, height = insets?.height ?? window.innerHeight;
  let { top = 0, right = 0, bottom = 0, left = 0 } = insets ?? {};
  if (width - left - right < MIN_FREE_PX || height - top - bottom < MIN_FREE_PX) top = right = bottom = left = 0;

  // Hole line in screen axes: `up` runs tee -> pin (the top of the screen), `side` to the right of it.
  const len = Math.max(dist(tee, pin), 1);
  const up: Vec2 = [(pin[0] - tee[0]) / len, (pin[1] - tee[1]) / len];
  const side: Vec2 = [up[1], -up[0]];
  const points = [tee, pin, ...line.points];
  const along = points.map(p => (p[0] - tee[0]) * up[0] + (p[1] - tee[1]) * up[1]);
  const across = points.map(p => (p[0] - tee[0]) * side[0] + (p[1] - tee[1]) * side[1]);
  const [u0, u1] = [Math.min(...along) - OVERHEAD_MARGIN, Math.max(...along) + OVERHEAD_MARGIN];
  const [s0, s1] = [Math.min(...across) - OVERHEAD_MARGIN, Math.max(...across) + OVERHEAD_MARGIN];

  // Metres per screen pixel so the hole fits the free area, then the camera height that gives it (FOV is vertical).
  const perPx = Math.max((u1 - u0) / (height - top - bottom), (s1 - s0) / (width - left - right));
  const above = (perPx * height) / 2 / Math.tan(((FOV / 2) * Math.PI) / 180);
  // Aim off-centre so the hole's middle lands in the middle of the free area, not behind a panel.
  const u = (u0 + u1) / 2 + ((top - bottom) / 2) * perPx;
  const s = (s0 + s1) / 2 + ((right - left) / 2) * perPx;
  const centre: Vec2 = [tee[0] + up[0] * u + side[0] * s, tee[1] + up[1] * u + side[1] * s];
  const ground = line.points.reduce((sum, p) => sum + field.heightAt(p[0], p[1]), 0) / line.points.length;
  return { x: centre[0], y: Math.max(ground + above, field.range + 10), z: worldZ(centre[1]), yaw: yawToward(tee, pin),
           pitch: -Math.PI / 2 + 1e-3, flying: true };
}
