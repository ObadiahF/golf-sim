import { useCallback, useEffect, useRef, useState } from 'react';
import { api, type Catalog, type HoleSummary, type Rating, type Status } from './api';
import { loadHole, type HoleData } from './hole/loadHole';

export interface Draft { rating: Rating | null; tags: string[]; comment: string }
const EMPTY_DRAFT: Draft = { rating: null, tags: [], comment: '' };

/** App state and server round-trips: load / generate holes, rate, retrain. */
export function useTrainer() {
  const [catalog, setCatalog] = useState<Catalog | null>(null);
  const [status, setStatus] = useState<Status | null>(null);
  const [hole, setHole] = useState<HoleData | null>(null);
  const [recent, setRecent] = useState<HoleSummary[]>([]);
  const [preset, setPreset] = useState('parkland');
  const [par, setPar] = useState<number | null>(null);
  const [draft, setDraft] = useState<Draft>(EMPTY_DRAFT);
  const [busy, setBusy] = useState<string | null>('Warming up…');
  const [error, setError] = useState<string | null>(null);
  const [toast, setToast] = useState<string | null>(null);
  const toastTimer = useRef<number>(0);

  const flash = useCallback((message: string) => {
    setToast(message);
    window.clearTimeout(toastTimer.current);
    toastTimer.current = window.setTimeout(() => setToast(null), 2600);
  }, []);

  /** Run one server task with a loading message; errors land in the banner. */
  const task = useCallback(async <T,>(message: string, fn: () => Promise<T>): Promise<T | undefined> => {
    setBusy(message);
    setError(null);
    try {
      return await fn();
    } catch (e) {
      setError(e instanceof Error ? e.message : String(e));
      return undefined;
    } finally {
      setBusy(null);
    }
  }, []);

  const refreshLists = useCallback(async (forPreset: string) => {
    const [s, r] = await Promise.all([api.status(forPreset), api.recent(30)]);
    setStatus(s);
    setRecent(r);
  }, []);

  const open = useCallback((summary: HoleSummary) => task('Building the hole…', async () => {
    const data = await loadHole(summary);
    setHole(data);
    setPreset(summary.preset);
    setDraft({ ...EMPTY_DRAFT, rating: summary.rating });
    window.history.replaceState(null, '', `?hole=${encodeURIComponent(summary.id)}`);
    await refreshLists(summary.preset);
  }), [task, refreshLists]);

  const generate = useCallback(async (forPreset = preset, forPar = par) => {
    const label = catalog?.presets.find(p => p.name === forPreset)?.label ?? forPreset;
    const summary = await task(`Generating a ${label} hole…`, () => api.generate(forPreset, forPar));
    if (summary) await open(summary);
  }, [catalog, preset, par, task, open]);

  const submit = useCallback(async () => {
    if (!hole || !draft.rating) return;
    const done = await task('Saving your rating…', () =>
      api.rate(hole.summary.id, draft.rating!, draft.comment, draft.tags));
    if (!done) return;
    setStatus(done.status);
    flash(`${draft.rating === 'up' ? '👍' : '👎'} saved: ${done.status.ratings} ratings so far`);
    await generate(hole.summary.preset, par);
  }, [hole, draft, task, flash, generate, par]);

  const retrain = useCallback(async () => {
    const s = await task('Retraining the taste model…', () => api.train(preset));
    if (s) { setStatus(s); flash(`Model retrained on ${s.trainedOn} ratings`); }
  }, [task, preset, flash]);

  // First load: ?hole=<id>, else the newest unrated hole, else a fresh one. (Once, even under StrictMode.)
  const started = useRef(false);
  useEffect(() => {
    if (started.current) return;
    started.current = true;
    (async () => {
      const ready = await task('Connecting…', async () => {
        const [c, r] = await Promise.all([api.catalog(), api.recent(30)]);
        setCatalog(c);
        setRecent(r);
        return r;
      });
      if (!ready) return;
      const wanted = new URLSearchParams(window.location.search).get('hole');
      const start = ready.find(h => h.id === wanted) ?? ready.find(h => !h.rating);
      if (start) await open(start);
      else {
        const summary = await task('Generating your first hole…', () => api.generate('parkland', null));
        if (summary) await open(summary);
      }
    })();
  }, []);

  return {
    catalog, status, hole, recent, preset, setPreset, par, setPar, draft, setDraft, busy, error, toast,
    dismissError: () => setError(null), open, generate, submit, retrain,
  };
}

export type Trainer = ReturnType<typeof useTrainer>;
