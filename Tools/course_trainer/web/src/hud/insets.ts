import { useEffect } from 'react';

/** Screen pixels covered by the HUD on each side of the 3D view, and the view's size. */
export interface Insets { top: number; right: number; bottom: number; left: number; width: number; height: number }

/** HUD pieces that hide the hole (the joystick and fly buttons are see-through, so they don't count). */
const COVERING = '.hud .column .panel, .hud .topbar, .hud .touch-views';
const CORNER_PX = 16;  // a panel this close to two edges sits in their corner

/**
 * What the HUD covers right now. Each visible panel counts toward the screen edge it sits against (the nearest one;
 * for a panel in a corner, the edge whose strip, cut to clear it, loses the least screen), so a top bar or the view
 * buttons under it are a top inset, the phone rating sheet a bottom one, a side column or the landscape rating panel
 * a left / right one.
 */
export function measureInsets(): Insets {
  const width = window.innerWidth, height = window.innerHeight;
  const insets: Insets = { top: 0, right: 0, bottom: 0, left: 0, width, height };
  for (const el of document.querySelectorAll<HTMLElement>(COVERING)) {
    const r = el.getBoundingClientRect();
    if (r.width < 1 || r.height < 1) continue;  // display: none (collapsed, or not on this layout)
    const sides = { top: r.bottom, bottom: height - r.top, left: r.right, right: width - r.left };
    const gap = { top: r.top, bottom: height - r.bottom, left: r.left, right: width - r.right };
    const keys = Object.keys(sides) as (keyof typeof sides)[];
    const nearest = Math.min(...keys.map(k => gap[k]));
    const lost = (k: keyof typeof sides) => sides[k] * (k === 'top' || k === 'bottom' ? width : height);
    const side = keys.filter(k => gap[k] <= nearest + CORNER_PX).reduce((a, b) => (lost(b) < lost(a) ? b : a));
    insets[side] = Math.max(insets[side], sides[side]);
  }
  return insets;
}

/**
 * Keeps `--hud-top` (bottom of the phone hole card) and `--hud-bottom` (height of the phone rating sheet) on <html>
 * up to date as panels collapse or the screen turns, so the touch controls and the waiting card sit in the free area.
 */
export function useHudInsetVars() {
  useEffect(() => {
    const root = document.documentElement.style;
    const update = () => {
      const card = document.querySelector('.hud .column.left')?.getBoundingClientRect();
      const sheet = document.querySelector('.hud .column.right')?.getBoundingClientRect();
      if (card) root.setProperty('--hud-top', `${Math.round(card.bottom)}px`);
      if (sheet) root.setProperty('--hud-bottom', `${Math.round(window.innerHeight - sheet.top)}px`);
    };
    const observer = new ResizeObserver(update);
    document.querySelectorAll('.hud .column').forEach(el => observer.observe(el));
    window.addEventListener('resize', update);
    update();
    return () => { observer.disconnect(); window.removeEventListener('resize', update); };
  }, []);
}
