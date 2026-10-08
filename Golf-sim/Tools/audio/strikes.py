"""Club strikes: one model of a clubhead hitting a ball, tuned per club family and per strike quality.

A strike is a sharp crack (the faces meeting), the ring of the clubhead (damped modes: a titanium driver rings high
and hollow, a forged iron gives a short steely ping), the knock of its body, a low thump, and for clubs that take
turf the divot after the ball. How well it was struck changes the mix:
  pure   - flush: a sharper, louder crack and a longer, cleaner ring; barely any turf
  solid  - a good, ordinary strike
  thin   - off the leading edge: a harsh high click, a stinging buzz of the shaft, no turf
  fat    - the club hits the ground first: a dull thud of turf before a muffled, ringless contact
  toe    - off the toe or heel: a dead, low clunk with a short ring and a little rattle
The putter has pure, solid and toe only.
"""
import numpy as np

from dsp import SR, band, click, decay, delayed, finish, low, modes, noise, t_axis

# Per family: duration; crack band / decay / gain; ring modes (hz range, tau, gain); body modes; thump; turf.
FAMILIES = {
    "driver": dict(d=0.55, crack=(1500, 14000, 0.0024, 1.5),
                   ring=[((3300, 3700), 0.045, 0.55), ((4700, 5200), 0.028, 0.4), ((2300, 2600), 0.06, 0.35),
                         ((6500, 7200), 0.015, 0.22), ((1150, 1300), 0.035, 0.25)],
                   body=[((800, 950), 0.02, 0.6), ((1500, 1700), 0.015, 0.3)],
                   thump=(400, 0.014, 2.2), turf=0.0, air=0.07),
    "wood": dict(d=0.45, crack=(1800, 14000, 0.0019, 1.5),
                 ring=[((3900, 4300), 0.028, 0.45), ((5600, 6100), 0.017, 0.3), ((2700, 3000), 0.035, 0.3)],
                 body=[((900, 1050), 0.018, 0.6), ((1700, 1900), 0.012, 0.3)],
                 thump=(450, 0.012, 1.8), turf=0.18, air=0.05),
    "iron": dict(d=0.42, crack=(2500, 16000, 0.0012, 1.6),
                 ring=[((2700, 3100), 0.013, 0.45), ((4200, 4600), 0.009, 0.3), ((1900, 2100), 0.016, 0.25)],
                 body=[((650, 750), 0.012, 0.5)],
                 thump=(350, 0.01, 1.2), turf=0.55, air=0.0),
    "wedge": dict(d=0.4, crack=(3000, 17000, 0.0009, 1.5),
                  ring=[((3300, 3700), 0.008, 0.4), ((5200, 5700), 0.006, 0.25)],
                  body=[((780, 880), 0.009, 0.5), ((1400, 1550), 0.007, 0.25)],
                  thump=(320, 0.008, 1.0), turf=0.85, air=0.0),
    "putter": dict(d=0.28, crack=(2000, 9000, 0.0008, 0.35),
                   ring=[((2300, 2600), 0.009, 0.35)],
                   body=[((1050, 1250), 0.02, 0.8), ((520, 600), 0.013, 0.3)],
                   thump=(300, 0.006, 0.2), turf=0.0, air=0.0),
}

QUALITIES = {
    "driver": ["pure", "solid", "thin", "fat", "toe"],
    "wood": ["pure", "solid", "thin", "fat", "toe"],
    "iron": ["pure", "solid", "thin", "fat", "toe"],
    "wedge": ["pure", "solid", "thin", "fat", "toe"],
    "putter": ["pure", "solid", "toe"],
}


def pick(rng, lo_hi):
    return rng.uniform(*lo_hi)


def ring_modes(spec, rng, freq=1.0, tau=1.0, gain=1.0):
    return [(pick(rng, hz) * freq, t * tau, g * gain) for hz, t, g in spec]


def turf(d, rng, gain, at=0.004, tau=0.05, lo=150, hi=2200):
    """The divot: a burst of torn grass and soil, swelling in a few ms."""
    env = delayed(decay(d, tau, attack=0.006), at)
    grass = band(noise(d, rng), 2000, 7000) * delayed(decay(d, tau * 0.5, attack=0.003), at) * 0.25
    return (band(noise(d, rng), lo, hi) * env + grass) * gain


