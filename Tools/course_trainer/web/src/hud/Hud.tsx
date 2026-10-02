import type { RefObject } from 'react';
import type { Player } from '../controls/player';
import { TouchControls } from '../controls/TouchControls';
import type { Trainer } from '../useTrainer';
import { Help } from './Help';
import { HolePanel } from './HolePanel';
import { Minimap } from './Minimap';
import { RatePanel } from './RatePanel';
import { TastePanel } from './TastePanel';

interface Props {
  trainer: Trainer;
  player: Player;
  locked: boolean;
  help: boolean;
  setHelp: (open: boolean) => void;
  commentRef: RefObject<HTMLTextAreaElement | null>;
  user: { name: string; logout: () => void };
}

/** React overlay on top of the 3D view. While the mouse is captured the panels dim and ignore the pointer. */
export function Hud({ trainer, player, locked, help, setHelp, commentRef, user }: Props) {
  const { hole, busy, error, toast, dismissError } = trainer;
  return (
    <div className="hud">
      {locked && <div className="crosshair" aria-hidden />}
      <aside className="column left">
        <HolePanel trainer={trainer} />
        <TastePanel trainer={trainer} />
      </aside>
      <aside className="column right">
        {hole && <Minimap hole={hole} player={player} />}
        <RatePanel trainer={trainer} commentRef={commentRef} />
      </aside>
      <TouchControls hole={hole} player={player} />

      {hole && !locked && !busy && !help && (
        <div className="click-hint">
          <strong>Click the course to explore</strong>
          <span>WASD · mouse · Space / Shift · <kbd>H</kbd> for all controls</span>
        </div>
      )}
      {locked && <div className="lock-hint">Esc to release the mouse · <kbd>1</kbd>/<kbd>2</kbd> rate · <kbd>T</kbd> <kbd>G</kbd> <kbd>O</kbd> views</div>}
      <div className="topbar">
        <span className="who" title="Logged in">{user.name}</span>
        <button className="btn ghost small" onClick={user.logout}>Log out</button>
        <button className="btn ghost small" onClick={() => setHelp(true)}>Controls <kbd>H</kbd></button>
      </div>

      {busy && (
        <div className="loading">
          <div className="ball" />
          <span>{busy}</span>
        </div>
      )}
      {error && (
        <div className="banner error" role="alert">
          {error}
          <button className="btn ghost small" onClick={dismissError}>Dismiss</button>
        </div>
      )}
      {toast && <div className="banner toast">{toast}</div>}
      {help && <Help onClose={() => setHelp(false)} />}
    </div>
  );
}
