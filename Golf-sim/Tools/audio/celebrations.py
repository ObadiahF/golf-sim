"""Celebration sounds: a jingle for a birdie (bells), a bigger one for an eagle (bells over brass) and a fanfare for an
albatross or a hole-in-one (brass, timpani and a cymbal), plus the fireworks at the cup (a whistling launch, a boom
with crackle). All synthesised: tones, noise and envelopes."""
import numpy as np
from scipy import signal

from dsp import SR, band, bell, brass, decay, finish, high, low, modes, noise, note_hz, place, t_axis


def room(x, rng, seconds=1.2, wet=0.22):
    """A little hall: x convolved with a decaying noise burst, mixed under the dry sound."""
    ir = low(noise(seconds, rng), 6000) * decay(seconds, seconds / 5)
    tail = signal.fftconvolve(x, ir)[:len(x)]
    tail *= np.max(np.abs(x)) / max(1e-9, np.max(np.abs(tail)))
    return x + tail * wet


def timpani(hz, seconds, rng):
    return (modes(seconds, [(hz, 0.5, 1.0), (hz * 1.5, 0.3, 0.5), (hz * 1.98, 0.2, 0.3)], rng, detune=0.003) +
            low(noise(seconds, rng), 500) * decay(seconds, 0.02) * 0.6)


def cymbal_swell(seconds, rng):
    t = t_axis(seconds)
    return high(noise(seconds, rng), 5000) * (t / seconds) ** 2.5 * np.exp(-np.clip(t - seconds * 0.95, 0, None) / 0.05)


def cymbal_crash(seconds, rng):
    return band(noise(seconds, rng), 3000, 16000) * decay(seconds, 0.6, attack=0.002)


def jingle_birdie(rng):
    """Three quick rising bells and a sparkle on top."""
    d = 1.6
    out = np.zeros(int(d * SR))
    for i, n in enumerate(["C6", "E6", "G6"]):
        place(out, bell(note_hz(n), 1.2, rng), i * 0.09)
    place(out, bell(note_hz("C7"), 1.2, rng, bright=1.3) * 1.1, 0.27)
    place(out, bell(note_hz("G6"), 1.0, rng) * 0.5, 0.27)
    return finish(room(out, rng), peak=0.7, fade=0.15)


def jingle_eagle(rng):
    """A run of bells up two octaves over a warm brass chord that swells in."""
    d = 2.6
    out = np.zeros(int(d * SR))
    run = ["G5", "C6", "E6", "G6", "C7", "E7"]
    for i, n in enumerate(run):
        place(out, bell(note_hz(n), 1.3, rng) * (0.8 + 0.05 * i), i * 0.075)
    for n in ["C4", "G4", "C5", "E5"]:
        place(out, brass(note_hz(n), 1.9, rng, attack=0.12, release=0.6) * 0.22, 0.42)
    place(out, bell(note_hz("C7"), 1.6, rng, bright=1.4), 0.46)
    return finish(room(out, rng, wet=0.28), peak=0.75, fade=0.2)


def jingle_ace(rng):
    """The fanfare: 'da-da-da DAAA' on the brass, a timpani roll into a big chord, a cymbal crash and bells."""
    d = 3.8
    out = np.zeros(int(d * SR))
    beat = 0.13
    for i in range(3):
        for n in ["G4", "B4", "D5"]:
            place(out, brass(note_hz(n), beat * 0.9, rng, attack=0.015, release=0.04) * 0.3, i * beat)
    hit = 3 * beat
    for n in ["C4", "G4", "C5", "E5", "G5", "C6"]:
        place(out, brass(note_hz(n), 2.6, rng, attack=0.025, release=0.9) * 0.24, hit)
    for i in range(10):  # the roll into the chord
        place(out, timpani(note_hz("G2"), 0.4, rng) * (0.15 + 0.05 * i), i * 0.035)
    place(out, timpani(note_hz("C2"), 2.0, rng) * 1.1, hit)
    place(out, cymbal_swell(hit, rng) * 0.25, 0.0)
    place(out, cymbal_crash(2.5, rng) * 0.35, hit)
    for i, n in enumerate(["C7", "E7", "G7", "C8"]):
        place(out, bell(note_hz(n), 1.4, rng) * 0.35, hit + 0.15 + i * 0.09)
    return finish(room(out, rng, wet=0.3), peak=0.8, fade=0.3)


def firework_launch(rng):
    """The rocket going up: a whistle sliding up through the noise of its trail."""
    d = 1.1
    t = t_axis(d)
    hz = 900 + 1900 * (t / d) ** 0.7
    whistle = np.sin(2 * np.pi * np.cumsum(hz) / SR) * (0.6 + 0.4 * np.sin(2 * np.pi * 23 * t))
    hiss = band(noise(d, rng), 1500, 7000)
    env = np.clip(t / 0.05, 0, 1) * np.clip((d - t) / 0.15, 0, 1)
    return finish((whistle * 0.5 + hiss * 0.5) * env, peak=0.5)


def firework_burst(rng):
    """The shell bursting: a deep boom, then crackling sparks that spread and fade."""
    d = 2.4
    boom = low(noise(d, rng), 180, 4) * decay(d, 0.25, attack=0.003) * 3.0 + modes(d, [(48, 0.35, 1.0), (75, 0.2, 0.5)], rng)
    crackle = np.zeros(int(d * SR))
    for _ in range(260):
        at = 0.15 + rng.exponential(0.45)
        i = int(at * SR)
        if i < len(crackle) - 400:
            crackle[i:i + 400] += rng.uniform(0.3, 1.0) * np.exp(-np.arange(400) / rng.uniform(20, 90)) * rng.choice([-1, 1])
    crackle = high(crackle, 1800) * np.clip(1.2 - t_axis(d) / d, 0, 1)
    return finish(room(boom + crackle * 0.6, rng, seconds=1.8, wet=0.35), peak=0.85, fade=0.2)


SOUNDS = {
    "jingle_birdie": (jingle_birdie, 1), "jingle_eagle": (jingle_eagle, 1), "jingle_ace": (jingle_ace, 1),
    "firework_launch": (firework_launch, 2), "firework_burst": (firework_burst, 3),
}
