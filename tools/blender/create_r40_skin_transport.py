"""Skin the r40 cage body and write a Unity skin transport.

python (Blender's) this.py cage-dir output.bytes [--correctives correctives.npz]

Skin weights are authored per cage vertex and carried to the surface with the
same limit operator as positions, so weights are as smooth as the surface.
- Torso/legs: transferred from r34 (approved lower-body/torso behaviour) and
  then low-passed by the cage.
- Shoulder: Chest -> ShoulderBlend (half rotation) -> UpperArm across the
  arm-root loops, so rotation is spread over the joint instead of one seam.
- Elbow: short blend centred on the elbow pivot; forearm keeps no upper-arm
  influence beyond it.
The bone header (24 bones) and head mesh are reused from r34 unchanged, so
the existing physics rig, colliders and joint frames fit without changes.
"""
import json
import os
import struct
import sys
from pathlib import Path

import numpy as np

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_cage_lib as lib  # noqa: E402
import r40_transport as rt  # noqa: E402

ROOT = Path(__file__).resolve().parents[2]
R34 = ROOT / 'BlenderSource/Characters/C1B-RW-034-fair-body/FairBodyCharacter.bytes'
NAMES = ('Root Pelvis Spine Chest Neck Head Clavicle_L UpperArm_L Forearm_L HandLogical_L Clavicle_R UpperArm_R '
         'Forearm_R HandLogical_R Thigh_L Calf_L Foot_L Thigh_R Calf_R Foot_R HipDeform_L HipDeform_R '
         'ShoulderBlend_L ShoulderBlend_R ShoulderBlendInner_L ShoulderBlendInner_R ShoulderBlendOuter_L '
         'ShoulderBlendOuter_R').split()
B = {n: i for i, n in enumerate(NAMES)}

# Shoulder rotation share per region (0 = torso, 1 = upper arm). Three skin-only helpers
# carry 1/4, 1/2 and 3/4 of the chest->upper-arm rotation, so a 100 degree swing is
# spread over four ~25 degree blends instead of two 50 degree ones.
HELPERS = (('ShoulderBlendInner_', .25), ('ShoulderBlend_', .5), ('ShoulderBlendOuter_', .75))
ARM_ROOT_T = {0: .42, 1: .60, 2: .78, 3: .92}
BORDER_T, NEAR_T = .25, .10
ARM_MID_RING = 9  # middle ring of the arm opening (create_r40_cage_body.ARM_ROWS[1])
FRONT_SCOPE = os.environ.get('R40_FRONT_SCOPE', 'border')  # border | all
FRONT_T_SCALE = float(os.environ.get('R40_FRONT_T_SCALE', 2.2))  # measured best on Unity punch captures (2026-10-15)
# Upper arm share per arm station around the elbow pivot (x = .3605).
ELBOW_UPPER = {4: 1.0, 5: .78, 6: .30, 7: .04}


def read_transport(path):
    data = path.read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4 + size])
    offset = 4 + size
    meshes = []
    for record in header['meshes']:
        n, t = record['vertices'], record['triangleIndices']
        pos = np.frombuffer(data, '<f4', n * 3, offset).reshape(n, 3)
        nor = np.frombuffer(data, '<f4', n * 3, offset + n * 12).reshape(n, 3)
        ids = np.frombuffer(data, '<i4', n * 4, offset + n * 24).reshape(n, 4)
        wts = np.frombuffer(data, '<f4', n * 4, offset + n * 40).reshape(n, 4)
        tri = np.frombuffer(data, '<i4', t, offset + n * 56)
        meshes.append(dict(record=record, pos=pos, nor=nor, ids=ids, wts=wts, tri=tri))
        offset += n * 56 + t * 4
    return header, meshes


def to_unity(p):
    return np.stack((p[:, 0], p[:, 2], -p[:, 1]), 1)


