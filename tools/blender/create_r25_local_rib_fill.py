"""Fill only r23's lateral ribs; keep its topology, rig, weights and limbs.

Blender --background --python-exit-code 1 --python this.py -- idle-matrices output-dir
Use r23 matrices from CaptureShoulderFrontAndPunchPoses. Never use r24 as input.
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
SOURCE=ROOT/'BlenderSource/Characters/C1B-RW-023-shallow-axilla'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(x):
    x=np.clip(x,0,1)
    return x*x*(3-2*x)


def main():
    args=sys.argv[sys.argv.index('--')+1:]
    matrix_path,output=map(lambda p:Path(p).resolve(),args[:2])
    if output.exists():
        raise RuntimeError('Preserve previous revisions')
    source=SOURCE/'CHR_MasterCharacter_C1B_ShallowAxilla_r23.blend'
    data=(SOURCE/'ShallowCharacter.bytes').read_bytes()
    size=struct.unpack_from('<i',data)[0]
    header=json.loads(data[4:4+size])
    assert header['sourceSha256']==sha(source)
    bpy.ops.wm.open_mainfile(filepath=str(source),load_ui=False)
    body=bpy.data.objects['CHR_C1B_R20_SkinnedBody']
    mesh=body.data
    n,o=len(mesh.vertices),4+size
    rest=np.frombuffer(data,'<f4',n*3,o).reshape(n,3).copy()
    normals=np.frombuffer(data,'<f4',n*3,o+n*12).reshape(n,3).copy()
    ids=np.frombuffer(data,'<i4',n*4,o+n*24).reshape(n,4)
    weights=np.frombuffer(data,'<f4',n*4,o+n*40).reshape(n,4)
    triangles=np.frombuffer(data,'<i4',header['meshes'][0]['triangleIndices'],o+n*56).reshape(-1,3)
    raw=matrix_path.read_bytes()
    assert struct.unpack_from('<i',raw)[0]==len(header['bones'])
    matrices=np.frombuffer(raw,'<f4',offset=4).reshape(-1,4,4)
    transforms=(matrices[ids]*weights[:,:,None,None]).sum(1).astype(np.float64)
    # Add lateral rib volume in the authored mesh, with an exact boundary
    # below the existing shoulder. Keep every original Y/Z coordinate and
    # every skin weight. The armpit/shoulder root above .56 is untouched.
    x,y=np.abs(rest[:,0]),rest[:,1]
    support=smooth((x-.035)/.09)*(1-smooth((x-.19)/.12))
    support*=smooth((y-.30)/.09)*(1-smooth((y-.46)/.10))
    support*=1-smooth((np.abs(rest[:,2])-.035)/.10)
    bind_delta=np.zeros_like(rest)
    bind_delta[:,0]=np.sign(rest[:,0])*.03*support
    tree=KDTree(n)
    for i,p in enumerate(rest):
        tree.insert(p,i)
    tree.balance()
    mirror=np.array([tree.find((-float(p[0]),float(p[1]),float(p[2])))[1] for p in rest])
    left=np.flatnonzero(rest[:,0]<-1e-6)
    bind_delta[mirror[left]]=bind_delta[left]*np.array([-1,1,1])
    bind_delta[np.abs(rest[:,0])<=1e-6]=0
    result=(rest+bind_delta).astype('<f4')
    changed=np.any(result!=rest,axis=1)
    assert np.array_equal(result[~changed],rest[~changed])
    assert not np.any(changed&(rest[:,1]>=.56))
    assert not np.any(changed&(rest[:,1]<=.30))
    assert not np.any(changed&(np.abs(rest[:,0])>=.31))
    assert np.array_equal(result[:,1:],rest[:,1:])
    def face_normals(points):
        return np.cross(points[triangles[:,1]]-points[triangles[:,0]],points[triangles[:,2]]-points[triangles[:,0]])
    assert np.all(np.einsum('ij,ij->i',face_normals(rest),face_normals(result))>0),'Local fill inverted a face'
    mesh.vertices.foreach_set('co',(result[:,[0,2,1]]*np.array([1,-1,1],np.float32)).ravel())
    mesh.update()
    recalculated=np.empty((n,3),np.float32)
    mesh.vertices.foreach_get('normal',recalculated.ravel())
    recalculated=recalculated[:,[0,2,1]]*np.array([1,1,-1],np.float32)
    affected=changed.copy()
    affected[np.unique(triangles[np.any(changed[triangles],axis=1)])]=True
    normals[affected]=recalculated[affected]
    normals[mirror[left]]=normals[left]*np.array([-1,1,1])
    output.mkdir(parents=True)
    blend=output/'CHR_MasterCharacter_C1B_LocalRibFill_r25.blend'
    body['local_rib_fill']='r23 vertices moved only on lateral ribs/axilla underside. Original topology, weights, rig and limbs retained.'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    blocks=[];current=o
    for record in header['meshes']:
        count=record['vertices'];end=current+count*56+record['triangleIndices']*4
        block=bytearray(data[current:end])
        if record['name']==body.name:
            block[:n*12]=result.tobytes()
            block[n*12:n*24]=normals.astype('<f4').tobytes()
            assert bytes(block[n*24:])==data[current+n*24:end]
        blocks.append(block);current=end
    assert current==len(data)
    header['sourceSha256']=sha(blend);header['localRibFillCandidate']=True
    encoded=json.dumps(header).encode()
    transport=output/'LocalRibCharacter.bytes'
    transport.write_bytes(struct.pack('<i',len(encoded))+encoded+b''.join(blocks))
    report={'source':str(source),'sourceSha256':sha(source),'candidateSha256':sha(blend),'transportSha256':sha(transport),
            'bodyVertices':n,'changedVertices':int(changed.sum()),'unchangedVertices':int((~changed).sum()),
            'maximumBindDisplacementMeters':float(np.linalg.norm(result-rest,axis=1).max()),
            'maximumNeutralLateralFillMeters':float(np.abs(np.einsum('nij,nj->ni',transforms[:,:3,:3],result-rest)[:,0]).max()),
            'headSkeletonWeightsTopologyPreserved':True,'mirrorMaxMeters':float(np.linalg.norm(result-result[mirror]*np.array([-1,1,1]),axis=1).max()),
            'changedBindBounds':[rest[changed].min(0).tolist(),rest[changed].max(0).tolist()],
            'visualApproved':False}
    (output/'LocalRibFillReview.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report),flush=True)


if __name__=='__main__':
    main()
