"""Build the r40 body cage in the idle pose (arms lowered) for idle binding.

Blender --background --python this.py -- output-dir

Game poses are small swings from idle (punch/grab ~35 deg, lift ~100 deg,
twist under ~12 deg) but ~90 deg plus up to ~104 deg of twist from a T-pose.
Binding the skin in idle therefore keeps the common pose undeformed and the
shoulder blend far from candy-wrapper twist.

Target shape (signed distance):
- torso + legs: a torso-only cage fitted to the approved r16 body, arms excluded
- arm: tube on the idle arm axis, passing just outside the shoulder joint centre
- smooth union only at shoulder/armpit height, so the hanging arm keeps a
  narrow open gap to the torso instead of a web.
"""
import json
import sys
from pathlib import Path

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree

sys.path.insert(0, str(Path(__file__).resolve().parent))
import r40_cage_lib as lib  # noqa: E402
import create_r40_cage_body as tcage  # noqa: E402

import r40_shape as shape  # noqa: E402
from r40_shape import ARM_LENGTH, ARM_RADII, AXIS_OFFSET, IDLE_DIR, PIVOT  # noqa: E402

LEVELS = 2
LOFT_FROM, LOFT_T = 3, (.28, .55, .80)    # first stations are lofted from the opening border


def torso_only(bvh_r16):
    cage = tcage.build(arms=False)
    arm_area = lambda p: abs(p[0]) > .150 and p[2] > .52
    shoulder = lambda c: c[2] > .50 and abs(c[0]) > .09
    C, faces, S, sub_faces, A = tcage.fit(cage, bvh_r16, iterations=25, exclude=arm_area, plate=shoulder, smooth=1.0)
    return cage, C, faces, A @ C, sub_faces


def idle_initial(torso_cage, torso_C):
    cage = tcage.build(arms=True)
    def keys(tags):
        count = {}
        out = []
        for t in tags:
            if t['region'] == 'torso':
                out.append(('torso', t['ring'], t['column']))
            else:
                n = count.get(t['region'], 0)
                count[t['region']] = n + 1
                out.append((t['region'], n))
        return out
    torso_pos = dict(zip(keys(torso_cage.tags), torso_C))
    C = np.array(cage.verts)
    for i, (tag, k) in enumerate(zip(cage.tags, keys(cage.tags))):
        if tag['region'] not in ('arm', 'hand'):
            C[i] = torso_pos[k]
    # Arm loops: stations from LOFT_FROM on hang on the idle axis at their T-pose arc length
    # from the joint; the first stations are lofted from the shoulder opening border.
    directions = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)]
    stations = {}
    for i, tag in enumerate(cage.tags):
        if tag['region'] == 'arm':
            stations.setdefault((tag['side'], tag['station']), []).append(i)
    for side in (1, -1):
        m = np.array((side, 1, 1))
        axis = IDLE_DIR * m
        top = PIVOT * m + np.array((side * AXIS_OFFSET, 0, 0))
        # Loop frame: 'up' in T (+z) becomes outward, 'back' (+y) stays back.
        outward = np.cross(np.array((0, 1, 0)), axis) * -side
        outward /= np.linalg.norm(outward)
        for st in range(len(tcage.ARM)):
            if st < LOFT_FROM:
                continue
            x_t, ry, rz, _ = tcage.ARM[st]
            s = x_t - PIVOT[0]
            centre = top + axis * s
            for v, (dy, dz) in zip(stations[(side, st)], directions):
                n = np.hypot(dy, dz)
                C[v] = centre + np.array((0, 1, 0)) * ry * dy / n * 1.08 + outward * rz * dz / n * 1.08
        # Border of the opening (torso vertices) in arm-loop order.
        border = border_loop(cage, stations[(side, 0)])
        target = stations[(side, LOFT_FROM)]
        for st in range(LOFT_FROM):
            t = LOFT_T[st]
            for b, v, w in zip(border, stations[(side, st)], target):
                C[v] = (1 - t) * C[b] + t * C[w]
        tip = [i for i, tag in enumerate(cage.tags) if tag['region'] == 'hand' and tag['side'] == side]
        end = top + axis * (ARM_LENGTH + ARM_RADII[-1][1] * .9)
        for v in tip:
            C[v] = end
    return cage, C


