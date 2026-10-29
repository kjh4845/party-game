"""Bake pose-space correctives for an r43 transport.

Default (R43_CORR_KIND=both, used for the game model): per shoulder reference pose the skin is first
offset along its normal toward the r43 target shape (faded out over the zone border), then only skin
that folds or is crushed (plus 4 rings) is smoothed along the surface (at most 20 mm). The SDF-target variants below
(normal / project) were tried and made the shoulder worse; they are kept for reference.

Original description:

Blender -b -P build_r43_correctives.py -- transport-in transport-out

For every reference pose the skin is posed (linear blend), then the vertices of the affected
region are moved onto the target surface of that pose (the r43 field with the arm placed on the
posed bones): repeated projection along the field gradient plus surface relaxation, which also
untangles folds. The difference is mapped back to bind space and stored as a transport v3
surface pose (the runtime SurfacePoseCorrectives interpolates them by pose distance).

- Shoulder references (mode 1): upper-arm directions around the shoulder, straight elbow;
  region = skin carrying shoulder-helper / chest+upper-arm weights near the shoulder.
- Elbow references (mode 2): elbow flex, arm held forward; region = elbow blend band.
"""
import json
import os
import struct
import sys
from pathlib import Path

import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
os.environ.setdefault('RELAX_OUT', os.environ.get('R43_BIND_OUT', '55'))
import r43_shape as shape  # noqa: E402
import r40_transport as rt  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:]
SRC, DST = Path(args[0]), Path(args[1])
T = rt.Transport(SRC)
I = T.index
body = T.meshes[0]
rest, tri = body['rest'], body['tri']
n = len(rest)
dense = np.zeros((n, len(T.names)))
for k in range(4):
    np.add.at(dense, (np.arange(n), body['ids'][:, k]), body['weights'][:, k])
ITER = int(os.environ.get('R43_CORR_ITER', 20))
STEP = float(os.environ.get('R43_CORR_STEP', .008))     # max projection per iteration (m)
LIMIT = float(os.environ.get('R43_CORR_LIMIT', .045))   # max corrective displacement (m)
KIND = os.environ.get('R43_CORR_KIND', 'both')        # normal: normal-only offset | project: projection+relax
NLIMIT = float(os.environ.get('R43_CORR_NLIMIT', .025))
NSMOOTH = int(os.environ.get('R43_CORR_NSMOOTH', 15))
FILL_ONLY = os.environ.get('R43_CORR_FILL', '0') == '1'
MAX_SHARE = float(os.environ.get('R43_CORR_MAX_SHARE', 1.1))   # skin carrying more arm rotation stays untouched
ELBOW = os.environ.get('R43_CORR_ELBOW', '0') == '1'
RELAX_IT = int(os.environ.get('R43_RELAX_IT', 40))
FADE_RINGS = int(os.environ.get('R43_FADE_RINGS', 8))
RELAX_RINGS = int(os.environ.get('R43_RELAX_RINGS', 4))
RELAX_LIMIT = float(os.environ.get('R43_RELAX_LIMIT', .02))
# R43_CORR_IRON=deg,limit,iterations,rings (2026-10-29 review: a sharp crease at the front of the shoulder where the arm
# leaves the torso, worst with the arm swung forward; the tangential relax above slides skin along a fold but cannot
# fill a valley). Unset: off. Set: after the fit, skin whose neighbouring triangles bend more than deg past their rest
# angle is smoothed in full (normal included) for iterations steps, easing out over rings rings, each vertex at most
# limit metres.
IRON = [float(v) for v in os.environ['R43_CORR_IRON'].split(',')] if 'R43_CORR_IRON' in os.environ else None
# R43_CORR_IRON_REFS=e0_a0,e30_a0,...: only these shoulder references are ironed (ironing the arm swung back left ripples
# over the shoulder). Unset: every reference.
IRON_REFS = set(os.environ['R43_CORR_IRON_REFS'].split(',')) if 'R43_CORR_IRON_REFS' in os.environ else None
IRON_FADE = int(os.environ.get('R43_CORR_IRON_FADE', 10))


def to_blender(p):
    return np.stack((p[..., 0], -p[..., 2], p[..., 1]), -1)


def to_unity(p):
    return np.stack((p[..., 0], p[..., 2], -p[..., 1]), -1)


# Surface graph for relaxation.
e = np.unique(np.sort(np.concatenate((tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]])), 1), axis=0)
src, dst = np.concatenate((e[:, 0], e[:, 1])), np.concatenate((e[:, 1], e[:, 0]))
deg = np.bincount(src, minlength=n).astype(float)


