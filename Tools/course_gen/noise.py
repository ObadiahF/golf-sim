"""Seeded fractal value noise, evaluated at arbitrary points (vectorised)."""
from __future__ import annotations

import numpy as np

LATTICE = 256


class Fbm:
    def __init__(self, seed: int, wavelength: float, octaves: int = 5, gain: float = 0.5):
        rng = np.random.default_rng(seed)
        self.wavelength = wavelength
        self.octaves = octaves
        self.gain = gain
        self.tables = rng.uniform(-1, 1, (8, LATTICE, LATTICE))  # one table per salt

    def _value(self, x: np.ndarray, y: np.ndarray, table: np.ndarray) -> np.ndarray:
        x0, y0 = np.floor(x), np.floor(y)
        tx, ty = x - x0, y - y0
        tx, ty = tx * tx * (3 - 2 * tx), ty * ty * (3 - 2 * ty)  # smoothstep
        i0, j0 = x0.astype(np.int64) % LATTICE, y0.astype(np.int64) % LATTICE
        i1, j1 = (i0 + 1) % LATTICE, (j0 + 1) % LATTICE
        a = table[j0, i0] + (table[j0, i1] - table[j0, i0]) * tx
        b = table[j1, i0] + (table[j1, i1] - table[j1, i0]) * tx
        return a + (b - a) * ty

    def __call__(self, x, y, salt: int = 0) -> np.ndarray:
        """Roughly -1..1 at meters (x, y); `salt` selects an independent field."""
        x, y = np.asarray(x, dtype=np.float64), np.asarray(y, dtype=np.float64)
        table = self.tables[salt % len(self.tables)]
        total, amp, norm, freq = np.zeros_like(x), 1.0, 0.0, 1.0 / self.wavelength
        for o in range(self.octaves):
            total += amp * self._value(x * freq + 17.3 * o, y * freq + 41.7 * o, table)
            norm += amp
            amp *= self.gain
            freq *= 2.0
        return total / norm
