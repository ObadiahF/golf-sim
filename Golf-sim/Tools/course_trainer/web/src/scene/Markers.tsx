import { Html } from '@react-three/drei';
import { useFrame } from '@react-three/fiber';
import { useMemo, useRef, useState } from 'react';
import * as THREE from 'three';
import { worldZ } from '../hole/heightField';
import { yards, type HoleData } from '../hole/loadHole';

const POLE_HEIGHT = 2.6;
const FLAG_W = 0.95, FLAG_H = 0.62;
const CUP_RADIUS = 0.054;
const LABEL_HIDE_M = 45;  // the label hides this close to the pin
const AT_TEE_M = 40;      // within this of the tee (the start and Tee views) the label shows the hole's yardage only

/** The flag label: the hole's yardage from the tee (as on the hole card), plus "to pin" once you have moved away. */
function pinLabel(hole: HoleData, camera: THREE.Vector3): string | null {
  const flat = (p: [number, number]) => Math.hypot(camera.x - p[0], camera.z - worldZ(p[1]));
  const toPin = flat(hole.pin);
  if (toPin <= LABEL_HIDE_M) return null;
  const length = `${yards(hole.summary.lengthMeters)} yd`;
  return flat(hole.tee) <= AT_TEE_M ? length : `${length} · ${yards(toPin)} yd to pin`;
}

/** Flagstick, waving flag, cup, and a floating distance marker (pinLabel; hidden once you are close). */
export function Pin({ hole }: { hole: HoleData }) {
  const [x, north] = hole.pin;
  const ground = hole.field.heightAt(x, north);
  const flag = useMemo(() => new THREE.PlaneGeometry(FLAG_W, FLAG_H, 12, 4).translate(FLAG_W / 2, 0, 0), []);
  const rest = useMemo(() => Float32Array.from(flag.attributes.position.array), [flag]);
  const [label, setLabel] = useState<string | null>(null);
  const lastLabel = useRef<string | null>(null);

  useFrame(({ clock, camera }) => {
    const t = clock.elapsedTime;
    const pos = flag.attributes.position as THREE.BufferAttribute;
    for (let i = 0; i < pos.count; i++) {
      const fx = rest[i * 3], fy = rest[i * 3 + 1];
      const k = fx / FLAG_W;
      pos.setZ(i, Math.sin(fx * 5 - t * 6 + fy * 1.5) * 0.09 * k);
      pos.setY(i, fy - k * k * 0.06);
    }
    pos.needsUpdate = true;
    flag.computeVertexNormals();

    const text = pinLabel(hole, camera.position);
    if (text !== lastLabel.current) { lastLabel.current = text; setLabel(text); }
  });

  return (
    <group position={[x, ground, worldZ(north)]}>
      <mesh position-y={0.004} rotation-x={-Math.PI / 2}>
        <circleGeometry args={[CUP_RADIUS + 0.012, 24]} />
        <meshBasicMaterial color="#f4f4ee" />
      </mesh>
      <mesh position-y={0.006} rotation-x={-Math.PI / 2}>
        <circleGeometry args={[CUP_RADIUS, 24]} />
        <meshBasicMaterial color="#0b0d08" />
      </mesh>
      <mesh position-y={POLE_HEIGHT / 2} castShadow>
        <cylinderGeometry args={[0.022, 0.022, POLE_HEIGHT, 8]} />
        <meshStandardMaterial color="#fbfbf3" roughness={0.4} />
      </mesh>
      <mesh geometry={flag} position-y={POLE_HEIGHT - FLAG_H / 2 - 0.03} rotation-y={0.6} castShadow>
        <meshStandardMaterial color="#e0302a" side={THREE.DoubleSide} roughness={0.7} emissive="#5a0d08" />
      </mesh>
      {label && (
        <Html position={[0, POLE_HEIGHT + 2.5, 0]} center zIndexRange={[5, 0]} style={{ pointerEvents: 'none' }}>
          <div className="pin-label"><span>⛳</span>{label}</div>
        </Html>
      )}
    </group>
  );
}

/** A pair of tee markers either side of the back tee, square to the line of play. */
export function TeeMarkers({ hole }: { hole: HoleData }) {
  const [tx, tn] = hole.tee;
  const [dx, dn] = hole.line.startDir();
  const markers = [-1, 1].map(side => {
    const x = tx + -dn * side * 3, n = tn + dx * side * 3;
    return [x, hole.field.heightAt(x, n) + 0.14, worldZ(n)] as [number, number, number];
  });
  return (
    <group>
      {markers.map((p, i) => (
        <mesh key={i} position={p} castShadow>
          <sphereGeometry args={[0.16, 16, 12]} />
          <meshStandardMaterial color="#1f5fd1" roughness={0.35} />
        </mesh>
      ))}
    </group>
  );
}
