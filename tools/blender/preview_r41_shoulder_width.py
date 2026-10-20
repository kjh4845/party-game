"""Shoulder-width preview: r40 (skinned) vs ball-shoulder candidates (rigid parts, SDF union + fillet).

Blender -b -P preview_shoulders.py -- out-dir
Candidates are target shapes (rigid arm segments posed by recorded arm directions), not skinning results.
"""
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector

ROOT = Path('/Users/kjh/local_dev/partygame')
sys.path.insert(0, str(ROOT / 'tools/blender'))
import r40_transport as rt  # noqa: E402

OUT = Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)
R40 = ROOT / 'BlenderSource/Characters/C1B-RW-040-cage-body'
T = rt.Transport(R40 / 'CageBodyCharacter.bytes')
cage = np.load(R40 / 'cage.npz')

POSES = {
    'Idle': None,
    'Punch': ROOT / '.codex_tmp/arm-volume-audit-20261008/Captures/Punch8_Matrices.bin',
    'Jab': 'jab',
    'Grab': ROOT / '.codex_tmp/surface-refine-20261008/ReferencePosesL/Grab_Matrices.bin',
}
# name -> pivot x (None = r40 skinned)
CANDIDATES = [('r40', None), ('A_x210', .21), ('B_x225', .225), ('As_x210', .21), ('Bs_x225', .225),
              ('S200', .20), ('S210', .21), ('S225', .225)]
PIVOT_Z = .62
UPPER_LEN, FORE_LEN = .1805, .1755
BALL_R = .058
UPPER_R = (.0525, .050)
FORE_R = (.049, .046)
FIST_R = .050
IDLE = np.array((.087, 0, -1.0)) / np.hypot(.087, 1.0)


def to_blender(p):
    return np.stack((p[..., 0], -p[..., 2], p[..., 1]), -1)


def chest_normalised(world):
    c = T.index['Chest']
    return (T.bind[c] @ np.linalg.inv(world[c])) @ world


def arm_dirs(pose):
    """side (+1 R / -1 L) -> (upper dir, forearm dir) in Blender coordinates."""
    if POSES[pose] is None:
        return {s: (IDLE * np.array((s, 1, 1)), IDLE * np.array((s, 1, 1))) for s in (1, -1)}
    if POSES[pose] == 'jab':
        n = lambda v: np.array(v) / np.linalg.norm(v)
        # R: straight forward from its own shoulder; L: guard (upper arm down-forward, forearm up-forward).
        return {1: (n((.04, -1, -.12)), n((-.02, -1, -.02))), -1: (n((-.15, -.55, -1)), n((.1, -1, .9)))}
    world = chest_normalised(T.world_from_recorded(POSES[pose]))
    out = {}
    for s, n in ((1, 'R'), (-1, 'L')):
        u, f, h = (to_blender(world[T.index[b + n], :3, 3]) for b in ('UpperArm_', 'Forearm_', 'HandLogical_'))
        out[s] = ((f - u) / np.linalg.norm(f - u), (h - f) / np.linalg.norm(h - f))
    return out


def r40_world(pose):
    if POSES[pose] == 'jab':
        return T.synthetic(1, 83, 0, 5, 0)
    return T.rest_world() if POSES[pose] is None else chest_normalised(T.world_from_recorded(POSES[pose]))


# ---------------------------------------------------------------- scene
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj)
scene = bpy.context.scene
mat = bpy.data.materials.new('Clay')
mat.use_nodes = True
shader = next(n for n in mat.node_tree.nodes if n.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.56, .61, .68, 1)
shader.inputs['Roughness'].default_value = .32


def mesh_object(name, verts, faces, smooth=True):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.polygons.foreach_set('use_smooth', [smooth] * len(mesh.polygons))
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


# ---------------------------------------------------------------- SDF candidate
import os  # noqa: E402
import openvdb as vdb  # noqa: E402

VS = float(os.environ.get('VOXEL', .004))
LO = np.array((-.44, -.50, -.03))
HI = np.array((.44, .32, 1.02))
I0 = np.floor(LO / VS).astype(int)
DIM = np.ceil((HI - LO) / VS).astype(int)
axes = [(I0[a] + np.arange(DIM[a])) * VS for a in range(3)]
GX, GY, GZ = np.meshgrid(*[ax.astype(np.float32) for ax in axes], indexing='ij')
print('grid', DIM, flush=True)


def torso_sdf():
    tr = vdb.createLinearTransform(VS)
    grid = vdb.FloatGrid.createLevelSetFromPolygons(cage['torso_surface'].astype(np.float32), None,
                                                    cage['torso_faces'].astype(np.uint32), tr, 14.0)
    out = np.empty(tuple(DIM), np.float32)
    grid.copyToArray(out, ijk=tuple(int(i) for i in I0))
    return out


TORSO = torso_sdf()


def smin(a, b, k):
    k = np.maximum(k, 1e-5)
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.minimum(a, b) - h * h * k * .25


def sphere_d(c, r):
    return np.sqrt((GX - c[0]) ** 2 + (GY - c[1]) ** 2 + (GZ - c[2]) ** 2) - r