def dilate(mask, steps):
    m = mask.copy()
    for _ in range(steps):
        hit = np.zeros(n, bool)
        hit[src[m[dst]]] = True
        m |= hit
    return m


def region(side):
    s = 'LR'[side]
    helpers = dense[:, [I[p + s] for p in ('ShoulderBlend_', 'ShoulderBlendInner_', 'ShoulderBlendOuter_')]].sum(1)
    up, fo = dense[:, I['UpperArm_' + s]], dense[:, I['Forearm_' + s]]
    trunk = dense[:, [I['Chest'], I['Spine'], I['Neck']]].sum(1)
    shoulder = (helpers > .005) | ((up > .005) & (trunk > .005))
    elbow = (up > .005) & (fo > .005)
    return dilate(shoulder, 6), dilate(elbow, 6), dilate(shoulder, 3), dilate(elbow, 3)


def skin_matrices(world):
    palette = T.palette(world)
    return (palette[body['ids']] * body['weights'][:, :, None, None]).sum(1)


def arm_of(world, side):
    s = 'LR'[side]
    u, f, h = (to_blender(world[I[b + s], :3, 3]) for b in ('UpperArm_', 'Forearm_', 'HandLogical_'))
    du = (f - u) / np.linalg.norm(f - u)
    df = (h - f) / np.linalg.norm(h - f)
    root = np.array(shape.PIVOT) * np.array(((1 if side else -1), 1, 1))
    mid = root + du * shape.ARM_LEN * shape.ELBOW_AT
    end = mid + df * shape.ARM_LEN * (1 - shape.ELBOW_AT)
    return root, mid, end


def tri_normals(P):
    a, b, c = P[tri[:, 0]], P[tri[:, 1]], P[tri[:, 2]]
    nr = np.cross(b - a, c - a)
    ar = np.linalg.norm(nr, axis=1)
    return nr / (ar[:, None] + 1e-20), ar


REST_AREA = tri_normals(rest)[1]
EDGE_TRIS = {}
for fi, (a, b, c) in enumerate(tri):
    for x, y in ((a, b), (b, c), (c, a)):
        EDGE_TRIS.setdefault((min(x, y), max(x, y)), []).append(fi)
TRI_PAIRS = np.array([v for v in EDGE_TRIS.values() if len(v) == 2])
_rn = tri_normals(rest)[0]
REST_PAIR_ANGLE = np.degrees(np.arccos(np.clip((_rn[TRI_PAIRS[:, 0]] * _rn[TRI_PAIRS[:, 1]]).sum(1), -1, 1)))


def bad_vertices(P):
    """Vertices next to a fold (neighbour triangles > 35 deg apart) or a crushed triangle."""
    tn, ar = tri_normals(P)
    ang = np.degrees(np.arccos(np.clip((tn[TRI_PAIRS[:, 0]] * tn[TRI_PAIRS[:, 1]]).sum(1), -1, 1)))
    bad_t = np.zeros(len(tri), bool)
    bad_t[TRI_PAIRS[ang - REST_PAIR_ANGLE > 25].ravel()] = True
    bad_t |= ar < .35 * REST_AREA
    m = np.zeros(n, bool)
    m[tri[bad_t].ravel()] = True
    return m


def fit(P0, field, grads, move, fixed_ring):
    """Move `move` vertices of the posed skin P0 (Blender m) onto the field surface; relax
    tangentially only around folded/crushed skin, so correct skin keeps its vertices."""
    P = P0.copy()
    sel = np.nonzero(move)[0]
    for it in range(ITER):
        f = shape.sample(field, P[sel])
        g = np.stack([shape.sample(grads[k], P[sel]) for k in range(3)], 1)
        gl = np.linalg.norm(g, axis=1)
        ok = gl > .3                          # outside the narrow band the field is flat: leave it
        nrm = g / np.maximum(gl, 1e-6)[:, None]
        step = np.clip(f / np.maximum(gl, 1e-6), -STEP, STEP)
        P[sel[ok]] -= step[ok][:, None] * nrm[ok]
        if it < ITER - 2:
            relax = dilate(bad_vertices(P), 3) & move
            if relax.any():
                acc = np.zeros_like(P)
                np.add.at(acc, src, P[dst])
                d = acc / deg[:, None] - P
                full_n = np.zeros_like(P)
                full_n[sel] = nrm
                d -= (d * full_n).sum(1, keepdims=True) * full_n     # tangential only
                P[relax] += .5 * d[relax]
        d = P - P0
        dl = np.linalg.norm(d, axis=1)
        far = dl > LIMIT
        P[far] = P0[far] + d[far] * (LIMIT / dl[far])[:, None]
    return P