def shaft_buzz(d, rng, gain):
    """A thin strike's sting: the shaft and head vibrating at a low pitch, heard as a fast rattle on a metallic tone."""
    t = t_axis(d)
    hz = rng.uniform(150, 210)
    rattle = 0.5 + 0.5 * np.sign(np.sin(2 * np.pi * hz * t))
    tone = modes(d, [(rng.uniform(2900, 3300), 0.05, 0.6), (rng.uniform(4300, 4800), 0.035, 0.4)], rng)
    return tone * rattle * decay(d, 0.06) * gain


def strike(family, quality, rng):
    f = FAMILIES[family]
    d = f["d"]
    lo, hi, crack_tau, crack_gain = f["crack"]
    thump_hz, thump_tau, thump_gain = f["thump"]
    ring_kw, body_gain, turf_gain, turf_kw, extra = {}, 1.0, f["turf"], {}, np.zeros(int(d * SR))
    contact = 0.0  # when the ball is struck (a fat strike hits the ground first)

    if quality == "pure":
        crack_gain *= 1.3
        crack_tau *= 0.75
        ring_kw = dict(tau=1.5, gain=1.25)
        turf_gain *= 0.55
        thump_gain *= 1.15
    elif quality == "thin":
        lo, crack_gain, crack_tau = lo * 1.6, crack_gain * 1.25, crack_tau * 0.7
        ring_kw = dict(freq=1.15, tau=0.6, gain=0.55)
        thump_gain *= 0.35
        turf_gain *= 0.1
        extra = shaft_buzz(d, rng, 0.4 if family != "driver" else 0.25)
    elif quality == "fat":
        contact = 0.018
        crack_gain *= 0.4
        hi = 6000
        ring_kw = dict(tau=0.4, gain=0.3)
        body_gain = 0.7
        thump_hz, thump_gain = thump_hz * 0.7, thump_gain * 1.6
        turf_gain = max(turf_gain, 0.4) * 2.4
        turf_kw = dict(at=0.0, tau=0.11, lo=90, hi=1400)
    elif quality == "toe":
        lo, hi, crack_gain = lo * 0.7, 6500, crack_gain * 0.6
        ring_kw = dict(freq=0.8, tau=0.4, gain=0.6)
        body_gain = 1.4
        extra = modes(d, [(rng.uniform(430, 520), 0.03, 0.7), (rng.uniform(1250, 1400), 0.012, 0.3)], rng)

    crack = click(d, rng, lo, hi, crack_tau) * crack_gain
    ring = modes(d, ring_modes(f["ring"], rng, **ring_kw), rng)
    body = modes(d, ring_modes(f["body"], rng), rng) * body_gain
    thump = low(noise(d, rng), thump_hz) * decay(d, thump_tau) * thump_gain
    air = band(noise(d, rng), 800, 5000) * decay(d, 0.12, attack=0.02) * f["air"]
    hit = delayed(crack + ring + body + thump + extra, contact)
    divot = turf(d, rng, turf_gain, **turf_kw) if turf_gain > 0 else 0.0
    return level(hit + divot + air, LOUDNESS[quality] * (0.6 if family == "putter" else 1.0))


# How loud each strike quality sounds (RMS of its first 80 ms): a flush strike is the loudest, a toe the quietest.
LOUDNESS = {"pure": 0.2, "solid": 0.16, "thin": 0.15, "fat": 0.13, "toe": 0.12}


def level(x, rms, ceiling=0.95):
    """Sets the loudness of the hit (not its peak, which a sharp crack would dominate), soft-limiting any peak."""
    x = finish(x, peak=1.0)
    head = x[:int(0.08 * SR)]
    x = x * rms / max(1e-9, np.sqrt(np.mean(head ** 2)))
    return np.tanh(x / ceiling) * ceiling


def all_strikes(variations=3):
    """{'strike_driver_pure': [clip, ...], ...} with fixed seeds (the same clips every run)."""
    out = {}
    for family, qualities in QUALITIES.items():
        for quality in qualities:
            name = f"strike_{family}_{quality}"
            out[name] = [strike(family, quality, np.random.default_rng(sum(map(ord, name)) * 31 + i)) for i in range(variations)]
    return out
