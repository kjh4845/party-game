"""Explicit broad shoulder annulus; neutral geometry prototype, not production skin.
Run with Blender --background --python script -- [stage path].
The edited surface is a parametric Hermite loft between chest/back and full upper-arm
boundary loops. No old interior point is projected into the replacement patch.
"""
from pathlib import Path
import sys, json
import bpy,numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree
ROOT=Path(__file__).resolve().parents[2]
OUT=Path(sys.argv[sys.argv.index('--')+1]) if '--' in sys.argv else ROOT/'.codex_tmp/shoulder-rebuild-20261014/Geometry/ExplicitLoftA'
OUT.mkdir(parents=True,exist_ok=True)
d=np.load(ROOT/'.codex_tmp/shoulder-line-20261013/Alternative/SparseConstrainedThin_biharmonic_10.npz')
pos=d['posed'];material=d['material'];tri=d['triangles'];n=len(pos)
mirror=np.load(ROOT/'BlenderSource/Characters/C1B-RW-034-fair-body/NeutralBindData.npz')['mirror']
original_n=n
source_cut_edges=[];source_cut_weights=[]
# Cut the original triangles at the continuous domain boundary instead of
# retaining their staircase edges. New cut vertices lie on the original face.
domain=(material*np.array([1,1,1])-np.array([.23,.58,0]))/np.array([.19,.17,.17])
positive_f=np.maximum((domain*domain).sum(1)-1,(material[:,0]-.305)/.19)
negative_f=positive_f[mirror]
fvalue=np.minimum(positive_f,negative_f)
cut_edges={};new_pos=[];new_mat=[];split_faces=[]
def cut_vertex(a,b):
 edge=tuple(sorted((int(a),int(b))))
 if edge in cut_edges:return cut_edges[edge]
 a,b=edge;t=float(fvalue[a]/(fvalue[a]-fvalue[b]));idx=original_n+len(new_pos);cut_edges[edge]=idx
 new_pos.append(pos[a]*(1-t)+pos[b]*t);new_mat.append(material[a]*(1-t)+material[b]*t);source_cut_edges.append(edge);source_cut_weights.append([1-t,t]);return idx
def clipped(face,keep_inside):
 out=[]
 for a,b in zip(face,np.roll(face,-1)):
  ia=(fvalue[a]<=0)==keep_inside;ib=(fvalue[b]<=0)==keep_inside
  if ia:out.append(int(a))
  if ia!=ib:out.append(cut_vertex(a,b))
 return out
for face in tri:
 signs=fvalue[face]<=0
 if signs.all() or not signs.any():split_faces.append(face.tolist());continue
 for keep_inside in (True,False):
  polygon=clipped(face,keep_inside)
  split_faces.extend([[polygon[0],polygon[i],polygon[i+1]] for i in range(1,len(polygon)-1)])
new_mirror=[];unmatched=[]
for edge in source_cut_edges:
 mate=tuple(sorted((int(mirror[edge[0]]),int(mirror[edge[1]]))))
 if mate not in cut_edges:unmatched.append((edge,mate))
 else:new_mirror.append(cut_edges[mate])
assert not unmatched,('Mirror cut edges unmatched',len(unmatched),unmatched[:3])
pos=np.r_[pos,np.array(new_pos)];material=np.r_[material,np.array(new_mat)];tri=np.array(split_faces,dtype=np.int32);mirror=np.r_[mirror,np.array(new_mirror)];n=len(pos)
mask=np.r_[positive_f<=0,np.array(new_mat)[:,0]>0]
fmask=mask[tri].all(1);ft=tri[fmask]
edges=np.sort(np.concatenate([ft[:,[0,1]],ft[:,[1,2]],ft[:,[2,0]]]),1);edges,count=np.unique(edges,axis=0,return_counts=True);edges=edges[count==1]
adj={}
for a,b in edges:adj.setdefault(a,[]).append(b);adj.setdefault(b,[]).append(a)
assert all(len(v)==2 for v in adj.values())
unseen=set(adj);loops=[]
while unseen:
 start=min(unseen);ids=[start];cur=start;prev=-1
 while True:
  nxt=next(k for k in adj[cur] if k!=prev)
  if nxt==start:break
  ids.append(nxt);prev,cur=cur,nxt
 unseen-=set(ids);loops.append(np.array(ids))