def vertex_normals(P):
    tn, ar = tri_normals(P)
    acc = np.zeros_like(P)
    for k in range(3):
        np.add.at(acc, tri[:, k], tn * ar[:, None])
    return acc / (np.linalg.norm(acc, axis=1, keepdims=True) + 1e-20)


def fit_normal(P0, field, grads, zone):
    """Normal-only offset of the posed skin onto the target surface: restores volume lost by
    linear blending (pinched arm root, dented shoulder top) without sliding vertices.
    Skin pressed into another part (normal against the field gradient) is left alone."""
    sel = np.nonzero(zone)[0]
    nv = vertex_normals(P0)
    f = shape.sample(field, P0[sel])
    g = np.stack([shape.sample(grads[k], P0[sel]) for k in range(3)], 1)
    g /= np.linalg.norm(g, axis=1, keepdims=True) + 1e-12
    agree = (nv[sel] * g).sum(1)
    h = np.zeros(n)
    h[sel] = np.where(agree > .6, np.clip(-f, -NLIMIT, NLIMIT), 0)
    # Fade to zero over the outer rings of the zone so the corrected skin meets the
    # untouched skin without a step (a hard edge made a cuff-like ring).
    fade = zone.astype(float)
    inner = zone.copy()
    for _ in range(FADE_RINGS):
        shrunk = inner.copy()
        edge = np.zeros(n, bool)
        edge[src[~inner[dst]]] = True
        shrunk &= ~edge
        fade += shrunk
        inner = shrunk
    fade = np.clip(fade / (FADE_RINGS + 1), 0, 1)
    h *= fade
    if FILL_ONLY:
        h = np.maximum(h, 0)          # only fill skin that sank inside the target shape
    for _ in range(NSMOOTH):
        acc = np.zeros(n)
        np.add.at(acc, src, h[dst])
        h = np.where(zone, .5 * h + .5 * acc / deg, 0) * np.where(fade > 0, 1, 0)
    return P0 + nv * h[:, None]


def fit_relax(P0, zone):
    """Tangential smoothing of folded/crushed skin only (shape kept, crinkles ironed out)."""
    P = P0.copy()
    for _ in range(RELAX_IT):
        bad = dilate(bad_vertices(P), RELAX_RINGS) & zone
        if not bad.any():
            break
        nv = vertex_normals(P)
        acc = np.zeros_like(P)
        np.add.at(acc, src, P[dst])
        d = acc / deg[:, None] - P
        d -= (d * nv).sum(1, keepdims=True) * nv
        P[bad] += .5 * d[bad]
        dd = P - P0
        dl = np.linalg.norm(dd, axis=1)
        far = dl > RELAX_LIMIT
        P[far] = P0[far] + dd[far] * (RELAX_LIMIT / dl[far])[:, None]
    return P


def fold_vertices(P, deg):
    tn, _ = tri_normals(P)
    ang = np.degrees(np.arccos(np.clip((tn[TRI_PAIRS[:, 0]] * tn[TRI_PAIRS[:, 1]]).sum(1), -1, 1)))
    bad_t = np.zeros(len(tri), bool)
    bad_t[TRI_PAIRS[ang - REST_PAIR_ANGLE > deg].ravel()] = True
    m = np.zeros(n, bool)
    m[tri[bad_t].ravel()] = True
    return m


def fit_iron(P0, zone):
    """Full Laplacian smoothing of creased skin, each vertex at most IRON limit from where it was. The smoothing is full on
    the crease and its first rings and eases to nothing over the next IRON rings (a hard edge to the smoothed patch left a
    step round the upper arm like a sleeve's edge), and once more over IRON_FADE rings inside the zone border."""
    deg_, limit, iterations, rings = IRON
    soft = int(rings)
    P = P0.copy()
    for _ in range(int(iterations)):
        core = fold_vertices(P, deg_) & zone
        if not core.any():
            break
        w, cur = np.zeros(n), core
        for k in range(soft + 3):
            w = np.where(cur & (w == 0), max(0.0, 1 - max(0, k - 2) / soft), w)
            cur = dilate(cur, 1)
        w *= zone
        acc = np.zeros_like(P)
        np.add.at(acc, src, P[dst])
        P += .5 * (acc / deg[:, None] - P) * w[:, None]
        dd = P - P0
        dl = np.linalg.norm(dd, axis=1)
        far = dl > limit
        P[far] = P0[far] + dd[far] * (limit / dl[far])[:, None]
    inside, fade = zone.copy(), np.zeros(n)
    for _ in range(IRON_FADE):
        inside = inside & ~dilate(~inside, 1)
        fade[inside] += 1 / IRON_FADE
    return P0 + (P - P0) * fade[:, None]


