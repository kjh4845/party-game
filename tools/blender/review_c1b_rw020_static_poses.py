#!/usr/bin/env python3
"""Review official C1b poses and September 4 action readiness without saving r20.

Usage: Blender --background --factory-startup --python <script> -- <new-output-dir>
Outputs are local review evidence, never animation clips or a shipping asset.
"""

import importlib.util
import json
from pathlib import Path
import sys

import bpy
from mathutils import Vector


ROOT = Path(__file__).resolve().parents[2]
HELPER_PATH = ROOT / 'tools/blender/render_c1b_rw020_pose_tests.py'
HELPER_SHA = 'dad7a246f8972e995e6a57ab578895a59de9ae4a50a3460993ee850f2a06cb4d'
OFFICIAL_IDS = ('Neutral', 'BothHandsGrab', 'StrikeReady_L', 'StrikeReady_R',
                'AirKick_L', 'AirKick_R', 'Dropkick', 'AirHandReach')
IDLE = {'UpperArm_L': (-85, 0, 0), 'UpperArm_R': (-85, 0, 0)}
FORWARD = {'UpperArm_L': (0, 0, 80), 'UpperArm_R': (0, 0, -80)}
POSES = (
    ('Neutral', 'Official', {}),
    ('BothHandsGrab', 'Official', {**FORWARD, 'Forearm_L': (0, 0, 10), 'Forearm_R': (0, 0, -10)}),
    ('StrikeReady_L', 'Official', {**IDLE, 'UpperArm_L': (-45, 0, 35), 'Forearm_L': (0, 0, 55)}),
    ('StrikeReady_R', 'Official', {**IDLE, 'UpperArm_R': (-45, 0, -35), 'Forearm_R': (0, 0, -55)}),
    ('AirKick_L', 'Official', {**IDLE, 'Thigh_L': (-45, 0, 0), 'Calf_L': (8, 0, 0)}),
    ('AirKick_R', 'Official', {**IDLE, 'Thigh_R': (-45, 0, 0), 'Calf_R': (8, 0, 0)}),
    ('Dropkick', 'Official', {'Root': (-55, 0, 0), 'UpperArm_L': (-60, 0, 20),
                            'UpperArm_R': (-60, 0, -20), 'Thigh_L': (-35, 0, 0),
                            'Thigh_R': (-35, 0, 0), 'Calf_L': (5, 0, 0), 'Calf_R': (5, 0, 0)}),
    ('AirHandReach', 'Official', {'UpperArm_L': (55, 0, 10), 'UpperArm_R': (55, 0, -10)}),
    ('Idle_ArmsToTorso', 'ActionReadiness', IDLE),
    ('Walk_StrideSample', 'ActionReadiness', {'UpperArm_L': (-80, 0, 8), 'UpperArm_R': (-80, 0, 8),
                                          'Thigh_L': (-18, 0, 0), 'Thigh_R': (12, 0, 0), 'Calf_R': (15, 0, 0)}),
    ('Sprint_StrideSample', 'ActionReadiness', {'UpperArm_L': (-75, 0, 25), 'UpperArm_R': (-75, 0, 25),
                                            'Thigh_L': (-35, 0, 0), 'Thigh_R': (20, 0, 0), 'Calf_R': (30, 0, 0)}),
    ('Pistol_MainArmGrip', 'ActionReadiness', {**IDLE, 'UpperArm_R': (0, 0, -80)}),
    ('LongGun_TwoArmGrip', 'ActionReadiness', FORWARD),
)


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def main():
    import hashlib
    args = sys.argv[sys.argv.index('--') + 1:] if '--' in sys.argv else []
    if len(args) != 1:
        raise RuntimeError('expected one new output directory')
    out = Path(args[0]).resolve()
    if out.exists():
        raise RuntimeError('output must be new; preserve previous reviews')
    if hashlib.sha256(HELPER_PATH.read_bytes()).hexdigest() != HELPER_SHA:
        raise RuntimeError('pose evaluation helper changed; review before repinning')
    helper = load('r20_static_helpers', HELPER_PATH)
    helper.require(helper.sha256(helper.R20_GENERATOR) == helper.R20_GENERATOR_SHA256, 'r20 generator drift')
    rig = load('r20_static_rig', helper.R20_GENERATOR)
    r18 = rig.r18
    source = ROOT / 'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    helper.require(helper.sha256(source) == helper.R20_BLEND_SHA256, 'r20 source drift')
    helper.require(helper.sha256(helper.R16_BLEND) == helper.R16_BLEND_SHA256, 'r16 source drift')
    out.mkdir(parents=True)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    scene = bpy.context.scene
    armature = bpy.data.objects[helper.ARMATURE_NAME]
    body, head = bpy.data.objects[helper.BODY_NAME], bpy.data.objects[helper.HEAD_NAME]
    helper.require(len(armature.data.bones) == 22 and len(bpy.data.actions) == 0, 'unexpected rig/Action scope')
    helper.require(len(armature.animation_data.drivers) == 8, 'helper driver count drift')
    rest = r18.r12.mesh_coordinates(body.data)
    edges = r18.r12.mesh_edges(body.data)
    collection, cameras, lights = helper.configure_scene(scene, r18)
    cameras['Back'] = r18.create_camera(collection, 'TMP_Static_Back', (0, 4, .5), (0, 0, .5), 1.38)
    scene.render.resolution_x = scene.render.resolution_y = 2048
    original_materials = {obj: list(obj.data.materials) for obj in (body, head)}
    silhouette = r18.create_material('TMP_Static_Silhouette', (0, 0, 0), emission_strength=1.0)
    silhouette.node_tree.nodes.clear()
    surface = silhouette.node_tree.nodes.new('ShaderNodeEmission')
    surface.inputs['Color'].default_value = (0, 0, 0, 1)
    output = silhouette.node_tree.nodes.new('ShaderNodeOutputMaterial')
    silhouette.node_tree.links.new(surface.outputs[0], output.inputs['Surface'])
    camera_origins = {name: camera.location.copy() for name, camera in cameras.items()}
    records = []
    report_path = out / 'StaticPoseReview.json'

    def render_objects(id, objects, view, style):
        if style == 'Silhouette':
            for obj in objects:
                obj.data.materials.clear()
                obj.data.materials.append(silhouette)
        path = out / f'{id}_{style}_{view}.png'
        r18.render_to(scene, cameras[view], str(path))
        if style == 'Silhouette':
            for obj in objects:
                obj.data.materials.clear()
                for material in original_materials[body if 'Body' in obj.name else head]:
                    obj.data.materials.append(material)
        return {'view': view, 'style': style, 'path': str(path), 'sha256': helper.sha256(path)}

    try:
        for id, category, rotations in POSES:
            helper.apply_pose(armature, rotations)
            depsgraph = bpy.context.evaluated_depsgraph_get()
            bounds = [obj.matrix_world @ Vector(corner)
                      for obj in (body, head)
                      for corner in obj.evaluated_get(depsgraph).bound_box]
            center = Vector(tuple((min(v[axis] for v in bounds) + max(v[axis] for v in bounds)) * .5
                                  for axis in range(3)))
            for name, camera in cameras.items():
                camera.location = camera_origins[name] + center - Vector((0, 0, .5))
                camera.data.ortho_scale = 1.5
            metrics = r18.mesh_pose_metrics(body, rest, edges)
            ok = (metrics['manifold']['result'] == 'PASS'
                  and metrics['fold']['foldoverEdgeCountAt90Degrees'] == 0
                  and metrics['fold']['hardEdgeCountAt45Degrees'] == 0
                  and metrics['selfIntersection']['result'] == 'PASS')
            views = ('Front', 'SideLeft', 'Back', 'ThreeQuarterRight') if id == 'Neutral' else ('ThreeQuarterRight',)
            if id == 'Idle_ArmsToTorso':
                views = ('Front', 'ThreeQuarterRight')
            styles = ('Neutral', 'Silhouette') if category == 'Official' else ('Neutral',)
            images = [render_objects(id, (body, head), view, style) for view in views for style in styles]
            evaluated = armature.evaluated_get(bpy.context.evaluated_depsgraph_get())
            endpoints = {name: list(armature.matrix_world @ evaluated.pose.bones[name].tail)
                         for name in ('HandLogical_L', 'HandLogical_R', 'Foot_L', 'Foot_R')}
            records.append({'id': id, 'category': category, 'rotationsLocalDegrees': rotations,
                            'framing': {'centerWorld': list(center), 'orthoScale': 1.5},
                            'metrics': metrics, 'terminalPositionsWorld': endpoints, 'images': images,
                            'geometryResult': 'PASS' if ok else 'FAIL', 'visualApproval': False})
            report_path.write_text(json.dumps({'complete': False, 'poses': records}, indent=2) + '\n')
            print('STATIC_POSE', id, 'PASS' if ok else 'FAIL', flush=True)
        helper.reset_pose(armature)
        cameras['Front'].location = camera_origins['Front']
        for id, offsets in [('Lineup_Overlap', (-.36, -.12, .12, .36)),
                            ('Lineup_Spread', (-1.8, -.6, .6, 1.8))]:
            objects = []
            for slot, x in enumerate(offsets):
                for obj in (body, head):
                    mesh = r18.evaluated_mesh(obj)
                    copy = bpy.data.objects.new(f'TMP_{id}_{slot}_{obj.name}', mesh)
                    collection.objects.link(copy)
                    copy.matrix_world = obj.matrix_world.copy()
                    copy.location.x += x
                    objects.append(copy)
            body.hide_render = head.hide_render = True
            cameras['Front'].data.ortho_scale = 3.0 if id == 'Lineup_Overlap' else 6.3
            images = [render_objects(id, objects, 'Front', style) for style in ('Neutral', 'Silhouette')]
            records.append({'id': id, 'category': 'Lineup', 'participants': 4, 'offsetsX': offsets,
                            'images': images, 'visualApproval': False})
            r18.remove_objects(objects)
            body.hide_render = head.hide_render = False
    finally:
        helper.reset_pose(armature)
        for obj in (body, head):
            obj.data.materials.clear()
            for material in original_materials[obj]:
                obj.data.materials.append(material)
    preservation = {'r20Unchanged': helper.sha256(source) == helper.R20_BLEND_SHA256,
                    'r16Unchanged': helper.sha256(helper.R16_BLEND) == helper.R16_BLEND_SHA256,
                    'actionCount': len(bpy.data.actions), 'driverCount': len(armature.animation_data.drivers),
                    'restPoseRestored': r18.pose_is_rest(armature), 'sourceSaved': False}
    helper.require(all(preservation[k] for k in ('r20Unchanged', 'r16Unchanged', 'restPoseRestored')), 'source preservation failed')
    helper.require(preservation['actionCount'] == 0 and preservation['driverCount'] == 8, 'animation scope changed')
    official = [r['id'] for r in records if r['category'] == 'Official']
    helper.require(official == list(OFFICIAL_IDS), 'official pose set mismatch')
    failed = [r['id'] for r in records if r.get('geometryResult') == 'FAIL']
    report = {'complete': True, 'ownerTask': 'C1BRW-004', 'sourceRevision': 'r20',
              'sourceSha256': helper.R20_BLEND_SHA256, 'generatorSha256': helper.sha256(__file__),
              'officialPoseCount': len(official), 'lineupCount': 2, 'actionReadinessPoseCount': 5,
              'preservation': preservation, 'poses': records, 'failedGeometryPoses': failed,
              'technicalResult': 'FAIL' if failed else 'PASS', 'userVisualApproval': False,
              'limitations': ['Static pose samples only; no timing/cadence, gameplay, physics or Unity parity tested.',
                              'Angles and lineup spacing are review candidates, not locked gameplay tuning.',
                              'No weapon model or recoil animation created. No Down/GetUp runtime completed.',
                              'Mesh QA detects self-intersection/folds; human review must assess shoulder/hip silhouette.']}
    report_path.write_text(json.dumps(report, indent=2) + '\n')
    print('STATIC_REVIEW_COMPLETE', report['technicalResult'], str(report_path), flush=True)
    if failed:
        raise RuntimeError('geometry review required: ' + ', '.join(failed))


if __name__ == '__main__':
    main()
