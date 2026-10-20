"""Rebuild the chest-to-belly envelope and bind the ribs to the torso.

Blender --background --python-exit-code 1 --python this.py -- output-dir
Build in the authored bind pose; the resulting skin is checked in Unity.
"""
from pathlib import Path
import hashlib
import json
import struct
import sys

import bpy
import bmesh
import numpy as np
from mathutils.kdtree import KDTree

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT / 'BlenderSource/Characters/C1B-RW-023-shallow-axilla'


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def smooth(value):
    value = np.clip(value, 0, 1)
    return value * value * (3 - 2 * value)


def soft_linear(value):
    value=np.clip(value,0,1)
    edge=.10
    return np.where(value<edge,value*value/(2*edge*(1-edge)),
                    np.where(value>1-edge,1-(1-value)**2/(2*edge*(1-edge)),(value-edge/2)/(1-edge)))


def main():
    args = sys.argv[sys.argv.index('--') + 1:]
    output = Path(args[0]).resolve()
    if output.exists():
        raise RuntimeError('Preserve existing model iterations')
    source = SOURCE / 'CHR_MasterCharacter_C1B_ShallowAxilla_r23.blend'
    data = (SOURCE / 'ShallowCharacter.bytes').read_bytes()
    size = struct.unpack_from('<i', data)[0]
    header = json.loads(data[4:4 + size])
    assert header['sourceSha256'] == sha(source)
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    body = bpy.data.objects['CHR_C1B_R20_SkinnedBody']
    mesh = body.data
    materials = list(mesh.materials)
    n, offset = len(mesh.vertices), 4 + size
    rest = np.frombuffer(data, '<f4', n * 3, offset).reshape(n, 3).copy()
    ids = np.frombuffer(data, '<i4', n * 4, offset + n * 24).reshape(n, 4)
    weights = np.frombuffer(data, '<f4', n * 4, offset + n * 40).reshape(n, 4)
    names = [bone['name'] for bone in header['bones']]
    dense = np.zeros((n, len(names)), np.float64)
    np.add.at(dense, (np.arange(n)[:, None], ids), weights)
    mapped = np.array([names.index(name[:-2] + '_R' if name.endswith('_L') else name[:-2] + '_L' if name.endswith('_R') else name) for name in names])
    original_n = n
    original_dense = dense
    # Retain the lower-body silhouette as the remesh input. Rebuild the upper
    # envelope in bind pose, with a shallow, open shoulder saddle.
    lower = bpy.data.meshes.new('R24 lower source')
    lower.from_pydata((rest[:, [0, 2, 1]] * np.array([1, -1, 1])).tolist(), [],
                      [list(p.vertices) for p in mesh.polygons])
    bm = bmesh.new()
    bm.from_mesh(lower)
    bmesh.ops.bisect_plane(bm, geom=list(bm.verts)+list(bm.edges)+list(bm.faces),
                                plane_co=(0, 0, .32), plane_no=(0, 0, 1), clear_outer=True)
    boundary = [e for e in bm.edges if e.is_boundary]
    bmesh.ops.holes_fill(bm, edges=boundary, sides=0)
    bm.to_mesh(lower)
    bm.free()
    pieces = []
    base = bpy.data.objects.new('R24 lower volume', lower)
    bpy.context.collection.objects.link(base)
    pieces.append(base)

    def ellipsoid(center, radius):
        bpy.ops.mesh.primitive_uv_sphere_add(segments=48, ring_count=32,
                                           location=(center[0], -center[2], center[1]))
        obj = bpy.context.object
        obj.scale = (radius[0], radius[2], radius[1])
        bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
        pieces.append(obj)

    # A single continuous rib/belly profile avoids a seam between stacked
    # ellipsoids and keeps the chest as broad as the upper abdomen.
    profile_y=np.array([.235,.28,.34,.42,.50,.57,.63,.675,.705,.718])
    profile_x=np.array([.145,.165,.173,.176,.176,.171,.150,.111,.060,0])
    profile_z=np.array([.125,.141,.149,.151,.151,.144,.119,.080,.037,0])
    ys=np.linspace(.235,.718,145)
    widths=np.interp(ys,profile_y,profile_x)
    depths=np.interp(ys,profile_y,profile_z)
    for _ in range(8):
        widths[1:-1]=(widths[:-2]+2*widths[1:-1]+widths[2:])*.25
        depths[1:-1]=(depths[:-2]+2*depths[1:-1]+depths[2:])*.25
    vertices=[]
    for y,rx,rz in zip(ys[:-1],widths[:-1],depths[:-1]):
        for angle in np.arange(64)*2*np.pi/64:
            vertices.append((rx*np.cos(angle),rz*np.sin(angle),y))
    faces=[]
    for ring in range(len(ys)-2):
        for col in range(64):
            a=ring*64+col;b=ring*64+(col+1)%64
            faces.append((a,b,b+64,a+64))
    bottom=len(vertices);vertices.append((0,0,ys[0]))
    top=len(vertices);vertices.append((0,0,ys[-1]))
    for col in range(64):
        faces.append((bottom,(col+1)%64,col))
        faces.append((top,(len(ys)-2)*64+col,(len(ys)-2)*64+(col+1)%64))
    trunk_mesh=bpy.data.meshes.new('Continuous ribcage')
    trunk_mesh.from_pydata(vertices,[],faces)
    trunk=bpy.data.objects.new('Continuous ribcage',trunk_mesh)
    bpy.context.collection.objects.link(trunk);pieces.append(trunk)
    for sign in (-1, 1):
        ellipsoid((sign*.164, .613, 0), (.075, .055, .069))
        for reach in np.linspace(.20, .534, 20):
            radius = .043-.004*smooth((reach-.28)/.20)
            ellipsoid((sign*reach, .618, 0), (radius, radius, radius))
    bpy.ops.object.select_all(action='DESELECT')
    for piece in pieces:
        piece.select_set(True)
    bpy.context.view_layer.objects.active = base
    bpy.ops.object.join()
    remesh = base.modifiers.new('Connected rounded volumes', 'REMESH')
    remesh.mode = 'VOXEL'
    remesh.voxel_size = .005
    remesh.use_smooth_shade = True
    bpy.ops.object.modifier_apply(modifier=remesh.name)
    relax = base.modifiers.new('Round shoulder junctions', 'SMOOTH')
    relax.factor, relax.iterations = 1.0, 24
    bpy.ops.object.modifier_apply(modifier=relax.name)
    symmetry = base.modifiers.new('Exact bilateral geometry', 'MIRROR')
    symmetry.use_bisect_axis[0] = True
    symmetry.use_clip = True
    symmetry.merge_threshold = .00001
    bpy.ops.object.modifier_apply(modifier=symmetry.name)
    bm=bmesh.new()
    bm.from_mesh(base.data)
    bmesh.ops.remove_doubles(bm, verts=list(bm.verts), dist=.00002)
    bmesh.ops.holes_fill(bm, edges=[edge for edge in bm.edges if edge.is_boundary], sides=0)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    assert all(edge.is_manifold for edge in bm.edges)
    bm.to_mesh(base.data)
    bm.free()
    subdiv = base.modifiers.new('Smooth final surface', 'SUBSURF')
    subdiv.levels = 1
    bpy.ops.object.modifier_apply(modifier=subdiv.name)
    triangulate = base.modifiers.new('Transport triangles', 'TRIANGULATE')
    bpy.ops.object.modifier_apply(modifier=triangulate.name)
    mesh = base.data.copy()
    mesh.materials.clear()
    for material in materials:
        mesh.materials.append(material)
    body.data = mesh
    bpy.data.objects.remove(base, do_unlink=True)
    n = len(mesh.vertices)
    target = np.empty((n, 3), np.float64)
    mesh.vertices.foreach_get('co', target.ravel())
    target = target[:, [0, 2, 1]] * np.array([1, 1, -1])
    triangles = np.array([list(p.vertices) for p in mesh.polygons], dtype='<i4')
    tree = KDTree(original_n)
    for i, point in enumerate(rest):
        tree.insert(point, i)
    tree.balance()
    closest=[tree.find_n(p,4) for p in target]
    nearest=np.array([[entry[1] for entry in row] for row in closest])
    distance=np.array([[entry[2] for entry in row] for row in closest])
    influence=1/np.maximum(distance,.0005)**2
    influence/=influence.sum(1)[:,None]
    transferred=(original_dense[nearest]*influence[:,:,None]).sum(1)
    x, y = np.abs(target[:, 0]), target[:, 1]
    dense = np.zeros((n, len(names)), np.float64)
    pelvis = 1-smooth((y-.27)/.17)
    chest = smooth((y-.43)/.14)
    dense[:, names.index('Pelvis')] = pelvis
    dense[:, names.index('Spine')] = (1-pelvis)*(1-chest)
    dense[:, names.index('Chest')] = (1-pelvis)*chest
    # The ribs remain entirely trunk-owned below the shoulder. Above the
    # armpit the helper provides a broad, continuous shoulder blend.
    shoulder = smooth((y-.42)/.09)
    arm = smooth((x-.12)/.24) * shoulder
    helper = 2*arm*(1-arm)
    upper = arm*arm
    dense *= (1-helper-upper)[:, None]
    forearm = soft_linear((x-.20)/.30)
    for sign, suffix in ((-1, 'L'), (1, 'R')):
        side = (target[:, 0]*sign > 0)
        dense[:, names.index('ShoulderBlend_'+suffix)] += helper*side
        dense[:, names.index('UpperArm_'+suffix)] += upper*(1-forearm)*side
        dense[:, names.index('Forearm_'+suffix)] += upper*forearm*side
    preserve = 1-smooth((y-.20)/.07)
    dense = dense*(1-preserve[:, None])+transferred*preserve[:, None]
    # The remesh adds samples around the foot sole. Bind that region smoothly
    # instead of inheriting nearest-vertex steps at the ankle.
    low=1-smooth((y-.04)/.04)
    foot=1-smooth((y-.02)/.045)
    dense*=1-low[:,None]
    for sign,suffix in ((-1,'L'),(1,'R')):
        side=target[:,0]*sign>0
        dense[:,names.index('Foot_'+suffix)]+=low*foot*side
        dense[:,names.index('Calf_'+suffix)]+=low*(1-foot)*side
    tree = KDTree(n)
    for i, point in enumerate(target):
        tree.insert(point, i)
    tree.balance()
    mirror = np.array([tree.find((-float(p[0]), float(p[1]), float(p[2])))[1] for p in target])
    left, center = np.flatnonzero(target[:, 0] < -1e-6), np.abs(target[:, 0]) <= 1e-6
    dense[mirror[left]] = dense[left][:, mapped]
    # Midline vertices must retain complete left/right influence pairs when
    # fitting four slots; a normal top-four sort can break a tie asymmetrically.
    for vertex in np.flatnonzero(center):
        weights_at=(dense[vertex]+dense[vertex,mapped])*.5
        groups=[(weights_at[i], [i] if mapped[i]==i else [i,mapped[i]]) for i in range(len(names)) if i<=mapped[i] and weights_at[i]>0]
        dense[vertex]=0
        slots=4
        for _,group in sorted(groups,key=lambda item:item[0],reverse=True):
            if len(group)<=slots:
                dense[vertex,group]=weights_at[group];slots-=len(group)
        dense[vertex]/=dense[vertex].sum()
    new_ids = np.argsort(dense, axis=1)[:, -4:][:, ::-1].astype('<i4')
    new_weights = np.take_along_axis(dense, new_ids, axis=1)
    new_weights /= new_weights.sum(1)[:, None]
    result = target.copy()
    result[mirror[left]] = result[left] * np.array([-1, 1, 1])
    result[center, 0] = 0
    result = result.astype('<f4')
    cross = np.cross(result[triangles[:, 1]] - result[triangles[:, 0]], result[triangles[:, 2]] - result[triangles[:, 0]])
    assert np.isfinite(result).all() and np.linalg.norm(cross, axis=1).min() > 1e-12
    mesh.vertices.foreach_set('co', (result[:, [0, 2, 1]] * np.array([1, -1, 1], np.float32)).ravel())
    mesh.update()
    # Clear first: source and transport must contain the same four influences.
    body.vertex_groups.clear()
    for name in names:
        body.vertex_groups.new(name=name)
    for i in range(n):
        for index, value in zip(new_ids[i], new_weights[i]):
            if value > 0:
                body.vertex_groups[names[index]].add([i], float(value), 'REPLACE')
    output.mkdir(parents=True)
    blend = output / 'CHR_MasterCharacter_C1B_RibcageVolume_r24.blend'
    body['ribcage_rework'] = 'Broad convex chest/ribs/upper belly; torso-owned ribs; mirrored shape and binding'
    bpy.ops.wm.save_as_mainfile(filepath=str(blend))
    normals = np.empty((n, 3), np.float32)
    mesh.vertices.foreach_get('normal', normals.ravel())
    normals = normals[:, [0, 2, 1]] * np.array([1, 1, -1], np.float32)
    normals[mirror[left]] = normals[left] * np.array([-1, 1, 1])
    blocks, current = [], offset
    for record in header['meshes']:
        count = record['vertices']
        end = current + count * 56 + record['triangleIndices'] * 4
        block = bytearray(data[current:end])
        if record['name'] == body.name:
            block = result.tobytes() + normals.astype('<f4').tobytes() + new_ids.tobytes() + new_weights.astype('<f4').tobytes() + triangles.tobytes()
            record['vertices'] = n
            record['triangleIndices'] = int(triangles.size)
        blocks.append(block)
        current = end
    assert current == len(data)
    header['sourceSha256'] = sha(blend)
    header['ribcageVolumeCandidate'] = True
    encoded = json.dumps(header).encode()
    transport = output / 'RibcageCharacter.bytes'
    transport.write_bytes(struct.pack('<i', len(encoded)) + encoded + b''.join(blocks))
    report = {'source': str(source), 'sourceSha256': sha(source), 'candidateSha256': sha(blend),
              'transportSha256': sha(transport), 'vertices': n, 'triangleIndices': int(triangles.size),
              'mirrorMaxMeters': float(np.linalg.norm(result - result[mirror] * np.array([-1, 1, 1]), axis=1).max()),
              'headAndSkeletonPreserved': True, 'topologyPreserved': False, 'visualApproved': False}
    (output / 'RibcageReview.json').write_text(json.dumps(report, indent=2) + '\n')
    print(json.dumps(report), flush=True)


if __name__ == '__main__':
    main()
