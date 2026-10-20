"""Fair torso and crotch on a coarse connected cage, then transfer by material topology.

Blender --background --python-exit-code 1 --python this.py -- output-directory
Keeps r33 shoulder outline, arm shafts, skeleton, weights and accepted actions.
"""
from pathlib import Path
import json,struct,sys
import bpy,numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.geometry import barycentric_transform
from mathutils.kdtree import KDTree

sys.path.insert(0,str(Path(__file__).resolve().parent))
from create_r27_surface_refine import normals,sha,smooth

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'BlenderSource/Characters/C1B-RW-033-close-arm-gap'
POSES=ROOT/'BlenderSource/Characters/C1B-RW-028-cylindrical-upper-arms/ReferencePoses'

def main():
    out=Path(sys.argv[sys.argv.index('--')+1]).resolve();assert not out.exists()
    data=(SOURCE/'CloseGapCharacter.bytes').read_bytes();size=struct.unpack_from('<i',data)[0];h=json.loads(data[4:4+size]);o=4+size;r=h['meshes'][0];n=r['vertices']
    source=SOURCE/'CHR_MasterCharacter_C1B_CloseArmGap_r33.blend';assert sha(source)==h['sourceSha256']
    rest=np.frombuffer(data,'<f4',n*3,o).reshape(n,3).copy();old_normal=np.frombuffer(data,'<f4',n*3,o+n*12).reshape(n,3).copy();ids=np.frombuffer(data,'<i4',n*4,o+n*24).reshape(n,4);weights=np.frombuffer(data,'<f4',n*4,o+n*40).reshape(n,4);tri=np.frombuffer(data,'<i4',r['triangleIndices'],o+n*56).reshape(-1,3)
    aux=np.load(SOURCE/'NeutralBindData.npz');material=aux['material'];mirror=aux['mirror'];names=[b['name'] for b in h['bones']]
    raw=np.frombuffer((ROOT/'.codex_tmp/close-arm-gap-20261010/CandidateC/UnityCaptures/Idle_Matrices.bin').read_bytes(),'<f4',offset=4).reshape(-1,4,4).astype(float);pal=np.linalg.inv(raw[0])@raw;t=(pal[ids]*weights[:,:,None,None]).sum(1);posed=np.einsum('nij,nj->ni',t[:,:3,:3],rest)+t[:,:3,3]
    allowed_bones=[i for i,k in enumerate(names) if not k.startswith(('Clavicle_','UpperArm_','Forearm_','HandLogical_','ShoulderBlend_'))]
    owner=(weights*np.isin(ids,allowed_bones)).sum(1)
    def gate(p,mat,ownership):
        return smooth((p[:,1]-.035)/.065)*(1-smooth((p[:,1]-.49)/.08))*(1-smooth((abs(mat[:,0])-.165)/.085))*smooth((ownership-.80)/.19)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False);body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data
    # Position and material-reference attributes travel through the same
    # unsubdivision/subdivision. Querying the reference avoids swapping close
    # flank/arm or left/right crotch surfaces in posed space.
    guide_mesh=bpy.data.meshes.new('R34_SurfaceControlCage');guide_mesh.from_pydata(posed.tolist(),[],[list(f.vertices) for f in mesh.polygons]);guide_mesh.update()
    a=guide_mesh.attributes.new(name='r34_material',type='FLOAT_VECTOR',domain='POINT');a.data.foreach_set('vector',material.astype(np.float32).ravel())
    a=guide_mesh.attributes.new(name='r34_owner',type='FLOAT',domain='POINT');a.data.foreach_set('value',owner.astype(np.float32))
    guide=bpy.data.objects.new('R34_NeutralSurfaceGuide',guide_mesh);bpy.context.collection.objects.link(guide);bpy.ops.object.select_all(action='DESELECT');guide.select_set(True);bpy.context.view_layer.objects.active=guide
    reduce=guide.modifiers.new('Recover connected control cage','DECIMATE');reduce.decimate_type='UNSUBDIV';reduce.iterations=6;bpy.ops.object.modifier_apply(modifier=reduce.name)
    count=len(guide.data.vertices);p=np.empty((count,3),np.float32);guide.data.vertices.foreach_get('co',p.ravel());p=p.astype(float);m=np.empty((count,3),np.float32);guide.data.attributes['r34_material'].data.foreach_get('vector',m.ravel());ownership=np.empty(count,np.float32);guide.data.attributes['r34_owner'].data.foreach_get('value',ownership)
    guide.data.calc_loop_triangles();coarse_tri=np.array([f.vertices for f in guide.data.loop_triangles],np.int32)
    ref_normal=normals(m,coarse_tri);f=m[coarse_tri];area=np.linalg.norm(np.cross(f[:,1]-f[:,0],f[:,2]-f[:,0]),axis=1);mass=np.zeros(count)
    for k in range(3):np.add.at(mass,coarse_tri[:,k],area/6)
    search=KDTree(count)
    for i,point in enumerate(m):search.insert(point,i)
    search.balance();g=gate(p,m,ownership);fits=[]
    # Use a physical-radius, area-weighted quadratic fit. Uniform graph steps
    # on this recovered cage smooth dense and sparse patches differently and
    # leave bands; a fixed geometric scale removes that density dependence.
    for i in range(count):
        if g[i]==0:fits.append(None);continue
        near=search.find_range(Vector(m[i]),.075);neighbours=np.array([j for _,j,_ in near],np.int32);distance=np.array([d for _,_,d in near]);keep=(ref_normal[neighbours]@ref_normal[i])>.20;neighbours=neighbours[keep];distance=distance[keep]
        if len(neighbours)<8:fits.append(None);continue
        normal=ref_normal[i];axis=np.eye(3)[np.argmin(abs(normal))];u=np.cross(normal,axis);u/=np.linalg.norm(u);v=np.cross(normal,u);local=m[neighbours]-m[i];a=local@u/.05;b=local@v/.05;design=np.column_stack([np.ones(len(a)),a,b,a*a,a*b,b*b]);weight=mass[neighbours]*np.exp(-(distance/.045)**2);weighted=design*np.sqrt(weight[:,None])
        if np.linalg.matrix_rank(weighted)<6:fits.append(None);continue
        kernel=np.linalg.pinv(weighted)[0]*np.sqrt(weight);fits.append((neighbours,kernel))
    for _ in range(3):
        updated=p.copy()
        for i,fit in enumerate(fits):
            if fit is not None:
                neighbours,kernel=fit;updated[i]=p[i]*(1-g[i])+(kernel@p[neighbours])*g[i]
        p=updated
    guide.data.vertices.foreach_set('co',p.astype(np.float32).ravel());guide.data.update()
    subdiv=guide.modifiers.new('Continuous rounded limit surface','SUBSURF');subdiv.levels=subdiv.render_levels=3
    deps=bpy.context.evaluated_depsgraph_get();evaluated=guide.evaluated_get(deps);limit=evaluated.to_mesh();nv=len(limit.vertices);q=np.empty((nv,3),np.float32);limit.vertices.foreach_get('co',q.ravel());ref=np.empty((nv,3),np.float32);limit.attributes['r34_material'].data.foreach_get('vector',ref.ravel());limit.calc_loop_triangles();faces=np.array([f.vertices for f in limit.loop_triangles],np.int32)
    tree=BVHTree.FromPolygons(ref.tolist(),faces.tolist(),all_triangles=True)
    result_guide=posed.copy();mask=gate(posed,material,owner);active=np.flatnonzero(mask>0)
    for i in active:
        nearest,_,face,_=tree.find_nearest(Vector(material[i]));a,b,c=faces[face];result_guide[i]=barycentric_transform(nearest,Vector(ref[a]),Vector(ref[b]),Vector(ref[c]),Vector(q[a]),Vector(q[b]),Vector(q[c]))
    delta=(result_guide-posed)*mask[:,None]
    # Transfer front/back depth from the smooth posed limit surface at the
    # original x/y coordinates. Reusing material-space barycentric samples
    # after pinning height leaves tiny texture-like wrinkles in grazing light.
    posed_surface=BVHTree.FromPolygons(q.tolist(),faces.tolist(),all_triangles=True)
    original_normal=normals(posed,tri)
    ray_gate=smooth((posed[:,1]-.12)/.06)*(1-smooth((posed[:,1]-.38)/.06))*smooth((abs(original_normal[:,2])-.20)/.50)*mask
    for i in np.flatnonzero(ray_gate>0):
        sign=1. if posed[i,2]>=0 else -1.
        hit,_,_,_=posed_surface.ray_cast(Vector((posed[i,0],posed[i,1],sign*.35)),Vector((0,0,-sign)),1.)
        if hit is not None:
            gain=ray_gate[i];delta[i,:2]*=1-gain;delta[i,2]=delta[i,2]*(1-gain)+(hit.z-posed[i,2])*gain
    # Torso weights are authored along height. Preserve their longitudinal
    # parameter while fairing width/depth; tangential height drift can appear
    # smooth at rest and fold during waist/chest flexion.
    delta[:,1]*=1-smooth((posed[:,1]-.13)/.07)
    # Keep the two rounded leg ends at the original ground level while fairing
    # their whole continuous surface, avoiding a smoothing boundary across legs.
    feet=posed[:,1]<.06
    floor_shift=float((posed+delta)[feet,1].min()-posed[feet,1].min())
    delta[:,1]-=floor_shift*(1-smooth((posed[:,1]-.07)/.12))
    # Retain the flank x/y envelope that closes the r33 arm gap. Fair its depth
    # into the broad torso without widening shoulders or reopening that gap.
    keep_gap=smooth((abs(posed[:,0])-.12)/.03)*smooth((posed[:,1]-.40)/.04)
    delta[:,0]*=(1-keep_gap)
    limit_distance=.014+.008*(1-smooth((posed[:,1]-.20)/.08));length=np.linalg.norm(delta,axis=1);delta*=np.minimum(1,limit_distance/np.maximum(length,1e-12))[:,None]
    bind_delta=np.linalg.solve(t[:,:3,:3],delta[...,None])[...,0];left=np.flatnonzero(rest[:,0]<-1e-6);center=abs(rest[:,0])<=1e-6;bind_delta[mirror[left]]=bind_delta[left]*[-1,1,1];bind_delta[center,0]=0;result=(rest+bind_delta).astype('<f4')
    target=np.einsum('nij,nj->ni',t[:,:3,:3],result)+t[:,:3,3];changed=np.any(result!=rest,axis=1);region=changed.copy();region[np.unique(tri[np.any(changed[tri],axis=1)])]=True
    protected=(posed[:,1]>=.57)|(posed[:,1]<=.035)|(abs(material[:,0])>=.25)|(owner<=.80);assert np.array_equal(result[protected],rest[protected])
    before=np.cross(posed[tri[:,1]]-posed[tri[:,0]],posed[tri[:,2]]-posed[tri[:,0]]);after=np.cross(target[tri[:,1]]-target[tri[:,0]],target[tri[:,2]]-target[tri[:,0]]);rotated=np.sum(before*after,axis=1)<=0
    print('Faces rotated beyond 90 degrees:',int(rotated.sum()),'examples:',material[tri[rotated]].mean(1)[:8].tolist(),flush=True)
    np.savez(out.parent/(out.name+'-DraftGeometry.npz'),rest=rest,result=result,posed=posed,target=target,material=material,triangles=tri,affected=changed)
    assert not rotated.any(),'Neutral face reversal'
    def normal_inverse(transform,points):
        nn=np.linalg.solve(transform[:,:3,:3],normals(points,tri)[...,None])[...,0];return nn/np.maximum(np.linalg.norm(nn,axis=1)[:,None],1e-12)
    base=old_normal.copy();base[region]=normal_inverse(t,target)[region];base[mirror[left]]=base[left]*[-1,1,1]
    native=lambda a:(a[:,[0,2,1]]*[1,-1,1]).astype(np.float32)
    def attr(label,values):
        a=mesh.attributes.get(label) or mesh.attributes.new(name=label,type='FLOAT_VECTOR',domain='POINT');a.data.foreach_set('vector',native(values).ravel())
    mesh.vertices.foreach_set('co',native(result).ravel());mesh.shape_keys.key_blocks['Basis'].data.foreach_set('co',native(result).ravel());mesh.normals_split_custom_set_from_vertices(native(base).tolist());attr('r34_normal_base',base)
    chunks=[];current=o
    for item in h['meshes']:
        end=current+item['vertices']*56+item['triangleIndices']*4;block=bytearray(data[current:end])
        if item['name']==body.name:block[:n*12]=result.tobytes();block[n*12:n*24]=base.astype('<f4').tobytes()
        chunks.append(block);current=end
    mapped=np.array([names.index(name[:-2]+'_R' if name.endswith('_L') else name[:-2]+'_L' if name.endswith('_R') else name) for name in names]);reflect=np.diag([-1.,1,1,1]);files={'Raise45':'Raised_45_Matrices.bin','Raise90':'Raised_90_Matrices.bin','Prepare':'Punch_02_Matrices.bin','Accelerate':'Punch_05_Matrices.bin','Strike':'Punch_08_Matrices.bin','FollowThrough':'Punch_12_Matrices.bin','Recover':'Punch_18_Matrices.bin'}
    for pose in h['surfacePoses']:
        if not pose['hasDelta']:continue
        dp=np.frombuffer(data,'<f4',n*3,current).reshape(n,3);dn=np.frombuffer(data,'<f4',n*3,current+n*12).reshape(n,3).copy();current+=n*24
        label,side=pose['name'].rsplit('_',1);palm=np.frombuffer((POSES/files.get(label,label+'_Matrices.bin')).read_bytes(),'<f4',offset=4).reshape(-1,4,4).astype(float);palm=np.linalg.inv(palm[0])@palm
        if side=='R':palm=reflect@palm[mapped]@reflect
        tx=(palm[ids]*weights[:,:,None,None]).sum(1);point=np.einsum('nij,nj->ni',tx[:,:3,:3],result+dp)+tx[:,:3,3];dn[region]=normal_inverse(tx,point)[region]-base[region];chunks.extend([dp.tobytes(),dn.astype('<f4').tobytes()]);mesh.shape_keys.key_blocks[pose['name']].data.foreach_set('co',native(result+dp).ravel());attr('r34_normal_'+pose['name'],dn)
    assert current==len(data)
    evaluated.to_mesh_clear();guide.modifiers.clear();guide.data.vertices.foreach_set('co',native(p).ravel());guide.hide_render=True;guide.hide_set(True);guide['purpose']='Coarse neutral torso and crotch fairing cage; not a gameplay mesh';body['r34_edit']='Continuous torso and crotch fairing; r33 shoulders, arm shafts, rig, weights and actions retained'
    out.mkdir(parents=True);blend=out/'CHR_MasterCharacter_C1B_FairBody_r34.blend';bpy.ops.wm.save_as_mainfile(filepath=str(blend),compress=True);h['sourceSha256']=sha(blend);encoded=json.dumps(h).encode();transport=out/'FairBodyCharacter.bytes';transport.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(chunks));np.savez(out/'NeutralBindData.npz',material=material,mirror=mirror)
    report={'sourceSha256':sha(source),'candidateSha256':sha(blend),'transportSha256':sha(transport),'controlCageVertices':count,'changedVertices':int(changed.sum()),'maximumNeutralChangeMeters':float(np.linalg.norm(target-posed,axis=1).max()),'facesRotatedBeyond90DegreesFromSource':int(rotated.sum()),'shouldersArmShaftsRigWeightsTopologyHeadPreserved':True,'mirrorError':float(np.linalg.norm(result-result[mirror]*[-1,1,1],axis=1).max()),'visualApproved':False};(out/'FairBodyReview.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report),flush=True)

if __name__=='__main__':main()
