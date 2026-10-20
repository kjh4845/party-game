#!/usr/bin/env python3
"""Read-only crouch deformation audit; findings do not modify the r20 rig.

Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <new-output-dir>
"""
import importlib.util
import json
import math
from pathlib import Path
import sys

import bpy
from mathutils import Matrix,Vector

ROOT=Path(__file__).resolve().parents[2]


def load(name,path):
    spec=importlib.util.spec_from_file_location(name,str(path));m=importlib.util.module_from_spec(spec);spec.loader.exec_module(m);return m


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    if len(args)!=1:raise RuntimeError('expected new output directory')
    out=Path(args[0]).resolve()
    if out.exists():raise RuntimeError('preserve previous audit output')
    t=load('crouch_tools',ROOT/'tools/blender/review_c1b_rw020_interactions.py')
    motion=load('crouch_motion',t.MOTION_PATH);h=load('crouch_h',motion.HELPER_PATH)
    assert h.sha256(t.MOTION_PATH)==t.MOTION_SHA
    r18=load('crouch_rig',h.R20_GENERATOR).r18
    source=ROOT/'BlenderSource/Characters/C1B-RW-020-helper-rig-preview/CHR_MasterCharacter_C1B_Rig_r20.blend'
    assert h.sha256(source)==h.R20_BLEND_SHA256
    out.mkdir(parents=True)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    scene=bpy.context.scene;rig=bpy.data.objects[h.ARMATURE_NAME];body=bpy.data.objects[h.BODY_NAME]
    rest=r18.r12.mesh_coordinates(body.data);edges=r18.r12.mesh_edges(body.data);ground=float(rest[:,2].min())
    h.apply_pose(rig,motion.IDLE)
    feet={side:rig.pose.bones[f'Foot_{side}'].matrix.copy() for side in ('L','R')}
    toe_y=sum(rig.pose.bones[f'Foot_{side}'].tail.y for side in ('L','R'))/2
    collection,cameras,_=h.configure_scene(scene,r18);scene.render.resolution_x=scene.render.resolution_y=900
    cameras['LowerBodyBack']=r18.create_camera(collection,'TMP_CrouchBack',(0,4,.15),(0,0,.15),.48)
    records=[]
    for method,values in [('FK', [40,50,60,70,80]),('PlantedIK',[.015,.025,.035])]:
        for value in values:
            h.apply_pose(rig,motion.IDLE)
            checks={};offset=(0,0,0)
            if method=='FK':
                h.apply_pose(rig,{**motion.IDLE,'Thigh_L':(-value/2,0,0),'Thigh_R':(-value/2,0,0),'Calf_L':(value,0,0),'Calf_R':(value,0,0)})
                mesh=r18.evaluated_mesh(body);points=r18.r12.mesh_coordinates(mesh);bpy.data.meshes.remove(mesh)
                offset=(0,toe_y-sum(rig.pose.bones[f'Foot_{s}'].tail.y for s in ('L','R'))/2,ground-float(points[:,2].min()))
                pelvis=rig.pose.bones['Pelvis'];pelvis.matrix=Matrix.Translation(offset)@pelvis.matrix
            else:
                offset=(0,0,-value);pelvis=rig.pose.bones['Pelvis'];pelvis.matrix=Matrix.Translation(offset)@pelvis.matrix;bpy.context.view_layer.update()
                for side in ('L','R'):
                    thigh=rig.pose.bones[f'Thigh_{side}'];calf=rig.pose.bones[f'Calf_{side}'];foot=rig.pose.bones[f'Foot_{side}']
                    start=thigh.head.copy();target=feet[side].translation;delta=target-start;distance=delta.length;a=thigh.bone.length;b=calf.bone.length
                    assert abs(a-b)<distance<a+b,'unreachable foot target'
                    direction=delta.normalized();pole=Vector((0,-1,0));bend=(pole-direction*pole.dot(direction)).normalized()
                    projection=(a*a+distance*distance-b*b)/(2*distance);height=math.sqrt(max(0,a*a-projection*projection))
                    t.aim_bone(thigh,start+direction*projection+bend*height);t.aim_bone(calf,target);foot.matrix=feet[side];bpy.context.view_layer.update()
                    checks[side]={'targetErrorMeters':(foot.head-target).length,'boneScaleMaxDelta':max(abs(v-1) for bone in (thigh,calf,foot) for v in bone.scale)}
            bpy.context.view_layer.update()
            metrics=r18.mesh_pose_metrics(body,rest,edges)
            valid=metrics['manifold']['result']=='PASS' and metrics['fold']['hardEdgeCountAt45Degrees']==0 and metrics['fold']['foldoverEdgeCountAt90Degrees']==0 and metrics['selfIntersection']['result']=='PASS'
            mesh=r18.evaluated_mesh(body);folds=r18.qa.folds(mesh);points=r18.r12.mesh_coordinates(mesh)
            locations=[list((mesh.vertices[e[0][0]].co+mesh.vertices[e[0][1]].co)/2) for e in folds['foldoverEdgesAt90Degrees']]
            ground_delta=float(points[:,2].min()-ground);bpy.data.meshes.remove(mesh)
            images=[]
            id=f'{method}_{value}'
            for view in ('ThreeQuarterRight','LowerBodyBack'):
                path=out/f'{id}_{view}.png';r18.render_to(scene,cameras[view],str(path));images.append({'view':view,'path':str(path),'sha256':h.sha256(path)})
            record={'id':id,'method':method,'inputValue':value,'inputUnit':'Calf local X degrees' if method=='FK' else 'pelvis drop meters',
                    'pelvisOffset':offset,'anchorChecks':checks,'groundDeltaMeters':ground_delta,'metrics':metrics,'foldLocationsWorld':locations,
                    'images':images,'result':'PASS' if valid else 'FAIL'}
            records.append(record);print('CROUCH_AUDIT',id,record['result'],flush=True)
    assert h.sha256(source)==h.R20_BLEND_SHA256 and h.sha256(h.R16_BLEND)==h.R16_BLEND_SHA256
    report={'complete':True,'sourcePath':str(source),'sourceSha256':h.R20_BLEND_SHA256,'generatorSha256':h.sha256(__file__),
            'samples':records,'result':'REVIEW_REQUIRED' if any(r['result']=='FAIL' for r in records) else 'PASS',
            'sourceSaved':False,'geometryOrWeightsChanged':False,'actionsCreated':0,
            'limitations':['Static deformation probes, not final jump/contact physics or a universal joint limit.',
                           'FK and IK both reproduce lower-leg failure at greater flexion; the exact weight correction remains untested.',
                           'Changes to weights, bind skeleton or geometry require the requested user decision.']}
    (out/'CrouchAudit.json').write_text(json.dumps(report,indent=2)+'\n');print('CROUCH_AUDIT_COMPLETE',report['result'],flush=True)


if __name__=='__main__':main()