assert len(loops)==2
loops.sort(key=len,reverse=True)
def parameterize(ids):
 # Neck-side crest and innermost arm/torso attachment are semantic landmarks.
 # The thick lower torso makes max-Z and min-Y invalid angular landmarks.
 top=int(np.argmax(material[ids,1]));ids=np.roll(ids,-top)
 if material[ids[1],2]>material[ids[-1],2]:ids=np.r_[ids[:1],ids[:0:-1]]
 if len(ids)>300:
  bottom=int(np.argmax(material[ids,0]))
 else:bottom=int(np.argmin(material[ids,1]))
 a=pos[np.r_[ids,ids[:1]]];dist=np.r_[0,np.cumsum(np.linalg.norm(np.diff(a,axis=0),axis=1))]
 theta=np.empty(len(ids));theta[:bottom]=(np.pi/2)+np.pi*dist[:bottom]/dist[bottom];theta[bottom:]=3*np.pi/2+np.pi*(dist[bottom:-1]-dist[bottom])/(dist[-1]-dist[bottom]);theta%=2*np.pi
 order=np.argsort(theta);return ids[order],theta[order]
body,tb=parameterize(loops[0]);arm,ta=parameterize(loops[1])
face_norm=np.cross(pos[tri[:,1]]-pos[tri[:,0]],pos[tri[:,2]]-pos[tri[:,0]])
normal=np.zeros_like(pos)
for j in range(3):np.add.at(normal,tri[:,j],face_norm)
normal/=np.maximum(np.linalg.norm(normal,axis=1)[:,None],1e-14)
inside=np.zeros_like(pos);ct=np.zeros(n);cent=pos[ft].mean(1)
for j in range(3):np.add.at(inside,ft[:,j],cent);np.add.at(ct,ft[:,j],1)
inside=inside/np.maximum(ct[:,None],1)-pos;inside-=(inside*normal).sum(1)[:,None]*normal
for ids in [body,arm]:
 tangent=pos[np.roll(ids,-1)]-pos[np.roll(ids,1)];tangent/=np.linalg.norm(tangent,axis=1)[:,None];inside[ids]-=(inside[ids]*tangent).sum(1)[:,None]*tangent
inside/=np.maximum(np.linalg.norm(inside,axis=1)[:,None],1e-14)
def samples(ids,th,at,values):return np.column_stack([np.interp(at,th,values[ids,j],period=2*np.pi) for j in range(3)])
def source_map(ids,th,at):
 hi=np.searchsorted(th,at,side='right');lo=(hi-1)%len(ids);low=th[lo]-np.where(hi==0,2*np.pi,0);high=th[hi%len(ids)]+np.where(hi==len(ids),2*np.pi,0);alpha=(at-low)/(high-low);return np.column_stack([ids[lo],ids[hi%len(ids)]]),np.column_stack([1-alpha,alpha])
# 24 angular controls; intermediate points evaluated from their periodic cubic
# interpolation so patch shape complexity remains explicit and bounded.
count=24;angles=np.arange(count)/count*2*np.pi
p0=samples(body,tb,angles,pos);p1=samples(arm,ta,angles,pos)
d0=samples(body,tb,angles,inside);d1=-samples(arm,ta,angles,inside)
chord=np.linalg.norm(p1-p0,axis=1)
d0/=np.maximum(np.linalg.norm(d0,axis=1)[:,None],1e-12);d1/=np.maximum(np.linalg.norm(d1,axis=1)[:,None],1e-12)
# The inner meridian is a raised U, not the short straight chord between the
# two nearby lower attachment points. Its tangents need enough arc length.
inside_gain=np.maximum(-np.sin(angles),0)**4
base_length=chord*.8
required_length=8*(.548-(p0[:,1]+p1[:,1])*.5)/np.maximum(d0[:,1]-d1[:,1],.1)
length=base_length*(1-inside_gain)+np.clip(required_length,.10,.65)*inside_gain
d0*=length[:,None];d1*=length[:,None]
outer_x=np.linspace(material[body,0].min(),material[arm,0].mean(),65);outer_points=[];actual_x=[]
for x in outer_x:
 eligible=np.flatnonzero((abs(material[:,0]-x)<.003)&(abs(material[:,2])<.003))
 j=eligible[np.argmax(material[eligible,1])];outer_points.append(pos[j]);actual_x.append(material[j,0])
