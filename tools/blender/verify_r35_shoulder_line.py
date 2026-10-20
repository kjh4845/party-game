"""Independently check a local r35 shoulder edit against the applied r34.

Run inside Blender, for example:
  blender --background --python-exit-code 1 --python this.py -- CANDIDATE_DIR
  ... -- CANDIDATE_DIR --captures UNITY_CAPTURE_DIR --crossings

Writes only under .codex_tmp/shoulder-line-20261013/Validation. Passing these
checks proves preservation/parity and the stated sampled geometry checks; it
does not prove visually smooth shoulders or continuous-pose correctness.
"""
from pathlib import Path
import argparse
import json
import struct
import sys

import bpy
import numpy as np
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
from create_r27_surface_refine import sha, normals
from create_c1b_rw020_lower_weight_candidate import (
    action_identity, geometry_identity, read_weights, rig_identity,
)

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT/'BlenderSource/Characters/C1B-RW-034-fair-body'
BASELINE_CAPTURES = ROOT/'artifacts/evidence/r34-fair-body-20261010'
OUTPUT = ROOT/'.codex_tmp/shoulder-line-20261013/Validation'
BODY = 'CHR_C1B_R20_SkinnedBody'
HEAD = 'CHR_C1B_R20_SkinnedHead'
RIG = 'RIG_C1B_R20_Armature'


def transport(path):
    data = path.read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4+size])
    offset = 4+size
    blocks = []
    for record in header['meshes']:
        assert record.get('influences', 4) == 4
        end = offset+record['vertices']*56+record['triangleIndices']*4
        blocks.append(data[offset:end])
        offset = end
    n = header['meshes'][0]['vertices']
    block = blocks[0]
    result = {
        'header': header, 'blocks': blocks,
        'positions': np.frombuffer(block, '<f4', n*3).reshape(n, 3),
        'normals': np.frombuffer(block, '<f4', n*3, n*12).reshape(n, 3),
        'ids': np.frombuffer(block, '<i4', n*4, n*24).reshape(n, 4),
        'weights': np.frombuffer(block, '<f4', n*4, n*40).reshape(n, 4),
        'triangles': np.frombuffer(block, '<i4', header['meshes'][0]['triangleIndices'], n*56).reshape(-1, 3),
        'poses': {},
    }
    for pose in header['surfacePoses']:
        if pose['hasDelta']:
            delta = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
            normal = np.frombuffer(data, '<f4', n*3, offset+n*12).reshape(n, 3)
            result['poses'][pose['name']] = (delta, normal)
            offset += n*24
    assert offset == len(data), 'Unparsed transport data'
    return result


def palette(path):
    raw = np.frombuffer(path.read_bytes(), '<f4', offset=4).reshape(-1, 4, 4).astype(float)
    return np.linalg.inv(raw[0])@raw


def deform(model, matrices):
    transform = (matrices[model['ids']]*model['weights'][:, :, None, None]).sum(1)
    return np.einsum('nij,nj->ni', transform[:, :3, :3], model['positions'])+transform[:, :3, 3]


def dense_weights(model):
    weights = np.zeros((len(model['positions']), len(model['header']['bones'])), np.float32)
    for slot in range(4):
        np.add.at(weights, (np.arange(len(weights)), model['ids'][:, slot]), model['weights'][:, slot])
    return weights


def captured_points(path, count):
    data = path.read_bytes()
    assert struct.unpack_from('<i', data)[0] == count, str(path)
    return np.frombuffer(data, '<f4', count*3, 4).reshape(count, 3).astype(float)


def edge_topology(triangles, count):
    edges = np.sort(np.concatenate((triangles[:, [0, 1]], triangles[:, [1, 2]], triangles[:, [2, 0]])), axis=1)
    keys = edges[:, 0].astype(np.int64)*count+edges[:, 1]
    order = np.argsort(keys)
    _, counts = np.unique(keys, return_counts=True)
    faces = np.tile(np.arange(len(triangles)), 3)
    shared = np.flatnonzero(np.diff(keys[order]) == 0)
    return faces[order[shared]], faces[order[shared+1]], counts


