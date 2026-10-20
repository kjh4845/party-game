"""Bake shoulder/elbow pose correctives for the r40 cage body and write the final transport.

Blender --background --python this.py -- cage-dir output.bytes [report.json]

For each reference pose the control cage is posed with linear blend skinning
and the blended joint regions are relaxed with ARAP (edge lengths of the cage
are kept as rigid as possible, so the compressed side bulges instead of folding
and the stretched side spreads evenly). The relaxed cage is subdivided with the
same limit operator as the body, and the difference to runtime LBS is stored as
a bind-space blend shape. At runtime SurfacePoseCorrectives interpolates them:
mode 1 references by shoulder rotation only, mode 2 by elbow rotation only.
"""
import json
import os
import sys
import tempfile
from pathlib import Path

import numpy as np
from mathutils import Quaternion, Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_cage_lib as lib  # noqa: E402
import r40_transport as rt  # noqa: E402
import create_r40_skin_transport as skin  # noqa: E402
import r40_shape as shape  # noqa: E402

SHOULDER_REFS = [  # (name, elevation from idle, azimuth: 0 forward, 90 outward, 180 back, <0 across)
    ('Neutral', 0, 0), ('Fwd35', 35, 0), ('Fwd70', 70, 0), ('Fwd100', 100, 0), ('Fwd135', 135, 0),
    ('Fwd165', 165, 0), ('Out40', 40, 90), ('Out80', 80, 90), ('Out120', 120, 90), ('Diag60', 60, 45),
    ('Diag110', 110, 45), ('Back35', 35, 180), ('Across45', 45, -50),
]
# Dense elbow references: blending two distant ARAP targets folds the inner elbow.
# Clean up to ~105 degrees; beyond that the forearm meets the upper arm (self contact).
ELBOW_REFS = [('Elbow0', 0)] + [(f'Elbow{a}', a) for a in range(30, 151, 15)]
# Shoulder references are off: with three shoulder helpers and the rounded axilla, plain
# skinning folded less than any baked shoulder target over the recorded combat poses
# (ARAP or SDF targets compressed the front armpit when the arm rises forward).
BAKE_SHOULDER = os.environ.get('R40_BAKE_SHOULDER') == '1'
ARAP_ITERATIONS = 24
FIT_ITERATIONS = 8
FIT_SMOOTH = 1.5
TARGET_MODE = os.environ.get('R40_TARGET', 'fit')  # arap | fit | fit-release
# Unity mesh space <-> Blender space (x, y, z) = (x, -z_u, y_u)
U2B = np.array(((1, 0, 0), (0, 0, -1), (0, 1, 0)), float)
SHOULDER_RADIUS = .16


def polar(m):
    u, _, vt = np.linalg.svd(m)
    r = u @ vt
    if np.linalg.det(r) < 0:
        u[:, -1] *= -1
        r = u @ vt
    return r


class CageArap:
    def __init__(self, rest, faces):
        self.rest = rest
        self.neighbours = [set() for _ in range(len(rest))]
        for f in faces:
            for k in range(4):
                for m in (1, 2, 3):  # edges and diagonals keep quads from shearing
                    self.neighbours[f[k]].add(f[(k + m) % 4])

    def solve(self, initial, free, fixed_rotations):
        P = initial.copy()
        free_idx = np.flatnonzero(free)
        position = {v: i for i, v in enumerate(free_idx)}
        n = len(free_idx)
        L = np.zeros((n, n))
        for i, v in enumerate(free_idx):
            for u in self.neighbours[v]:
                L[i, i] += 1
                if u in position:
                    L[i, position[u]] -= 1
        R = np.repeat(np.eye(3)[None], len(P), 0)
        R[~free] = fixed_rotations[~free]
        for _ in range(ARAP_ITERATIONS):
            for v in free_idx:
                S = np.zeros((3, 3))
                for u in self.neighbours[v]:
                    S += np.outer(self.rest[v] - self.rest[u], P[v] - P[u])
                R[v] = polar(S).T
            b = np.zeros((n, 3))
            for i, v in enumerate(free_idx):
                for u in self.neighbours[v]:
                    b[i] += .5 * (R[v] + R[u]) @ (self.rest[v] - self.rest[u])
                    if u not in position:
                        b[i] += P[u]
            P[free_idx] = np.linalg.solve(L, b)
        return P


def reference_world(T, side, kind, a, b=0):
    if kind == 'shoulder':
        return T.synthetic(side, a, b, 0, 0)
    return T.synthetic(side, 0, 0, a, 0)


def relative(T, world, side):
    I = T.index
    s = 'LR'[side]
    c, u, f, sp = I['Chest'], I['UpperArm_' + s], I['Forearm_' + s], I['Spine']
    q = lambda m: rt.quat(m)
    to_list = lambda x: [x.x, x.y, x.z, x.w]
    return (to_list(q(np.linalg.inv(world[c]) @ world[u])), to_list(q(np.linalg.inv(world[u]) @ world[f])),
            to_list(q(np.linalg.inv(world[sp]) @ world[c])))