outer_points=np.array(outer_points);actual_x=np.array(actual_x);fit=np.column_stack([np.polyval(np.polyfit(actual_x,outer_points[:,j],5),outer_x) for j in range(3)]);uu=np.linspace(0,1,len(fit))[:,None];fit+=(p0[6]-fit[0])*(2*uu**3-3*uu**2+1)+(p1[6]-fit[-1])*(-2*uu**3+3*uu**2);outer_points=fit
# Avoid overshoot outside original total shoulder envelope while keeping neck slope.
def periodic_cubic(values,at):
 x=at/(2*np.pi)*len(values);i=np.floor(x).astype(int);t=(x-i)[:,None];a,b,c,e=[values[(i+k)%len(values)] for k in [-1,0,1,2]]
 return .5*((2*b)+(-a+c)*t+(2*a-5*b+4*c-e)*t*t+(-a+3*b-3*c+e)*t*t*t)
middle_u=.55
outer_mid=np.array([np.interp(middle_u,np.linspace(0,1,len(outer_points)),outer_points[:,j]) for j in range(3)])
inner_mid=np.array([.151,.548,0.])
mid_center=(outer_mid+inner_mid)*.5;mid_axis=(outer_mid-inner_mid)*.5
pm=mid_center+np.sin(angles)[:,None]*mid_axis+np.cos(angles)[:,None]*np.array([0,0,.070])
# Endpoint derivatives maintain original local boundary tangent directions.
d0/=np.linalg.norm(d0,axis=1)[:,None];d1/=np.linalg.norm(d1,axis=1)[:,None]
d0*=np.linalg.norm(pm-p0,axis=1)[:,None]/middle_u*.8;d1*=np.linalg.norm(p1-pm,axis=1)[:,None]/(1-middle_u)*.8
dm=(p1-p0)*.8
dm=dm*(1-inside_gain[:,None])+np.array([.012,0.,0.])*inside_gain[:,None]
def hermite(a,b,da,db,t):return (2*t**3-3*t**2+1)*a+(t**3-2*t**2+t)*da+(-2*t**3+3*t**2)*b+(t**3-t**2)*db
def surface(at,u):
 a,b,c,e,m,md=[periodic_cubic(v,at) for v in [p0,d0,p1,d1,pm,dm]]
 if u<=middle_u:return hermite(a,m,b*middle_u,md*middle_u,u/middle_u)
 return hermite(m,c,md*(1-middle_u),e*(1-middle_u),(u-middle_u)/(1-middle_u))
