"""Grade the line of play (TH-5, G6-1, G6-4): cap how steeply the corridor climbs from the tee, level the landing
zones along and across the line, and level the tee boxes.

Natural noise on the mountain preset rises 20-50% in places. Left alone, the tee boxes (each padded to its own
ground level) become a staircase of terraces, the ground straight ahead of the tee is a wall that low shots cannot
clear, and a perfect drive can land on a 20 % side slope. Here the ground along the path is re-profiled to a
grade-limited curve and tilted where it leans too far across the line. Every cell takes the correction at its
continuous position along the path (interpolated between samples, so no stair steps), and the whole correction is
blurred and fades out beyond the rough, so the relief off the corridor stays dramatic.
"""
from __future__ import annotations

import numpy as np
import shapely
from scipy.ndimage import gaussian_filter, gaussian_filter1d

STEP = 1.0                 # meters between profile samples along the path
TEE_DECK = 60.0            # meters from the back tee covering every tee box: graded at most TEE_GRADE uphill
TEE_GRADE = 0.04           # max uphill grade of the tee deck; forward boxes never stand higher than this
TEE_RUN = 120.0            # meters from the tee where the gentle uphill cap applies
TEE_UPHILL = 0.10          # max uphill grade within TEE_RUN (validate.LAUNCH_DEG allows ~14%)
UPHILL = 0.20              # max uphill grade beyond TEE_RUN
DOWNHILL = 0.25            # max downhill grade anywhere along the line
LANDING_RUN = 30.0         # meters either side of a landing zone...
LANDING_GRADE = 0.06       # ...graded at most this much up or down along the line (G6-4)
PROFILE_SMOOTH = 6.0       # meters (sigma) the raw profile is smoothed before grading
BLEND = 35.0               # meters outside the rough over which the correction fades to nothing
CORRECTION_SMOOTH = 5.0    # meters (sigma) the correction field is blurred: no creases at corners or the fade (G6-1)
MAX_TEE_MOUND = 1.5        # meters the back tee may be built up so the forward boxes fit the grade

# Side slope (G6-4): the ground across the line is tilted back to these grades, measured CROSS_SPAN m either side.
CROSS_SPAN = 10.0
FAIRWAY_CROSS = 0.08       # anywhere along the line...
LANDING_CROSS = 0.04       # ...and at the landing zones (validate.LANDING_CROSS checks 8 %)
CROSS_WIDTH = 25.0         # meters: the tilt is full near the line and fades out over about twice this
CROSS_ENDS = 30.0          # meters at the tee and the green left to their pads


def _landing_weight(s: np.ndarray, landings) -> np.ndarray:
    """1 at a landing zone, fading to 0 about LANDING_RUN m either side (0 everywhere without landings)."""
    if not len(landings):
        return np.zeros_like(s)
    return np.exp(-0.5 * ((s[:, None] - np.asarray(landings)[None, :]) / LANDING_RUN) ** 2).max(axis=1)


def grade_profile(profile: np.ndarray, s: np.ndarray, landings=()) -> np.ndarray:
    """The closest grade-limited version of `profile` (sampled at arc lengths `s`): a forward pass from the
    tee and a backward pass from the green, averaged so neither end takes the whole cut or fill. `landings`: arc
    lengths of the landing zones, held to LANDING_GRADE within LANDING_RUN."""
    end = s[1:]
    up = np.select([end <= TEE_DECK, end <= TEE_RUN], [TEE_GRADE, TEE_UPHILL], UPHILL)
    down = np.full_like(end, DOWNHILL)
    if len(landings):
        near = np.abs(end[:, None] - np.asarray(landings)[None, :]).min(axis=1) <= LANDING_RUN
        up, down = np.where(near, np.minimum(up, LANDING_GRADE), up), np.where(near, LANDING_GRADE, down)
    up, down = up * np.diff(s), down * np.diff(s)
    fwd, back = profile.copy(), profile.copy()
    for i in range(1, len(s)):
        fwd[i] = np.clip(profile[i], fwd[i - 1] - down[i - 1], fwd[i - 1] + up[i - 1])
    for i in range(len(s) - 2, -1, -1):
        back[i] = np.clip(profile[i], back[i + 1] - up[i], back[i + 1] + down[i])
    return (fwd + back) / 2  # the set of grade-limited profiles is convex, so the average is one too


