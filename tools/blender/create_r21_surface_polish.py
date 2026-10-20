"""Topology-preserving bounded surface fairing; writes a new Blender + Unity review pair."""
import bpy, numpy as np, json, struct, hashlib, sys
from pathlib import Path
from mathutils import Vector, Matrix

ROOT=Path(__file__).resolve().parents[2]
args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else []
SOURCE=ROOT/'.codex_tmp/shoulder-reach-20260916/candidate-r02'
OUT=Path(args[0]).resolve() if args else ROOT/'BlenderSource/Characters/C1B-RW-021-surface-polish-r02'
EVIDENCE=Path(args[1]).resolve() if len(args)>1 else OUT/'Renders'
def sha(p):return hashlib.sha256(Path(p).read_bytes()).hexdigest()
def smoothstep(x):x=np.clip(x,0,1);return x*x*(3-2*x)

bpy.ops.wm.open_mainfile(filepath=str(SOURCE/'R20_ShoulderBlendCandidate.blend'),load_ui=False)
body=bpy.data.objects['CHR_C1B_R20_SkinnedBody']; mesh=body.data
v=np.empty((len(mesh.vertices),3),np.float32);mesh.vertices.foreach_get('co',v.ravel())
edges=np.empty((len(mesh.edges),2),np.int32);mesh.edges.foreach_get('vertices',edges.ravel())
src=np.concatenate((edges[:,0],edges[:,1]));dst=np.concatenate((edges[:,1],edges[:,0]));counts=np.bincount(src,minlength=len(v))[:,None]
def lap(p):
 return np.column_stack([np.bincount(src,weights=p[dst,k],minlength=len(v)) for k in range(3)])/np.maximum(counts,1)-p
before_rough=np.linalg.norm(lap(v),axis=1)
p=v.astype(np.float64)
# Fair a wider neighbourhood, then restore the original overall extents.
# The final displacement cap and sole/terminal gate preserve the silhouette.
for i in range(240):p+=.5*lap(p)
mid=(v.min(0)+v.max(0))*.5;span=v.max(0)-v.min(0)
p=(p-(p.min(0)+p.max(0))*.5)*(span/(p.max(0)-p.min(0)))+mid
delta=p-v
# Keep planted soles, ends and the approved outer dimensions fixed at their extrema.
gate=smoothstep((v[:,2]-.012)/.025)*smoothstep((.574-np.abs(v[:,0]))/.025)
length=np.linalg.norm(delta,axis=1);delta*=np.minimum(1,.0025/np.maximum(length,1e-12))[:,None]*gate[:,None]
result=(v+delta).astype(np.float32)
mesh.vertices.foreach_set('co',result.ravel());mesh.update()
after_rough=np.linalg.norm(lap(result),axis=1)
OUT.mkdir(parents=True,exist_ok=True);EVIDENCE.mkdir(parents=True,exist_ok=True)
blend=OUT/'CHR_MasterCharacter_C1B_SurfacePolish_r21.blend'
if blend.exists():raise RuntimeError('Preserve existing candidate')
bpy.ops.wm.save_as_mainfile(filepath=str(blend))
data=(SOURCE/'ShoulderCharacter.bytes').read_bytes();size=struct.unpack_from('<i',data)[0];header=json.loads(data[4:4+size]);offset=4+size;blocks=[]
for rec in header['meshes']:
 n=rec['vertices'];end=offset+n*56+rec['triangleIndices']*4;block=bytearray(data[offset:end])
 if rec['name']==body.name:
  original=np.frombuffer(block,dtype='<f4',count=n*3).reshape(n,3)
  converted=v[:,[0,2,1]]*np.array([1,1,-1],np.float32)
  assert np.max(abs(original-converted))<1e-6
  normals=np.empty((n,3),np.float32);mesh.vertices.foreach_get('normal',normals.ravel())
  block[:n*12]=(result[:,[0,2,1]]*np.array([1,1,-1],np.float32)).astype('<f4').tobytes()
  block[n*12:n*24]=(normals[:,[0,2,1]]*np.array([1,1,-1],np.float32)).astype('<f4').tobytes()
 blocks.append(block);offset=end
assert offset==len(data)
header['sourceSha256']=sha(blend);header['surfacePolishCandidate']=True
encoded=json.dumps(header).encode();transport=OUT/'SurfaceCharacter.bytes';transport.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(blocks))
report={'source':str(SOURCE/'R20_ShoulderBlendCandidate.blend'),'sourceSha256':sha(SOURCE/'R20_ShoulderBlendCandidate.blend'),'candidate':str(blend),'candidateSha256':sha(blend),'transportSha256':sha(transport),'bodyVertices':len(v),'passes':240,'maximumDisplacementMeters':float(np.max(np.linalg.norm(result-v,axis=1))),'meanDisplacementMeters':float(np.mean(np.linalg.norm(result-v,axis=1))),'laplacianRmsBefore':float(np.sqrt(np.mean(before_rough**2))),'laplacianRmsAfter':float(np.sqrt(np.mean(after_rough**2))),'boundsBefore':[v.min(0).tolist(),v.max(0).tolist()],'boundsAfter':[result.min(0).tolist(),result.max(0).tolist()],'topologyWeightsRigActionsHeadPreserved':True}
(OUT/'SurfaceReview.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)

# Native Blender renders use identical light, camera and pose for each version.
rig=bpy.data.objects['RIG_C1B_R20_Armature']
if rig.animation_data:rig.animation_data.action=None
for b in rig.pose.bones:
 if not b.name.startswith('HipDeform_'):b.matrix_basis=Matrix.Identity(4)
bpy.context.view_layer.update()
for side,sign in [('L',-1),('R',1)]:
 upper=rig.pose.bones['UpperArm_'+side];forearm=rig.pose.bones['Forearm_'+side]
 q=(forearm.head-upper.head).rotation_difference(Vector((sign*.087,0,-1)))
 m=upper.matrix.copy();pos=m.translation.copy();m=q.to_matrix().to_4x4()@m;m.translation=pos;upper.matrix=m
 bpy.context.view_layer.update()
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True
scene.render.resolution_x=800;scene.render.resolution_y=1000;scene.render.resolution_percentage=100
scene.world.color=(.13,.13,.13);scene.view_settings.view_transform='AgX'
mat=bpy.data.materials.new('Comparison neutral clay');mat.diffuse_color=(.66,.72,.8,1);mat.use_nodes=True
bsdf=mat.node_tree.nodes.get('Principled BSDF');bsdf.inputs['Base Color'].default_value=(.66,.72,.8,1);bsdf.inputs['Roughness'].default_value=.36
for obj in [body,bpy.data.objects['CHR_C1B_R20_SkinnedHead']]:obj.data.materials.clear();obj.data.materials.append(mat)
for name,loc,power,size in [('Key',(-2,-3,3),450,2),('Rim',(1,2,2),350,1.5),('Fill',(2,-1,1),80,2)]:
 d=bpy.data.lights.new(name,'AREA');d.energy=power;d.shape='DISK';d.size=size;o=bpy.data.objects.new(name,d);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.5))-o.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('ComparisonCamera',bpy.data.cameras.new('ComparisonCamera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=1.05
for label,points in [('before',v),('after',result)]:
 mesh.vertices.foreach_set('co',points.ravel());mesh.update();bpy.context.view_layer.update()
 for angle,loc in [('front',(0,-3,.57)),('side',(-3,0,.57)),('threequarter',(-2,-3,1.0))]:
  cam.location=loc;cam.rotation_euler=(Vector((0,0,.48))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(EVIDENCE/(label+'_'+angle+'.png'));bpy.ops.render.render(write_still=True)
