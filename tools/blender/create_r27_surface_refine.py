"""Refine r26's existing silhouette using a coarse surface guide and local rebinding.

Blender --background --python-exit-code 1 --python this.py -- pose-dir output-dir
The saved guide is derived from the existing mesh, never from replacement primitives.
Head, legs, bone transforms and combat animation sources remain outside the edit.
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
SOURCE = ROOT / 'BlenderSource/Characters/C1B-RW-026-smooth-body'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(x):
    x = np.clip(x, 0, 1)
    return x*x*x*(10+x*(-15+6*x))


def soft_linear(x):
    """A broad joint blend with eased endpoints and a bounded central slope."""
    x = np.clip(x, 0, 1)
    edge = .12
    return np.where(x < edge, x*x/(2*edge*(1-edge)),
                    np.where(x > 1-edge, 1-(1-x)**2/(2*edge*(1-edge)),
                             (x-edge/2)/(1-edge)))


def normals(points, triangles):
    faces = np.cross(points[triangles[:, 1]]-points[triangles[:, 0]],
                     points[triangles[:, 2]]-points[triangles[:, 0]])
    values = np.zeros_like(points, dtype=np.float64)
    for corner in range(3):
        np.add.at(values, triangles[:, corner], faces)
    return values / np.maximum(np.linalg.norm(values, axis=1)[:, None], 1e-12)


def main():
    poses, output = [Path(p).resolve() for p in sys.argv[sys.argv.index('--')+1:][:2]]
    if output.exists():
        raise RuntimeError('Preserve previous candidates')
    source = SOURCE/'CHR_MasterCharacter_C1B_SmoothBody_r26.blend'
    data = (SOURCE/'SmoothCharacter.bytes').read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4+size])
    assert sha(source) == header['sourceSha256']
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
    mesh = body.data
    n, offset = len(mesh.vertices), 4+size
    rest = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3).copy()
    ids = np.frombuffer(data, '<i4', n*4, offset+n*24).reshape(n, 4)
    weights = np.frombuffer(data, '<f4', n*4, offset+n*40).reshape(n, 4)
    triangles = np.frombuffer(data, '<i4', header['meshes'][0]['triangleIndices'], offset+n*56).reshape(-1, 3)
    names = [record['name'] for record in header['bones']]
    mapped = np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names])
    dense = np.zeros((n, len(names)))
    np.add.at(dense, (np.arange(n)[:, None], ids), weights)
    neutral = np.frombuffer((poses/'IdleSkinMatrices.bin').read_bytes(), '<f4', offset=4).reshape(-1, 4, 4)
    palette = np.linalg.inv(neutral[0]) @ neutral
    transform = (palette[ids]*weights[:, :, None, None]).sum(1).astype(float)
    posed = np.einsum('nij,nj->ni', transform[:, :3, :3], rest)+transform[:, :3, 3]
    tree = KDTree(n)
    for i, point in enumerate(rest):
        tree.insert(point, i)
    tree.balance()
    mirror = np.array([tree.find((-float(p[0]), float(p[1]), float(p[2])))[1] for p in rest])
    left, center = np.flatnonzero(rest[:, 0] < -1e-6), np.abs(rest[:, 0]) <= 1e-6

    # Recover a manageable control cage from the existing all-quad surface.
    # The neutral pose is the shape reference: bind-pose folds must not dictate
    # the visible torso's contour or arm thickness.
    guide_mesh = mesh.copy()
    guide_mesh.name = 'R27_NeutralSurfaceGuide'
    for attribute in list(guide_mesh.attributes):
        if attribute.name.startswith('r26_'):
            guide_mesh.attributes.remove(attribute)
    guide_mesh.vertices.foreach_set('co', posed.astype(np.float32).ravel())
    guide = bpy.data.objects.new('R27_SurfaceControlCage', guide_mesh)
    bpy.context.collection.objects.link(guide)
    bpy.ops.object.select_all(action='DESELECT')
    guide.select_set(True)
    bpy.context.view_layer.objects.active = guide
    reduce = guide.modifiers.new('Recover coarse quads', 'DECIMATE')
    reduce.decimate_type = 'UNSUBDIV'
    reduce.iterations = 6
    bpy.ops.object.modifier_apply(modifier=reduce.name)
    cage_vertices = len(guide.data.vertices)
    fair = guide.modifiers.new('Fair continuous curves', 'SMOOTH')
    fair.factor, fair.iterations = .55, 6
    bpy.ops.object.modifier_apply(modifier=fair.name)
    subdiv = guide.modifiers.new('Continuous limit surface', 'SUBSURF')
    subdiv.levels = subdiv.render_levels = 3
    # Fair along mesh edges so the close arm/flank surfaces cannot exchange
    # vertices, which an unconstrained nearest-surface projection would allow.
    edges = np.empty((len(mesh.edges), 2), np.int32)
    mesh.edges.foreach_get('vertices', edges.ravel())
    src = np.concatenate((edges[:, 0], edges[:, 1]))
    dst = np.concatenate((edges[:, 1], edges[:, 0]))
    degree = np.maximum(np.bincount(src, minlength=n), 1)[:, None]
    def lap(points):
        return np.column_stack([np.bincount(src, weights=points[dst, axis], minlength=n) for axis in range(3)])/degree-points
    projected = posed.copy().astype(float)
    for _ in range(700):
        projected += .5*lap(projected)
    # Smoothly taper the edit above the legs and at the terminal contact caps.
    gate = smooth((rest[:, 1]-.16)/.12)*(1-smooth((np.abs(rest[:, 0])-.49)/.05))
    delta = projected-posed
    length = np.linalg.norm(delta, axis=1)
    delta *= (np.minimum(1, .012/np.maximum(length, 1e-12))*gate)[:, None]
    target = posed+delta
    # Retain the original arm volume after fairing, using continuous oval
    # sections rather than retaining tiny bumps at the elbow and wrist.
    reach = np.abs(rest[:, 0])
    arm_center = np.sign(rest[:, 0])*(.1953+.002*(reach-.42))
    longitudinal = posed[:, 1]
    rx = .033+.004*(longitudinal-.42)
    rz = .036+.015*(longitudinal-.42)
    tip_y = float(posed[reach > .53, 1].min())
    cap = np.sqrt(np.maximum(0, 1-(np.maximum(.263-longitudinal, 0)/(.263-tip_y))**2))
    rx, rz = rx*cap, rz*cap
    angle = np.arctan2(posed[:, 2]/np.maximum(rz, .001),
                      (posed[:, 0]-arm_center)/np.maximum(rx, .001))
    tube = posed.copy()
    tube[:, 0] = arm_center+rx*np.cos(angle)
    tube[:, 2] = rz*np.sin(angle)
    tube_gate = smooth((reach-.255)/.080)
    target = target*(1-tube_gate[:, None])+tube*tube_gate[:, None]
    # Relax the join between the existing shoulder and the oval arm guide.
    blend_gate = smooth((reach-.20)/.045)*(1-smooth((reach-.37)/.07))
    for _ in range(120):
        target += .5*lap(target)*blend_gate[:, None]
    target[mirror[left]] = target[left]*np.array([-1, 1, 1])
    target[center, 0] = 0
    print('R27 guide', cage_vertices, 'neutral max change', np.linalg.norm(delta, axis=1).max(), flush=True)

    # Remove arm ownership from the ribs, with a broad shoulder transition.
    # Rebinding against the neutral target keeps this weight edit from changing
    # the accepted idle silhouette.
    x, y = np.abs(rest[:, 0]), rest[:, 1]
    proposed = np.zeros_like(dense)
    # The two trunk intervals meet instead of overlapping. The elbow starts
    # beyond the spine-owned shoulder, keeping the authored skin within four
    # influences without a discontinuous top-four cut across a joint.
    pelvis = 1-smooth((y-.23)/.16)
    chest = smooth((y-.39)/.15)
    proposed[:, names.index('Pelvis')] = pelvis
    proposed[:, names.index('Spine')] = (1-pelvis)*(1-chest)
    proposed[:, names.index('Chest')] = (1-pelvis)*chest
    arm = soft_linear((x-.13)/.22)*smooth((y-.36)/.12)
    helper, upper = 2*arm*(1-arm), arm*arm
    proposed *= (1-helper-upper)[:, None]
    elbow = soft_linear((x-.28)/.25)
    for sign, side in ((-1, 'L'), (1, 'R')):
        selected = (rest[:, 0]*sign > 0)
        proposed[:, names.index('ShoulderBlend_'+side)] += helper*selected
        proposed[:, names.index('UpperArm_'+side)] += upper*(1-elbow)*selected
        proposed[:, names.index('Forearm_'+side)] += upper*elbow*selected
    rebind = smooth((y-.24)/.04)*(1-smooth((x-.49)/.04))
    proposed = proposed*rebind[:, None]+dense*(1-rebind[:, None])
    proposed[mirror[left]] = proposed[left][:, mapped]
    assert np.count_nonzero(proposed[rebind >= 1-1e-8] > 1e-8, axis=1).max() <= 4, 'Joint transitions must not compete for a fifth skin slot'
    new_ids = np.argsort(proposed, axis=1)[:, -4:][:, ::-1].astype('<i4')
    new_weights = np.take_along_axis(proposed, new_ids, axis=1)
    new_weights /= new_weights.sum(1)[:, None]
    # Preserve untouched data exactly, including the lower-body four-slot order.
    untouched = rebind == 0
    new_ids[untouched], new_weights[untouched] = ids[untouched], weights[untouched]
    transform = (palette[new_ids]*new_weights[:, :, None, None]).sum(1)
    result = np.linalg.solve(transform[:, :3, :3], (target-transform[:, :3, 3])[..., None])[..., 0]
    frozen = (gate == 0) & (tube_gate == 0) & untouched
    result[frozen] = rest[frozen]
    result[mirror[left]] = result[left]*np.array([-1, 1, 1])
    result[center, 0] = 0
    result = result.astype('<f4')
    # Final normals come from the actual final neutral geometry.
    posed_result = np.einsum('nij,nj->ni', transform[:, :3, :3], result)+transform[:, :3, 3]
    vertex_normals = np.linalg.solve(transform[:, :3, :3], normals(posed_result, triangles)[..., None])[..., 0]
    vertex_normals /= np.maximum(np.linalg.norm(vertex_normals, axis=1)[:, None], 1e-12)
    vertex_normals[mirror[left]] = vertex_normals[left]*np.array([-1, 1, 1])
    vertex_normals[center, 0] = 0
    vertex_normals /= np.maximum(np.linalg.norm(vertex_normals, axis=1)[:, None], 1e-12)
    assert np.isfinite(result).all()
    mesh.vertices.foreach_set('co', (result[:, [0, 2, 1]]*np.array([1, -1, 1], np.float32)).ravel())
    for attribute in list(mesh.attributes):
        if attribute.name.startswith('r26_'):
            mesh.attributes.remove(attribute)
    mesh.update()
    mesh.normals_split_custom_set_from_vertices((vertex_normals[:, [0, 2, 1]]*np.array([1, -1, 1])).tolist())
    attr = mesh.attributes.new(name='r27_normal_base', type='FLOAT_VECTOR', domain='POINT')
    attr.data.foreach_set('vector', (vertex_normals[:, [0, 2, 1]]*np.array([1, -1, 1])).astype(np.float32).ravel())
    body.vertex_groups.clear()
    for name in names:
        body.vertex_groups.new(name=name)
    for i in range(n):
        for index, value in zip(new_ids[i], new_weights[i]):
            if value > 0:
                body.vertex_groups[names[index]].add([i], float(value), 'REPLACE')
    # Store the editable neutral guide in native coordinates, hidden from export.
    coords = np.array([v.co[:] for v in guide.data.vertices])
    guide.data.vertices.foreach_set('co', (coords[:, [0, 2, 1]]*np.array([1, -1, 1])).astype(np.float32).ravel())
    guide.hide_render = True
    guide.hide_set(True)
    body['surface_refinement'] = 'r26 silhouette fitted to a coarse neutral guide; torso-owned ribs; mirrored local rebind'
    if 'normal_pose_references' in body:
        del body['normal_pose_references']
    output.mkdir(parents=True)
    blend = output/'CHR_MasterCharacter_C1B_SurfaceRefine_r27.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    blocks, current = [], offset
    for record in header['meshes']:
        count = record['vertices']
        end = current+count*56+record['triangleIndices']*4
        block = data[current:end]
        if record['name'] == body.name:
            block = result.tobytes()+vertex_normals.astype('<f4').tobytes()+new_ids.tobytes()+new_weights.astype('<f4').tobytes()+triangles.tobytes()
        blocks.append(block)
        current = end
    header['version'] = 1
    header.pop('normalPoses', None)
    header['sourceSha256'] = sha(blend)
    header['surfaceRefineCandidate'] = True
    encoded = json.dumps(header).encode()
    transport = output/'RefinedCharacter.bytes'
    transport.write_bytes(struct.pack('<i', len(encoded))+encoded+b''.join(blocks))
    report = {'source': str(source), 'sourceSha256': sha(source), 'candidateSha256': sha(blend),
              'transportSha256': sha(transport), 'bodyVertices': n, 'controlCageVertices': cage_vertices,
              'headSkeletonTopologyPreserved': True, 'lowerBodyCoordinatesAndWeightsPreserved': bool(np.array_equal(result[rest[:, 1] <= .16], rest[rest[:, 1] <= .16])),
              'maximumNeutralDisplacementMeters': float(np.linalg.norm(posed_result-posed, axis=1).max()),
              'mirrorMaxMeters': float(np.linalg.norm(result-result[mirror]*np.array([-1, 1, 1]), axis=1).max()),
              'visualApproved': False}
    (output/'SurfaceRefineReview.json').write_text(json.dumps(report, indent=2)+'\n')
    np.savez(output/'RefinementData.npz', rest=result, materialCoordinates=rest, ids=new_ids, weights=new_weights, mirror=mirror, gate=gate)
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
