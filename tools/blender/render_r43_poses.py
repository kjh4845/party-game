"""Pose an r43 transport with recorded Unity punch/grab/lift captures (retargeted onto the
r43 bone lengths) and synthetic sweeps; render and measure the arm/torso junction.

Blender -b -P render_r43_poses.py -- transport out-dir capture-dir [capture-dir ...]
Env: R43_POSES (comma list of capture stems or syn:side:elev:az:flex:twist), R43_VIEWS, R43_RENDER=0

Metrics per pose (body mesh):
- shoulderFold / elbowFold: largest increase of a neighbour-triangle angle vs bind (deg)
- collapse: smallest posed/bind triangle area ratio in the shoulder region
- intersections: shoulder-region triangle pairs that intersect without sharing a vertex
"""
import json
import os
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_transport as rt  # noqa: E402

args = sys.argv[sys.argv.index('--') + 1:]
T = rt.Transport(args[0])
OUT = Path(args[1])
OUT.mkdir(parents=True, exist_ok=True)
R40 = rt.Transport(rt.ROOT / 'BlenderSource/Characters/C1B-RW-040-cage-body/CageBodyCharacter.bytes')
captures = {}
for d in args[2:]:
    for p in sorted(Path(d).glob('*_Matrices.bin')):
        stem = p.name[:-len('_Matrices.bin')]
        n_bones = (len(p.read_bytes()) - 4) // 64
        own = 'r43-unity' in str(p)
        captures[('own:' if own else 'r40:' if n_bones == 28 else '') + stem] = p
body = T.meshes[0]
tri, rest = body['tri'], body['rest']
n = len(rest)
I = T.index
dense = np.zeros((n, len(T.names)))
for k in range(4):
    np.add.at(dense, (np.arange(n), body['ids'][:, k]), body['weights'][:, k])
helpers = [i for nm, i in I.items() if nm.startswith('ShoulderBlend')]
upper = dense[:, [I['UpperArm_L'], I['UpperArm_R']]].sum(1)
fore = dense[:, [I['Forearm_L'], I['Forearm_R']]].sum(1)
shoulder_v = (dense[:, helpers].sum(1) > .01) | ((upper > .01) & (upper < .99))
elbow_v = (upper > .01) & (fore > .01)
edges = {}
for fi, (a, b, c) in enumerate(tri):
    for x, y in ((a, b), (b, c), (c, a)):
        edges.setdefault((min(x, y), max(x, y)), []).append(fi)
pairs = np.array([v for v in edges.values() if len(v) == 2])
sh_e = shoulder_v[tri[pairs[:, 0]]].any(1)
el_e = elbow_v[tri[pairs[:, 0]]].any(1)
sh_t = shoulder_v[tri].any(1)


def face(P):
    a, b, c = P[tri[:, 0]], P[tri[:, 1]], P[tri[:, 2]]
    nr = np.cross(b - a, c - a)
    ar = np.linalg.norm(nr, axis=1)
    return nr / (ar[:, None] + 1e-20), ar


def vnormals(P):
    fn, fa = face(P)
    acc = np.zeros_like(P)
    for k in range(3):
        np.add.at(acc, tri[:, k], fn * fa[:, None])
    return acc / (np.linalg.norm(acc, axis=1, keepdims=True) + 1e-20)


bn, ba = face(rest)
big = ba > .2 * np.median(ba)     # marching-cube slivers are excluded from area/intersection checks
uedges = np.unique(np.sort(np.concatenate((tri[:, [0, 1]], tri[:, [1, 2]], tri[:, [2, 0]])), 1), axis=0)
sh_ue = shoulder_v[uedges].any(1)
el_ue = elbow_v[uedges].any(1)
bvn = vnormals(rest)
bind_angle = np.degrees(np.arccos(np.clip((bvn[uedges[:, 0]] * bvn[uedges[:, 1]]).sum(1), -1, 1)))
bind_len = np.linalg.norm(rest[uedges[:, 0]] - rest[uedges[:, 1]], axis=1)