def pose_with_upper(side, direction_u, flex=0.0):
    """Both arms at idle, `side` upper arm turned to direction (Unity, chest frame = root here)."""
    world = T.idle_world()
    s = 'LR'[side]
    u, f = I['UpperArm_' + s], I['Forearm_' + s]
    cur = Vector(world[f, :3, 3] - world[u, :3, 3]).normalized()
    r = np.array(cur.rotation_difference(Vector(direction_u).normalized()).to_matrix())
    world[u, :3, :3] = r @ world[u, :3, :3]
    local = [np.linalg.inv(T.bind[b['parent']]) @ T.bind[i] if b['parent'] >= 0 else T.bind[i]
             for i, b in enumerate(T.header['bones'])]
    for i, b in enumerate(T.header['bones']):
        if b['parent'] < 0 or b['name'].startswith('UpperArm_'):
            continue
        lm = local[i]
        if i == f and flex:
            axis = (0, 1, 0) if s == 'L' else (0, -1, 0)   # elbow hinge, as r40_transport.synthetic
            lm = lm @ np.array(Quaternion(axis, np.radians(flex)).to_matrix().to_4x4())
        world[i] = world[b['parent']] @ lm
    return world


def direction(side, elev, az):
    """Upper-arm direction from the idle direction: elevation toward forward (az 0) / out (90) /
    back (180) / across the chest (-45)."""
    d = np.array(T.header['idleArmDirection'], float)
    sign = 1 if side else -1
    idle = np.array((sign * abs(d[0]), d[1], d[2]))
    idle /= np.linalg.norm(idle)
    fwd, out = np.array((0, 0, 1.0)), np.array((sign, 0, 0.0))
    a, e_ = np.radians(az), np.radians(elev)
    v = idle * np.cos(e_) + np.sin(e_) * (fwd * np.cos(a) + out * np.sin(a))
    return v / np.linalg.norm(v)


SHOULDER_REFS = [(0, 0), (-10, 90)] + [(e_, 0) for e_ in (30, 60, 90, 120, 150)] + \
                [(e_, 45) for e_ in (45, 90, 135)] + [(e_, 90) for e_ in (45, 90, 135)] + \
                [(e_, -45) for e_ in (45, 90)] + [(e_, 180) for e_ in (30, 60)]
ELBOW_REFS = [0, 30, 60, 90, 120, 145]
# R43_CORR_EXTRA_REFS=elev,az;elev,az: more shoulder references (2026-10-29: the run swings the arm 20-35 deg forward, between
# the e0 and e30/e60 references).
if 'R43_CORR_EXTRA_REFS' in os.environ:
    SHOULDER_REFS += [tuple(int(float(v)) for v in pair.split(',')) for pair in os.environ['R43_CORR_EXTRA_REFS'].split(';')]
refs = []
for side in (1, 0):
    for elev, az in SHOULDER_REFS:
        refs.append(dict(side=side, mode=1, world=pose_with_upper(side, direction(side, elev, az)), name=f'S{"LR"[side]}_e{elev}_a{az}'))
    for flex in ELBOW_REFS:
        refs.append(dict(side=side, mode=2, world=pose_with_upper(side, direction(side, 60, 0), flex), name=f'E{"LR"[side]}_f{flex}'))
if not ELBOW:
    refs = [r for r in refs if r['mode'] == 1]
SHARE = {}
for side in (0, 1):
    s_ = 'LR'[side]
    SHARE[side] = sum(dense[:, I[nm + s_]] * fr for nm, fr in (('ShoulderBlendInner_', .25), ('ShoulderBlend_', .5),
                      ('ShoulderBlendOuter_', .75), ('UpperArm_', 1.0), ('Forearm_', 1.0)))