def capsule_d(a, b, ra, rb):
    d = b - a
    L = np.linalg.norm(d)
    d = d / L
    px, py, pz = GX - a[0], GY - a[1], GZ - a[2]
    s = px * d[0] + py * d[1] + pz * d[2]
    h = np.clip(s / L, 0, 1)
    qx, qy, qz = px - h * L * d[0], py - h * L * d[1], pz - h * L * d[2]
    return np.sqrt(qx * qx + qy * qy + qz * qz) - (ra + (rb - ra) * h), s


def ellipsoid_d(c, r):
    x, y, z = (GX - c[0]) / r[0], (GY - c[1]) / r[1], (GZ - c[2]) / r[2]
    k0 = np.sqrt(x * x + y * y + z * z)
    k1 = np.sqrt((x / r[0]) ** 2 + (y / r[1]) ** 2 + (z / r[2]) ** 2)
    return k0 * (k0 - 1) / np.maximum(k1, 1e-6)


SHOULDER_K = (float(os.environ.get('SHOULDER_K', .035)), .004)
YOKE = os.environ.get('YOKE', '1') == '1'
SLOPE = os.environ.get('SLOPE') == '1'
SMOOTH_ARM = os.environ.get('SMOOTH_ARM') == '1'
ARM_PROFILE = (.054, .050, .046)  # root, elbow, tip
if SLOPE:
    BALL_R = float(os.environ.get('BALL', .054))


def build_candidate(px, pose):
    body = TORSO
    if YOKE:
        if SLOPE:
            # Dome: high at the neck, falling to the shoulder balls (rounded, sloped shoulder line).
            body = smin(body, ellipsoid_d((0, 0, .575), (px + .005, .1, .118)), .05)
        else:
            body = smin(body, ellipsoid_d((0, 0, .622), (px - .01, .095, .07)), .045)
    for s, (ud, fd) in arm_dirs(pose).items():
        pivot = np.array((s * px, 0, PIVOT_Z))
        elbow = pivot + ud * UPPER_LEN
        tip = elbow + fd * FORE_LEN
        if SMOOTH_ARM:
            # One continuous tapered tube: both segments share the elbow radius, so the
            # union is round outside the bend with no ring; the root sphere is the shoulder.
            upper, along = capsule_d(pivot, elbow, ARM_PROFILE[0], ARM_PROFILE[1])
            fore, _ = capsule_d(elbow, tip, ARM_PROFILE[1], ARM_PROFILE[2])
            arm = np.minimum(upper, fore)
        else:
            upper, along = capsule_d(pivot, elbow, *UPPER_R)
            fore, _ = capsule_d(elbow, tip, *FORE_R)
            arm = smin(sphere_d(pivot, BALL_R), upper, .02)
            arm = smin(arm, fore, .012)
            arm = smin(arm, sphere_d(tip, FIST_R), .015)
        # Shoulder fillet only at the arm root (by distance along the upper arm).
        t = np.clip((along - .02) / .06, 0, 1)
        body = smin(body, arm, SHOULDER_K[0] + (SHOULDER_K[1] - SHOULDER_K[0]) * t)
    grid = vdb.FloatGrid()
    grid.copyFromArray(np.ascontiguousarray(body, np.float32), ijk=tuple(int(i) for i in I0))
    grid.transform = vdb.createLinearTransform(VS)
    points, quads = grid.convertToQuads(isovalue=0.0)
    obj = mesh_object(f'cand_{px}_{pose}', points, quads[:, ::-1])
    return obj


def sdf_union_object(obj, name):
    return obj

# ---------------------------------------------------------------- render setup
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 560, 640
scene.world = scene.world or bpy.data.worlds.new('World')
scene.world.use_nodes = True
bg = next(n for n in scene.world.node_tree.nodes if n.type == 'BACKGROUND')
bg.inputs[0].default_value = (.12, .15, .20, 1)
bg.inputs[1].default_value = .45
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
camera.data.ortho_scale = 1.2
VIEWS = {'Front': (0, -1, 0), 'ThreeQuarter': (-.67, -1, .25), 'Top': (0, -.35, 1)}

head_mesh = T.meshes[1]
head = mesh_object('Head', to_blender(head_mesh['rest']), head_mesh['tri'])
r40_body = mesh_object('R40Body', to_blender(T.meshes[0]['rest']), T.meshes[0]['tri'])

ONLY = os.environ.get('ONLY')
for label, px in CANDIDATES:
    if ONLY and label not in ONLY.split(','):
        continue
    for pose in [p for p in POSES if not os.environ.get('POSE') or p in os.environ['POSE'].split(',')]:
        built = None
        if px is None:
            r40_body.hide_render = False
            world = r40_world(pose)
            posed = to_blender(T.skin(world, 0))
            r40_body.data.vertices.foreach_set('co', posed.astype(np.float32).ravel())
            r40_body.data.update()
        else:
            r40_body.hide_render = True
            built = build_candidate(px, pose)
        views = ['Front', 'ThreeQuarter'] + (['Top'] if pose in ('Punch', 'Jab') else [])
        for view in views:
            direction = Vector(VIEWS[view]).normalized()
            target = Vector((0, -.04, .47))
            camera.location = target + direction * 3
            camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = str(OUT / f'{label}_{pose}_{view}.png')
            bpy.ops.render.render(write_still=True)
        if built is not None:
            bpy.data.objects.remove(built)
        print('done', label, pose, flush=True)
