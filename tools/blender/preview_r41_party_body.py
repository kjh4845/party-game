"""r41 target-shape preview after the Party Animals reference (e.png): no raised shoulder,
thick arms whose root melts into the upper torso side, arms held out from the body,
and punches that turn the upper body.

Blender -b -P preview_r41_party_body.py -- out-dir
Env: VARIANTS="name:px:pz,..." (joint x / z in metres), POSES="Idle,..."

Shapes are rigid-part target shapes (signed-distance union, OpenVDB meshing), not skinning.
"""
import os
import sys
from pathlib import Path

import bpy
import numpy as np
import openvdb as vdb
from mathutils import Matrix, Vector

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_transport as rt  # noqa: E402

OUT = Path(sys.argv[sys.argv.index('--') + 1])
OUT.mkdir(parents=True, exist_ok=True)
R40 = ROOT / 'BlenderSource/Characters/C1B-RW-040-cage-body'
T = rt.Transport(R40 / 'CageBodyCharacter.bytes')
cage = np.load(R40 / 'cage.npz')
TORSO_V, TORSO_F = cage['torso_surface'].copy(), cage['torso_faces']


def pear(V, amount):
    # Widen x/y around the belly (z ~ .30) with a smooth bump that is 0 above the chest (z > .58).
    z = V[:, 2]
    w = np.clip(1 - np.abs(z - .30) / .28, 0, 1)
    w = w * w * (3 - 2 * w)
    out = V.copy()
    out[:, 0] *= 1 + amount * w
    out[:, 1] *= 1 + amount * .8 * w
    return out
HEAD = T.meshes[1]

ARM_SCALE = float(os.environ.get('ARM_SCALE', 1))
UPPER_LEN, FORE_LEN = .1805 * ARM_SCALE, .1755 * ARM_SCALE
PEAR = float(os.environ.get('PEAR', 0))  # extra width at the belly, fading to 0 at the chest
ARM_R = tuple(float(x) for x in os.environ.get('ARM_R', '.060,.057,.055').split(','))  # root, elbow, paw
ROOT_K = float(os.environ.get('ROOT_K', .07))   # fillet size where the arm leaves the torso
ROOT_FADE = tuple(float(x) for x in os.environ.get('ROOT_FADE', '.02,.13').split(','))  # fillet fades along the upper arm (m)
HEAD_MERGE = float(os.environ.get('HEAD_MERGE', 0))  # >0: head fused into the torso with this fillet (m)
HEAD_DZ = float(os.environ.get('HEAD_DZ', 0))        # move the head down (m) before fusing                           # fillet fades out along the upper arm (m)