def dense_weights(ids, wts, count=len(NAMES)):
    W = np.zeros((len(ids), count))
    for k in range(4):
        np.add.at(W, (np.arange(len(ids)), ids[:, k]), wts[:, k])
    return W


def cage_weights(C, faces, tags, r34_body):
    n = len(C)
    limit = lib.limit_operator(n, faces) @ C  # cage vertex positions on the surface
    U = to_unity(limit)
    src = r34_body['pos']
    Wsrc = dense_weights(r34_body['ids'], r34_body['wts'])
    W = np.zeros((n, len(NAMES)))
    # r34 transfer: Gaussian average over nearby source vertices on the same side.
    for i, p in enumerate(U):
        d = np.linalg.norm(src - p, axis=1)
        near = d < max(.02, np.sort(d)[64])
        g = np.exp(-(d[near] / .012) ** 2)
        W[i] = (Wsrc[near] * g[:, None]).sum(0) / g.sum()
    # Upper body: torso skin follows Spine/Chest only; arm bones are re-authored below.
    for i, tag in enumerate(tags):
        if tag['region'] in ('torso', 'top') and U[i, 1] > .30:
            keep = W[i, [B['Pelvis'], B['Spine'], B['Chest']]]
            W[i] = 0
            W[i, [B['Pelvis'], B['Spine'], B['Chest']]] = keep / keep.sum()
    # Shoulder and arms.
    seg = lib.TORSO_SEGMENTS
    for i, tag in enumerate(tags):
        side = None
        t = None
        if tag['region'] == 'arm':
            side = tag['side']
            st = tag['station']
            if st in ARM_ROOT_T:
                t = ARM_ROOT_T[st]
            else:
                upper = ELBOW_UPPER.get(st, 0.0 if st > 7 else 1.0)
                s = 'R' if side > 0 else 'L'
                W[i] = 0
                W[i, B['UpperArm_' + s]] = upper
                W[i, B['Forearm_' + s]] = 1 - upper
                continue
        elif tag['region'] == 'hand':
            s = 'R' if tag['side'] > 0 else 'L'
            W[i] = 0
            W[i, B['Forearm_' + s]] = 1
            continue
        elif tag['region'] == 'torso':
            for sd, centre in ((1, seg // 4), (-1, 3 * seg // 4)):
                dk = min((tag['column'] - centre) % seg, (centre - tag['column']) % seg)
                dj = abs(tag['ring'] - ARM_MID_RING)
                if dk <= 1 and dj <= 1:
                    side, t = sd, BORDER_T
                elif dk <= 2 and dj <= 2:
                    side, t = sd, NEAR_T * (1 if max(dk, dj) == 2 and min(dk, dj) < 2 else .5)
        if side is None:
            continue
        if tag['region'] == 'torso' and C[i, 1] < -.015 and (t == BORDER_T or FRONT_SCOPE == 'all'):
            t *= FRONT_T_SCALE  # front armpit pillar: compressed when the arm swings forward
        s = 'R' if side > 0 else 'L'
        torso = W[i].copy()
        controls = [(0.0, None)] + [(f, B[name + s]) for name, f in HELPERS] + [(1.0, B['UpperArm_' + s])]
        W[i] = 0
        for (t0, b0), (t1, b1) in zip(controls, controls[1:]):
            if t0 <= t <= t1:
                k = (t - t0) / (t1 - t0)
                if b0 is None:
                    W[i] += torso * (1 - k)
                else:
                    W[i, b0] += 1 - k
                W[i, b1] += k
                break
    W /= W.sum(1, keepdims=True)
    return W


def with_helper_bones(bones):
    """Add the inner/outer shoulder helpers as copies of ShoulderBlend (same parent and rest)."""
    bones = [dict(b) for b in bones]
    names = [b['name'] for b in bones]
    for prefix in ('ShoulderBlendInner_', 'ShoulderBlendOuter_'):
        for s in 'LR':
            if prefix + s not in names:
                bones.append(dict(bones[names.index('ShoulderBlend_' + s)], name=prefix + s))
    assert [b['name'] for b in bones] == list(NAMES), [b['name'] for b in bones]
    return bones


def top4(W):
    order = np.argsort(-W, axis=1)[:, :4]
    vals = np.take_along_axis(W, order, 1)
    vals = np.where(vals > 1e-4, vals, 0)
    vals /= vals.sum(1, keepdims=True)
    return order.astype(np.int32), vals.astype(np.float32)


def build(cage_dir, levels=None):
    data = np.load(cage_dir / 'cage.npz')
    C, faces, tags = data['cage'], [tuple(f) for f in data['faces']], json.loads(str(data['tags']))
    # Export one level finer than the fit: the same limit surface, denser shading.
    levels = int(os.environ.get('R40_EXPORT_LEVELS', 3)) if levels is None else levels
    header, meshes = read_transport(R34)
    body = meshes[0]
    Wc = cage_weights(C, faces, tags, body)
    S, sub_faces = lib.subdivision(len(C), faces, levels)
    A = lib.limit_operator(S.shape[0], sub_faces) @ S
    P = A @ C
    W = A @ Wc
    bind = str(data['bind']) if 'bind' in data.files else 'tpose'
    header = dict(header, bones=with_helper_bones(header['bones']))
    if bind == 'idle':
        header = dict(header, bones=rt.idle_bones(header['bones']))
    return dict(C=C, faces=faces, tags=tags, A=A, S=S, sub_faces=sub_faces, P=P, W=W, Wc=Wc,
                header=header, head=meshes[1], bind=bind)


def write_transport(path, model, deltas=None, references=None):
    header = {k: v for k, v in model['header'].items() if k in ('bones',)}
    P = model['P']
    N = lib.vertex_normals(P, model['sub_faces'])
    ids, wts = top4(model['W'])
    tri = lib.triangulate(model['sub_faces']).ravel()
    head = model['head']
    header['version'] = 3 if references else 1
    header['sourceSha256'] = 'r40-cage'
    header['animationClips'] = 0
    header['cageBodyCandidate'] = True
    header['shoulderHelpers'] = [{'name': name + s, 'fraction': f} for name, f in HELPERS for s in 'LR']
    header['bind'] = model['bind']
    header['meshes'] = [
        {'name': 'CHR_C1B_R40_SkinnedBody', 'vertices': int(len(P)), 'triangleIndices': int(len(tri))},
        {'name': 'CHR_C1B_R40_SkinnedHead', 'vertices': int(len(head['pos'])), 'triangleIndices': int(len(head['tri']))},
    ]
    if references:
        header['surfacePoses'] = references
    blob = json.dumps(header).encode()
    with open(path, 'wb') as f:
        f.write(struct.pack('<i', len(blob)))
        f.write(blob)
        for pos, nor, i4, w4, t in (
                (to_unity(P), to_unity(N), ids, wts, tri),
                (head['pos'], head['nor'], head['ids'], head['wts'], head['tri'])):
            f.write(np.ascontiguousarray(pos, '<f4').tobytes())
            f.write(np.ascontiguousarray(nor, '<f4').tobytes())
            f.write(np.ascontiguousarray(i4, '<i4').tobytes())
            f.write(np.ascontiguousarray(w4, '<f4').tobytes())
            f.write(np.ascontiguousarray(t, '<i4').tobytes())
        if references:
            for ref, delta in zip(references, deltas):
                if ref['hasDelta']:
                    f.write(np.ascontiguousarray(delta['vertices'], '<f4').tobytes())
                    f.write(np.ascontiguousarray(delta['normals'], '<f4').tobytes())


if __name__ == '__main__':
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else sys.argv[1:]
    model = build(Path(args[0]))
    write_transport(Path(args[1]), model)
    ids, wts = top4(model['W'])
    print('vertices', len(model['P']), 'max dropped weight', float(1 - np.sort(model['W'], 1)[:, -4:].sum(1).min()))