def main():
    args = sys.argv[sys.argv.index('--') + 1:]
    cage_dir, out = Path(args[0]), Path(args[1])
    model = skin.build(cage_dir)
    with tempfile.TemporaryDirectory() as tmp:
        base = Path(tmp) / 'base.bytes'
        skin.write_transport(base, model)
        T = rt.Transport(base)
    body = T.meshes[0]
    I = T.index
    A = model['A']
    cage_rest = skin.to_unity(model['C'])
    Wc = model['Wc']
    fine_rest = body['rest']
    fine_normals = body['normals']
    tri = body['tri']
    sub_faces = model['sub_faces']
    arap = CageArap(cage_rest, model['faces'])
    references, deltas, report = [], [], []
    tags = model['tags']
    data = np.load(cage_dir / 'cage.npz')
    torso_field = shape.TorsoField(data['torso_surface'], data['torso_faces'])
    L, _ = lib.umbrella(len(cage_rest), model['faces'])
    LtL = L.T @ L
    part = A @ np.array([t['region'] in ('arm', 'hand') for t in tags], float)
    idle_arms = shape.idle_arms()  # index 0 = +x (R), 1 = -x (L)
    pivots = [T.bind[I['UpperArm_' + s], :3, 3] for s in 'LR']
    # Unity +x is the R side.
    rigid_arm = [np.array([t['region'] in ('arm', 'hand') and t.get('side') == sd and
                           (t['region'] == 'hand' or t['station'] >= 4) for t in tags]) for sd in (-1, 1)]
    other_arm = [np.array([t['region'] in ('arm', 'hand') and t.get('side') == -sd for t in tags]) for sd in (-1, 1)]

    for side in (0, 1):
        s = 'LR'[side]
        shoulder_bones = [I['ShoulderBlend_' + s], I['UpperArm_' + s], I['Clavicle_' + s]]
        elbow_bones = [I['Forearm_' + s], I['HandLogical_' + s]]
        for kind, refs in (('shoulder', SHOULDER_REFS if BAKE_SHOULDER else []), ('elbow', ELBOW_REFS)):
            for ref in refs:
                name = ref[0]
                world = reference_world(T, side, kind, *ref[1:])
                rotation, elbow, torso = relative(T, world, side)
                record = {'name': f'{name}_{s}', 'side': side, 'rotation': rotation, 'elbowRotation': elbow,
                          'torsoRotation': torso, 'mode': 1 if kind == 'shoulder' else 2, 'hasDelta': False}
                if ref[1] == 0:
                    references.append(record)
                    deltas.append(None)
                    continue
                palette = T.palette(world)
                # Free only the cage vertices that blend bones rotating differently in this pose;
                # everything else stays rigid with its bones.
                rot = [rt.quat(m) for m in palette]
                free = np.zeros(len(Wc), bool)
                for v in range(len(Wc)):
                    used = np.flatnonzero(Wc[v] > .003)
                    spread = max((rt.angle(rot[a], rot[b]) for a in used for b in used if a < b), default=0)
                    free[v] = spread > .5
                if kind == 'shoulder':
                    # The rotation is spread over the whole shoulder neighbourhood, not only
                    # the weighted seam: torso skin near the joint relaxes too.
                    near = np.linalg.norm(cage_rest - pivots[side], axis=1) < SHOULDER_RADIUS
                    free |= near & ~rigid_arm[side] & ~other_arm[side]
                Mc = np.einsum('vb,bij->vij', Wc, palette)
                cage_lbs = np.einsum('vij,vj->vi', Mc[:, :3, :3], cage_rest) + Mc[:, :3, 3]
                fixed_rot = np.array([polar(m[:3, :3]) for m in Mc])
                relaxed = arap.solve(cage_lbs, free, fixed_rot)
                if kind == 'shoulder' and TARGET_MODE != 'arap':
                    # Fit the relaxed cage to the posed body: torso + this arm's tube rotated by the
                    # upper arm, joined by the shoulder fillet. ARAP only supplies the vertex layout.
                    m = palette[I['UpperArm_' + s]]
                    origin, direction = idle_arms[0 if s == 'R' else 1]
                    o_u = m[:3, :3] @ (U2B.T @ origin) + m[:3, 3]
                    d_u = m[:3, :3] @ (U2B.T @ direction)
                    arms = [(U2B @ o_u, U2B @ d_u / np.linalg.norm(d_u)), idle_arms[1 if s == 'R' else 0]]
                    rb = relaxed @ U2B.T
                    fitted = shape.fit(A, LtL, rb, torso_field, arms, part, FIT_ITERATIONS, FIT_SMOOTH, reference=rb,
                                       quiet=True, release=TARGET_MODE == 'fit-release')
                    keep = np.zeros(len(free), bool)
                    keep[free] = True
                    relaxed = np.where(keep[:, None], fitted @ U2B, relaxed)
                target = A @ relaxed
                influence = (np.abs(A[:, free]).sum(1) > 1e-6)
                Mf = (palette[body['ids']] * body['weights'][:, :, None, None]).sum(1)
                M3 = Mf[:, :3, :3]
                lbs = np.einsum('vij,vj->vi', M3, fine_rest) + Mf[:, :3, 3]
                inv = np.linalg.inv(M3)
                delta = np.einsum('vij,vj->vi', inv, target - lbs)
                delta[~influence] = 0
                normals = lib.vertex_normals(target, sub_faces)
                nb = np.einsum('vij,vj->vi', inv, normals)
                nb /= np.linalg.norm(nb, axis=1, keepdims=True)
                dn = nb - fine_normals
                dn[~influence] = 0
                record['hasDelta'] = True
                references.append(record)
                deltas.append({'vertices': delta, 'normals': dn})
                report.append({'name': record['name'], 'maxDelta': float(np.linalg.norm(delta, axis=1).max()),
                               'vertices': int(influence.sum())})
                print(f"{record['name']:14s} max delta {report[-1]['maxDelta']*1000:6.2f} mm over {report[-1]['vertices']} vertices")
    skin.write_transport(out, model, deltas, references)
    if len(args) > 2:
        Path(args[2]).write_text(json.dumps(report, indent=1))


if __name__ == '__main__':
    main()
