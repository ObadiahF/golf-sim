import { Environment, Sky } from '@react-three/drei';
import { Canvas } from '@react-three/fiber';
import { useEffect, useMemo, useRef } from 'react';
import * as THREE from 'three';
import { PlayerController } from '../controls/PlayerController';
import { devHandle, type Player } from '../controls/player';
import { worldZ } from '../hole/heightField';
import type { HoleData } from '../hole/loadHole';
import { FOV } from '../hole/views';
import { Pin, TeeMarkers } from './Markers';
import { Terrain } from './Terrain';
import { Trees } from './Trees';
import { Water } from './Water';

const SUN_AZIMUTH = 215; // degrees clockwise from north: afternoon sun from the south-west

interface Props {
  hole: HoleData;
  player: Player;
  onLockChange: (locked: boolean) => void;
}

function sunDirection(elevationDeg: number) {
  const el = THREE.MathUtils.degToRad(elevationDeg), az = THREE.MathUtils.degToRad(SUN_AZIMUTH);
  return new THREE.Vector3(Math.sin(az) * Math.cos(el), Math.sin(el), -Math.cos(az) * Math.cos(el));
}

/** Sun with a shadow frustum covering the whole tile. */
function Sun({ hole, direction }: { hole: HoleData; direction: THREE.Vector3 }) {
  const light = useRef<THREE.DirectionalLight>(null);
  const size = hole.field.size;
  const centre = new THREE.Vector3(size / 2, hole.field.range / 2, worldZ(size / 2));

  useEffect(() => {
    const l = light.current;
    if (!l) return;
    const half = size * 0.6;
    const cam = l.shadow.camera;
    cam.left = -half; cam.right = half; cam.top = half; cam.bottom = -half;
    cam.near = 1; cam.far = size * 3;
    cam.updateProjectionMatrix();
    l.target.position.copy(centre);
    l.target.updateMatrixWorld();
  });

  return (
    <directionalLight
      ref={light}
      position={centre.clone().addScaledVector(direction, size * 1.2)}
      intensity={2.1}
      color="#fff4e0"
      castShadow
      shadow-mapSize={[4096, 4096]}
      shadow-bias={-0.0004}
      shadow-normalBias={0.6}
    />
  );
}

export function HoleScene({ hole, player, onLockChange }: Props) {
  const { sky } = hole.theme;
  const sun = useMemo(() => sunDirection(sky.sunElevation), [sky.sunElevation]);
  const skyProps = { distance: 45000, sunPosition: sun.toArray(), turbidity: sky.turbidity, rayleigh: sky.rayleigh,
                     mieCoefficient: 0.004, mieDirectionalG: 0.85 };
  const fogFar = Math.max(2600, hole.field.size * 5);

  return (
    <Canvas
      shadows="percentage"
      dpr={[1, 1.5]}
      gl={{ antialias: true, powerPreference: 'high-performance' }}
      camera={{ fov: FOV, near: 0.1, far: 50000 }}
      onCreated={({ gl }) => {
        gl.toneMappingExposure = 0.82;
        devHandle().renderer = gl;
      }}
    >
      <fog attach="fog" args={[sky.fog, fogFar * 0.12, fogFar]} />
      <Sky {...skyProps} />
      <Environment resolution={128} environmentIntensity={0.55} frames={1}>
        <Sky {...skyProps} />
      </Environment>
      <hemisphereLight args={['#dbe9ff', '#4b5a2c', 0.35]} />
      <Sun hole={hole} direction={sun} />
      <Terrain hole={hole} />
      <Trees hole={hole} />
      <Water hole={hole} />
      <Pin hole={hole} />
      <TeeMarkers hole={hole} />
      <PlayerController hole={hole} player={player} onLockChange={onLockChange} />
    </Canvas>
  );
}
