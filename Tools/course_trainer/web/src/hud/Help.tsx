const CONTROLS: [string, string][] = [
  ['Click the view', 'capture the mouse (Esc releases it)'],
  ['Mouse', 'look around'],
  ['W A S D', 'move (relative to where you face)'],
  ['Space / Shift', 'fly up / down'],
  ['Space ×2', 'toggle flying ↔ walking (walking: Space jumps)'],
  ['Ctrl or W ×2', 'sprint'],
  ['Scroll wheel', 'flying speed'],
  ['T / G / O', 'teleport: tee · green · overhead'],
  ['1 / 2', '👍 / 👎'],
  ['F', 'write feedback'],
  ['Enter', 'submit rating & next hole'],
  ['N', 'skip to the next hole (no rating)'],
  ['L', 'top holes (leaderboard)'],
  ['H', 'this help'],
];

export function Help({ onClose }: { onClose: () => void }) {
  return (
    <div className="help-backdrop" onClick={onClose}>
      <section className="panel card help" onClick={e => e.stopPropagation()}>
        <header className="card-head">
          <span className="eyebrow">Controls</span>
          <button className="btn ghost small" onClick={onClose}>Close <kbd>H</kbd></button>
        </header>
        <dl>
          {CONTROLS.map(([key, what]) => (
            <div key={key}><dt>{key}</dt><dd>{what}</dd></div>
          ))}
        </dl>
        <p className="muted">
          Walk the hole, judge it like a golfer, then rate it. Chips are structured hints (“more trees”) that nudge the
          matching style knob when the model retrains; your note is stored with the rating. Holes come from a shared
          pool: you never see one twice, and the most-liked ones top the leaderboard.
        </p>
      </section>
    </div>
  );
}