def audit(P):
    """fold: largest increase of the angle between neighbouring vertex normals (deg);
    collapse: smallest area ratio of non-sliver shoulder triangles; intersections on non-slivers."""
    vn = vnormals(P)
    ang = np.degrees(np.arccos(np.clip((vn[uedges[:, 0]] * vn[uedges[:, 1]]).sum(1), -1, 1))) - bind_angle
    # Only folds on the visible surface count: a vertex whose outward ray hits the body within
    # 3 cm is inside a contact (arm pressed to the torso), hidden in every view.
    full = BVHTree.FromPolygons([Vector(v) for v in P], tri.tolist())
    cand = np.unique(uedges[(ang > 30) & (sh_ue | el_ue)])
    hidden = np.zeros(len(P), bool)
    for v in cand:
        hit = full.ray_cast(Vector(P[v] + vn[v] * 1e-3), Vector(vn[v]), .03)
        hidden[v] = hit[0] is not None
    ang = np.where(hidden[uedges].any(1), 0, ang)
    global LAST_FOLD
    LAST_FOLD = np.zeros(len(P))
    np.maximum.at(LAST_FOLD, uedges[:, 0], ang)
    np.maximum.at(LAST_FOLD, uedges[:, 1], ang)
    _, pa = face(P)
    ratio = pa / (ba + 1e-20)
    sel = np.nonzero(sh_t & big)[0]
    sub = tri[sel]
    tree = BVHTree.FromPolygons([Vector(v) for v in P], sub.tolist())
    sets = [set(t) for t in sub.tolist()]
    hits = [(i, j) for i, j in tree.overlap(tree) if i < j and not (sets[i] & sets[j])]
    worst = [round(float(v), 3) for v in P[sub[hits[0][0]]].mean(0)] if hits else None
    # Arm-into-torso contact is expected (the approved idle presses the arm to the torso);
    # report it separately from visible folds.
    tn, _ = face(P)
    abs_angle = np.degrees(np.arccos(np.clip((tn[pairs[:, 0]] * tn[pairs[:, 1]]).sum(1), -1, 1)))
    global ABS_V
    ABS_V = np.zeros(len(P))
    np.maximum.at(ABS_V, tri[pairs[:, 0]].ravel(), np.repeat(abs_angle, 3))
    np.maximum.at(ABS_V, tri[pairs[:, 1]].ravel(), np.repeat(abs_angle, 3))
    folded = np.zeros(len(P), bool)
    folded[uedges[(ang > 45) & (sh_ue | el_ue)].ravel()] = True
    return {'unityMaxAngle': float(abs_angle.max()), 'over60': int((abs_angle > 60).sum()),
            'visibleFoldVertices45': int(folded.sum()), 'shoulderFold': float(ang[sh_ue].max()), 'elbowFold': float(ang[el_ue].max()) if el_ue.any() else 0.0,
            'collapse': float(ratio[sh_t & big].min()), 'intersections': len(hits), 'intersectionAt': worst}


def pose_world(spec):
    if spec == 'bind':
        return T.rest_world()
    if spec == 'idle':
        return T.idle_world()
    if spec.startswith('syn:'):
        side, elev, az, flex, twist = [float(v) for v in spec[4:].split(':')]
        return T.synthetic(int(side), elev, az, flex, twist)
    if spec.startswith('own:'):
        return T.world_from_own_recording(captures[spec])
    raw = captures[spec].read_bytes()
    palette = np.frombuffer(raw, '<f4', offset=4).reshape(-1, 4, 4).astype(np.float64)
    if len(palette) == len(R40.names):          # captured from the r40 28-bone rig
        palette = np.linalg.inv(palette[0]) @ palette
        return T.retarget(palette @ R40.bind)
    return T.retarget(T.world_from_recorded(captures[spec]))


default = ('bind,idle,Punch2,Punch8,Punch18,LeftBody_Worst,RightBody_Worst,Head_Worst,Alternating_Worst,Moving_Worst,'
           'Far_Worst,Wall_Worst,GrabPunch_Worst,Exchange_Worst,Lift45_Flex0,Lift90_Flex0,Lift90_Flex45,Lift90_Flex90,'
           'Lift90_Flex120,Lift120_Flex0,SweepWorst,Sweep_0090,Sweep_0270,r40:Alternating_Worst,r40:Far_Worst,r40:GrabPunch_Worst,r40:Moving_Worst,'
           'syn:1:90:0:0:0,syn:1:90:0:90:0,syn:1:130:0:0:0,syn:1:90:90:0:0,syn:1:160:45:0:0,syn:1:60:0:120:0')
poses = os.environ.get('R43_POSES', default).split(',')
poses = [p for p in poses if p in ('bind', 'idle') or p.startswith('syn:') or p in captures]

# ---------------------------------------------------------------- render scene
do_render = os.environ.get('R43_RENDER', '1') == '1'
for obj in list(bpy.data.objects):
    bpy.data.objects.remove(obj)
scene = bpy.context.scene
mat = bpy.data.materials.new('White')
mat.use_nodes = True
sh = next(nd for nd in mat.node_tree.nodes if nd.type == 'BSDF_PRINCIPLED')
sh.inputs['Base Color'].default_value = (.92, .92, .92, 1)
sh.inputs['Roughness'].default_value = .45
mesh = bpy.data.meshes.new('body')
B = rest[:, [0, 2, 1]] * np.array((1, -1, 1))
mesh.from_pydata(B.tolist(), [], tri.tolist())
mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
HEAT = os.environ.get('R43_HEAT') == '1'
if HEAT:
    attr = mesh.color_attributes.new('fold', 'FLOAT_COLOR', 'POINT')
    node = mat.node_tree.nodes.new('ShaderNodeVertexColor')
    node.layer_name = 'fold'
    mat.node_tree.links.new(node.outputs['Color'], sh.inputs['Base Color'])
