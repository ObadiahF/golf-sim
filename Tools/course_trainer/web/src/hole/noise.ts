/** Small deterministic PRNG (mulberry32): same hole seed -> same trees. */
export function rng(seed: number) {
  let a = seed >>> 0;
  return () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

/** Tileable fractal value noise in 0..1, `size` x `size`. */
export function tileableNoise(size: number, seed: number, octaves = 4, baseCells = 4): Float32Array {
  const rand = rng(seed);
  const out = new Float32Array(size * size);
  let amp = 1, total = 0;
  for (let o = 0, cells = baseCells; o < octaves; o++, cells *= 2, amp *= 0.5) {
    const lattice = Array.from({ length: cells * cells }, rand);
    const at = (i: number, j: number) => lattice[((j + cells) % cells) * cells + ((i + cells) % cells)];
    for (let y = 0; y < size; y++) {
      const fy = (y / size) * cells, j = Math.floor(fy), ty = smooth(fy - j);
      for (let x = 0; x < size; x++) {
        const fx = (x / size) * cells, i = Math.floor(fx), tx = smooth(fx - i);
        const a = at(i, j) + (at(i + 1, j) - at(i, j)) * tx;
        const b = at(i, j + 1) + (at(i + 1, j + 1) - at(i, j + 1)) * tx;
        out[y * size + x] += (a + (b - a) * ty) * amp;
      }
    }
    total += amp;
  }
  for (let i = 0; i < out.length; i++) out[i] /= total;
  return out;
}

const smooth = (t: number) => t * t * (3 - 2 * t);

/** Grey-scale canvas from noise, centred on mid grey so 'overlay' blending keeps the base colour. */
export function noiseCanvas(size: number, seed: number, contrast: number, octaves = 4, baseCells = 4) {
  const noise = tileableNoise(size, seed, octaves, baseCells);
  const canvas = document.createElement('canvas');
  canvas.width = canvas.height = size;
  const ctx = canvas.getContext('2d')!;
  const img = ctx.createImageData(size, size);
  for (let i = 0; i < noise.length; i++) {
    const v = Math.max(0, Math.min(255, 128 + (noise[i] - 0.5) * 255 * contrast));
    img.data[i * 4] = img.data[i * 4 + 1] = img.data[i * 4 + 2] = v;
    img.data[i * 4 + 3] = 255;
  }
  ctx.putImageData(img, 0, 0);
  return canvas;
}