def face_statistics(points, triangles, first, second, region):
    p = points[triangles]
    normals = np.cross(p[:, 1]-p[:, 0], p[:, 2]-p[:, 0])
    area2 = np.linalg.norm(normals, axis=1)
    normals /= np.maximum(area2[:, None], 1e-30)
    angles = np.degrees(np.arccos(np.clip(np.sum(normals[first]*normals[second], axis=1), -1, 1)))
    face_region = np.any(region[triangles], axis=1)
    selected = face_region[first] | face_region[second]
    local = angles[selected]
    worst = int(np.flatnonzero(selected)[np.argmax(local)])
    worst_vertices = triangles[first[worst]]
    return {
        'maximumDegrees': float(local.max()),
        'p95Degrees': float(np.percentile(local, 95)),
        'p99Degrees': float(np.percentile(local, 99)),
        'edgesOver15Degrees': int(np.count_nonzero(local > 15)),
        'edgesOver30Degrees': int(np.count_nonzero(local > 30)),
        'edgesOver60Degrees': int(np.count_nonzero(local > 60)),
        'degenerateTriangles': int(np.count_nonzero(area2 < 1e-12)),
        'worstTriangleVertices': worst_vertices.tolist(),
        'worstPosition': points[worst_vertices].mean(0).tolist(),
    }


def segment_hits(p0, p1, triangles, return_points=False):
    direction = p1-p0
    e1, e2 = triangles[:, 1]-triangles[:, 0], triangles[:, 2]-triangles[:, 0]
    cross = np.cross(direction, e2)
    determinant = np.einsum('ij,ij->i', e1, cross)
    valid = abs(determinant) > 1e-15
    inverse = np.divide(1., determinant, out=np.zeros_like(determinant), where=valid)
    source = p0-triangles[:, 0]
    u = inverse*np.einsum('ij,ij->i', source, cross)
    q = np.cross(source, e1)
    v = inverse*np.einsum('ij,ij->i', direction, q)
    distance = inverse*np.einsum('ij,ij->i', e2, q)
    epsilon = 1e-7
    hits = valid & (u > epsilon) & (v > epsilon) & (u+v < 1-epsilon) & (distance > epsilon) & (distance < 1-epsilon)
    return (hits, p0+direction*distance[:, None]) if return_points else hits


def crossings(points, triangles, region):
    selected = np.flatnonzero(np.any(region[triangles], axis=1))
    if not len(selected):
        return {'trianglesChecked': 0, 'properNonAdjacentCrossingPairs': 0, 'exampleVertexTriples': [], 'trianglePairs': [], 'intersectionBoundsBySide': {}}
    faces = triangles[selected]
    vertices = points.tolist()
    local_tree = BVHTree.FromPolygons(vertices, faces.tolist(), all_triangles=True)
    full_tree = BVHTree.FromPolygons(vertices, triangles.tolist(), all_triangles=True)
    pairs = np.asarray(local_tree.overlap(full_tree), dtype=np.int32).reshape(-1, 2)
    # One triangle must touch the edited shoulder patch. The other may be
    # anywhere on the body; do not misclassify inherited contacts between two
    # protected regions as a shoulder edit regression.
    pairs[:, 0] = selected[pairs[:, 0]]
    pairs = np.unique(np.sort(pairs, axis=1), axis=0)
    pairs = pairs[pairs[:, 0] != pairs[:, 1]]
    a, b = triangles[pairs[:, 0]], triangles[pairs[:, 1]]
    separate = ~np.any(a[:, :, None] == b[:, None, :], axis=(1, 2))
    a, b, pairs = a[separate], b[separate], pairs[separate]
    hits = np.zeros(len(a), bool)
    intersection_points = []
    for i, j in ((0, 1), (1, 2), (2, 0)):
        forward, forward_points = segment_hits(points[a[:, i]], points[a[:, j]], points[b], True)
        reverse, reverse_points = segment_hits(points[b[:, i]], points[b[:, j]], points[a], True)
        hits |= forward | reverse
        intersection_points.extend((forward_points[forward], reverse_points[reverse]))
    intersection_points = np.concatenate(intersection_points)
    bounds = {}
    for side, sign in (('L', -1), ('R', 1)):
        selected_points = intersection_points[intersection_points[:, 0]*sign >= 0]
        if len(selected_points):
            bounds[side] = {'minimum': selected_points.min(0).tolist(), 'maximum': selected_points.max(0).tolist()}
    indices = np.flatnonzero(hits)[:6]
    return {'trianglesChecked': len(faces), 'properNonAdjacentCrossingPairs': int(hits.sum()),
            'exampleVertexTriples': [[a[i].tolist(), b[i].tolist()] for i in indices],
            'trianglePairs': pairs[hits].tolist(), 'intersectionBoundsBySide': bounds}


