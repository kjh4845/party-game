#!/usr/bin/env python3
"""Sample complete procedural Walk/Sprint cycles and one side-independent Grip.

Run Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <new-output-dir>. The existing r16/r20 files and their Actions stay unchanged.
This is a deformation/motion review, not an authored runtime animation asset.
"""

import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[2]
HELPER_PATH = ROOT / 'tools/blender/render_c1b_rw020_pose_tests.py'
HELPER_SHA = 'dad7a246f8972e995e6a57ab578895a59de9ae4a50a3460993ee850f2a06cb4d'
IDLE = {'UpperArm_L': (-85, 0, 0), 'UpperArm_R': (-85, 0, 0)}
CYCLES = {'Walk': {'frames': 36, 'fps': 30, 'armSwing': 10, 'thighSwing': 18, 'kneeSwing': 15},
          'Sprint': {'frames': 24, 'fps': 30, 'armSwing': 25, 'thighSwing': 35, 'kneeSwing': 30}}


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def locomotion_pose(phase, profile):
    stride = math.sin(phase * math.tau)
    # After the X lowering rotation, local Y produces the sagittal swing.
    # Local Z would mostly rotate the small residual horizontal arm component.
    # Mirrored bind frames give opposite world trajectories from the same sign.
    return {**IDLE, 'UpperArm_L': (-85, -profile['armSwing'] * stride, 0),
            'UpperArm_R': (-85, -profile['armSwing'] * stride, 0),
            'Thigh_L': (-profile['thighSwing'] * stride, 0, 0),
            'Thigh_R': (profile['thighSwing'] * stride, 0, 0),
            'Calf_L': (profile['kneeSwing'] * max(0, -stride), 0, 0),
            'Calf_R': (profile['kneeSwing'] * max(0, stride), 0, 0)}


def apply_straight_arm_grip(armature, side):
    """The same arm chain and forward direction for either hand; no gun IDs."""
    # Solve from the same bind frame, not from an arbitrary preceding pose.
    # A shortest-arc swing from lowered Idle would carry an unwanted axial roll.
    for part in ('UpperArm', 'Forearm', 'HandLogical'):
        armature.pose.bones[f'{part}_{side}'].matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    for part in ('UpperArm', 'Forearm', 'HandLogical'):
        bone = armature.pose.bones[f'{part}_{side}']
        bpy.context.view_layer.update()
        matrix = bone.matrix.copy()
        current_direction = (bone.tail - bone.head).normalized()
        swing = current_direction.rotation_difference(Vector((0, -1, 0)))
        target_rotation = swing.to_matrix() @ matrix.to_3x3()
        target = target_rotation.to_4x4()
        target.translation = matrix.translation
        bone.matrix = target
        bpy.context.view_layer.update()


