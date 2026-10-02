import { useEffect, useState } from 'react';
import { api, type TopHole } from '../api';
import { yards } from '../hole/loadHole';
import type { Trainer } from '../useTrainer';

const SCORE_DETAILS = 'Score: the Wilson score lower bound (95%) of the like share, 0-100. It is the share of likes ' +
  'the hole has at least, given how many votes it has, so 1 👍 scores 21 and 9 👍 1 👎 scores 60. Only holes with ' +
  'more 👍 than 👎 are listed.';

/** Leaderboard overlay: the most-liked holes by everyone's votes. Opening one does not count as seeing it. */
export function TopPanel({ trainer, onClose }: { trainer: Trainer; onClose: () => void }) {
  const [holes, setHoles] = useState<TopHole[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const presetLabel = (name: string) => trainer.catalog?.presets.find(p => p.name === name)?.label ?? name;

  useEffect(() => {
    api.top().then(setHoles, e => setError(e instanceof Error ? e.message : String(e)));
  }, []);

  const pick = (h: TopHole) => {
    onClose();
    trainer.open(h, true);
  };

  return (
    <div className="help-backdrop" onClick={onClose}>
      <section className="panel card top-card" onClick={e => e.stopPropagation()} aria-label="Top holes">
        <header className="card-head">
          <span className="eyebrow">Top holes</span>
          <button className="btn ghost small" onClick={onClose}>Close <kbd>L</kbd></button>
        </header>
        {error && <p className="login-error">{error}</p>}
        {!holes && !error && <p className="muted">Loading the leaderboard…</p>}
        {holes?.length === 0 && <p className="muted">No liked holes yet. Give the ones you enjoy a 👍 and they show up here.</p>}
        <ol className="top-list">
          {holes?.map(h => (
            <li key={h.id}>
              <button className="top-row" onClick={() => pick(h)} title={h.id}>
                <span className="top-rank">{h.rank}</span>
                <img src={h.previewUrl} alt="" loading="lazy" width={52} height={52} />
                <span className="top-what">
                  <strong>{presetLabel(h.preset)}</strong>
                  <small>Par {h.par} · {yards(h.lengthMeters)} yd{h.rating ? ` · you ${h.rating === 'up' ? '👍' : '👎'}` : ''}</small>
                </span>
                <span className="top-votes">👍 {h.ups} <span className="muted">👎 {h.downs}</span></span>
                <span className="top-score" title={SCORE_DETAILS}>
                  {Math.round(h.score * 100)}
                </span>
              </button>
            </li>
          ))}
        </ol>
        <small className="muted top-note" title={SCORE_DETAILS}>
          Ranked by likes, with confidence: more votes count more. Only holes with more 👍 than 👎 make the list.
        </small>
      </section>
    </div>
  );
}
