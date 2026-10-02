import { yards } from '../hole/loadHole';
import type { Trainer } from '../useTrainer';

const knobLabel = (name: string) => name.replace(/_/g, ' ');

/** Scorecard header: what this hole is, plus the generator controls. */
export function HolePanel({ trainer }: { trainer: Trainer }) {
  const { hole, catalog, preset, setPreset, par, setPar, generate, busy, recent, open } = trainer;
  const s = hole?.summary;
  const presetLabel = (name: string) => catalog?.presets.find(p => p.name === name)?.label ?? name;
  const score = s?.modelScore;

  return (
    <section className="panel card hole-card">
      <header className="card-head">
        <span className="eyebrow">{s ? presetLabel(s.preset) : 'Course Trainer'}</span>
        {s?.rating && <span className={`stamp stamp-${s.rating}`}>{s.rating === 'up' ? 'Liked' : 'Disliked'}</span>}
      </header>
      {s && (
        <>
          <div className="scoreline">
            <div><small>Par</small><strong>{s.par}</strong></div>
            <div><small>Yards</small><strong>{yards(s.lengthMeters)}</strong></div>
            <div title="The model's predicted chance you like this hole (blank: sampled without the model)">
              <small>Model</small><strong>{score == null ? '—' : `${Math.round(score * 100)}%`}</strong>
            </div>
          </div>
          <dl className="meta">
            <dt>Theme</dt><dd>{s.theme}</dd>
            <dt>Seed</dt><dd>{s.seed}</dd>
            <dt>Hole</dt><dd className="mono" title={s.id}>{s.id}</dd>
          </dl>
          <details className="knobs">
            <summary>Style knobs</summary>
            <ul>
              {Object.entries(s.params).map(([k, v]) => (
                <li key={k} title={catalog?.params[k]}>
                  <span>{knobLabel(k)}</span><meter min={0} max={1} value={v} />
                </li>
              ))}
            </ul>
          </details>
        </>
      )}
      <div className="gen-row">
        <select value={preset} onChange={e => setPreset(e.target.value)} aria-label="Preset">
          {catalog?.presets.map(p => <option key={p.name} value={p.name}>{p.label}</option>)}
        </select>
        <select value={par ?? ''} onChange={e => setPar(e.target.value ? Number(e.target.value) : null)} aria-label="Par">
          <option value="">Any par</option>
          {catalog?.pars.map(p => <option key={p} value={p}>Par {p}</option>)}
        </select>
      </div>
      <div className="gen-row">
        <button className="btn" disabled={!!busy} onClick={() => generate()}>Generate</button>
        <button className="btn ghost" disabled={!!busy} onClick={() => generate()} title="Next hole without rating (N)">
          Skip <kbd>N</kbd>
        </button>
      </div>
      {recent.length > 1 && (
        <select className="history" value="" aria-label="Recent holes" onChange={e => {
          const h = recent.find(r => r.id === e.target.value);
          if (h) open(h);
        }}>
          <option value="">Revisit a recent hole…</option>
          {recent.map(h => (
            <option key={h.id} value={h.id}>
              {h.rating === 'up' ? '👍' : h.rating === 'down' ? '👎' : '·'} {presetLabel(h.preset)} par {h.par}, {yards(h.lengthMeters)} yd ({h.seed})
            </option>
          ))}
        </select>
      )}
    </section>
  );
}
