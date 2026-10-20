#!/usr/bin/env python3
"""Transfer lateral trunk arm weights to existing torso influences; preserve sources."""
import hashlib
import json
from pathlib import Path
import struct
import sys

import bpy
import numpy as np
from mathutils import Matrix

ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/blender'))
import create_c1b_rw020_lower_weight_candidate as w

def main():
    args=sys.argv[sys.argv.index('--')+1:];out=Path(args[0]).resolve();strength=float(args[1]) if len(args)>1 else .92
    if out.exists():raise RuntimeError('Preserve prior candidates')
    approval=json.loads((ROOT/'artifacts/reports/R20_LOWER_WEIGHT_VISUAL_APPROVAL_20260905.json').read_text())
    source=ROOT/approval['candidatePath'];assert w.sha(source)==approval['candidateSha256']
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False);out.mkdir(parents=True)
    t=w.load('lat_interactions',ROOT/'tools/blender/review_c1b_rw020_interactions.py');motion=w.load('lat_motion',t.MOTION_PATH)
    h=w.load('lat_helpers',motion.HELPER_PATH);r18=w.load('lat_r20',h.R20_GENERATOR).r18
    rig=bpy.data.objects[h.ARMATURE_NAME];body=bpy.data.objects[h.BODY_NAME];head=bpy.data.objects[h.HEAD_NAME];scene=bpy.context.scene
    rest=r18.r12.mesh_coordinates(body.data);names,base=w.read_weights(body);index={name:i for i,name in enumerate(names)}
    identity={'body':w.geometry_identity(body),'head':w.geometry_identity(head),'rig':w.rig_identity(rig)};head_weights=w.read_weights(head)[1]
    actions={s:bpy.data.actions[f'REVIEW_ElbowPunch_{s}_r02'] for s in ('L','R')};action_ids={s:w.action_identity(a) for s,a in actions.items()}
    def coordinates():
        mesh=r18.evaluated_mesh(body)
        try:return r18.r12.mesh_coordinates(mesh)
        finally:bpy.data.meshes.remove(mesh)
    def punch(side,frame):
        rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE)
        for part in ('Clavicle','UpperArm','Forearm','HandLogical'):rig.pose.bones[f'{part}_{side}'].rotation_mode='QUATERNION'
        rig.animation_data.action=actions[side];rig.animation_data.action_slot=actions[side].slots[0]
        scene.frame_set(int(frame),subframe=frame-int(frame));bpy.context.view_layer.update()
    rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE);idle_before=coordinates()
    x,y,z=idle_before.T
    regions={'side_back':(x<-.075)&(x>-.18)&(z>.35)&(z<.60)&(y>.025),
             'upper_armpit_back':(x<-.065)&(x>-.20)&(z>.52)&(z<.68)&(y>.015)}
    torso=base[:,index['Spine']]+base[:,index['Chest']]
    for key in regions:regions[key]&=torso>=.5
    baseline={}
    for frame in (4,6,8,9):punch('L',frame);baseline[frame]=coordinates()
    rig.animation_data.action=None;h.reset_pose(rig)
    x,y,z=rest.T;absx=np.abs(x)
    gate=(w.smooth((z-.28)/.10)*(1-w.smooth((z-.48)/.16))
          *w.smooth((absx-.04)/.04)*(1-w.smooth((absx-.12)/.07))
          *w.smooth((torso-.20)/.40)*(.80+.20*w.smooth((y+.05)/.10)))
    proposed=base.copy();transferred=np.zeros(len(rest),np.float64)
    allowed=['Spine','Chest','Clavicle_L','Clavicle_R','UpperArm_L','UpperArm_R']
    for name in allowed[2:]:
        col=index[name];removed=base[:,col]*gate*strength;proposed[:,col]-=removed.astype(np.float32);transferred+=removed
    # Deposit only into existing Spine/Chest slots: no fifth skin influence.
    for name in ('Spine','Chest'):
        col=index[name];fraction=np.divide(base[:,col],torso,out=np.zeros(len(rest)),where=torso>1e-8)
        proposed[:,col]+=(transferred*fraction).astype(np.float32)
    changed=np.flatnonzero(np.any(proposed!=base,axis=1));assert len(changed)>0
    frozen=[i for i,name in enumerate(names) if name not in allowed]
    assert np.array_equal(base[:,frozen],proposed[:,frozen])
    assert np.array_equal(base[z<.28],proposed[z<.28])
    assert np.max(np.count_nonzero(proposed>1e-8,axis=1))<=4
    assert np.max(np.abs(proposed.astype(np.float64).sum(axis=1)-1))<1e-6
    pairs,unmatched,_=r18.mirror_vertex_pairs(rest);assert unmatched==0
    mirrored=[index[r18.mirrored_group_name(name)] for name in names]
    symmetry=max(float(np.max(np.abs(proposed[a]-proposed[b,mirrored]))) for a,b in pairs);assert symmetry<1e-6
    for name in allowed:
        col=index[name];group=body.vertex_groups[name]
        for vertex in changed:
            if proposed[vertex,col]!=base[vertex,col]:group.add([int(vertex)],float(proposed[vertex,col]),'REPLACE')
    body.data.update();bpy.context.view_layer.update();assert np.array_equal(w.read_weights(body)[1],proposed)
    rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE);idle_after=coordinates();comparisons={}
    for frame in baseline:punch('L',frame);comparisons[frame]=coordinates()
    measurements={}
    for key,mask in regions.items():
        before=np.max(np.stack([np.linalg.norm(v-idle_before,axis=1) for v in baseline.values()]),axis=0)[mask]
        after=np.max(np.stack([np.linalg.norm(v-idle_after,axis=1) for v in comparisons.values()]),axis=0)[mask]
        measurements[key]={'vertices':int(mask.sum()),'beforeMean':float(before.mean()),'afterMean':float(after.mean()),'beforeMax':float(before.max()),'afterMax':float(after.max())}
    samples=[]
    def check(name,overlap=False):
        mesh=r18.evaluated_mesh(body)
        try:
            folds=r18.qa.folds(mesh);folds.pop('foldoverEdgesAt90Degrees',None)
            intersection=r18.qa.bvh_self_overlap(mesh) if overlap else None
            if intersection:intersection.pop('nonAdjacentOverlapPairs',None)
        finally:bpy.data.meshes.remove(mesh)
        ok=folds['hardEdgeCountAt45Degrees']==0 and folds['foldoverEdgeCountAt90Degrees']==0 and (intersection is None or intersection['result']=='PASS')
        samples.append({'name':name,'fold':folds,'selfIntersection':intersection,'result':'PASS' if ok else 'FAIL'})
        (out/'Progress.json').write_text(json.dumps({'measurements':measurements,'samples':samples},indent=2)+'\n')
        print('LAT_QA',name,'PASS' if ok else 'FAIL',flush=True)
    for side in ('L','R'):
        for frame in (1,4,6,8,12,16,22):punch(side,frame);check(f'Punch_{side}_{frame}',frame in (4,8))
    rig.animation_data.action=None
    for mode,profile in motion.CYCLES.items():
        for phase in (0,.25,.5,.75):h.apply_pose(rig,motion.locomotion_pose(phase,profile));check(f'{mode}_{phase}')
    for sides in [('L',),('R',),('L','R')]:
        for mode in ('Grab','Wall'):
            h.apply_pose(rig,motion.IDLE)
            for side in sides:
                target=(-.20 if side=='L' else .20,-.20,.91) if mode=='Wall' else (-.21 if side=='L' else .21,-.34,.635)
                t.set_arm(rig,side,t.anchored_arm(rig,side,target))
            check(mode+'_'+''.join(sides),len(sides)==2)
    h.reset_pose(rig)
    for bone in rig.pose.bones:
        if not bone.name.startswith('HipDeform_'):bone.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    assert w.geometry_identity(body)==identity['body'] and w.geometry_identity(head)==identity['head'] and w.rig_identity(rig)==identity['rig']
    assert np.array_equal(w.read_weights(head)[1],head_weights)
    assert all(w.action_identity(actions[s])==action_ids[s] for s in actions)
    candidate=out/'R20_LatWeightCandidate.blend';body['weight_revision']='lat-weight-candidate';body['weight_scope']='Lateral trunk shoulder/upper-arm weights transferred to existing Spine/Chest; lower-body and head weights preserved'
    bpy.ops.wm.save_as_mainfile(filepath=str(candidate));candidate_hash=w.sha(candidate)
    bpy.ops.wm.open_mainfile(filepath=str(candidate),load_ui=False)
    assert np.array_equal(w.read_weights(bpy.data.objects[h.BODY_NAME])[1],proposed)
    assert w.geometry_identity(bpy.data.objects[h.BODY_NAME])==identity['body'] and w.rig_identity(bpy.data.objects[h.ARMATURE_NAME])==identity['rig']
    # Reuse the byte-identical approved geometry/bind transport, replacing only
    # body influence indices/weights after saved-candidate parity is checked.
    previous=ROOT/'.codex_tmp/unity-physics-jump-20260905/ApprovedCharacter.bytes'
    with previous.open('rb') as stream:
        header=json.loads(stream.read(struct.unpack('<i',stream.read(4))[0]));blocks=[]
        bone_index={b['name']:i for i,b in enumerate(header['bones'])}
        for mesh in header['meshes']:
            count=mesh['vertices'];positions=stream.read(count*12);normals=stream.read(count*12);ids=stream.read(count*16);weights=stream.read(count*16);tris=stream.read(mesh['triangleIndices']*4)
            if mesh['name']==h.BODY_NAME:
                assert count==len(proposed);new_ids=np.frombuffer(ids,dtype='<i4').reshape(count,4).copy();new_weights=np.frombuffer(weights,dtype='<f4').reshape(count,4).copy()
                for vertex in changed:
                    row=proposed[vertex];new_ids[vertex]=0;new_weights[vertex]=0
                    slots=np.flatnonzero(row>0);assert len(slots)<=4
                    for slot,column in enumerate(slots):new_ids[vertex,slot]=bone_index[names[column]];new_weights[vertex,slot]=row[column]
                ids=new_ids.tobytes();weights=new_weights.tobytes()
            blocks.extend((positions,normals,ids,weights,tris))
        assert not stream.read(1)
    header['sourceSha256']=candidate_hash;encoded=json.dumps(header).encode();transport=out/'LatCharacter.bytes'
    with transport.open('wb') as stream:
        stream.write(struct.pack('<i',len(encoded)));stream.write(encoded)
        for block in blocks:stream.write(block)
    assert w.sha(source)==approval['candidateSha256']
    report={'status':'PASS' if all(s['result']=='PASS' for s in samples) else 'REVIEW_REQUIRED','candidate':str(candidate),'candidateSha256':candidate_hash,'transport':str(transport),'transportSha256':w.sha(transport),'source':str(source),'sourceSha256':approval['candidateSha256'],'strength':strength,'changedVertices':len(changed),'changedGroups':allowed,'maximumWeightSymmetryError':symmetry,'maximumInfluences':int(np.max(np.count_nonzero(proposed>0,axis=1))),'geometryAndRigUnchanged':True,'headAndLowerWeightsUnchanged':True,'punchActionsUnchanged':True,'measurements':measurements,'samples':samples,'userVisualApproval':False}
    (out/'LatWeightReview.json').write_text(json.dumps(report,indent=2)+'\n');print('LAT_RESULT',report['status'],json.dumps(measurements),flush=True)

if __name__=='__main__':main()
