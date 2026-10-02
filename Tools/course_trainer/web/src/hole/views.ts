import { boundsOf, dist, type Vec2 } from './geometry';
import { worldZ } from './heightField';
import type { HoleData } from './loadHole';

export type ViewName = 'start' | 'tee' | 'green' | 'overhead';

/** A camera placement in world space. Yaw 0 looks toward -Z (north); pitch < 0 looks down. */
export interface View { x: number; y: number; z: number; yaw: number; pitch: number; flying: boolean }

export const EYE_HEIGHT = 1.7;
export const FOV = 65;

/** Yaw that faces from package point a toward b. */
export const yawToward = (a: Vec2, b: Vec2) => Math.atan2(-(b[0] - a[0]), b[1] - a[1]);

export function viewFor(hole: HoleData, name: ViewName): View {
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
    case 'overhead': { // straight down over the whole hole, tee at the bottom of the screen
      const b = boundsOf(line.points);
      const centre: Vec2 = [(b.minX + b.maxX) / 2, (b.minZ + b.maxZ) / 2];
      const extent = Math.max(dist(tee, pin), b.maxX - b.minX, b.maxZ - b.minZ) * 1.25 + 60;
      const height = extent / 2 / Math.tan(((FOV / 2) * Math.PI) / 180);
      return { x: centre[0], y: field.range + height, z: worldZ(centre[1]), yaw: yawToward(tee, pin),
               pitch: -Math.PI / 2 + 1e-3, flying: true };
    }
  }
}
