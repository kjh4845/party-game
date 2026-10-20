"""Close the upper rib/arm gap on r28 without moving or widening shoulders.

Blender --background --python-exit-code 1 --python this.py -- output-directory
The edit is a symmetric lateral fill below the original shoulder line. Bones,
skin weights, topology, head, arm exterior and accepted motion remain intact.
"""
from pathlib import Path
import json,struct,sys
import bpy,numpy as np
from mathutils.kdtree import KDTree
sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_r27_surface_refine import normals,sha

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'BlenderSource/Characters/C1B-RW-028-cylindrical-upper-arms'

def smooth(x):
    x=np.clip(x,0,1)
    return x*x*(3-2*x)

def main():
    output=Path(sys.argv[sys.argv.index('--')+1]).resolve();assert not output.exists()
    data=(SOURCE/'CylindricalCharacter.bytes').read_bytes();size=struct.unpack_from('<i',data)[0];header=json.loads(data[4:4+size]);offset=4+size;record=header['meshes'][0];n=record['vertices']
    source=SOURCE/'CHR_MasterCharacter_C1B_CylindricalArms_r28.blend';assert sha(source)==header['sourceSha256']
    rest=np.frombuffer(data,'<f4',n*3,offset).reshape(n,3).copy();base_normal=np.frombuffer(data,'<f4',n*3,offset+n*12).reshape(n,3).copy();ids=np.frombuffer(data,'<i4',n*4,offset+n*24).reshape(n,4);weights=np.frombuffer(data,'<f4',n*4,offset+n*40).reshape(n,4);tri=np.frombuffer(data,'<i4',record['triangleIndices'],offset+n*56).reshape(-1,3)
    old=(ROOT/'BlenderSource/Characters/C1B-RW-026-smooth-body/SmoothCharacter.bytes').read_bytes();material=np.frombuffer(old,'<f4',n*3,4+struct.unpack_from('<i',old)[0]).reshape(n,3);names=[b['name'] for b in header['bones']]
    raw=np.frombuffer((ROOT/'.codex_tmp/arm-volume-audit-20261008/Captures/Idle_Matrices.bin').read_bytes(),'<f4',offset=4).reshape(-1,4,4).astype(float);palette=np.linalg.inv(raw[0])@raw
    transforms=(palette[ids]*weights[:,:,None,None]).sum(1);posed=np.einsum('nij,nj->ni',transforms[:,:3,:3],rest)+transforms[:,:3,3]
    x,y,z=posed.T;reach=np.abs(material[:,0])
    # Shoulder surface at and above .58, including its entire outer outline,
    # is excluded. The lower waist and all arm-shaft material are excluded too.
    torso_bones=[names.index(name) for name in ('Pelvis','Spine','Chest','Clavicle_L','Clavicle_R')]
    owner=(weights*np.isin(ids,torso_bones)).sum(1)
    mask=smooth((np.abs(x)-.080)/.060)*smooth((y-.44)/.070)*(1-smooth((y-.525)/.055))
    mask*=smooth((owner-.65)/.30)*(1-smooth((reach-.220)/.030))
    mask*=1-smooth((material[:,1]-.555)/.030)
    mask*=1-smooth((np.abs(z)-.075)/.075)
    amplitude=.017
    delta=np.zeros_like(posed);delta[:,0]=np.sign(x)*amplitude*mask
    bind_delta=np.linalg.solve(transforms[:,:3,:3],delta[...,None])[...,0]
    tree=KDTree(n)
    for i,p in enumerate(rest):tree.insert(p,i)
    tree.balance();mirror=np.array([tree.find((-float(p[0]),float(p[1]),float(p[2])))[1] for p in rest]);left=np.flatnonzero(rest[:,0]<-1e-6)
    bind_delta[mirror[left]]=bind_delta[left]*[-1,1,1];result=(rest+bind_delta).astype('<f4');affected=np.any(result!=rest,axis=1)
    normal_region=affected.copy();normal_region[np.unique(tri[np.any(affected[tri],axis=1)])]=True
    target=np.einsum('nij,nj->ni',transforms[:,:3,:3],result)+transforms[:,:3,3];new_normal=base_normal.copy()
    calculated=np.linalg.solve(transforms[:,:3,:3],normals(target,tri)[...,None])[...,0];calculated/=np.maximum(np.linalg.norm(calculated,axis=1)[:,None],1e-12);new_normal[normal_region]=calculated[normal_region];new_normal[mirror[left]]=new_normal[left]*[-1,1,1]
    protected=(y>=.58)|(y<=.44)|(reach>=.250)|(owner<=.65)|(material[:,1]>=.585)
    assert np.array_equal(result[protected],rest[protected])
    face_before=np.cross(posed[tri[:,1]]-posed[tri[:,0]],posed[tri[:,2]]-posed[tri[:,0]]);face_after=np.cross(target[tri[:,1]]-target[tri[:,0]],target[tri[:,2]]-target[tri[:,0]])
    assert np.all(np.sum(face_before*face_after,axis=1)>0),'Lateral fill must not reverse any neutral face'
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False);body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data
    native=lambda p:(p[:,[0,2,1]]*[1,-1,1]).astype(np.float32)
    mesh.vertices.foreach_set('co',native(result).ravel());mesh.shape_keys.key_blocks['Basis'].data.foreach_set('co',native(result).ravel());mesh.normals_split_custom_set_from_vertices(native(new_normal).tolist())
    attr=mesh.attributes.get('r33_normal_base') or mesh.attributes.new(name='r33_normal_base',type='FLOAT_VECTOR',domain='POINT');attr.data.foreach_set('vector',native(new_normal).ravel())
    blocks=[];current=offset
    for item in header['meshes']:
        end=current+item['vertices']*56+item['triangleIndices']*4;block=bytearray(data[current:end])
        if item['name']==body.name:block[:n*12]=result.tobytes();block[n*12:n*24]=new_normal.astype('<f4').tobytes()
        blocks.append(block);current=end
    mapped=np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names]);reflect=np.diag([-1.,1,1,1])
    pose_files={'Raise45':'Raised_45_Matrices.bin','Raise90':'Raised_90_Matrices.bin','Prepare':'Punch_02_Matrices.bin','Accelerate':'Punch_05_Matrices.bin','Strike':'Punch_08_Matrices.bin','FollowThrough':'Punch_12_Matrices.bin','Recover':'Punch_18_Matrices.bin'}
    for ref in header['surfacePoses']:
        if not ref['hasDelta']:continue
        old_delta=np.frombuffer(data,'<f4',n*3,current).reshape(n,3);old_dn=np.frombuffer(data,'<f4',n*3,current+n*12).reshape(n,3);current+=n*24
        label,side=ref['name'].rsplit('_',1);file=pose_files.get(label,label+'_Matrices.bin');m=np.frombuffer((SOURCE/'ReferencePoses'/file).read_bytes(),'<f4',offset=4).reshape(-1,4,4).astype(float);m=np.linalg.inv(m[0])@m
        if side=='R':m=reflect@m[mapped]@reflect
        t=(m[ids]*weights[:,:,None,None]).sum(1);p=np.einsum('nij,nj->ni',t[:,:3,:3],result+old_delta)+t[:,:3,3];nn=np.linalg.solve(t[:,:3,:3],normals(p,tri)[...,None])[...,0];nn/=np.maximum(np.linalg.norm(nn,axis=1)[:,None],1e-12)
        dn=old_dn.copy();dn[normal_region]=nn[normal_region]-new_normal[normal_region];blocks.extend((old_delta.tobytes(),dn.astype('<f4').tobytes()))
        mesh.shape_keys.key_blocks[ref['name']].data.foreach_set('co',native(result+old_delta).ravel())
    assert current==len(data)
    output.mkdir(parents=True);blend=output/'CHR_MasterCharacter_C1B_CloseArmGap_r33.blend';body['r33_edit']='r28 shoulder outline and rig preserved; lateral upper-rib fill only';bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True)
    header['sourceSha256']=sha(blend);encoded=json.dumps(header).encode();transport=output/'CloseGapCharacter.bytes';transport.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(blocks))
    np.savez(output/'NeutralBindData.npz',material=material,mirror=mirror)
    report={'sourceSha256':sha(source),'candidateSha256':sha(blend),'transportSha256':sha(transport),'maximumNeutralFillMeters':float(np.linalg.norm(target-posed,axis=1).max()),'changedVertices':int(affected.sum()),'shoulderOutlineProtected':True,'rigWeightsTopologyHeadPreserved':True,'mirrorError':float(np.linalg.norm(result-result[mirror]*[-1,1,1],axis=1).max()),'visualApproved':False}
    (output/'CloseGapReview.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)

if __name__=='__main__':main()
