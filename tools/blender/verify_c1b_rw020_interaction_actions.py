#!/usr/bin/env python3
"""Reopen a saved interaction candidate; verify masks, samples and half frames."""
import hashlib
import importlib.util
import json
from pathlib import Path
import sys

import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[2]


def load(name,path):
    spec=importlib.util.spec_from_file_location(name,str(path));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    if len(args)!=2:raise RuntimeError('expected candidate blend and new report path')
    candidate,output=map(lambda p:Path(p).resolve(),args)
    if output.exists():raise RuntimeError('preserve previous report')
    before=sha(candidate)
    tools=load('verify_interaction_tools',ROOT/'tools/blender/review_c1b_rw020_interactions.py')
    motion=load('verify_interaction_motion',tools.MOTION_PATH)
    h=load('verify_interaction_h',motion.HELPER_PATH)
    r18=load('verify_interaction_r20',h.R20_GENERATOR).r18
    bpy.ops.wm.open_mainfile(filepath=str(candidate),load_ui=False)
    rig=bpy.data.objects[h.ARMATURE_NAME];body=bpy.data.objects[h.BODY_NAME]
    for side in ('L','R'):
        for part in tools.ARM_PARTS:
            assert rig.pose.bones[f'{part}_{side}'].rotation_mode == 'QUATERNION', 'saved Action must play without changing rotation mode'
    assert bpy.context.scene.get('review_base_pose'), 'saved hand-only clips need their unkeyed base pose'
    saved_base = {bone.name: bone.matrix_basis.to_quaternion() for bone in rig.pose.bones
                  if bone.name.startswith(('UpperArm_', 'Forearm_', 'HandLogical_'))}
    h.apply_pose(rig, motion.IDLE)
    assert all(saved_base[name].rotation_difference(rig.pose.bones[name].matrix_basis.to_quaternion()).angle < .0001
               for name in saved_base), 'unkeyed base pose differs from review Idle'
    rest=r18.r12.mesh_coordinates(body.data);edges=r18.r12.mesh_edges(body.data)
    assert len(rig.data.bones)==22 and len(rig.animation_data.drivers)==8
    assert bpy.context.scene.render.fps==24
    actions=list(bpy.data.actions);assert sorted(a.name for a in actions)==['REVIEW_Punch_L_r01','REVIEW_Punch_R_r01']
    records=[]
    for side in ('L','R'):
        action=bpy.data.actions[f'REVIEW_Punch_{side}_r01']
        assert len(action.slots)==1 and len(action.layers)==1
        bag=action.layers[0].strips[0].channelbag(action.slots[0]);curves=list(bag.fcurves)
        expected={f'pose.bones["{part}_{side}"].rotation_quaternion' for part in tools.ARM_PARTS}
        assert len(curves)==12 and {c.data_path for c in curves}==expected
        assert all(len(c.keyframe_points)==25 for c in curves)
        assert all([round(k.co.x,6) for k in c.keyframe_points]==list(range(1,26)) for c in curves)
        rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE);idle=tools.arm_rotations(rig,side)
        h.apply_pose(rig,{**motion.IDLE,f'UpperArm_{side}':(-45,0,35 if side=='L' else -35),f'Forearm_{side}':(0,0,55 if side=='L' else -55)})
        ready=tools.arm_rotations(rig,side)
        h.apply_pose(rig,motion.IDLE);motion.apply_straight_arm_grip(rig,side);extended=tools.arm_rotations(rig,side)
        samples=[];first=None;last=None
        for index in range(49):
            frame=1+index*.5;t=(frame-1)/24
            rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE)
            if t<.25:tools.blend_arm(rig,side,idle,ready,t/.25)
            elif t<.45:tools.blend_arm(rig,side,ready,extended,(t-.25)/.20)
            elif t<.55:tools.set_arm(rig,side,extended)
            else:tools.blend_arm(rig,side,extended,idle,(t-.55)/.45)
            mesh=r18.evaluated_mesh(body);expected_coordinates=r18.r12.mesh_coordinates(mesh);bpy.data.meshes.remove(mesh)
            h.apply_pose(rig,motion.IDLE)
            for part in tools.ARM_PARTS:rig.pose.bones[f'{part}_{side}'].rotation_mode='QUATERNION'
            rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
            bpy.context.scene.frame_set(int(frame),subframe=frame-int(frame));bpy.context.view_layer.update()
            mesh=r18.evaluated_mesh(body);actual=r18.r12.mesh_coordinates(mesh);bpy.data.meshes.remove(mesh)
            delta=float(np.linalg.norm(actual-expected_coordinates,axis=1).max())
            if index%2==0:assert delta<.00001,('keyframe replay mismatch',side,frame,delta)
            metrics=r18.mesh_pose_metrics(body,rest,edges)
            valid=metrics['manifold']['result']=='PASS' and metrics['fold']['hardEdgeCountAt45Degrees']==0 and metrics['fold']['foldoverEdgeCountAt90Degrees']==0 and metrics['selfIntersection']['result']=='PASS'
            samples.append({'frame':frame,'authoredKeyframe':index%2==0,'proceduralComparisonDelta':delta,'metrics':metrics,'result':'PASS' if valid else 'FAIL'})
            if index==0:first=actual.copy()
            if index==48:last=actual.copy()
            print('SAVED_ACTION_SAMPLE',side,frame,samples[-1]['result'],flush=True)
        closure=float(np.linalg.norm(last-first,axis=1).max());assert closure<.00001
        records.append({'action':action.name,'handMask':side,'curves':len(curves),'keyframesPerCurve':25,'rootMotionCurves':0,'oppositeHandCurves':0,'loopDelta':closure,'samples':samples})
    rig.animation_data.action=None;h.reset_pose(rig)
    assert before==sha(candidate)
    report={'candidate':str(candidate),'candidateSha256':before,'sourceSaved':False,'framesPerSecond':24,
            'storedQuaternionModeVerified':True,'storedIdleBasePoseVerified':True,
            'actionCount':2,'sampleCount':98,'actions':records,'result':'PASS' if all(s['result']=='PASS' for a in records for s in a['samples']) else 'FAIL',
            'limitations':['Blender Action replay only; no FBX/Unity clip import, contact/hit authority or visual approval.']}
    output.write_text(json.dumps(report,indent=2)+'\n');print('SAVED_ACTION_VERIFICATION',report['result'],flush=True)
    if report['result']!='PASS':raise RuntimeError('saved action deformation failed')


if __name__=='__main__':main()
