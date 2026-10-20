"""Fair the shoulder in its actual Unity idle pose, then solve back to bind space.

Blender --background --python-exit-code 1 --python this.py -- idle-matrices.bin output-dir
The source, skeleton, head, lower body and distal arms remain unchanged.
"""
from pathlib import Path
import hashlib
import json
import struct
import sys

import bpy
import numpy as np
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'BlenderSource/Characters/C1B-RW-021-surface-polish-r02'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(value):
    value = np.clip(value, 0, 1)
    return value * value * (3 - 2 * value)


def main():
    args = sys.argv[sys.argv.index('--') + 1:]
    matrices_path, output = Path(args[0]).resolve(), Path(args[1]).resolve()
    if output.exists():
        raise RuntimeError('Preserve existing model candidates')
    source_blend = SOURCE / 'CHR_MasterCharacter_C1B_SurfacePolish_r21.blend'
    source_transport = SOURCE / 'SurfaceCharacter.bytes'
    original_hash = sha(source_blend)
    data = source_transport.read_bytes()
    header_size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4 + header_size])
    assert header['sourceSha256'] == original_hash
    bpy.ops.wm.open_mainfile(filepath=str(source_blend), load_ui=False)
    body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
    mesh = body.data
    count = len(mesh.vertices)
    offset = 4 + header_size
    rest = np.frombuffer(data, dtype='<f4', count=count * 3, offset=offset).reshape(count, 3).copy()
    ids = np.frombuffer(data, dtype='<i4', count=count * 4, offset=offset + count * 24).reshape(count, 4)
    weights = np.frombuffer(data, dtype='<f4', count=count * 4, offset=offset + count * 40).reshape(count, 4)
    names = [bone['name'] for bone in header['bones']]
    original_weights = np.zeros((count, len(names)), np.float32)
    np.add.at(original_weights, (np.arange(count)[:, None], ids), weights)
    corrected_weights = original_weights.copy()
    for side in ['L', 'R']:
        upper = names.index('UpperArm_' + side)
        helper = names.index('ShoulderBlend_' + side)
        chest, spine = names.index('Chest'), names.index('Spine')
        torso = corrected_weights[:, chest] + corrected_weights[:, spine]
        transfer = np.minimum(corrected_weights[:, helper] * .6, np.minimum(2 * corrected_weights[:, upper], 2 * torso))
        corrected_weights[:, upper] -= transfer * .5
        for bone in [chest, spine]:
            corrected_weights[:, bone] -= transfer * .5 * np.divide(corrected_weights[:, bone], torso, out=np.zeros(count), where=torso > 1e-8)
        corrected_weights[:, helper] += transfer
    corrected_weights = np.maximum(corrected_weights, 0)
    corrected_weights /= corrected_weights.sum(axis=1)[:, None]
    weight_region = original_weights[:, names.index('ShoulderBlend_L')] + original_weights[:, names.index('ShoulderBlend_R')] > 0
    corrected_weights[~weight_region] = original_weights[~weight_region]
    new_ids = np.argsort(corrected_weights, axis=1)[:, -4:][:, ::-1].astype('<i4')
    new_weights = np.take_along_axis(corrected_weights, new_ids, axis=1).astype('<f4')
    assert np.count_nonzero(corrected_weights > 1e-7, axis=1).max() <= 4
    assert np.max(abs(new_weights.sum(axis=1) - 1)) < 1e-6
    raw = matrices_path.read_bytes()
    bone_count = struct.unpack_from('<i', raw)[0]
    assert bone_count == len(header['bones']) == 24
    matrices = np.frombuffer(raw, dtype='<f4', offset=4).reshape(bone_count, 4, 4)
    transform = np.sum(matrices[new_ids] * new_weights[:, :, None, None], axis=1).astype(np.float64)
    posed = np.einsum('nij,nj->ni', transform[:, :3, :3], rest) + transform[:, :3, 3]
    edges = np.empty((len(mesh.edges), 2), np.int32)
    mesh.edges.foreach_get('vertices', edges.ravel())
    source = np.concatenate((edges[:, 0], edges[:, 1]))
    destination = np.concatenate((edges[:, 1], edges[:, 0]))
    degree = np.bincount(source, minlength=count)[:, None]

    def laplacian(points):
        means = np.column_stack([np.bincount(source, weights=points[destination, axis], minlength=count) for axis in range(3)])
        return means / np.maximum(degree, 1) - points

    x, y = np.abs(rest[:, 0]), rest[:, 1]
    gate = smooth((x - .075) / .06) * (1 - smooth((x - .265) / .075))
    gate *= smooth((y - .44) / .07) * (1 - smooth((y - .675) / .055))
    # Work on the bent, skin-weighted surface: bind-pose smoothing alone misses
    # the shoulder cap and the pinched armpit when the arms hang down.
    fair = posed.copy()
    for iteration in range(1100):
        fair += .45 * laplacian(fair) * gate[:, None]
    displacement = fair - posed
    length = np.linalg.norm(displacement, axis=1)
    displacement *= np.minimum(1, .022 / np.maximum(length, 1e-12))[:, None]
    bind_delta = np.linalg.solve(transform[:, :3, :3], displacement[..., None])[..., 0]
    # Symmetrize the correction, without altering any original unedited vertex.
    tree = KDTree(count)
    for index, point in enumerate(rest):
        tree.insert(point, index)
    tree.balance()
    mirror = np.array([tree.find((-point[0], point[1], point[2]))[1] for point in rest])
    reflected = bind_delta[mirror] * np.array([-1, 1, 1])
    bind_delta = (bind_delta + reflected) * .5
    bind_delta[gate == 0] = 0
    result = (rest + bind_delta).astype(np.float32)
    triangles = np.frombuffer(data, dtype='<i4', count=header['meshes'][0]['triangleIndices'], offset=offset + count * 56).reshape(-1, 3)

    def face_normals(points):
        return np.cross(points[triangles[:, 1]] - points[triangles[:, 0]], points[triangles[:, 2]] - points[triangles[:, 0]])

    before_normals = face_normals(rest)
    scale = 1.0
    while np.any(np.einsum('ij,ij->i', before_normals, face_normals(result)) <= 0):
        scale *= .85
        if scale < .2:
            raise RuntimeError('Contour correction cannot preserve face orientation')
        result = (rest + bind_delta * scale).astype(np.float32)
    assert np.array_equal(result[gate == 0], rest[gate == 0])
    blender_points = result[:, [0, 2, 1]] * np.array([1, -1, 1], np.float32)
    mesh.vertices.foreach_set('co', blender_points.ravel())
    mesh.update()
    changed_weights = np.flatnonzero(np.max(abs(corrected_weights - original_weights), axis=1) > 1e-7)
    for vertex in changed_weights:
        for bone in range(len(names)):
            if abs(corrected_weights[vertex, bone] - original_weights[vertex, bone]) < 1e-7:
                continue
            group = body.vertex_groups[names[bone]]
            if corrected_weights[vertex, bone] > 0:
                group.add([int(vertex)], float(corrected_weights[vertex, bone]), 'REPLACE')
            else:
                group.remove([int(vertex)])
    body['shoulder_contour_scope'] = 'Symmetric shoulder contour plus reduced LBS volume collapse through existing skin helpers; skeleton preserved'
    output.mkdir(parents=True)
    blend = output / 'CHR_MasterCharacter_C1B_ShoulderContour_r22.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    normals = np.empty((count, 3), np.float32)
    mesh.vertices.foreach_get('normal', normals.ravel())
    blocks = []
    current_offset = offset
    for record in header['meshes']:
        n = record['vertices']
        end = current_offset + n * 56 + record['triangleIndices'] * 4
        block = bytearray(data[current_offset:end])
        if record['name'] == body.name:
            block[:n * 12] = result.astype('<f4').tobytes()
            block[n * 12:n * 24] = (normals[:, [0, 2, 1]] * np.array([1, 1, -1], np.float32)).astype('<f4').tobytes()
            block[n * 24:n * 40] = new_ids.tobytes()
            block[n * 40:n * 56] = new_weights.tobytes()
        blocks.append(block)
        current_offset = end
    assert current_offset == len(data)
    header['sourceSha256'] = sha(blend)
    header['shoulderContourCandidate'] = True
    encoded = json.dumps(header).encode()
    target = output / 'ShoulderCharacter.bytes'
    target.write_bytes(struct.pack('<i', len(encoded)) + encoded + b''.join(blocks))
    corrected_pose = np.einsum('nij,nj->ni', transform[:, :3, :3], result) + transform[:, :3, 3]
    selected = gate > .5
    report = {
        'source': str(source_blend), 'sourceSha256': original_hash,
        'candidate': str(blend), 'candidateSha256': sha(blend), 'transportSha256': sha(target),
        'changedVertices': int(np.count_nonzero(np.any(result != rest, axis=1))),
        'maximumBindDisplacementMeters': float(np.linalg.norm(result - rest, axis=1).max()),
        'maximumIdleDisplacementMeters': float(np.linalg.norm(corrected_pose - posed, axis=1).max()),
        'idleLocalLaplacianRmsBefore': float(np.sqrt(np.mean(laplacian(posed)[selected] ** 2))),
        'idleLocalLaplacianRmsAfter': float(np.sqrt(np.mean(laplacian(corrected_pose)[selected] ** 2))),
        'correctionScale': scale, 'passes': 1100, 'boneCount': bone_count,
        'shoulderWeightVerticesChanged': int(len(changed_weights)),
        'topologySkeletonHeadLowerBodyDistalArmsUnchanged': True, 'restFaceOrientationPreserved': True,
        'visualApproved': False,
    }
    assert sha(source_blend) == original_hash
    (output / 'ShoulderContourReview.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
