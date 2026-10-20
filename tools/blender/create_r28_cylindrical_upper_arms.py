"""Fill r27's pinched upper arms with a round envelope in neutral and pose keys.

Blender --background --python-exit-code 1 --python this.py -- output-directory
Only the upper-arm surface changes. Existing weights, rig and combat stay intact.
"""
from pathlib import Path
import json
import struct
import sys

import bpy
import numpy as np
from mathutils import Quaternion
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_r27_surface_refine import sha, smooth, normals

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT/'BlenderSource/Characters/C1B-RW-027-surface-refine'


def main():
    output = Path(sys.argv[sys.argv.index('--')+1]).resolve()
    if output.exists():
        raise RuntimeError('Preserve existing revisions')
    blend = SOURCE/'CHR_MasterCharacter_C1B_SurfaceRefine_r27.blend'
    data = (SOURCE/'RefinedCharacter.bytes').read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4+size])
    assert header['sourceSha256'] == sha(blend) and header['version'] == 3
    bpy.ops.wm.open_mainfile(filepath=str(blend), load_ui=False)
    body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
    mesh = body.data
    n, offset = len(mesh.vertices), 4+size
    rest = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3).copy()
    base_normals = np.frombuffer(data, '<f4', n*3, offset+n*12).reshape(n, 3).copy()
    ids = np.frombuffer(data, '<i4', n*4, offset+n*24).reshape(n, 4)
    weights = np.frombuffer(data, '<f4', n*4, offset+n*40).reshape(n, 4)
    triangles = np.frombuffer(data, '<i4', header['meshes'][0]['triangleIndices'], offset+n*56).reshape(-1, 3)
    names = [r['name'] for r in header['bones']]
    mapped = np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names])
    tree = KDTree(n)
    for i, point in enumerate(rest):
        tree.insert(point, i)
    tree.balance()
    mirror = np.array([tree.find((-float(p[0]), float(p[1]), float(p[2])))[1] for p in rest])
    left = np.flatnonzero(rest[:, 0] < -1e-6)
    reflect = np.diag([-1., 1, 1, 1])
    rest_bones = []
    for r in header['bones']:
        x, y, z, w = r['rotation']
        m = Quaternion((w, x, y, z)).to_matrix().to_4x4()
        m.translation = r['position']
        rest_bones.append(m if r['parent'] < 0 else rest_bones[r['parent']]@m)
    rest_bones = np.array(rest_bones)
    # Material coordinates identify the arm shaft independently of its pose.
    old_data = (ROOT/'BlenderSource/Characters/C1B-RW-026-smooth-body/SmoothCharacter.bytes').read_bytes()
    old_offset = 4+struct.unpack_from('<i', old_data)[0]
    material = np.frombuffer(old_data, '<f4', n*3, old_offset).reshape(n, 3)
    reach = np.abs(material[:, 0])
    mask = smooth((reach-.19)/.075)*(1-smooth((reach-.34)/.075))
    mask *= smooth((material[:, 1]-.52)/.045)
    left_mask = mask*(material[:, 0] < 0)
    affected = mask > 0
    normal_region = affected.copy()
    normal_region[np.unique(triangles[np.any(affected[triangles], axis=1)])] = True
    edges = np.empty((len(mesh.edges), 2), np.int32)
    mesh.edges.foreach_get('vertices', edges.ravel())
    edge_src = np.concatenate((edges[:, 0], edges[:, 1]))
    edge_dst = np.concatenate((edges[:, 1], edges[:, 0]))
    transition = left_mask
    active_edges = transition[edge_src] > 0
    rows, cols = edge_src[active_edges], edge_dst[active_edges]
    subset = np.unique(np.concatenate((rows, cols)))
    index = np.full(n, -1)
    index[subset] = np.arange(len(subset))
    rows, cols = index[rows], index[cols]
    degree = np.maximum(np.bincount(rows, minlength=len(subset)), 1)[:, None]
    gain = transition[subset, None]*.5

    def fair_connection(points, side):
        selected = subset if side == 'L' else mirror[subset]
        local = points[selected].copy()
        for _ in range(90):
            average = np.column_stack([np.bincount(rows, weights=local[cols, axis], minlength=len(subset)) for axis in range(3)])/degree
            local += gain*(average-local)
        points[selected] = local
        return points

    def polish_volume(points, side):
        selected = subset if side == 'L' else mirror[subset]
        local = points[selected].copy()
        def lap(p):
            return np.column_stack([np.bincount(rows, weights=p[cols, axis], minlength=len(subset)) for axis in range(3)])/degree-p
        # Paired passes soften the attachment's curvature without the volume
        # loss that made r27's upper arms look pinched.
        for _ in range(400):
            local += gain*lap(local)
            local -= 1.02*gain*lap(local)
        points[selected] = local
        return points

    face_edges = np.sort(np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]])), axis=1)
    face_ids = np.tile(np.arange(len(triangles)), 3)
    keys = face_edges[:, 0].astype(np.int64)*n+face_edges[:, 1]
    order = np.argsort(keys)
    shared = np.flatnonzero(np.diff(keys[order]) == 0)
    first_face, second_face = face_ids[order[shared]], face_ids[order[shared+1]]

    def relax_attachment(points, side):
        region = left_mask if side == 'L' else left_mask[mirror]
        # Relax only small folded neighbourhoods. A hard pin at a cross-section
        # would move a crease to the edge of that pin instead of resolving it.
        allowed = np.all(region[triangles[first_face]] > .002, axis=1)
        for attempt in range(14):
            face = np.cross(points[triangles[:, 1]]-points[triangles[:, 0]], points[triangles[:, 2]]-points[triangles[:, 0]])
            face /= np.maximum(np.linalg.norm(face, axis=1)[:, None], 1e-15)
            angles = np.degrees(np.arccos(np.clip(np.einsum('ij,ij->i', face[first_face], face[second_face]), -1, 1)))
            hot = np.flatnonzero(allowed & (angles > 18))
            if not len(hot):
                break
            bad = np.unique(triangles[np.concatenate((first_face[hot], second_face[hot]))])
            search = KDTree(len(bad))
            for i, vertex in enumerate(bad):
                search.insert(points[vertex], i)
            search.balance()
            eligible = np.flatnonzero(region > .001)
            distances = np.array([search.find(points[i])[2] for i in eligible])
            radius = .016+attempt*.004
            local_mask = np.zeros(n)
            local_mask[eligible] = smooth(1-distances/radius)*region[eligible]
            selected = local_mask[edge_src] > 0
            row, col = edge_src[selected], edge_dst[selected]
            vertices = np.unique(np.concatenate((row, col)))
            indices = np.full(n, -1)
            indices[vertices] = np.arange(len(vertices))
            row, col = indices[row], indices[col]
            count = np.maximum(np.bincount(row, minlength=len(vertices)), 1)[:, None]
            p = points[vertices].copy()
            strength = local_mask[vertices, None]*.5
            for _ in range(1800):
                average = np.column_stack([np.bincount(row, weights=p[col, axis], minlength=len(vertices)) for axis in range(3)])/count
                p += strength*(average-p)
            points[vertices] = p
        return points

    def palette(file):
        return np.frombuffer((SOURCE/'ReferencePoses'/file).read_bytes(), '<f4', offset=4).reshape(-1, 4, 4).astype(float)

    def transforms(mats):
        return (mats[ids]*weights[:, :, None, None]).sum(1)

    def pose(t, points):
        return np.einsum('nij,nj->ni', t[:, :3, :3], points)+t[:, :3, 3]

    # Fit the existing skin's centerline, which differs from the bone axis
    # after r27's shoulder fairing. Material angles preserve ring ordering.
    fits = {}
    u = (reach-.32)/.12
    polynomial = np.column_stack((np.ones(n), u, u*u, u*u*u))
    for side in ('L', 'R'):
        upper_id, fore_id = names.index('UpperArm_'+side), names.index('Forearm_'+side)
        start = rest_bones[upper_id, :3, 3]
        direction = rest_bones[fore_id, :3, 3]-start
        direction /= np.linalg.norm(direction)
        radial = material-(start+((material-start)@direction)[:, None]*direction)
        front = np.array([0., 0, 1])
        angle = np.arctan2(radial@front, radial@np.cross(front, direction))
        design = np.column_stack((polynomial, polynomial[:, :3]*np.cos(angle)[:, None],
                                  polynomial[:, :3]*np.sin(angle)[:, None]))
        sign = -1 if side == 'L' else 1
        samples = np.flatnonzero((material[:, 0]*sign > 0) & (reach > .23) & (reach < .43) & (material[:, 1] > .55))
        active = np.flatnonzero(mask*(material[:, 0]*sign > 0) > 0)
        fits[side] = samples, active, angle[active], np.linalg.pinv(design[samples])

    def fill(points, mats, side):
        samples, active, angle, inverse = fits[side]
        coefficients = inverse@points[samples]
        center = polynomial[active]@coefficients[:4]
        tangent = coefficients[1]+2*u[active, None]*coefficients[2]+3*u[active, None]**2*coefficients[3]
        tangent /= np.maximum(np.linalg.norm(tangent, axis=1)[:, None], 1e-9)
        first = polynomial[active, :3]@coefficients[4:7]
        original_second = polynomial[active, :3]@coefficients[7:10]
        first -= tangent*np.einsum('ij,ij->i', tangent, first)[:, None]
        first /= np.maximum(np.linalg.norm(first, axis=1)[:, None], 1e-9)
        second = np.cross(tangent, first)
        second *= np.sign(np.einsum('ij,ij->i', second, original_second))[:, None]
        cylinder = center+.038*(np.cos(angle)[:, None]*first+np.sin(angle)[:, None]*second)
        delta = np.zeros_like(points)
        delta[active] = (cylinder-points[active])*mask[active, None]
        # Fair the displacement, keeping the new round section's volume.
        return polish_volume(relax_attachment(points+fair_connection(delta, side), side), side)

    def inverse_normals(t, points):
        result = np.linalg.solve(t[:, :3, :3], normals(points, triangles)[..., None])[..., 0]
        return result/np.maximum(np.linalg.norm(result, axis=1)[:, None], 1e-12)

    neutral = palette('IdleSkinMatrices.bin')
    neutral_t = transforms(neutral)
    before = pose(neutral_t, rest)
    target = fill(fill(before, neutral, 'L'), neutral, 'R')
    result = rest+np.linalg.solve(neutral_t[:, :3, :3], (target-before)[..., None])[..., 0]
    result[mirror[left]] = result[left]*np.array([-1, 1, 1])
    result = result.astype('<f4')
    result[~affected] = rest[~affected]
    base_delta = result-rest
    new_normals = base_normals.copy()
    recalculated = inverse_normals(neutral_t, pose(neutral_t, result))
    new_normals[normal_region] = recalculated[normal_region]
    new_normals[mirror[left]] = new_normals[left]*np.array([-1, 1, 1])
    mesh.vertices.foreach_set('co', (result[:, [0, 2, 1]]*np.array([1, -1, 1], np.float32)).ravel())
    mesh.shape_keys.key_blocks['Basis'].data.foreach_set('co', (result[:, [0, 2, 1]]*np.array([1, -1, 1], np.float32)).ravel())
    mesh.normals_split_custom_set_from_vertices((new_normals[:, [0, 2, 1]]*np.array([1, -1, 1])).tolist())
    mesh.attributes['r27_normal_base'].data.foreach_set('vector', (new_normals[:, [0, 2, 1]]*np.array([1, -1, 1], np.float32)).ravel())
    blocks, current = [], offset
    for record in header['meshes']:
        count = record['vertices']
        end = current+count*56+record['triangleIndices']*4
        block = bytearray(data[current:end])
        if record['name'] == body.name:
            block[:n*12] = result.tobytes()
            block[n*12:n*24] = new_normals.astype('<f4').tobytes()
        blocks.append(block)
        current = end
    pose_files = {'Raise45': 'Raised_45_Matrices.bin', 'Raise90': 'Raised_90_Matrices.bin',
                  'Prepare': 'Punch_02_Matrices.bin', 'Accelerate': 'Punch_05_Matrices.bin',
                  'Strike': 'Punch_08_Matrices.bin', 'FollowThrough': 'Punch_12_Matrices.bin',
                  'Recover': 'Punch_18_Matrices.bin'}
    pose_reports = []
    for ref in header['surfacePoses']:
        if not ref['hasDelta']:
            continue
        old_delta = np.frombuffer(data, '<f4', n*3, current).reshape(n, 3)
        old_normal_delta = np.frombuffer(data, '<f4', n*3, current+n*12).reshape(n, 3)
        current += n*24
        label, side = ref['name'].rsplit('_', 1)
        mats = palette(pose_files.get(label, label+'_Matrices.bin'))
        if side == 'R':
            mats = reflect@mats[mapped]@reflect
        t = transforms(mats)
        old_posed = pose(t, rest+old_delta)
        target = fill(old_posed, mats, side)
        side_region = affected & (material[:, 0] < 0 if side == 'L' else material[:, 0] > 0)
        new_delta = old_delta.copy()
        adjustment = np.linalg.solve(t[:, :3, :3], (target-old_posed)[..., None])[..., 0]-base_delta
        new_delta[side_region] += adjustment[side_region]
        evaluated = pose(t, result+new_delta)
        correct_normals = inverse_normals(t, evaluated)
        new_normal_delta = old_normal_delta.copy()
        selected_normals = normal_region & (material[:, 0] < 0 if side == 'L' else material[:, 0] > 0)
        new_normal_delta[selected_normals] = correct_normals[selected_normals]-new_normals[selected_normals]
        blocks.extend([new_delta.astype('<f4').tobytes(), new_normal_delta.astype('<f4').tobytes()])
        key = mesh.shape_keys.key_blocks[ref['name']]
        key.data.foreach_set('co', ((result+new_delta)[:, [0, 2, 1]]*np.array([1, -1, 1], np.float32)).ravel())
        mesh.attributes['r27_normal_'+ref['name']].data.foreach_set('vector', (new_normal_delta[:, [0, 2, 1]]*np.array([1, -1, 1], np.float32)).ravel())
        pose_reports.append({'pose': ref['name'], 'maximumFillMeters': float(np.linalg.norm(target-old_posed, axis=1).max())})
    assert current == len(data)
    mesh.update()
    body['upper_arm_revision'] = 'r28: round biceps/triceps envelope in neutral and every corrective; r27 torso/rig/weights preserved'
    output.mkdir(parents=True)
    blend = output/'CHR_MasterCharacter_C1B_CylindricalArms_r28.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend), compress=True)
    header['sourceSha256'] = sha(blend)
    header['cylindricalUpperArms'] = True
    encoded = json.dumps(header).encode()
    transport = output/'CylindricalCharacter.bytes'
    transport.write_bytes(struct.pack('<i', len(encoded))+encoded+b''.join(blocks))
    report = {'source': str(SOURCE), 'candidateSha256': sha(blend), 'transportSha256': sha(transport),
              'editedVertices': int(np.any(result != rest, axis=1).sum()),
              'maximumNeutralFillMeters': float(np.linalg.norm(pose(neutral_t, result)-before, axis=1).max()),
              'bodyOutsideUpperArmsPreserved': bool(np.array_equal(result[~affected], rest[~affected])),
              'weightsSkeletonTopologyPreserved': True,
              'mirrorMaxMeters': float(np.linalg.norm(result-result[mirror]*np.array([-1, 1, 1]), axis=1).max()),
              'poseFills': pose_reports, 'visualApproved': False}
    (output/'CylindricalArmsReview.json').write_text(json.dumps(report, indent=2)+'\n')
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
