#!/usr/bin/env python3
"""Synthesises the golf sim's club strikes, ball landings, replay sting and celebrations as 44.1 kHz 16-bit mono WAVs.

    python3 Tools/audio/synth_sounds.py            # writes Assets/GolfSim/Game/Audio/Synth/*.wav

Everything here is generated from noise, filters, resonators and envelopes (no recordings), so it is ours to use.
The crowd, ambience, trees, rocks, water, cup and UI sounds are CC0 recordings (Assets/GolfSim/Game/Audio/CC0/SOURCES.txt).
Club strikes (strikes.py) come per club family and strike quality, e.g. strike_iron_thin_2; the celebrations
(jingles and fireworks) are in celebrations.py. Each sound gets a few variations (different random seeds and small
parameter changes); AudioCatalog picks one at random and GameAudio varies volume and pitch on top.
"""
import glob
import os

import numpy as np
from scipy import signal

import celebrations
import strikes
from dsp import OUT, SR, band, decay, finish, high, low, mix, modes, noise, t_axis, write


# ---- ball on the ground ----

def land_grass(rng):
    d = 0.3
    thud = low(noise(d, rng), 380, 4) * decay(d, 0.025, attack=0.002) * 1.3
    body = modes(d, [(rng.uniform(85, 120), 0.04, 0.6), (rng.uniform(180, 230), 0.02, 0.3)], rng)
    blades = high(noise(d, rng), 3000) * decay(d, 0.02, attack=0.001) * 0.06
    return finish(thud + body + blades, peak=0.8)


def land_green(rng):
    d = 0.25
    thock = low(noise(d, rng), 900, 4) * decay(d, 0.014, attack=0.0008)
    body = modes(d, [(rng.uniform(140, 180), 0.03, 0.6), (rng.uniform(420, 520), 0.012, 0.4)], rng)
    return finish(thock + body, peak=0.8)


def land_sand(rng):
    """A muffled thump that sprays sand: grainy noise, many tiny grains decaying over ~200 ms."""
    d = 0.5
    thump = low(noise(d, rng), 300, 4) * decay(d, 0.03, attack=0.002) * 1.2
    spray = band(noise(d, rng), 600, 6000) * decay(d, 0.09, attack=0.004)
    grains = np.zeros(int(d * SR))
    for _ in range(140):
        i = int(abs(rng.exponential(0.07)) * SR)
        if i < len(grains):
            grains[i] += rng.uniform(-1, 1)
    grains = band(grains, 2000, 9000) * 2.5
    return finish(thump + spray * 0.5 + grains * 0.4, peak=0.8)


# ---- the replay sting ----

def swoosh(rng, d=0.65, lo=350, hi=4200, peak_at=0.55):
    """Air rushing past: noise through a band-pass whose centre sweeps up and back down, swelling in and out."""
    n = int(d * SR)
    x = rng.standard_normal(n)
    t = np.linspace(0, 1, n)
    centre = lo + (hi - lo) * np.sin(np.pi * np.clip(t / (peak_at * 2), 0, 1)) ** 2
    out = np.zeros(n)
    block = 256
    zi = None
    for i in range(0, n, block):
        c = centre[min(i + block // 2, n - 1)]
        sos = signal.butter(2, [c * 0.6, min(c * 1.6, SR / 2 - 100)], btype="bandpass", fs=SR, output="sos")
        if zi is None:
            zi = signal.sosfilt_zi(sos) * 0
        y, zi = signal.sosfilt(sos, x[i:i + block], zi=zi)
        out[i:i + block] = y
    env = np.sin(np.pi * np.clip(t / peak_at, 0, 1) / 2) ** 2 * np.exp(-np.clip(t - peak_at, 0, 1) * 7)
    return finish(out * env, peak=0.6, fade=0.03)


def replay_sting(rng):
    """The replay's transition: a quick whoosh with a soft low hit under it."""
    w = swoosh(rng, 0.5, 500, 6000, 0.35)
    hit = modes(0.6, [(55, 0.15, 1.0), (110, 0.08, 0.4)], rng) * np.clip(t_axis(0.6) / 0.005, 0, 1)
    return finish(mix(w, np.concatenate([np.zeros(int(0.16 * SR)), hit * 0.5])), peak=0.6, fade=0.05)

SOUNDS = {
    "land_grass": (land_grass, 3), "land_green": (land_green, 3), "land_sand": (land_sand, 3),
    "replay_sting": (replay_sting, 1),
    **celebrations.SOUNDS,
}


def main():
    for old in glob.glob(os.path.join(OUT, "strike_*.wav")):  # strike names change with the families and qualities
        os.remove(old)
    count = 0
    for name, (make, n) in SOUNDS.items():
        for i in range(n):
            rng = np.random.default_rng(sum(map(ord, name)) * 31 + i)  # fixed seeds: the same clips every run
            write(f"{name}_{i + 1}", make(rng))
            count += 1
    for name, clips in strikes.all_strikes().items():
        for i, clip in enumerate(clips):
            write(f"{name}_{i + 1}", clip)
            count += 1
    print(f"wrote {count} clips to {os.path.abspath(OUT)}")


if __name__ == "__main__":
    main()
