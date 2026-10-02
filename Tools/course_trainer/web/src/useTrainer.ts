import { useCallback, useEffect, useRef, useState } from 'react';
import { api, type Catalog, type HoleSummary, type PoolProgress, type Rating, type Status } from './api';
import { loadHole, type HoleData } from './hole/loadHole';

export interface Draft { rating: Rating | null; tags: string[]; comment: string }
const EMPTY_DRAFT: Draft = { rating: null, tags: [], comment: '' };
const POLL_MS = 3000;

/** App state and server round-trips: next pool hole, ad-hoc generate, rate, retrain. */
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
  const [pool, setPool] = useState<PoolProgress | null>(null);
  /** No unseen pool hole is ready yet: the HUD shows a waiting card while `next` polls. */
  const [waiting, setWaiting] = useState(false);
  const toastTimer = useRef<number>(0);
  const pollTimer = useRef<number>(0);
  /** False once this view unmounts (logout, 401): late responses are dropped and polling stops for good. */
  const alive = useRef(true);
  useEffect(() => {
    alive.current = true;
    return () => { alive.current = false; window.clearTimeout(pollTimer.current); };
  }, []);

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
      const result = await fn();
      return alive.current ? result : undefined;
    } catch (e) {
      if (alive.current) setError(e instanceof Error ? e.message : String(e));
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

  /** Show a hole. `peek` (leaderboard): viewing it does not count as having seen it. */
  const open = useCallback((summary: HoleSummary, peek = false) => task('Building the hole…', async () => {
    window.clearTimeout(pollTimer.current);
    setWaiting(false);
    const data = await loadHole(summary, peek);
    if (!alive.current) return;
    setHole(data);
    setPreset(summary.preset);
    setDraft({ ...EMPTY_DRAFT, rating: summary.rating });
    window.history.replaceState(null, '', `?hole=${encodeURIComponent(summary.id)}`);
    await refreshLists(summary.preset);
  }), [task, refreshLists]);

  /** The main flow: your next unseen hole from the shared pool, or wait (polling) while the batch generates. */
  const nextRef = useRef<(poll?: boolean) => Promise<void>>(async () => {});
  const next = useCallback(async (poll = false) => {
    window.clearTimeout(pollTimer.current);
    const res = poll ? await api.next().catch(() => undefined) : await task('Finding your next hole…', api.next);
    if (!alive.current) return;
    if (res) setPool(res.pool);
    if (res?.hole) return void await open(res.hole);
    if (res || poll) {
      setWaiting(true);
      pollTimer.current = window.setTimeout(() => nextRef.current(true), POLL_MS);
    }
  }, [task, open]);
  nextRef.current = next;

  /** Advanced: one hole made now with a chosen preset / par (still counts as seen). */
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
    await next();
  }, [hole, draft, task, flash, next]);

  const retrain = useCallback(async () => {
    const s = await task('Retraining the taste model…', () => api.train(preset));
    if (s) { setStatus(s); flash(`Model retrained on ${s.trainedOn} ratings`); }
  }, [task, preset, flash]);

  // First load: ?hole=<id>, else the hole you were last looking at if unrated, else the next pool hole.
  // (Once, even under StrictMode.)
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
      const mine = ready.find(h => h.id === wanted);
      // A ?hole= that is not one of mine (a shared link, a peeked Top hole, then reload) is only peeked, like Top
      // holes: it can still be served to me later. Rating it marks it seen.
      const linked = mine ?? (wanted ? await api.hole(wanted).catch(() => undefined) : undefined);
      if (!alive.current) return;
      const start = linked ?? (ready[0] && !ready[0].rating ? ready[0] : undefined);
      if (start) await open(start, !mine && start === linked);
      else await next();
    })();
  }, []);

  return {
    catalog, status, hole, recent, preset, setPreset, par, setPar, draft, setDraft, busy, error, toast, pool, waiting,
    dismissError: () => setError(null), open, next, generate, submit, retrain,
  };
}

export type Trainer = ReturnType<typeof useTrainer>;
