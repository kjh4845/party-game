"""Verify the saved Blender asset, its transport and the protected r26 source."""
from pathlib import Path
import json
import struct
import sys

import bpy
import numpy as np
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_c1b_rw020_lower_weight_candidate import geometry_identity, rig_identity, read_weights, action_identity, sha

root = Path(__file__).resolve().parents[2]
candidate = Path(sys.argv[sys.argv.index('--')+1]).resolve()
source = root/'BlenderSource/Characters/C1B-RW-026-smooth-body/CHR_MasterCharacter_C1B_SmoothBody_r26.blend'
bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
head = bpy.data.objects['CHR_C1B_R20_SkinnedHead']
rig = bpy.data.objects['RIG_C1B_R20_Armature']
source_rig, source_body, source_head = rig_identity(rig), geometry_identity(body), geometry_identity(head)
source_names, source_weights = read_weights(body)
source_points = np.empty((len(body.data.vertices), 3), np.float32)
body.data.vertices.foreach_get('co', source_points.ravel())
source_head_names, source_head_weights = read_weights(head)
source_actions = {a.name: action_identity(a) for a in bpy.data.actions}
source_sha = sha(source)

blend = candidate/'CHR_MasterCharacter_C1B_SurfaceRefine_r27.blend'
bpy.ops.wm.open_mainfile(filepath=str(blend), load_ui=False)
body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
head = bpy.data.objects['CHR_C1B_R20_SkinnedHead']
rig = bpy.data.objects['RIG_C1B_R20_Armature']
mesh = body.data
assert rig_identity(rig) == source_rig
assert geometry_identity(head) == source_head
assert read_weights(head)[0] == source_head_names and np.array_equal(read_weights(head)[1], source_head_weights)
assert {a.name: action_identity(a) for a in bpy.data.actions} == source_actions
current_body = geometry_identity(body)
for key in ('vertices', 'edges', 'faces', 'edgeHash', 'loopHash'):
    assert current_body[key] == source_body[key]
points = np.empty_like(source_points)
mesh.vertices.foreach_get('co', points.ravel())
lower = source_points[:, 2] <= .16
assert np.array_equal(points[lower], source_points[lower])
native_names, native_weights = read_weights(body)
source_by_name = source_weights[:, [source_names.index(name) for name in native_names]]
assert np.array_equal(native_weights[lower], source_by_name[lower])
assert not any(m.use_deform_preserve_volume for m in body.modifiers if m.type == 'ARMATURE'), 'Unity transport uses linear skinning'
data = (candidate/'RefinedCharacter.bytes').read_bytes()
size = struct.unpack_from('<i', data)[0]
header = json.loads(data[4:4+size])
assert header['version'] == 3 and header['sourceSha256'] == sha(blend)
n, offset = len(points), 4+size
transport_points = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
converted = points[:, [0, 2, 1]]*np.array([1, 1, -1], np.float32)
assert np.array_equal(converted, transport_points)
ids = np.frombuffer(data, '<i4', n*4, offset+n*24).reshape(n, 4)
weights = np.frombuffer(data, '<f4', n*4, offset+n*40).reshape(n, 4)
dense = np.zeros_like(native_weights)
np.add.at(dense, (np.arange(n)[:, None], ids), weights)
assert native_names == [b['name'] for b in header['bones']]
assert np.array_equal(dense, native_weights)
assert np.abs(weights.sum(1)-1).max() < 1e-6
tree = KDTree(n)
for i, point in enumerate(transport_points):
    tree.insert(point, i)
tree.balance()
mirror = np.array([tree.find((-float(p[0]), float(p[1]), float(p[2])))[1] for p in transport_points])
mapped = [native_names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in native_names]
mirror_error = float(np.linalg.norm(transport_points-transport_points[mirror]*np.array([-1, 1, 1]), axis=1).max())
weight_mirror_error = float(np.abs(dense-dense[mirror][:, mapped]).max())
assert mirror_error < 1e-7 and weight_mirror_error < 1e-6
assert len(mesh.shape_keys.key_blocks) == 1+sum(r['hasDelta'] for r in header['surfacePoses'])
normal = np.empty_like(points)
mesh.attributes['r27_normal_base'].data.foreach_get('vector', normal.ravel())
base_normals = np.frombuffer(data, '<f4', n*3, offset+n*12).reshape(n, 3)
assert np.array_equal(normal[:, [0, 2, 1]]*np.array([1, 1, -1], np.float32), base_normals)
offset += sum(r['vertices']*56+r['triangleIndices']*4 for r in header['meshes'])
maximum_key_error = 0.
for ref in header['surfacePoses']:
    if not ref['hasDelta']:
        continue
    delta = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
    normal_delta = np.frombuffer(data, '<f4', n*3, offset+n*12).reshape(n, 3)
    offset += n*24
    shape = np.empty_like(points)
    mesh.shape_keys.key_blocks[ref['name']].data.foreach_get('co', shape.ravel())
    error = float(np.abs(shape[:, [0, 2, 1]]*np.array([1, 1, -1])-transport_points-delta).max())
    maximum_key_error = max(maximum_key_error, error)
    assert error < 1e-7
    mesh.attributes['r27_normal_'+ref['name']].data.foreach_get('vector', normal.ravel())
    assert np.array_equal(normal[:, [0, 2, 1]]*np.array([1, 1, -1], np.float32), normal_delta)
assert offset == len(data)
assert sha(source) == source_sha
report = {'passed': True, 'sourceUnchanged': True, 'headSkeletonActionsTopologyPreserved': True,
          'lowerBodyCoordinatesAndWeightsPreserved': True, 'nativeTransportGeometryAndWeightsMatch': True,
          'nativeTransportBaseAndPoseNormalsMatch': True, 'positionalCorrectiveCount': len(mesh.shape_keys.key_blocks)-1,
          'maximumNativeShapeKeyRoundingErrorMeters': maximum_key_error,
          'bodyMirrorErrorMeters': mirror_error, 'weightMirrorError': weight_mirror_error,
          'candidateSha256': sha(blend), 'transportSha256': sha(candidate/'RefinedCharacter.bytes')}
(candidate/'NativeValidation.json').write_text(json.dumps(report, indent=2)+'\n')
print(json.dumps(report), flush=True)
