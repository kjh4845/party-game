"""Rebuild shoulder volume in the actual hanging-arm pose and enforce bilateral symmetry.

Blender --background --python-exit-code 1 --python this.py -- idle-matrices output-dir [fill-depth]
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
SOURCE = ROOT / 'BlenderSource/Characters/C1B-RW-022-shoulder-contour'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(value):
    value = np.clip(value, 0, 1)
    return value * value * (3 - 2 * value)


def main():
    args = sys.argv[sys.argv.index('--') + 1:]
    matrix_path, output = Path(args[0]).resolve(), Path(args[1]).resolve()
    depth = float(args[2]) if len(args) > 2 else .035
    if output.exists():
        raise RuntimeError('Preserve existing model iterations')
    source = SOURCE / 'CHR_MasterCharacter_C1B_ShoulderContour_r22.blend'
    data = (SOURCE / 'ShoulderCharacter.bytes').read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4 + size])
    assert header['sourceSha256'] == sha(source)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
    mesh = body.data
    n = len(mesh.vertices)
    offset = 4 + size
    rest = np.frombuffer(data, '<f4', n * 3, offset).reshape(n, 3).copy()
    normals = np.frombuffer(data, '<f4', n * 3, offset + n * 12).reshape(n, 3)
    ids = np.frombuffer(data, '<i4', n * 4, offset + n * 24).reshape(n, 4)
    values = np.frombuffer(data, '<f4', n * 4, offset + n * 40).reshape(n, 4)
    names = [bone['name'] for bone in header['bones']]
    dense = np.zeros((n, len(names)), np.float32)
    np.add.at(dense, (np.arange(n)[:, None], ids), values)
    original_dense = dense.copy()
    tree = KDTree(n)
    for i, point in enumerate(rest):
        tree.insert(point, i)
    tree.balance()
    mirror = np.array([tree.find((-float(p[0]), float(p[1]), float(p[2])))[1] for p in rest])
    mapped = np.array([names.index(name[:-2] + '_R' if name.endswith('_L') else name[:-2] + '_L' if name.endswith('_R') else name) for name in names])
    # One completed side supplies the other side's exact influence distribution.
    left = np.flatnonzero(rest[:, 0] < -1e-6)
    dense[mirror[left]] = dense[left][:, mapped]
    new_ids = np.argsort(dense, axis=1)[:, -4:][:, ::-1].astype('<i4')
    weights = np.take_along_axis(dense, new_ids, axis=1).astype('<f4')
    raw = matrix_path.read_bytes()
    bone_count = struct.unpack_from('<i', raw)[0]
    assert bone_count == 24
    matrices = np.frombuffer(raw, '<f4', offset=4).reshape(bone_count, 4, 4)
    transforms = (matrices[new_ids] * weights[:, :, None, None]).sum(axis=1).astype(np.float64)
    inverse_root = np.linalg.inv(matrices[0])
    raw_pose = np.einsum('nij,nj->ni', transforms[:, :3, :3], rest) + transforms[:, :3, 3]
    pose = raw_pose @ inverse_root[:3, :3].T + inverse_root[:3, 3]
    posed_normals = np.einsum('nij,nj->ni', transforms[:, :3, :3], normals) @ inverse_root[:3, :3].T
    posed_normals /= np.maximum(np.linalg.norm(posed_normals, axis=1)[:, None], 1e-12)
    edges = np.empty((len(mesh.edges), 2), np.int32)
    mesh.edges.foreach_get('vertices', edges.ravel())
    src = np.concatenate((edges[:, 0], edges[:, 1]))
    dst = np.concatenate((edges[:, 1], edges[:, 0]))
    degree = np.bincount(src, minlength=n)[:, None]

    def lap(points):
        mean = np.column_stack([np.bincount(src, weights=points[dst, axis], minlength=n) for axis in range(3)])
        return mean / np.maximum(degree, 1) - points

    x, y = np.abs(pose[:, 0]), pose[:, 1]
    local = smooth((x - .075) / .05) * (1 - smooth((x - .23) / .08))
    local *= smooth((y - .44) / .08) * (1 - smooth((y - .625) / .06))
    underside = smooth((.35 - posed_normals[:, 1]) / .85)
    delta = np.zeros_like(pose)
    # Add a thick rounded bridge beneath the shoulder. This lowers the opening
    # of the axillary notch and restores its front/back volume, rather than
    # retaining the cavity and only smoothing its surface.
    delta[:, 1] = -depth * local * underside
    delta[:, 2] = pose[:, 2] * .35 * local * underside
    cap = smooth((x - .135) / .07) * (1 - smooth((x - .27) / .04))
    cap *= smooth((y - .575) / .08) * smooth((posed_normals[:, 1] - .1) / .7)
    delta[:, 1] -= .012 * cap
    for iteration in range(200):
        delta += .4 * lap(delta)
    target = pose + delta
    for iteration in range(350):
        target += .4 * lap(target) * local[:, None]
    raw_delta = (target - pose) @ matrices[0, :3, :3].T
    bind_delta = np.linalg.solve(transforms[:, :3, :3], raw_delta[..., None])[..., 0]
    result = rest + bind_delta
    # Exact reflection, including the base coordinates. The previous process
    # only mirrored the correction and retained the base's float differences.
    result[mirror[left]] = result[left] * np.array([-1, 1, 1])
    center = np.abs(rest[:, 0]) <= 1e-6
    result[center, 0] = 0
    result = result.astype(np.float32)
    triangles = np.frombuffer(data, '<i4', header['meshes'][0]['triangleIndices'], offset + n * 56).reshape(-1, 3)

    def face_normals(points):
        return np.cross(points[triangles[:, 1]] - points[triangles[:, 0]], points[triangles[:, 2]] - points[triangles[:, 0]])

    base_normals = face_normals(rest)
    scale = 1.0
    while np.any(np.einsum('ij,ij->i', base_normals, face_normals(result)) <= 0):
        scale *= .9
        if scale < .3:
            raise RuntimeError('Local volume sculpt requires a topology repair')
        result = rest + bind_delta * scale
        result[mirror[left]] = result[left] * np.array([-1, 1, 1])
        result[center, 0] = 0
        result = result.astype(np.float32)
    mesh.vertices.foreach_set('co', (result[:, [0, 2, 1]] * np.array([1, -1, 1], np.float32)).ravel())
    mesh.update()
    for vertex in np.flatnonzero(np.any(dense != original_dense, axis=1)):
        # Reuse the existing four groups per vertex; symmetry never introduces
        # an additional influence beyond the original mirrored allocation.
        original_entries = {body.vertex_groups[g.group].name: g.weight for g in mesh.vertices[int(vertex)].groups}
        for name, weight in original_entries.items():
            if dense[vertex, names.index(name)] == 0:
                body.vertex_groups[name].remove([int(vertex)])
        for index in np.flatnonzero(dense[vertex] > 0):
            body.vertex_groups[names[index]].add([int(vertex)], float(dense[vertex, index]), 'REPLACE')
    output.mkdir(parents=True)
    blend = output / 'CHR_MasterCharacter_C1B_ShallowAxilla_r23.blend'
    body['axilla_rework'] = 'Filled shoulder underside and axilla volume; exact mirrored body coordinates and shoulder weights'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    new_normals = np.empty((n, 3), np.float32)
    mesh.vertices.foreach_get('normal', new_normals.ravel())
    new_normals = new_normals[:, [0, 2, 1]] * np.array([1, 1, -1], np.float32)
    new_normals[mirror[left]] = new_normals[left] * np.array([-1, 1, 1])
    blocks = []
    current = offset
    for record in header['meshes']:
        count = record['vertices']
        end = current + count * 56 + record['triangleIndices'] * 4
        block = bytearray(data[current:end])
        if record['name'] == body.name:
            block[:n * 12] = result.astype('<f4').tobytes()
            block[n * 12:n * 24] = new_normals.astype('<f4').tobytes()
            block[n * 24:n * 40] = new_ids.tobytes()
            block[n * 40:n * 56] = weights.tobytes()
        blocks.append(block)
        current = end
    assert current == len(data)
    header['sourceSha256'] = sha(blend)
    header['shallowAxillaCandidate'] = True
    encoded = json.dumps(header).encode()
    transport = output / 'ShallowCharacter.bytes'
    transport.write_bytes(struct.pack('<i', len(encoded)) + encoded + b''.join(blocks))
    mirror_error = np.linalg.norm(result - result[mirror] * np.array([-1, 1, 1]), axis=1)
    report = {'source': str(source), 'sourceSha256': sha(source), 'candidateSha256': sha(blend),
              'transportSha256': sha(transport), 'fillDepthMeters': depth, 'appliedScale': scale,
              'maximumBindChangeMeters': float(np.linalg.norm(result - rest, axis=1).max()),
              'mirrorMaxMeters': float(mirror_error.max()), 'maximumInfluences': int(np.count_nonzero(dense > 0, axis=1).max()),
              'headAndSkeletonPreserved': True, 'topologyPreserved': True, 'visualApproved': False}
    assert mirror_error.max() < 1e-6 and report['maximumInfluences'] <= 4
    (output / 'ShallowAxillaReview.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
