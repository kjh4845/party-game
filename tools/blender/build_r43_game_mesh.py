"""Build the r43 game body: approved r43 shape -> skinned Unity transport.

Blender -b -P build_r43_game_mesh.py -- output-dir

- Mesh: the r43 signed-distance body in the relaxed pose (arms straight, RELAX_OUT deg out),
  head fused (NECK_K), one skinned mesh. Normals from the field gradient.
- Bones: the r40 28-bone header moved to the r43 proportions (bind = relaxed pose):
  shoulder joint at the arm pivot, elbow at ELBOW_AT, hand so that HandLogical + the punch
  terminal (.06 m) reaches the round arm end; legs centred in the r43 legs; head bone
  follows the head offset. Clavicle, spine and rotations otherwise unchanged.
- Weights: trunk by height (Pelvis/Spine/Chest), neck/head by height, legs by height per side,
  arms by arm-vs-torso distance with the chest->upper-arm rotation spread over the three
  ShoulderBlend helpers, and a short elbow blend.
- Header extras read by the Unity loader: idleArmDirection, armColliderRadii, contactSearch,
  ignoreUpperArmTorso, and for a slimmed body (SLIM set) torsoColliderRadii.

Writes R43CharacterBase.bytes (transport v1). The game file R43Character.bytes is made from it by
build_r43_correctives.py (adds the shoulder crinkle correctives, transport v3). and BuildReport.json.
"""
import json
import os
import struct
import sys
from pathlib import Path

import numpy as np
from mathutils import Matrix, Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
# The game mesh is bound in an A-pose (arms 55 deg); the game idle (16 deg) is set by the rig.
os.environ.setdefault('RELAX_OUT', os.environ.get('R43_BIND_OUT', '55'))
import r43_shape as shape  # noqa: E402
import r40_transport as rt  # noqa: E402

OUT = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
OUT.mkdir(parents=True, exist_ok=True)
env = lambda k, d: float(os.environ.get(k, d))

TERMINAL = .06            # punch library HandLogical head -> tail (ApprovedPunch.json)
HELPERS = (('ShoulderBlendInner_', .25), ('ShoulderBlend_', .5), ('ShoulderBlendOuter_', .75))
ROOT_RAMP = (env('R43_ROOT_S0', -.16), env('R43_ROOT_S1', 0))   # rotation share along the arm root (m)
ARMNESS = (env('R43_ARM_A0', -.004), env('R43_ARM_A1', .008))       # (torso - arm) distance ramp (m)
ELBOW_SPAN = env('R43_ELBOW_SPAN', .035)
RIGID_FROM = env('R43_RIGID_FROM', .03)          # along the arm (m): fully on the arm bone beyond this


def smooth(e0, e1, x):
    t = np.clip((x - e0) / (e1 - e0), 0, 1)
    return t * t * (3 - 2 * t)


def to_unity(p):
    return np.stack((p[..., 0], p[..., 2], -p[..., 1]), -1)


# ---------------------------------------------------------------- geometry
body, _ = shape.body_field('Relaxed')
MESH_STEP = env('R43_MESH_STEP', .006)   # output mesh spacing; the field itself stays at shape.VS
P, quads = shape.field_mesh_coarse(body, MESH_STEP) if MESH_STEP > shape.VS else shape.field_mesh(body)
tris = np.concatenate((quads[:, [0, 1, 2]], quads[:, [0, 2, 3]]))
grad = np.stack(np.gradient(body, shape.VS), -1)
N = np.stack([shape.sample(grad[..., k], P) for k in range(3)], 1)
N /= np.linalg.norm(N, axis=1, keepdims=True) + 1e-12
# Winding must agree with the outward field normals (Unity culls back faces).
_fn = np.cross(P[tris[:, 1]] - P[tris[:, 0]], P[tris[:, 2]] - P[tris[:, 0]])
if (_fn * N[tris].mean(1)).sum(1).mean() < 0:
    tris = tris[:, ::-1].copy()
print('mesh', len(P), 'vertices', len(tris), 'triangles', flush=True)

# Torso-only field (arch cut, no arms/head) for arm-vs-torso classification.
torso = shape.TORSO_SDF if shape.LEG_LOFT else -shape.smin(-shape.TORSO_SDF, shape.ARCH_SDF, shape.ARCH_K)
torso_d = shape.sample(torso, P)
tg = np.stack(np.gradient(torso, shape.VS), -1)
torso_n = np.stack([shape.sample(tg[..., k], P) for k in range(3)], 1)
torso_n /= np.linalg.norm(torso_n, axis=1, keepdims=True) + 1e-12

