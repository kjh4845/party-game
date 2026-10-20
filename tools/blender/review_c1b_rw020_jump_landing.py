#!/usr/bin/env python3
"""Author five in-place jump phases on the approved lower-weight candidate.

Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <new-output-directory>
"""
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
FPS = 30
PHASES = [('Takeoff', 0, 10), ('Rise', 10, 18), ('Apex', 18, 22),
          ('Fall', 22, 30), ('Landing', 30, 44)]
# Global frame, planted pelvis drop, airborne knee angle, forward arm swing.
WAYPOINTS = [(0, 0, 0, 0), (5, .025, 0, -.22), (10, 0, 0, .55),
             (18, 0, 45, .65), (22, 0, 35, .50), (30, 0, 0, .20),
             (34, .035, 0, .40), (40, .012, 0, .12), (44, 0, 0, 0)]


def parameters(frame):
    for a, b in zip(WAYPOINTS, WAYPOINTS[1:]):
        if a[0] <= frame <= b[0]:
            u = (frame-a[0])/(b[0]-a[0]); u = u*u*(3-2*u)
            return [x+(y-x)*u for x, y in zip(a[1:], b[1:])]
    raise ValueError(frame)


def main():
    args = sys.argv[sys.argv.index('--')+1:]
    if len(args) != 1:
        raise RuntimeError('expected a new output directory')
    out = Path(args[0]).resolve()
    if out.exists():
        raise RuntimeError('preserve previous candidates')
    sys.path.insert(0, str(ROOT/'tools/blender'))
    import create_c1b_rw020_lower_weight_candidate as weight
    t = weight.load('jump_interactions', ROOT/'tools/blender/review_c1b_rw020_interactions.py')
    motion = weight.load('jump_motion', t.MOTION_PATH)
    h = weight.load('jump_helpers', motion.HELPER_PATH)
    r18 = weight.load('jump_rig', h.R20_GENERATOR).r18
    approval = json.loads((ROOT/'artifacts/reports/R20_LOWER_WEIGHT_VISUAL_APPROVAL_20260905.json').read_text())
    assert approval['status'] == 'APPROVED'
    source = ROOT/approval['candidatePath']
    assert weight.sha(source) == approval['candidateSha256']
    out.mkdir(parents=True)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    scene = bpy.context.scene; rig = bpy.data.objects[h.ARMATURE_NAME]
    body = bpy.data.objects[h.BODY_NAME]; head = bpy.data.objects[h.HEAD_NAME]
    geometry = {o.name: weight.geometry_identity(o) for o in (body, head)}
    weights = {o.name: weight.read_weights(o) for o in (body, head)}
    skeleton = weight.rig_identity(rig)
    punch = {a.name: weight.action_identity(a) for a in bpy.data.actions}
    assert len(punch) == 2
    rest = r18.r12.mesh_coordinates(body.data); edges = r18.r12.mesh_edges(body.data)
    ground = float(rest[:, 2].min())
    rig.animation_data.action = None; h.apply_pose(rig, motion.IDLE)
    feet = {s: rig.pose.bones['Foot_'+s].matrix.copy() for s in ('L', 'R')}
    keyed = [b.name for b in rig.pose.bones if b.name != 'Root' and not b.name.startswith('HipDeform_')]

    def coordinates():
        mesh = r18.evaluated_mesh(body)
        try:
            return r18.r12.mesh_coordinates(mesh)
        finally:
            bpy.data.meshes.remove(mesh)

    def authored_pose(frame):
        rig.animation_data.action = None
        drop, knee, swing = parameters(frame)
        h.apply_pose(rig, {**motion.IDLE, 'Thigh_L': (-knee/2, 0, 0),
                          'Thigh_R': (-knee/2, 0, 0), 'Calf_L': (knee, 0, 0), 'Calf_R': (knee, 0, 0)})
        pelvis = rig.pose.bones['Pelvis']
        if drop > 1e-7:
            pelvis.matrix = Matrix.Translation((0, 0, -drop)) @ pelvis.matrix
            bpy.context.view_layer.update()
            for side in ('L', 'R'):
                a = rig.pose.bones['Thigh_'+side]; b = rig.pose.bones['Calf_'+side]
                f = rig.pose.bones['Foot_'+side]; start = a.head.copy(); target = feet[side].translation
                delta = target-start; d = delta.length
                assert abs(a.bone.length-b.bone.length) < d < a.bone.length+b.bone.length
                direction = delta.normalized(); pole = Vector((0, -1, 0))
                bend = (pole-direction*pole.dot(direction)).normalized()
                projection = (a.bone.length**2+d*d-b.bone.length**2)/(2*d)
                height = math.sqrt(max(0, a.bone.length**2-projection**2))
                t.aim_bone(a, start+direction*projection+bend*height)
                t.aim_bone(b, target); f.matrix = feet[side]
                bpy.context.view_layer.update()
        # Aim the existing lowered arm chain symmetrically; retain the small
        # outward Idle component so the hands do not intersect the torso.
        for side in ('L', 'R'):
            a = rig.pose.bones['UpperArm_'+side]
            direction = (a.tail-a.head).normalized(); direction.y -= swing
            t.aim_bone(a, a.head+direction)
        # Author a flat-floor stance at contact. This corrects the reviewed
        # 3.53 mm surface residual in the pose, without changing skin weights.
        if frame <= 10 or frame >= 30:
            delta = ground-float(coordinates()[:, 2].min())
            pelvis.matrix = Matrix.Translation((0, 0, delta)) @ pelvis.matrix
        bpy.context.view_layer.update()
        for name in keyed:
            bone = rig.pose.bones[name]; basis = bone.matrix_basis.copy()
            bone.rotation_mode = 'QUATERNION'; bone.matrix_basis = basis
            bone.scale = (1, 1, 1)
        bpy.context.view_layer.update()

    names = {}; expected = {}; snapshots = {}
    # Quarter-frame keys bound the skin's contact error near straight knees;
    # half-frame keys previously left a 0.16 mm terminal interpolation error.
    for index in range(177):
        frame = index*.25; authored_pose(frame)
        snapshots[frame] = {name: (list(rig.pose.bones[name].rotation_quaternion),
                                   list(rig.pose.bones[name].location)) for name in keyed}
    for phase, start, end in PHASES:
        action = bpy.data.actions.new('REVIEW_Jump_'+phase+'_r01'); action.use_fake_user = True
        action['review_only'] = True; action['gameplay_root_motion'] = False
        action['phase'] = phase; action['duration_seconds'] = (end-start)/FPS
        names[phase] = action.name; rig.animation_data.action = action
        for index in range((end-start)*4+1):
            global_frame = start+index*.25; local = 1+index*.25
            for name, (q, loc) in snapshots[global_frame].items():
                bone = rig.pose.bones[name]; bone.rotation_quaternion = q; bone.location = loc
                bone.keyframe_insert(data_path='rotation_quaternion', frame=local, group=name)
                if name == 'Pelvis':
                    bone.keyframe_insert(data_path='location', frame=local, group=name)
        for curve in action.layers[0].strips[0].channelbag(action.slots[0]).fcurves:
            for key in curve.keyframe_points:
                key.interpolation = 'LINEAR'
        rig.animation_data.action = None
    # Skin minimum height is nonlinear between rotation keys. Correct only
    # pelvis translation on a finer grid; retain the authored joint motion.
    for phase, start, end in PHASES:
        action = bpy.data.actions[names[phase]]
        if phase in ('Takeoff', 'Landing'):
            for index in range((end-start)*16+1):
                local = 1+index/16
                rig.animation_data.action = None; h.apply_pose(rig, motion.IDLE)
                for name in keyed:
                    rig.pose.bones[name].rotation_mode = 'QUATERNION'
                rig.animation_data.action = action; rig.animation_data.action_slot = action.slots[0]
                scene.frame_set(int(local), subframe=local-int(local)); bpy.context.view_layer.update()
                delta = ground-float(coordinates()[:, 2].min())
                pelvis = rig.pose.bones['Pelvis']
                pelvis.matrix = Matrix.Translation((0, 0, delta)) @ pelvis.matrix
                pelvis.keyframe_insert(data_path='location', frame=local, group='Pelvis')
            for curve in action.layers[0].strips[0].channelbag(action.slots[0]).fcurves:
                for key in curve.keyframe_points:
                    key.interpolation = 'LINEAR'
        expected[phase] = weight.action_identity(action)
        rig.animation_data.action = None
    authored_pose(0)
    scene.render.fps = FPS; scene.frame_start = 1; scene.frame_end = 11
    scene['review_scope'] = 'Five in-place Jump/Landing phases; preview trajectory is not gameplay physics'
    scene['lower_weight_approval_record'] = approval['recordId']
    scene['jump_visual_approval'] = False
    action = bpy.data.actions[names['Takeoff']]
    rig.animation_data.action = action; rig.animation_data.action_slot = action.slots[0]
    scene.frame_set(1)
    candidate = out/'R20_JumpLandingAnimationCandidate_r01.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(candidate)); candidate_sha = weight.sha(candidate)
    bpy.ops.wm.open_mainfile(filepath=str(candidate), load_ui=False)
    scene = bpy.context.scene; rig = bpy.data.objects[h.ARMATURE_NAME]
    body = bpy.data.objects[h.BODY_NAME]; head = bpy.data.objects[h.HEAD_NAME]
    for o in (body, head):
        assert weight.geometry_identity(o) == geometry[o.name]
        current = weight.read_weights(o)
        assert current[0] == weights[o.name][0] and np.array_equal(current[1], weights[o.name][1])
    assert weight.rig_identity(rig) == skeleton and len(rig.animation_data.drivers) == 8
    assert all(weight.action_identity(bpy.data.actions[n]) == curves for n, curves in punch.items())
    assert len(bpy.data.actions) == 7
    for phase, start, end in PHASES:
        action = bpy.data.actions[names[phase]]
        assert weight.action_identity(action) == expected[phase]
        assert all('Root' not in c['path'] and c['path'].startswith('pose.bones[') for c in expected[phase])
    collection, cameras, _ = h.configure_scene(scene, r18)
    scene.render.resolution_x = scene.render.resolution_y = 560
    for camera in cameras.values():
        camera.data.ortho_scale = 1.65
    # A floor line is a visual reference only; no rigid-body scene is implied.
    bpy.ops.mesh.primitive_cube_add(size=1, location=(0, 0, ground-.006))
    floor = bpy.context.object; floor.name = 'TMP_JumpReviewFloor'; floor.scale = (.9, .75, .012)
    floor.data.materials.append(r18.create_material('TMP_JumpFloorMaterial', (.12, .16, .20)))
    pairs, unmatched, _ = r18.mirror_vertex_pairs(rest); assert unmatched == 0
    pair_array = np.asarray(pairs, dtype=np.int32)
    records = []; endpoints = {}; maximum_mirror = 0; contact_max = 0; anchor_xy_max = 0
    rendered = set(); root_before = rig.matrix_world.copy()
    for phase, start, end in PHASES:
        for index in range((end-start)*2+1):
            local = 1+index*.5; global_frame = start+index*.5
            rig.animation_data.action = None; h.apply_pose(rig, motion.IDLE)
            for name in keyed:
                rig.pose.bones[name].rotation_mode = 'QUATERNION'
            action = bpy.data.actions[names[phase]]
            rig.animation_data.action = action; rig.animation_data.action_slot = action.slots[0]
            scene.frame_set(int(local), subframe=local-int(local)); bpy.context.view_layer.update()
            points = coordinates()
            if index == 0: endpoints[(phase, 'start')] = points.copy()
            if index == (end-start)*2: endpoints[(phase, 'end')] = points.copy()
            mirrored = points[pair_array[:, 0]].copy(); mirrored[:, 0] *= -1
            mirror_error = float(np.linalg.norm(mirrored-points[pair_array[:, 1]], axis=1).max())
            maximum_mirror = max(maximum_mirror, mirror_error)
            contact = global_frame <= 10 or global_frame >= 30
            delta = float(points[:, 2].min()-ground)
            if contact:
                contact_max = max(contact_max, abs(delta))
                for side in ('L', 'R'):
                    actual = rig.pose.bones['Foot_'+side].head; target = feet[side].translation
                    anchor_xy_max = max(anchor_xy_max, math.hypot(actual.x-target.x, actual.y-target.y))
            metrics = r18.mesh_pose_metrics(body, rest, edges)
            valid = (metrics['manifold']['result'] == 'PASS' and metrics['fold']['hardEdgeCountAt45Degrees'] == 0
                     and metrics['fold']['foldoverEdgeCountAt90Degrees'] == 0 and metrics['selfIntersection']['result'] == 'PASS')
            assert max(abs(rig.matrix_world[i][j]-root_before[i][j]) for i in range(4) for j in range(4)) < 1e-6
            assert all(abs(v) < 1e-6 for v in rig.pose.bones['Root'].location)
            assert max(abs(v-1) for name in keyed for v in rig.pose.bones[name].scale) < 1e-5
            images = []
            if global_frame.is_integer() and global_frame not in rendered:
                rendered.add(global_frame)
                # Preview only: a 0.22 m parabolic arc. Nothing is keyed on Root
                # or object transforms, and this is never saved to the candidate.
                u = min(1, max(0, (global_frame-10)/20)); height = .22*4*u*(1-u)
                rig.location.z += height; bpy.context.view_layer.update()
                for view in ('ThreeQuarterRight', 'SideLeft'):
                    p = out/f'Jump_{int(global_frame):03d}_{view}.png'
                    r18.render_to(scene, cameras[view], str(p))
                    images.append({'path': str(p), 'view': view, 'sha256': weight.sha(p)})
                rig.matrix_world = root_before; bpy.context.view_layer.update()
            row = {'phase': phase, 'localFrame': local, 'globalFrame': global_frame,
                   'contactExpected': contact, 'surfaceGroundDeltaMeters': delta,
                   'mirrorMaximumVertexDelta': mirror_error, 'metrics': metrics, 'images': images,
                   'result': 'PASS' if valid else 'FAIL'}
            records.append(row)
            (out/'JumpLandingReview.json').write_text(json.dumps({'complete': False, 'samples': records}, indent=2)+'\n')
            print('JUMP_SAMPLE', phase, local, row['result'], flush=True)
    seams = {PHASES[i][0]+'->'+PHASES[i+1][0]: float(np.linalg.norm(
        endpoints[(PHASES[i][0], 'end')]-endpoints[(PHASES[i+1][0], 'start')], axis=1).max()) for i in range(4)}
    closure = float(np.linalg.norm(endpoints[('Takeoff', 'start')]-endpoints[('Landing', 'end')], axis=1).max())
    assert maximum_mirror < 1e-5 and max(seams.values()) < 1e-5 and closure < 1e-5
    assert contact_max < 1e-4 and anchor_xy_max < 1e-5
    assert weight.sha(source) == approval['candidateSha256'] and weight.sha(candidate) == candidate_sha
    assert weight.sha(h.R16_BLEND) == h.R16_BLEND_SHA256
    assert all(r['result'] == 'PASS' for r in records), 'surface deformation needs review'
    report = {'complete': True, 'result': 'PASS', 'candidatePath': str(candidate), 'candidateSha256': candidate_sha,
              'sourcePath': str(source), 'sourceSha256': approval['candidateSha256'], 'generatorSha256': weight.sha(__file__),
              'actions': names, 'phases': PHASES, 'fps': FPS, 'durationSeconds': 44/FPS,
              'sampleCount': len(records), 'renderCount': len(rendered)*2, 'samples': records,
              'maximumMirrorDeltaMeters': maximum_mirror, 'phaseSeamsMeters': seams, 'idleClosureMeters': closure,
              'contactMaximumGroundDeltaMeters': contact_max, 'footAnchorMaximumXYDeltaMeters': anchor_xy_max,
              'preservation': {'geometryWeightsSkeletonUnchanged': True, 'approvedPunchCurvesUnchanged': True,
                               'sourceFilesUnchanged': True, 'bones': 22, 'helperDrivers': 8},
              'gameplayRootMotion': False, 'userVisualApproval': False,
              'limitations': ['Flat-floor animation prototype; Unity contact, collision, movement and phase selection are not integrated.',
                              'Preview arc 0.22m and phase timings are illustrative, not gameplay tuning.',
                              'Moving jumps, terrain, fall-height variants, interruptions and Grip layering remain separate scenarios.']}
    (out/'JumpLandingReview.json').write_text(json.dumps(report, indent=2)+'\n')
    print('JUMP_COMPLETE', report['result'], flush=True)


if __name__ == '__main__':
    main()
