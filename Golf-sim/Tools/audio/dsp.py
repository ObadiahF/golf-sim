"""Building blocks for the synthesised sounds: noise, filters, envelopes, struck-object modes, tones and WAV output."""
import os
import wave

import numpy as np
from scipy import signal

SR = 44100
OUT = os.path.join(os.path.dirname(__file__), "..", "..", "Assets", "GolfSim", "Game", "Audio", "Synth")


def t_axis(seconds):
    return np.arange(int(seconds * SR)) / SR


def noise(seconds, rng):
    return rng.standard_normal(int(seconds * SR))


def band(x, lo, hi, order=2):
    sos = signal.butter(order, [lo, min(hi, SR / 2 - 100)], btype="bandpass", fs=SR, output="sos")
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


def delayed(x, seconds):
    """x starting `seconds` later (same length, the tail cut)."""
    n = int(seconds * SR)
    return np.concatenate([np.zeros(n), x])[:len(x)]


def modes(seconds, partials, rng, detune=0.01):
    """Sum of damped sine modes: [(hz, tau, gain)]. Struck objects (wood, plastic, metal) are made of these."""
    t = t_axis(seconds)
    out = np.zeros_like(t)
    for hz, tau, gain in partials:
        f = hz * (1 + rng.uniform(-detune, detune))
        out += gain * np.sin(2 * np.pi * f * t + rng.uniform(0, 6.28)) * np.exp(-t / tau)
    return out


def click(seconds, rng, lo=2000, hi=12000, tau=0.0015):
    return band(noise(seconds, rng), lo, hi) * decay(seconds, tau, attack=0.0001)


def pad(x, seconds):
    n = int(seconds * SR)
    return np.pad(x, (0, max(0, n - len(x))))[:n]


def mix(*parts):
    n = max(len(p) for p in parts)
    return sum(pad(p, n / SR) for p in parts)


def place(out, x, at):
    """Adds x into out starting at `at` seconds (cut at the end of out)."""
    i = int(at * SR)
    if i >= len(out):
        return out
    n = min(len(x), len(out) - i)
    out[i:i + n] += x[:n]
    return out


def finish(x, peak=0.89, fade=0.01):
    x = x - np.mean(x)
    n = int(fade * SR)
    if n and len(x) > n:
        x[-n:] *= np.linspace(1, 0, n)
    m = np.max(np.abs(x))
    return x / m * peak if m > 0 else x


# ---- tones (the celebration jingles) ----

def note_hz(name):
    """'C5', 'F#4', 'Bb3' -> Hz (A4 = 440)."""
    steps = {"C": -9, "D": -7, "E": -5, "F": -4, "G": -2, "A": 0, "B": 2}[name[0]]
    rest = name[1:]
    if rest[0] in "#b":
        steps += 1 if rest[0] == "#" else -1
        rest = rest[1:]
    return 440.0 * 2 ** ((steps + 12 * (int(rest) - 4)) / 12)


def bell(hz, seconds, rng, bright=1.0):
    """A glockenspiel / celesta tone: inharmonic partials that ring and fade, the high ones first."""
    return modes(seconds, [(hz, 0.55, 1.0), (hz * 2.76, 0.18, 0.45 * bright), (hz * 5.4, 0.07, 0.25 * bright),
                           (hz * 8.93, 0.03, 0.12 * bright)], rng, detune=0.002) * np.clip(t_axis(seconds) / 0.002, 0, 1)


def brass(hz, seconds, rng, attack=0.04, release=0.25):
    """A bright brass-section note: a few detuned sawtooths, the filter opening with the attack, a little vibrato."""
    t = t_axis(seconds)
    vib = 1 + 0.004 * np.sin(2 * np.pi * 5.5 * t) * np.clip((t - 0.15) / 0.2, 0, 1)
    out = np.zeros_like(t)
    for detune in (-0.006, 0.0, 0.007):
        phase = np.cumsum(hz * (1 + detune) * vib) / SR
        out += 2 * (phase - np.floor(phase + 0.5))
    env = np.clip(t / attack, 0, 1) * np.clip((seconds - t) / release, 0, 1)
    bright = low(out, 900 + 3500 * min(1.0, 0.04 / attack), 2)
    return bright * env * (0.85 + 0.15 * np.exp(-t / 0.08))


def write(name, x):
    os.makedirs(OUT, exist_ok=True)
    data = (np.clip(x, -1, 1) * 32767).astype(np.int16)
    with wave.open(os.path.join(OUT, name + ".wav"), "wb") as w:
        w.setnchannels(1)
        w.setsampwidth(2)
        w.setframerate(SR)
        w.writeframes(data.tobytes())