# ---------------------------------------------------------------- bones
T = rt.Transport(shape.ROOT / 'BlenderSource/Characters/C1B-RW-040-cage-body/CageBodyCharacter.bytes')
bones = json.loads(json.dumps(T.header['bones']))
names = [b['name'] for b in bones]
I = {n: i for i, n in enumerate(names)}
W = rt.bone_world(bones)          # Unity space, r40 idle bind
arm_geo = {}
for side, s in ((1, 'R'), (-1, 'L')):
    d = shape.POSES['Relaxed'][side]
    root, mid, end = shape.arm_points(side, d)
    pivot_u, dir_u = to_unity(root), to_unity(np.asarray(d, float))
    upper_len = np.linalg.norm(mid - root)
    tip = np.linalg.norm(end - root) + shape.ARM_R[2]
    fore_len = tip - TERMINAL - upper_len
    arm_geo[side] = dict(root=root, mid=mid, end=end, upper=upper_len, fore=fore_len)
    u, f, h = I['UpperArm_' + s], I['Forearm_' + s], I['HandLogical_' + s]
    current = Vector(W[f, :3, 3] - W[u, :3, 3]).normalized()
    delta = np.array(current.rotation_difference(Vector(dir_u)).to_matrix())
    for b, pos in ((u, pivot_u), (f, pivot_u + dir_u * upper_len), (h, pivot_u + dir_u * (upper_len + fore_len))):
        W[b, :3, :3] = delta @ W[b, :3, :3]
        W[b, :3, 3] = pos
    for prefix, _ in HELPERS:
        k = I[prefix + s]
        W[k, :3, :3] = W[u, :3, :3]
        W[k, :3, 3] = pivot_u
# Legs: centre of the r43 leg (outer torso edge and arch gap at the ground).
leg_x = .5 * (shape.TORSO[np.argmin(np.abs(shape.TORSO[:, 0] - .03)), 1] + shape.ARCH[-1, 1])
# R43_LEG_X: keep the leg bones at this x (m) whatever the leg skin's shape (LEG_TOE_IN moves only the skin).
if 'R43_LEG_X' in os.environ:
    leg_x = float(os.environ['R43_LEG_X'])
for s, sign in (('L', -1), ('R', 1)):
    for b in ('Thigh_', 'Calf_', 'Foot_', 'HipDeform_'):
        W[I[b + s], 0, 3] = sign * leg_x
# Head follows the head mesh offset (forward = Unity +z).
W[I['Head'], 1, 3] += shape.HEAD_OFF[1]
W[I['Head'], 2, 3] += shape.HEAD_OFF[0]
for i, b in enumerate(bones):
    local = W[i] if b['parent'] < 0 else np.linalg.inv(W[b['parent']]) @ W[i]
    q = Matrix(local[:3, :3].tolist()).to_quaternion()
    b['position'] = [float(v) for v in local[:3, 3]]
    b['rotation'] = [q.x, q.y, q.z, q.w]
W = rt.bone_world(bones)

# ---------------------------------------------------------------- weights
x, y, z = P[:, 0], P[:, 1], P[:, 2]
n_b = len(names)
Wt = np.zeros((len(P), n_b))
# trunk
pel = 1 - smooth(.20, .30, z)
che = smooth(.40, .52, z)
spi = np.clip(1 - pel - che, 0, 1)
head = smooth(.685, .735, z)
neck = smooth(.62, .69, z) * (1 - head)
keep = 1 - head - neck
Wt[:, I['Pelvis']] = pel * keep
Wt[:, I['Spine']] = spi * keep
Wt[:, I['Chest']] = che * keep
Wt[:, I['Neck']] = neck
Wt[:, I['Head']] = head
# legs
foot = 1 - smooth(.02, .05, z)
calf = (1 - smooth(.08, .12, z)) * (1 - foot)
thigh = 1 - foot - calf
# R43_HIP_HELPER=t0,t1,h0,h1,seam (2026-10-29 review, third person from behind: the skin under the hips swung back with
# a kicked-back leg, the whole lower torso corner peeling off with it). Unset: the leg follows its bone fully below .13,
# blending to the trunk by .22, split left/right at x = 0. Set: the leg bones only up to t0..t1; above that up to h0..h1
# the hip band follows HipDeform_ (the rig turns it a quarter of the thigh, ProceduralJumpPose), and within seam of
# x = 0 the band fades to the pelvis so the two sides never pull one seam apart.
hip_cfg = [float(v) for v in os.environ['R43_HIP_HELPER'].split(',')] if 'R43_HIP_HELPER' in os.environ else None
if hip_cfg is None:
    leg, hip = 1 - smooth(.13, .22, z), np.zeros(len(P))
