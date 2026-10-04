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
  autumn: {
    ...PARKLAND,
    ground: { ...PARKLAND.ground, outside: '#8a6a2e', woods: '#7a4a22', scrub: '#9a7a3e', rough: '#6f7a34' },
    foliage: ['#c0632a', '#d9932f', '#a8452a', '#8a9a3a'],
    sky: { turbidity: 8, rayleigh: 1.6, fog: '#e4d2b8', sunElevation: 20 },
  },
  tropical: {
    ...PARKLAND,
    ground: { ...PARKLAND.ground, outside: '#4f7f2c', scrub: '#5e8a34', rough: '#4f8a2e', bunker: '#f4eedc', water: '#2a8a94' },
    foliage: ['#3f8a2e', '#4f9a34', '#2f7a2a', '#6aa83a'],
    sky: { turbidity: 7, rayleigh: 1.8, fog: '#cfe6e6', sunElevation: 45 },
  },
  canyon: {
    ...PARKLAND,
    ground: {
      ...PARKLAND.ground, outside: '#a65a32', rough: '#8f8a4a', scrub: '#b4683a', woods: '#8a5030', bunker: '#e0a070',
    },
    foliage: ['#6b7a3a', '#5c6b34', '#7d8a45'],
    sky: { turbidity: 9, rayleigh: 1.0, fog: '#ead2bc', sunElevation: 35 },
  },
  winter: {
    ...PARKLAND,
    ground: { ...PARKLAND.ground, outside: '#eef2f6', scrub: '#e4eaf0', woods: '#dfe6ec', rough: '#8a9a7a' },
    foliage: ['#26472a', '#2e5430', '#355a2e'],
    sky: { turbidity: 4, rayleigh: 1.2, fog: '#dfe7f0', sunElevation: 22 },
  },
  heathland: {
    ...PARKLAND,
    ground: { ...PARKLAND.ground, outside: '#7a5a6e', scrub: '#8a6478', rough: '#7a8a4e', woods: '#4d5a2c' },
    foliage: ['#4a5a28', '#5d6b2c', '#3e5a2c'],
    sky: { turbidity: 7, rayleigh: 1.8, fog: '#d8dce4', sunElevation: 32 },
  },
};

export const themeFor = (name?: string) => THEMES[name ?? ''] ?? PARKLAND;

/** Any surface name from a package (incl. real OSM holes) mapped onto the palette. */
export function surfaceOf(name: string): Surface {
  if ((SURFACE_ORDER as string[]).includes(name)) return name as Surface;
  if (name === 'native' || name === 'heath') return 'scrub';
  return 'rough';
}
