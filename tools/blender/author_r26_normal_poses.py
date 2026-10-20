"""Add mirrored, normal-only pose references to an r26 geometry-stage asset.

Blender --background --python-exit-code 1 --python this.py -- source-dir output-dir pose-dir
pose-dir contains IdleSkinMatrices.bin, Raised_45_Matrices.bin,
Raised_90_Matrices.bin and Punch_08_Matrices.bin from Unity review captures.
"""
from pathlib import Path
import bpy,numpy as np,json,struct,sys,hashlib
from mathutils import Matrix
root=Path(__file__).resolve().parents[2]
args=sys.argv[sys.argv.index('--')+1:]
source,output,poses=map(lambda p:Path(p).resolve(),args[:3])
if output.exists():raise RuntimeError('Preserve model iterations')
data=(source/'SmoothCharacter.bytes').read_bytes();sz=struct.unpack_from('<i',data)[0];header=json.loads(data[4:4+sz]);o=4+sz;n=header['meshes'][0]['vertices'];v=np.frombuffer(data,'<f4',n*3,o).reshape(n,3);normals=np.frombuffer(data,'<f4',n*3,o+n*12).reshape(n,3);ids=np.frombuffer(data,'<i4',n*4,o+n*24).reshape(n,4);weights=np.frombuffer(data,'<f4',n*4,o+n*40).reshape(n,4);tri=np.frombuffer(data,'<i4',header['meshes'][0]['triangleIndices'],o+n*56).reshape(-1,3);names=[r['name'] for r in header['bones']]
assert header['version']==1, 'Use a geometry-stage transport before adding normal frames'
assert header['sourceSha256']==hashlib.sha256((source/'CHR_MasterCharacter_C1B_SmoothBody_r26.blend').read_bytes()).hexdigest()
dense=np.zeros((n,24));np.add.at(dense,(np.arange(n)[:,None],ids),weights)
bpy.ops.wm.open_mainfile(filepath=str(source/'CHR_MasterCharacter_C1B_SmoothBody_r26.blend'),load_ui=False);body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data
from mathutils.kdtree import KDTree
tree=KDTree(n)
for i,p in enumerate(v):tree.insert(p,i)
tree.balance();mirror=np.array([tree.find((-float(p[0]),float(p[1]),float(p[2])))[1] for p in v]);mapped=np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names]);reflect=np.diag([-1.,1,1,1])
rest_bones=[]
for record in header['bones']:
 x,y,z,w=record['rotation'];m=np.eye(4);m[:3,:3]=np.array([[1-2*(y*y+z*z),2*(x*y-z*w),2*(x*z+y*w)],[2*(x*y+z*w),1-2*(x*x+z*z),2*(y*z-x*w)],[2*(x*z-y*w),2*(y*z+x*w),1-2*(x*x+y*y)]]);m[:3,3]=record['position'];rest_bones.append(m if record['parent']<0 else rest_bones[record['parent']]@m)
rest_bones=np.array(rest_bones)
def rotation(mats,side):
 chest=mats[names.index('Chest')]@rest_bones[names.index('Chest')];upper=mats[names.index('UpperArm_'+side)]@rest_bones[names.index('UpperArm_'+side)];relative=np.linalg.inv(chest[:3,:3])@upper[:3,:3];q=Matrix(relative.tolist()).to_quaternion();return [float(q.x),float(q.y),float(q.z),float(q.w)]
def smooth(x):
 x=np.clip(x,0,1);return x*x*(3-2*x)
arm=[names.index('Clavicle_L'),names.index('UpperArm_L'),names.index('ShoulderBlend_L')];trunk=[names.index(k) for k in ['Pelvis','Spine','Chest','Neck','ShoulderBlend_L']];mask=smooth(dense[:,arm].sum(1)/.025)*smooth(dense[:,trunk].sum(1)/.05)
neutral_path=poses/'IdleSkinMatrices.bin';neutral=np.frombuffer(neutral_path.read_bytes(),'<f4',offset=4).reshape(24,4,4)
refs=[];blocks=[];provenance=[]
for side in ['L','R']:
 mats=neutral if side=='L' else np.einsum('ij,bjk,kl->bil',reflect,neutral[mapped],reflect)
 refs.append({'name':'Neutral_'+side,'side':0 if side=='L' else 1,'rotation':rotation(mats,side),'hasNormals':False})
for label,path in [('Raise45',poses/'Raised_45_Matrices.bin'),('Raise90',poses/'Raised_90_Matrices.bin'),('Strike',poses/'Punch_08_Matrices.bin')]:
 mats=np.frombuffer(path.read_bytes(),'<f4',offset=4).reshape(24,4,4).astype(float);t=(mats[ids]*weights[:,:,None,None]).sum(1);p=np.einsum('nij,nj->ni',t[:,:3,:3],v)+t[:,:3,3];fn=np.cross(p[tri[:,1]]-p[tri[:,0]],p[tri[:,2]]-p[tri[:,0]]);normal=np.zeros((n,3))
 for corner in range(3):np.add.at(normal,tri[:,corner],fn)
 normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-12);bind=np.linalg.solve(t[:,:3,:3],normal[...,None])[...,0];bind/=np.maximum(np.linalg.norm(bind,axis=1)[:,None],1e-12);left=(bind-normals)*mask[:,None];left[np.linalg.norm(left,axis=1)<.0005]=0
 for side in ['L','R']:
  delta=left if side=='L' else left[mirror]*np.array([-1,1,1]);pose=mats if side=='L' else np.einsum('ij,bjk,kl->bil',reflect,mats[mapped],reflect);name=label+'_'+side
  refs.append({'name':name,'side':0 if side=='L' else 1,'rotation':rotation(pose,side),'hasNormals':True});blocks.append(delta.astype('<f4').tobytes())
  attribute=mesh.attributes.new(name='r26_normal_'+name,type='FLOAT_VECTOR',domain='POINT');attribute.data.foreach_set('vector',(delta[:,[0,2,1]]*np.array([1,-1,1])).astype(np.float32).ravel())
 provenance.append({'pose':label,'path':str(path),'sha256':hashlib.sha256(path.read_bytes()).hexdigest()})
body['normal_pose_references']=json.dumps(refs)
output.mkdir(parents=True);blend=output/'CHR_MasterCharacter_C1B_SmoothBody_r26.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend))
header['version']=2;header['normalPoses']=refs;header['sourceSha256']=hashlib.sha256(blend.read_bytes()).hexdigest();encoded=json.dumps(header).encode();transport=output/'SmoothCharacter.bytes';transport.write_bytes(struct.pack('<i',len(encoded))+encoded+data[o:]+b''.join(blocks))
report=json.loads((source/'SmoothBodyReview.json').read_text());report.update({'candidateSha256':header['sourceSha256'],'transportSha256':hashlib.sha256(transport.read_bytes()).hexdigest(),'normalPoseCorrectives':len(blocks),'normalPoseGeometryDeltas':'zero; normals only','normalPoseReferences':provenance});(output/'SmoothBodyReview.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps({'normalFrames':len(blocks),'bytes':transport.stat().st_size,'output':str(output)}),flush=True)
