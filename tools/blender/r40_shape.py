"""Signed-distance description of the r40 body, shared by the idle fit and correctives.

All coordinates are Blender space (+Z up, front -Y, +X = character right/R).
- torso: closed torso-only surface fitted to r16 (BVH signed distance)
- arms: round tubes given by (origin, direction); origin is just outside the
  shoulder joint centre so the tube rotates about its own root
- shoulder: smooth union of torso and arm near the arm root (by arc length s
  along the arm), so the junction is a fillet in any arm pose
- axilla: further along the arm the torso is carved to keep a small clearance,
  so an arm resting against the body never merges into it
"""
import time

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

PIVOT = np.array((.18, 0, .62))           # UpperArm joint centre, +x side
IDLE_DIR = np.array((.087, 0, -1.0)) / np.hypot(.087, 1.0)
AXIS_OFFSET = .006
ARM_LENGTH = .356                          # joint -> tip sphere centre
ARM_RADII = [(0.0, .037), (.05, .0355), (.18, .035), (.30, .0335), (ARM_LENGTH, .033)]
BLEND_K = .030
UNION_S0, UNION_S1 = -.005, .055           # full union before S0, none after S1 (metres along the arm)
CLEARANCE = .005
import os  # noqa: E402
_env = lambda k, d: float(os.environ.get(k, d))
AXILLA_EXTRA, AXILLA_S, AXILLA_SPAN = _env('R40_AXILLA_EXTRA', .028), .055, _env('R40_AXILLA_SPAN', .030)
GROOVE_K = _env('R40_GROOVE_K', .015)
# An arm resting beside the torso (idle) keeps a groove; an arm sunk deep into it (a forward
# arm along the chest side) is left in contact rather than carving a deep channel.
SINK_FREE, SINK_SPAN = .006, .008


def idle_arms():
    out = []
    for side in (1, -1):
        m = np.array((side, 1, 1))
        out.append((PIVOT * m + np.array((side * AXIS_OFFSET, 0, 0)), IDLE_DIR * m))
    return out


def radius_at(s):
    xs, rs = zip(*ARM_RADII)
    return np.interp(s, xs, rs)


def arm_field(P, arms):
    best = np.full(len(P), np.inf)
    best_s = np.zeros(len(P))
    best_axis = np.zeros_like(P)
    for a, d in arms:
        s = (P - a) @ d
        sc = np.clip(s, 0, ARM_LENGTH)
        axis = a + sc[:, None] * d
        dist = np.linalg.norm(P - axis, axis=1) - radius_at(sc)
        closer = dist < best
        best = np.where(closer, dist, best)
        best_s = np.where(closer, s, best_s)
        best_axis = np.where(closer[:, None], axis, best_axis)
    return best, best_s, best_axis


def union_weight(s):
    t = np.clip((s - UNION_S0) / (UNION_S1 - UNION_S0), 0, 1)
    return 1 - t * t * (3 - 2 * t)


def smooth_max(a, b, k):
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.maximum(a, b) + h * h * k / 4


class TorsoField:
    def __init__(self, verts, faces):
        self.tree = BVHTree.FromPolygons([Vector(v) for v in verts], [list(f) for f in faces])

    def __call__(self, P):
        out = np.empty(len(P))
        for i, p in enumerate(P):
            loc, normal, _, dist = self.tree.find_nearest(Vector(p))
            out[i] = dist if np.dot(p - np.array(loc), np.array(normal)) >= 0 else -dist
        return out


def field(P, torso, arms, part=None, release=False):
    """part: None = blended union; else per point 0 = torso sheet, 1 = arm sheet."""
    b, s, axis = arm_field(P, arms)
    a = torso(P)
    union = union_weight(s)
    groove = 1 - union
    if release:
        sink = radius_at(np.clip(s, 0, ARM_LENGTH)) - torso(axis)
        t = np.clip((sink - SINK_FREE) / SINK_SPAN, 0, 1)
        groove = groove * (1 - t * t * (3 - 2 * t))
    clearance = CLEARANCE + AXILLA_EXTRA * np.exp(-((s - AXILLA_S) / AXILLA_SPAN) ** 2)
    a = np.where(groove > 1e-3, smooth_max(a, (clearance - b) * groove - (1 - groove), GROOVE_K), a)
    k = BLEND_K * union + 1e-6
    h = np.maximum(k - np.abs(a - b), 0) / k
    blended = np.minimum(a, b) - h * h * k / 4
    if part is None:
        return blended
    own = np.where(part > .5, b, a)
    return np.where(union > .02, blended, own)


def project(P, torso, arms, part=None, steps=4, eps=2e-4, release=False):
    P = P.copy()
    for _ in range(steps):
        f = field(P, torso, arms, part, release)
        grad = np.stack([(field(P + e, torso, arms, part, release) - field(P - e, torso, arms, part, release)) / (2 * eps)
                         for e in np.eye(3) * eps], 1)
        P -= (f / ((grad ** 2).sum(1) + 1e-12))[:, None] * grad
    return P


def fit(A, LtL, C0, torso, arms, part, iterations=14, smooth=.5, mirror=None, reference=None, label='fit',
        quiet=False, release=False):
    """Least squares fit of cage C so that A @ C lies on the field; Laplacian kept close to `reference`."""
    reference = C0 if reference is None else reference
    C = C0.copy()
    lhs = A.T @ A + smooth * LtL + 1e-3 * np.eye(len(C))
    for it in range(iterations):
        start = time.time()
        T = project(A @ C, torso, arms, part, release=release)
        C = np.linalg.solve(lhs, A.T @ T + smooth * LtL @ reference + 1e-3 * C)
        if mirror is not None:
            C = .5 * (C + C[mirror] * np.array((-1, 1, 1)))
        if not quiet and (it % 4 == 3 or it == iterations - 1):
            d = np.abs(field(A @ C, torso, arms, part, release))
            print(f'{label} {it}: mean {d.mean()*1000:.2f} mm p95 {np.percentile(d,95)*1000:.2f} '
                  f'max {d.max()*1000:.2f} ({time.time()-start:.1f}s)')
    return C
