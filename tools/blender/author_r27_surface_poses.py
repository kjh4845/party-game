"""Author mirrored positional shoulder correctives from actual Unity poses.

Blender --background --python-exit-code 1 --python this.py -- shape-dir pose-dir output-dir [reuse-bank]
Includes real Blender shape keys and matching Unity position/normal frames.
"""
from pathlib import Path
import hashlib
import json
import struct
import sys

import bpy
import numpy as np
from mathutils import Matrix
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_r27_surface_refine import normals, smooth, sha

arguments = [Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:]]
source, poses, output = arguments[:3]
if output.exists():
    raise RuntimeError('Preserve previous candidates')
data = (source/'RefinedCharacter.bytes').read_bytes()
size = struct.unpack_from('<i', data)[0]
header = json.loads(data[4:4+size])
assert header['version'] == 1
blend = source/'CHR_MasterCharacter_C1B_SurfaceRefine_r27.blend'
assert sha(blend) == header['sourceSha256']
bpy.ops.wm.open_mainfile(filepath=str(blend), load_ui=False)
body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
mesh = body.data
n, offset = len(mesh.vertices), 4+size
v = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
base_normals = np.frombuffer(data, '<f4', n*3, offset+n*12).reshape(n, 3)
ids = np.frombuffer(data, '<i4', n*4, offset+n*24).reshape(n, 4)
weights = np.frombuffer(data, '<f4', n*4, offset+n*40).reshape(n, 4)
tri = np.frombuffer(data, '<i4', header['meshes'][0]['triangleIndices'], offset+n*56).reshape(-1, 3)
names = [r['name'] for r in header['bones']]
mapped = np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names])
mirror = np.load(source/'RefinementData.npz')['mirror']
reflect = np.diag([-1., 1, 1, 1])
rest_bones = []
for record in header['bones']:
    x, y, z, w = record['rotation']
    m = np.eye(4)
    m[:3, :3] = np.array([[1-2*(y*y+z*z), 2*(x*y-z*w), 2*(x*z+y*w)],
                         [2*(x*y+z*w), 1-2*(x*x+z*z), 2*(y*z-x*w)],
                         [2*(x*z-y*w), 2*(y*z+x*w), 1-2*(x*x+y*y)]])
    m[:3, 3] = record['position']
    rest_bones.append(m if record['parent'] < 0 else rest_bones[record['parent']]@m)
rest_bones = np.array(rest_bones)


def rotation(mats, first, second):
    a, b = names.index(first), names.index(second)
    rel = np.linalg.inv((mats[a]@rest_bones[a])[:3, :3])@(mats[b]@rest_bones[b])[:3, :3]
    q = Matrix(rel.tolist()).to_quaternion()
    return [float(q.x), float(q.y), float(q.z), float(q.w)]


def reference(label, mats, side, has_delta):
    return {'name': label+'_'+side, 'side': 0 if side == 'L' else 1,
            'rotation': rotation(mats, 'Chest', 'UpperArm_'+side),
            'torsoRotation': rotation(mats, 'Spine', 'Chest'),
            'elbowRotation': rotation(mats, 'UpperArm_'+side, 'Forearm_'+side), 'hasDelta': has_delta}


edges = np.empty((len(mesh.edges), 2), np.int32)
mesh.edges.foreach_get('vertices', edges.ravel())
src = np.concatenate((edges[:, 0], edges[:, 1]))
dst = np.concatenate((edges[:, 1], edges[:, 0]))
degree = np.maximum(np.bincount(src, minlength=n), 1)[:, None]


def lap(points):
    return np.column_stack([np.bincount(src, weights=points[dst, axis], minlength=n) for axis in range(3)])/degree-points


face_edges = np.sort(np.concatenate((tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]])), axis=1)
edge_faces = np.tile(np.arange(len(tri)), 3)
edge_key = face_edges[:, 0].astype(np.int64)*n+face_edges[:, 1]
order = np.argsort(edge_key)
shared = np.flatnonzero(np.diff(edge_key[order]) == 0)
pair_a, pair_b = edge_faces[order[shared]], edge_faces[order[shared+1]]


