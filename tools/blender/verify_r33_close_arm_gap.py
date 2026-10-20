"""Verify the local gap fill and preserve the original shoulder/rig identity."""
from pathlib import Path
import json,struct,sys
import bpy,numpy as np
sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_r27_surface_refine import sha
from create_c1b_rw020_lower_weight_candidate import rig_identity,geometry_identity,action_identity,read_weights
ROOT=Path(__file__).resolve().parents[2];source=ROOT/'BlenderSource/Characters/C1B-RW-028-cylindrical-upper-arms';candidate=Path(sys.argv[sys.argv.index('--')+1]).resolve()
def read(path):
    d=path.read_bytes();z=struct.unpack_from('<i',d)[0];h=json.loads(d[4:4+z]);o=4+z;blocks=[]
    for item in h['meshes']:
        end=o+item['vertices']*56+item['triangleIndices']*4;blocks.append(d[o:end]);o=end
    return d,h,blocks,o
old,oh,ob,oo=read(source/'CylindricalCharacter.bytes');data,h,blocks,o=read(candidate/'CloseGapCharacter.bytes');assert h['bones']==oh['bones'] and h['meshes']==oh['meshes'] and h['surfacePoses']==oh['surfacePoses'];assert blocks[1:]==ob[1:]
n=h['meshes'][0]['vertices'];assert blocks[0][n*24:]==ob[0][n*24:];v=np.frombuffer(blocks[0],'<f4',n*3).reshape(n,3);original=np.frombuffer(ob[0],'<f4',n*3).reshape(n,3);changed=np.any(v!=original,axis=1)
raw=np.frombuffer((ROOT/'.codex_tmp/arm-volume-audit-20261008/Captures/Idle_Matrices.bin').read_bytes(),'<f4',offset=4).reshape(-1,4,4);ids=np.frombuffer(ob[0],'<i4',n*4,n*24).reshape(n,4);w=np.frombuffer(ob[0],'<f4',n*4,n*40).reshape(n,4);pal=np.linalg.inv(raw[0])@raw;t=(pal[ids]*w[:,:,None,None]).sum(1);posed=np.einsum('nij,nj->ni',t[:,:3,:3],original)+t[:,:3,3]
assert not np.any(changed&(posed[:,1]>=.58));assert not np.any(changed&(posed[:,1]<=.44))
mirror=np.load(candidate/'NeutralBindData.npz')['mirror'];mirror_error=float(np.linalg.norm(v-v[mirror]*[-1,1,1],axis=1).max());assert mirror_error<1e-7
bpy.ops.wm.open_mainfile(filepath=str(source/'CHR_MasterCharacter_C1B_CylindricalArms_r28.blend'),load_ui=False);oldrig=rig_identity(bpy.data.objects['RIG_C1B_R20_Armature']);head=geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedHead']);topology=geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedBody']);weights=read_weights(bpy.data.objects['CHR_C1B_R20_SkinnedBody']);actions={a.name:action_identity(a) for a in bpy.data.actions}
blend=candidate/'CHR_MasterCharacter_C1B_CloseArmGap_r33.blend';assert sha(blend)==h['sourceSha256'];bpy.ops.wm.open_mainfile(filepath=str(blend),load_ui=False);body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data
assert rig_identity(bpy.data.objects['RIG_C1B_R20_Armature'])==oldrig;assert geometry_identity(bpy.data.objects['CHR_C1B_R20_SkinnedHead'])==head;assert {a.name:action_identity(a) for a in bpy.data.actions}==actions
now=geometry_identity(body)
for key in ('vertices','edges','faces','edgeHash','loopHash'):assert now[key]==topology[key]
newweights=read_weights(body);assert newweights[0]==weights[0] and np.array_equal(newweights[1],weights[1]);p=np.empty((n,3),np.float32);mesh.vertices.foreach_get('co',p.ravel());convert=lambda p:p[:,[0,2,1]]*np.array([1,1,-1],np.float32);assert np.array_equal(convert(p),v)
mesh.attributes['r33_normal_base'].data.foreach_get('vector',p.ravel());assert np.array_equal(convert(p),np.frombuffer(blocks[0],'<f4',n*3,n*12).reshape(n,3));maximum=0.
for ref in h['surfacePoses']:
    if not ref['hasDelta']:continue
    delta=np.frombuffer(data,'<f4',n*3,o).reshape(n,3);old_delta=np.frombuffer(old,'<f4',n*3,oo).reshape(n,3);assert np.array_equal(delta,old_delta)
    mesh.shape_keys.key_blocks[ref['name']].data.foreach_get('co',p.ravel());error=float(np.max(abs(convert(p)-v-delta)));maximum=max(maximum,error);assert error<1e-7;o+=n*24;oo+=n*24
assert o==len(data) and oo==len(old)
report={'passed':True,'originalShoulderAndLowerBodyCoordinatesProtected':True,'headRigWeightsTopologyActionsPreserved':True,'originalPosePositionDeltasPreserved':True,'nativeTransportBaseAndPosePositionsMatch':True,'maximumNativeKeyRoundingError':maximum,'mirrorError':mirror_error,'candidateSha256':sha(blend),'transportSha256':sha(candidate/'CloseGapCharacter.bytes')};(candidate/'NativeValidation.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)