else:
    t0, t1, h0, h1, seam = hip_cfg
    leg = 1 - smooth(t0, t1, z)
    hip = np.clip((1 - smooth(h0, h1, z)) - leg, 0, 1) * smooth(0, seam, np.abs(x))
for s, mask in (('R', x >= 0), ('L', x < 0)):
    m = leg * mask
    h = hip * mask
    Wt[:, :] *= (1 - m - h)[:, None]
    Wt[:, I['Thigh_' + s]] += m * thigh
    Wt[:, I['Calf_' + s]] += m * calf
    Wt[:, I['Foot_' + s]] += m * foot
    Wt[:, I['HipDeform_' + s]] += h
# arms
# Surface graph: the rotation share is smoothed along the skin as one scalar per side and only
# then turned into bone weights, so every vertex keeps <= 4 influences (trunk + two adjacent
# shoulder controls) and neighbouring vertices never disagree.
e_ = np.concatenate((tris[:, [0, 1]], tris[:, [1, 2]], tris[:, [2, 0]]))
e_ = np.unique(np.sort(e_, 1), axis=0)
src, dst = np.concatenate((e_[:, 0], e_[:, 1])), np.concatenate((e_[:, 1], e_[:, 0]))
deg = np.bincount(src, minlength=len(P)).astype(float)
SMOOTH_IT = int(os.environ.get('R43_WEIGHT_SMOOTH', 60))


def surface_smooth(v, iterations, keep=None):
    for _ in range(iterations):
        acc = np.zeros_like(v)
        np.add.at(acc, src, v[dst])
        v = .5 * v + .5 * acc / deg
        if keep is not None:
            v = np.where(keep, v, 0)
    return v


armness = np.zeros(len(P))
for side, s in ((1, 'R'), (-1, 'L')):
    g = arm_geo[side]
    mask = (x * side) > 0
    root, end = g['root'], g['end']
    axis = (end - root) / np.linalg.norm(end - root)
    along = (P - root) @ axis
    L = np.linalg.norm(end - root)
    h = np.clip(along / L, 0, 1)
    radius = np.interp(h, [0, shape.ELBOW_AT, 1], shape.ARM_R)
    d_arm = np.linalg.norm(P - (root + np.clip(along, 0, L)[:, None] * axis), axis=1) - radius
    # Arm vs torso: closer to the arm surface AND facing away from the arm axis rather than
    # along the torso normal (the arm's inner face and the torso side face each other).
    radial = P - (root + np.clip(along, 0, L)[:, None] * axis)
    radial /= np.linalg.norm(radial, axis=1, keepdims=True) + 1e-12
    facing = smooth(-.25, .25, (N * radial).sum(1) - (N * torso_n).sum(1))
    a = smooth(*ARMNESS, torso_d - d_arm) * facing * smooth(-.03, .02, along) * mask
    t = surface_smooth(a * smooth(*ROOT_RAMP, along), SMOOTH_IT, mask)
    # The arm itself is rigid from RIGID_FROM: smoothing may not spill partial rotation into it
    # (that bends the arm root). Between the ramp end and RIGID_FROM the share eases to 1.
    rigid = a * smooth(ROOT_RAMP[1], RIGID_FROM, along)
    t = np.maximum(t, rigid)
    t = np.clip(t, 0, 1)
    armness = np.maximum(armness, a)
    e = smooth(g['upper'] - ELBOW_SPAN, g['upper'] + ELBOW_SPAN, along)
    chain = np.zeros_like(Wt)
    chain[:, I['UpperArm_' + s]] = 1 - e
    chain[:, I['Forearm_' + s]] = e
    controls = [(0.0, None)] + [(f, I[p + s]) for p, f in HELPERS] + [(1.0, 'chain')]
    new = np.zeros_like(Wt)
    for (t0, b0), (t1, b1) in zip(controls, controls[1:]):
        sel = mask & (t >= t0) & (t <= t1) & (t > 1e-4)
        k = ((t - t0) / (t1 - t0))[sel]
        lo = Wt[sel] if b0 is None else np.eye(n_b)[b0][None].repeat(sel.sum(), 0)
        hi = chain[sel] if b1 == 'chain' else np.eye(n_b)[b1][None].repeat(sel.sum(), 0)
        new[sel] = lo * (1 - k)[:, None] + hi * k[:, None]
    upd = mask & (t > 1e-4)
    Wt[upd] = new[upd]