# Exact boundary collars remove interpolation drift at both source loops.
def exact_surface(at,u):
 q=surface(at,u);sa=samples(body,tb,at,pos)-periodic_cubic(p0,at);sb=samples(arm,ta,at,pos)-periodic_cubic(p1,at)
 q+=sa*(2*u**3-3*u**2+1)+sb*(-2*u**3+3*u**2)
 # The boundary itself follows fine mesh edges. Use a smooth circumferential
 # tangent and the true surface normal, not normalized tiny triangle means.
 n0=samples(body,tb,at,normal);n1=samples(arm,ta,at,normal)
 t0=periodic_cubic(p0,(at+.001)%(2*np.pi))-periodic_cubic(p0,(at-.001)%(2*np.pi))
 t1=periodic_cubic(p1,(at+.001)%(2*np.pi))-periodic_cubic(p1,(at-.001)%(2*np.pi))
 e0=np.cross(n0,t0);e1=np.cross(n1,t1)
 e0*=np.sign((e0*periodic_cubic(d0,at)).sum(1))[:,None];e1*=np.sign((e1*periodic_cubic(d1,at)).sum(1))[:,None]
 e0/=np.maximum(np.linalg.norm(e0,axis=1)[:,None],1e-12);e1/=np.maximum(np.linalg.norm(e1,axis=1)[:,None],1e-12)
 e0*=periodic_cubic(np.repeat(np.linalg.norm(d0,axis=1)[:,None],3,axis=1),at);e1*=periodic_cubic(np.repeat(np.linalg.norm(d1,axis=1)[:,None],3,axis=1),at)
 if u<=middle_u:
  t=u/middle_u;q+=(e0-periodic_cubic(d0,at))*middle_u*(t**3-2*t**2+t)
 else:
  t=(u-middle_u)/(1-middle_u);q+=(e1-periodic_cubic(d1,at))*(1-middle_u)*(t**3-t**2)
 # Explicit outer meridian preserves the accepted neck-to-arm outline.
 outer=np.array([np.interp(u,np.linspace(0,1,len(outer_points)),outer_points[:,j]) for j in range(3)])
 old_outer=surface(np.array([np.pi/2]),u)[0]
 q+=(outer-old_outer)*np.maximum(np.sin(at),0)[:,None]**4
 return q
vertices=[v for v in pos];rings=[(body,tb)];new=[];parameters=[];maps=[];mweights=[]
for u in np.linspace(0,1,33)[1:-1]:
 at=tb.copy() if u<.07 else ta.copy() if u>.93 else np.arange(96)/96*2*np.pi;ids=np.arange(len(vertices),len(vertices)+len(at));vertices.extend(exact_surface(at,float(u)));rings.append((ids,at));new.extend(ids);parameters.extend(np.column_stack([np.full(len(at),u),at]));ib,wb=source_map(body,tb,at);ia,wa=source_map(arm,ta,at);maps.extend(np.c_[ib,ia]);mweights.extend(np.c_[wb*(1-u),wa*u])
rings.append((arm,ta))
def bridge(a,at,b,bt):
 out=[];i=j=0
 while i<len(a) or j<len(b):
  na=at[(i+1)%len(a)]+(2*np.pi if i+1>=len(a) else 0);nb=bt[(j+1)%len(b)]+(2*np.pi if j+1>=len(b) else 0)
  if i<len(a) and (j>=len(b) or na<nb):out.append([a[i%len(a)],a[(i+1)%len(a)],b[j%len(b)]]);i+=1
  else:out.append([a[i%len(a)],b[(j+1)%len(b)],b[j%len(b)]]);j+=1
 return out
patch=np.array([f for (a,at),(b,bt) in zip(rings[:-1],rings[1:]) for f in bridge(a,at,b,bt)])
# Match patch boundary orientation to removed mesh, no nearest-surface inference.
removed_orient={tuple(sorted((a,b))):(a,b) for f in ft for a,b in zip(f,np.roll(f,-1))}
a,b=body[:2];match=[f for f in patch if a in f and b in f][0];pa=[(int(x),int(y)) for x,y in zip(match,np.roll(match,-1)) if set((x,y))==set((a,b))][0]
if pa!=removed_orient[tuple(sorted((a,b)))]:patch=patch[:,::-1]
new=np.array(new);v=np.array(vertices);rightmap=np.full(len(v),-1,int);rightmap[:n]=mirror;rightmap[new]=np.arange(len(v),len(v)+len(new));all_v=np.r_[v,v[new]*[-1,1,1]];all_t=np.r_[tri[~(fmask|mask[mirror][tri].all(1))],patch,rightmap[patch[:,::-1]]]
used=np.unique(all_t);remap=np.full(len(all_v),-1,int);remap[used]=np.arange(len(used));all_v=all_v[used];all_t=remap[all_t];new_ids=remap[new];right_ids=remap[rightmap[new]]
# Restore the measured original total shoulder width with a smooth local
# outer-surface correction. Shaft boundary and torso-side boundary gain are 0.
def smoothstep(t):
 t=np.clip(t,0,1);return t*t*(3-2*t)
