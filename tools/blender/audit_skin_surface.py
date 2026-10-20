"""Audit closedness and sharp face inversions in captured skinned geometry.

python audit_skin_surface.py model.bytes capture-dir output.json
Uses geometric triangle normals, independent of lighting or shading normals.
"""
from pathlib import Path
import hashlib, json, struct, sys
import numpy as np

source, captures, output = map(Path, sys.argv[1:4])
data = source.read_bytes(); size = struct.unpack_from('<i', data)[0]
header = json.loads(data[4:4+size]); record = header['meshes'][0]
n = record['vertices']; offset = 4+size
tri = np.frombuffer(data, '<i4', record['triangleIndices'], offset+n*(24+8*record.get('influences',4))).reshape(-1,3)
edges = np.sort(np.concatenate((tri[:,[0,1]],tri[:,[1,2]],tri[:,[2,0]])),axis=1)
keys = edges[:,0].astype(np.int64)*n+edges[:,1]; order=np.argsort(keys)
unique, counts=np.unique(keys,return_counts=True)
faces=np.tile(np.arange(len(tri)),3); shared=np.flatnonzero(np.diff(keys[order])==0)
first,second=faces[order[shared]],faces[order[shared+1]]
material=np.load(source.parent/'NeutralBindData.npz')['material'] if (source.parent/'NeutralBindData.npz').exists() else None
report={'modelSha256':hashlib.sha256(data).hexdigest(),'boundaryEdges':int(np.count_nonzero(counts==1)),'nonManifoldEdges':int(np.count_nonzero(counts>2)),'poses':[]}
pose_files=sorted(captures.glob('*_Vertices.bin'))
assert pose_files, f'No captured vertices in {captures}'
for path in pose_files:
    raw=path.read_bytes();assert struct.unpack_from('<i',raw)[0]==n
    points=np.frombuffer(raw,'<f4',n*3,4).reshape(n,3).astype(float)
    p=points[tri]; normals=np.cross(p[:,1]-p[:,0],p[:,2]-p[:,0]); areas=np.linalg.norm(normals,axis=1)
    normals/=np.maximum(areas[:,None],1e-30)
    angle=np.degrees(np.arccos(np.clip(np.sum(normals[first]*normals[second],axis=1),-1,1)))
    index=int(np.argmax(angle)); ids=tri[first[index]]
    row={'pose':path.stem.removesuffix('_Vertices'),'maximumAdjacentFaceAngle':float(angle[index]),'edgesAbove60Degrees':int(np.count_nonzero(angle>60)),'worstPosition':points[ids].mean(0).tolist(),'degenerateTriangles':int(np.count_nonzero(areas<1e-12))}
    if material is not None:row['worstMaterialPosition']=material[ids].mean(0).tolist()
    report['poses'].append(row);print(row,flush=True)
output.write_text(json.dumps(report,indent=2)+'\n')
