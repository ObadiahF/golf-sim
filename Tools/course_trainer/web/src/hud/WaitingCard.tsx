import type { Trainer } from '../useTrainer';
import { RecentHoles } from './HolePanel';

/**
 * Shown while you have seen every ready pool hole. A card, not a full-screen overlay: the top bar, Skip, Advanced ›
 * Generate and history stay usable while `next` polls in the background. Rating is off meanwhile (RatePanel): the hole
 * still on screen is the one you just skipped or rated; revisiting a hole from the list here opens it for rating.
 */
export function WaitingCard({ trainer, onTop }: { trainer: Trainer; onTop: () => void }) {
  const { pool, recent } = trainer;
  return (
    <section className="panel card waiting-card" role="status">
      <div className="ball" />
      {pool?.capped ? (
        <>
          <strong>No new holes until more are rated</strong>
          <small>You've seen every hole in the pool, and many of them have no votes yet. The next batch starts once
            people rate more: revisit a hole you skipped, or browse the top holes.</small>
        </>
      ) : (
        <>
          <strong>Generating new holes…</strong>
          <small>{pool ? `${pool.ready} of ${pool.size} ready in batch ${pool.batch}; ` : ''}you've seen every one so far.
            The next appears here on its own.</small>
        </>
      )}
      {recent.length > 0 && <RecentHoles trainer={trainer} />}
      <button className="btn small" onClick={onTop}>Browse the top holes meanwhile</button>
    </section>
  );
}
