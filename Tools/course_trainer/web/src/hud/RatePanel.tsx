import type { RefObject } from 'react';
import type { Rating } from '../api';
import type { Trainer } from '../useTrainer';

const THUMBS: { rating: Rating; icon: string; label: string; key: string }[] = [
  { rating: 'up', icon: '👍', label: 'Like', key: '1' },
  { rating: 'down', icon: '👎', label: 'Dislike', key: '2' },
];

/** Thumbs, quick-feedback chips, free text, submit (which also generates the next hole). */
export function RatePanel({ trainer, commentRef }: { trainer: Trainer; commentRef: RefObject<HTMLTextAreaElement | null> }) {
  const { draft, setDraft, catalog, submit, busy, hole } = trainer;
  const toggleTag = (id: string) =>
    setDraft(d => ({ ...d, tags: d.tags.includes(id) ? d.tags.filter(t => t !== id) : [...d.tags, id] }));

  return (
    <section className="panel card rate-card">
      <div className="thumbs">
        {THUMBS.map(t => (
          <button key={t.rating} className={`thumb thumb-${t.rating} ${draft.rating === t.rating ? 'on' : ''}`}
                  aria-pressed={draft.rating === t.rating}
                  onClick={() => setDraft(d => ({ ...d, rating: d.rating === t.rating ? null : t.rating }))}>
            <span className="thumb-icon">{t.icon}</span>
            <span>{t.label}</span>
            <kbd>{t.key}</kbd>
          </button>
        ))}
      </div>
      <div className="chips" role="group" aria-label="Quick feedback">
        {catalog?.feedback.map(tag => (
          <button key={tag.id} className={`chip ${draft.tags.includes(tag.id) ? 'on' : ''}`}
                  aria-pressed={draft.tags.includes(tag.id)} onClick={() => toggleTag(tag.id)}
                  title={Object.entries(tag.knobs).map(([k, d]) => `${d > 0 ? 'more' : 'less'} ${k}`).join(', ')}>
            {tag.label}
          </button>
        ))}
      </div>
      <textarea
        ref={commentRef}
        rows={2}
        placeholder="Anything else? (F to type, optional)"
        value={draft.comment}
        onFocus={() => document.exitPointerLock()}
        onChange={e => setDraft(d => ({ ...d, comment: e.target.value }))}
        onKeyDown={e => {
          if (e.key === 'Escape') e.currentTarget.blur();
          if (e.key === 'Enter' && (e.metaKey || e.ctrlKey)) { e.preventDefault(); submit(); }
        }}
      />
      <button className="btn submit" disabled={!draft.rating || !!busy || !hole} onClick={submit}>
        {draft.rating ? `Submit ${draft.rating === 'up' ? '👍' : '👎'} & next hole` : 'Pick 👍 or 👎'}
        <kbd>Enter</kbd>
      </button>
    </section>
  );
}
