import * as THREE from 'three';
import type { View } from '../hole/views';

export const FLY_SPEED = { initial: 24, min: 2, max: 220 };

/** Mutable player state shared by the controller (writes every frame) and the HUD (reads in rAF). */
export class Player {
  readonly position = new THREE.Vector3();
  readonly velocity = new THREE.Vector3();
  yaw = 0;
  pitch = 0;
  flying = true;
  grounded = false;
  sprinting = false;
  flySpeed = FLY_SPEED.initial;
  locked = false;
  /** On-screen touch input (TouchControls): move stick -1..1 (x = right, y = forward) and up / down. */
  readonly touch = { x: 0, y: 0, vertical: 0 };

  place(view: View) {
    this.position.set(view.x, view.y, view.z);
    this.velocity.set(0, 0, 0);
    this.yaw = view.yaw;
    this.pitch = view.pitch;
    this.flying = view.flying;
    this.grounded = !view.flying;
  }

  adjustFlySpeed(wheelDelta: number) {
    this.flySpeed = THREE.MathUtils.clamp(this.flySpeed * Math.exp(-wheelDelta * 0.0012), FLY_SPEED.min, FLY_SPEED.max);
  }
}

/** Phones and tablets: no pointer lock, touch controls instead. */
export const isTouchDevice = () => window.matchMedia('(pointer: coarse)').matches;

/** True when keystrokes belong to a form field (so movement / rating hotkeys must ignore them). */
export function isTyping(target: EventTarget | null = document.activeElement) {
  const el = target as HTMLElement | null;
  return !!el && (el.isContentEditable || ['INPUT', 'TEXTAREA', 'SELECT'].includes(el.tagName));
}
