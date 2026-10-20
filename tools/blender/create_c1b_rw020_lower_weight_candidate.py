#!/usr/bin/env python3
"""Create a lower-leg weight-only candidate and regress r20 motion/approved punch.

Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <new-output-dir>
"""
import hashlib
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
import numpy as np
from mathutils import Matrix,Vector

ROOT=Path(__file__).resolve().parents[2]
LO=.04
HI=.22
DEPTH_BIAS=1.0
EDIT_Z_MAX=.175
ALLOWED=('Thigh_L','Calf_L','Thigh_R','Calf_R')


def load(name,path):
    spec=importlib.util.spec_from_file_location(name,str(path));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m


def sha(path):return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(v):
    v=np.clip(v,0,1);return v*v*v*(v*(v*6-15)+10)


def read_weights(obj):
    names=[g.name for g in obj.vertex_groups];matrix=np.zeros((len(obj.data.vertices),len(names)),dtype=np.float32)
    for vertex in obj.data.vertices:
        for entry in vertex.groups:matrix[vertex.index,entry.group]=entry.weight
    return names,matrix


def geometry_identity(obj):
    mesh=obj.data;points=np.empty(len(mesh.vertices)*3,np.float32);mesh.vertices.foreach_get('co',points)
    edges=np.empty(len(mesh.edges)*2,np.int32);mesh.edges.foreach_get('vertices',edges)
    loops=np.empty(len(mesh.loops),np.int32);mesh.loops.foreach_get('vertex_index',loops)
    return {'vertices':len(mesh.vertices),'edges':len(mesh.edges),'faces':len(mesh.polygons),
            'pointHash':hashlib.sha256(points.tobytes()).hexdigest(),'edgeHash':hashlib.sha256(edges.tobytes()).hexdigest(),
            'loopHash':hashlib.sha256(loops.tobytes()).hexdigest()}


def rig_identity(rig):
    return [{'name':b.name,'parent':b.parent.name if b.parent else None,'deform':b.use_deform,
             'head':list(b.head_local),'tail':list(b.tail_local),'matrix':[v for row in b.matrix_local for v in row]} for b in rig.data.bones]


