"""r42 body after the Gang Beasts default avatar (Gang_Beast_default_avatar_render.png):
same silhouette, fists replaced by round arm ends, white. Keeps the r40 head mesh and
1 m height; the measured arm axis meets the existing UpperArm joint (+-.18, .62).

Blender -b -P preview_r42_gb_default.py -- out-dir
Writes <pose>_<view>.png renders and, for the front view, a silhouette overlay against the
reference (overlay_front.png) with IoU.

Measurements (reference px -> m: 1/525 m per px, ground at y=540, centre x=161).
Target shapes only (signed-distance union, OpenVDB meshing), not skinning.
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
REF = ROOT / 'Gang_Beast_default_avatar_render.png'
T = rt.Transport(ROOT / 'BlenderSource/Characters/C1B-RW-040-cage-body/CageBodyCharacter.bytes')
HEAD = T.meshes[1]
env = lambda k, d: [float(x) for x in os.environ.get(k, d).split(',')]

# Torso profile: (height, half width, half depth). Upper body is broad and carries the arm roots;
# waist under the armpit, hips widen into the legs.
TORSO = np.array(env('TORSO', '.148,.030,.030, .158,.110,.100, .176,.155,.135, .210,.170,.148, .270,.168,.146,'
                              ' .360,.162,.142, .450,.158,.140, .520,.160,.140, .600,.162,.136, .670,.156,.126,'
                              ' .722,.138,.108, .752,.090,.074'))
TORSO = TORSO.reshape(-1, 3)
EXPONENT = env('EXPONENT', '2.1')[0]
SHOULDER = np.array(env('SHOULDER', '.18,0,.62'))   # arm axis root (= UpperArm joint)
BALL = env('BALL', '.135,0,.652,.070')                # shoulder mass: centre x,y,z, radius
ARM_PTS = np.array(env('ARM_PTS', '.150,0,.630,.058, .205,0,.450,.037, .242,0,.275,.032')).reshape(-1, 4)
BALL_K = env('BALL_K', '.03')[0]
ARM_OUT = np.radians(env('ARM_OUT', '13')[0])
ARM_LEN = env('ARM_LEN', '.355')[0]                  # joint -> centre of the round end
ARM_R = env('ARM_R', '.040,.034,.033')                # root, mid, end
ARM_K = env('ARM_K', '.03')[0]                        # arm/torso fillet near the root
ARM_FADE = env('ARM_FADE', '.15,.25')                 # fillet fades out along the arm (m)
LEG = env('LEG', '.125,0,.19, .160,0,.032, .060,.031')  # hip, foot, radius hip, radius foot
LEG_K = env('LEG_K', '.05')[0]


def n(v):
    v = np.asarray(v, float)
    return v / np.linalg.norm(v)


def arm_dir(side, swing_fwd=0.0, swing_out=None):
    out = ARM_OUT if swing_out is None else np.radians(swing_out)
    d = np.array((side * np.sin(out), 0, -np.cos(out)))
    if swing_fwd:
        a = np.radians(swing_fwd)
        d = np.array((d[0], -np.sin(a) * np.hypot(d[0], d[2]) * 1, d[2] * np.cos(a)))
    return n(d)


POSES = {
    'Idle': dict(yaw=0, arms={1: arm_dir(1), -1: arm_dir(-1)}),
    'Punch': dict(yaw=-20, arms={1: n((-.05, -1, -.05)), -1: n((-.2, 0, -1))}),
    'Grab': dict(yaw=0, arms={1: n((-.08, -1, .08)), -1: n((.08, -1, .08))}),
}

# ---------------------------------------------------------------- geometry
VS = .004
LO, HI = np.array((-.40, -.50, -.03)), np.array((.40, .30, .80))
I0 = np.floor(LO / VS).astype(int)
DIM = np.ceil((HI - LO) / VS).astype(int)
GX, GY, GZ = np.meshgrid(*[((I0[a] + np.arange(DIM[a])) * VS).astype(np.float32) for a in range(3)], indexing='ij')


def catmull_rom(P, per=12):
    P = np.vstack((2 * P[0] - P[1], P, 2 * P[-1] - P[-2]))
    out = []
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for t in np.linspace(0, 1, per, endpoint=False):
            out.append(.5 * ((2 * p1) + (-p0 + p2) * t + (2 * p0 - 5 * p1 + 4 * p2 - p3) * t * t
                             + (-p0 + 3 * p1 - 3 * p2 + p3) * t ** 3))
    out.append(P[-2])
    return np.array(out)


def torso_mesh(seg=64):
    phi = np.arange(seg) * 2 * np.pi / seg
    V, F = [], []
    profile = catmull_rom(TORSO)
    for z, a, b in profile:
        c, s = np.cos(phi), np.sin(phi)
        x = a * np.sign(c) * np.abs(c) ** (2 / EXPONENT)
        y = b * np.sign(s) * np.abs(s) ** (2 / EXPONENT)
        V += list(zip(x, y, np.full(seg, z)))
    rings = len(profile)
    for j in range(rings - 1):
        for k in range(seg):
            a, b = j * seg + k, j * seg + (k + 1) % seg
            F.append((a, b, b + seg, a + seg))
    bottom, top = len(V), len(V) + 1
    V += [(0, 0, profile[0, 0] - .004), (0, 0, profile[-1, 0] + .004)]
    tris = [(bottom, (k + 1) % seg, k) for k in range(seg)]
    tris += [(top, (rings - 1) * seg + k, (rings - 1) * seg + (k + 1) % seg) for k in range(seg)]
    return np.array(V), np.array(F), np.array(tris)


def level_set(points, tris=None, quads=None):
    grid = vdb.FloatGrid.createLevelSetFromPolygons(
        points.astype(np.float32), None if tris is None else tris.astype(np.uint32),
        None if quads is None else quads.astype(np.uint32), vdb.createLinearTransform(VS), 20.0)
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


def blur(a, r, passes=3):
    for axis in range(3):
        for _ in range(passes):
            c = np.cumsum(np.pad(a, [(r + 1, r) if i == axis else (0, 0) for i in range(3)], mode='edge'), axis=axis)
            hi = np.take(c, np.arange(2 * r + 1, c.shape[axis]), axis=axis)
            lo = np.take(c, np.arange(0, c.shape[axis] - 2 * r - 1), axis=axis)
            a = (hi - lo) / (2 * r + 1)
    return a.astype(np.float32)


BLUR = int(env('BLUR', '2')[0])
TV, TQ, TT = torso_mesh()
TORSO_SDF = level_set(TV, TT, TQ)
if BLUR:
    TORSO_SDF = blur(TORSO_SDF, BLUR)


def pose_matrix(yaw):
    pivot = Vector((0, 0, .3))
    return Matrix.Translation(pivot) @ Matrix.Rotation(np.radians(yaw), 4, 'Z') @ Matrix.Translation(-pivot)


def build(pose):
    spec = POSES[pose]
    M = np.array(pose_matrix(spec['yaw']))
    R, t = M[:3, :3], M[:3, 3]
    # Rotate the sample grid instead of the torso (inverse transform), only for posed torso.
    body = TORSO_SDF if spec['yaw'] == 0 else level_set(TV @ R.T + t, TT, TQ)
    if spec['yaw'] != 0 and BLUR:
        body = blur(body, BLUR)
    hip_l, foot_l, rh, rf = np.array(LEG[0:3]), np.array(LEG[3:6]), LEG[6], LEG[7]
    for side in (1, -1):
        m = np.array((side, 1, 1))
        leg, _ = capsule(hip_l * m, foot_l * m, rh, rf)
        body = smin(body, leg, LEG_K)
    for side, d in spec['arms'].items():
        m = np.array((side, 1, 1))
        ball = np.array(BALL[:3]) * m
        pts = ARM_PTS[:, :3] * m
        # Pose: rotate the idle arm about the shoulder ball so that it points along d.
        idle_dir = (pts[-1] - pts[0]) / np.linalg.norm(pts[-1] - pts[0])
        rot = np.array(Vector(idle_dir).rotation_difference(Vector(d)).to_matrix()) if pose != 'Idle' else np.eye(3)
        pts = (pts - ball) @ rot.T + ball
        pts = pts @ R.T + t
        ball = R @ ball + t
        bx, by, bz = GX - ball[0], GY - ball[1], GZ - ball[2]
        arm = np.sqrt(bx * bx + by * by + bz * bz) - BALL[3]
        a1, s = capsule(pts[0], pts[1], ARM_PTS[0, 3], ARM_PTS[1, 3])
        a2, _ = capsule(pts[1], pts[2], ARM_PTS[1, 3], ARM_PTS[2, 3])
        arm = smin(arm, np.minimum(a1, a2), BALL_K)
        f = np.clip((s - ARM_FADE[0]) / (ARM_FADE[1] - ARM_FADE[0]), 0, 1)
        body = smin(body, arm, ARM_K + (.002 - ARM_K) * f * f * (3 - 2 * f))
    grid = vdb.FloatGrid()
    grid.copyFromArray(np.ascontiguousarray(body, np.float32), ijk=tuple(int(i) for i in I0))
    grid.transform = vdb.createLinearTransform(VS)
    points, quads = grid.convertToQuads(isovalue=0.0)
    head = np.stack((HEAD['rest'][:, 0], -HEAD['rest'][:, 2], HEAD['rest'][:, 1]), 1) @ R.T + t
    return [mesh_object('body', points, quads[:, ::-1]), mesh_object('head', head, HEAD['tri'])]


# ---------------------------------------------------------------- scene
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
for name, loc, energy, radius in [('Key', (-2, -3, 2.8), 520, 1.2), ('Rim', (2, 1, 2), 220, 1.2),
                                  ('Fill', (2, -3, 1), 60, 2)]:
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
VIEWS = {'Front': (0, -1, 0), 'ThreeQuarter': (-.67, -1, .25), 'Side': (-1, 0, 0)}


def render(path, view, match_reference=False):
    direction = Vector(VIEWS[view]).normalized()
    if match_reference:
        # Same pixel scale/placement as the reference: 525 px per metre, ground at y=540, centre x=161.
        scene.render.resolution_x, scene.render.resolution_y = 325, 558
        camera.data.ortho_scale = 558 / 525
        target = Vector(((162.5 - 161) / 525, 0, (540 - 279) / 525))
        scene.render.film_transparent = True
    else:
        scene.render.resolution_x, scene.render.resolution_y = 520, 600
        camera.data.ortho_scale = 1.25
        target = Vector((0, 0, .5))
        scene.render.film_transparent = False
    camera.location = target + direction * 3
    camera.rotation_euler = (target - camera.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


def silhouette_overlay(ours_path, out_path):
    ours = bpy.data.images.load(str(ours_path))
    ref = bpy.data.images.load(str(REF))
    w, h = ref.size
    A = np.array(ours.pixels[:]).reshape(h, w, 4)[::-1, :, 3] > .5
    R = np.array(ref.pixels[:]).reshape(h, w, 4)[::-1]
    B = (R[..., 3] > .5) & (R[..., 0] > .45) & (R[..., 1] < .45)
    iou = (A & B).sum() / max(1, (A | B).sum())
    img = np.zeros((h, w, 4))
    img[..., 3] = 1
    img[B & ~A] = (1, .2, .2, 1)       # reference only: red
    img[A & ~B] = (.2, .8, 1, 1)       # ours only: cyan
    img[A & B] = (.85, .85, .85, 1)    # both
    out = bpy.data.images.new('overlay', w, h, alpha=True)
    out.pixels = img[::-1].ravel()
    out.filepath_raw = str(out_path)
    out.file_format = 'PNG'
    out.save()
    rows = []
    for y in range(160, 540, 20):
        ra, rb = np.nonzero(A[y])[0], np.nonzero(B[y])[0]
        rows.append((y, (ra.min(), ra.max()) if len(ra) else None, (rb.min(), rb.max()) if len(rb) else None))
    return iou, rows


pose_filter = os.environ.get('POSES')
for pose in POSES:
    if pose_filter and pose not in pose_filter.split(','):
        continue
    objs = build(pose)
    if pose == 'Idle':
        render(OUT / 'Idle_RefMatch.png', 'Front', match_reference=True)
        iou, rows = silhouette_overlay(OUT / 'Idle_RefMatch.png', OUT / 'overlay_front.png')
        print(f'IOU {iou:.3f}')
        for y, a, b in rows:
            print(f'ROW y={y} z={(540 - y) / 525:.3f} ours={a} ref={b}')
    for view in os.environ.get('VIEWS', 'Front,ThreeQuarter,Side').split(','):
        render(OUT / f'{pose}_{view}.png', view)
    for o in objs:
        bpy.data.objects.remove(o)
    print('done', pose, flush=True)
