import type { Trainer } from '../useTrainer';

const pretty = (s: string) => s.replace(/_/g, ' ');

/** What the model has learned so far, and the retrain button. */
export function TastePanel({ trainer }: { trainer: Trainer }) {
  const { status, retrain, busy, catalog } = trainer;
  if (!status) return null;
  const down = status.ratings - status.up;
  const stale = status.ratings - status.trainedOn;
  const presetLabel = catalog?.presets.find(p => p.name === status.preset)?.label ?? status.preset;

  return (
    <section className="panel card taste-card">
      <header className="card-head">
        <span className="eyebrow">Your taste{presetLabel ? ` · ${presetLabel}` : ''}</span>
        <span className="tally"><b>{status.up}</b>👍 <b>{down}</b>👎</span>
      </header>
      <div className="taste-lists">
        {status.likes.map(l => <span key={l} className="taste like">＋ {pretty(l)}</span>)}
        {status.dislikes.map(d => <span key={d} className="taste dislike">－ {pretty(d)}</span>)}
        {!status.likes.length && !status.dislikes.length && (
          <span className="muted">{status.trainedOn ? 'No clear preferences yet.' : 'Not trained yet.'}</span>
        )}
      </div>
      <button className="btn ghost" disabled={!!busy || !status.ratings} onClick={retrain}>
        Retrain{stale > 0 ? ` (${stale} new)` : ''}
      </button>
      <small className="muted">Model trained on {status.trainedOn} of {status.ratings} ratings</small>
    </section>
  );
}
