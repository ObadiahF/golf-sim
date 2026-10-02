import { useFrame, useThree } from '@react-three/fiber';
import { useEffect, useRef } from 'react';
import * as THREE from 'three';
import { northOf } from '../hole/heightField';
import type { HoleData } from '../hole/loadHole';
import { measureInsets } from '../hud/insets';
import { EYE_HEIGHT, viewFor, type ViewName } from '../hole/views';
import { isTouchDevice, isTyping, type Player } from './player';

// Minecraft creative-mode feel.
const LOOK_SENSITIVITY = 0.0022;
const TOUCH_LOOK_SENSITIVITY = 0.005;
const DOUBLE_TAP_MS = 300;
const WALK_SPEED = 6, SPRINT_FACTOR = 1.9, FLY_SPRINT_FACTOR = 2.5;
const GRAVITY = 28, JUMP_SPEED = 8.5;
const FLY_CLEARANCE = 0.7, MAX_ALTITUDE = 2500;
const VIEW_KEYS: Record<string, ViewName> = { KeyT: 'tee', KeyG: 'green', KeyO: 'overhead' };

interface Props {
  hole: HoleData;
  player: Player;
  onLockChange: (locked: boolean) => void;
}

/** Pointer-lock mouse look + WASD / Space / Shift movement, with fly <-> walk and terrain collision. */
export function PlayerController({ hole, player, onLockChange }: Props) {
  const { camera, gl } = useThree();
  const keys = useRef(new Set<string>());
  const taps = useRef({ space: 0, w: 0, sprintLatch: false });

  // New hole: start hovering behind the tee, looking down the hole.
  useEffect(() => { player.place(viewFor(hole, 'start')); }, [hole, player]);

  useEffect(() => {
    const canvas = gl.domElement;
    const lock = () => {
      if (isTouchDevice()) return; // touch: drag to look (below), no pointer lock
      if (document.pointerLockElement !== canvas) {
        (document.activeElement as HTMLElement | null)?.blur?.();
        Promise.resolve(canvas.requestPointerLock()).catch(() => { /* user exited before lock completed */ });
      }
    };
    const onLockChanged = () => {
      player.locked = document.pointerLockElement === canvas;
      if (!player.locked) keys.current.clear();
      onLockChange(player.locked);
    };
    const onMouseMove = (e: MouseEvent) => {
      if (!player.locked) return;
      player.yaw -= e.movementX * LOOK_SENSITIVITY;
      player.pitch = THREE.MathUtils.clamp(player.pitch - e.movementY * LOOK_SENSITIVITY, -Math.PI / 2 + 1e-3, Math.PI / 2 - 1e-3);
    };
    const onWheel = (e: WheelEvent) => { if (player.locked) player.adjustFlySpeed(e.deltaY); };
    // Touch: one finger dragging on the 3D view turns the camera.
    let lastTouch: { id: number; x: number; y: number } | null = null;
    const onTouchStart = (e: TouchEvent) => {
      const t = e.changedTouches[0];
      if (!lastTouch && t) lastTouch = { id: t.identifier, x: t.clientX, y: t.clientY };
    };
    const onTouchMove = (e: TouchEvent) => {
      const t = Array.from(e.changedTouches).find(c => c.identifier === lastTouch?.id);
      if (!t || !lastTouch) return;
      e.preventDefault();
      player.yaw -= (t.clientX - lastTouch.x) * TOUCH_LOOK_SENSITIVITY;
      player.pitch = THREE.MathUtils.clamp(player.pitch - (t.clientY - lastTouch.y) * TOUCH_LOOK_SENSITIVITY,
                                           -Math.PI / 2 + 1e-3, Math.PI / 2 - 1e-3);
      lastTouch = { id: t.identifier, x: t.clientX, y: t.clientY };
    };
    const onTouchEnd = (e: TouchEvent) => {
      if (Array.from(e.changedTouches).some(c => c.identifier === lastTouch?.id)) lastTouch = null;
    };
    const onKeyDown = (e: KeyboardEvent) => {
      if (isTyping(e.target)) return;
      const view = VIEW_KEYS[e.code];
      if (view && !e.metaKey && !e.ctrlKey) { player.place(viewFor(hole, view, measureInsets())); return; }
      if (!player.locked) return;
      if (['Space', 'ControlLeft', 'ControlRight'].includes(e.code)) e.preventDefault();
      keys.current.add(e.code);
      if (e.repeat) return;
      const now = performance.now();
      if (e.code === 'Space') {
        if (now - taps.current.space < DOUBLE_TAP_MS) {
          player.flying = !player.flying;
          player.velocity.y = 0;
          taps.current.space = 0;
        } else {
          taps.current.space = now;
          if (!player.flying && player.grounded) { player.velocity.y = JUMP_SPEED; player.grounded = false; }
        }
      }
      if (e.code === 'KeyW') {
        if (now - taps.current.w < DOUBLE_TAP_MS) taps.current.sprintLatch = true;
        taps.current.w = now;
      }
    };
    const onKeyUp = (e: KeyboardEvent) => {
      keys.current.delete(e.code);
      if (e.code === 'KeyW') taps.current.sprintLatch = false;
    };
    // Accidental Ctrl+W (sprint + forward) would close the tab on Windows / Linux: ask first while playing.
    const onBlur = () => keys.current.clear();
    const onBeforeUnload = (e: BeforeUnloadEvent) => { if (player.locked) e.preventDefault(); };

    canvas.addEventListener('click', lock);
    canvas.addEventListener('touchstart', onTouchStart, { passive: true });
    canvas.addEventListener('touchmove', onTouchMove, { passive: false });
    canvas.addEventListener('touchend', onTouchEnd);
    canvas.addEventListener('touchcancel', onTouchEnd);
    document.addEventListener('pointerlockchange', onLockChanged);
    document.addEventListener('mousemove', onMouseMove);
    document.addEventListener('wheel', onWheel, { passive: true });
    window.addEventListener('keydown', onKeyDown);
    window.addEventListener('keyup', onKeyUp);
    window.addEventListener('blur', onBlur);
    window.addEventListener('beforeunload', onBeforeUnload);
    return () => {
      canvas.removeEventListener('click', lock);
      canvas.removeEventListener('touchstart', onTouchStart);
      canvas.removeEventListener('touchmove', onTouchMove);
      canvas.removeEventListener('touchend', onTouchEnd);
      canvas.removeEventListener('touchcancel', onTouchEnd);
      document.removeEventListener('pointerlockchange', onLockChanged);
      document.removeEventListener('mousemove', onMouseMove);
      document.removeEventListener('wheel', onWheel);
      window.removeEventListener('keydown', onKeyDown);
      window.removeEventListener('keyup', onKeyUp);
      window.removeEventListener('blur', onBlur);
      window.removeEventListener('beforeunload', onBeforeUnload);
    };
  }, [gl, hole, player, onLockChange]);

  const forward = useRef(new THREE.Vector3());
  const right = useRef(new THREE.Vector3());
  const wish = useRef(new THREE.Vector3());

  useFrame((_, rawDt) => {
    const dt = Math.min(rawDt, 0.05);
    const k = keys.current;
    const axis = (pos: string, neg: string) => (k.has(pos) ? 1 : 0) - (k.has(neg) ? 1 : 0);
    player.sprinting = taps.current.sprintLatch || k.has('ControlLeft') || k.has('ControlRight');

    forward.current.set(-Math.sin(player.yaw), 0, -Math.cos(player.yaw));
    right.current.set(Math.cos(player.yaw), 0, -Math.sin(player.yaw));
    const touch = player.touch;
    wish.current.copy(forward.current).multiplyScalar(axis('KeyW', 'KeyS') + touch.y)
      .addScaledVector(right.current, axis('KeyD', 'KeyA') + touch.x);
    if (wish.current.lengthSq() > 1) wish.current.normalize();

    const v = player.velocity, p = player.position;
    if (player.flying) {
      const speed = player.flySpeed * (player.sprinting ? FLY_SPRINT_FACTOR : 1);
      const blend = 1 - Math.exp(-dt * 9);
      v.x += (wish.current.x * speed - v.x) * blend;
      v.z += (wish.current.z * speed - v.z) * blend;
      const vertical = THREE.MathUtils.clamp(
        (k.has('Space') ? 1 : 0) - (k.has('ShiftLeft') || k.has('ShiftRight') ? 1 : 0) + touch.vertical, -1, 1);
      v.y += (vertical * speed * 0.8 - v.y) * blend;
    } else {
      const speed = WALK_SPEED * (player.sprinting ? SPRINT_FACTOR : 1);
      const blend = 1 - Math.exp(-dt * (player.grounded ? 14 : 3));
      v.x += (wish.current.x * speed - v.x) * blend;
      v.z += (wish.current.z * speed - v.z) * blend;
      v.y -= GRAVITY * dt;
    }
    p.addScaledVector(v, dt);

    // Stay over the modelled land and above the ground (or water surface).
    const { origin, span } = hole.surfaces;
    p.x = THREE.MathUtils.clamp(p.x, origin + 5, origin + span - 5);
    p.z = THREE.MathUtils.clamp(p.z, -(origin + span - 5), -(origin + 5));
    const floor = hole.floorAt(p.x, northOf(p.z));
    const minY = floor + (player.flying ? FLY_CLEARANCE : EYE_HEIGHT);
    player.grounded = !player.flying && p.y <= minY + 0.02;
    if (p.y < minY) { p.y = minY; if (v.y < 0) v.y = 0; }
    p.y = Math.min(p.y, floor + MAX_ALTITUDE);

    camera.position.copy(p);
    camera.rotation.set(player.pitch, player.yaw, 0, 'YXZ');
  });

  return null;
}
