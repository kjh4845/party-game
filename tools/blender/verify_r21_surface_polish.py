import bpy,numpy as np,json,sys,struct,hashlib
from pathlib import Path
from mathutils.kdtree import KDTree
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/blender'))
import create_c1b_rw020_lower_weight_candidate as common
src=ROOT/'.codex_tmp/shoulder-reach-20260916/candidate-r02/R20_ShoulderBlendCandidate.blend'
out=ROOT/'BlenderSource/Characters/C1B-RW-021-surface-polish-r02'
def snapshot(p):
 bpy.ops.wm.open_mainfile(filepath=str(p),load_ui=False)
 rig=bpy.data.objects['RIG_C1B_R20_Armature'];body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];head=bpy.data.objects['CHR_C1B_R20_SkinnedHead']
 v=np.empty((len(body.data.vertices),3),np.float32);body.data.vertices.foreach_get('co',v.ravel())
 return {'rig':common.rig_identity(rig),'actions':{a.name:common.action_identity(a) for a in bpy.data.actions if len(a.layers)},'geometry':common.geometry_identity(body),'head':common.geometry_identity(head),'weights':common.read_weights(body),'headWeights':common.read_weights(head),'vertices':v,'faces':np.array([list(p.vertices) for p in body.data.polygons],dtype=np.int32)}
a=snapshot(src);b=snapshot(out/'CHR_MasterCharacter_C1B_SurfacePolish_r21.blend')
assert a['rig']==b['rig'] and a['actions']==b['actions'] and a['head']==b['head']
assert a['geometry']['edgeHash']==b['geometry']['edgeHash'] and a['geometry']['loopHash']==b['geometry']['loopHash']
for key in ['weights','headWeights']:
 assert a[key][0]==b[key][0] and np.array_equal(a[key][1],b[key][1])
v=b['vertices'];tree=KDTree(len(v))
for i,p in enumerate(v):tree.insert(p,i)
tree.balance();mirror=max(tree.find((-p[0],p[1],p[2]))[2] for p in v)
assert mirror<1e-5
face=b['faces'];pa=a['vertices'][face];pb=v[face]
na=np.cross(pa[:,1]-pa[:,0],pa[:,2]-pa[:,0]);nb=np.cross(pb[:,1]-pb[:,0],pb[:,2]-pb[:,0]);dots=np.einsum('ij,ij->i',na,nb)
assert np.all(dots>0)
def transport(p):
 data=p.read_bytes();n=struct.unpack_from('<i',data)[0];h=json.loads(data[4:4+n]);offset=4+n;blocks=[]
 for m in h['meshes']:
  end=offset+m['vertices']*56+m['triangleIndices']*4;blocks.append(data[offset:end]);offset=end
 assert offset==len(data);return h,blocks
h1,blocks1=transport(src.parent/'ShoulderCharacter.bytes');h2,blocks2=transport(out/'SurfaceCharacter.bytes')
assert h1['bones']==h2['bones'] and h1['meshes']==h2['meshes']
assert blocks1[0][len(v)*24:]==blocks2[0][len(v)*24:] and blocks1[1]==blocks2[1]
report={'passed':True,'boneCount':len(h2['bones']),'actionCount':len(a['actions']),'topologyIdentical':True,'bodyWeightsIdentical':True,'headGeometryWeightsNormalsIdentical':True,'noFlippedFaces':True,'mirrorErrorMeters':mirror,'maxDisplacementMeters':float(np.linalg.norm(v-a['vertices'],axis=1).max()),'transportWeightsTrianglesIdentical':True,'sourceSha256':common.sha(src)}
(out/'SurfaceValidation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)
