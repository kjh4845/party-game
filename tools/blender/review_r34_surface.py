"""Compare actual transports under identical grazing light and close cameras."""
from pathlib import Path
import sys,json,struct,hashlib
import bpy,numpy as np
from mathutils import Vector

args=sys.argv[sys.argv.index('--')+1:];old,new,poses,out=map(lambda p:Path(p).resolve(),args[:4])
sys.argv=[sys.argv[0],'--',str(new),str(poses),str(out)]
loader=Path(__file__).with_name('render_skin_transport.py').read_text().split('\nscene = bpy.context.scene')[0]
exec(compile(loader,str(Path(__file__).with_name('render_skin_transport.py')),'exec'),globals())
old_data=old.read_bytes();offset=4+struct.unpack_from('<i',old_data)[0];old_rest=[]
for record in header['meshes']:
    n=record['vertices'];old_rest.append(np.frombuffer(old_data,'<f4',n*3,offset).reshape(n,3));offset+=n*56+record['triangleIndices']*4
palette=np.frombuffer((poses/'IdleSkinMatrices.bin').read_bytes(),'<f4',offset=4).reshape(-1,4,4);palette=np.linalg.inv(palette[0])@palette;delta=correction(palette)
scene=bpy.context.scene;scene.render.engine='CYCLES';scene.cycles.samples=48;scene.cycles.use_denoising=True;scene.cycles.seed=0;scene.render.resolution_x=900;scene.render.resolution_y=850;scene.render.resolution_percentage=100;scene.view_settings.view_transform='AgX'
shader.inputs['Base Color'].default_value=(.56,.59,.63,1);shader.inputs['Roughness'].default_value=.48
scene.world.use_nodes=True;background=next(n for n in scene.world.node_tree.nodes if n.type=='BACKGROUND');background.inputs[0].default_value=(.11,.13,.16,1);background.inputs[1].default_value=.30
for name,loc,power,size in [('Grazing',(-2.5,-.35,.95),220,.50),('Fill',(1,-3,.7),30,2)]:
    light=bpy.data.lights.new(name,'AREA');light.energy=power;light.size=size;obj=bpy.data.objects.new(name,light);scene.collection.objects.link(obj);obj.location=loc;obj.rotation_euler=(Vector((0,0,.30))-obj.location).to_track_quat('-Z','Y').to_euler()
camera=bpy.data.objects.new('SurfaceReviewCamera',bpy.data.cameras.new('SurfaceReviewCamera'));scene.collection.objects.link(camera);scene.camera=camera;camera.data.type='ORTHO'
views=[('TorsoFront',(0,-3,.38),(0,0,.38),.61),('GroinFront',(0,-3,.15),(0,0,.15),.38),('TorsoThreeQuarter',(-1.5,-3,.45),(0,0,.33),.66)]
for label,positions in [('Before',old_rest),('After',[item[1] for item in parts])]:
    for (obj,_,ids,weights),rest in zip(parts,positions):
        t=(palette[ids]*weights[:,:,None,None]).sum(1);p=rest+delta if obj.name.endswith('SkinnedBody') else rest;point=np.einsum('nij,nj->ni',t[:,:3,:3],p)+t[:,:3,3];obj.data.vertices.foreach_set('co',(point[:,[0,2,1]]*[1,-1,1]).astype(np.float32).ravel());obj.data.update()
    for view,location,target,scale in views:
        camera.location=location;camera.rotation_euler=(Vector(target)-camera.location).to_track_quat('-Z','Y').to_euler();camera.data.ortho_scale=scale;scene.render.filepath=str(out/(label+'_'+view+'.png'));bpy.ops.render.render(write_still=True)
report={'beforeSha256':hashlib.sha256(old.read_bytes()).hexdigest(),'afterSha256':hashlib.sha256(new.read_bytes()).hexdigest(),'geometry':'Transport positions and corrective deltas with recorded Unity neutral matrices; normals recomputed from the actual geometry.','sameCameraLightMaterialAndSeed':True,'views':[v[0] for v in views]};(out/'RenderReview.json').write_text(json.dumps(report,indent=2)+'\n')