def border_loop(cage, first):
    """Torso vertices bridged to the first arm loop, in the same order."""
    first_set = set(first)
    partner = {}
    for f in cage.faces:
        for k in range(4):
            a, b = f[k], f[(k + 1) % 4]
            if a in first_set and b not in first_set and cage.tags[b]['region'] == 'torso':
                partner.setdefault(a, set()).add(b)
            if b in first_set and a not in first_set and cage.tags[a]['region'] == 'torso':
                partner.setdefault(b, set()).add(a)
    out = []
    for v in first:
        cands = partner[v]
        out.append(min(cands, key=lambda c: np.linalg.norm(np.array(cage.verts[c]) - np.array(cage.verts[v]))))
    return out


def fit_field(cage, C0, torso, iterations=14, smooth=.5):
    faces = lib.orient_outward(C0, cage.faces)
    cage.faces = faces
    S, sub_faces = lib.subdivision(len(C0), faces, LEVELS)
    A = lib.limit_operator(S.shape[0], sub_faces) @ S
    L, _ = lib.umbrella(len(C0), faces)
    arm_mask = np.array([t['region'] in ('arm', 'hand') for t in cage.tags], float)
    C = shape.fit(A, L.T @ L, C0, torso, shape.idle_arms(), A @ arm_mask, iterations, smooth,
                  mirror=tcage.mirror_map(C0), label='idle fit')
    return C, faces, sub_faces, A


def main():
    out = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
    out.mkdir(parents=True, exist_ok=True)
    bvh_r16, _ = tcage.load_r16()
    torso_cage, torso_C, torso_faces, torso_P, torso_sub = torso_only(bvh_r16)
    torso = shape.TorsoField(torso_P, torso_sub)
    cage, C0 = idle_initial(torso_cage, torso_C)
    C, faces, sub_faces, A = fit_field(cage, C0, torso)
    P = A @ C
    angles, _ = lib.face_angles(P, sub_faces)
    tree = BVHTree.FromPolygons([Vector(p) for p in P], lib.triangulate(sub_faces).tolist())
    tris = lib.triangulate(sub_faces)
    sets = [set(t) for t in tris.tolist()]
    hits = [(i, j) for i, j in tree.overlap(tree) if i < j and not (sets[i] & sets[j])]
    report = {'cageVertices': len(C), 'cageFaces': len(faces), 'levels': LEVELS, 'bind': 'idle',
              'subdividedVertices': int(len(P)), 'maxNeighbourFaceAngle': float(angles.max()),
              'selfIntersections': len(hits), 'bounds': [P.min(0).tolist(), P.max(0).tolist()]}
    print(json.dumps(report, indent=1))
    np.savez(out / 'cage.npz', cage=C, faces=np.array(faces), tags=json.dumps(cage.tags), levels=LEVELS,
             bind='idle', torso_surface=torso_P, torso_faces=np.array(torso_sub))
    (out / 'CageReport.json').write_text(json.dumps(report, indent=1))
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    mesh = bpy.data.meshes.new('C1B_R40_BodyCage_Idle')
    mesh.from_pydata(C.tolist(), [], [list(f) for f in faces])
    mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
    obj = bpy.data.objects.new('C1B_R40_BodyCage_Idle', mesh)
    bpy.context.collection.objects.link(obj)
    mod = obj.modifiers.new('Subdivision', 'SUBSURF')
    mod.levels = mod.render_levels = LEVELS
    bpy.ops.wm.save_as_mainfile(filepath=str(out / 'CHR_MasterCharacter_C1B_CageBody_r40.blend'))


if __name__ == '__main__':
    main()
