import { useLayoutEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import { worldZ } from '../hole/heightField';
import type { HoleData } from '../hole/loadHole';
import { OBJECT_KINDS, type ObjectKind, type PlacedObject } from '../hole/objects';
import { treeGeometry, WIDTH } from './treeGeometry';

const material = new THREE.MeshStandardMaterial({ vertexColors: true, flatShading: true, roughness: 0.85 });
const STONE = ['#8d877d', '#9a948a', '#7c776f', '#a7a196'];
const CACTUS = ['#5f7f43', '#6a8a4a', '#577540'];
const SINK = 0.15; // metres below the ground, so slopes don't leave gaps under a base

function palette(kind: ObjectKind, foliage: string[]) {
  const colors = kind === 'boulder' || kind === 'rock' ? STONE : kind === 'cactus' ? CACTUS : foliage;
  return colors.map(c => new THREE.Color(c));
}

/** One instanced mesh per kind; every instance placed, scaled and turned exactly as objects.bin says. */
function KindMesh({ kind, items, hole }: { kind: ObjectKind; items: PlacedObject[]; hole: HoleData }) {
  const ref = useRef<THREE.InstancedMesh>(null);
  const colors = useMemo(() => palette(kind, hole.theme.foliage), [kind, hole.theme]);

  useLayoutEffect(() => {
    const mesh = ref.current;
    if (!mesh) return;
    const matrix = new THREE.Matrix4(), q = new THREE.Quaternion(), color = new THREE.Color();
    const up = new THREE.Vector3(0, 1, 0);
    items.forEach((o, i) => {
      const ground = hole.field.heightAt(o.x, o.north);
      const width = o.height * WIDTH[kind];
      q.setFromAxisAngle(up, -o.rotation); // clockwise from north
      matrix.compose(new THREE.Vector3(o.x, ground - SINK, worldZ(o.north)), q,
                     new THREE.Vector3(width, o.height + SINK, width));
      mesh.setMatrixAt(i, matrix);
      color.copy(colors[Math.floor(o.variant * colors.length) % colors.length]);
      color.offsetHSL(0, 0, ((o.variant * 7919) % 1 - 0.5) * 0.08);
      mesh.setColorAt(i, color);
    });
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    mesh.computeBoundingSphere();
  }, [items, hole, colors, kind]);

  if (!items.length) return null;
  return <instancedMesh ref={ref} args={[treeGeometry(kind), material, items.length]} castShadow receiveShadow />;
}

export function Trees({ hole }: { hole: HoleData }) {
  return (
    <group>
      {OBJECT_KINDS.map(kind => (
        <KindMesh key={`${hole.summary.id}-${kind}`} kind={kind} items={hole.objects[kind]} hole={hole} />
      ))}
    </group>
  );
}