def _normals(xs: np.ndarray, zs: np.ndarray) -> tuple[np.ndarray, np.ndarray]:
    """Unit left normals of the sampled path."""
    hx, hz = np.gradient(xs), np.gradient(zs)
    norm = np.hypot(hx, hz)
    norm[norm == 0] = 1.0
    return -hz / norm, hx / norm


def cross_excess(h: np.ndarray, grid, s, xs, zs, landings=()) -> np.ndarray:
    """Per path sample: how much the ground leans across the line (rise to the left per meter) beyond the allowed
    side slope (FAIRWAY_CROSS, LANDING_CROSS at the landing zones), 0 near the tee and the green."""
    nx, nz = _normals(xs, zs)
    left = grid.sample(h, xs + nx * CROSS_SPAN, zs + nz * CROSS_SPAN)
    right = grid.sample(h, xs - nx * CROSS_SPAN, zs - nz * CROSS_SPAN)
    lean = gaussian_filter1d((left - right) / (2 * CROSS_SPAN), PROFILE_SMOOTH / STEP, mode="nearest")
    cap = FAIRWAY_CROSS - (FAIRWAY_CROSS - LANDING_CROSS) * _landing_weight(s, landings)
    ends = np.clip(np.minimum(s, s[-1] - s) / CROSS_ENDS - 1, 0.0, 1.0)  # off within CROSS_ENDS, full by twice that
    return (lean - np.clip(lean, -cap, cap)) * ends * ends * (3 - 2 * ends)


def grade_corridor(h: np.ndarray, grid, path, rough_dist: np.ndarray, landings=()) -> np.ndarray:
    """Shift the ground so the hole line follows grade_profile and its side slope stays under the caps.
    Each cell is projected onto the path and takes the along-line correction interpolated at that arc length,
    plus a tilt about the line (growing with its signed offset, fading beyond ~CROSS_WIDTH). The correction is full
    inside the rough (`rough_dist` = meters outside it), fades to zero BLEND m beyond, and is blurred by
    CORRECTION_SMOOTH. `landings`: arc lengths of the landing zones."""
    s = np.arange(0.0, path.length + STEP / 2, STEP)
    pts = shapely.line_interpolate_point(path, s)
    xs, zs = shapely.get_x(pts), shapely.get_y(pts)
    profile = gaussian_filter1d(grid.sample(h, xs, zs), PROFILE_SMOOTH / STEP, mode="nearest")
    delta = grade_profile(profile, s, landings) - profile
    lean = cross_excess(h, grid, s, xs, zs, landings)

    near = rough_dist < BLEND
    cx, cz = grid.x[near], grid.z[near]
    at = shapely.line_locate_point(path, shapely.points(cx, cz))
    foot = shapely.line_interpolate_point(path, at)
    nx, nz = _normals(xs, zs)
    offset = (cx - shapely.get_x(foot)) * np.interp(at, s, nx) + (cz - shapely.get_y(foot)) * np.interp(at, s, nz)
    tilt = offset * np.exp(-0.5 * (offset / CROSS_WIDTH) ** 2)
    t = np.clip(1 - rough_dist[near] / BLEND, 0.0, 1.0)
    field = np.zeros_like(h)
    field[near] = (np.interp(at, s, delta) - np.interp(at, s, lean) * tilt) * t * t * (3 - 2 * t)
    return h + gaussian_filter(field, CORRECTION_SMOOTH / grid.d, mode="nearest")


def tee_levels(h: np.ndarray, grid, path, tees, raise_m: float) -> list[float]:
    """Pad level for each tee box (back box first). Each box sits raise_m above its own ground, except that
    no forward box may stand more than TEE_GRADE above the back box: the back box is built up (at most
    MAX_TEE_MOUND) and any box still too high is lowered, so the tee shot never faces a terrace."""
    base = [float(np.median(h[m])) + raise_m if (m := grid.mask([t])).any() else None for t in tees]
    if not tees or base[0] is None:
        return base
    s = [path.project(t.centroid) for t in tees]
    need = max((b - TEE_GRADE * (si - s[0]) for b, si in zip(base, s) if b is not None), default=base[0])
    back = base[0] + float(np.clip(need - base[0], 0.0, MAX_TEE_MOUND))
    return [back] + [None if b is None else min(b, back + TEE_GRADE * max(si - s[0], 0.0))
                     for b, si in zip(base[1:], s[1:])]