def compare_contact_footprints(before, after, tolerance=.0001):
    old_pairs = {tuple(pair) for pair in before['trianglePairs']}
    new_pairs = {tuple(pair) for pair in after['trianglePairs']}
    expanded = {}
    for side, bounds in after['intersectionBoundsBySide'].items():
        previous = before['intersectionBoundsBySide'].get(side)
        if previous is None:
            expanded[side] = {'newContactRegion': True, 'maximumExpansionMeters': None}
        else:
            amount = np.maximum(np.array(previous['minimum'])-bounds['minimum'], np.array(bounds['maximum'])-previous['maximum'])
            expanded[side] = {'newContactRegion': False, 'axisExpansionMeters': np.maximum(amount, 0).tolist(), 'maximumExpansionMeters': float(max(0., amount.max()))}
    acceptable = all(not value['newContactRegion'] and value['maximumExpansionMeters'] <= tolerance for value in expanded.values())
    return {'originalPairs': len(old_pairs), 'candidatePairs': len(new_pairs), 'sameTrianglePairs': len(old_pairs & new_pairs), 'newTrianglePairsInsideOrOutsideBand': len(new_pairs-old_pairs), 'removedTrianglePairs': len(old_pairs-new_pairs), 'footprintExpansionBySide': expanded, 'toleranceMeters': tolerance, 'withinOriginalFootprintAtDeclaredTolerance': acceptable,
            'scope': 'Actual intersection-point bounding boxes per side; triangle identities may shift inside inherited contact. This does not measure full overlap volume or replace visible-contact review.'}


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('candidate', type=Path)
    parser.add_argument('--captures', type=Path)
    parser.add_argument('--crossings', action='store_true')
    parser.add_argument('--crossing-poses', default='Idle,Lift45_Flex0,Lift90_Flex0,Lift120_Flex0,Lift90_Flex120,Punch8,SweepWorst')
    parser.add_argument('--baseline-self-test', action='store_true', help='Exercise preservation/parity checks on the original r34 files')
    parser.add_argument('--max-shoulder-expansion', type=float, default=.002)
    parser.add_argument('--contact-footprint-tolerance', type=float, default=.0001, help='Inherited contact footprint tolerance in meters; default0.1mm is below0.1pixel in current1000px/1.14m clay views')
    args = parser.parse_args(sys.argv[sys.argv.index('--')+1:])
    candidate = args.candidate.resolve()
    output = OUTPUT/(candidate.name+'-Validation.json')
    OUTPUT.mkdir(parents=True, exist_ok=True)
    report = {'passed': False, 'visualApproved': False, 'candidate': str(candidate), 'checks': {}, 'limits': [
        'Exact preservation and sampled geometry checks do not establish visual smoothness.',
        'Neutral shoulder envelope is measured in 3D; same-camera front/side/three-quarter and continuous raises still require visual review.',
        'Crossings, when requested, exclude shared vertices, coplanar contact and edge-only touches; unsampled animation poses are not covered.',
        'Arm/body slit appearance is not inferred from face-angle or crossing checks.',
    ]}
    def check(name, value):
        report['checks'][name] = bool(value)
        assert value, name
    try:
        old = transport(SOURCE/'FairBodyCharacter.bytes')
        model_path = candidate/('FairBodyCharacter.bytes' if args.baseline_self_test else 'ShoulderLineCharacter.bytes')
        model = transport(model_path)
        old_header, header = old['header'], model['header']
        check('same24BoneHierarchyAndPhysicalPositions', len(header['bones']) == 24 and header['bones'] == old_header['bones'])
        check('sameMeshSchema', header['meshes'] == old_header['meshes'])
        old_references = {reference['name']: reference for reference in old_header['surfacePoses']}
        references = {reference['name']: reference for reference in header['surfacePoses']}
        added_references = set(references)-set(old_references)
        check('originalPoseReferencesAndTrajectoryPreserved', all(references.get(name) == reference for name, reference in old_references.items()))
        check('noMoreThanSixPairedAdditionalReferences', len(added_references) <= 6 and len(references) == len(header['surfacePoses']) and all(name[:-2]+('_R' if name.endswith('_L') else '_L') in added_references for name in added_references))
        report['additionalPoseReferences'] = sorted(added_references)
        check('headAndOtherMeshTransportExact', model['blocks'][1:] == old['blocks'][1:])
        check('triangleTopologyExact', np.array_equal(model['triangles'], old['triangles']))
        aux = np.load(SOURCE/'NeutralBindData.npz')
        material, mirror = aux['material'], aux['mirror']
        candidate_aux = np.load(candidate/'NeutralBindData.npz')
        check('referenceMaterialAndMirrorExact', np.array_equal(candidate_aux['material'], material) and np.array_equal(candidate_aux['mirror'], mirror))
        neutral_palette = palette(BASELINE_CAPTURES/'Idle_Matrices.bin')
        before, after = deform(old, neutral_palette), deform(model, neutral_palette)
        # Independent anatomical guard, deliberately not copied from candidate editMask.
        allowed = (abs(material[:, 0]) > .08) & (abs(material[:, 0]) < .31) & (material[:, 1] > .445) & (before[:, 1] > .45)
        # The reconstructed root can extend a short way into the proximal arm.
        # Keep this extension above the ribs and inside the existing outer arm
        # silhouette; the distal shaft and all lower geometry remain protected.
        root_extension = (abs(material[:, 0]) >= .31) & (abs(material[:, 0]) < .33) & (material[:, 1] > .445) & (before[:, 1] > .45) & (before[:, 1] < .650) & (abs(before[:, 0]) < .2151)
        allowed |= root_extension
        geometry_protected = ~allowed
        # A local rebind may alter bind-space coordinates to retain the same
        # visible neutral shape. Keep this distinct from permission to reshape
        # the arm shaft; neutral and reference-pose shaft geometry are checked.
        rebind_allowed = (abs(material[:, 0]) > .12) & (abs(material[:, 0]) < .375) & (material[:, 1] > .45)
        bind_allowed = allowed | rebind_allowed
        protected = ~bind_allowed
        check('bindPositionsOnlyWithinShoulderAndDeclaredRebindPatch', np.array_equal(model['positions'][protected], old['positions'][protected]))
        old_weights, weights = dense_weights(old), dense_weights(model)
        edited_vertices = np.any(model['positions'] != old['positions'], axis=1) | np.any(weights != old_weights, axis=1)
        check('weightsOnlyWithinLocalShoulderRebindPatch', np.array_equal(weights[~rebind_allowed], old_weights[~rebind_allowed]))
        check('weightsFiniteNonnegativeNormalized', np.isfinite(weights).all() and weights.min() >= 0 and np.max(abs(weights.sum(1)-1)) < 1e-5)
        names = [bone['name'] for bone in header['bones']]
        forearm_columns = [names.index(name) for name in ('Forearm_L', 'Forearm_R')]
        check('existingForearmWeightFractionsPreserved', np.max(abs(weights[:, forearm_columns]-old_weights[:, forearm_columns])) < 1e-6)
        mapped = np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names])
        check('mirroredWeights', np.max(abs(weights-weights[mirror][:, mapped])) < 1e-6)
        mirror_error = float(np.linalg.norm(model['positions']-model['positions'][mirror]*[-1, 1, 1], axis=1).max())
        check('mirroredGeometry', mirror_error < 1e-7)
        check('lowerBodyCoreAndArmShaftNeutralShapePreserved', np.max(np.linalg.norm(after[geometry_protected]-before[geometry_protected], axis=1)) < 2e-6)
        triangles = model['triangles']
        normal_allowed = bind_allowed.copy()
        normal_allowed[np.unique(triangles[np.any(bind_allowed[triangles], axis=1)])] = True
        check('baseNormalsChangedOnlyLocally', np.array_equal(model['normals'][~normal_allowed], old['normals'][~normal_allowed]))
        shoulder_region = (before[:, 1] > .55) & (abs(material[:, 0]) < .32)
        expansion = float(np.max(abs(after[shoulder_region, 0]))-np.max(abs(before[shoulder_region, 0])))
        check('shoulderEnvelopeNotBroadened', expansion <= args.max_shoulder_expansion)
        original_faces = np.cross(before[triangles[:, 1]]-before[triangles[:, 0]], before[triangles[:, 2]]-before[triangles[:, 0]])
        new_faces = np.cross(after[triangles[:, 1]]-after[triangles[:, 0]], after[triangles[:, 2]]-after[triangles[:, 0]])
        rotated_faces = int(np.count_nonzero(np.sum(original_faces*new_faces, axis=1) <= 0))
        report['geometry'] = {'mirrorMaxMeters': mirror_error, 'changedVertices': int(np.any(model['positions'] != old['positions'], axis=1).sum()), 'changedWeightsVertices': int(np.any(weights != old_weights, axis=1).sum()), 'maximumNeutralDisplacementMeters': float(np.linalg.norm(after-before, axis=1).max()), 'shoulderHalfWidthExpansionMeters': expansion, 'allowedShoulderHalfWidthExpansionMeters': args.max_shoulder_expansion}
        report['geometry']['facesRotatedBeyond90DegreesFromSource'] = rotated_faces
        report['geometry']['rotationMeaning'] = 'Diagnostic only: a changed surface can legitimately rotate relative to its former normal; actual area, dihedral and crossing checks determine failure.'
        bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'CHR_MasterCharacter_C1B_FairBody_r34.blend'), load_ui=False)
        original_rig = rig_identity(bpy.data.objects[RIG])
        original_head = geometry_identity(bpy.data.objects[HEAD])
        original_body = geometry_identity(bpy.data.objects[BODY])
        original_actions = {a.name: action_identity(a) for a in bpy.data.actions}
        blend = candidate/('CHR_MasterCharacter_C1B_FairBody_r34.blend' if args.baseline_self_test else 'CHR_MasterCharacter_C1B_ShoulderLine_r35.blend')
        normal_prefix = 'r34_normal_' if args.baseline_self_test else 'r35_normal_'
        check('transportSourceShaMatchesNative', sha(blend) == header['sourceSha256'])
        bpy.ops.wm.open_mainfile(filepath=str(blend), load_ui=False)
        check('nativeRigExact', rig_identity(bpy.data.objects[RIG]) == original_rig)
        check('nativeHeadExact', geometry_identity(bpy.data.objects[HEAD]) == original_head)
        check('nativeActionsExact', {a.name: action_identity(a) for a in bpy.data.actions} == original_actions)
        body = bpy.data.objects[BODY]
        mesh = body.data
        now = geometry_identity(body)
        check('nativeBodyTopologyExact', all(now[key] == original_body[key] for key in ('vertices', 'edges', 'faces', 'edgeHash', 'loopHash')))
        group_names, native_weights = read_weights(body)
        native_weights = native_weights[:, [group_names.index(name) for name in names]]
        check('nativeTransportWeightsMatch', np.max(abs(native_weights-weights)) < 1e-7)
        points = np.empty_like(model['positions'])
        def read_native(collection, field):
            collection.foreach_get(field, points.ravel())
            return points[:, [0, 2, 1]]*np.array([1, 1, -1], np.float32)
        check('nativeTransportBasePositionsMatch', np.array_equal(read_native(mesh.vertices, 'co'), model['positions']))
        check('nativeTransportBasisMatches', np.array_equal(read_native(mesh.shape_keys.key_blocks['Basis'].data, 'co'), model['positions']))
        check('nativeTransportBaseNormalsMatch', np.array_equal(read_native(mesh.attributes[normal_prefix+'base'].data, 'vector'), model['normals']))
        key_error = 0.
        pose_changes = []
        reference_files = {'Raise45': 'Raised_45_Matrices.bin', 'Raise90': 'Raised_90_Matrices.bin', 'Prepare': 'Punch_02_Matrices.bin', 'Accelerate': 'Punch_05_Matrices.bin', 'Strike': 'Punch_08_Matrices.bin', 'FollowThrough': 'Punch_12_Matrices.bin', 'Recover': 'Punch_18_Matrices.bin'}
        reference_directory = ROOT/'BlenderSource/Characters/C1B-RW-028-cylindrical-upper-arms/ReferencePoses'
        reflect = np.diag([-1., 1, 1, 1])
        extra_deltas, extra_palettes, extra_anchor_baselines = {}, {}, {}
        recorded_extra_poses = {'BentLow': 'Sweep_0210', 'BentHigh': 'SweepWorst', 'StraightHigh': 'Sweep_0090', 'HighStraight': 'Sweep_0090'}
        old_pose_stack = np.array([value[0] for value in old['poses'].values()])
        current_original_pose_stack = np.array([model['poses'][name][0] for name in old['poses']])
        old_pose_sides = np.array([reference['side'] for reference in old_header['surfacePoses'] if reference['hasDelta']])
        declared_protected = candidate_aux['editMask'] == 0 if 'editMask' in candidate_aux else protected
        for extra_name in added_references:
            label, side = extra_name.rsplit('_', 1)
            recording = recorded_extra_poses[label]
            directory = ROOT/'.codex_tmp/shoulder-line-20261013/BaselineSweep'
            raw_weights = (directory/(recording+'_BlendWeights.bin')).read_bytes()
            assert struct.unpack_from('<i', raw_weights)[0] == len(old_pose_stack)
            recorded_weights = np.frombuffer(raw_weights, '<f4', offset=4)
            expected_side = 0 if side == 'L' else 1
            carried = np.einsum('knc,k->nc', old_pose_stack, recorded_weights*(old_pose_sides == expected_side))
            authored_baseline = np.einsum('knc,k->nc', current_original_pose_stack, recorded_weights*(old_pose_sides == expected_side))
            carried[(material[:, 0] >= 0) if side == 'L' else (material[:, 0] <= 0)] = 0
            authored_baseline[(material[:, 0] >= 0) if side == 'L' else (material[:, 0] <= 0)] = 0
            ref_palette = palette(directory/(recording+'_Matrices.bin'))
            if side == 'R':
                ref_palette = reflect@ref_palette[mapped]@reflect
            extra_deltas[extra_name] = carried
            extra_anchor_baselines[extra_name] = authored_baseline
            extra_palettes[extra_name] = ref_palette
        for name, (delta, normal) in model['poses'].items():
            old_delta, old_normal = old['poses'].get(name, (extra_deltas.get(name, np.zeros_like(delta)), np.zeros_like(normal)))
            edited_vertices |= np.any(abs(delta-old_delta) > 1e-7, axis=1)
            if name in old['poses']:
                check(name+'PositionDeltaOnlyLocal', np.array_equal(delta[protected], old_delta[protected]))
                check(name+'NormalDeltaOnlyLocal', np.array_equal(normal[~normal_allowed], old_normal[~normal_allowed]))
            else:
                # A new anchor must carry the original composite deformation
                # outside the edit; zeroing it would erase unrelated original
                # shape corrections when this anchor receives all the weight.
                extra_protected = protected if np.any(weights != old_weights) else (declared_protected | geometry_protected)
                check(name+'OutsideDeclaredPatchCarriesOriginalSideRecordedDeformation', np.max(abs(delta[extra_protected]-old_delta[extra_protected])) < 1e-7)
                if name.endswith('_L'):
                    transform = (extra_palettes[name][model['ids']]*model['weights'][:, :, None, None]).sum(1)
                    p = np.einsum('nij,nj->ni', transform[:, :3, :3], model['positions']+delta)+transform[:, :3, 3]
                    expected = np.linalg.solve(transform[:, :3, :3], normals(p, triangles)[..., None])[..., 0]
                    expected /= np.maximum(np.linalg.norm(expected, axis=1)[:, None], 1e-12)
                    expected -= model['normals']
                    expected[material[:, 0] >= 0] = 0
                    check(name+'NormalsMatchActualReferenceGeometry', np.max(abs(normal-expected)) < 1e-5)
            native_delta = read_native(mesh.shape_keys.key_blocks[name].data, 'co')-model['positions']
            error = float(np.max(abs(native_delta-delta)))
            key_error = max(key_error, error)
            check(name+'NativePositionDeltaMatch', error < 1e-7)
            check(name+'NativeNormalDeltaMatch', np.array_equal(read_native(mesh.attributes[normal_prefix+name].data, 'vector'), normal))
            if name in old['poses']:
                label, side = name.rsplit('_', 1)
                ref_palette = palette(reference_directory/reference_files.get(label, label+'_Matrices.bin'))
                if side == 'R':
                    ref_palette = reflect@ref_palette[mapped]@reflect
                old_pose_model = {**old, 'positions': old['positions']+old_delta}
                pose_model = {**model, 'positions': model['positions']+delta}
                fixed_region = geometry_protected & ((material[:, 0] < 0) if side == 'L' else (material[:, 0] > 0))
                fixed_error = float(np.linalg.norm(deform(pose_model, ref_palette)[fixed_region]-deform(old_pose_model, ref_palette)[fixed_region], axis=1).max())
                report.setdefault('protectedReferencePoseGeometryErrorMeters', {})[name] = fixed_error
                check(name+'ProtectedReferenceArmGeometryPreserved', fixed_error < 2e-6)
            if name.endswith('_L'):
                other_delta, other_normal = model['poses'][name[:-2]+'_R']
                old_other_delta = old['poses'].get(name[:-2]+'_R', (extra_deltas.get(name[:-2]+'_R', np.zeros_like(other_delta)), None))[0]
                # Pre-existing r28/r34 corrective deltas are not perfectly
                # bilateral. Permit local symmetry repairs, while preventing
                # increases at any vertex and preserving protected deltas.
                old_error = abs(old_other_delta-old_delta[mirror]*[-1, 1, 1])
                new_error = abs(other_delta-delta[mirror]*[-1, 1, 1])
                if name in old['poses']:
                    check(name+'PoseBilateralErrorNotIncreased', np.max(new_error-old_error) < 1e-7)
                else:
                    added = delta-extra_anchor_baselines[name]
                    added_other = other_delta-extra_anchor_baselines[name[:-2]+'_R']
                    check(name+'NewLocalCorrectionMirroredBeyondSideSpecificBaseline', np.max(abs(added_other-added[mirror]*[-1, 1, 1])) < 1e-7)
                report.setdefault('posePositionMirrorErrors', {})[name] = {
                    'r34': float(np.max(abs(old_other_delta-old_delta[mirror]*[-1, 1, 1]))),
                    'candidate': float(np.max(abs(other_delta-delta[mirror]*[-1, 1, 1]))),
                }
                # Historical normal deltas contain small asymmetric export roundoff.
                report.setdefault('poseNormalMirrorErrors', {})[name] = float(np.max(abs(other_normal-normal[mirror]*[-1, 1, 1])))
            pose_changes.append({'pose': name, 'changedVertices': int(np.any(delta != old_delta, axis=1).sum())})
        report['maximumNativeKeyRoundingErrorMeters'] = key_error
        report['poseChanges'] = pose_changes
        first, second, counts = edge_topology(triangles, len(points))
        check('closedManifoldTopology', np.all(counts == 2))
        report['neutralShoulderAngles'] = {'r34': face_statistics(before, triangles, first, second, allowed), 'candidate': face_statistics(after, triangles, first, second, allowed)}
        check('neutralNoDegenerateTriangles', report['neutralShoulderAngles']['candidate']['degenerateTriangles'] == 0)
        check('neutralShoulderHasNoNewSevereFold', report['neutralShoulderAngles']['candidate']['maximumDegrees'] <= max(30., report['neutralShoulderAngles']['r34']['maximumDegrees']))
        report['crossingScope'] = 'Any triangle touching an actually edited vertex against the whole body. Unchanged distant contact regions are excluded.'
        crossing_region = edited_vertices
        if args.baseline_self_test:
            crossing_region = (abs(material[:, 0]) > .08) & (abs(material[:, 0]) < .31) & (material[:, 1] > .45) & (before[:, 1] > .45)
        report['neutralShoulderBaselineCrossings'] = crossings(before, triangles, crossing_region)
        report['neutralShoulderCrossings'] = crossings(after, triangles, crossing_region)
        report['neutralContactComparison'] = compare_contact_footprints(report['neutralShoulderBaselineCrossings'], report['neutralShoulderCrossings'], args.contact_footprint_tolerance)
        check('neutralNoNewProperCrossingOutsideInheritedContactFootprint', report['neutralContactComparison']['withinOriginalFootprintAtDeclaredTolerance'])
        report['capturedPoses'] = []
        if args.captures:
            files = sorted(args.captures.glob('*_Vertices.bin'))
            check('actualUnityCapturedVerticesPresent', bool(files))
            for path in files:
                pose = path.stem.removesuffix('_Vertices')
                p = captured_points(path, len(points))
                row = {'pose': pose, 'candidate': face_statistics(p, triangles, first, second, allowed)}
                baseline = BASELINE_CAPTURES/path.name
                if baseline.exists():
                    row['r34'] = face_statistics(captured_points(baseline, len(points)), triangles, first, second, allowed)
                check(pose+'NoDegenerateTriangles', row['candidate']['degenerateTriangles'] == 0)
                if args.crossings and pose in args.crossing_poses.split(','):
                    row['crossings'] = crossings(p, triangles, crossing_region)
                report['capturedPoses'].append(row)
                print(json.dumps(row), flush=True)
                if 'crossings' in row:
                    check(pose+'NoProperNonAdjacentCrossings', row['crossings']['properNonAdjacentCrossingPairs'] == 0)
        report['candidateSha256'] = sha(blend)
        report['transportSha256'] = sha(model_path)
        report['passed'] = True
    except Exception as error:
        report['failure'] = repr(error)
        raise
    finally:
        output.write_text(json.dumps(report, indent=2)+'\n')
        print('Validation report: '+str(output), flush=True)
    print(json.dumps({'passed': report['passed'], 'geometry': report['geometry'], 'neutralShoulderAngles': report['neutralShoulderAngles']}), flush=True)


if __name__ == '__main__':
    main()
