import { useLayoutEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import { worldZ } from '../hole/heightField';
import type { HoleData } from '../hole/loadHole';
import type { TreeKind } from '../hole/theme';
import type { TreeInstance } from '../hole/treeScatter';
import { treeGeometry } from './treeGeometry';

const material = new THREE.MeshStandardMaterial({ vertexColors: true, flatShading: true, roughness: 0.85 });

function TreeKindMesh({ kind, trees, hole }: { kind: TreeKind; trees: TreeInstance[]; hole: HoleData }) {
  const ref = useRef<THREE.InstancedMesh>(null);
  const palette = useMemo(() => hole.theme.foliage.map(c => new THREE.Color(c)), [hole.theme]);

  useLayoutEffect(() => {
    const mesh = ref.current;
    if (!mesh) return;
    const matrix = new THREE.Matrix4(), q = new THREE.Quaternion(), color = new THREE.Color();
    const up = new THREE.Vector3(0, 1, 0);
    trees.forEach((t, i) => {
      const ground = hole.field.heightAt(t.x, t.north);
      q.setFromAxisAngle(up, t.rotation);
      matrix.compose(new THREE.Vector3(t.x, ground - 0.15, worldZ(t.north)), q,
                     new THREE.Vector3(t.width, t.height, t.width));
      mesh.setMatrixAt(i, matrix);
      color.copy(palette[Math.floor(t.tint * palette.length) % palette.length]);
      color.offsetHSL(0, 0, (t.tint * 7919 % 1 - 0.5) * 0.08);
      mesh.setColorAt(i, color);
    });
    mesh.instanceMatrix.needsUpdate = true;
    if (mesh.instanceColor) mesh.instanceColor.needsUpdate = true;
    mesh.computeBoundingSphere();
  }, [trees, hole, palette]);

  if (!trees.length) return null;
  return <instancedMesh ref={ref} args={[treeGeometry(kind), material, trees.length]} castShadow receiveShadow />;
}

export function Trees({ hole }: { hole: HoleData }) {
  return (
    <group>
      {(Object.keys(hole.forest) as TreeKind[]).map(kind => (
        <TreeKindMesh key={`${hole.summary.id}-${kind}`} kind={kind} trees={hole.forest[kind]} hole={hole} />
      ))}
    </group>
  );
}