mesh.materials.append(mat)
obj = bpy.data.objects.new('body', mesh)
scene.collection.objects.link(obj)
scene.render.engine = 'CYCLES'
scene.cycles.samples = 24
scene.cycles.use_denoising = True
scene.render.resolution_x, scene.render.resolution_y = 520, 600
scene.world = scene.world or bpy.data.worlds.new('World')
scene.world.use_nodes = True
bg = next(nd for nd in scene.world.node_tree.nodes if nd.type == 'BACKGROUND')
bg.inputs[0].default_value = (.12, .15, .20, 1)
bg.inputs[1].default_value = .22
scene.view_settings.view_transform = 'AgX'
for name, energy, radius in (('Key', 420, 1.6), ('Fill', 130, 2.5), ('Rim', 200, 1.2)):
    light = bpy.data.lights.new(name, 'AREA')
    light.energy, light.size = energy, radius
    lo = bpy.data.objects.new(name, light)
    scene.collection.objects.link(lo)
cam = bpy.data.objects.new('Camera', bpy.data.cameras.new('Camera'))
scene.collection.objects.link(cam)
scene.camera = cam
cam.data.type = 'ORTHO'
VIEWS = {'Front': (0, -1, 0), 'ThreeQuarter': (-.67, -1, .25), 'RightThreeQuarter': (.67, -1, .25),
         'Side': (-1, 0, 0), 'RightSide': (1, 0, 0), 'Back': (0, 1, 0), 'BackRightThreeQuarter': (.67, 1, .25),
         'Top': (-.25, -.5, 1), 'CloseRight': (.8, -1, .35), 'CloseRightBack': (.8, 1, .35),
         'ZoomFront': (.35, -1, .25), 'ZoomBack': (.35, 1, .25), 'ZoomTop': (.3, -.2, 1), 'ZoomUnder': (.6, -.6, -.6)}
views = os.environ.get('R43_VIEWS', 'Front,RightThreeQuarter,RightSide,Back,Top,CloseRight').split(',')


def render(path, view, centre):
    d = Vector(VIEWS[view]).normalized()
    close = view.startswith('Close')
    zoom = view.startswith('Zoom')
    target = Vector(centre) + (Vector((.12, 0, .1)) if close else Vector((.17, 0, .1)) if zoom else Vector((0, 0, 0)))
    cam.data.ortho_scale = .55 if close else .30 if zoom else 1.35
    cam.location = target + d * 3
    cam.rotation_euler = (target - cam.location).to_track_quat('-Z', 'Y').to_euler()
    side = d.cross(Vector((0, 0, 1)))
    side = side.normalized() if side.length > 1e-3 else Vector((1, 0, 0))
    for name, off in (('Key', d * 2.4 + side * 1.6 + Vector((0, 0, 2))), ('Fill', d * 2.6 - side * 2 + Vector((0, 0, .4))),
                      ('Rim', -d * 2.2 + Vector((0, 0, 1.6)))):
        lo = scene.objects[name]
        lo.location = target + off
        lo.rotation_euler = (target - lo.location).to_track_quat('-Z', 'Y').to_euler()
    scene.render.filepath = str(path)
    bpy.ops.render.render(write_still=True)


results = []
for spec in poses:
    world = pose_world(spec)
    P = T.skin(world, 0, correct=os.environ.get('R43_CORRECT', '1') == '1')
    r = audit(P)
    r['pose'] = spec
    results.append(r)
    print(f"{spec:28s} unityMax {r['unityMaxAngle']:6.1f} n>60 {r['over60']:4d}  fold>45 verts {r['visibleFoldVertices45']:4d}  shoulderFold {r['shoulderFold']:6.1f} elbowFold {r['elbowFold']:6.1f} "
          f"collapse {r['collapse']:.2f} x{r['intersections']}", flush=True)
    if do_render:
        Bp = P[:, [0, 2, 1]] * np.array((1, -1, 1))
        mesh.vertices.foreach_set('co', Bp.astype(np.float32).ravel())
        if HEAT:
            if os.environ.get('R43_HEAT_W') == '1':   # share of the arm rotation carried (helpers .25/.5/.75, upper arm 1)
                share = sum(dense[:, I[nm + s]] * fr for s in 'LR' for nm, fr in (('ShoulderBlendInner_', .25), ('ShoulderBlend_', .5), ('ShoulderBlendOuter_', .75), ('UpperArm_', 1.0), ('Forearm_', 1.0)))
                t = np.clip(share, 0, 1) * (share < .99)
            else:
                t = np.clip((ABS_V - 40) / 20, 0, 1) if os.environ.get('R43_HEAT_ABS') == '1' else np.clip((LAST_FOLD - 20) / 40, 0, 1)
            col = np.stack((.92 + 0 * t, .92 * (1 - t), .92 * (1 - t), np.ones_like(t)), 1)
            mesh.color_attributes['fold'].data.foreach_set('color', col.astype(np.float32).ravel())
        mesh.update()
        root = world[I['Pelvis'], :3, 3]
        centre = (float(root[0]), float(-root[2]), .5)
        label = spec.replace(':', '_')
        for v in views:
            render(OUT / f'{label}_{v}.png', v, centre)
(OUT / 'PoseAudit.json').write_text(json.dumps(results, indent=1))