width_target=float(np.max(abs(pos[pos[:,1]>.54,0])))
generated_all=np.r_[new_ids,right_ids]
gain=smoothstep((abs(all_v[generated_all,0])-.195)/.025)*smoothstep((all_v[generated_all,1]-.510)/.040)
above=all_v[generated_all,1]>.54
excess=abs(all_v[generated_all,0])-width_target
width_adjustment=float(max(0,np.max(np.where(above,excess/np.maximum(gain,1e-12),0))))
all_v[generated_all,0]-=np.sign(all_v[generated_all,0])*width_adjustment*gain
new_mat=np.zeros_like(all_v);oldids=np.where(used<n,used,-1);keep=oldids>=0;new_mat[keep]=material[oldids[keep]];maps=np.array(maps);mweights=np.array(mweights);new_mat[new_ids]=(material[maps]*mweights[:,:,None]).sum(1);new_mat[right_ids]=new_mat[new_ids]*[-1,1,1]
# A positive gap requires repairing the inherited rib fill as well as the
# new bridge: the preserved lower body boundary itself penetrates the arm.
# Keep the arm surface fixed and distribute one smooth inward torso-side field.
arm_faces=(new_mat[all_t,0]>.305-1e-7).all(1)&(oldids[all_t]>=0).all(1)
arm_tree=BVHTree.FromPolygons(all_v.tolist(),all_t[arm_faces].tolist(),all_triangles=True)
body_branch=(oldids>=0)&(abs(new_mat[:,0])<.275)
body_branch[new_ids[np.array(parameters)[:,0]<.55]]=True;body_branch[right_ids[np.array(parameters)[:,0]<.55]]=True
body_region=body_branch&(all_v[:,1]>.33)&(all_v[:,1]<.55)
gap_gain=smoothstep((abs(all_v[:,0])-.10)/.04)*np.exp(-(all_v[:,2]/.075)**2)*(1-smoothstep((all_v[:,1]-.390)/.140))*smoothstep((all_v[:,1]-.330)/.090)
gap_amplitude=0.;clearance_required=[]
for i in np.flatnonzero(body_region&(all_v[:,0]>0)&(all_v[:,1]>.42)):
 hit,_,_,_=arm_tree.ray_cast(Vector((0,all_v[i,1],all_v[i,2])),Vector((1,0,0)),.5)
 if hit is not None:
  need=all_v[i,0]-hit.x+.0008
  if need>0 and gap_gain[i]>1e-8:
   gap_amplitude=max(gap_amplitude,float(need/gap_gain[i]));clearance_required.append([int(i),float(need)])
