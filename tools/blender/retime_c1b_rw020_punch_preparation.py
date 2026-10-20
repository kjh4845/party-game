#!/usr/bin/env python3
"""Halve only the elbow-punch preparation; translate all later keys unchanged.

Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <r01-candidate.blend> <new-output-dir>
"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np

ROOT=Path(__file__).resolve().parents[2]
OLD_CHAMBER=7.0
NEW_CHAMBER=4.0
FPS=30


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def remap(frame):
    return 1+(frame-1)*.5 if frame<=OLD_CHAMBER else frame-3


def curves(action):return list(action.layers[0].strips[0].channelbag(action.slots[0]).fcurves)


def mesh_identity():
    result={}
    for obj in bpy.data.objects:
        if obj.type!='MESH':continue
        points=np.empty(len(obj.data.vertices)*3,dtype=np.float32);obj.data.vertices.foreach_get('co',points)
        edges=np.empty(len(obj.data.edges)*2,dtype=np.int32);obj.data.edges.foreach_get('vertices',edges)
        result[obj.name]={'vertices':len(obj.data.vertices),'polygons':len(obj.data.polygons),
                          'positionsSha256':hashlib.sha256(points.tobytes()).hexdigest(),
                          'edgesSha256':hashlib.sha256(edges.tobytes()).hexdigest()}
    return result


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    if len(args)!=2:raise RuntimeError('expected previous candidate and new output directory')
    source,out=[Path(v).resolve() for v in args]
    if out.exists():raise RuntimeError('preserve existing candidates')
    baseline=json.loads((ROOT/'artifacts/reports/R20_ELBOW_PUNCH_REVIEW_20260905.json').read_text())
    assert sha(source)==baseline['candidateSha256']
    spec=importlib.util.spec_from_file_location('retime_original',ROOT/'tools/blender/review_c1b_rw020_elbow_punch.py')
    original=importlib.util.module_from_spec(spec);spec.loader.exec_module(original)
    t=original.load('retime_tools',original.INTERACTION_PATH);motion=original.load('retime_motion',t.MOTION_PATH)
    h=original.load('retime_h',motion.HELPER_PATH);r18=original.load('retime_rig',h.R20_GENERATOR).r18
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    scene=bpy.context.scene;rig=bpy.data.objects[h.ARMATURE_NAME]
    source_hash=sha(source);mesh_before=mesh_identity();reference={};names={}
    for side in ('L','R'):
        action=bpy.data.actions[f'REVIEW_ElbowPunch_{side}_r01']
        fc=curves(action);assert len(fc)==16
        reference[side]={}
        for curve in fc:
            assert len(curve.keyframe_points)==25 and all(k.interpolation=='LINEAR' for k in curve.keyframe_points)
            key=curve.data_path+'#'+str(curve.array_index)
            reference[side][key]={'samples':[float(curve.evaluate(1+i*.5)) for i in range(49)],
                                  'values':[float(k.co.y) for k in curve.keyframe_points],
                                  'frames':[float(k.co.x) for k in curve.keyframe_points]}
            for point in curve.keyframe_points:
                point.co.x=remap(point.co.x)
                point.handle_left.x=remap(point.handle_left.x)
                point.handle_right.x=remap(point.handle_right.x)
            curve.update()
        action.name=f'REVIEW_ElbowPunch_{side}_r02';names[side]=action.name
    scene.render.fps=FPS;scene.frame_start=1;scene.frame_end=21
    scene['review_scope']='Preparation 0.2 to 0.1 s; later pose curves shifted by 0.1 s with unchanged speed'
    scene.frame_set(1)
    out.mkdir(parents=True);candidate=out/'R20_ElbowPunchAnimationCandidate_r02.blend'
    bpy.ops.wm.save_as_mainfile(filepath=str(candidate));candidate_hash=sha(candidate)
    bpy.ops.wm.open_mainfile(filepath=str(candidate),load_ui=False)
    rig=bpy.data.objects[h.ARMATURE_NAME];body=bpy.data.objects[h.BODY_NAME]
    assert mesh_identity()==mesh_before
    assert len(rig.data.bones)==22 and len(rig.animation_data.drivers)==8 and len(bpy.data.actions)==2
    max_curve_delta=0;max_pose_delta=0;records=[];geometry=[]
    rest=r18.r12.mesh_coordinates(body.data);edges=r18.r12.mesh_edges(body.data)
    geometry_old_frames={1,3,5,7,7.5,11,15,25}
    for side in ('L','R'):
        action=bpy.data.actions[names[side]];fc=curves(action)
        for curve in fc:
            ref=reference[side][curve.data_path+'#'+str(curve.array_index)]
            assert [float(k.co.y) for k in curve.keyframe_points]==ref['values']
            assert [float(k.co.x) for k in curve.keyframe_points]==[remap(f) for f in ref['frames']]
            assert all(k.interpolation=='LINEAR' for k in curve.keyframe_points)
        for i in range(49):
            old=1+i*.5;new=remap(old)
            rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE)
            for part in original.PARTS:rig.pose.bones[f'{part}_{side}'].rotation_mode='QUATERNION'
            rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
            bpy.context.scene.frame_set(int(new),subframe=new-int(new));bpy.context.view_layer.update()
            for curve in fc:
                ref=reference[side][curve.data_path+'#'+str(curve.array_index)]['samples'][i]
                delta=abs(float(curve.evaluate(new))-ref);max_curve_delta=max(max_curve_delta,delta)
                actual=rig.path_resolve(curve.data_path)[curve.array_index]
                max_pose_delta=max(max_pose_delta,abs(float(actual)-ref))
            records.append({'side':side,'oldFrame':old,'newFrame':new})
            if old in geometry_old_frames:
                m=r18.mesh_pose_metrics(body,rest,edges)
                ok=m['manifold']['result']=='PASS' and m['fold']['hardEdgeCountAt45Degrees']==0 and m['fold']['foldoverEdgeCountAt90Degrees']==0 and m['selfIntersection']['result']=='PASS'
                geometry.append({'side':side,'newFrame':new,'metrics':m,'result':'PASS' if ok else 'FAIL'})
                print('RETIME_GEOMETRY',side,new,geometry[-1]['result'],flush=True)
    assert max_curve_delta<1e-6 and max_pose_delta<1e-6
    assert sha(candidate)==candidate_hash and sha(source)==source_hash
    assert sha(h.R16_BLEND)==h.R16_BLEND_SHA256
    r20=ROOT/'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    assert sha(r20)==h.R20_BLEND_SHA256
    report={'complete':True,'candidatePath':str(candidate),'candidateSha256':candidate_hash,'sourceCandidateSha256':source_hash,
            'generatorSha256':sha(__file__),'preparationBeforeSeconds':.2,'preparationAfterSeconds':.1,
            'durationBeforeSeconds':.8,'durationAfterSeconds':.7,'framesPerSecond':FPS,
            'laterTimingChange':'all keys after chamber shifted by -3 frames; durations and values unchanged',
            'extensionSeconds':4/FPS,'poseValuesUnchanged':True,'meshUnchanged':True,
            'curveEquivalenceSamples':len(records),'maxCurveValueDelta':max_curve_delta,'maxReplayedQuaternionDelta':max_pose_delta,
            'retimeSamples':records,'geometryChecks':geometry,'sourcePreservation':{'previousCandidateUnchanged':True,'r16Unchanged':True,'r20Unchanged':True},
            'result':'PASS' if all(g['result']=='PASS' for g in geometry) else 'FAIL','userVisualApproval':False,
            'limitations':['Only animation preparation timing changed; gameplay attack cadence/Hit not changed.',
                           'Pose comparison reuses the validated source curve at exactly mapped times. Fresh surface QA covers preparation and join/representative later samples.',
                           'Blender candidate only; no FBX/Unity import.']}
    (out/'PreparationRetimeReview.json').write_text(json.dumps(report,indent=2)+'\n');print('PREPARATION_RETIME',report['result'],flush=True)
    if report['result']!='PASS':raise RuntimeError('retime deformation failed')


if __name__=='__main__':main()