def main():
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if len(args) != 1:
        raise RuntimeError('expected one new output directory')
    out = Path(args[0]).resolve()
    if out.exists():
        raise RuntimeError('output directory already exists')
    assert hashlib.sha256(HELPER_PATH.read_bytes()).hexdigest() == HELPER_SHA
    h = load('motion_review_helpers', HELPER_PATH)
    assert h.sha256(h.R20_GENERATOR) == h.R20_GENERATOR_SHA256
    rig = load('motion_review_rig', h.R20_GENERATOR)
    r18 = rig.r18
    source = ROOT / 'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    assert h.sha256(source) == h.R20_BLEND_SHA256
    assert h.sha256(h.R16_BLEND) == h.R16_BLEND_SHA256
    out.mkdir(parents=True)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    scene = bpy.context.scene
    armature = bpy.data.objects[h.ARMATURE_NAME]
    body = bpy.data.objects[h.BODY_NAME]
    _, cameras, _ = h.configure_scene(scene, r18)
    scene.render.resolution_x = scene.render.resolution_y = 720
    for camera in cameras.values():
        camera.data.ortho_scale = 1.45
    rest = r18.r12.mesh_coordinates(body.data)
    edges = r18.r12.mesh_edges(body.data)
    records, cycle_records, grips = [], {}, []
    report_path = out / 'MotionReview.json'

    def coordinates():
        mesh = r18.evaluated_mesh(body)
        try:
            return r18.r12.mesh_coordinates(mesh)
        finally:
            bpy.data.meshes.remove(mesh)

    def capture(id, profile=None):
        metrics = r18.mesh_pose_metrics(body, rest, edges)
        ok = (metrics['manifold']['result'] == 'PASS'
              and metrics['fold']['foldoverEdgeCountAt90Degrees'] == 0
              and metrics['fold']['hardEdgeCountAt45Degrees'] == 0
              and metrics['selfIntersection']['result'] == 'PASS')
        evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
        endpoints = {name: list(evaluated.pose.bones[name].tail)
                     for name in ('HandLogical_L', 'HandLogical_R', 'Foot_L', 'Foot_R')}
        images = []
        for view in ('ThreeQuarterRight', 'SideLeft'):
            path = out / f'{id}_{view}.png'
            r18.render_to(scene, cameras[view], str(path))
            images.append({'view': view, 'path': str(path), 'sha256': h.sha256(path)})
        record = {'id': id, 'metrics': metrics, 'geometryResult': 'PASS' if ok else 'FAIL',
                  'terminalPositions': endpoints, 'images': images}
        if profile:
            record['profile'] = profile
        records.append(record)
        report_path.write_text(json.dumps({'complete': False, 'samples': records}, indent=2) + '\n')
        print('MOTION_SAMPLE', id, record['geometryResult'], flush=True)
        return record

    for mode, profile in CYCLES.items():
        phase_zero = None
        for frame in range(profile['frames']):
            phase = frame / profile['frames']
            h.apply_pose(armature, locomotion_pose(phase, profile))
            if frame == 0:
                phase_zero = coordinates()
            capture(f'{mode}_{frame:03d}', {'phase': phase, **profile})
        h.apply_pose(armature, locomotion_pose(1, profile))
        closure = float(np.linalg.norm(coordinates() - phase_zero, axis=1).max())
        samples = [r for r in records if r['id'].startswith(mode + '_')]
        first_extreme = samples[profile['frames']//4]['terminalPositions']
        opposite_extreme = samples[3*profile['frames']//4]['terminalPositions']
        trajectories = {}
        for side in ('L', 'R'):
            hand_delta = first_extreme[f'HandLogical_{side}'][1] - opposite_extreme[f'HandLogical_{side}'][1]
            foot_delta = first_extreme[f'Foot_{side}'][1] - opposite_extreme[f'Foot_{side}'][1]
            trajectories[side] = {'handForwardTravel': abs(hand_delta), 'footForwardTravel': abs(foot_delta),
                                  'contralateral': hand_delta * foot_delta < 0,
                                  'visibleArmSwing': abs(hand_delta) > 0.04}
        cycle_records[mode] = {**profile, 'durationSeconds': profile['frames']/profile['fps'],
                               'loopClosureMaximumVertexDelta': closure,
                               'loopClosed': closure < 1e-6, 'trajectories': trajectories,
                               'frameCountIncludesClosingDuplicate': False}
    grip_coordinates = {}
    for sides in [('L',), ('R',), ('L', 'R')]:
        h.apply_pose(armature, IDLE)
        for side in sides:
            apply_straight_arm_grip(armature, side)
        record = capture('SharedGrip_' + ''.join(sides))
        direction_errors = {}
        evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
        for side in sides:
            for part in ('UpperArm', 'Forearm', 'HandLogical'):
                bone = evaluated.pose.bones[f'{part}_{side}']
                direction_errors[bone.name] = math.degrees((bone.tail-bone.head).angle(Vector((0,-1,0))))
        record['armDirectionErrorDegrees'] = direction_errors
        grips.append(record)
        if len(sides) == 1:
            grip_coordinates[sides[0]] = coordinates()
    tree = KDTree(len(rest))
    for i, vertex in enumerate(rest):
        tree.insert(vertex, i)
    tree.balance()
    mirror = [tree.find((-v[0], v[1], v[2]))[1] for v in rest]
    reflected = grip_coordinates['L'].copy()
    reflected[:, 0] *= -1
    mirror_delta = float(np.linalg.norm(reflected-grip_coordinates['R'][mirror], axis=1).max())
    h.reset_pose(armature)
    preservation = {'r20Unchanged': h.sha256(source) == h.R20_BLEND_SHA256,
                    'r16Unchanged': h.sha256(h.R16_BLEND) == h.R16_BLEND_SHA256,
                    'actionCount': len(bpy.data.actions), 'driverCount': len(armature.animation_data.drivers),
                    'restPoseRestored': r18.pose_is_rest(armature), 'sourceSaved': False}
    assert preservation['r20Unchanged'] and preservation['r16Unchanged'] and preservation['restPoseRestored']
    assert preservation['actionCount'] == 0 and preservation['driverCount'] == 8
    failed = [r['id'] for r in records if r['geometryResult'] == 'FAIL']
    motion_ok = all(v['loopClosed'] and all(t['contralateral'] and t['visibleArmSwing']
                    for t in v['trajectories'].values()) for v in cycle_records.values())
    motion_ok = motion_ok and all(cycle_records['Sprint']['trajectories'][side]['handForwardTravel']
                                 > cycle_records['Walk']['trajectories'][side]['handForwardTravel'] for side in ('L', 'R'))
    report = {'complete': True, 'sourceSha256': h.R20_BLEND_SHA256,
              'generatorSha256': h.sha256(__file__), 'samples': records, 'cycles': cycle_records,
              'sharedGrip': {'templateCount': 1, 'evaluatedHandMasks': ['L', 'R', 'LR'],
                             'mirrorMaximumVertexDelta': mirror_delta, 'weaponSpecificPoseCount': 0},
              'preservation': preservation, 'failedGeometrySamples': failed,
              'motionTrajectoryResult': 'PASS' if motion_ok else 'FAIL',
              'technicalResult': 'PASS' if not failed and motion_ok else 'FAIL',
              'visualApproval': False,
              'limitations': ['Sampled frames only; no claim of continuous collision freedom between frames.',
                              'Cycle timing and amplitudes are review START values, not gameplay tuning approval.',
                              'No ground contact, root translation, physics motor or Unity runtime was tested.',
                              'Ragdoll/GetUp recovery is a separate review. No fire/recoil or reload clip authored.']}
    report_path.write_text(json.dumps(report, indent=2)+'\n')
    print('MOTION_REVIEW_COMPLETE',report['technicalResult'],str(report_path),flush=True)
    if failed or not motion_ok:
        raise RuntimeError('deformation or motion trajectory failures need review')


if __name__ == '__main__':
    main()
