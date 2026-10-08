"""Fills pool batches in the background (never inside a request: Cloudflare cuts those at 100 s).

One daemon thread wakes on `kick()` (a new batch, startup) or every minute, and for each batch still generating:
retrains the model on everyone's latest votes (once per batch), then generates the missing positions. Up to
TRAINER_MAX_GENERATIONS holes are made at once, sharing the API's generation semaphore with ad-hoc Generate, in
worker processes (spawned: the GIL would otherwise keep a 24-core server on one core). Each hole of a preference
batch lets the model pick the preset and style (preference.choose_style with preset=None: Thompson sampling, so a
batch leans toward what people like while still exploring) from a fixed seed per position; a stock batch's holes
are all its preset (the model still picks the style).
Stock: once nothing is generating, a preset with fewer than TRAINER_POOL_MIN_STOCK playable game holes
(random_holes.preset_counts) gets a stock batch, the most-short preset first (pool.start_stock), each preset at most
once per run_pending, so a preset whose holes keep failing can't spin the worker.
"""
from __future__ import annotations

import threading
import traceback
from concurrent.futures import ProcessPoolExecutor, ThreadPoolExecutor
from concurrent.futures.process import BrokenProcessPool
from multiprocessing import get_context
from pathlib import Path

import db
import pool
from config import Settings, log
from hole_checks import HoleChecks
from gen_hole import generate_hole, train_and_save
from pg_store import PostgresStore
from random_holes import preset_counts
from style import PRESETS

ATTEMPTS = 4  # seeds tried per position before it is left empty


def train_and_log(dsn: str, store: PostgresStore, lock: threading.Lock, user_id: int | None = None,
                  only: str | None = None) -> dict | None:
    """Retrain from everyone's latest votes (or `only` one user's), save, log to training_runs. None: no votes."""
    with lock:  # one retrain at a time (they all write the one model file)
        trained = train_and_save(store, only)
        if trained is not None:
            db.log_training(dsn, user_id, only, trained["ratings"], trained)
    return trained


class PoolWorker:
    def __init__(self, settings: Settings, store: PostgresStore, generations: threading.Semaphore,
                 train_lock: threading.Lock, checks: HoleChecks):
        self.settings = settings
        self.dsn = settings.database_url
        self.store = store
        self.generations = generations
        self.train_lock = train_lock
        self.checks = checks
        self._wake = threading.Event()
        self._busy = threading.Lock()
        self._thread: threading.Thread | None = None
        self._processes: ProcessPoolExecutor | None = None

    def start(self) -> None:
        """Make sure a batch exists and start the background thread (unless settings.pool_autorun is off)."""
        batch, created = pool.ensure_batch(self.dsn, self.settings.pool_batch_size)
        if created:
            log(f"pool: started batch {batch.id} ({batch.size} holes)")
        if not self.settings.pool_autorun or self._thread is not None:
            return
        self._processes = self._new_processes()
        self._thread = threading.Thread(target=self._loop, name="pool-worker", daemon=True)
        self._thread.start()
        self.kick()

    def _new_processes(self) -> ProcessPoolExecutor:
        return ProcessPoolExecutor(self.settings.max_generations, mp_context=get_context("spawn"))

    def stop(self) -> None:
        if self._processes is not None:
            self._processes.shutdown(wait=False, cancel_futures=True)

    def kick(self) -> None:
        self._wake.set()

    def _loop(self) -> None:
        while True:
            self._wake.wait(60)
            self._wake.clear()
            try:
                self.run_pending()
            except Exception:  # keep the worker alive; the next wake retries
                log(f"pool: worker error\n{traceback.format_exc()}")

    def run_pending(self) -> int:
        """Fill every batch still generating, then stock batches while a preset is short (each preset at most once);
        returns the number of holes made. Tests call this directly."""
        made = 0
        stocked: set[str] = set()
        with self._busy:
            while True:
                for batch in pool.generating(self.dsn):
                    made += self._fill(batch)
                batch = self._start_stock(stocked)
                if batch is None:
                    return made
                stocked.add(batch.preset)

    def _fill(self, batch: pool.Batch) -> int:
        if batch.trained_on_votes is None:
            trained = train_and_log(self.dsn, self.store, self.train_lock)
            pool.set_trained(self.dsn, batch.id, trained["ratings"] if trained else 0)
        positions = pool.missing_positions(self.dsn, batch)
        with ThreadPoolExecutor(self.settings.max_generations) as slots:
            done = sum(slots.map(lambda p: self._make(batch, p), positions))
        pool.mark_ready(self.dsn, batch.id)
        kind = f"stock batch ({batch.preset})" if batch.preset else "batch"
        log(f"pool: {kind} {batch.id} ready ({done} new of {len(positions)} missing, size {batch.size})")
        return done

    def _start_stock(self, skip: set[str]) -> pool.Batch | None:
        """A stock batch for the most-short preset not in `skip` (pool.start_stock), or None."""
        stock = self.settings.pool_min_stock
        if stock <= 0:
            return None
        counts = preset_counts(self.dsn, self.checks.holes)
        short = [s for s in pool.shortfalls(counts, stock, PRESETS) if s[0] not in skip]
        batch = pool.start_stock(self.dsn, short, self.settings.pool_batch_size) if short else None
        if batch is not None:
            log(f"pool: started stock batch {batch.id} ({batch.size} {batch.preset} holes; "
                f"{counts[batch.preset]} of {stock} playable)")
        return batch

    def _make(self, batch: pool.Batch, position: int) -> bool:
        for attempt in range(ATTEMPTS):
            seed = pool.hole_seed(batch.id, position, attempt)
            try:
                with self.generations:
                    result = self._generate(seed, batch.preset)
            except RuntimeError:  # no playable layout for the chosen style: next seed
                continue
            except Exception:
                log(f"pool: batch {batch.id} position {position} failed\n{traceback.format_exc()}")
                return False
            self.checks.check(result["id"])  # playability check as it joins the pool (new holes pass)
            pool.add_hole(self.dsn, batch.id, position, result["id"], result["preset"])
            return True
        return False

    def _generate(self, seed: int, preset: str | None) -> dict:
        args = (preset, None, seed, None, Path(self.settings.holes_dir), self.settings.gen_spacing, None, True)
        if self._processes is None:
            return generate_hole(*args)[0]
        try:
            return self._processes.submit(generate_hole, *args).result()[0]
        except BrokenProcessPool:  # a worker died (e.g. out of memory): start fresh ones and retry once
            log("pool: worker processes died, restarting them")
            self._processes = self._new_processes()
            return self._processes.submit(generate_hole, *args).result()[0]
