"""r43 body shape (signed distance), after Gang_Beast_basic_poses.webp.

Shared by preview_r43_gb_basic.py (renders) and build_r43_game_mesh.py (skinned transport).
All parameters can be overridden by environment variables of the same name; the defaults
are the user-approved shape (2026-10-15: neck B, roundness C, legs slightly thicker).
Blender coordinates: +Z up, front -Y, +X = character right.
"""
import os
import sys
from pathlib import Path

import numpy as np
import openvdb as vdb

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_transport as rt  # noqa: E402

T = rt.Transport(ROOT / 'BlenderSource/Characters/C1B-RW-040-cage-body/CageBodyCharacter.bytes')
HEAD = T.meshes[1]
env = lambda k, d: [float(x) for x in os.environ.get(k, d).split(',')]

# (z, half width, half depth)
TORSO = np.array(env('TORSO', '-.010,.182,.040, .007,.192,.052, .030,.194,.060, .060,.194,.066, .100,.192,.074,'
                              ' .140,.189,.084, .180,.186,.097, .209,.183,.112, .236,.180,.124, .262,.177,.131,'
                              ' .300,.172,.135, .360,.168,.135, .420,.166,.135, .480,.165,.135, .530,.163,.134,'
                              ' .570,.158,.130, .605,.148,.122, .635,.130,.110, .660,.106,.092, .680,.078,.068,'
                              ' .693,.045,.040, .700,.015,.015')).reshape(-1, 3)
EXPONENT = env('EXPONENT', '1.9')[0]
PIVOT = env('PIVOT', '.162,0,.588')
ARM_LEN = env('ARM_LEN', '.40')[0]
ARM_R = env('ARM_R', '.058,.050,.045')          # root, middle, end
ARM_K = env('ARM_K', '.085')[0]                   # under-arm web at the root
ARM_FADE = env('ARM_FADE', '-.045,.07')
RELAX_OUT = env('RELAX_OUT', '16')[0]
LEG = env('LEG', '.135,0,.170, .170,0,.026, .062,.028')
LEG_K = env('LEG_K', '.04')[0]
HEAD_OFF = env('HEAD_OFF', '.015,-.012')               # head shift (forward m, up m); forward = -Y
# Leg loft rings measured from the turnaround: (z, centre x, half width, centre y, half depth).
LEG_ROWS = np.array(env('LEG_ROWS', '.004,.160,.022,.008,.020, .020,.162,.027,.007,.034, .047,.162,.034,.003,.037,'
                                    ' .074,.158,.037,0,.047, .101,.151,.044,-.003,.050, .128,.145,.050,0,.061,'
                                    ' .155,.128,.067,0,.067, .175,.110,.085,0,.078, .200,.098,.098,0,.092')).reshape(-1, 5)
