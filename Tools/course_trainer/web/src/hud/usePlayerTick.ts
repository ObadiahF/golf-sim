import { useEffect, useRef } from 'react';

/** Calls `draw` from requestAnimationFrame at up to `fps` (HUD bits that follow the player without React renders). */
export function usePlayerTick(draw: () => void, fps = 20) {
  const latest = useRef(draw);
  latest.current = draw;
  useEffect(() => {
    let frame = 0, last = 0;
    const loop = (t: number) => {
      frame = requestAnimationFrame(loop);
      if (t - last >= 1000 / fps) { last = t; latest.current(); }
    };
    frame = requestAnimationFrame(loop);
    return () => cancelAnimationFrame(frame);
  }, [fps]);
}
