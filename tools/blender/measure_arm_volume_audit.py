"""Measure matching arm sections from Unity-baked poses, independent of camera.

python this.py <capture-directory> <output-json>
Fits centerlines using a fixed material-angle basis, then intersects the actual
mesh with perpendicular planes. Section area is not whole-arm volume.
"""
from pathlib import Path
import json
import struct
import sys

import numpy as np

ROOT = Path(__file__).resolve().parents[2]
captures, output = [Path(p).resolve() for p in sys.argv[1:3]]
source = ROOT/'BlenderSource/Characters/C1B-RW-028-cylindrical-upper-arms/CylindricalCharacter.bytes'
data = source.read_bytes()
size = struct.unpack_from('<i', data)[0]
header = json.loads(data[4:4+size])
n, offset = header['meshes'][0]['vertices'], 4+size
ids = np.frombuffer(data, '<i4', n*4, offset+n*24).reshape(n, 4)
weights = np.frombuffer(data, '<f4', n*4, offset+n*40).reshape(n, 4)
triangles = np.frombuffer(data, '<i4', header['meshes'][0]['triangleIndices'], offset+n*56).reshape(-1, 3)
names = [b['name'] for b in header['bones']]
reference = (ROOT/'BlenderSource/Characters/C1B-RW-026-smooth-body/SmoothCharacter.bytes').read_bytes()
material = np.frombuffer(reference, '<f4', n*3, 4+struct.unpack_from('<i', reference)[0]).reshape(n, 3)
metadata = json.loads((captures/'ArmAuditPoses.json').read_text())
sections = {'upper_middle': .30, 'upper_distal': .34, 'elbow': .36,
            'elbow_distal': .38, 'forearm_proximal': .40, 'forearm_middle': .445, 'forearm_distal': .48}
fits = {}
for side, sign in [('L', -1), ('R', 1)]:
    for label, reach in sections.items():
        selected = np.flatnonzero((np.abs(material[:, 0]*sign-reach) < .003) & (material[:, 1] > .54))
        p = material[selected]
        center_y = (p[:, 1].max()+p[:, 1].min())*.5
        theta = np.arctan2(p[:, 2], p[:, 1]-center_y)
        u = (p[:, 0]*sign-reach)/.003
        design = np.column_stack((np.ones(len(p)), u, np.cos(theta), np.sin(theta), u*np.cos(theta), u*np.sin(theta)))
        assert np.linalg.matrix_rank(design) == 6
        fits[(side, label)] = selected, design, np.linalg.pinv(design)


def plane_section(points, center, axis, sign, reach):
    """Intersect the actual mesh with the local normal plane and trace its arm loop."""
    distance = (points-center)@axis
    positive = distance >= 0
    crosses = positive[triangles]
    active = triangles[np.any(crosses, axis=1) & ~np.all(crosses, axis=1)]
    nodes, graph, parameter = {}, {}, {}
    for face in active:
        ends = []
        for a, b in ((face[0], face[1]), (face[1], face[2]), (face[2], face[0])):
            if positive[a] == positive[b]:
                continue
            key = int(min(a, b))*n+int(max(a, b))
            if key not in nodes:
                t = distance[a]/(distance[a]-distance[b])
                nodes[key] = points[a]+t*(points[b]-points[a])
                parameter[key] = material[a]+t*(material[b]-material[a])
            ends.append(key)
        assert len(ends) == 2
        a, b = ends
        graph.setdefault(a, []).append(b)
        graph.setdefault(b, []).append(a)
    assert all(len(neighbours) == 2 for neighbours in graph.values())
    remaining = set(graph)
    choices = []
    first = np.cross(axis, np.array([1., 0, 0]))
    if np.linalg.norm(first) < .1:
        first = np.cross(axis, np.array([0., 1, 0]))
    first /= np.linalg.norm(first)
    second = np.cross(axis, first)
    while remaining:
        start = next(iter(remaining)); previous = None; current = start; loop = []
        while True:
            loop.append(current); remaining.discard(current)
            a, b = graph[current]
            following = a if a != previous else b
            previous, current = current, following
            if current == start:
                break
        p = np.array([nodes[k] for k in loop])
        bind = np.array([parameter[k] for k in loop])
        if np.mean(bind[:, 0]*sign) < .20 or abs(np.mean(bind[:, 0]*sign)-reach) > .06:
            continue
        q = np.column_stack(((p-center)@first, (p-center)@second))
        area = abs(np.sum(q[:, 0]*np.roll(q[:, 1], -1)-q[:, 1]*np.roll(q[:, 0], -1)))*.5
        angles = np.linspace(0, np.pi, 180, endpoint=False)
        projection = q@np.stack((np.cos(angles), np.sin(angles)))
        widths = np.ptp(projection, axis=0)
        choices.append((np.linalg.norm(p.mean(0)-center), area, float(widths.min()), float(widths.max()), len(loop)))
    assert choices, (sign, reach, center)
    _, area, minimum, maximum, count = min(choices, key=lambda v: v[0])
    return area, minimum, maximum, count

