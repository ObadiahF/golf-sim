import { useRef, useState, type RefObject } from 'react';
import type { Rating } from '../api';
import { isTouchDevice } from '../controls/player';
import type { Trainer } from '../useTrainer';

const SWIPE_PX = 24;  // a drag on the sheet's handle further than this opens / closes it (shorter: a tap toggles)

const THUMBS: { rating: Rating; icon: string; label: string; key: string }[] = [
  { rating: 'up', icon: '👍', label: 'Like', key: '1' },
  { rating: 'down', icon: '👎', label: 'Dislike', key: '2' },
];

/**
 * Thumbs, quick-feedback chips, free text, submit (which also generates the next hole). On phones it is a bottom
 * sheet that collapses to the thumbs row (handle: tap or swipe). While waiting for new holes there is nothing to rate:
 * the hole still on screen is one you already passed, so rating is off until a hole opens (T6-4).
 */
export function RatePanel({ trainer, commentRef }: { trainer: Trainer; commentRef: RefObject<HTMLTextAreaElement | null> }) {
  const { draft, setDraft, catalog, submit, busy, hole, waiting } = trainer;
  const [collapsed, setCollapsed] = useState(false);
  const drag = useRef({ from: 0, swiped: false });
  /** Opposite chips are exclusive, like the thumbs: picking Too long drops Too short. */
  const toggleTag = (id: string) => setDraft(d => {
    if (d.tags.includes(id)) return { ...d, tags: d.tags.filter(t => t !== id) };
    const excludes = catalog?.feedback.find(t => t.id === id)?.excludes ?? [];
    return { ...d, tags: [...d.tags.filter(t => !excludes.includes(t)), id] };
  });

  if (waiting) {
    return (
      <section className="panel card rate-card rate-paused" role="note">
        <strong>Nothing to rate yet</strong>
        <small className="muted">The hole behind is one you already skipped or rated, so rating is off until the next
          one opens. To rate a hole you skipped, pick it from “Revisit a recent hole…”.</small>
      </section>
    );
  }

  return (
    <section className={`panel card rate-card ${collapsed ? 'collapsed' : ''}`}>
      <button className="sheet-handle" aria-expanded={!collapsed}
              aria-label={collapsed ? 'Show feedback and comment' : 'Collapse to the thumbs'}
              onPointerDown={e => { e.currentTarget.setPointerCapture(e.pointerId); drag.current = { from: e.clientY, swiped: false }; }}
              onPointerUp={e => {
                const dy = e.clientY - drag.current.from;
                drag.current.swiped = Math.abs(dy) >= SWIPE_PX;
                if (drag.current.swiped) setCollapsed(dy > 0);  // swipe down closes, up opens
              }}
              onClick={() => { if (!drag.current.swiped) setCollapsed(!collapsed); drag.current.swiped = false; }} />
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
        placeholder={isTouchDevice() ? 'Anything else? (optional)' : 'Anything else? (F to type, optional)'}
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