LEG_LOFT = os.environ.get('LEG_LOFT', '0') == '1'
# Arch between the legs (front view): (z, half gap). The torso loft runs to the ground and the arch is cut out.
ARCH = np.array(env('ARCH', '.188,0, .180,.036, .165,.055, .135,.068, .095,.074, .050,.076, .000,.076, -.03,.076')).reshape(-1, 2)
ARCH_K = env('ARCH_K', '.04')[0]
NECK_K = env('NECK_K', '.055')[0]                  # >0: head joined to the torso with this fillet (neck only)
NECK_Z = env('NECK_Z', '.70,.78')                 # fillet full below z0, none above z1 (keeps the head top round)
CARVE = env('CARVE', '.014,.48,.06')              # depth (m), centre z, half-height of the underarm narrowing
BELLY = env('BELLY', '.07')[0]                      # extra front/back fullness around the belly (fraction)
FINAL_BLUR = int(env('FINAL_BLUR', '3')[0])
SHEAR = env('SHEAR', '0,.45,.70')               # upper torso moved forward by s between z0..z1
# Slimmer body (2026-10-17 review): torso half width and half depth multipliers. The shoulder pivot
# follows the width; the legs (torso width minus the arch gap) narrow only half as much as the torso,
# so a slim body does not stand on sticks. 1,1 = approved r43.
SLIM = env('SLIM', '1,1')
_leg = TORSO[0, 1] - ARCH[-1, 1]
_gap = (TORSO[0, 1] * SLIM[0] - _leg * (1 + SLIM[0]) / 2) / ARCH[-1, 1]
TORSO[:, 1] *= SLIM[0]
# SLIM_KEEP_LEGS=z0,z1: below z0 the legs keep their approved depth; between z0 and z1 the depth follows
# one cubic (Hermite) from the leg depth and slope at z0 to the slim torso depth at z1 with zero slope,
# so it rises monotonically and turns into the torso without a corner (a linear blend left a ridge at
# the hips, a soft minimum a visible bend, 2026-10-17).
_approved = TORSO[:, 2].copy()
TORSO[:, 2] *= SLIM[1]
_keep = env('SLIM_KEEP_LEGS', '-9,-9')
if _keep[0] > -9:
    _z0, _z1 = _keep
    _p0 = np.interp(_z0, TORSO[:, 0], _approved)
    _m0 = (np.interp(_z0 + .01, TORSO[:, 0], _approved) - np.interp(_z0 - .01, TORSO[:, 0], _approved)) / .02 * (_z1 - _z0)
    _p1 = np.interp(_z1, TORSO[:, 0], TORSO[:, 2])
    for _i, _z in enumerate(TORSO[:, 0]):
        if _z <= _z0:
            TORSO[_i, 2] = _approved[_i]
        elif _z < _z1:
            _u = (_z - _z0) / (_z1 - _z0)
            TORSO[_i, 2] = (2*_u**3 - 3*_u**2 + 1) * _p0 + (_u**3 - 2*_u**2 + _u) * _m0 + (3*_u**2 - 2*_u**3) * _p1
# SLIM_TOP=z0,z1,f: the slim depth eases back to f (fraction of the approved depth) between z0 and z1,
# so a slim torso still meets the unchanged head without a pinched nape.
_top = env('SLIM_TOP', '9,9,1')
_t = np.clip((TORSO[:, 0] - _top[0]) / max(_top[1] - _top[0], 1e-6), 0, 1)
_t = _t * _t * (3 - 2 * _t)
TORSO[:, 2] *= 1 + _t * (_top[2] / SLIM[1] - 1)
ARCH[:, 1] *= _gap
# LEG_TOE_IN=dx,z0,z1 (2026-10-29 review, third person from the front: the legs read bow-legged): both edges of each leg
# (the torso's side, which is the leg's outside, and the crotch arch, its inside) move dx in toward the middle up to knee
# height z0, easing to nothing by z1 (the crotch), so the lower leg shifts in as a whole and the leg straightens instead
# of curving (moving the floor most and fading early bent the legs more). An optional fourth value is where the outside
# stops easing (default z1): faded by the crotch it left the side bulging out at the hip (the outside narrowed 0.9 cm from
# the hip down to the knee), so it eases over the hips into the waist instead. Unset: the reviewed shape.
if 'LEG_TOE_IN' in os.environ:
    _toe = env('LEG_TOE_IN', '0,.09,.17')
    _dx, _z0, _z1 = _toe[:3]
    _z1o = _toe[3] if len(_toe) > 3 else _z1
    _w = lambda z, z1: 1 - np.clip((z - _z0) / (z1 - _z0), 0, 1) ** 2 * (3 - 2 * np.clip((z - _z0) / (z1 - _z0), 0, 1))
    TORSO[:, 1] -= _dx * _w(np.maximum(TORSO[:, 0], 0), _z1o)
    ARCH[:, 1] = np.maximum(0, ARCH[:, 1] - _dx * _w(np.maximum(ARCH[:, 0], 0), _z1))