def n(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


# Pose: torso yaw/pitch about the hips plus arm directions in the torso frame (side +1 = R).
# Arm directions: (upper, forearm). Blender axes: front -Y, up +Z, +X = character right.
OUT20 = np.radians(float(os.environ.get('IDLE_OUT', 22)))
IDLE = lambda s: (n((s * np.sin(OUT20), -.03, -np.cos(OUT20))),) * 2  # straight arm
POSES = {
    'Idle': dict(yaw=0, pitch=0, arms={1: IDLE(1), -1: IDLE(-1)}),
    # Wind-up: right shoulder pulled back, fist cocked at the side; left arm forward as a guard.
    'PunchWindup': dict(yaw=20, pitch=4, arms={1: (n((.35, .35, -1)), n((.1, -1, .25))),
                                                -1: (n((-.35, -1, -.55)), n((.1, -1, .2)))}),
    # Extension: upper body turns ~25 deg, right arm almost straight forward at chest height.
    'PunchHit': dict(yaw=-25, pitch=8, arms={1: (n((-.06, -1, -.08)),) * 2,
                                            -1: (n((-.3, .2, -1)),) * 2}),
    'Grab': dict(yaw=0, pitch=6, arms={1: (n((-.1, -1, .05)),) * 2,
                                       -1: (n((.1, -1, .05)),) * 2}),
}
VARIANTS = [(v.split(':')[0], float(v.split(':')[1]), float(v.split(':')[2]))
            for v in os.environ.get('VARIANTS', 'J18:.18:.62,J17L:.17:.585').split(',')]


def pose_matrix(yaw, pitch):
    pivot = Vector((0, 0, .30))
    m = Matrix.Rotation(np.radians(yaw), 4, 'Z') @ Matrix.Rotation(np.radians(-pitch), 4, 'X')
    return Matrix.Translation(pivot) @ m @ Matrix.Translation(-pivot)


def apply(m, P):
    M = np.array(m)
    return P @ M[:3, :3].T + M[:3, 3]


# ---------------------------------------------------------------- scene
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj)
scene = bpy.context.scene
mat = bpy.data.materials.new('Clay')
mat.use_nodes = True
shader = next(nd for nd in mat.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED')
shader.inputs['Base Color'].default_value = (.56, .61, .68, 1)
shader.inputs['Roughness'].default_value = .32


def mesh_object(name, verts, faces):
    mesh = bpy.data.meshes.new(name)
    mesh.from_pydata([tuple(v) for v in verts], [], [tuple(f) for f in faces])
    mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
    mesh.materials.append(mat)
    obj = bpy.data.objects.new(name, mesh)
    scene.collection.objects.link(obj)
    return obj


VS = .004
LO, HI = np.array((-.48, -.52, -.03)), np.array((.48, .40, 1.02))
I0 = np.floor(LO / VS).astype(int)
DIM = np.ceil((HI - LO) / VS).astype(int)
GX, GY, GZ = np.meshgrid(*[((I0[a] + np.arange(DIM[a])) * VS).astype(np.float32) for a in range(3)], indexing='ij')


def level_set(points, quads):
    grid = vdb.FloatGrid.createLevelSetFromPolygons(points.astype(np.float32), None, quads.astype(np.uint32),
                                                    vdb.createLinearTransform(VS), 18.0)
    out = np.empty(tuple(DIM), np.float32)
    grid.copyToArray(out, ijk=tuple(int(i) for i in I0))
    return out


def smin(a, b, k):
    k = np.maximum(k, 1e-5)
    h = np.maximum(k - np.abs(a - b), 0) / k
    return np.minimum(a, b) - h * h * k * .25


def capsule(a, b, ra, rb):
    d = b - a
    L = np.linalg.norm(d)
    d = d / L
    px, py, pz = GX - a[0], GY - a[1], GZ - a[2]
    s = px * d[0] + py * d[1] + pz * d[2]
    h = np.clip(s / L, 0, 1)
    qx, qy, qz = px - h * L * d[0], py - h * L * d[1], pz - h * L * d[2]
    return np.sqrt(qx * qx + qy * qy + qz * qz) - (ra + (rb - ra) * h), s


def build(px, pz, pose):
    spec = POSES[pose]
    m = pose_matrix(spec['yaw'], spec['pitch'])
    R = np.array(m)[:3, :3]
    body = level_set(apply(m, pear(TORSO_V, PEAR)), TORSO_F)
    head_pts = apply(m, np.stack((HEAD['rest'][:, 0], -HEAD['rest'][:, 2], HEAD['rest'][:, 1] - HEAD_DZ), 1))
    if HEAD_MERGE > 0:
        tr = vdb.createLinearTransform(VS)
        hg = vdb.FloatGrid.createLevelSetFromPolygons(head_pts.astype(np.float32), HEAD['tri'].astype(np.uint32), None, tr, 18.0)
        hd = np.empty(tuple(DIM), np.float32)
        hg.copyToArray(hd, ijk=tuple(int(i) for i in I0))
        body = smin(body, hd, HEAD_MERGE)
    for s, (ud, fd) in spec['arms'].items():
        pivot = apply(m, np.array([[s * px, 0, pz]]))[0]
        elbow = pivot + R @ ud * UPPER_LEN
        tip = elbow + R @ fd * FORE_LEN
        upper, along = capsule(pivot, elbow, ARM_R[0], ARM_R[1])
        fore, _ = capsule(elbow, tip, ARM_R[1], ARM_R[2])
        arm = np.minimum(upper, fore)
        t = np.clip((along - ROOT_FADE[0]) / (ROOT_FADE[1] - ROOT_FADE[0]), 0, 1)
        body = smin(body, arm, ROOT_K + (.004 - ROOT_K) * t * t * (3 - 2 * t))
    grid = vdb.FloatGrid()
    grid.copyFromArray(np.ascontiguousarray(body, np.float32), ijk=tuple(int(i) for i in I0))
    grid.transform = vdb.createLinearTransform(VS)
    points, quads = grid.convertToQuads(isovalue=0.0)
    body_obj = mesh_object(f'body_{pose}', points, quads[:, ::-1])
    if HEAD_MERGE > 0:
        return [body_obj]
    head_obj = mesh_object(f'head_{pose}', head_pts, HEAD['tri'])
    return [body_obj, head_obj]


scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 560, 600
scene.world = scene.world or bpy.data.worlds.new('World')
scene.world.use_nodes = True
bg = next(nd for nd in scene.world.node_tree.nodes if nd.type == 'BACKGROUND')
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
VIEWS = {'Front': (0, -1, 0), 'ThreeQuarter': (-.67, -1, .25), 'RightThreeQuarter': (.8, -1, .2), 'Top': (0, -.35, 1)}
VIEW_LIST = os.environ.get('VIEWS', 'Front,ThreeQuarter,RightThreeQuarter').split(',')
pose_filter = os.environ.get('POSES')

for label, px, pz in VARIANTS:
    for pose in POSES:
        if pose_filter and pose not in pose_filter.split(','):
            continue
        objs = build(px, pz, pose)
        for view in VIEW_LIST:
            direction = Vector(VIEWS[view]).normalized()
            target = Vector((0, -.04, .47))
            camera.location = target + direction * 3
            camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
            scene.render.filepath = str(OUT / f'{label}_{pose}_{view}.png')
            bpy.ops.render.render(write_still=True)
        for o in objs:
            bpy.data.objects.remove(o)
        print('done', label, pose, flush=True)