def action_identity(action):
    return [{'path':c.data_path,'index':c.array_index,'keys':[[float(k.co.x),float(k.co.y),k.interpolation] for k in c.keyframe_points]}
            for c in action.layers[0].strips[0].channelbag(action.slots[0]).fcurves]


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    if len(args)!=1:raise RuntimeError('expected new output directory')
    out=Path(args[0]).resolve()
    if out.exists():raise RuntimeError('preserve prior candidates')
    t=load('lower_tools',ROOT/'tools/blender/review_c1b_rw020_interactions.py');motion=load('lower_motion',t.MOTION_PATH)
    h=load('lower_h',motion.HELPER_PATH);r18=load('lower_r20',h.R20_GENERATOR).r18
    source=ROOT/'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    approval=json.loads((ROOT/'artifacts/reports/R20_PUNCH_VISUAL_APPROVAL_20260905.json').read_text());punch=ROOT/approval['candidatePath']
    assert sha(source)==h.R20_BLEND_SHA256 and sha(punch)==approval['candidateSha256']
    assert sha(h.R16_BLEND)==h.R16_BLEND_SHA256
    out.mkdir(parents=True);bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    scene=bpy.context.scene;rig=bpy.data.objects[h.ARMATURE_NAME];body=bpy.data.objects[h.BODY_NAME];head=bpy.data.objects[h.HEAD_NAME]
    rest=r18.r12.mesh_coordinates(body.data);edges=r18.r12.mesh_edges(body.data)
    names,base=read_weights(body);name_index={name:i for i,name in enumerate(names)};head_names,head_weights=read_weights(head)
    geometry_before={'body':geometry_identity(body),'head':geometry_identity(head)};rig_before=rig_identity(rig)
    modifier_before=[(m.type,m.use_deform_preserve_volume) for m in body.modifiers if m.type=='ARMATURE']
    pairs,unmatched,_=r18.mirror_vertex_pairs(rest);assert unmatched==0
    with bpy.data.libraries.load(str(punch),link=False) as (src,dst):
        dst.actions=[name for name in src.actions if name.startswith('REVIEW_ElbowPunch_')]
    actions={side:bpy.data.actions[f'REVIEW_ElbowPunch_{side}_r02'] for side in ('L','R')}
    action_names={side:action.name for side,action in actions.items()}
    action_before={side:action_identity(action) for side,action in actions.items()}
    def coordinates():
        mesh=r18.evaluated_mesh(body)
        try:return r18.r12.mesh_coordinates(mesh)
        finally:bpy.data.meshes.remove(mesh)
    def set_punch(side,frame):
        rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE)
        for part in ('Clavicle','UpperArm','Forearm','HandLogical'):rig.pose.bones[f'{part}_{side}'].rotation_mode='QUATERNION'
        action=actions[side];rig.animation_data.action=action;rig.animation_data.action_slot=action.slots[0]
        scene.frame_set(int(frame),subframe=frame-int(frame));bpy.context.view_layer.update()
    baseline_punch={}
    for side in ('L','R'):
        for frame in (1,2,3,4,6,8,12,16,22):
            set_punch(side,frame);baseline_punch[(side,frame)]=coordinates()
    rig.animation_data.action=None;h.reset_pose(rig)
    x,y,z=rest.T;left=x<0
    thigh=np.where(left,base[:,name_index['Thigh_L']],base[:,name_index['Thigh_R']]).astype(np.float64)
    calf=np.where(left,base[:,name_index['Calf_L']],base[:,name_index['Calf_R']]).astype(np.float64)
    total=thigh+calf;ratio=np.divide(thigh,total,out=np.zeros(len(rest)),where=total>1e-9)
    gate=smooth((z-.025)/.025)*(1-smooth((z-.125)/.05));gate[(thigh<1e-8)|(calf<1e-8)]=0
    fraction=ratio+(smooth((z-LO-DEPTH_BIAS*y)/(HI-LO))-ratio)*gate
    for first,second in pairs:
        if gate[first]>0 and gate[second]>0:
            f=(fraction[first]+fraction[second])*.5;fraction[first]=fraction[second]=f
    proposed=base.copy()
    for side,mask in [('L',left),('R',~left)]:
        proposed[mask,name_index['Thigh_'+side]]=(total[mask]*fraction[mask]).astype(np.float32)
        proposed[mask,name_index['Calf_'+side]]=(total[mask]*(1-fraction[mask])).astype(np.float32)
    frozen=[i for i,name in enumerate(names) if name not in ALLOWED]
    assert np.array_equal(proposed[z>=EDIT_Z_MAX],base[z>=EDIT_Z_MAX])
    assert np.array_equal(proposed[:,frozen],base[:,frozen])
    changed=np.where(np.any(proposed!=base,axis=1))[0]
    assert len(changed)>0 and float(z[changed].max())<EDIT_Z_MAX
    for name in ALLOWED:
        column=name_index[name];group=body.vertex_groups[name]
        for vertex in changed:
            if proposed[vertex,column]!=base[vertex,column]:group.add([int(vertex)],float(proposed[vertex,column]),'REPLACE')
    body.data.update();bpy.context.view_layer.update()
    new_names,actual=read_weights(body);assert new_names==names and np.array_equal(actual,proposed)
    assert np.array_equal(actual[z>=EDIT_Z_MAX],base[z>=EDIT_Z_MAX]) and np.array_equal(actual[:,frozen],base[:,frozen])
    assert read_weights(head)[0]==head_names and np.array_equal(read_weights(head)[1],head_weights)
    assert geometry_identity(body)==geometry_before['body'] and geometry_identity(head)==geometry_before['head']
    assert rig_identity(rig)==rig_before
    assert [(m.type,m.use_deform_preserve_volume) for m in body.modifiers if m.type=='ARMATURE']==modifier_before
    assert int(np.max(np.count_nonzero(actual>1e-8,axis=1)))<=4
    assert float(np.max(np.abs(actual.astype(np.float64).sum(axis=1)-1)))<1e-6
    mirror_columns=[name_index[r18.mirrored_group_name(name)] for name in names]
    max_weight_mirror=max(float(np.max(np.abs(actual[a]-actual[b,mirror_columns]))) for a,b in pairs)
    assert max_weight_mirror<=1e-7
    collection,cameras,lights=h.configure_scene(scene,r18);scene.render.resolution_x=scene.render.resolution_y=720
    cameras['LowerBodyBack']=r18.create_camera(collection,'TMP_WeightBack',(0,4,.15),(0,0,.15),.48)
    h.apply_pose(rig,motion.IDLE);feet={s:rig.pose.bones[f'Foot_{s}'].matrix.copy() for s in ('L','R')}
    toe_y=sum(rig.pose.bones[f'Foot_{s}'].tail.y for s in ('L','R'))/2;ground=float(rest[:,2].min());records=[]
    def capture(id,render=False):
        m=r18.mesh_pose_metrics(body,rest,edges)
        ok=m['manifold']['result']=='PASS' and m['fold']['hardEdgeCountAt45Degrees']==0 and m['fold']['foldoverEdgeCountAt90Degrees']==0 and m['selfIntersection']['result']=='PASS'
        images=[]
        if render:
            for view in ('ThreeQuarterRight','LowerBodyBack'):
                p=out/f'{id}_{view}.png';r18.render_to(scene,cameras[view],str(p));images.append({'path':str(p),'view':view,'sha256':sha(p)})
        record={'id':id,'metrics':m,'images':images,'result':'PASS' if ok else 'FAIL'};records.append(record)
        (out/'PoseProgress.json').write_text(json.dumps({'complete':False,'samples':records},indent=2)+'\n')
        print('LOWER_WEIGHT_QA',id,record['result'],flush=True)
    for mode,values in [('FK',[40,50,60,70,80]),('PlantedIK',[.015,.025,.035])]:
        for value in values:
            rig.animation_data.action=None;h.apply_pose(rig,motion.IDLE)
            if mode=='FK':
                h.apply_pose(rig,{**motion.IDLE,'Thigh_L':(-value/2,0,0),'Thigh_R':(-value/2,0,0),'Calf_L':(value,0,0),'Calf_R':(value,0,0)})
                p=coordinates();offset=(0,toe_y-sum(rig.pose.bones[f'Foot_{s}'].tail.y for s in ('L','R'))/2,ground-float(p[:,2].min()));pelvis=rig.pose.bones['Pelvis'];pelvis.matrix=Matrix.Translation(offset)@pelvis.matrix
            else:
                pelvis=rig.pose.bones['Pelvis'];pelvis.matrix=Matrix.Translation((0,0,-value))@pelvis.matrix;bpy.context.view_layer.update()
                for side in ('L','R'):
                    a=rig.pose.bones[f'Thigh_{side}'];b=rig.pose.bones[f'Calf_{side}'];f=rig.pose.bones[f'Foot_{side}'];start=a.head.copy();target=feet[side].translation;delta=target-start;d=delta.length;direction=delta.normalized();pole=Vector((0,-1,0));bend=(pole-direction*pole.dot(direction)).normalized();pr=(a.bone.length*a.bone.length+d*d-b.bone.length*b.bone.length)/(2*d);height=math.sqrt(max(0,a.bone.length*a.bone.length-pr*pr));t.aim_bone(a,start+direction*pr+bend*height);t.aim_bone(b,target);f.matrix=feet[side];bpy.context.view_layer.update()
            capture(f'{mode}_{value}',True)
    key_pass=all(r['result']=='PASS' for r in records)
    if key_pass:
        for mode,profile in motion.CYCLES.items():
            for frame in range(profile['frames']):
                h.apply_pose(rig,motion.locomotion_pose(frame/profile['frames'],profile));capture(f'{mode}_{frame:03d}')
    punch_delta=0
    for (side,frame),reference in baseline_punch.items():
        set_punch(side,frame);delta=float(np.linalg.norm(coordinates()-reference,axis=1).max());punch_delta=max(punch_delta,delta)
        capture(f'ApprovedPunch_{side}_{frame}')
    assert punch_delta<1e-5
    rig.animation_data.action=None
    for side_mask in [('L',),('R',),('L','R')]:
        for mode in ('Grab','Wall'):
            h.apply_pose(rig,motion.IDLE)
            for side in side_mask:
                target=(-.20 if side=='L' else .20,-.20,.91) if mode=='Wall' else (-.21 if side=='L' else .21,-.34,.635)
                t.set_arm(rig,side,t.anchored_arm(rig,side,target))
            capture(mode+'_'+''.join(side_mask))
    h.reset_pose(rig)
    for bone in rig.pose.bones:
        if not bone.name.startswith('HipDeform_'):bone.rotation_mode='QUATERNION';bone.matrix_basis=Matrix.Identity(4)
    bpy.context.view_layer.update()
    neutral_delta=float(np.linalg.norm(coordinates()-rest,axis=1).max())
    assert neutral_delta<1e-5
    assert all(action_identity(actions[s])==action_before[s] for s in ('L','R'))
    for action in actions.values():action.use_fake_user=True
    r18.remove_objects([*cameras.values(),*lights])
    body['weight_revision']='lower-weight-review-r01';body['weight_scope']='Only Thigh/Calf transfer below z=0.175; all other weights preserved'
    scene['source_sha256']=h.R20_BLEND_SHA256;scene['user_visual_approval']=False
    candidate=out/'R20_LowerWeightCandidate_r01.blend';bpy.ops.wm.save_as_mainfile(filepath=str(candidate));candidate_hash=sha(candidate)
    # Reopen, so preservation checks refer to the saved candidate rather than memory.
    bpy.ops.wm.open_mainfile(filepath=str(candidate),load_ui=False)
    saved_body=bpy.data.objects[h.BODY_NAME];saved_head=bpy.data.objects[h.HEAD_NAME];saved_rig=bpy.data.objects[h.ARMATURE_NAME]
    assert geometry_identity(saved_body)==geometry_before['body'] and geometry_identity(saved_head)==geometry_before['head']
    assert rig_identity(saved_rig)==rig_before and np.array_equal(read_weights(saved_body)[1],actual)
    assert len(saved_rig.animation_data.drivers)==8 and len(saved_rig.data.bones)==22
    assert all(action_identity(bpy.data.actions[action_names[s]])==action_before[s] for s in ('L','R'))
    assert sha(source)==h.R20_BLEND_SHA256 and sha(punch)==approval['candidateSha256'] and sha(h.R16_BLEND)==h.R16_BLEND_SHA256
    report={'complete':True,'candidatePath':str(candidate),'candidateSha256':candidate_hash,'sourceSha256':h.R20_BLEND_SHA256,'generatorSha256':sha(__file__),
            'parameters':{'lo':LO,'hi':HI,'depthBias':DEPTH_BIAS,'fullBlendZ': [.05,.125],'editZMax':EDIT_Z_MAX},
            'changedVertices':int(len(changed)),'changedGroups':list(ALLOWED),'maxWeightMirrorDeviation':max_weight_mirror,'maximumInfluences':int(np.max(np.count_nonzero(actual>1e-8,axis=1))),
            'geometryUnchanged':True,'bindSkeletonUnchanged':True,'upperWeightsUnchanged':True,'otherWeightGroupsUnchanged':True,'headUnchanged':True,
            'approvedPunchCurvesUnchanged':True,'approvedPunchPoseMaxVertexDelta':punch_delta,'neutralPoseMaxVertexDelta':neutral_delta,'sourceFilesUnchanged':True,
            'samples':records,'result':'PASS' if key_pass and all(r['result']=='PASS' for r in records) else 'REVIEW_REQUIRED','userVisualApproval':False,
            'limitations':['Weight-only local candidate; no geometry, bones, modifiers, or approved punch curves changed.',
                           'Pose samples are not a universal joint-limit or Unity physics proof.','No canonical replacement, Unity import, Build, commit or push.']}
    (out/'LowerWeightReview.json').write_text(json.dumps(report,indent=2)+'\n');print('LOWER_WEIGHT_CANDIDATE',report['result'],flush=True)


if __name__=='__main__':main()
