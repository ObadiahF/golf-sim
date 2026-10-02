import { useEffect, useRef, useState, type PointerEvent as ReactPointerEvent } from 'react';
import type { HoleData } from '../hole/loadHole';
import { measureInsets } from '../hud/insets';
import { viewFor, type ViewName } from '../hole/views';
import { isTouchDevice, type Player } from './player';

const STICK_RADIUS = 44; // px the knob can travel from the centre
const VIEWS: { name: ViewName; label: string }[] = [
  { name: 'tee', label: 'Tee' }, { name: 'green', label: 'Green' }, { name: 'overhead', label: 'Overhead' },
];

/**
 * Phone / tablet controls: a move stick (bottom-left), fly up / down (bottom-right) and one-tap views.
 * Drag anywhere on the 3D view to look (handled in PlayerController). Renders nothing on mouse devices.
 */
export function TouchControls({ hole, player }: { hole: HoleData | null; player: Player }) {
  const [touch, setTouch] = useState(isTouchDevice);
  useEffect(() => {
    const query = window.matchMedia('(pointer: coarse)');
    const update = () => setTouch(query.matches);
    query.addEventListener('change', update);
    return () => query.removeEventListener('change', update);
  }, []);
  if (!touch || !hole) return null;

  const hold = (vertical: number) => ({
    onPointerDown: (e: ReactPointerEvent) => { e.currentTarget.setPointerCapture(e.pointerId); player.touch.vertical = vertical; },
    onPointerUp: () => { player.touch.vertical = 0; },
    onPointerCancel: () => { player.touch.vertical = 0; },
  });

  return (
    <div className="touch-controls">
      <div className="touch-views">
        {VIEWS.map(v => (
          <button key={v.name} className="btn small" onClick={() => player.place(viewFor(hole, v.name, measureInsets()))}>{v.label}</button>
        ))}
      </div>
      <Stick player={player} />
      <div className="touch-fly">
        <button aria-label="Fly up" {...hold(1)}>▲</button>
        <button aria-label="Fly down" {...hold(-1)}>▼</button>
      </div>
    </div>
  );
}

function Stick({ player }: { player: Player }) {
  const knob = useRef<HTMLDivElement>(null);
  const centre = useRef({ x: 0, y: 0 });

  const move = (e: ReactPointerEvent) => {
    let dx = e.clientX - centre.current.x, dy = e.clientY - centre.current.y;
    const len = Math.hypot(dx, dy);
    if (len > STICK_RADIUS) { dx *= STICK_RADIUS / len; dy *= STICK_RADIUS / len; }
    player.touch.x = dx / STICK_RADIUS;
    player.touch.y = -dy / STICK_RADIUS;
    if (knob.current) knob.current.style.transform = `translate(${dx}px, ${dy}px)`;
  };
  const release = () => {
    player.touch.x = player.touch.y = 0;
    if (knob.current) knob.current.style.transform = '';
  };

  return (
    <div className="touch-stick" aria-label="Move"
         onPointerDown={e => {
           const r = e.currentTarget.getBoundingClientRect();
           centre.current = { x: r.left + r.width / 2, y: r.top + r.height / 2 };
           e.currentTarget.setPointerCapture(e.pointerId);
           move(e);
         }}
         onPointerMove={e => { if (e.currentTarget.hasPointerCapture(e.pointerId)) move(e); }}
         onPointerUp={release} onPointerCancel={release}>
      <div className="touch-knob" ref={knob} />
    </div>
  );
}
