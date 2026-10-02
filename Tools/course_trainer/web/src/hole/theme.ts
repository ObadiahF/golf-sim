// Look of each Unity theme in the browser: ground colours per surface and foliage tints. What grows where
// comes from the hole package (objects.bin), never from here.

export type Surface = 'outside' | 'rough' | 'scrub' | 'woods' | 'fairway' | 'tee' | 'green' | 'bunker' | 'water';

/** Paint order (later wins), matching the package's own area order. */
export const SURFACE_ORDER: Surface[] = ['rough', 'scrub', 'woods', 'fairway', 'tee', 'green', 'bunker', 'water'];

export interface Theme {
  ground: Record<Surface, string>;
  foliage: string[];
  sky: { turbidity: number; rayleigh: number; fog: string; sunElevation: number };
}

const PARKLAND: Theme = {
  ground: {
    outside: '#4b6a2a', rough: '#4f772c', scrub: '#7d7d45', woods: '#3a4a23',
    fairway: '#5f9f35', tee: '#6cad3f', green: '#86d05a', bunker: '#e8d8a8', water: '#2c463a',
  },
  foliage: ['#3d6b2a', '#4a7a2e', '#33602a', '#5b8336'],
  sky: { turbidity: 6, rayleigh: 1.4, fog: '#c9d9e4', sunElevation: 38 },
};

export const THEMES: Record<string, Theme> = {
  parkland: PARKLAND,
  lakes: PARKLAND,
  coastal: PARKLAND,
  forest: {
    ...PARKLAND,
    ground: { ...PARKLAND.ground, outside: '#46602a', woods: '#35401f', rough: '#557a30' },
    foliage: ['#2b5226', '#335c2a', '#28472a', '#3e6a2c'],
  },
  mountain: {
    ...PARKLAND,
    ground: { ...PARKLAND.ground, outside: '#5b6b3a', woods: '#3a4224', scrub: '#857d55' },
    foliage: ['#26472a', '#2e5430', '#355a2e'],
    sky: { turbidity: 3, rayleigh: 1.1, fog: '#d3e0ea', sunElevation: 42 },
  },
  links: {
    ...PARKLAND,
    ground: {
      ...PARKLAND.ground, outside: '#9a9a5e', rough: '#8a9c4e', scrub: '#a69d62', woods: '#4d5a2c',
      fairway: '#76a840', tee: '#80b247', green: '#94d35e', bunker: '#dccb98',
    },
    foliage: ['#5d6b2c', '#6f7a34', '#4a5a28', '#8c7f2e'],
    sky: { turbidity: 8, rayleigh: 2.2, fog: '#d6dfe2', sunElevation: 30 },
  },
  desert: {
    ...PARKLAND,
    ground: {
      ...PARKLAND.ground, outside: '#c2a477', rough: '#a59a5c', scrub: '#b89a6a', woods: '#a08a5f',
      fairway: '#6aa53a', tee: '#78ae44', green: '#8fd25c', bunker: '#ecdcae',
    },
    foliage: ['#6b7a3a', '#7d8a45', '#5c6b34'],
    sky: { turbidity: 10, rayleigh: 1.0, fog: '#e6dccb', sunElevation: 50 },
  },
};

export const themeFor = (name?: string) => THEMES[name ?? ''] ?? PARKLAND;

/** Any surface name from a package (incl. real OSM holes) mapped onto the palette. */
export function surfaceOf(name: string): Surface {
  if ((SURFACE_ORDER as string[]).includes(name)) return name as Surface;
  if (name === 'native' || name === 'heath') return 'scrub';
  return 'rough';
}
