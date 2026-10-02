"""Grade the line of play (TH-5): cap how steeply the corridor climbs from the tee, and level the tee boxes.

Natural noise on the mountain preset rises 20-50% in places. Left alone, the tee boxes (each padded to its own
ground level) become a staircase of terraces, and the ground straight ahead of the tee is a wall that low shots
cannot clear. Here the ground along the path is re-profiled to a grade-limited curve, and the correction fades
out beyond the rough so the relief off the corridor stays dramatic.
"""
from __future__ import annotations

import numpy as np
import shapely
from scipy.ndimage import distance_transform_edt, gaussian_filter1d

STEP = 1.0                 # meters between profile samples along the path
TEE_DECK = 60.0            # meters from the back tee covering every tee box: graded at most TEE_GRADE uphill
TEE_GRADE = 0.04           # max uphill grade of the tee deck; forward boxes never stand higher than this
TEE_RUN = 120.0            # meters from the tee where the gentle uphill cap applies
TEE_UPHILL = 0.10          # max uphill grade within TEE_RUN (validate.LAUNCH_DEG allows ~14%)
UPHILL = 0.20              # max uphill grade beyond TEE_RUN
DOWNHILL = 0.25            # max downhill grade anywhere along the line
PROFILE_SMOOTH = 6.0       # meters (sigma) the raw profile is smoothed before grading
BLEND = 35.0               # meters outside the rough over which the correction fades to nothing
MAX_TEE_MOUND = 1.5        # meters the back tee may be built up so the forward boxes fit the grade


def grade_profile(profile: np.ndarray, s: np.ndarray) -> np.ndarray:
    """The closest grade-limited version of `profile` (sampled at arc lengths `s`): a forward pass from the
    tee and a backward pass from the green, averaged so neither end takes the whole cut or fill."""
    up = np.select([s[1:] <= TEE_DECK, s[1:] <= TEE_RUN], [TEE_GRADE, TEE_UPHILL], UPHILL) * np.diff(s)
    down = DOWNHILL * np.diff(s)
    fwd, back = profile.copy(), profile.copy()
    for i in range(1, len(s)):
        fwd[i] = np.clip(profile[i], fwd[i - 1] - down[i - 1], fwd[i - 1] + up[i - 1])
    for i in range(len(s) - 2, -1, -1):
        back[i] = np.clip(profile[i], back[i + 1] - up[i], back[i + 1] + down[i])
    return (fwd + back) / 2  # the set of grade-limited profiles is convex, so the average is one too


def grade_corridor(h: np.ndarray, grid, path, rough_dist: np.ndarray) -> np.ndarray:
    """Shift the ground by a function of distance along the path so the hole line follows grade_profile.
    Every cell takes the correction of its nearest path sample, at full strength inside the rough
    (`rough_dist` = meters outside it) and fading to zero BLEND m beyond."""
    s = np.arange(0.0, path.length + STEP / 2, STEP)
    pts = shapely.line_interpolate_point(path, s)
    xs, zs = shapely.get_x(pts), shapely.get_y(pts)
    profile = gaussian_filter1d(grid.sample(h, xs, zs), PROFILE_SMOOTH / STEP, mode="nearest")
    delta = grade_profile(profile, s) - profile

    rows = np.clip(np.rint(zs / grid.d).astype(int), 0, grid.n - 1)
    cols = np.clip(np.rint(xs / grid.d).astype(int), 0, grid.n - 1)
    index = np.full(h.shape, -1)
    index[rows, cols] = np.arange(len(s))   # later samples win a shared cell; they are < 1 cell apart
    nearest = distance_transform_edt(index < 0, return_distances=False, return_indices=True)
    field = delta[index[nearest[0], nearest[1]]]
    t = np.clip(1 - rough_dist / BLEND, 0.0, 1.0)
    return h + field * t * t * (3 - 2 * t)


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
