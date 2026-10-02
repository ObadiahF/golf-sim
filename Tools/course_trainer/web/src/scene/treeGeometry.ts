import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import type { TreeKind } from '../hole/theme';

// Low-poly tree models, 1 m tall and 1 m wide at the base of the crown, origin at the ground.
// Vertex colours: bark brown, white crown (tinted per instance with the theme's foliage colour).

const BARK = new THREE.Color('#5a4330');
const CROWN = new THREE.Color('#ffffff');

function part(geometry: THREE.BufferGeometry, color: THREE.Color, transform: THREE.Matrix4) {
  const g = geometry.index ? geometry.toNonIndexed() : geometry;
  g.applyMatrix4(transform);
  const colors = new Float32Array(g.attributes.position.count * 3);
  for (let i = 0; i < colors.length; i += 3) colors.set([color.r, color.g, color.b], i);
  g.setAttribute('color', new THREE.BufferAttribute(colors, 3));
  g.deleteAttribute('uv');
  return g;
}

const m = () => new THREE.Matrix4();
const at = (x: number, y: number, z: number, sx = 1, sy = 1, sz = 1) =>
  m().compose(new THREE.Vector3(x, y, z), new THREE.Quaternion(), new THREE.Vector3(sx, sy, sz));

/** Trunk radius / height in units of tree width / height. */
function trunk(height: number, radius: number) {
  return part(new THREE.CylinderGeometry(radius * 0.7, radius, height, 6), BARK, at(0, height / 2, 0));
}

function conifer() {
  const tiers = [0.18, 0.42, 0.64].map((y, i) => {
    const r = 0.5 * (1 - i * 0.24), h = 0.42 - i * 0.04;
    return part(new THREE.ConeGeometry(r, h, 8), CROWN, at(0, y + h / 2, 0));
  });
  return [trunk(0.25, 0.05), ...tiers];
}

function deciduous() {
  const blob = (x: number, y: number, z: number, s: number) =>
    part(new THREE.IcosahedronGeometry(0.5, 1), CROWN, at(x, y, z, s, s * 0.85, s));
  return [trunk(0.45, 0.045), blob(0, 0.62, 0, 0.9), blob(0.18, 0.72, 0.1, 0.6), blob(-0.16, 0.76, -0.12, 0.55)];
}

function palm() {
  const leaves = Array.from({ length: 7 }, (_, i) => {
    const leaf = new THREE.ConeGeometry(0.09, 0.62, 3);
    const rot = new THREE.Quaternion().setFromEuler(new THREE.Euler(0, (i / 7) * Math.PI * 2, Math.PI / 2.3));
    const mat = m().compose(new THREE.Vector3(0, 0.95, 0), rot, new THREE.Vector3(1, 1, 0.35))
      .multiply(at(0, 0.3, 0));
    return part(leaf, CROWN, mat);
  });
  return [trunk(0.98, 0.035), ...leaves];
}

function bush() {
  return [part(new THREE.DodecahedronGeometry(0.5, 0), CROWN, at(0, 0.32, 0, 1, 0.68, 1))];
}

const BUILDERS: Record<TreeKind, () => THREE.BufferGeometry[]> = { conifer, deciduous, palm, bush };

const cache = new Map<TreeKind, THREE.BufferGeometry>();

export function treeGeometry(kind: TreeKind) {
  if (!cache.has(kind)) {
    const g = mergeGeometries(BUILDERS[kind]())!;
    g.computeVertexNormals();
    g.computeBoundingSphere();
    cache.set(kind, g);
  }
  return cache.get(kind)!;
}
