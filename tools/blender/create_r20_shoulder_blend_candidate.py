#!/usr/bin/env python3
"""Build an isolated shoulder skin candidate; keep approved geometry and bones intact.
Blender --background --factory-startup --python-exit-code 1 --python this.py
-- <weights.npz> <new-output-directory>
"""
import hashlib,json,struct,sys
from pathlib import Path
import bpy
import numpy as np
from mathutils import Matrix
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/blender'))
import create_c1b_rw020_lower_weight_candidate as common

def main():
 args=sys.argv[sys.argv.index('--')+1:];weights_path=Path(args[0]).resolve();out=Path(args[1]).resolve()
 if out.exists():raise RuntimeError('Preserve prior candidate directories')
 source=ROOT/'.codex_tmp/lat-weight-20260909/candidate-r07/R20_LatWeightCandidate.blend';source_hash=common.sha(source)
 transport=ROOT/'.codex_tmp/lat-weight-20260909/Selected/LatCharacter.bytes';data=transport.read_bytes();size=struct.unpack_from('<i',data)[0];header=json.loads(data[4:4+size]);assert header['sourceSha256']==source_hash
 candidate=np.load(weights_path);weights=candidate['weights'];names=candidate['names'].tolist();assert names==[b['name'] for b in header['bones']]+['ShoulderBlend_L','ShoulderBlend_R']
 assert np.count_nonzero(weights>0,axis=1).max()<=4 and np.max(abs(weights.sum(1)-1))<1e-6
 bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False);rig=bpy.data.objects['RIG_C1B_R20_Armature'];body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];head=bpy.data.objects['CHR_C1B_R20_SkinnedHead']
 assert len(body.data.vertices)==len(weights)
 geometry={'body':common.geometry_identity(body),'head':common.geometry_identity(head)};bones_before=common.rig_identity(rig);head_weights=common.read_weights(head)
 old_names,old_weights=common.read_weights(body);old=np.zeros_like(weights)
 for col,name in enumerate(old_names):old[:,names.index(name)]=old_weights[:,col]
 changed=np.flatnonzero(np.max(abs(old-weights),axis=1)>0);assert len(changed)>0
 # Geometry, head weights, and lower weights remain byte-identical.
 rest=np.empty(len(body.data.vertices)*3,np.float32);body.data.vertices.foreach_get('co',rest);rest=rest.reshape(-1,3)
 assert np.array_equal(old[rest[:,2]<.46],weights[rest[:,2]<.46])
 if rig.animation_data:rig.animation_data.action=None
 for b in rig.pose.bones:
  if not b.name.startswith('HipDeform_'):b.matrix_basis=Matrix.Identity(4)
 bpy.context.view_layer.objects.active=rig;rig.select_set(True);bpy.ops.object.mode_set(mode='EDIT')
 for side in ['L','R']:
  original=rig.data.edit_bones['UpperArm_'+side];helper=rig.data.edit_bones.new('ShoulderBlend_'+side)
  helper.head=original.head;helper.tail=original.tail;helper.roll=original.roll;helper.parent=rig.data.edit_bones['Chest'];helper.use_deform=True
 bpy.ops.object.mode_set(mode='OBJECT')
 for side in ['L','R']:
  helper=rig.pose.bones['ShoulderBlend_'+side];c=helper.constraints.new('COPY_TRANSFORMS');c.name='Half chest-to-arm skin rotation';c.target=rig;c.subtarget='UpperArm_'+side;c.owner_space='POSE';c.target_space='POSE';c.influence=.5
 for name in names:
  if body.vertex_groups.get(name) is None:body.vertex_groups.new(name=name)
 groups=[body.vertex_groups[name] for name in names]
 for i in changed:
  for b in range(len(names)):
   if old[i,b]==weights[i,b]:continue
   if weights[i,b]>0:groups[b].add([int(i)],float(weights[i,b]),'REPLACE')
   else:groups[b].remove([int(i)])
 body.data.update();bpy.context.view_layer.update()
 actual_names,actual=common.read_weights(body);assert set(actual_names)==set(names) and np.array_equal(actual[:,[actual_names.index(name) for name in names]],weights)
 assert common.geometry_identity(body)==geometry['body'] and common.geometry_identity(head)==geometry['head']
 assert np.array_equal(common.read_weights(head)[1],head_weights[1])
 after={b['name']:b for b in common.rig_identity(rig)};assert all(after[b['name']]==b for b in bones_before)
 out.mkdir(parents=True);blend=out/'R20_ShoulderBlendCandidate.blend';body['shoulder_blend_scope']='Local shoulder weights and two skin-only helper bones; original mesh and 22 bones preserved'
 bpy.ops.wm.save_as_mainfile(filepath=str(blend));blend_hash=common.sha(blend)
 # Keep the original transport's geometry, normals, head block and bone order.
 c=Matrix(((1,0,0,0),(0,0,1,0),(0,-1,0,0),(0,0,0,1)))
 for side in ['L','R']:
  b=rig.data.bones['ShoulderBlend_'+side];world=c@b.matrix_local@c.inverted();parent=c@b.parent.matrix_local@c.inverted();local=parent.inverted()@world;q=local.to_quaternion()
  header['bones'].append({'name':b.name,'parent':names.index('Chest'),'position':list(local.translation),'rotation':[q.x,q.y,q.z,q.w]})
 offset=4+size;blocks=[]
 for record in header['meshes']:
  n=record['vertices'];end=offset+n*56+record['triangleIndices']*4;block=bytearray(data[offset:end]);assert len(block)==end-offset
  if record['name']==body.name:
   ids=np.argsort(weights,axis=1)[:,-4:][:,::-1].astype('<i4');vals=np.take_along_axis(weights,ids,axis=1).astype('<f4');block[n*24:n*40]=ids.tobytes();block[n*40:n*56]=vals.tobytes()
  blocks.append(block);offset=end
 assert offset==len(data);header['sourceSha256']=blend_hash;header['shoulderBlendCandidate']=True;encoded=json.dumps(header).encode();target=out/'ShoulderCharacter.bytes';target.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(blocks))
 assert common.sha(source)==source_hash
 report={'source':str(source),'sourceSha256':source_hash,'candidate':str(blend),'candidateSha256':blend_hash,'transport':str(target),'transportSha256':common.sha(target),'generatorSha256':common.sha(__file__),'weightsSha256':common.sha(weights_path),'original22BonesUnchanged':True,'boneCount':24,'addedSkinOnlyBones':['ShoulderBlend_L','ShoulderBlend_R'],'geometryUnchanged':True,'headAndLowerWeightsUnchanged':True,'changedVertices':int(len(changed)),'maximumInfluences':int(np.count_nonzero(weights>0,axis=1).max()),'runtimeRequirement':'Drive skin helpers halfway from chest reference to physical upper arm, without any physics body or feedback.','userVisualApproval':False}
 (out/'ModelReview.json').write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n');print(json.dumps(report),flush=True)
if __name__=='__main__':main()