def repair_small_folds(points, region):
    """Relax crowded shoulder triangles locally, preserving the broad outline."""
    allowed = np.all(region[tri[pair_a]] > .02, axis=1)
    peak = 0.
    for attempt in range(12):
        face = np.cross(points[tri[:, 1]]-points[tri[:, 0]], points[tri[:, 2]]-points[tri[:, 0]])
        face /= np.maximum(np.linalg.norm(face, axis=1)[:, None], 1e-15)
        angles = np.degrees(np.arccos(np.clip(np.einsum('ij,ij->i', face[pair_a], face[pair_b]), -1, 1)))
        peak = float(angles[allowed].max())
        hot = np.flatnonzero(allowed & (angles > 32))
        if not len(hot):
            break
        vertices = np.unique(tri[np.concatenate((pair_a[hot], pair_b[hot]))])
        tree = KDTree(len(vertices))
        for i, vertex in enumerate(vertices):
            tree.insert(points[vertex], i)
        tree.balance()
        eligible = np.flatnonzero(region > .001)
        radius = .018 + attempt*.004
        distance = np.array([tree.find(points[i])[2] for i in eligible])
        selected = eligible[distance < radius]
        local_mask = np.zeros(n)
        local_mask[selected] = smooth(1-distance[distance < radius]/radius)*region[selected]
        selection = local_mask[src] > 0
        rows, cols = src[selection], dst[selection]
        subset = np.unique(np.concatenate((rows, cols)))
        index = np.full(n, -1)
        index[subset] = np.arange(len(subset))
        rows, cols = index[rows], index[cols]
        local = points[subset].copy()
        local_degree = np.maximum(np.bincount(rows, minlength=len(subset)), 1)[:, None]
        gain = local_mask[subset, None]*.5
        for _ in range(1800):
            average = np.column_stack([np.bincount(rows, weights=local[cols, axis], minlength=len(subset)) for axis in range(3)])/local_degree
            local += gain*(average-local)
        points[subset] = local
        print('local fold fairing', attempt, 'peak', peak, 'vertices', len(subset), flush=True)
    return peak


# Use the original material coordinates for smooth masks, not coordinates
# distorted by inverse binding. Restrict geometry correction to the shoulder.
original = np.load(source/'RefinementData.npz')
material = original['materialCoordinates'] if 'materialCoordinates' in original else v
mask = smooth((-material[:, 0]-.04)/.07)*(1-smooth((-material[:, 0]-.31)/.11))
mask *= smooth((material[:, 1]-.37)/.13)
neutral = np.frombuffer((poses/'IdleSkinMatrices.bin').read_bytes(), '<f4', offset=4).reshape(-1, 4, 4)
refs = [reference('Neutral', neutral, 'L', False),
        reference('Neutral', reflect@neutral[mapped]@reflect, 'R', False)]
blocks, reports = [], []
body.shape_key_add(name='Basis')
cases = [('Raise45', 'Raised_45_Matrices.bin'), ('Raise90', 'Raised_90_Matrices.bin'),
                    ('Prepare', 'Punch_02_Matrices.bin'), ('Accelerate', 'Punch_05_Matrices.bin'),
                    ('Strike', 'Punch_08_Matrices.bin'), ('FollowThrough', 'Punch_12_Matrices.bin'),
                    ('Recover', 'Punch_18_Matrices.bin')]
for label in ('Impact', 'Grab', 'Deflect', 'Reach', 'Exchange'):
    file = label+'_Matrices.bin'
    if (poses/file).exists():
        cases.append((label, file))
