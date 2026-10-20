#!/usr/bin/env python3
"""Author hand-masked punch candidates and review anchored Grab/Wall IK.

Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <new-output-dir> [--peak-only]
Fixtures are kinematic review inputs, not gameplay contact or hit authority.
"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
MOTION_PATH = ROOT / 'tools/blender/review_c1b_rw020_motion_cycles.py'
MOTION_SHA = 'b3a7e56a478d9e5694e3195222b338169b99e232e9468523bc9dabf6cc7814b0'
ARM_PARTS = ('UpperArm', 'Forearm', 'HandLogical')
SCENARIOS = ('Punch_L', 'Punch_R', 'Grab_L', 'Grab_R', 'Grab_LR', 'Wall_L', 'Wall_R', 'Wall_LR')


def load(name, path):
    spec = importlib.util.spec_from_file_location(name, str(path))
    mod = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(mod)
    return mod


def ease(t):
    t = min(1.0, max(0.0, t))
    return t*t*(3-2*t)


def arm_rotations(rig, side):
    return {part: rig.pose.bones[f'{part}_{side}'].matrix_basis.to_quaternion().normalized()
            for part in ARM_PARTS}


def set_arm(rig, side, rotations):
    for part, rotation in rotations.items():
        bone = rig.pose.bones[f'{part}_{side}']
        bone.rotation_mode = 'QUATERNION'
        bone.location = (0, 0, 0)
        bone.scale = (1, 1, 1)
        bone.rotation_quaternion = rotation
    bpy.context.view_layer.update()


def blend_arm(rig, side, start, end, t):
    set_arm(rig, side, {part: start[part].slerp(end[part], ease(t)) for part in ARM_PARTS})


def aim_bone(bone, target):
    matrix = bone.matrix.copy()
    direction = Vector(target) - bone.head
    swing = (bone.tail - bone.head).normalized().rotation_difference(direction.normalized())
    pose = (swing.to_matrix() @ matrix.to_3x3()).to_4x4()
    pose.translation = matrix.translation
    bone.matrix = pose
    bpy.context.view_layer.update()


def anchored_arm(rig, side, world_target):
    # Bind-frame solve avoids carrying twist from an arbitrary preceding pose.
    for part in ARM_PARTS:
        rig.pose.bones[f'{part}_{side}'].matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    upper = rig.pose.bones[f'UpperArm_{side}']
    forearm = rig.pose.bones[f'Forearm_{side}']
    terminal = rig.pose.bones[f'HandLogical_{side}']
    target = rig.matrix_world.inverted() @ Vector(world_target)
    origin = upper.head.copy()
    a = upper.bone.length
    b = forearm.bone.length + terminal.bone.length
    delta = target-origin
    distance = delta.length
    if not abs(a-b)+1e-5 < distance < a+b-1e-5:
        raise RuntimeError(f'unreachable target {side}: {distance} vs {a+b}; no arm stretch allowed')
    direction = delta.normalized()
    pole = Vector((0, 0, -1))
    bend = pole-direction*pole.dot(direction)
    if bend.length < 1e-5:
        bend = Vector((-1 if side == 'L' else 1, 0, 0))
    bend.normalize()
    projection = (a*a+distance*distance-b*b)/(2*distance)
    height = math.sqrt(max(0, a*a-projection*projection))
    elbow = origin+direction*projection+bend*height
    aim_bone(upper, elbow)
    aim_bone(forearm, target)
    aim_bone(terminal, target)
    return arm_rotations(rig, side)


def prepare_candidate_base_pose(h, motion, rig):
    # A hand-only Action needs a usable unkeyed base pose on the other hand.
    # Persist quaternion mode so selecting the Action works after reopening.
    rig.animation_data.action = None
    h.apply_pose(rig, motion.IDLE)
    for bone in rig.pose.bones:
        if bone.name.startswith('HipDeform_'):
            continue
        basis = bone.matrix_basis.copy()
        bone.rotation_mode = 'QUATERNION'
        bone.matrix_basis = basis
    bpy.context.scene.render.fps = 24
    bpy.context.scene['review_base_pose'] = 'Idle; unkeyed opposite hand remains available for layering'


def main():
    args = sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    peak_only = '--peak-only' in args
    args = [x for x in args if x != '--peak-only']
    if len(args) != 1:
        raise RuntimeError('expected new output directory and optional --peak-only')
    out = Path(args[0]).resolve()
    if out.exists():
        raise RuntimeError('preserve previous output; choose a new directory')
    assert hashlib.sha256(MOTION_PATH.read_bytes()).hexdigest() == MOTION_SHA
    motion = load('interaction_motion', MOTION_PATH)
    h = load('interaction_helpers', motion.HELPER_PATH)
    assert h.sha256(motion.HELPER_PATH) == motion.HELPER_SHA
    assert h.sha256(h.R20_GENERATOR) == h.R20_GENERATOR_SHA256
    r20 = load('interaction_rig', h.R20_GENERATOR)
    r18 = r20.r18
    source = ROOT / 'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    assert h.sha256(source) == h.R20_BLEND_SHA256
    assert h.sha256(h.R16_BLEND) == h.R16_BLEND_SHA256
    out.mkdir(parents=True)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    scene = bpy.context.scene
    rig = bpy.data.objects[h.ARMATURE_NAME]
    body = bpy.data.objects[h.BODY_NAME]
    assert len(bpy.data.actions) == 0
    collection, cameras, lights = h.configure_scene(scene, r18)
    scene.render.resolution_x = scene.render.resolution_y = 640
    for camera in cameras.values(): camera.data.ortho_scale = 1.5
    rest = r18.r12.mesh_coordinates(body.data)
    edges = r18.r12.mesh_edges(body.data)
    fixture_material = r18.create_material('TMP_ContactFixture', (.15,.32,.40))
    target_material = r18.create_material('TMP_Anchor', (.95,.38,.08), emission_strength=.3)
    fixtures=[]

    def cube(name, center, scale):
        bpy.ops.mesh.primitive_cube_add(size=1, location=center)
        obj=bpy.context.object;obj.name=name;obj.scale=scale
        obj.data.materials.append(fixture_material)
        modifier=obj.modifiers.new('ReviewWireframe','WIREFRAME');modifier.thickness=.005
        fixtures.append(obj)
        return obj

    def sphere(name, target):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=12, ring_count=8, radius=.012, location=target)
        obj=bpy.context.object;obj.name=name;obj.data.materials.append(target_material);fixtures.append(obj)
        return obj

    punch_poses={}
    for side in ('L','R'):
        h.apply_pose(rig,motion.IDLE);idle=arm_rotations(rig,side)
        h.apply_pose(rig,{**motion.IDLE,f'UpperArm_{side}':(-45,0,35 if side=='L' else -35),
                         f'Forearm_{side}':(0,0,55 if side=='L' else -55)})
        ready=arm_rotations(rig,side)
        h.apply_pose(rig,motion.IDLE);motion.apply_straight_arm_grip(rig,side)
        punch_poses[side]=(idle,ready,arm_rotations(rig,side))
    results=[];actions=[];report_path=out/'InteractionReview.json'
    for scenario in SCENARIOS:
        kind,hand_mask=scenario.split('_');sides=list(hand_mask)
        if rig.animation_data: rig.animation_data.action=None
        h.apply_pose(rig,motion.IDLE)
        targets={side:Vector((-.20 if side=='L' else .20,-.20,.91)) if kind=='Wall'
                 else Vector((-.21 if side=='L' else .21,-.34,.635)) for side in sides}
        if kind=='Grab': cube('TMP_GrabObject',(0 if len(sides)==2 else targets[sides[0]].x,-.39,.635),(.58 if len(sides)==2 else .16,.10,.16))
        if kind=='Wall': cube('TMP_WallLedge',(0,-.22,.52),(.85,.04,.78))
        if kind!='Punch':
            for side,target in targets.items():sphere('TMP_Anchor_'+side,target)
        frames=[];action=None
        if kind=='Punch' and not peak_only:
            action=bpy.data.actions.new('REVIEW_'+scenario+'_r01');action.use_fake_user=True
            rig.animation_data_create();rig.animation_data.action=action
            action['review_only']=True;action['hand_mask']=hand_mask;action['gameplay_root_motion']=False
            actions.append(action)
        phases=[.2,.5,.75] if peak_only else [i/24 for i in range(25)]
        for index,t in enumerate(phases):
            scene.frame_set(index+1)
            h.apply_pose(rig,motion.IDLE)
            phase=''
            if kind=='Punch':
                idle,ready,extended=punch_poses[sides[0]]
                if t<.25: blend_arm(rig,sides[0],idle,ready,t/.25);phase='prepare'
                elif t<.45: blend_arm(rig,sides[0],ready,extended,(t-.25)/.20);phase='extend'
                elif t<.55: set_arm(rig,sides[0],extended);phase='extension sample'
                else: blend_arm(rig,sides[0],extended,idle,(t-.55)/.45);phase='return'
                if action:
                    for part in ARM_PARTS:
                        bone=rig.pose.bones[f'{part}_{sides[0]}']
                        bone.keyframe_insert(data_path='rotation_quaternion',frame=index+1,group=bone.name)
            else:
                strength=ease(t/.25) if t<.25 else (1-ease((t-.75)/.25) if t>.75 else 1)
                phase='reach' if t<.25 else ('release' if t>.75 else 'anchored hold')
                offset=Vector((.018*math.sin(t*math.tau*2)*strength,0,-.020*(1-math.cos(t*math.tau))*strength/2))
                root_bone=rig.pose.bones['Root']
                root_bone.matrix=Matrix.Translation(offset) @ root_bone.bone.matrix_local
                bpy.context.view_layer.update()
                for side in sides:
                    idle=arm_rotations(rig,side);solution=anchored_arm(rig,side,targets[side])
                    blend_arm(rig,side,idle,solution,strength)
            metrics=r18.mesh_pose_metrics(body,rest,edges)
            valid=(metrics['manifold']['result']=='PASS' and metrics['fold']['hardEdgeCountAt45Degrees']==0
                   and metrics['fold']['foldoverEdgeCountAt90Degrees']==0 and metrics['selfIntersection']['result']=='PASS')
            evaluated=rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
            hands={side:rig.matrix_world @ evaluated.pose.bones[f'HandLogical_{side}'].tail for side in sides}
            errors={side:(hands[side]-targets[side]).length for side in sides} if phase=='anchored hold' else {}
            contact_ok=all(v<.0001 for v in errors.values())
            images=[]
            for view in ('ThreeQuarterRight','SideLeft'):
                path=out/f'{scenario}_{index:03d}_{view}.png';r18.render_to(scene,cameras[view],str(path))
                images.append({'view':view,'path':str(path),'sha256':h.sha256(path)})
            frames.append({'frame':index+1,'phaseFraction':t,'phase':phase,'metrics':metrics,
                           'anchorErrorMeters':errors,'images':images,
                           'result':'PASS' if valid and contact_ok else 'FAIL'})
            print('INTERACTION_FRAME',scenario,index,frames[-1]['result'],errors,flush=True)
        if action:
            rig.animation_data.action=None
        r18.remove_objects(fixtures);fixtures.clear()
        results.append({'id':scenario,'frames':frames,'frameRate':24,'durationSeconds':1.0,
                        'kind':'authored hand-only action' if action else 'kinematic IK fixture',
                        'hitAuthorityTested':False,'physicsContactTested':False,
                        'result':'PASS' if all(f['result']=='PASS' for f in frames) else 'FAIL'})
        report_path.write_text(json.dumps({'complete':False,'scenarios':results},indent=2)+'\n')
    h.reset_pose(rig)
    candidate=None
    if not peak_only and all(s['result']=='PASS' for s in results):
        r18.remove_objects([*cameras.values(),*lights])
        prepare_candidate_base_pose(h, motion, rig)
        scene['review_scope']='Punch hand-only Actions; Grab/Wall use procedural anchor solve. Not game physics.'
        scene['source_sha256']=h.R20_BLEND_SHA256
        candidate=out/'R20_InteractionAnimationCandidate_r01.blend'
        bpy.ops.wm.save_as_mainfile(filepath=str(candidate))
    preservation={'r20Unchanged':h.sha256(source)==h.R20_BLEND_SHA256,'r16Unchanged':h.sha256(h.R16_BLEND)==h.R16_BLEND_SHA256,
                  'driverCount':len(rig.animation_data.drivers),'boneCount':len(rig.data.bones),'sourceSaved':False}
    assert preservation['r20Unchanged'] and preservation['r16Unchanged']
    report={'complete':True,'peakOnly':peak_only,'scenarios':results,'preservation':preservation,
            'generatorSha256':h.sha256(__file__),'candidatePath':str(candidate) if candidate else None,
            'candidateSha256':h.sha256(candidate) if candidate else None,'authoredActionCount':len(actions),
            'result':'PASS' if all(s['result']=='PASS' for s in results) else 'REVIEW_REQUIRED',
            'limitations':['Kinematic fixtures do not test contact, mass, gravity, hit or network authority.',
                           'Root offsets during hold are fixture inputs, not authored root-motion curves.',
                           'Frame samples are checked; subframe/Unity import and user visual approval remain pending.']}
    report_path.write_text(json.dumps(report,indent=2)+'\n');print('INTERACTION_COMPLETE',report['result'],flush=True)
    if report['result']!='PASS':raise RuntimeError('interaction pose/anchor review required')


if __name__=='__main__':main()
