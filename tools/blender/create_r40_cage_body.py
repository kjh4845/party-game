"""Build the r40 control-cage body fitted to the approved r16 silhouette.

Blender --background --python this.py -- output-dir [levels]

Writes cage.npz (cage positions/faces/tags, subdivision operator inputs) and
CageBody.blend (cage with a Subdivision modifier for future hand editing).
The fitted shape follows r16 by closest-point least squares while a cage
Laplacian keeps the loops evenly spaced. Smoothness is a property of the
subdivision surface, not of any smoothing pass on a dense mesh.
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

ROOT = Path(__file__).resolve().parents[2]
R16 = ROOT / 'BlenderSource/Characters/C1B-RW-016-preview/CHR_MasterCharacter_C1B_NeutralRework_r16.blend'
LEVELS = 2

SEG = lib.TORSO_SEGMENTS
PHI = np.arange(SEG) * 2 * np.pi / SEG
EXPONENT = 2.6
# Torso rings: height, half width, half depth (r16 measurements, cage sits slightly outside).
TORSO = [
    (.150, .167, .090), (.200, .170, .116), (.260, .170, .131), (.320, .167, .143), (.380, .163, .144),
    (.440, .161, .141), (.500, .162, .139), (.528, .164, .138), (.555, .167, .137), (.620, .172, .135), (.678, .155, .114),
    (.705, .112, .082),
]
ARM_ROWS = (8, 9, 10)  # rings bounding the arm opening; the middle ring vertex at the side is removed
# Arm rings: x, radius (front/back), radius (up/down), centre height.
ARM = [
    (.190, .050, .046, .629), (.210, .043, .040, .632), (.235, .040, .037, .634), (.265, .039, .036, .635),
    (.305, .038, .035, .635), (.340, .038, .035, .635), (.372, .038, .035, .635), (.405, .037, .034, .635),
    (.455, .036, .034, .635), (.505, .035, .033, .635), (.543, .034, .032, .635),
]
ARM_TIP = .574
LEG_CENTRE, LEG_RADIUS = .098, .068
LEGS = [(.105, 1.0), (.070, .99), (.036, .97), (.011, .90)]
CROTCH_Z = .126


def build(arms=True):
    cage = lib.Cage()
    rings = []
    for j, (z, a, b) in enumerate(TORSO):
        ring = []
        for k, phi in enumerate(PHI):
            x, y = lib.superellipse(a, b, phi, EXPONENT)
            if arms and j in ARM_ROWS[1:2] and k in (SEG // 4, 3 * SEG // 4):
                ring.append(None)  # opening centre for the arm
                continue
            ring.append(cage.add((x, y, z), region='torso', ring=j, column=k))
        rings.append(ring)
    for j in range(len(rings) - 1):
        for k in range(SEG):
            a, b = rings[j][k], rings[j][(k + 1) % SEG]
            c, d = rings[j + 1][(k + 1) % SEG], rings[j + 1][k]
            if None in (a, b, c, d):
                continue
            cage.quad(a, b, c, d)
    # Top cap: square grid with corners on the diagonal columns.
    top = rings[-1]
    q = SEG // 4
    boundary = [top[(SEG - q // 2 + i) % SEG] for i in range(SEG)]
    cage.cap(boundary, q, q, (0, 0, .016), region='top')

    # Arms: the 8-vertex border of each opening becomes the first arm loop.
    for side, centre in ((1, SEG // 4), (-1, 3 * SEG // 4)) if arms else ():
        lo, mid, hi = ARM_ROWS
        before, after = (centre - side) % SEG, (centre + side) % SEG
        # Order around the arm axis: bottom-front, bottom, bottom-back, back, top-back, top, top-front, front.
        front, back = before, after  # 'before' is always the -Y (front) column
        border = [rings[lo][front], rings[lo][centre], rings[lo][back], rings[mid][back],
                  rings[hi][back], rings[hi][centre], rings[hi][front], rings[mid][front]]
        directions = [(-1, -1), (0, -1), (1, -1), (1, 0), (1, 1), (0, 1), (-1, 1), (-1, 0)]
        previous = border
        for i, (x, ry, rz, zc) in enumerate(ARM):
            loop = []
            for dy, dz in directions:
                norm = np.hypot(dy, dz)
                loop.append(cage.add((side * x, ry * dy / norm * 1.08, zc + rz * dz / norm * 1.08),
                                     region='arm', side=side, station=i))
            cage.bridge(previous, loop)
            previous = loop
        # 2x2 end cap; boundary walk starts at a corner.
        cap_boundary = [previous[0], previous[1], previous[2], previous[3], previous[4], previous[5],
                        previous[6], previous[7]]
        cage.cap(cap_boundary, 2, 2, (side * (ARM_TIP - ARM[-1][0]) * 1.25, 0, 0), region='hand', side=side)

    # Legs: half of the bottom torso ring plus a crotch bridge shared by both legs.
    bottom = rings[0]
    bridge = [cage.add((0, y, CROTCH_Z), region='crotch') for y in (.045, 0, -.045)]  # back -> front
    for side in (1, -1):
        if side == 1:
            loop = [bottom[k] for k in range(0, SEG // 2 + 1)] + bridge
        else:
            loop = [bottom[k % SEG] for k in range(SEG, SEG // 2 - 1, -1)] + bridge
        centre = np.array((side * LEG_CENTRE, 0))
        first = cage.verts[loop[0]][:2] - centre
        second = cage.verts[loop[2]][:2] - centre
        start = np.arctan2(first[1], first[0])
        turn = np.sign(first[0] * second[1] - first[1] * second[0])
        previous = loop
        for i, (z, scale) in enumerate(LEGS):
            ring = []
            for j in range(len(loop)):
                angle = start + turn * j * 2 * np.pi / len(loop)
                xy = centre + np.array((np.cos(angle), np.sin(angle))) * LEG_RADIUS * scale * 1.06
                ring.append(cage.add((xy[0], xy[1], z), region='leg', side=side, station=i))
            cage.bridge(previous, ring)
            previous = ring
        # 3x3 sole cap; corners land on the leg diagonals.
        cage.cap(previous, len(previous) // 4, len(previous) // 4, (0, 0, -.013), region='sole', side=side)
    return cage


def mirror_map(P):
    mirrored = P * np.array((-1, 1, 1))
    index = np.array([np.argmin(np.linalg.norm(P - m, axis=1)) for m in mirrored])
    error = np.linalg.norm(P[index] - mirrored, axis=1).max()
    assert error < 1e-6, error
    return index


def load_r16():
    with bpy.data.libraries.load(str(R16)) as (src, dst):
        dst.objects = [n for n in src.objects if 'TPoseBody' in n]
    obj = dst.objects[0]
    mesh = obj.data
    verts = [obj.matrix_world @ v.co for v in mesh.vertices]
    polys = [tuple(p.vertices) for p in mesh.polygons]
    return BVHTree.FromPolygons(verts, polys), np.array([tuple(v) for v in verts])


def fit(cage, bvh, iterations=30, smooth=.6, levels=LEVELS, exclude=None, plate=None):
    C0 = np.array(cage.verts)
    faces = lib.orient_outward(C0, cage.faces)
    cage.faces = faces
    S, sub_faces = lib.subdivision(len(C0), faces, levels)
    A = lib.limit_operator(S.shape[0], sub_faces) @ S
    L, _ = lib.umbrella(len(C0), faces)
    mirror = mirror_map(C0)
    C = C0.copy()
    LtL = L.T @ L
    # Inside `plate` the cage keeps no memory of its initial bumps: zero Laplacian (thin plate).
    keep = np.ones(len(C0)) if plate is None else np.array([0.0 if plate(c) else 1.0 for c in C0])
    reference = L.T @ (keep[:, None] * (L @ C0))
    for it in range(iterations):
        P = A @ C
        N = lib.vertex_normals(P, sub_faces)
        T = np.empty_like(P)
        w = np.zeros(len(P))
        for i, (p, n) in enumerate(zip(P, N)):
            hit, normal, _, _ = bvh.find_nearest(Vector(p))
            if hit is None:
                continue
            T[i] = hit
            agree = np.dot(n, normal)
            w[i] = np.clip((agree - .2) / .5, 0, 1)
            if exclude is not None and exclude(np.array(hit)):
                w[i] = 0
        W = A * w[:, None]
        lhs = A.T @ W + smooth * LtL + 1e-3 * np.eye(len(C))
        rhs = W.T @ T + smooth * reference + 1e-3 * C
        C = np.linalg.solve(lhs, rhs)
        C = .5 * (C + C[mirror] * np.array((-1, 1, 1)))
        if it % 5 == 4 or it == iterations - 1:
            d = np.linalg.norm(A @ C - T, axis=1)[w > .5]
            print(f'fit {it}: mean {d.mean()*1000:.2f} mm, p95 {np.percentile(d,95)*1000:.2f} mm, max {d.max()*1000:.2f} mm')
    return C, faces, S, sub_faces, A


def main():
    args = sys.argv[sys.argv.index('--') + 1:]
    OUT = Path(args[0]).resolve()
    OUT.mkdir(parents=True, exist_ok=True)
    cage = build()
    bvh, _ = load_r16()
    C, faces, S, sub_faces, A = fit(cage, bvh)
    P = A @ C
    angles, _ = lib.face_angles(P, sub_faces)
    report = {
        'cageVertices': len(C), 'cageFaces': len(faces), 'levels': LEVELS,
        'subdividedVertices': int(P.shape[0]), 'subdividedFaces': len(sub_faces),
        'maxNeighbourFaceAngle': float(angles.max()), 'p99NeighbourFaceAngle': float(np.percentile(angles, 99)),
        'bounds': [P.min(0).tolist(), P.max(0).tolist()],
    }
    print(json.dumps(report, indent=1))
    np.savez(OUT / 'cage.npz', cage=C, faces=np.array(faces), tags=json.dumps(cage.tags), levels=LEVELS)
    (OUT / 'CageReport.json').write_text(json.dumps(report, indent=1))
    # Blender source: editable cage + subdivision modifier.
    for obj in list(bpy.data.objects):
        bpy.data.objects.remove(obj)
    mesh = bpy.data.meshes.new('C1B_R40_BodyCage')
    mesh.from_pydata(C.tolist(), [], [list(f) for f in faces])
    mesh.polygons.foreach_set('use_smooth', [True] * len(mesh.polygons))
    obj = bpy.data.objects.new('C1B_R40_BodyCage', mesh)
    bpy.context.collection.objects.link(obj)
    mod = obj.modifiers.new('Subdivision', 'SUBSURF')
    mod.levels = mod.render_levels = LEVELS
    bpy.ops.wm.save_as_mainfile(filepath=str(OUT / 'CHR_MasterCharacter_C1B_CageBody_r40.blend'))


if __name__ == '__main__':
    main()