records = []
for meta in metadata['poses']:
    raw = (captures/(meta['name']+'_Vertices.bin')).read_bytes()
    assert struct.unpack_from('<i', raw)[0] == n
    points = np.frombuffer(raw, '<f4', n*3, 4).reshape(n, 3).astype(float)
    row = {'name': meta['name'], 'actualElbowDegrees': meta['elbowAngle'],
           'shoulderProtractionDegrees': meta['shoulderForward'], 'sections': {}}
    for (side, label), (selected, design, inverse) in fits.items():
        coefficients = inverse@points[selected]
        axis = coefficients[1]/np.linalg.norm(coefficients[1])
        residual = np.linalg.norm(design@coefficients-points[selected], axis=1)
        radial = coefficients[2:4].T.copy()
        radial -= axis[:, None]*(axis@radial)[None, :]
        major, minor = np.linalg.svd(radial, compute_uv=False)
        area, minimum, maximum, contour_points = plane_section(points, coefficients[0], axis, -1 if side == 'L' else 1, sections[label])
        row['sections'][side+'_'+label] = {
            'minimumDiameter': minimum, 'maximumDiameter': maximum,
            'equivalentDiameter': float(2*np.sqrt(area/np.pi)), 'area': area,
            'projectedMaterialEllipseArea': float(np.pi*major*minor), 'contourPoints': contour_points,
            'fitRms': float(np.sqrt(np.mean(residual**2))), 'vertices': len(selected)}
    if meta['name'] == 'Idle':
        trunk = np.isin(ids, [names.index(k) for k in ('Pelvis', 'Spine', 'Chest')])
        owned = (weights*trunk).sum(1) > .95
        waist = points[owned & (np.abs(points[:, 1]-.365) < .02)]
        torso_width = float(np.ptp(waist[:, 0]))
    records.append(row)
baseline = next(r for r in records if r['name'] == 'Lift90_Flex0')
idle = next(r for r in records if r['name'] == 'Idle')
for row in records:
    for label, section in row['sections'].items():
        section['areaRatioToStraightRaised'] = section['area']/baseline['sections'][label]['area']
        section['minimumDiameterRatioToStraightRaised'] = section['minimumDiameter']/baseline['sections'][label]['minimumDiameter']
report = {'measurement': 'Actual baked-mesh cross-section contours, perpendicular to centerlines fitted at matched material positions',
          'units': 'Unity scene units; ratios are dimensionless', 'torsoWidthAtMidsection': torso_width,
          'neutralArmToTorsoRatios': {label: idle['sections']['L_'+label]['equivalentDiameter']/torso_width for label in sections},
          'poses': records,
          'limitations': ['Cross-sectional area is a local proxy, not whole-arm volume.',
                          'Requested joint targets are constrained by physics; actual elbow angles are recorded.',
                          'Shoulder creasing is assessed in the actual rendered views, not inferred from section area.']}
output.parent.mkdir(parents=True, exist_ok=True)
output.write_text(json.dumps(report, indent=2)+'\n')
print('torso width', torso_width)
print('neutral arm/body diameter ratios', report['neutralArmToTorsoRatios'])
for row in records:
    print(row['name'], 'elbows', np.round(row['actualElbowDegrees'], 1),
          {key: round(row['sections']['L_'+key]['areaRatioToStraightRaised'], 3) for key in ('upper_middle', 'elbow', 'elbow_distal', 'forearm_middle')})
