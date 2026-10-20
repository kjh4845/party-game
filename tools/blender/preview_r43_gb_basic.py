"""r43 body after Gang_Beast_basic_poses.webp (front/side/back turnaround with a cm grid).

Blender -b -P preview_r43_gb_basic.py -- out-dir

Measured from the turnaround (1.025 px/cm, total height ~145 cm) and scaled to 1 m:
- torso: column, half width ~.17, half depth ~.138; top at .686, rounded strongly front/back
- arms: leave the top corners (pivot ~(+-.165, .645)), ~8 cm thick, hang ~28 deg out, ~.45 m long
- legs: outer edge vertical at +-.197, crotch arch top at .16, 12 cm -> 5.5 cm wide
White, no fists (round ends). Keeps the r40 head mesh.

Renders TPose/Relaxed front, side and 3/4, and reference-matched silhouettes for
overlay against the turnaround panels.
"""
import os
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

sys.path.insert(0, str(Path(__file__).resolve().parent))
from r43_shape import *  # noqa: E402,F401,F403

OUT = Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)


def build(pose):
    body, head = body_field(pose)
    points, quads = field_mesh(body)
    objs = [mesh_object('body', points, quads)]
    if NECK_K <= 0:
        objs.append(mesh_object('head', head, HEAD['tri']))
    return objs


for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj)
scene = bpy.context.scene
mat = bpy.data.materials.new('White')
mat.use_nodes = True
shader = next(nd for nd in mat.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.92, .92, .92, 1)
shader.inputs['Roughness'].default_value = .45


def mesh_object(name, verts, faces):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.world = scene.world or bpy.data.worlds.new('World')
scene.world.use_nodes = True
bg = next(nd for nd in scene.world.node_tree.nodes if nd.type == 'BACKGROUND')
bg.inputs[0].default_value = (.12, .15, .20, 1)
bg.inputs[1].default_value = .22
scene.view_settings.view_transform = 'AgX'
for name, loc, energy, radius in [('Key', (-2, -3, 2.8), 420, 1.6), ('Rim', (2, 1, 2), 200, 1.2),
                                  ('Fill', (2, -3, 1), 130, 2.5)]:
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
         'RightSide': (1, 0, 0), 'RightThreeQuarter': (.67, -1, .25), 'BackThreeQuarter': (-.67, 1, .25),
         'BackRightThreeQuarter': (.67, 1, .25), 'Top': (-.25, -.5, 1), 'Low': (-.4, -1, -.35)}
PX_PER_M = 145 * 1.025   # turnaround pixels per metre after scaling 145 cm -> 1 m


def render(path, view, panel=False):
    direction = Vector(VIEWS[view]).normalized()
    if panel:
        # One turnaround panel: 268 x 208 px, ground at 186 px from the panel top, body centre x=133.5.
        PS = int(os.environ.get('PANEL_SCALE', 1))
        scene.render.resolution_x, scene.render.resolution_y = 268 * PS, 208 * PS
        camera.data.ortho_scale = 268 / PX_PER_M
        lateral = (134 - 133.5) / PX_PER_M
        target = Vector((lateral, 0, (186 - 104) / PX_PER_M)) if view in ('Front', 'Back') else Vector((0, lateral, (186 - 104) / PX_PER_M))
        scene.render.film_transparent = True
    else:
        scene.render.resolution_x, scene.render.resolution_y = 520, 600
        camera.data.ortho_scale = 1.35
        target = Vector((0, 0, .5))
        scene.render.film_transparent = False
    camera.location = target + direction * 3
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    # Lights follow the camera so every view (back included) is lit the same way.
    side = direction.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
    for name, offset in (('Key', direction * 2.4 + side * 1.6 + Vector((0, 0, 2.0))),
                         ('Fill', direction * 2.6 - side * 2.0 + Vector((0, 0, .4))),
                         ('Rim', -direction * 2.2 + Vector((0, 0, 1.6)))):
        lo = scene.objects[name]
        lo.location = Vector((0, 0, .45)) + offset
        lo.rotation_euler = (Vector((0, 0, .45)) - lo.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


pose_filter = os.environ.get('POSES')
for pose in POSES:
    if pose_filter and pose not in pose_filter.split(','):
        continue
    objs = build(pose)
    if pose in ('TPose', 'Relaxed'):
        render(OUT / f'{pose}_PanelFront.png', 'Front', panel=True)
    if pose == 'Relaxed':
        render(OUT / f'{pose}_PanelBack.png', 'Back', panel=True)
        render(OUT / f'{pose}_PanelSide.png', 'Side', panel=True)
    if pose == 'TPose':
        render(OUT / f'{pose}_PanelSide.png', 'Side', panel=True)
    for view in os.environ.get('VIEWS', 'Front,ThreeQuarter,Side').split(','):
        render(OUT / f'{pose}_{view}.png', view)
    for o in objs:
        bpy.data.objects.remove(o)
    print('done', pose, flush=True)