print('references', len(refs), flush=True)
print('settings', json.dumps({k: os.environ[k] for k in sorted(os.environ) if k.startswith('R43_CORR') or k in ('SLIM', 'SLIM_KEEP_LEGS', 'NECK_K', 'ARCH_K')}), flush=True)

rest_normals = body['normals']
deltas, records = [], []
for ref in refs:
    side, world = ref['side'], ref['world']
    W = T.with_helpers(world)
    M = skin_matrices(W)
    P_u = np.einsum('nij,nj->ni', M[:, :3, :3], rest) + M[:, :3, 3]
    arms = {(1 if s else -1): arm_of(W, s) for s in (0, 1)}
    if KIND == 'relax':
        field = grads = None
    elif False:
        pass
    else:
        field, _ = shape.body_field_arms(arms)
        grads = np.gradient(field, shape.VS)
    sh, el, sh_core, el_core = region(side)
    zone = sh if ref['mode'] == 1 else el
    zone = zone & (SHARE[side] < MAX_SHARE)
    # Only skin that is folded/crushed in this pose (plus a margin) is corrected.
    problem = dilate(bad_vertices(P_u), 4) & zone
    move = dilate(problem, 3) & zone
    ring = move & ~dilate(problem, 1)              # outer band eases into the untouched skin
    P_b = to_blender(P_u)
    if KIND == 'both':
        move = zone
        ring = np.zeros(n, bool)
        fitted = fit_relax(fit_normal(P_b, field, grads, zone), zone)
        if IRON is not None and (IRON_REFS is None or ref['name'][3:] in IRON_REFS):
            fitted = fit_iron(fitted, zone)
    elif KIND == 'relax':
        move = zone
        ring = np.zeros(n, bool)
        fitted = fit_relax(P_b, zone)
    elif KIND == 'normal':
        move = zone
        ring = np.zeros(n, bool)
        fitted = fit_normal(P_b, field, grads, zone)
    else:
        fitted = fit(P_b, field, grads, move, np.zeros(n, bool))
    w = np.ones(n)
    w[ring] = .5
    w[~move] = 0
    target_u = to_unity(P_b + (fitted - P_b) * w[:, None])
    Minv = np.linalg.inv(M[:, :3, :3])
    delta = np.einsum('nij,nj->ni', Minv, target_u - P_u)
    delta[~move] = 0
    if KIND in ('normal', 'relax', 'both'):
        nt = to_unity(vertex_normals(fitted))
    else:
        g = np.stack([shape.sample(grads[k], fitted) for k in range(3)], 1)
        nt = to_unity(g / (np.linalg.norm(g, axis=1, keepdims=True) + 1e-12))
    nb = np.einsum('nij,nj->ni', Minv, nt)
    nb /= np.linalg.norm(nb, axis=1, keepdims=True) + 1e-12
    dn = np.where(move[:, None], nb - rest_normals, 0) * w[:, None]
    deltas.append((delta.astype(np.float32), dn.astype(np.float32)))
    c, u, f, sp = I['Chest'], I['UpperArm_' + 'LR'[side]], I['Forearm_' + 'LR'[side]], I['Spine']
    q = rt.quat(np.linalg.inv(W[c]) @ W[u])
    eq = rt.quat(np.linalg.inv(W[u]) @ W[f])
    tq = rt.quat(np.linalg.inv(W[sp]) @ W[c])
    records.append({'name': ref['name'], 'side': side, 'mode': ref['mode'], 'rotation': [q.x, q.y, q.z, q.w],
                    'elbowRotation': [eq.x, eq.y, eq.z, eq.w], 'torsoRotation': [tq.x, tq.y, tq.z, tq.w],
                    'hasDelta': True})
    print(ref['name'], 'moved', int(move.sum()), 'max delta mm', round(float(np.linalg.norm(delta, axis=1).max() * 1000), 1),
          flush=True)

# ---------------------------------------------------------------- write v3
data = SRC.read_bytes()
size = struct.unpack_from('<i', data)[0]
header = json.loads(data[4:4 + size])
payload = data[4 + size:]
header['version'] = 3
header['surfacePoses'] = records
blob = json.dumps(header).encode()
with open(DST, 'wb') as fh:
    fh.write(struct.pack('<i', len(blob)))
    fh.write(blob)
    fh.write(payload)
    for dv, dn in deltas:
        fh.write(np.ascontiguousarray(dv, '<f4').tobytes())
        fh.write(np.ascontiguousarray(dn, '<f4').tobytes())
print('wrote', DST, DST.stat().st_size // (1 << 20), 'MB')
