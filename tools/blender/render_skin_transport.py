"""Render the actual skinned transport geometry using recorded Unity matrices.

Blender --background --python this.py -- transport pose-dir output-dir
This clay inspection recomputes geometric normals, exposing real shape defects.
"""
import json
import struct
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector, Matrix, Quaternion

args = sys.argv[sys.argv.index('--')+1:]
transport, poses, output = [Path(p).resolve() for p in args[:3]]
output.mkdir(parents=True, exist_ok=True)
data = transport.read_bytes()
size = struct.unpack_from('<i', data)[0]
header = json.loads(data[4:4+size])
offset = 4+size
bpy.ops.object.select_all(action='SELECT')
bpy.ops.object.delete(use_global=False)
parts = []
mat = bpy.data.materials.new('Neutral clay')
mat.use_nodes = True
shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.56, .61, .68, 1)
shader.inputs['Roughness'].default_value = .32
for record in header['meshes']:
    n = record['vertices']
    rest = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
    ids = np.frombuffer(data, '<i4', n*4, offset+n*24).reshape(n, 4)
    weights = np.frombuffer(data, '<f4', n*4, offset+n*40).reshape(n, 4)
    tri = np.frombuffer(data, '<i4', record['triangleIndices'], offset+n*56).reshape(-1, 3)
    mesh = bpy.data.meshes.new(record['name'])
    mesh.from_pydata(rest.tolist(), [], tri.tolist())
    for face in mesh.polygons:
        face.use_smooth = True
    obj = bpy.data.objects.new(record['name'], mesh)
    bpy.context.collection.objects.link(obj)
    mesh.materials.append(mat)
    parts.append((obj, rest, ids, weights))
    offset += n*56+record['triangleIndices']*4

pose_frames = []
bone_world = []
names = [b['name'] for b in header['bones']]
for b in header['bones']:
    x, y, z, w = b['rotation']
    m = Quaternion((w, x, y, z)).to_matrix().to_4x4()
    m.translation = Vector(b['position'])
    bone_world.append(m if b['parent'] < 0 else bone_world[b['parent']]@m)
bone_world = np.array(bone_world)
if header['version'] == 3:
    for ref in header['surfacePoses']:
        delta = None
        if ref['hasDelta']:
            n = header['meshes'][0]['vertices']
            delta = np.frombuffer(data, '<f4', n*3, offset).reshape(n, 3)
            offset += n*24
        pose_frames.append((ref, delta))


def correction(palette):
    total = np.zeros_like(parts[0][1])
    for side in range(2):
        suffix = 'L' if side == 0 else 'R'
        c, u, f = [names.index(name) for name in ('Chest', 'UpperArm_'+suffix, 'Forearm_'+suffix)]
        posed_bones = palette@bone_world
        shoulder = Matrix((np.linalg.inv(posed_bones[c, :3, :3])@posed_bones[u, :3, :3]).tolist()).to_quaternion()
        elbow = Matrix((np.linalg.inv(posed_bones[u, :3, :3])@posed_bones[f, :3, :3]).tolist()).to_quaternion()
        spine = names.index('Spine')
        torso = Matrix((np.linalg.inv(posed_bones[spine, :3, :3])@posed_bones[c, :3, :3]).tolist()).to_quaternion()
        rows = []
        for ref, delta in pose_frames:
            if ref['side'] != side:
                continue
            q, e = ref['rotation'], ref['elbowRotation']
            a = np.degrees(2*np.arccos(min(1, abs(shoulder.dot(Quaternion((q[3], *q[:3])))))))
            b = np.degrees(2*np.arccos(min(1, abs(elbow.dot(Quaternion((e[3], *e[:3])))))))
            t = ref.get('torsoRotation', [0, 0, 0, 1])
            c_angle = np.degrees(2*np.arccos(min(1, abs(torso.dot(Quaternion((t[3], *t[:3])))))))
            squared = a*a+.5*b*b+4*c_angle*c_angle
            rows.append((delta, squared, 1/max(.0025, squared)**2))
        weight = sum(r[2] for r in rows)
        distance = np.sqrt(min(r[1] for r in rows))
        x = np.clip((distance-45)/40, 0, 1)
        coverage = 1-x*x*(3-2*x)
        for delta, _, value in rows:
            if delta is not None:
                total += delta*(coverage*value/weight)
    return total

scene = bpy.context.scene
scene.render.engine = 'CYCLES'
scene.cycles.samples = 20
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 800, 1000
scene.render.resolution_percentage = 100
scene.world.use_nodes = True
background = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
background.inputs[0].default_value = (.12, .15, .20, 1)
background.inputs[1].default_value = .45
scene.view_settings.view_transform = 'AgX'
for name, loc, energy, radius in [('Key', (-2, -3, 2.8), 350, 1.4), ('Rim', (2, 1, 2), 250, 1.2), ('Fill', (2, -3, 1), 70, 2)]:
    light = bpy.data.lights.new(name, 'AREA')
    light.energy, light.size = energy, radius
    obj = bpy.data.objects.new(name, light)
    scene.collection.objects.link(obj)
    obj.location = loc
    obj.rotation_euler = (Vector((0, 0, .5))-obj.location).to_track_quat('-Z', 'Y').to_euler()
camera = bpy.data.objects.new('InspectionCamera', bpy.data.cameras.new('InspectionCamera'))
scene.collection.objects.link(camera)
scene.camera = camera
camera.data.type = 'ORTHO'
camera.data.ortho_scale = 1.14
views = [('Front', (0, -3, .48)), ('ThreeQuarter', (-2, -3, .72)), ('Side', (-3, 0, .48))]
for label, path in [('Idle', 'IdleSkinMatrices.bin'), ('Raised90', 'Raised_90_Matrices.bin'), ('Strike', 'Punch_08_Matrices.bin')]:
    palette = np.frombuffer((poses/path).read_bytes(), '<f4', offset=4).reshape(-1, 4, 4)
    palette = np.linalg.inv(palette[0]) @ palette
    delta = correction(palette) if pose_frames else None
    for obj, rest, ids, weights in parts:
        t = (palette[ids]*weights[:, :, None, None]).sum(1)
        corrected = rest+delta if delta is not None and obj.name.endswith('SkinnedBody') else rest
        posed = np.einsum('nij,nj->ni', t[:, :3, :3], corrected)+t[:, :3, 3]
        obj.data.vertices.foreach_set('co', (posed[:, [0, 2, 1]]*np.array([1, -1, 1])).astype(np.float32).ravel())
        obj.data.update()
    for view, location in views if label == 'Idle' else [views[1], views[2]]:
        camera.location = location
        camera.rotation_euler = (Vector((0, 0, .48))-camera.location).to_track_quat('-Z', 'Y').to_euler()
        scene.render.filepath = str(output/(label+'_'+view+'.png'))
        bpy.ops.render.render(write_still=True)