PIVOT[0] *= SLIM[0]


def n(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


RELAX_FORE = env('RELAX_FORE', '-1')[0]           # forearm angle from vertical; <0 = straight arm
ELBOW_AT = env('ELBOW_AT', '.46')[0]               # elbow position along the arm (fraction)


def relaxed(side):
    a = np.radians(RELAX_OUT)
    upper = n((side * np.sin(a), 0, -np.cos(a)))
    if RELAX_FORE < 0:
        return upper
    b = np.radians(RELAX_FORE)
    return upper, n((side * np.sin(b), -.08, -np.cos(b)))


POSES = {
    'TPose': {1: n((1, 0, 0)), -1: n((-1, 0, 0))},
    'Relaxed': {1: relaxed(1), -1: relaxed(-1)},
    'Punch': {1: n((-.05, -1, -.05)), -1: relaxed(-1)},
    'Grab': {1: n((-.08, -1, .05)), -1: n((.08, -1, .05))},
}

VS = .004
LO, HI = np.array((-.72, -.62, -.03)), np.array((.72, .30, 1.04))
I0 = np.floor(LO / VS).astype(int)
DIM = np.ceil((HI - LO) / VS).astype(int)
GX, GY, GZ = np.meshgrid(*[((I0[a] + np.arange(DIM[a])) * VS).astype(np.float32) for a in range(3)], indexing='ij')


def catmull_rom(P, per=10):
    P = np.vstack((2 * P[0] - P[1], P, 2 * P[-1] - P[-2]))
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for t in np.linspace(0, 1, per, endpoint=False):
            out.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                             + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-2])
    return np.array(out)


def torso_mesh(seg=64):
    phi = np.arange(seg) * 2 * np.pi / seg
    profile = catmull_rom(TORSO)
    V, F = [], []
    for z, a, b in profile:
        c, s = np.cos(phi), np.sin(phi)
        b = b * (1 + BELLY * np.exp(-((z - .40) / .14) ** 2))
        a = a - CARVE[0] * np.exp(-((z - CARVE[1]) / CARVE[2]) ** 2)   # torso side under the armpit
        t = np.clip((z - SHEAR[1]) / (SHEAR[2] - SHEAR[1]), 0, 1)
        dy = -SHEAR[0] * t * t * (3 - 2 * t)
        V += list(zip(a * np.sign(c) * np.abs(c) ** (2 / EXPONENT), b * np.sign(s) * np.abs(s) ** (2 / EXPONENT) + dy,
                      np.full(seg, z)))
    rings = len(profile)
    for j in range(rings - 1):
        for k in range(seg):
            a, b = j * seg + k, j * seg + (k + 1) % seg
            F.append((a, b, b + seg, a + seg))
    bottom, top = len(V), len(V) + 1
    V += [(0, 0, profile[0, 0] - .003), (0, 0, profile[-1, 0] + .003)]
    tris = [(bottom, (k + 1) % seg, k) for k in range(seg)]
    tris += [(top, (rings - 1) * seg + k, (rings - 1) * seg + (k + 1) % seg) for k in range(seg)]
    return np.array(V), np.array(F), np.array(tris)


def level_set(points, tris=None, quads=None):
    grid = vdb.FloatGrid.createLevelSetFromPolygons(
        points.astype(np.float32), None if tris is None else tris.astype(np.uint32),
        None if quads is None else quads.astype(np.uint32), vdb.createLinearTransform(VS), 20.0)
    out = np.empty(tuple(DIM), np.float32)
    grid.copyToArray(out, ijk=tuple(int(i) for i in I0))
    return out


def blur(a, r, passes=3):
    for axis in range(3):
        for _ in range(passes):
            c = np.cumsum(np.pad(a, [(r + 1, r) if i == axis else (0, 0) for i in range(3)], mode='edge'), axis=axis)
            a = (np.take(c, np.arange(2 * r + 1, c.shape[axis]), axis=axis)
                 - np.take(c, np.arange(0, c.shape[axis] - 2 * r - 1), axis=axis)) / (2 * r + 1)
    return a.astype(np.float32)


