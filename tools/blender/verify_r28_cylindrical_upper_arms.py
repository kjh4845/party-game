"""Verify r28 source/transport parity and the protected parts of r27."""
from pathlib import Path
import json
import struct
import sys

import bpy
import numpy as np
from mathutils.kdtree import KDTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_r27_surface_refine import sha, smooth
from create_c1b_rw020_lower_weight_candidate import rig_identity, geometry_identity, action_identity, read_weights

ROOT = Path(__file__).resolve().parents[2]
source = ROOT/'BlenderSource/Characters/C1B-RW-027-surface-refine'
candidate = Path(sys.argv[sys.argv.index('--')+1]).resolve()


def load_transport(path):
    data = path.read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4+size])
    offset = 4+size
    blocks = []
    for r in header['meshes']:
        n = r['vertices']
        end = offset+n*56+r['triangleIndices']*4
        blocks.append(data[offset:end])
        offset = end
    return data, header, blocks, offset


old_data, old_header, old_blocks, old_offset = load_transport(source/'RefinedCharacter.bytes')
data, header, blocks, offset = load_transport(candidate/'CylindricalCharacter.bytes')
assert header['bones'] == old_header['bones'] and header['meshes'] == old_header['meshes']
assert header['surfacePoses'] == old_header['surfacePoses']
assert blocks[1:] == old_blocks[1:]
n = header['meshes'][0]['vertices']
v = np.frombuffer(blocks[0], '<f4', n*3).reshape(n, 3)
original = np.frombuffer(old_blocks[0], '<f4', n*3).reshape(n, 3)
assert blocks[0][n*24:] == old_blocks[0][n*24:]
mat_data = (ROOT/'BlenderSource/Characters/C1B-RW-026-smooth-body/SmoothCharacter.bytes').read_bytes()
material = np.frombuffer(mat_data, '<f4', n*3, 4+struct.unpack_from('<i', mat_data)[0]).reshape(n, 3)
reach = np.abs(material[:, 0])
mask = smooth((reach-.19)/.075)*(1-smooth((reach-.34)/.075))*smooth((material[:, 1]-.52)/.045)
assert np.array_equal(v[mask == 0], original[mask == 0])
tree = KDTree(n)
for i, p in enumerate(original):
    tree.insert(p, i)
tree.balance()
mirror = np.array([tree.find((-float(p[0]), float(p[1]), float(p[2])))[1] for p in original])
mirror_error = float(np.linalg.norm(v-v[mirror]*np.array([-1, 1, 1]), axis=1).max())
assert mirror_error < 1e-7
bpy.ops.wm.open_mainfile(filepath=str(source/'CHR_MasterCharacter_C1B_SurfaceRefine_r27.blend'), load_ui=False)
source_rig = rig_identity(bpy.data.objects['RIG_C1B_R20_Armature'])
source_head = geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedHead'])
source_body = geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedBody'])
source_weights = read_weights(bpy.data.objects['CHR_C1B_R20_SkinnedBody'])
source_actions = {a.name: action_identity(a) for a in bpy.data.actions}
blend = candidate/'CHR_MasterCharacter_C1B_CylindricalArms_r28.blend'
assert sha(blend) == header['sourceSha256']
bpy.ops.wm.open_mainfile(filepath=str(blend), load_ui=False)
body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
mesh = body.data
assert rig_identity(bpy.data.objects['RIG_C1B_R20_Armature']) == source_rig
assert geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedHead']) == source_head
assert {a.name: action_identity(a) for a in bpy.data.actions} == source_actions
now = geometry_identity(body)
for key in ('vertices', 'edges', 'faces', 'edgeHash', 'loopHash'):
    assert now[key] == source_body[key]
weights = read_weights(body)
assert weights[0] == source_weights[0] and np.array_equal(weights[1], source_weights[1])
points = np.empty((n, 3), np.float32)
mesh.vertices.foreach_get('co', points.ravel())
convert = lambda p: p[:, [0, 2, 1]]*np.array([1, 1, -1], np.float32)
assert np.array_equal(convert(points), v)
mesh.attributes['r27_normal_base'].data.foreach_get('vector', points.ravel())
assert np.array_equal(convert(points), np.frombuffer(blocks[0], '<f4', n*3, n*12).reshape(n, 3))
maximum_key_error = 0.
for ref in header['surfacePoses']:
    if not ref['hasDelta']:
        continue
    delta = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
    normal = np.frombuffer(data, '<f4', n*3, offset+n*12).reshape(n, 3)
    old_delta = np.frombuffer(old_data, '<f4', n*3, old_offset).reshape(n, 3)
    assert np.array_equal(delta[mask == 0], old_delta[mask == 0])
    mesh.shape_keys.key_blocks[ref['name']].data.foreach_get('co', points.ravel())
    error = float(np.abs(convert(points)-v-delta).max())
    maximum_key_error = max(maximum_key_error, error)
    assert error < 1e-7
    mesh.attributes['r27_normal_'+ref['name']].data.foreach_get('vector', points.ravel())
    assert np.array_equal(convert(points), normal)
    offset += n*24
    old_offset += n*24
assert offset == len(data) and old_offset == len(old_data)
report = {'passed': True, 'outsideUpperArmAndAttachmentCoordinatesPreserved': True,
          'headSkeletonTopologyWeightsActionsPreserved': True, 'poseReferencesPreserved': True,
          'nativeTransportBaseAndCorrectivesMatch': True, 'mirrorMaxMeters': mirror_error,
          'maximumNativeKeyRoundingErrorMeters': maximum_key_error,
          'candidateSha256': sha(blend), 'transportSha256': sha(candidate/'CylindricalCharacter.bytes')}
(candidate/'NativeValidation.json').write_text(json.dumps(report, indent=2)+'\n')
print(json.dumps(report), flush=True)