clearance_delta=np.zeros_like(all_v);clearance_delta[body_region,0]=-np.sign(all_v[body_region,0])*gap_amplitude*gap_gain[body_region]
all_v+=clearance_delta
e=np.sort(np.concatenate([all_t[:,[0,1]],all_t[:,[1,2]],all_t[:,[2,0]]]),1);_,ec=np.unique(e,axis=0,return_counts=True)
np.savez(OUT/'Geometry.npz',vertices=all_v,triangles=all_t,sourceVertexIndices=oldids,sourceNeutral=pos,sourceTriangles=tri,originalSourceVertexCount=np.array(original_n),sourceCutEdges=np.array(source_cut_edges),sourceCutWeights=np.array(source_cut_weights),material=new_mat,leftGeneratedVertices=new_ids,rightGeneratedVertices=right_ids,ringParameters=np.array(parameters),mappingSourceIndices=maps,mappingSourceWeights=mweights,bodyBoundarySourceIds=body,armBoundarySourceIds=arm,leftBoundaryBody=remap[body],leftBoundaryArm=remap[arm],leftPatchTriangles=remap[patch],neutralBodyClearanceDelta=clearance_delta,bodyClearanceAmplitude=np.array(gap_amplitude),controlAngles=angles,controlPositionsBody=p0,controlPositionsArm=p1,controlTangentsBody=d0,controlTangentsArm=d1,controlPositionsMiddle=pm,controlTangentsMiddle=dm,middleParameter=np.array(middle_u),sampledCageParameters=np.linspace(0,1,8),sampledCageLoops=np.array([exact_surface(angles,u) for u in np.linspace(0,1,8)]),positiveSideBone=np.array('UpperArm_R'),innerApexHeight=np.array(.548))
report={'vertices':len(all_v),'triangles':len(all_t),'oldPatchTrianglesEachSide':len(ft),'newPatchTrianglesEachSide':len(patch),'angularControlsPerEndpoint':24,'surfaceDefinition':'24 angular samples of 3 loop positions + 3 longitudinal tangents; 8 sampled cage loops are derived, not additional independent controls','boundaryLoops':[len(body),len(arm)],'edgesNotTwoFaces':int((ec!=2).sum()),'sourceShoulderWidth':float(np.ptp(pos[pos[:,1]>.54,0])),'newShoulderWidth':float(np.ptp(all_v[all_v[:,1]>.54,0])),'bodyClearanceAmplitudeMeters':gap_amplitude,'bodyClearanceMaximumMoveMeters':float(abs(clearance_delta[:,0]).max()),'bodyClearanceRayConstraintPoints':len(clearance_required),'widthCorrectionAmplitudeMeters':width_adjustment,'sourceRetainedPointsExact':bool(np.array_equal(all_v[keep],pos[oldids[keep]])),'visualApproved':False}
(OUT/'GeometryReview.json').write_text(json.dumps(report,indent=2));print(report,flush=True)
# Save genuine neutral mesh and four views.
bpy.ops.object.select_all(action='SELECT');bpy.ops.object.delete(use_global=False)
mesh=bpy.data.meshes.new('ExplicitShoulderConnectedBody');mesh.from_pydata((all_v[:,[0,2,1]]*[1,-1,1]).tolist(),[],all_t.tolist());mesh.update();obj=bpy.data.objects.new('R36_ExplicitShoulderNeutral',mesh);bpy.context.collection.objects.link(obj)
for f in mesh.polygons:f.use_smooth=True
mat=bpy.data.materials.new('Neutral clay');mat.diffuse_color=(.56,.61,.68,1);obj.data.materials.append(mat)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=24;scene.cycles.use_denoising=True;scene.render.resolution_x=800;scene.render.resolution_y=900;scene.render.resolution_percentage=100;scene.world.color=(.12,.15,.20)
for name,loc,energy,size in [('Key',(-2,-3,3),350,1.4),('Rim',(2,1,2),250,1.2),('Fill',(2,-3,1),70,2)]:
 l=bpy.data.lights.new(name,'AREA');l.energy=energy;l.size=size;o=bpy.data.objects.new(name,l);scene.collection.objects.link(o);o.location=loc;o.rotation_euler=(Vector((0,0,.5))-o.location).to_track_quat('-Z','Y').to_euler()
cam=bpy.data.objects.new('InspectionCamera',bpy.data.cameras.new('InspectionCamera'));scene.collection.objects.link(cam);scene.camera=cam;cam.data.type='ORTHO';cam.data.ortho_scale=.65
bpy.ops.wm.save_as_mainfile(filepath=str(OUT/'ExplicitShoulderNeutral.blend'),compress=True)
for label,loc in [('Front',(0,-3,.54)),('ThreeQuarter',(-2,-3,.72)),('Side',(-3,0,.54)),('Back',(0,3,.54))]:
 cam.location=loc;cam.rotation_euler=(Vector((0,0,.50))-cam.location).to_track_quat('-Z','Y').to_euler();scene.render.filepath=str(OUT/(label+'.png'));bpy.ops.render.render(write_still=True)