def smin(a, b, k):
    k = np.maximum(k, 1e-5)
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.minimum(a, b) - h * h * k * .25


def capsule(a, b, ra, rb):
    d = b - a
    L = np.linalg.norm(d)
    d = d / L
    px, py, pz = GX - a[0], GY - a[1], GZ - a[2]
    s = px * d[0] + py * d[1] + pz * d[2]
    h = np.clip(s / L, 0, 1)
    qx, qy, qz = px - h * L * d[0], py - h * L * d[1], pz - h * L * d[2]
    return np.sqrt(qx * qx + qy * qy + qz * qz) - (ra + (rb - ra) * h), s


def leg_mesh(side, seg=40):
    rows = catmull_rom(LEG_ROWS[:, [0, 1, 2, 3, 4]], per=6)
    phi = np.arange(seg) * 2 * np.pi / seg
    V, F = [], []
    for z, xc, a, yc, b in rows:
        c, s = np.cos(phi), np.sin(phi)
        V += list(zip(side * xc + a * np.sign(c) * np.abs(c) ** (2 / EXPONENT),
                      yc + b * np.sign(s) * np.abs(s) ** (2 / EXPONENT), np.full(seg, z)))
    for j in range(len(rows) - 1):
        for k in range(seg):
            a, b = j * seg + k, j * seg + (k + 1) % seg
            F.append((a, b, b + seg, a + seg) if side > 0 else (a, a + seg, b + seg, b))
    bottom, top = len(V), len(V) + 1
    V += [(side * rows[0, 1], rows[0, 3], 0), (side * rows[-1, 1], rows[-1, 3], rows[-1, 0])]
    last = (len(rows) - 1) * seg
    if side > 0:
        T = [(bottom, (k + 1) % seg, k) for k in range(seg)] + [(top, last + k, last + (k + 1) % seg) for k in range(seg)]
    else:
        T = [(bottom, k, (k + 1) % seg) for k in range(seg)] + [(top, last + (k + 1) % seg, last + k) for k in range(seg)]
    return np.array(V), np.array(F), np.array(T)


def arch_mesh():
    prof = catmull_rom(ARCH, per=6)
    pts = np.vstack(([(x, z) for z, x in prof], [(-x, z) for z, x in prof[::-1]]))
    n_ = len(pts)
    V = [(x, y, z) for y in (-.4, .4) for x, z in pts]
    F = [(i, (i + 1) % n_, n_ + (i + 1) % n_, n_ + i) for i in range(n_)]
    c0, c1 = len(V), len(V) + 1
    cx = pts[:, 0].mean(); cz = pts[:, 1].mean()
    V += [(cx, -.4, cz), (cx, .4, cz)]
    T_ = [(c0, (i + 1) % n_, i) for i in range(n_)] + [(c1, n_ + i, n_ + (i + 1) % n_) for i in range(n_)]
    return np.array(V), np.array(F), np.array(T_)


TV, TQ, TT = torso_mesh()
TORSO_SDF = blur(level_set(TV, TT, TQ), 2)
ARCH_SDF = None if LEG_LOFT else level_set(*(lambda V, F, T_: (V, T_, F))(*arch_mesh()))
LEG_SDF = {side: level_set(*(lambda V, F, T_: (V, T_, F))(*leg_mesh(side))) for side in (1, -1)} if LEG_LOFT else {}


def body_field(pose):
    """Signed distance of the body (head fused when NECK_K > 0) and the posed head points."""
    return body_field_arms({side: arm_points(side, d) for side, d in POSES[pose].items()})