cached, cached_reports = {}, {}
if len(arguments) > 3:
    previous = arguments[3]
    old_data = (previous/'RefinedCharacter.bytes').read_bytes()
    old_size = struct.unpack_from('<i', old_data)[0]
    old_header = json.loads(old_data[4:4+old_size])
    old_offset = 4+old_size
    mesh_size = sum(r['vertices']*56+r['triangleIndices']*4 for r in header['meshes'])
    assert old_data[old_offset:old_offset+mesh_size] == data[offset:], 'Reuse requires identical base geometry, normals and weights'
    old_offset += mesh_size
    for ref in old_header['surfacePoses']:
        if ref['hasDelta']:
            cached[ref['name']] = (np.frombuffer(old_data, '<f4', n*3, old_offset).reshape(n, 3),
                                   np.frombuffer(old_data, '<f4', n*3, old_offset+n*12).reshape(n, 3))
            old_offset += n*24
    cached_reports = {r['pose']: r for r in json.loads((previous/'SurfaceRefineReview.json').read_text())['surfaceCorrectives']}
for label, file in cases:
    mats = np.frombuffer((poses/file).read_bytes(), '<f4', offset=4).reshape(-1, 4, 4).astype(float)
    if label+'_L' in cached:
        assert cached_reports[label]['referenceSha256'] == sha(poses/file)
        positional, normal_delta = cached[label+'_L']
        pose_report = cached_reports[label]
    else:
        t = (mats[ids]*weights[:, :, None, None]).sum(1)
        posed = np.einsum('nij,nj->ni', t[:, :3, :3], v)+t[:, :3, 3]
        fair = posed.copy()
        for _ in range(1200):
            fair += .5*lap(fair)*mask[:, None]
        peak = repair_small_folds(fair, mask)
        delta = fair-posed
        assert np.linalg.norm(delta, axis=1).max() < .045, 'Unexpected corrective displacement'
        positional = np.linalg.solve(t[:, :3, :3], delta[..., None])[..., 0]
        normal = np.linalg.solve(t[:, :3, :3], normals(posed+delta, tri)[..., None])[..., 0]
        normal /= np.maximum(np.linalg.norm(normal, axis=1)[:, None], 1e-12)
        normal_mask = smooth((-material[:, 0]-.005)/.025)*smooth((material[:, 1]-.32)/.10)
        normal_delta = (normal-base_normals)*normal_mask[:, None]
        pose_report = {'pose': label, 'referenceSha256': sha(poses/file),
                       'maximumPosedDisplacementMeters': float(np.linalg.norm(delta, axis=1).max()),
                       'shoulderMaximumAdjacentFaceAngle': peak}
    for side in ('L', 'R'):
        d = positional if side == 'L' else positional[mirror]*np.array([-1, 1, 1])
        nd = normal_delta if side == 'L' else normal_delta[mirror]*np.array([-1, 1, 1])
        pose = mats if side == 'L' else reflect@mats[mapped]@reflect
        refs.append(reference(label, pose, side, True))
        blocks.extend([d.astype('<f4').tobytes(), nd.astype('<f4').tobytes()])
        key = body.shape_key_add(name=label+'_'+side)
        key.data.foreach_set('co', ((v+d)[:, [0, 2, 1]]*np.array([1, -1, 1])).astype(np.float32).ravel())
        attr = mesh.attributes.new(name='r27_normal_'+label+'_'+side, type='FLOAT_VECTOR', domain='POINT')
        attr.data.foreach_set('vector', (nd[:, [0, 2, 1]]*np.array([1, -1, 1])).astype(np.float32).ravel())
    reports.append(pose_report)
    print('R27 corrective', reports[-1], flush=True)
body['surface_pose_references'] = json.dumps(refs)
output.mkdir(parents=True)
blend = output/'CHR_MasterCharacter_C1B_SurfaceRefine_r27.blend'
bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
header['version'], header['surfacePoses'], header['sourceSha256'] = 3, refs, sha(blend)
encoded = json.dumps(header).encode()
transport = output/'RefinedCharacter.bytes'
transport.write_bytes(struct.pack('<i', len(encoded))+encoded+data[offset:]+b''.join(blocks))
report = json.loads((source/'SurfaceRefineReview.json').read_text())
report.update({'candidateSha256': sha(blend), 'transportSha256': sha(transport), 'surfaceCorrectives': reports})
(output/'SurfaceRefineReview.json').write_text(json.dumps(report, indent=2)+'\n')
