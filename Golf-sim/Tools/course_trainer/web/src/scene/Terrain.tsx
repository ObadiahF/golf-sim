import { useThree } from '@react-three/fiber';
import { useEffect, useMemo } from 'react';
import * as THREE from 'three';
import type { HoleData } from '../hole/loadHole';
import { worldZ } from '../hole/heightField';
import { noiseCanvas } from '../hole/noise';

const SEGMENTS = 400;          // mesh quads per side (tile + apron); the heightmap itself is up to 1024
const DETAIL_METRES = 2.5;     // tiling grass detail so close-ups are not just blurry texels

/** Heightmap mesh (decimated) wearing the painted surface map, plus a far ground plane to the horizon. */
export function Terrain({ hole }: { hole: HoleData }) {
  const { gl } = useThree();
  const { field, surfaces } = hole;

  const geometry = useMemo(() => {
    const { origin, span } = surfaces;
    const verts = SEGMENTS + 1;
    const pos = new Float32Array(verts * verts * 3);
    const uv = new Float32Array(verts * verts * 2);
    for (let r = 0, i = 0; r < verts; r++) {
      const north = origin + (r / SEGMENTS) * span;
      for (let c = 0; c < verts; c++, i++) {
        const x = origin + (c / SEGMENTS) * span;
        pos.set([x, field.heightAt(x, north), worldZ(north)], i * 3);
        uv.set([c / SEGMENTS, r / SEGMENTS], i * 2);
      }
    }
    const index = new Uint32Array(SEGMENTS * SEGMENTS * 6);
    for (let r = 0, k = 0; r < SEGMENTS; r++) {
      for (let c = 0; c < SEGMENTS; c++) {
        const a = r * verts + c, b = a + 1, d = a + verts, e = d + 1;
        index.set([a, b, d, b, e, d], k);
        k += 6;
      }
    }
    const g = new THREE.BufferGeometry();
    g.setAttribute('position', new THREE.BufferAttribute(pos, 3));
    g.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
    g.setIndex(new THREE.BufferAttribute(index, 1));
    g.computeVertexNormals();
    g.computeBoundingSphere();
    return g;
  }, [field, surfaces]);

  // The surface map and the detail noise are per hole: both are disposed with the material on hole change.
  const material = useMemo(() => {
    const map = new THREE.CanvasTexture(surfaces.canvas);
    map.colorSpace = THREE.SRGBColorSpace;
    map.anisotropy = gl.capabilities.getMaxAnisotropy();
    const detail = new THREE.CanvasTexture(noiseCanvas(128, 99, 1.6, 3, 8));
    detail.wrapS = detail.wrapT = THREE.RepeatWrapping;
    const m = new THREE.MeshStandardMaterial({ map, roughness: 0.95, metalness: 0 });
    m.userData.detail = detail;
    m.onBeforeCompile = shader => {
      shader.uniforms.detailMap = { value: detail };
      shader.uniforms.detailRepeat = { value: surfaces.span / DETAIL_METRES };
      shader.fragmentShader = shader.fragmentShader
        .replace('#include <common>', '#include <common>\nuniform sampler2D detailMap;\nuniform float detailRepeat;')
        .replace('#include <map_fragment>', `#include <map_fragment>
          float fine = texture2D(detailMap, vMapUv * detailRepeat).r;
          float broad = texture2D(detailMap, vMapUv * detailRepeat * 0.13).r;
          diffuseColor.rgb *= mix(0.86, 1.14, fine) * mix(0.93, 1.07, broad);`);
    };
    return m;
  }, [surfaces, gl]);

  useEffect(() => () => { geometry.dispose(); }, [geometry]);
  useEffect(() => () => {
    material.map?.dispose();
    (material.userData.detail as THREE.Texture).dispose();
    material.dispose();
  }, [material]);

  const centre = field.size / 2;
  // Ring starts where every direction is already flat, so it never pokes through the hole's valleys.
  const innerRadius = field.size * Math.SQRT1_2 + field.flatFrom + 1;
  return (
    <group>
      <mesh geometry={geometry} material={material} receiveShadow />
      {/* Beyond the apron: flat land at the edge height, lost in the haze. */}
      <mesh rotation-x={-Math.PI / 2} position={[centre, field.edgeHeight - 0.05, worldZ(centre)]} receiveShadow>
        <ringGeometry args={[innerRadius, 9000, 64, 1]} />
        <meshStandardMaterial color={hole.theme.ground.outside} roughness={1} />
      </mesh>
    </group>
  );
}
