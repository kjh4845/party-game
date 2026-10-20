#!/usr/bin/env python3
"""Prepare local, four-influence shoulder-helper weights from approved r07 data.
Run with Python + NumPy, or Blender --background --python this.py -- output.npz.
"""
from pathlib import Path
import hashlib,json,struct,sys
import numpy as np
ROOT=Path(__file__).resolve().parents[2]
def smooth(x):
 x=np.clip(x,0,1);return x*x*(3-2*x)
def main():
 args=sys.argv[sys.argv.index('--')+1:] if '--' in sys.argv else sys.argv[1:]
 output=Path(args[0]).resolve()
 if output.exists():raise RuntimeError('Preserve previous weight candidates')
 source=ROOT/'.codex_tmp/lat-weight-20260909/Selected/LatCharacter.bytes';data=source.read_bytes();size=struct.unpack_from('<i',data)[0];header=json.loads(data[4:4+size]);names=[b['name'] for b in header['bones']];assert len(names)==22
 offset=4+size;n=header['meshes'][0]['vertices'];assert header['meshes'][0]['name'].endswith('SkinnedBody')
 v=np.frombuffer(data,dtype='<f4',count=n*3,offset=offset).reshape(n,3)
 ids=np.frombuffer(data,dtype='<i4',count=n*4,offset=offset+n*24).reshape(n,4);values=np.frombuffer(data,dtype='<f4',count=n*4,offset=offset+n*40).reshape(n,4)
 original=np.zeros((n,24),np.float32);np.add.at(original,(np.arange(n)[:,None],ids),values);weights=original.copy()
 gate=smooth((np.abs(v[:,0])-.08)/.05)*smooth((v[:,1]-.46)/.08)*(1-smooth((v[:,1]-.66)/.08));region=gate>0
 for side in ['L','R']:
  ua=names.index('UpperArm_'+side);ch=names.index('Chest');cl=names.index('Clavicle_'+side);sp=names.index('Spine');neck=names.index('Neck');extra=22+(side=='R')
  # These unanimated reference bones share Chest's deformation in the lab.
  # Free redundant slots before adding the helper, avoiding top-4 discontinuities.
  weights[region,ch]+=weights[region,cl];weights[region,cl]=0
  weights[region,ch]+=weights[region,neck];weights[region,neck]=0
  torso=weights[:,[ch,sp]].sum(1);total=weights[:,ua]+torso
  w=np.divide(weights[:,ua],total,out=np.zeros(n),where=total>1e-8)
  g=gate*((v[:,0]<0) if side=='L' else (v[:,0]>0));helper=(2*w*(1-w)*total*g).astype(np.float32)
  weights[:,ua]-=.5*helper
  for b in [ch,sp]:weights[:,b]-=.5*helper*np.divide(weights[:,b],torso,out=np.zeros(n),where=torso>1e-8)
  weights[:,extra]=helper
 kept=np.argsort(weights,axis=1)[:,-4:];result=np.zeros_like(weights);np.put_along_axis(result,kept,np.take_along_axis(weights,kept,axis=1),axis=1);result/=result.sum(1)[:,None];result[~region]=original[~region]
 assert np.count_nonzero(result>0,axis=1).max()<=4 and np.max(abs(result.sum(1)-1))<1e-6
 assert np.array_equal(result[v[:,1]<.46],original[v[:,1]<.46])
 names+=['ShoulderBlend_L','ShoulderBlend_R']
 # Source coordinates are mirror-symmetric within float tolerance, not bitwise.
 from itertools import product
 buckets={};cell=.0001
 for i,p in enumerate(v):buckets.setdefault(tuple(np.floor(p/cell).astype(int)),[]).append(i)
 offsets=list(product((-1,0,1),repeat=3));mirror=[]
 for p in v:
  q=p*np.array([-1,1,1],np.float32);key=np.floor(q/cell).astype(int)
  candidates=[i for delta in offsets for i in buckets.get(tuple(key+delta),())]
  distances=np.linalg.norm(v[candidates]-q,axis=1);nearest=int(np.argmin(distances));assert distances[nearest]<1e-5;mirror.append(candidates[nearest])
 mirror=np.asarray(mirror);columns=[names.index(k[:-2]+'_R' if k.endswith('_L') else k[:-2]+'_L' if k.endswith('_R') else k) for k in names]
 symmetry=float(abs(result-result[mirror][:,columns]).max());assert symmetry<1e-6
 output.parent.mkdir(parents=True,exist_ok=True);np.savez(output,weights=result,names=names)
 report={'source':str(source),'sourceSha256':hashlib.sha256(data).hexdigest(),'changedVertices':int(np.any(result!=original,axis=1).sum()),'outsideRegionUnchanged':True,'mirrorError':symmetry,'maximumInfluences':4,'minimumWeight':float(result.min()),'maximumSumError':float(np.max(abs(result.sum(1)-1)))}
 output.with_suffix('.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report))
if __name__=='__main__':main()
