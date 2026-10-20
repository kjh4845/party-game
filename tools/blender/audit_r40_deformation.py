"""Deformation audit for a skin transport: folds, collapse and self-intersection.

Blender --background --python this.py -- transport report.json [recorded-capture-dir ...]

Poses: a synthetic sweep from the idle arm direction (elevation 0..170 deg in the
forward, diagonal and outward planes, elbow flex and twist) for both arms,
plus every Unity-recorded *_Matrices.bin in the capture directories.
Metrics on the body mesh, split by region (shoulder / elbow / other):
- fold: largest increase of a neighbour-face angle relative to bind (deg)
- collapse: smallest face area ratio posed/bind
- intersections: triangle pairs that intersect without sharing a vertex
"""
import json
import sys
from pathlib import Path

import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_transport as rt  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:]
T = rt.Transport(args[0])
report_path = Path(args[1])
captures = [Path(p) for p in args[2:]]
import os
CORRECT = os.environ.get("R40_NO_CORRECTIVES") != "1"
body = T.meshes[0]
tri, rest = body['tri'], body['rest']
n = len(rest)
I = T.index
dense = np.zeros((n, len(T.names)))
for k in range(4):
    np.add.at(dense, (np.arange(n), body['ids'][:, k]), body['weights'][:, k])
elbow_v = np.zeros(n, bool)
shoulder_v = np.zeros(n, bool)
for s in 'LR':
    u, f = dense[:, I['UpperArm_' + s]], dense[:, I['Forearm_' + s]]
    elbow_v |= (u > .01) & (f > .01)
    trunk = dense[:, [I['Chest'], I['Spine'], I['Neck']]].sum(1)
    helpers = [i for n, i in I.items() if n.startswith('ShoulderBlend') and n.endswith('_' + s)]
    shoulder_v |= (dense[:, helpers].sum(1) > .01) | (dense[:, I['Clavicle_' + s]] > .01) | ((u > .01) & (trunk > .01))

edge_map = {}
for fi, (a, b, c) in enumerate(tri):
    for x, y in ((a, b), (b, c), (c, a)):
        edge_map.setdefault((min(x, y), max(x, y)), []).append(fi)
pairs = np.array([v for v in edge_map.values() if len(v) == 2])
elbow_e = elbow_v[tri[pairs[:, 0]]].any(1)
shoulder_e = shoulder_v[tri[pairs[:, 0]]].any(1)
tri_sets = [set(x) for x in tri.tolist()]


def face_data(P):
    a, b, c = P[tri[:, 0]], P[tri[:, 1]], P[tri[:, 2]]
    nrm = np.cross(b - a, c - a)
    area = np.linalg.norm(nrm, axis=1)
    return nrm / (area[:, None] + 1e-20), area


bind_n, bind_area = face_data(rest)
bind_angle = np.degrees(np.arccos(np.clip((bind_n[pairs[:, 0]] * bind_n[pairs[:, 1]]).sum(1), -1, 1)))


def audit(P):
    nrm, area = face_data(P)
    ang = np.degrees(np.arccos(np.clip((nrm[pairs[:, 0]] * nrm[pairs[:, 1]]).sum(1), -1, 1)))
    fold = ang - bind_angle
    k = int(np.argmax(fold))
    tree = BVHTree.FromPolygons([Vector(p) for p in P], tri.tolist())
    hits = [(i, j) for i, j in tree.overlap(tree) if i < j and not (tri_sets[i] & tri_sets[j])]
    region = lambda m: float(fold[m].max()) if m.any() else 0.0
    return {'shoulderFold': region(shoulder_e), 'elbowFold': region(elbow_e),
            'otherFold': region(~(shoulder_e | elbow_e)), 'fold': float(fold.max()),
            'collapse': float((area / (bind_area + 1e-20)).min()), 'intersections': len(hits),
            'worst': [round(float(x), 3) for x in P[tri[pairs[k, 0]]].mean(0)]}


results = []
for side in (1, 0):
    for azimuth in (0, 45, 90):
        for elevation in (0, 35, 70, 100, 130, 160):
            for flex, twist in ((0, 0), (60, 0), (100, 0), (130, 0), (0, -30), (0, 30)):
                if elevation == 0 and azimuth:
                    continue
                r = audit(T.skin(T.synthetic(side, elevation, azimuth, flex, twist), correct=CORRECT))
                r['pose'] = f"syn {'LR'[side]} elev{elevation} az{azimuth} flex{flex} twist{twist}"
                results.append(r)
for directory in captures:
    for path in sorted(directory.glob('*_Matrices.bin')):
        r = audit(T.skin(T.world_from_recorded(path), correct=CORRECT))
        r['pose'] = 'recorded ' + path.stem
        results.append(r)

summary = {
    'poses': len(results),
    'maxShoulderFold': max(r['shoulderFold'] for r in results),
    'maxElbowFold': max(r['elbowFold'] for r in results),
    'maxOtherFold': max(r['otherFold'] for r in results),
    'shoulderOver30': sum(r['shoulderFold'] > 30 for r in results),
    'shoulderOver45': sum(r['shoulderFold'] > 45 for r in results),
    'elbowOver30': sum(r['elbowFold'] > 30 for r in results),
    'elbowOver45': sum(r['elbowFold'] > 45 for r in results),
    'minCollapse': min(r['collapse'] for r in results),
    'posesWithIntersections': sum(r['intersections'] > 0 for r in results),
    'recordedWithIntersections': sum(r['intersections'] > 0 for r in results if r['pose'].startswith('recorded')),
}
print(json.dumps(summary, indent=1))
for key in ('shoulderFold', 'elbowFold', 'otherFold'):
    print('--- worst', key)
    for r in sorted(results, key=lambda r: -r[key])[:5]:
        print(f"{r['pose']:44s} {key} {r[key]:6.1f} collapse {r['collapse']:.2f} x{r['intersections']:4d} at {r['worst']}")
report_path.write_text(json.dumps({'summary': summary, 'results': results}, indent=1))
