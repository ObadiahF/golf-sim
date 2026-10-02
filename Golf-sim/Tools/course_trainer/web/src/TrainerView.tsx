import { useCallback, useEffect, useRef, useState } from 'react';
import { devHandle, isInteractive, isTyping, Player } from './controls/player';
import { Hud } from './hud/Hud';
import { HoleScene } from './scene/HoleScene';
import { useTrainer } from './useTrainer';
import type { Session } from './useSession';

/** The 3D hole plus the HUD, for one logged-in user. */
export function TrainerView({ session, name }: { session: Session; name: string }) {
  const trainer = useTrainer();
  const player = useRef(new Player()).current;
  devHandle().player = player;  // pointer lock can't be scripted: automation moves the player directly
  const [locked, setLocked] = useState(false);
  const [help, setHelp] = useState(false);
  const [top, setTop] = useState(false);
  const comment = useRef<HTMLTextAreaElement>(null);
  const onLockChange = useCallback((l: boolean) => setLocked(l), []);
  const { setDraft, submit, next, busy, waiting } = trainer;

  // Rating / navigation hotkeys (movement keys live in PlayerController). Never while typing.
  useEffect(() => {
    const onKey = (e: KeyboardEvent) => {
      if (isTyping(e.target) || e.metaKey || e.ctrlKey || e.altKey || e.repeat) return;
      const rate = { Digit1: 'up', Digit2: 'down' } as const;
      if (e.code in rate && !waiting) {  // waiting: nothing to rate (RatePanel)
        const rating = rate[e.code as keyof typeof rate];
        setDraft(d => ({ ...d, rating: d.rating === rating ? null : rating }));
      } else if (e.code === 'Enter' && !busy && !isInteractive(e.target)) {
        submit();
      } else if (e.code === 'KeyN' && !busy) {
        next();
      } else if (e.code === 'KeyL') {
        document.exitPointerLock();
        setTop(t => !t);
      } else if (e.code === 'KeyH' || e.code === 'F1') {
        e.preventDefault();
        setHelp(h => !h);
      } else if (e.code === 'Escape') {  // closes an open overlay (the browser's own Esc releases the mouse)
        setTop(false);
        setHelp(false);
      } else if (e.code === 'KeyF') {
        e.preventDefault();
        document.exitPointerLock();
        comment.current?.focus();
      }
    };
    window.addEventListener('keydown', onKey);
    return () => window.removeEventListener('keydown', onKey);
  }, [setDraft, submit, next, busy, waiting]);

  return (
    <div className={`app ${locked ? 'is-locked' : ''}`}>
      {trainer.hole && <HoleScene hole={trainer.hole} player={player} onLockChange={onLockChange} />}
      <Hud trainer={trainer} player={player} user={{ name, logout: session.logout }} locked={locked} help={help} setHelp={setHelp} top={top} setTop={setTop} commentRef={comment} />
    </div>
  );
}
