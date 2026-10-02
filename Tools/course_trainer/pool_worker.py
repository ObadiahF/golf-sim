"""Fills pool batches in the background (never inside a request: Cloudflare cuts those at 100 s).

One daemon thread wakes on `kick()` (a new batch, startup) or every minute, and for each batch still generating:
retrains the model on everyone's latest votes (once per batch), then generates the missing positions. Up to
TRAINER_MAX_GENERATIONS holes are made at once, sharing the API's generation semaphore with ad-hoc Generate, in
worker processes (spawned: the GIL would otherwise keep a 24-core server on one core). Each hole lets the model
pick the preset and style (preference.choose_style with preset=None: Thompson sampling, so a batch leans toward
what people like while still exploring) from a fixed seed per position.
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
        """Fill every batch still generating; returns the number of holes made. Tests call this directly."""
        made = 0
        with self._busy:
            for batch in pool.generating(self.dsn):
                if batch.trained_on_votes is None:
                    trained = train_and_log(self.dsn, self.store, self.train_lock)
                    pool.set_trained(self.dsn, batch.id, trained["ratings"] if trained else 0)
                positions = pool.missing_positions(self.dsn, batch)
                with ThreadPoolExecutor(self.settings.max_generations) as slots:
                    done = sum(slots.map(lambda p: self._make(batch.id, p), positions))
                pool.mark_ready(self.dsn, batch.id)
                log(f"pool: batch {batch.id} ready ({done} new of {len(positions)} missing, size {batch.size})")
                made += done
        return made

    def _make(self, batch_id: int, position: int) -> bool:
        for attempt in range(ATTEMPTS):
            seed = pool.hole_seed(batch_id, position, attempt)
            try:
                with self.generations:
                    result = self._generate(seed)
            except RuntimeError:  # no playable layout for the chosen style: next seed
                continue
            except Exception:
                log(f"pool: batch {batch_id} position {position} failed\n{traceback.format_exc()}")
                return False
            self.checks.check(result["id"])  # tee-shot check as it joins the pool (new holes pass)
            pool.add_hole(self.dsn, batch_id, position, result["id"])
            return True
        return False

    def _generate(self, seed: int) -> dict:
        args = (None, None, seed, None, Path(self.settings.holes_dir), self.settings.gen_spacing, None, True)
        if self._processes is None:
            return generate_hole(*args)[0]
        try:
            return self._processes.submit(generate_hole, *args).result()[0]
        except BrokenProcessPool:  # a worker died (e.g. out of memory): start fresh ones and retry once
            log("pool: worker processes died, restarting them")
            self._processes = self._new_processes()
            return self._processes.submit(generate_hole, *args).result()[0]
