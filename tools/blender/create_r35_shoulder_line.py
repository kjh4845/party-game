"""Export a local neutral shoulder reconstruction as Blender and v3 skin data.

Blender --background --python-exit-code 1 --python this.py -- output target.npz [pose-targets.npz] [skin-weights.npz] [extra-name,...]
The target archive contains actual neutral `target` positions and `mask`,
matched by the existing material vertex order. Bones, original motion actions,
weights and topology remain unchanged. Optional per-pose target surfaces replace
only the affected shoulder positional corrections in the existing pose bank.
"""
from pathlib import Path
import json,struct,sys
import bpy,numpy as np
from mathutils import Quaternion, Matrix
sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_r27_surface_refine import normals,sha
ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'BlenderSource/Characters/C1B-RW-034-fair-body'
POSES=ROOT/'BlenderSource/Characters/C1B-RW-028-cylindrical-upper-arms/ReferencePoses'

def main():
    args=sys.argv[sys.argv.index('--')+1:];out=Path(args[0]).resolve();target_path=Path(args[1]).resolve();pose_path=Path(args[2]).resolve() if len(args)>2 else None;pose_targets=np.load(pose_path) if pose_path else None;weight_path=Path(args[3]).resolve() if len(args)>3 and args[3]!='-' else None;extra_names=args[4].split(',') if len(args)>4 else [];assert not out.exists()
    data=(SOURCE/'FairBodyCharacter.bytes').read_bytes();size=struct.unpack_from('<i',data)[0];h=json.loads(data[4:4+size]);o=4+size;r=h['meshes'][0];n=r['vertices'];source=SOURCE/'CHR_MasterCharacter_C1B_FairBody_r34.blend';assert sha(source)==h['sourceSha256']
    rest=np.frombuffer(data,'<f4',n*3,o).reshape(n,3).copy();old_normal=np.frombuffer(data,'<f4',n*3,o+n*12).reshape(n,3).copy();ids=np.frombuffer(data,'<i4',n*4,o+n*24).reshape(n,4);weights=np.frombuffer(data,'<f4',n*4,o+n*40).reshape(n,4);tri=np.frombuffer(data,'<i4',r['triangleIndices'],o+n*56).reshape(-1,3)
    original_ids=ids;original_weights=weights;weight_mask=np.zeros(n,dtype=bool)
    if weight_path:
        weight_data=np.load(weight_path);ids=weight_data['ids'].astype('<i4');weights=weight_data['weights'].astype('<f4');weight_mask=weight_data['weightMask'];assert ids.shape==(n,4) and weights.shape==(n,4);assert np.max(abs(weights.sum(1)-1))<1e-6 and weights.min()>=0
    aux=np.load(SOURCE/'NeutralBindData.npz');material=aux['material'];mirror=aux['mirror'];names=[b['name'] for b in h['bones']]
    def palette(file):
        raw=np.frombuffer((POSES/file).read_bytes(),'<f4',offset=4).reshape(-1,4,4).astype(float);return np.linalg.inv(raw[0])@raw
    def transform(pal,original=False):
        bid=original_ids if original else ids;bw=original_weights if original else weights
        return (pal[bid]*bw[:,:,None,None]).sum(1)
    def apply(t,p):return np.einsum('nij,nj->ni',t[:,:3,:3],p)+t[:,:3,3]
    neutral_pal=palette('IdleSkinMatrices.bin');nt=transform(neutral_pal);original_nt=transform(neutral_pal,True);posed=apply(original_nt,rest);target_data=np.load(target_path);target=target_data['target'];mask=np.maximum(target_data['mask'],target_data['mask'][mirror]);assert target.shape==posed.shape
    if 'posed' in target_data:assert np.max(abs(target_data['posed']-posed))<1e-6
    if 'material' in target_data:assert np.array_equal(target_data['material'],material)
    assert np.array_equal(target[mask==0],posed[mask==0]),'Target edited outside declared patch'
    bind_delta=np.linalg.solve(nt[:,:3,:3],(target-nt[:,:3,3])[...,None])[...,0]-rest;bind_delta[(mask==0)&(~weight_mask)]=0;left=np.flatnonzero(rest[:,0]<-1e-6);center=abs(rest[:,0])<=1e-6;bind_delta[mirror[left]]=bind_delta[left]*[-1,1,1];bind_delta[center,0]=0;result=(rest+bind_delta).astype('<f4');target=apply(nt,result)
    changed=np.any(result!=rest,axis=1);region=changed.copy();pose_mask=np.maximum(pose_targets['poseMask'],pose_targets['poseMask'][mirror]) if pose_targets is not None and 'poseMask' in pose_targets else mask;pose_mask=np.maximum(pose_mask,weight_mask);region|=pose_mask>0;region[np.unique(tri[np.any(region[tri],axis=1)])]=True
    before=np.cross(posed[tri[:,1]]-posed[tri[:,0]],posed[tri[:,2]]-posed[tri[:,0]]);after=np.cross(target[tri[:,1]]-target[tri[:,0]],target[tri[:,2]]-target[tri[:,0]]);rotated=np.sum(before*after,axis=1)<=0
    # Normal rotation against the source is diagnostic only. Actual integrity
    # requires new-surface degeneracy, adjacent-angle and crossing checks.
    assert np.linalg.norm(after,axis=1).min()>1e-12,'Degenerate neutral triangle'
    def inverse_normal(t,p):
        nn=np.linalg.solve(t[:,:3,:3],normals(p,tri)[...,None])[...,0];return nn/np.maximum(np.linalg.norm(nn,axis=1)[:,None],1e-12)
    base=old_normal.copy();base[region]=inverse_normal(nt,target)[region];base[mirror[left]]=base[left]*[-1,1,1]
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False);body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data;native=lambda a:(a[:,[0,2,1]]*[1,-1,1]).astype(np.float32)
    def attr(label,values):
        a=mesh.attributes.get(label) or mesh.attributes.new(name=label,type='FLOAT_VECTOR',domain='POINT');a.data.foreach_set('vector',native(values).ravel())
    if weight_path:
        affected=np.flatnonzero(weight_mask).tolist()
        for name in names:
            group=body.vertex_groups.get(name)
            if group is not None:group.remove(affected)
        for i in affected:
            for slot in range(4):
                value=float(weights[i,slot])
                if value>0:body.vertex_groups[names[ids[i,slot]]].add([i],value,'REPLACE')
    mesh.vertices.foreach_set('co',native(result).ravel());mesh.shape_keys.key_blocks['Basis'].data.foreach_set('co',native(result).ravel());mesh.normals_split_custom_set_from_vertices(native(base).tolist());attr('r35_normal_base',base)
    chunks=[];current=o
    for item in h['meshes']:
        end=current+item['vertices']*56+item['triangleIndices']*4;block=bytearray(data[current:end])
        if item['name']==body.name:block[:n*12]=result.tobytes();block[n*12:n*24]=base.astype('<f4').tobytes();block[n*24:n*40]=ids.astype('<i4').tobytes();block[n*40:n*56]=weights.astype('<f4').tobytes()
        chunks.append(block);current=end
    mapped=np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names]);reflect=np.diag([-1.,1,1,1]);files={'Raise45':'Raised_45_Matrices.bin','Raise90':'Raised_90_Matrices.bin','Prepare':'Punch_02_Matrices.bin','Accelerate':'Punch_05_Matrices.bin','Strike':'Punch_08_Matrices.bin','FollowThrough':'Punch_12_Matrices.bin','Recover':'Punch_18_Matrices.bin'};reports=[];pose_adjustments={}
    for ref in h['surfacePoses']:
        if not ref['hasDelta']:continue
        old_dp=np.frombuffer(data,'<f4',n*3,current).reshape(n,3);dp=old_dp.copy();dn=np.frombuffer(data,'<f4',n*3,current+n*12).reshape(n,3).copy();current+=n*24;label,side=ref['name'].rsplit('_',1);palm=palette(files.get(label,label+'_Matrices.bin'))
        if side=='R':palm=reflect@palm[mapped]@reflect
        tx=transform(palm);old_point=apply(transform(palm,True),rest+old_dp)
        if pose_targets is not None:
            selected=(pose_mask>0)&((material[:,0]<0) if side=='L' else (material[:,0]>0))
            if side=='L':
                desired=pose_targets['pose_'+ref['name']];replacement=np.linalg.solve(tx[:,:3,:3],(desired-tx[:,:3,3])[...,None])[...,0]-result;dp[selected]=replacement[selected];pose_adjustments[label]=dp-old_dp
            else:dp[selected]+=(pose_adjustments[label][mirror]*[-1,1,1])[selected]
        new_point=apply(tx,result+dp);dn[region]=inverse_normal(tx,new_point)[region]-base[region];chunks.extend((dp.tobytes(),dn.astype('<f4').tobytes()));mesh.shape_keys.key_blocks[ref['name']].data.foreach_set('co',native(result+dp).ravel());attr('r35_normal_'+ref['name'],dn);reports.append({'pose':ref['name'],'maximumPosedChangeMeters':float(np.linalg.norm(new_point-old_point,axis=1).max())})
    assert current==len(data)
    if extra_names:
        assert pose_targets is not None
        worlds=[]
        for bone in h['bones']:
            x,y,z,w=bone['rotation'];matrix=Quaternion((w,x,y,z)).to_matrix().to_4x4();matrix.translation=bone['position'];worlds.append(matrix if bone['parent']<0 else worlds[bone['parent']]@matrix)
        worlds=np.array(worlds)
        def relative(pal,parent,child):
            world=pal@worlds;a=names.index(parent);b=names.index(child);q=Matrix((np.linalg.inv(world[a,:3,:3])@world[b,:3,:3]).tolist()).to_quaternion();return [float(q.x),float(q.y),float(q.z),float(q.w)]
        for label in extra_names:
            pal=pose_targets['extra_matrix_'+label];desired=pose_targets['extra_target_'+label];source_dp=pose_targets['extra_sourceDelta_'+label];tx=transform(pal);selected=(pose_mask>0)&(material[:,0]<0);replacement=np.linalg.solve(tx[:,:3,:3],(desired-tx[:,:3,3])[...,None])[...,0]-result
            correction=np.zeros_like(source_dp);correction[selected]=replacement[selected]-source_dp[selected]
            for side in ('L','R'):
                # Keep each measured side's original weighted pose sum. Only
                # the new local shoulder correction is mirrored; mirroring
                # the complete left baseline changes the unedited right arch.
                own=material[:,0]<0 if side=='L' else material[:,0]>0;d=source_dp.copy();d[~own]=0;added=correction if side=='L' else correction[mirror]*[-1,1,1];d+=added;point=apply(tx,result+d);nd=inverse_normal(tx,point)-base;nd[~own]=0;name=label+'_'+side;ref={'name':name,'side':0 if side=='L' else 1,'rotation':relative(pal,'Chest','UpperArm_'+side),'torsoRotation':relative(pal,'Spine','Chest'),'elbowRotation':relative(pal,'UpperArm_'+side,'Forearm_'+side),'hasDelta':True};h['surfacePoses'].append(ref);chunks.extend((d.astype('<f4').tobytes(),nd.astype('<f4').tobytes()));key=body.shape_key_add(name=name);key.data.foreach_set('co',native(result+d).ravel());attr('r35_normal_'+name,nd)
        body['surface_pose_references']=json.dumps(h['surfacePoses'])
    body['r35_edit']='Local neutral shoulder root reconstruction; source skeleton, topology and actions preserved; local skin rebind and positional pose targets applied when supplied';out.mkdir(parents=True)
    blend=out/'CHR_MasterCharacter_C1B_ShoulderLine_r35.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True);h['sourceSha256']=sha(blend);encoded=json.dumps(h).encode();transport=out/'ShoulderLineCharacter.bytes';transport.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(chunks));np.savez(out/'NeutralBindData.npz',material=material,mirror=mirror,editMask=np.maximum(mask,pose_mask),neutralEditMask=mask,normalRegion=region);np.savez(out/'DraftGeometry.npz',rest=rest,result=result,posed=posed,target=target,material=material,triangles=tri,affected=changed);(out/'ShoulderTarget.npz').write_bytes(target_path.read_bytes())
    if weight_path:(out/'SkinWeights.npz').write_bytes(weight_path.read_bytes())
    if pose_path:(out/'ShoulderPoseTargets.npz').write_bytes(pose_path.read_bytes())
    report={'sourceSha256':sha(source),'targetArchiveSha256':sha(target_path),'candidateSha256':sha(blend),'transportSha256':sha(transport),'changedVertices':int(changed.sum()),'maximumNeutralChangeMeters':float(np.linalg.norm(target-posed,axis=1).max()),'facesRotatedBeyond90DegreesFromSource':int(rotated.sum()),'bonesTopologyHeadActionsPreserved':True,'skinWeightsPreserved':weight_path is None,'changedSkinWeightVertices':int(weight_mask.sum()),'originalPosePositionDeltasPreserved':pose_targets is None,'additionalReferences':extra_names,'poseTargetArchiveSha256':sha(pose_path) if pose_path else None,'mirrorError':float(np.linalg.norm(result-result[mirror]*[-1,1,1],axis=1).max()),'neutralShoulderOuterWidthBefore':float(np.ptp(posed[posed[:,1]>.54,0])),'neutralShoulderOuterWidthAfter':float(np.ptp(target[posed[:,1]>.54,0])),'poseChanges':reports,'visualApproved':False};(out/'ShoulderLineReview.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)
if __name__=='__main__':main()
