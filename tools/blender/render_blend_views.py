"""Render every mesh in an open .blend with neutral clay from fixed views.

Blender --background file.blend --python this.py -- output-dir prefix [View:scale:cx:cz ...]
Default views: Front, ThreeQuarter, Side, Back (full body). Optional close views
take an ortho scale and a target centre (x, z) in metres.
"""
import sys
from pathlib import Path

import bpy
from mathutils import Vector

args = sys.argv[sys.argv.index('--') + 1:]
out, prefix = Path(args[0]).resolve(), args[1]
out.mkdir(parents=True, exist_ok=True)
DIRS = {'Front': (0, -1, 0), 'ThreeQuarter': (-.67, -1, .25), 'Side': (-1, 0, 0), 'Back': (0, 1, 0),
        'RightThreeQuarter': (.67, -1, .25), 'Top': (-.2, -.4, 1), 'Below': (.3, -.6, -.6)}
views = [v.split(':') for v in args[2:]] or [[k, '1.2', '0', '.5'] for k in ('Front', 'ThreeQuarter', 'Side', 'Back')]
scene = bpy.context.scene
for obj in list(scene.objects):
    if obj.type not in ('MESH',):
        bpy.data.objects.remove(obj)
mat = bpy.data.materials.new('clay')
mat.use_nodes = True
shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.56, .61, .68, 1)
shader.inputs['Roughness'].default_value = .32
for obj in scene.objects:
    obj.data.materials.clear()
    obj.data.materials.append(mat)
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 640, 800
if scene.world is None:
    scene.world = bpy.data.worlds.new('w')
scene.world.use_nodes = True
bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs[0].default_value = (.12, .15, .20, 1)
bg.inputs[1].default_value = .45
scene.view_settings.view_transform = 'AgX'
for name, loc, energy, radius in [('Key', (-2, -3, 2.8), 350, 1.4), ('Rim', (2, 1, 2), 250, 1.2), ('Fill', (2, -3, 1), 70, 2)]:
    light = bpy.data.lights.new(name, 'AREA')
    light.energy, light.size = energy, radius
    lo = bpy.data.objects.new(name, light)
    scene.collection.objects.link(lo)
    lo.location = loc
    lo.rotation_euler = (Vector((0, 0, .5)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
cam = bpy.data.objects.new('cam', bpy.data.cameras.new('cam'))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
for view in views:
    name, scale, cx, cz = view[0], float(view[1]), float(view[2]), float(view[3])
    target = Vector((cx, 0, cz))
    cam.data.ortho_scale = scale
    cam.location = target + Vector(DIRS[name]).normalized() * 3
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(out / f'{prefix}_{name}_{scale}.png')
    bpy.ops.render.render(write_still=True)
