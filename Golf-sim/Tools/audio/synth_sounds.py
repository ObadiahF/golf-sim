#!/usr/bin/env python3
"""Synthesises the golf sim's club strikes, ball landings and replay sting as 44.1 kHz 16-bit mono WAVs.

    python3 Tools/audio/synth_sounds.py            # writes Assets/GolfSim/Game/Audio/Synth/*.wav

Everything here is generated from noise, filters, resonators and envelopes (no recordings), so it is ours to use.
The crowd, ambience, trees, rocks, water, cup and UI sounds are CC0 recordings (Assets/GolfSim/Game/Audio/CC0/SOURCES.txt).
Each sound gets a few variations (different random seeds and small parameter changes); AudioCatalog picks one
at random and GameAudio varies volume and pitch on top.
"""
import os
import wave

import numpy as np
from scipy import signal

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "GolfSim", "Game", "Audio", "Synth")


# ---- building blocks ----

def t_axis(seconds):
    return np.arange(int(seconds * SR)) / SR


def noise(seconds, rng):
    return rng.standard_normal(int(seconds * SR))


def band(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, hi], btype="bandpass", fs=SR, output="sos")
    return signal.sosfilt(sos, x)


def low(x, hz, order=2):
    return signal.sosfilt(signal.butter(order, hz, btype="lowpass", fs=SR, output="sos"), x)


def high(x, hz, order=2):
    return signal.sosfilt(signal.butter(order, hz, btype="highpass", fs=SR, output="sos"), x)


def decay(seconds, tau, attack=0.0005):
    t = t_axis(seconds)
    env = np.exp(-t / tau)
    if attack > 0:
        env *= np.clip(t / attack, 0, 1)
    return env


def modes(seconds, partials, rng, detune=0.01):
    """Sum of damped sine modes: [(hz, tau, gain)]. Struck objects (wood, plastic, metal) are made of these."""
    t = t_axis(seconds)
    out = np.zeros_like(t)
    for hz, tau, gain in partials:
        f = hz * (1 + rng.uniform(-detune, detune))
        out += gain * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-t / tau)
    return out


def click(seconds, rng, lo=2000, hi=12000, tau=0.0015):
    return band(noise(seconds, rng), lo, min(hi, SR / 2 - 100)) * decay(seconds, tau, attack=0.0001)


def pad(x, seconds):
    n = int(seconds * SR)
    return np.pad(x, (0, max(0, n - len(x))))[:n]


def mix(*parts):
    n = max(len(p) for p in parts)
    return sum(pad(p, n / SR) for p in parts)


def finish(x, peak=0.89, fade=0.01):
    x = x - np.mean(x)
    n = int(fade * SR)
    if n and len(x) > n:
        x[-n:] *= np.linspace(1, 0, n)
    m = np.max(np.abs(x))
    return x / m * peak if m > 0 else x


def write(name, x):
    os.makedirs(OUT, exist_ok=True)
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())


# ---- club strikes ----

def driver(rng):
    """Titanium driver: a hard crack with a bright, hollow ring of the clubhead, and a little air after."""
    d = 0.45
    crack = click(d, rng, 1500, 14000, 0.002) * 1.4
    ring = modes(d, [(rng.uniform(3300, 3700), 0.035, 0.5), (rng.uniform(4700, 5200), 0.022, 0.35),
                     (rng.uniform(2300, 2600), 0.05, 0.3), (rng.uniform(6500, 7200), 0.012, 0.2)], rng)
    body = modes(d, [(rng.uniform(800, 950), 0.02, 0.6), (rng.uniform(1500, 1700), 0.015, 0.3)], rng)
    thump = low(noise(d, rng), 400) * decay(d, 0.012) * 2.0
    air = band(noise(d, rng), 800, 5000) * decay(d, 0.12, attack=0.02) * 0.06
    return finish(crack + ring * 0.8 + body + thump + air)


def iron(rng):
    """Forged iron: a tight, crisp click, a short steely ping, then the turf of the divot."""
    d = 0.4
    crisp = click(d, rng, 2500, 16000, 0.0012) * 1.6
    ping = modes(d, [(rng.uniform(2700, 3100), 0.012, 0.45), (rng.uniform(4200, 4600), 0.008, 0.3),
                     (rng.uniform(1900, 2100), 0.015, 0.25)], rng)
    knock = modes(d, [(rng.uniform(650, 750), 0.012, 0.5)], rng)
    turf_env = decay(d, 0.045, attack=0.006)
    turf_env = np.roll(turf_env, int(0.004 * SR))
    turf = band(noise(d, rng), 150, 2200) * turf_env * 0.5
    return finish(crisp + ping + knock + turf)


def putter(rng):
    """Putter: a soft, short 'tock' of a milled face on the cover."""
    d = 0.25
    tock = modes(d, [(rng.uniform(1050, 1250), 0.018, 0.8), (rng.uniform(2300, 2600), 0.008, 0.35),
                     (rng.uniform(520, 600), 0.012, 0.3)], rng)
    tick = click(d, rng, 2000, 9000, 0.0008) * 0.35
    return finish(tock + tick, peak=0.7)


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
    "strike_driver": (driver, 4), "strike_iron": (iron, 4), "strike_putter": (putter, 3),
    "land_grass": (land_grass, 3), "land_green": (land_green, 3), "land_sand": (land_sand, 3),
    "replay_sting": (replay_sting, 1),
}


def main():
    for name, (make, count) in SOUNDS.items():
        for i in range(count):
            rng = np.random.default_rng(sum(map(ord, name)) * 31 + i)  # fixed seeds: the same clips every run
            write(f"{name}_{i + 1}", make(rng))
    print(f"wrote {sum(c for _, c in SOUNDS.values())} clips to {os.path.abspath(OUT)}")


if __name__ == "__main__":
    main()
