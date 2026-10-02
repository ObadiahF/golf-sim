import { useFrame } from '@react-three/fiber';
import { useEffect, useMemo } from 'react';
import * as THREE from 'three';
import { worldZ } from '../hole/heightField';
import type { HoleData } from '../hole/loadHole';
import { tileableNoise } from '../hole/noise';

const RIPPLE_METRES = 9;

/** Tileable ripple normal map derived from fractal noise (no texture files needed). */
function rippleNormals(size = 256) {
  const h = tileableNoise(size, 4242, 4, 6);
  const data = new Uint8Array(size * size * 4);
  const at = (x: number, y: number) => h[((y + size) % size) * size + ((x + size) % size)];
  for (let y = 0; y < size; y++) {
    for (let x = 0; x < size; x++) {
      const dx = (at(x + 1, y) - at(x - 1, y)) * 6, dy = (at(x, y + 1) - at(x, y - 1)) * 6;
      const len = Math.hypot(dx, dy, 1);
      const i = (y * size + x) * 4;
      data.set([(-dx / len * 0.5 + 0.5) * 255, (-dy / len * 0.5 + 0.5) * 255, (1 / len * 0.5 + 0.5) * 255, 255], i);
    }
  }
  const tex = new THREE.DataTexture(data, size, size);
  tex.wrapS = tex.wrapT = THREE.RepeatWrapping;
  tex.generateMipmaps = true;
  tex.minFilter = THREE.LinearMipmapLinearFilter;
  tex.needsUpdate = true;
  return tex;
}

/** Flat water surfaces at each body's level, reflecting the sky environment, with drifting ripples. */
export function Water({ hole }: { hole: HoleData }) {
  const { pkg } = hole;

  const geometry = useMemo(() => {
    const pos: number[] = [], uv: number[] = [];
    for (const body of pkg.water) {
      const y = body.level - pkg.heightmap.minElevation + 0.03;
      const p = body.triangles.points;
      for (let t = 0; t + 5 < p.length; t += 6) {
        // Positive (x, north) cross product = front face up in world space (Z = -north); flip the rest.
        const up = (p[t + 2] - p[t]) * (p[t + 5] - p[t + 1]) - (p[t + 4] - p[t]) * (p[t + 3] - p[t + 1]) > 0;
        for (const i of up ? [t, t + 2, t + 4] : [t, t + 4, t + 2]) {
          pos.push(p[i], y, worldZ(p[i + 1]));
          uv.push(p[i] / RIPPLE_METRES, p[i + 1] / RIPPLE_METRES);
        }
      }
    }
    if (!pos.length) return null;
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.Float32BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.Float32BufferAttribute(uv, 2));
    g.setAttribute('normal', new THREE.Float32BufferAttribute(pos.map((_, i) => (i % 3 === 1 ? 1 : 0)), 3));
    return g;
  }, [pkg]);

  const material = useMemo(() => {
    const normalMap = rippleNormals();
    return new THREE.MeshStandardMaterial({
      color: '#163a46', roughness: 0.06, metalness: 0, envMapIntensity: 1.1,
      normalMap, normalScale: new THREE.Vector2(0.35, 0.35),
      transparent: true, opacity: 0.92,
    });
  }, []);

  useFrame((_, dt) => {
    const map = material.normalMap!;
    map.offset.x = (map.offset.x + dt * 0.015) % 1;
    map.offset.y = (map.offset.y + dt * 0.009) % 1;
  });

  useEffect(() => () => { geometry?.dispose(); }, [geometry]);
  useEffect(() => () => { material.normalMap?.dispose(); material.dispose(); }, [material]);

  return geometry ? <mesh geometry={geometry} material={material} receiveShadow /> : null;
}
