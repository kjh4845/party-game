"""Render a skin transport in Unity-recorded or synthetic poses.

Blender --background --python this.py -- transport output-dir label=pose[@view,view] ...
pose: 'bind', a recorded *_Matrices.bin path, or 'syn:side:elevation:azimuth:flex:twist'
(side 0 = L, 1 = R; elevation from the idle arm direction, azimuth 0 forward / 90 out).
Views: Front, ThreeQuarter, Side, Back, Top, RightThreeQuarter, plus Close* variants
framed on the shoulders. Clay shading with recomputed normals.
"""
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_transport as rt  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:]
transport, output = rt.Transport(args[0]), Path(args[1]).resolve()
output.mkdir(parents=True, exist_ok=True)

for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj)
mat = bpy.data.materials.new('Neutral clay')
mat.use_nodes = True
shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.56, .61, .68, 1)
shader.inputs['Roughness'].default_value = .32
objects = []
for mesh_data in transport.meshes:
    mesh = bpy.data.meshes.new(mesh_data['name'])
    mesh.from_pydata(mesh_data['rest'].tolist(), [], mesh_data['tri'].tolist())
    mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
    obj = bpy.data.objects.new(mesh_data['name'], mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(mat)
    objects.append(obj)

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 640, 800
scene.world.use_nodes = True
background = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
background.inputs[0].default_value = (.12, .15, .20, 1)
background.inputs[1].default_value = .45
scene.view_settings.view_transform = 'AgX'
for name, loc, energy, radius in [('Key', (-2, -3, 2.8), 350, 1.4), ('Rim', (2, 1, 2), 250, 1.2),
                                  ('Fill', (2, -3, 1), 70, 2)]:
    light = bpy.data.lights.new(name, 'AREA')
    light.energy, light.size = energy, radius
    lo = bpy.data.objects.new(name, light)
    scene.collection.objects.link(lo)
    lo.location = loc
    lo.rotation_euler = (Vector((0, 0, .5)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
camera = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = 'ORTHO'
VIEWS = {'Front': (0, -1, 0), 'ThreeQuarter': (-.67, -1, .25), 'Side': (-1, 0, 0), 'Back': (0, 1, 0),
         'Top': (-.2, -.4, 1), 'RightThreeQuarter': (.67, -1, .25), 'RightSide': (1, 0, 0)}

for request in args[2:]:
    label, _, spec = request.partition('=')
    pose, _, views = spec.partition('@')
    views = views.split(',') if views else ['Front', 'ThreeQuarter', 'Side']
    if pose == 'bind':
        world = transport.rest_world()
    elif pose.startswith('syn:'):
        side, elevation, azimuth, flex, twist = [float(x) for x in pose[4:].split(':')]
        world = transport.synthetic(int(side), elevation, azimuth, flex, twist)
    else:
        world = transport.world_from_recorded(pose)
    centre = None
    for i, obj in enumerate(objects):
        posed = transport.skin(world, i, dqs=label.endswith('DQS'))
        blender = posed[:, [0, 2, 1]] * np.array([1, -1, 1])
        obj.data.vertices.foreach_set('co', blender.astype(np.float32).ravel())
        obj.data.update()
        if centre is None:
            centre = blender.mean(0)
    for view in views:
        if view.startswith('At:'):
            # At:dx:dy:dz:scale:tx:ty:tz in Blender coordinates
            dx, dy, dz, scale, tx, ty, tz = [float(v) for v in view[3:].split(':')]
            target = Vector((tx, ty, tz))
            camera.data.ortho_scale = scale
            camera.location = target + Vector((dx, dy, dz)).normalized() * 3
            camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = str(output / f'{label}_{view.replace(":", "_")}.png')
            bpy.ops.render.render(write_still=True)
            continue
        close = view.startswith('Close')
        direction = Vector(VIEWS[view[5:] if close else view]).normalized()
        target = Vector((centre[0], centre[1], .56 if close else .5))
        camera.data.ortho_scale = .55 if close else 1.2
        camera.location = target + direction * 3
        camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(output / f'{label}_{view}.png')
        bpy.ops.render.render(write_still=True)
