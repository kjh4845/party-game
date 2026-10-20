"""Fair the filled torso into a soft, continuous rounded surface.

Blender --background --python-exit-code 1 --python this.py -- idle-matrices output-dir
The reference is r25 in its actual Unity neutral pose. Original skeleton,
weights, topology, head and limb proportions are retained.
"""
from pathlib import Path
import hashlib
import json
import struct
import sys

import bpy
import numpy as np
from mathutils.kdtree import KDTree

ROOT=Path(__file__).resolve().parents[2]
SOURCE=ROOT/'BlenderSource/Characters/C1B-RW-025-local-rib-fill'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(x):
    x=np.clip(x,0,1)
    return x*x*x*(10+x*(-15+6*x))


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    matrix_path,output=map(lambda p:Path(p).resolve(),args[:2])
    if output.exists():raise RuntimeError('Preserve previous revisions')
    source=SOURCE/'CHR_MasterCharacter_C1B_LocalRibFill_r25.blend'
    data=(SOURCE/'LocalRibCharacter.bytes').read_bytes()
    size=struct.unpack_from('<i',data)[0];header=json.loads(data[4:4+size])
    assert header['sourceSha256']==sha(source)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    body=bpy.data.objects['CHR_C1B_R20_SkinnedBody'];mesh=body.data
    n,o=len(mesh.vertices),4+size
    rest=np.frombuffer(data,'<f4',n*3,o).reshape(n,3).copy()
    ids=np.frombuffer(data,'<i4',n*4,o+n*24).reshape(n,4)
    weights=np.frombuffer(data,'<f4',n*4,o+n*40).reshape(n,4)
    triangles=np.frombuffer(data,'<i4',header['meshes'][0]['triangleIndices'],o+n*56).reshape(-1,3)
    names=[record['name'] for record in header['bones']]
    dense=np.zeros((n,len(names)),np.float64)
    np.add.at(dense,(np.arange(n)[:,None],ids),weights)
    matrices=np.frombuffer(matrix_path.read_bytes(),'<f4',offset=4).reshape(-1,4,4)
    transforms=(matrices[ids]*weights[:,:,None,None]).sum(1).astype(np.float64)
    inverse_root=np.linalg.inv(matrices[0])
    posed=np.einsum('nij,nj->ni',transforms[:,:3,:3],rest)+transforms[:,:3,3]
    posed=posed@inverse_root[:3,:3].T+inverse_root[:3,3]
    x,y,z=posed.T
    torso=[names.index(name) for name in ('Pelvis','Spine','Chest')]
    owner=smooth(dense[:,torso].sum(1)/.35)
    mask=owner*smooth((y-.10)/.22)*(1-smooth((y-.43)/.13))
    mask*=1-smooth((np.abs(rest[:,0])-.22)/.065)
    mask*=1-smooth((rest[:,1]-.51)/.055)
    # A broad belly envelope joins the filled ribs to the abdomen. The change
    # fades over the whole torso instead of forming a local lateral pad.
    rx=np.maximum(.184-.38*(y-.30)**2,.04)
    rz=np.maximum(.155-.25*(y-.33)**2,.04)
    scale=1/np.sqrt(np.maximum((x/rx)**2+(z/rz)**2,1e-12))
    growth=np.maximum(0,scale-1)
    growth*=smooth(growth/.06)
    offset=np.zeros_like(posed)
    offset[:,0]=x*growth*mask;offset[:,2]=z*growth*mask
    length=np.linalg.norm(offset,axis=1)
    upper=smooth((y-.36)/.12)
    limit=.014*(1-upper)+.002*upper
    gain=np.ones(n);active=length>1e-12
    gain[active]=limit[active]*np.tanh(length[active]/limit[active])/length[active]
    target=posed+offset*gain[:,None]
    edges=np.empty((len(mesh.edges),2),np.int32);mesh.edges.foreach_get('vertices',edges.ravel())
    src=np.concatenate((edges[:,0],edges[:,1]));dst=np.concatenate((edges[:,1],edges[:,0]))
    degree=np.maximum(np.bincount(src,minlength=n),1)[:,None]
    fair=owner*smooth((y-.12)/.20)*(1-smooth((y-.48)/.095))
    fair*=1-smooth((np.abs(rest[:,0])-.22)/.065)
    fair*=1-smooth((rest[:,1]-.51)/.055)
    def lap(points):
        mean=np.column_stack([np.bincount(src,weights=points[dst,axis],minlength=n) for axis in range(3)])/degree
        return mean-points
    # Paired fairing steps remove curvature ripples without repeatedly
    # shrinking the inflated form, unlike an ordinary repeated smooth brush.
    for _ in range(140):
        target+=.48*lap(target)*fair[:,None]
        target-=.50*lap(target)*fair[:,None]
    delta=target-posed
    allowed=(mask>0)|(fair>0)
    delta[~allowed]=0
    raw_delta=delta@matrices[0,:3,:3].T
    bind_delta=np.linalg.solve(transforms[:,:3,:3],raw_delta[...,None])[...,0]
    tree=KDTree(n)
    for i,p in enumerate(rest):tree.insert(p,i)
    tree.balance()
    mirror=np.array([tree.find((-float(p[0]),float(p[1]),float(p[2])))[1] for p in rest])
    left=np.flatnonzero(rest[:,0]<-1e-6);center=np.abs(rest[:,0])<=1e-6
    bind_delta[mirror[left]]=bind_delta[left]*np.array([-1,1,1])
    bind_delta[center,0]=0
    result=(rest+bind_delta).astype('<f4')
    assert np.array_equal(result[~allowed],rest[~allowed])
    mesh.vertices.foreach_set('co',(result[:,[0,2,1]]*np.array([1,-1,1],np.float32)).ravel())
    mesh.update()
    # Author normals from the evaluated smooth surface, not from the inverse
    # bind-pose distortion. Store the same custom normals in Blender and Unity.
    evaluated=np.einsum('nij,nj->ni',transforms[:,:3,:3],result)+transforms[:,:3,3]
    face_normals=np.cross(evaluated[triangles[:,1]]-evaluated[triangles[:,0]],evaluated[triangles[:,2]]-evaluated[triangles[:,0]])
    posed_normals=np.zeros((n,3),np.float64)
    for corner in range(3):np.add.at(posed_normals,triangles[:,corner],face_normals)
    posed_normals/=np.maximum(np.linalg.norm(posed_normals,axis=1)[:,None],1e-12)
    normals=np.linalg.solve(transforms[:,:3,:3],posed_normals[...,None])[...,0]
    normals/=np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-12)
    normals[mirror[left]]=normals[left]*np.array([-1,1,1])
    normals[center,0]=0
    normals/=np.maximum(np.linalg.norm(normals,axis=1)[:,None],1e-12)
    mesh.normals_split_custom_set_from_vertices((normals[:,[0,2,1]]*np.array([1,-1,1])).tolist())
    # Keep full-precision authoring data as well as Blender's corner normals.
    base_attribute=mesh.attributes.new(name='r26_normal_base',type='FLOAT_VECTOR',domain='POINT')
    base_attribute.data.foreach_set('vector',(normals[:,[0,2,1]]*np.array([1,-1,1])).astype(np.float32).ravel())
    output.mkdir(parents=True)
    blend=output/'CHR_MasterCharacter_C1B_SmoothBody_r26.blend'
    body['smooth_body']='Fair continuous rib-to-belly envelope and evaluated-surface custom normals; original head, limbs, topology, weights and rig retained.'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    blocks=[];current=o
    for record in header['meshes']:
        count=record['vertices'];end=current+count*56+record['triangleIndices']*4
        block=bytearray(data[current:end])
        if record['name']==body.name:
            block[:n*12]=result.tobytes();block[n*12:n*24]=normals.astype('<f4').tobytes()
            assert bytes(block[n*24:])==data[current+n*24:end]
        blocks.append(block);current=end
    assert current==len(data)
    header['sourceSha256']=sha(blend);header['smoothBodyCandidate']=True
    encoded=json.dumps(header).encode();transport=output/'SmoothCharacter.bytes'
    transport.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(blocks))
    report={'source':str(source),'sourceSha256':sha(source),'candidateSha256':sha(blend),'transportSha256':sha(transport),
            'bodyVertices':n,'editedVertices':int(np.any(result!=rest,axis=1).sum()),
            'headSkeletonTopologyWeightsPreserved':True,'outsideTorsoCoordinatesPreserved':True,
            'customNormalsFromNeutralSurface':True,'maximumNeutralShapeChangeMeters':float(np.linalg.norm(delta,axis=1).max()),
            'mirrorMaxMeters':float(np.linalg.norm(result-result[mirror]*np.array([-1,1,1]),axis=1).max()),'visualApproved':False}
    (output/'SmoothBodyReview.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report),flush=True)


if __name__=='__main__':main()
