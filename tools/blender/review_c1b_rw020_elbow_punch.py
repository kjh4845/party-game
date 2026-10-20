#!/usr/bin/env python3
"""Author and reopen elbow-driven punch candidates; preserve r20 and prior work.

Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <new-output-directory>
"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Quaternion, Vector
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[2]
INTERACTION_PATH=ROOT/'tools/blender/review_c1b_rw020_interactions.py'
INTERACTION_SHA='4279fbcc60946f3aede9b422b47e8e3960c779e5ffc27bb28d27ed94506949e3'
PARTS=('Clavicle','UpperArm','Forearm','HandLogical')
KEY_FRAMES=(1,5,7,9,11,12,15,19,25)
FPS=30


def load(name,path):
    spec=importlib.util.spec_from_file_location(name,str(path));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def snapshot(rig,side):
    return {part:rig.pose.bones[f'{part}_{side}'].matrix_basis.to_quaternion().normalized() for part in PARTS}


def build_waypoints(rig,side,t,motion,h):
    sign=1 if side=='L' else -1
    h.apply_pose(rig,motion.IDLE);idle=snapshot(rig,side)
    def target_pose(depth,z,clavicle,relative=False):
        h.apply_pose(rig,{**motion.IDLE,f'Clavicle_{side}':(0,0,sign*clavicle)})
        shoulder=rig.matrix_world @ rig.pose.bones[f'UpperArm_{side}'].head
        target=(shoulder.x,shoulder.y-depth,shoulder.z+z) if relative else (-.20 if side=='L' else .20,-depth,z)
        rotations=t.anchored_arm(rig,side,target);t.set_arm(rig,side,rotations)
        return snapshot(rig,side)
    guard=target_pose(.23,.66,-2)
    chamber=target_pose(.20,.66,-5)
    strike=target_pose(.378,.005,7,True)
    recoil=target_pose(.24,.66,2)
    # Proximal lead with a still-folded forearm before the snap extension.
    drive={part:chamber[part].slerp(strike[part],.68 if part in ('Clavicle','UpperArm') else .18) for part in PARTS}
    return dict(zip(KEY_FRAMES,(idle,guard,chamber,drive,strike,strike,recoil,guard,idle)))


def pose_at(frame,waypoints):
    start,end=KEY_FRAMES[0],KEY_FRAMES[-1]
    for a,b in zip(KEY_FRAMES,KEY_FRAMES[1:]):
        if a<=frame<=b:start,end=a,b;break
    u=(frame-start)/(end-start);u=u*u*(3-2*u)
    return {p:waypoints[start][p].slerp(waypoints[end][p],u) for p in PARTS}


def apply_rotations(rig,side,rotations):
    for part,q in rotations.items():
        bone=rig.pose.bones[f'{part}_{side}'];bone.rotation_mode='QUATERNION'
        bone.location=(0,0,0);bone.scale=(1,1,1);bone.rotation_quaternion=q
    bpy.context.view_layer.update()


def main():
    args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
    if len(args)!=1:raise RuntimeError('expected one new output directory')
    out=Path(args[0]).resolve()
    if out.exists():raise RuntimeError('do not overwrite prior reviews')
    assert sha(INTERACTION_PATH)==INTERACTION_SHA
    t=load('elbow_interactions',INTERACTION_PATH);motion=load('elbow_motion',t.MOTION_PATH)
    assert sha(t.MOTION_PATH)==t.MOTION_SHA
    h=load('elbow_helpers',motion.HELPER_PATH);r18=load('elbow_rig',h.R20_GENERATOR).r18
    source=ROOT/'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    assert sha(source)==h.R20_BLEND_SHA256 and sha(h.R16_BLEND)==h.R16_BLEND_SHA256
    out.mkdir(parents=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    scene=bpy.context.scene;rig=bpy.data.objects[h.ARMATURE_NAME]
    assert len(bpy.data.actions)==0
    expected={};names={};waypoint_angles={}
    for side in ('L','R'):
        waypoints=build_waypoints(rig,side,t,motion,h)
        action=bpy.data.actions.new(f'REVIEW_ElbowPunch_{side}_r01');action.use_fake_user=True
        action['hand_mask']=side;action['review_only']=True;action['gameplay_root_motion']=False
        rig.animation_data_create();rig.animation_data.action=action
        names[side]=action.name;expected[side]={}
        for frame in range(1,26):
            scene.frame_set(frame);h.apply_pose(rig,motion.IDLE)
            rotations=pose_at(frame,waypoints);apply_rotations(rig,side,rotations)
            expected[side][frame]={part:list(q) for part,q in rotations.items()}
            for part in PARTS:rig.pose.bones[f'{part}_{side}'].keyframe_insert(data_path='rotation_quaternion',frame=frame,group=f'{part}_{side}')
        # Dense samples supply authored easing; avoid Bezier overshoot between them.
        for layer in action.layers:
            for strip in layer.strips:
                for curve in strip.channelbag(action.slots[0]).fcurves:
                    for key in curve.keyframe_points:key.interpolation='LINEAR'
        rig.animation_data.action=None
    t.prepare_candidate_base_pose(h,motion,rig)
    scene.render.fps=FPS;scene.frame_start=1;scene.frame_end=24
    scene['review_scope']='Elbow-driven punch revision; 0.8 s START, no Hit authority or game Root Motion'
    rig.animation_data.action=bpy.data.actions[names['L']]
    rig.animation_data.action_slot=rig.animation_data.action.slots[0]
    scene.frame_set(1)
    candidate=out/'R20_ElbowPunchAnimationCandidate_r01.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(candidate));candidate_hash=sha(candidate)
    # All geometry and curve validation below operates on the reopened file.
    bpy.ops.wm.open_mainfile(filepath=str(candidate),load_ui=False)
    scene=bpy.context.scene;rig=bpy.data.objects[h.ARMATURE_NAME];body=bpy.data.objects[h.BODY_NAME]
    assert scene.render.fps==FPS and len(bpy.data.actions)==2
    assert len(rig.data.bones)==22 and len(rig.animation_data.drivers)==8
    for side in ('L','R'):
        action=bpy.data.actions[names[side]]
        assert all(rig.pose.bones[f'{part}_{side}'].rotation_mode=='QUATERNION' for part in PARTS)
        curves=list(action.layers[0].strips[0].channelbag(action.slots[0]).fcurves)
        assert len(curves)==16
        assert {c.data_path for c in curves}=={f'pose.bones["{part}_{side}"].rotation_quaternion' for part in PARTS}
        assert all(len(c.keyframe_points)==25 for c in curves)
    rest=r18.r12.mesh_coordinates(body.data);edges=r18.r12.mesh_edges(body.data)
    _,cameras,_=h.configure_scene(scene,r18);scene.render.resolution_x=scene.render.resolution_y=640
    for camera in cameras.values():camera.data.ortho_scale=1.5
    tree=KDTree(len(rest))
    for i,vertex in enumerate(rest):tree.insert(vertex,i)
    tree.balance();mirror=[tree.find((-p[0],p[1],p[2]))[1] for p in rest]
    records=[];max_mirror=0;start_coordinates={};end_coordinates={}
    for sample in range(49):
        frame=1+sample*.5;left=None
        for side in ('L','R'):
            rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE)
            for part in PARTS:rig.pose.bones[f'{part}_{side}'].rotation_mode='QUATERNION'
            action=bpy.data.actions[names[side]];rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
            scene.frame_set(int(frame),subframe=frame-int(frame));bpy.context.view_layer.update()
            if frame.is_integer():
                for part,q in expected[side][int(frame)].items():
                    actual=rig.pose.bones[f'{part}_{side}'].rotation_quaternion
                    assert Quaternion(q).rotation_difference(actual).angle<.0001
            mesh=r18.evaluated_mesh(body);coordinates=r18.r12.mesh_coordinates(mesh);bpy.data.meshes.remove(mesh)
            if side=='L':left=coordinates.copy();left[:,0]*=-1
            else:max_mirror=max(max_mirror,float(np.linalg.norm(left-coordinates[mirror],axis=1).max()))
            if sample==0:start_coordinates[side]=coordinates.copy()
            if sample==48:end_coordinates[side]=coordinates.copy()
            metrics=r18.mesh_pose_metrics(body,rest,edges)
            valid=(metrics['manifold']['result']=='PASS' and metrics['fold']['hardEdgeCountAt45Degrees']==0
                   and metrics['fold']['foldoverEdgeCountAt90Degrees']==0 and metrics['selfIntersection']['result']=='PASS')
            evaluated=rig.evaluated_get(bpy.context.evaluated_depsgraph_get())
            upper=evaluated.pose.bones[f'UpperArm_{side}'];forearm=evaluated.pose.bones[f'Forearm_{side}']
            elbow=math.degrees((upper.tail-upper.head).angle(forearm.tail-forearm.head))
            hand=list(evaluated.pose.bones[f'HandLogical_{side}'].tail)
            images=[]
            if frame.is_integer():
                for view in ('ThreeQuarterRight','SideLeft'):
                    p=out/f'ElbowPunch_{side}_{int(frame)-1:03d}_{view}.png';r18.render_to(scene,cameras[view],str(p))
                    images.append({'path':str(p),'view':view,'sha256':sha(p)})
            records.append({'side':side,'frame':frame,'timeSeconds':(frame-1)/FPS,'elbowAngleDegrees':elbow,
                            'handPosition':hand,'metrics':metrics,'images':images,'result':'PASS' if valid else 'FAIL'})
            print('ELBOW_PUNCH_SAMPLE',side,frame,round(elbow,2),records[-1]['result'],flush=True)
        (out/'ElbowPunchReview.json').write_text(json.dumps({'complete':False,'samples':records},indent=2)+'\n')
    closure={side:float(np.linalg.norm(end_coordinates[side]-start_coordinates[side],axis=1).max()) for side in ('L','R')}
    timing={}
    for side in ('L','R'):
        side_rows={r['frame']:r for r in records if r['side']==side}
        chamber=side_rows[7.0]['elbowAngleDegrees'];strike=side_rows[11.0]['elbowAngleDegrees'];recoil=side_rows[15.0]['elbowAngleDegrees']
        timing[side]={'chamberElbowDegrees':chamber,'strikeElbowDegrees':strike,'recoilElbowDegrees':recoil,
                      'extensionSeconds':4/FPS,'elbowExtensionExcursionDegrees':chamber-strike}
        assert chamber>100 and strike<25 and recoil>85,'elbow articulation did not meet review intent'
    assert max(closure.values())<1e-5 and max_mirror<1e-5
    assert sha(candidate)==candidate_hash and sha(source)==h.R20_BLEND_SHA256 and sha(h.R16_BLEND)==h.R16_BLEND_SHA256
    report={'complete':True,'candidatePath':str(candidate),'candidateSha256':candidate_hash,'generatorSha256':sha(__file__),
            'samples':records,'sampleCount':98,'renderCount':100,'actions':names,'framesPerSecond':FPS,'durationSeconds':.8,
            'timing':timing,'loopClosure':closure,'mirrorMaximumVertexDelta':max_mirror,
            'authoredCurveScope':'Active-side Clavicle, UpperArm, Forearm, HandLogical quaternion only; 16 curves per Action',
            'sourcePreservation':{'r16Unchanged':True,'r20Unchanged':True,'rigBoneCount':22,'helperDriverCount':8,'geometryWeightsChanged':False},
            'result':'PASS' if all(r['result']=='PASS' for r in records) else 'REVIEW_REQUIRED','userVisualApproval':False,
            'limitations':['Review timing is START; no gameplay attack cadence, Hit or damage changed.',
                           'Only animation articulation changed; previous Grab/Wall and common Firearm Grip remain separate.',
                           'Reopened Blender Action sampled at frames and half frames; no FBX/Unity import.']}
    (out/'ElbowPunchReview.json').write_text(json.dumps(report,indent=2)+'\n');print('ELBOW_PUNCH_COMPLETE',report['result'],flush=True)
    if report['result']!='PASS':raise RuntimeError('surface deformation requires review')


if __name__=='__main__':main()
