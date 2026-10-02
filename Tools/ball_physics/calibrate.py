#!/usr/bin/env python3
"""Fit the golf ball flight model to TrackMan PGA Tour averages.

The model here must stay identical to Assets/GolfSim/Ball/Runtime/BallFlightModel.cs:
gravity + quadratic drag + Magnus lift, with
    S  = r * |spin| / |v|              (spin parameter)
    Re = rho * |v| * 2r / mu           (Reynolds number)
    Cd = cd0 + cdSpin * S + cdRe * (1e5 / Re)   (dimple drag rises as the ball slows)
    CL = clScale * S ** clPower
    spin(t) = spin0 * exp(-t / spinDecayTime)
Run:  ../course_prep/.venv/bin/python calibrate.py
"""
from __future__ import annotations

import math

import numpy as np
from scipy.optimize import least_squares

MASS = 0.04593          # kg (USGA max)
RADIUS = 0.021335       # m (1.68 in diameter)
AREA = math.pi * RADIUS ** 2
RHO = 1.184             # kg/m^3, TrackMan's normalised conditions: sea level, 77 F
G = 9.81
MU = 1.85e-5            # air dynamic viscosity at 25 C, Pa*s
DT = 0.002

MPH, YARD, RPM = 0.44704, 0.9144, 2 * math.pi / 60

# TrackMan PGA Tour averages: ball speed mph, launch deg, spin rpm -> apex yd, land angle deg, carry yd
TOUR = {
    "Driver": (167, 10.9, 2686, 32, 38, 275),
    "3-wood": (158, 9.2, 3655, 30, 43, 243),
    "5-iron": (132, 12.1, 5361, 31, 47, 194),
    "7-iron": (120, 16.3, 7097, 32, 50, 172),
    "9-iron": (109, 20.4, 8647, 30, 51, 148),
    "PW": (102, 24.2, 9304, 29, 52, 136),
}


def fly(speed, launch_deg, spin_rpm, cd0, cd_spin, cd_re, cl_scale, cl_power, decay):
    """Semi-implicit flight to landing at launch height. Returns carry m, apex m, land angle deg."""
    a = math.radians(launch_deg)
    vx, vy = speed * math.cos(a), speed * math.sin(a)
    x = y = apex = t = 0.0
    spin0 = spin_rpm * RPM
    k = 0.5 * RHO * AREA / MASS
    while True:
        v = math.hypot(vx, vy)
        spin = spin0 * math.exp(-t / decay)
        s = RADIUS * spin / v
        re = RHO * v * 2 * RADIUS / MU
        cd = cd0 + cd_spin * s + cd_re * (1e5 / re)
        cl = cl_scale * s ** cl_power
        ax = k * v * (-cd * vx - cl * vy)
        ay = k * v * (-cd * vy + cl * vx) - G
        vx += ax * DT
        vy += ay * DT
        nx, ny = x + vx * DT, y + vy * DT
        t += DT
        if ny < 0 and vy < 0:
            f = y / (y - ny)  # interpolate the crossing
            return x + f * (nx - x), apex, math.degrees(math.atan2(-vy, vx))
        x, y = nx, ny
        apex = max(apex, y)


def residuals(p):
    out = []
    for speed, launch, spin, apex_yd, land, carry_yd in TOUR.values():
        carry, apex, land_angle = fly(speed * MPH, launch, spin, *p)
        out += [(carry - carry_yd * YARD) / (carry_yd * YARD) * 3,  # carry matters most
                (apex - apex_yd * YARD) / (apex_yd * YARD),
                (land_angle - land) / 50]
    return out


def main():
    start = [0.15, 0.25, 0.05, 0.6, 0.5, 25.0]
    fit = least_squares(residuals, start, bounds=([0.0, 0, 0, 0.1, 0.2, 8], [0.35, 1.5, 0.3, 2.0, 1.2, 60]), diff_step=1e-3)
    names = ["cd0", "cdSpin", "cdRe", "clScale", "clPower", "spinDecayTime"]
    print("fitted:", ", ".join(f"{n}={v:.4f}" for n, v in zip(names, fit.x)))
    print(f"{'club':8} {'carry yd':>14} {'apex yd':>12} {'land deg':>12}")
    for club, (speed, launch, spin, apex_yd, land, carry_yd) in TOUR.items():
        carry, apex, land_angle = fly(speed * MPH, launch, spin, *fit.x)
        print(f"{club:8} {carry / YARD:6.1f} ({carry_yd:3})  {apex / YARD:5.1f} ({apex_yd:2})  {land_angle:5.1f} ({land:2})")


if __name__ == "__main__":
    main()
