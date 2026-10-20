"""Verify r34 native/transport parity and protected r33 proportions and motion."""
from pathlib import Path
import json,struct,sys
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_r27_surface_refine import sha
from create_c1b_rw020_lower_weight_candidate import rig_identity,geometry_identity,action_identity,read_weights
ROOT=Path(__file__).resolve().parents[2];source=ROOT/'BlenderSource/Characters/C1B-RW-033-close-arm-gap';candidate=Path(sys.argv[sys.argv.index('--')+1]).resolve()
def read(path):
    d=path.read_bytes();z=struct.unpack_from('<i',d)[0];h=json.loads(d[4:4+z]);o=4+z;blocks=[]
    for item in h['meshes']:
        end=o+item['vertices']*56+item['triangleIndices']*4;blocks.append(d[o:end]);o=end
    return d,h,blocks,o
old,oh,ob,oo=read(source/'CloseGapCharacter.bytes');data,h,blocks,o=read(candidate/'FairBodyCharacter.bytes');assert h['bones']==oh['bones'] and h['meshes']==oh['meshes'] and h['surfacePoses']==oh['surfacePoses'];assert blocks[1:]==ob[1:]
n=h['meshes'][0]['vertices'];assert blocks[0][n*24:]==ob[0][n*24:];v=np.frombuffer(blocks[0],'<f4',n*3).reshape(n,3);original=np.frombuffer(ob[0],'<f4',n*3).reshape(n,3)
raw=np.frombuffer((ROOT/'.codex_tmp/close-arm-gap-20261010/CandidateC/UnityCaptures/Idle_Matrices.bin').read_bytes(),'<f4',offset=4).reshape(-1,4,4);ids=np.frombuffer(ob[0],'<i4',n*4,n*24).reshape(n,4);w=np.frombuffer(ob[0],'<f4',n*4,n*40).reshape(n,4);pal=np.linalg.inv(raw[0])@raw;t=(pal[ids]*w[:,:,None,None]).sum(1);posed=np.einsum('nij,nj->ni',t[:,:3,:3],original)+t[:,:3,3]
material=np.load(candidate/'NeutralBindData.npz')['material'];protected=(posed[:,1]>=.57)|(posed[:,1]<=.035)|(abs(material[:,0])>=.25);assert np.array_equal(v[protected],original[protected])
newposed=np.einsum('nij,nj->ni',t[:,:3,:3],v)+t[:,:3,3];floor_error=abs(float(newposed[:,1].min()-posed[:,1].min()));assert floor_error<1e-5
mirror=np.load(candidate/'NeutralBindData.npz')['mirror'];mirror_error=float(np.linalg.norm(v-v[mirror]*[-1,1,1],axis=1).max());assert mirror_error<1e-7
bpy.ops.wm.open_mainfile(filepath=str(source/'CHR_MasterCharacter_C1B_CloseArmGap_r33.blend'),load_ui=False);oldrig=rig_identity(bpy.data.objects['RIG_C1B_R20_Armature']);head=geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedHead']);topology=geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedBody']);weights=read_weights(bpy.data.objects['CHR_C1B_R20_SkinnedBody']);actions={a.name:action_identity(a) for a in bpy.data.actions}
blend=candidate/'CHR_MasterCharacter_C1B_FairBody_r34.blend';assert sha(blend)==h['sourceSha256'];bpy.ops.wm.open_mainfile(filepath=str(blend),load_ui=False);body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data
assert rig_identity(bpy.data.objects['RIG_C1B_R20_Armature'])==oldrig;assert geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedHead'])==head;assert {a.name:action_identity(a) for a in bpy.data.actions}==actions
now=geometry_identity(body)
for key in ('vertices','edges','faces','edgeHash','loopHash'):assert now[key]==topology[key]
nw=read_weights(body);assert nw[0]==weights[0] and np.array_equal(nw[1],weights[1]);p=np.empty((n,3),np.float32);mesh.vertices.foreach_get('co',p.ravel());convert=lambda p:p[:,[0,2,1]]*np.array([1,1,-1],np.float32);assert np.array_equal(convert(p),v)
mesh.attributes['r34_normal_base'].data.foreach_get('vector',p.ravel());assert np.array_equal(convert(p),np.frombuffer(blocks[0],'<f4',n*3,n*12).reshape(n,3));maximum=0.
for ref in h['surfacePoses']:
    if not ref['hasDelta']:continue
    dp=np.frombuffer(data,'<f4',n*3,o).reshape(n,3);old_delta=np.frombuffer(old,'<f4',n*3,oo).reshape(n,3);assert np.array_equal(dp,old_delta)
    mesh.shape_keys.key_blocks[ref['name']].data.foreach_get('co',p.ravel());error=float(np.max(abs(convert(p)-v-dp)));maximum=max(maximum,error);assert error<1e-7
    dn=np.frombuffer(data,'<f4',n*3,o+n*12).reshape(n,3);mesh.attributes['r34_normal_'+ref['name']].data.foreach_get('vector',p.ravel());assert np.array_equal(convert(p),dn);o+=n*24;oo+=n*24
assert o==len(data) and oo==len(old)
report={'passed':True,'shoulderOutlineArmShaftsProtected':True,'legGroundLevelErrorMeters':floor_error,'headRigWeightsTopologyActionsPreserved':True,'originalPosePositionDeltasPreserved':True,'nativeTransportBasePosePositionsAndNormalsMatch':True,'maximumNativeKeyRoundingError':maximum,'mirrorError':mirror_error,'candidateSha256':sha(blend),'transportSha256':sha(candidate/'FairBodyCharacter.bytes')};(candidate/'NativeValidation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)