def body_field_arms(arms):
    """Body field with arbitrary arms: {side: (shoulder pivot, elbow, round-end centre)} (Blender m)."""
    body = TORSO_SDF
    if not LEG_LOFT:
        body = -smin(-body, ARCH_SDF, ARCH_K)
    hip, foot = np.array(LEG[0:3]), np.array(LEG[3:6])
    for side in (1, -1) if LEG_LOFT else ():
        body = smin(body, LEG_SDF[side], LEG_K)
    for side, (root, mid, end) in arms.items():
        a1, s = capsule(root, mid, ARM_R[0], ARM_R[1])
        a2, _ = capsule(mid, end, ARM_R[1], ARM_R[2])
        f = np.clip((s - ARM_FADE[0]) / (ARM_FADE[1] - ARM_FADE[0]), 0, 1)
        body = smin(body, np.minimum(a1, a2), ARM_K + (.002 - ARM_K) * f * f * (3 - 2 * f))
    head = np.stack((HEAD['rest'][:, 0], -HEAD['rest'][:, 2] - HEAD_OFF[0], HEAD['rest'][:, 1] + HEAD_OFF[1]), 1)
    if NECK_K > 0:
        t = np.clip((GZ - NECK_Z[0]) / (NECK_Z[1] - NECK_Z[0]), 0, 1)
        body = smin(body, level_set(head, HEAD['tri']), NECK_K * (1 - t * t * (3 - 2 * t)) + 1e-4)
    if FINAL_BLUR:
        body = blur(body, FINAL_BLUR)
    return body, head


def arm_points(side, d):
    """Shoulder pivot, elbow and round-end centre of one arm (Blender metres)."""
    root = np.array(PIVOT) * np.array((side, 1, 1))
    du, df = d if isinstance(d, tuple) else (d, d)
    mid = root + du * ARM_LEN * ELBOW_AT
    end = mid + df * ARM_LEN * (1 - ELBOW_AT)
    return root, mid, end


def field_mesh(body):
    grid = vdb.FloatGrid()
    grid.copyFromArray(np.ascontiguousarray(body, np.float32), ijk=tuple(int(i) for i in I0))
    grid.transform = vdb.createLinearTransform(VS)
    points, quads = grid.convertToQuads(isovalue=0.0)
    return np.asarray(points, float), np.asarray(quads)[:, ::-1]


def sample(field, P):
    """Trilinear sample of a grid field at points P (Blender metres)."""
    g = (np.asarray(P) / VS) - I0
    i = np.clip(np.floor(g).astype(int), 0, np.array(DIM) - 2)
    f = np.clip(g - i, 0, 1)
    out = 0
    for dx in (0, 1):
        for dy in (0, 1):
            for dz in (0, 1):
                w = (f[:, 0] if dx else 1 - f[:, 0]) * (f[:, 1] if dy else 1 - f[:, 1]) * (f[:, 2] if dz else 1 - f[:, 2])
                out = out + w * field[i[:, 0] + dx, i[:, 1] + dy, i[:, 2] + dz]
    return out


def field_mesh_coarse(body, step):
    """Mesh of the same field resampled on a coarser grid (same shape, fewer vertices)."""
    lo, hi = I0 * VS, (I0 + np.array(DIM) - 1) * VS
    j0 = np.ceil(lo / step).astype(int)
    dim = (np.floor(hi / step).astype(int) - j0) + 1
    axes = [((j0[a] + np.arange(dim[a])) * step) for a in range(3)]
    X, Y, Z = np.meshgrid(*axes, indexing='ij')
    coarse = sample(body, np.stack((X.ravel(), Y.ravel(), Z.ravel()), 1)).reshape(tuple(dim))
    grid = vdb.FloatGrid()
    grid.copyFromArray(np.ascontiguousarray(coarse, np.float32), ijk=tuple(int(i) for i in j0))
    grid.transform = vdb.createLinearTransform(step)
    points, quads = grid.convertToQuads(isovalue=0.0)
    return np.asarray(points, float), np.asarray(quads)[:, ::-1]