Wt /= Wt.sum(1, keepdims=True)


def top4(Wd):
    order = np.argsort(-Wd, axis=1)[:, :4]
    vals = np.take_along_axis(Wd, order, 1)
    vals = np.where(vals > 1e-4, vals, 0)
    vals /= vals.sum(1, keepdims=True)
    return order.astype(np.int32), vals.astype(np.float32)


ids, wts = top4(Wt)

# ---------------------------------------------------------------- transport
IDLE_OUT = np.radians(env('R43_IDLE_OUT', 16))   # game idle arm angle (the bind may be wider: A-pose)
side_dir = to_unity(np.array((np.sin(IDLE_OUT), 0, -np.cos(IDLE_OUT))))
radii = [float(np.mean(shape.ARM_R[:2])) - .003, float(np.mean(shape.ARM_R[1:])) - .003]
header = {
    'version': 1, 'sourceSha256': 'r43-gb-basic', 'animationClips': 0, 'bind': 'apose' if abs(shape.RELAX_OUT - np.degrees(IDLE_OUT)) > .5 else 'relaxed',
    'bones': bones,
    'shoulderHelpers': [{'name': p + s, 'fraction': f} for p, f in HELPERS for s in 'LR'],
    'idleArmDirection': [float(v) for v in side_dir],    # right arm; left mirrors x
    'armColliderRadii': radii,
    'contactSearch': [4, 3],    # irregular marching-cube skin: 4 seeds + 3-ring escape in SkinSurfaceReview
    'ignoreUpperArmTorso': True,    # the .051 upper arm overlaps the .15 Spine capsule at 16 deg and was pushed to 28 deg
    'meshes': [{'name': 'CHR_C1B_R43_SkinnedBody', 'vertices': int(len(P)), 'triangleIndices': int(tris.size)}],
}
if 'SLIM' in os.environ:
    # Trunk capsules for the slimmed body: the standing Spine/Chest radius is the mean of the chest half
    # width and half depth (the approved r43 gives the .15 the rig used), the limp ragdoll's spine/chest
    # radii follow the half depth so a downed body lies on its own surface.
    rows = shape.TORSO[(shape.TORSO[:, 0] > .3) & (shape.TORSO[:, 0] < .5)]
    half_w, half_d = float(rows[:, 1].mean()), float(rows[:, 2].mean())
    header['torsoColliderRadii'] = [round((half_w + half_d) / 2, 4), round(half_d * .95, 4), round(half_d, 4)]
blob = json.dumps(header).encode()
path = OUT / os.environ.get('R43_OUT_NAME', 'R43CharacterBase.bytes')
with open(path, 'wb') as fh:
    fh.write(struct.pack('<i', len(blob)))
    fh.write(blob)
    for arr, dt in ((to_unity(P), '<f4'), (to_unity(N), '<f4'), (ids, '<i4'), (wts, '<f4'), (tris.ravel(), '<i4')):
        fh.write(np.ascontiguousarray(arr, dt).tobytes())

report = {
    'vertices': int(len(P)), 'triangles': int(len(tris)),
    'shoulderPivot': [float(v) for v in arm_geo[1]['root']], 'upperArm': arm_geo[1]['upper'],
    'forearmToHandLogical': arm_geo[1]['fore'], 'terminal': TERMINAL,
    'rigTipFromShoulder': arm_geo[1]['upper'] + arm_geo[1]['fore'] + TERMINAL,
    'r40RigTipFromShoulder': .1805 + .14 + .06,
    'legCentreX': float(leg_x), 'idleArmDirection': header['idleArmDirection'], 'armColliderRadii': radii,
    'maxDroppedWeight': float(1 - np.sort(Wt, 1)[:, -4:].sum(1).min()),
    'boneWorld': {n: [round(float(v), 4) for v in W[i, :3, 3]] for i, n in enumerate(names)},
}
if hip_cfg is not None:
    report['hipHelper'] = hip_cfg
# Every shape/weight setting that differs from the script defaults, so the transport can be rebuilt exactly.
report['buildEnv'] = {k: v for k, v in sorted(os.environ.items()) if k.startswith(('R43_', 'SLIM', 'ARCH', 'NECK', 'LEG', 'TORSO', 'PIVOT', 'ARM_', 'CARVE', 'RELAX_OUT'))}
(OUT / 'BuildReport.json').write_text(json.dumps(report, indent=1))
print(json.dumps({k: v for k, v in report.items() if k != 'boneWorld'}, indent=1))
print('wrote', path, path.stat().st_size // 1024, 'KB')
