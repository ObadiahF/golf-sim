import * as THREE from 'three';
import { mergeGeometries } from 'three/examples/jsm/utils/BufferGeometryUtils.js';
import type { ObjectKind } from '../hole/objects';

// Low-poly models for every object kind, 1 m tall, origin at the ground; WIDTH is each model's footprint
// relative to its height. Vertex colours: bark brown, white crown / stone (tinted per instance).

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

function shrub() {
  return [part(new THREE.DodecahedronGeometry(0.5, 0), CROWN, at(0, 0.5, 0, 1, 1, 1))];
}

function cactus() {
  const arm = (x: number, y: number, h: number) => [
    part(new THREE.CylinderGeometry(0.06, 0.06, 0.18, 6), CROWN, at(x / 2, y, 0, 1, 1, 1).multiply(
      m().makeRotationZ(Math.PI / 2))),
    part(new THREE.CylinderGeometry(0.06, 0.06, h, 6), CROWN, at(x, y + h / 2, 0)),
  ];
  return [part(new THREE.CylinderGeometry(0.1, 0.11, 1, 7), CROWN, at(0, 0.5, 0)),
          ...arm(0.18, 0.4, 0.3), ...arm(-0.18, 0.55, 0.25)];
}

/** Rocks: a squashed, lumpy icosahedron so each instance's rotation makes it look different. */
function stone(flatten: number) {
  const g = new THREE.IcosahedronGeometry(0.5, 0);
  const pos = g.attributes.position;
  for (let i = 0; i < pos.count; i++) {
    const k = 0.82 + ((Math.sin(i * 12.9898) * 43758.5453) % 1 + 1) % 1 * 0.3;
    pos.setXYZ(i, pos.getX(i) * k, pos.getY(i) * k * flatten, pos.getZ(i) * k);
  }
  return [part(g, CROWN, at(0, 0.5 * flatten * 0.9, 0, 1, 1 / flatten, 1))];
}

const BUILDERS: Record<ObjectKind, () => THREE.BufferGeometry[]> = {
  conifer, deciduous, palm, cactus, shrub, boulder: () => stone(0.8), rock: () => stone(0.6),
};

/** Footprint width / height per model, so a scaled instance keeps the model's proportions. */
export const WIDTH: Record<ObjectKind, number> = {
  conifer: 0.36, deciduous: 0.75, palm: 0.5, cactus: 0.45, shrub: 1.3, boulder: 1.3, rock: 1.5,
};

const cache = new Map<ObjectKind, THREE.BufferGeometry>();

export function treeGeometry(kind: ObjectKind) {
  if (!cache.has(kind)) {
    const g = mergeGeometries(BUILDERS[kind]())!;
    g.computeVertexNormals();
    g.computeBoundingSphere();
    cache.set(kind, g);
  }
  return cache.get(kind)!;
}
