#!/usr/bin/env python3

"""Build an r19 hip-only exact-seam retopology on immutable r16.

Only the connected pair-of-pants band whose source face centroids satisfy
0.120 <= z <= 0.240 is replaced.  Its original top and two leg boundaries
are retained exactly.  A center crotch seam divides the band into mirrored
left/right annuli; deterministic zipper strips are then converted to quads
with a SIMPLE Catmull-Clark-style topological split.  QuadriFlow is forbidden.

The current r18 Generic 20-bone, four-weight, linear-skinning contract is
reused verbatim after the mesh reconstruction.  The r16 source and r18
generator/output are read-only inputs and are never saved.
"""

import hashlib
import importlib.util
import json
import math
import os
import struct
import sys
from heapq import heappop, heappush

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.abspath(os.path.join(HERE, "..", ".."))
R18_GENERATOR = os.path.join(HERE, "create_c1b_rw018_rig.py")
R18_GENERATOR_SHA256 = (
    "e6de3d1974bf11fdb45d7f207c2d1641812e29e00a73b56727fb838bb0fcca02"
)
R18_BLEND = os.path.join(
    ROOT_DIR,
    "BlenderSource",
    "Characters",
    "C1B-RW-018-rig-preview",
    "CHR_MasterCharacter_C1B_Rig_r18.blend",
)
R18_BLEND_SHA256 = (
    "cdd85350e387d66005a9b122f2348c66a212eb3d4924b0a10bb928acb6c10fbe"
)

ASSET_ID = "CHR_MasterCharacter_C1B_Rig"
REVISION = "r19"
VERSION = "0.19.0-local-hip-retopo-rig-preview"
ARMATURE_NAME = "RIG_C1B_R19_Armature"
ARMATURE_DATA_NAME = "RIG_C1B_R19_ArmatureData"
BODY_NAME = "CHR_C1B_R19_HipRetopoSkinnedBody"
HEAD_NAME = "CHR_C1B_R19_SkinnedHead"
RIG_COLLECTION_NAME = "C1BRW019_HipRetopoRig"

PATCH_Z_MINIMUM = 0.120
PATCH_Z_MAXIMUM = 0.240
PATCH_SOURCE_FACE_COUNT = 33790
PATCH_SOURCE_VERTEX_COUNT = 34343
PATCH_BOUNDARY_VERTEX_COUNT = 1108
PATCH_BOUNDARY_COUNTS = (260, 260, 588)
PATCH_STRIP_COUNT = 41
UV_INNER_RADIUS = 0.80
UV_OUTER_RADIUS = 2.00
TOP_FRONT_SOURCE_ID = 56685
TOP_BACK_SOURCE_ID = 56815
BOTTOM_L_FRONT_SOURCE_ID = 54752
BOTTOM_R_FRONT_SOURCE_ID = 26694
BOTTOM_L_BACK_SOURCE_ID = 51966
CENTERLINE_COST_FACTOR = 240.0

EXPECTED_SOURCE_SIGNATURE = {
    "vertices": 227942,
    "edges": 455880,
    "faces": 227940,
}
EXPECTED_OUTSIDE_FACE_COUNT = 194150
EXPECTED_OUTSIDE_VERTEX_COUNT = 194707
EXPECTED_SELECTED_FACE_ID_SHA256 = (
    "ac966f7c25ab6794a0136882250b9950b09097b1aa8c61d4f258ed75af2ce4eb"
)
EXPECTED_OUTSIDE_COORDINATE_SHA256 = (
    "a9b4c8dfdb2b51bd26c89cb8cd7a335c166df01a9e61ba035285fe6e06fcf146"
)
EXPECTED_OUTSIDE_FACE_SHA256 = (
    "1d9458c3354b1984de23a79c3e41b9b0c5c4b3b4cbb43cf1f0cde1cfa309d5ef"
)
EXPECTED_OUTSIDE_EDGE_SHA256 = (
    "e495216e85494e48c24a5dd3826408af22dfd5d59da326cda3d35678641fa670"
)

SURFACE_SOURCE_TO_TARGET_P99_MAXIMUM = 0.00075
SURFACE_SOURCE_TO_TARGET_MAXIMUM = 0.00250
SURFACE_TARGET_TO_SOURCE_MAXIMUM = 0.00010
BOUNDS_MAXIMUM_DELTA = 0.00025
VOLUME_RELATIVE_MAXIMUM_DELTA = 0.0020
FACE_ASPECT_P99_MAXIMUM = 4.0
FACE_ASPECT_MAXIMUM = 12.0
MIRROR_PATCH_TOLERANCE = 1.0e-6
ZIPPER_EVENT_MERGE_FRACTION = 0.75


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def import_file(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


if file_sha256(R18_GENERATOR) != R18_GENERATOR_SHA256:
    raise RuntimeError("current r18 generator hash does not match the pinned contract")

r18 = import_file("c1b_rw018_for_r19", R18_GENERATOR)
r12 = r18.r12
qa = r18.qa

# r18 helpers resolve these globals at call time.  The skeleton specification,
# weighting equations, pose tests, and QA limits are deliberately not changed.
r18.ASSET_ID = ASSET_ID
r18.REVISION = REVISION
r18.VERSION = VERSION
r18.ARMATURE_NAME = ARMATURE_NAME
r18.ARMATURE_DATA_NAME = ARMATURE_DATA_NAME
r18.BODY_NAME = BODY_NAME
r18.HEAD_NAME = HEAD_NAME

SOURCE_BLEND = r18.SOURCE_BLEND
SOURCE_SHA256 = r18.SOURCE_SHA256
APPROVAL_RECORD = r18.APPROVAL_RECORD
APPROVAL_SHA256 = r18.APPROVAL_SHA256
SOURCE_BODY = r18.SOURCE_BODY
SOURCE_HEAD = r18.SOURCE_HEAD


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <output.blend> <render-dir> <report.json>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    geometry_only = False
    if len(values) == 4 and values[3] == "--geometry-only":
        geometry_only = True
        values = values[:3]
    if len(values) != 3:
        raise RuntimeError(
            "expected output blend, render directory, report, and optional --geometry-only"
        )
    blend_path, render_dir, report_path = map(os.path.abspath, values)
    require(blend_path.lower().endswith(".blend"), "output must be a .blend")
    require(report_path.lower().endswith(".json"), "report must be a .json")
    protected = (
        SOURCE_BLEND,
        APPROVAL_RECORD,
        R18_GENERATOR,
        R18_BLEND,
        os.path.abspath(__file__),
    )
    require(
        all(os.path.realpath(blend_path) != os.path.realpath(path) for path in protected),
        "output blend may not overwrite an input or generator",
    )
    require(
        all(os.path.realpath(report_path) != os.path.realpath(path) for path in protected),
        "report may not overwrite an input or generator",
    )
    return blend_path, render_dir, report_path, geometry_only


def array_sha256(values, dtype="<i8"):
    return hashlib.sha256(np.asarray(values, dtype=dtype).tobytes()).hexdigest()


def patch_face_id_hash(values):
    digest = hashlib.sha256()
    ordered = sorted(int(value) for value in values)
    digest.update(b"C1BR16_PATCH_FACE_IDS_V1")
    digest.update(struct.pack("<Q", len(ordered)))
    for face_id in ordered:
        digest.update(struct.pack("<I", face_id))
    return digest.hexdigest()


def mesh_face_array(mesh):
    require(all(len(face.vertices) == 4 for face in mesh.polygons), "source is not all-quads")
    return np.asarray([tuple(face.vertices) for face in mesh.polygons], dtype=np.int64)


def build_edge_faces(faces):
    edge_faces = {}
    for face_index, face in enumerate(faces):
        for corner in range(len(face)):
            first = int(face[corner])
            second = int(face[(corner + 1) % len(face)])
            key = (first, second) if first < second else (second, first)
            edge_faces.setdefault(key, []).append(face_index)
    require(all(len(linked) == 2 for linked in edge_faces.values()), "source edge is not manifold")
    return edge_faces


def boundary_loops(selected, edge_faces):
    boundary_edges = []
    for edge, linked in edge_faces.items():
        if bool(selected[linked[0]]) != bool(selected[linked[1]]):
            boundary_edges.append(edge)
    adjacency = {}
    for first, second in boundary_edges:
        adjacency.setdefault(first, []).append(second)
        adjacency.setdefault(second, []).append(first)
    require(adjacency, "patch boundary is empty")
    require(all(len(neighbors) == 2 for neighbors in adjacency.values()), "patch boundary is not closed")
    unseen = set(adjacency)
    loops = []
    while unseen:
        start = min(unseen)
        loop = [start]
        previous = None
        current = start
        while True:
            choices = sorted(adjacency[current])
            following = choices[0] if choices[0] != previous else choices[1]
            if following == start:
                break
            require(following not in loop, "patch boundary self-repeats")
            loop.append(following)
            previous, current = current, following
        unseen.difference_update(loop)
        loops.append(loop)
    require(sum(len(loop) for loop in loops) == len(boundary_edges), "boundary traversal mismatch")
    return loops, boundary_edges


def reverse_keep_start(loop):
    return [loop[0], *reversed(loop[1:])]


def rotate_to_source_id(loop, source_id):
    require(source_id in loop, f"boundary anchor missing: {source_id}")
    offset = loop.index(source_id)
    return loop[offset:] + loop[:offset]


def signed_xy_area(loop, coordinates):
    points = coordinates[np.asarray(loop, dtype=np.int64)]
    following = np.roll(points, -1, axis=0)
    return 0.5 * float(
        np.sum(points[:, 0] * following[:, 1] - following[:, 0] * points[:, 1])
    )


def canonical_boundaries(loops, coordinates):
    require(sorted(len(loop) for loop in loops) == list(PATCH_BOUNDARY_COUNTS), "unexpected boundary counts")
    top = max(loops, key=lambda loop: float(np.mean(coordinates[loop, 2])))
    bottoms = [loop for loop in loops if loop is not top]
    bottom_l = min(bottoms, key=lambda loop: float(np.mean(coordinates[loop, 0])))
    bottom_r = max(bottoms, key=lambda loop: float(np.mean(coordinates[loop, 0])))

    top = rotate_to_source_id(top, TOP_FRONT_SOURCE_ID)
    if top[len(top) // 2] != TOP_BACK_SOURCE_ID or coordinates[top[1], 0] <= 0.0:
        top = reverse_keep_start(top)
    require(top[len(top) // 2] == TOP_BACK_SOURCE_ID, "top back anchor mismatch")
    require(coordinates[top[1], 0] > 0.0, "top loop is not canonical CCW")

    bottom_l = rotate_to_source_id(bottom_l, BOTTOM_L_FRONT_SOURCE_ID)
    if signed_xy_area(bottom_l, coordinates) < 0.0:
        bottom_l = reverse_keep_start(bottom_l)
    bottom_r = rotate_to_source_id(bottom_r, BOTTOM_R_FRONT_SOURCE_ID)
    if signed_xy_area(bottom_r, coordinates) > 0.0:
        bottom_r = reverse_keep_start(bottom_r)

    require(len(top) == 588 and len(bottom_l) == len(bottom_r) == 260, "canonical loop size mismatch")
    require(all(len(loop) % 2 == 0 for loop in (top, bottom_l, bottom_r)), "boundary loop is odd")
    return top, bottom_l, bottom_r


def exterior_coordinate_hash(vertex_ids, coordinates):
    digest = hashlib.sha256()
    digest.update(b"C1BR16_OUTSIDE_VERTEX_COORDS_F32_LE_V1")
    ordered = sorted(int(index) for index in vertex_ids)
    digest.update(struct.pack("<Q", len(ordered)))
    for source_id in ordered:
        x, y, z = np.asarray(coordinates[source_id], dtype="<f4")
        digest.update(struct.pack("<Ifff", source_id, float(x), float(y), float(z)))
    return digest.hexdigest()


def canonical_face_cycle(face):
    face = [int(value) for value in face]
    offset = face.index(min(face))
    return face[offset:] + face[:offset]


def exterior_face_hash(face_ids, faces, mesh):
    digest = hashlib.sha256()
    digest.update(b"C1BR16_OUTSIDE_FACE_TOPOLOGY_V1")
    ordered = sorted(int(index) for index in face_ids)
    digest.update(struct.pack("<Q", len(ordered)))
    for source_fid in ordered:
        cycle = canonical_face_cycle(faces[source_fid])
        polygon = mesh.polygons[source_fid]
        digest.update(struct.pack("<II", source_fid, len(cycle)))
        for source_vid in cycle:
            digest.update(struct.pack("<I", source_vid))
        digest.update(
            struct.pack(
                "<IB",
                int(polygon.material_index),
                int(bool(polygon.use_smooth)),
            )
        )
    return digest.hexdigest()


def exterior_edge_hash(face_ids, faces):
    edges = set()
    for face_id in face_ids:
        face = faces[int(face_id)]
        for corner in range(len(face)):
            first = int(face[corner])
            second = int(face[(corner + 1) % len(face)])
            edges.add((first, second) if first < second else (second, first))
    digest = hashlib.sha256()
    digest.update(b"C1BR16_OUTSIDE_EDGE_IDS_V1")
    ordered = sorted(edges)
    digest.update(struct.pack("<Q", len(ordered)))
    for first, second in ordered:
        digest.update(struct.pack("<II", first, second))
    return digest.hexdigest()


def patch_graph(selected, faces, coordinates):
    adjacency = {}
    for face_id in np.flatnonzero(selected):
        face = faces[int(face_id)]
        for corner in range(len(face)):
            first = int(face[corner])
            second = int(face[(corner + 1) % len(face)])
            if second in adjacency.get(first, ()):
                continue
            midpoint_x = 0.5 * (float(coordinates[first, 0]) + float(coordinates[second, 0]))
            length = float(np.linalg.norm(coordinates[first] - coordinates[second]))
            cost = length * (
                1.0 + CENTERLINE_COST_FACTOR * (abs(midpoint_x) / 0.09) ** 2
            )
            adjacency.setdefault(first, {})[second] = cost
            adjacency.setdefault(second, {})[first] = cost
    return adjacency


def centerline_path(selected, faces, coordinates):
    adjacency = patch_graph(selected, faces, coordinates)
    start = TOP_FRONT_SOURCE_ID
    target = TOP_BACK_SOURCE_ID
    distance = {start: 0.0}
    parent = {}
    queue = [(0.0, start)]
    while queue:
        current_distance, current = heappop(queue)
        if current_distance != distance.get(current):
            continue
        if current == target:
            break
        for neighbor, edge_cost in adjacency[current].items():
            proposal = current_distance + edge_cost
            if proposal < distance.get(neighbor, float("inf")):
                distance[neighbor] = proposal
                parent[neighbor] = current
                heappush(queue, (proposal, neighbor))
    require(target in parent, "no centerline path through hip patch")
    path = [target]
    while path[-1] != start:
        path.append(parent[path[-1]])
    path.reverse()
    points = coordinates[np.asarray(path, dtype=np.int64)]
    lengths = np.linalg.norm(np.diff(points, axis=0), axis=1)
    return path, points, float(np.sum(lengths))


def sample_open_polyline(points, count):
    points = np.asarray(points, dtype=np.float64)
    segment_lengths = np.linalg.norm(np.diff(points, axis=0), axis=1)
    cumulative = np.concatenate(([0.0], np.cumsum(segment_lengths)))
    require(cumulative[-1] > 0.0, "zero-length polyline")
    queries = np.linspace(0.0, cumulative[-1], count)
    result = []
    for query in queries:
        index = min(int(np.searchsorted(cumulative, query, side="right") - 1), len(points) - 2)
        length = segment_lengths[index]
        factor = 0.0 if length <= 1.0e-12 else (query - cumulative[index]) / length
        result.append(points[index] * (1.0 - factor) + points[index + 1] * factor)
    return np.asarray(result, dtype=np.float64)


def sample_open_curve(points, parameters):
    points = np.asarray(points, dtype=np.float64)
    segment_lengths = np.linalg.norm(np.diff(points, axis=0), axis=1)
    cumulative = np.concatenate(([0.0], np.cumsum(segment_lengths)))
    require(cumulative[-1] > 0.0, "zero-length curve")
    normalized = cumulative / cumulative[-1]
    result = []
    for parameter in parameters:
        parameter = min(max(float(parameter), 0.0), 1.0)
        index = min(int(np.searchsorted(normalized, parameter, side="right") - 1), len(points) - 2)
        width = normalized[index + 1] - normalized[index]
        factor = 0.0 if width <= 1.0e-12 else (parameter - normalized[index]) / width
        result.append(points[index] * (1.0 - factor) + points[index + 1] * factor)
    return np.asarray(result, dtype=np.float64)


def local_patch_bvh(coordinates, faces, face_ids):
    used = sorted({int(vertex) for face_id in face_ids for vertex in faces[int(face_id)]})
    mapping = {source_id: local_id for local_id, source_id in enumerate(used)}
    local_coordinates = [Vector(tuple(float(value) for value in coordinates[index])) for index in used]
    local_faces = [
        [mapping[int(vertex)] for vertex in faces[int(face_id)]] for face_id in face_ids
    ]
    return BVHTree.FromPolygons(local_coordinates, local_faces, all_triangles=False), used


def project_point(bvh, point, projection_distances):
    location, _normal, _face_index, distance = bvh.find_nearest(
        Vector(tuple(float(value) for value in point))
    )
    require(location is not None, "BVH projection failed")
    projection_distances.append(float(distance))
    return np.asarray(location, dtype=np.float64)


def boundary_midpoint_map(full_loop):
    require(len(full_loop) % 2 == 0, "boundary midpoint source loop is odd")
    result = {}
    for index in range(0, len(full_loop), 2):
        first = int(full_loop[index])
        midpoint = int(full_loop[(index + 1) % len(full_loop)])
        second = int(full_loop[(index + 2) % len(full_loop)])
        result[tuple(sorted((first, second)))] = midpoint
    return result


def zipper_strip(
    first_ring,
    second_ring,
    first_parameters=None,
    second_parameters=None,
):
    first_count = len(first_ring)
    second_count = len(second_ring)
    if first_parameters is None:
        first_parameters = np.arange(first_count, dtype=np.float64) / first_count
    if second_parameters is None:
        second_parameters = np.arange(second_count, dtype=np.float64) / second_count
    first_parameters = np.asarray(first_parameters, dtype=np.float64)
    second_parameters = np.asarray(second_parameters, dtype=np.float64)
    require(
        len(first_parameters) == first_count
        and len(second_parameters) == second_count,
        "zipper parameter count mismatch",
    )
    require(
        abs(float(first_parameters[0])) <= 1.0e-10
        and abs(float(second_parameters[0])) <= 1.0e-10
        and np.all(np.diff(first_parameters) > 0.0)
        and np.all(np.diff(second_parameters) > 0.0),
        "zipper parameters are not canonical increasing cycles",
    )
    first_index = 0
    second_index = 0
    faces = []
    while first_index < first_count or second_index < second_count:
        next_first = (
            float(first_parameters[first_index + 1])
            if first_index + 1 < first_count
            else 1.0
            if first_index < first_count
            else float("inf")
        )
        next_second = (
            float(second_parameters[second_index + 1])
            if second_index + 1 < second_count
            else 1.0
            if second_index < second_count
            else float("inf")
        )
        first = first_ring[first_index % first_count]
        second = second_ring[second_index % second_count]
        current_first = (
            float(first_parameters[first_index])
            if first_index < first_count
            else 1.0
        )
        current_second = (
            float(second_parameters[second_index])
            if second_index < second_count
            else 1.0
        )
        local_step = max(
            next_first - current_first,
            next_second - current_second,
        )
        merge_tolerance = ZIPPER_EVENT_MERGE_FRACTION * local_step
        if (
            first_index < first_count
            and second_index < second_count
            and abs(next_first - next_second)
            <= max(1.0e-12, merge_tolerance)
        ):
            faces.append(
                (
                    first,
                    first_ring[(first_index + 1) % first_count],
                    second_ring[(second_index + 1) % second_count],
                    second,
                )
            )
            first_index += 1
            second_index += 1
        elif next_first < next_second:
            faces.append(
                (first, first_ring[(first_index + 1) % first_count], second)
            )
            first_index += 1
        else:
            faces.append(
                (first, second_ring[(second_index + 1) % second_count], second)
            )
            second_index += 1
    return faces


def ring_polar_parameters(ring, vertex_uv):
    angles = []
    previous = None
    for vertex in ring:
        uv = vertex_uv[int(vertex)]
        angle = math.atan2(float(uv[1]), float(uv[0]))
        if angle < 0.0:
            angle += 2.0 * math.pi
        if previous is None:
            angle = 0.0
        else:
            while angle <= previous + 1.0e-12:
                angle += 2.0 * math.pi
        angles.append(angle)
        previous = angle
    parameters = np.asarray(angles, dtype=np.float64) / (2.0 * math.pi)
    require(
        parameters[-1] < 1.0 - 1.0e-10,
        "ring polar parameter wrapped beyond its final edge",
    )
    return parameters


def expanding_quad_strip(inner_ring, outer_ring, phase=0):
    inner_count = len(inner_ring)
    outer_count = len(outer_ring)
    require(
        outer_count >= inner_count
        and (outer_count - inner_count) % 2 == 0,
        "quad transition requires a nonnegative even ring-count difference",
    )
    ear_count = (outer_count - inner_count) // 2
    ear_positions = (
        {
            int(math.floor(index * inner_count / ear_count + 0.5))
            % inner_count
            for index in range(ear_count)
        }
        if ear_count
        else set()
    )
    require(len(ear_positions) == ear_count, "quad transition ear positions collided")
    faces = []
    outer_index = -1 if ear_count else 0
    ears_used = 0
    for inner_index in range(inner_count):
        if inner_index in ear_positions:
            faces.append(
                (
                    inner_ring[inner_index],
                    outer_ring[outer_index % outer_count],
                    outer_ring[(outer_index + 1) % outer_count],
                    outer_ring[(outer_index + 2) % outer_count],
                )
            )
            outer_index += 2
            ears_used += 1
        faces.append(
            (
                inner_ring[inner_index],
                outer_ring[outer_index % outer_count],
                outer_ring[(outer_index + 1) % outer_count],
                inner_ring[(inner_index + 1) % inner_count],
            )
        )
        outer_index += 1
    require(ears_used == ear_count, "quad transition ear count mismatch")
    expected_final = outer_count - 1 if ear_count else outer_count
    require(
        outer_index == expected_final,
        "quad transition did not consume outer ring",
    )
    return faces


def canonical_left_half_boundaries(
    source_coordinates, source_faces, selected, edge_faces
):
    face_center_x = np.mean(
        source_coordinates[source_faces, 0], axis=1, dtype=np.float64
    )
    left_selected = selected & (face_center_x < 0.0)
    loops, _boundary_edges = boundary_loops(left_selected, edge_faces)
    require(sorted(len(loop) for loop in loops) == [260, 520], "left half boundary mismatch")
    outer = max(loops, key=len)
    bottom = min(loops, key=len)

    outer = rotate_to_source_id(outer, TOP_FRONT_SOURCE_ID)
    back_index = outer.index(TOP_BACK_SOURCE_ID)
    first_minimum_z = float(
        np.min(source_coordinates[np.asarray(outer[: back_index + 1]), 2])
    )
    other_arc = outer[back_index:] + outer[:1]
    second_minimum_z = float(
        np.min(source_coordinates[np.asarray(other_arc), 2])
    )
    if first_minimum_z > second_minimum_z:
        outer = reverse_keep_start(outer)
        back_index = outer.index(TOP_BACK_SOURCE_ID)
    require(
        float(np.min(source_coordinates[np.asarray(outer[: back_index + 1]), 2]))
        <= 0.135,
        "left outer first arc is not the crotch seam",
    )

    bottom = rotate_to_source_id(bottom, BOTTOM_L_FRONT_SOURCE_ID)
    if signed_xy_area(bottom, source_coordinates) < 0.0:
        bottom = reverse_keep_start(bottom)
    require(BOTTOM_L_BACK_SOURCE_ID in bottom, "left bottom back anchor missing")
    bottom_back_index = bottom.index(BOTTOM_L_BACK_SOURCE_ID)
    require(
        bottom_back_index == len(bottom) // 2,
        "left bottom back landmark is not opposite the front landmark",
    )
    return left_selected, outer, back_index, bottom, bottom_back_index


def boundary_uv_values(loop, back_index, radius, source_coordinates):
    first = source_coordinates[np.asarray(loop[: back_index + 1], dtype=np.int64)]
    second_ids = loop[back_index:] + loop[:1]
    second = source_coordinates[np.asarray(second_ids, dtype=np.int64)]

    def normalized_distances(points):
        lengths = np.linalg.norm(np.diff(points, axis=0), axis=1)
        cumulative = np.concatenate(([0.0], np.cumsum(lengths)))
        require(cumulative[-1] > 0.0, "zero boundary arc length")
        return cumulative / cumulative[-1]

    result = {}
    for source_id, parameter in zip(
        loop[: back_index + 1], normalized_distances(first)
    ):
        angle = math.pi * float(parameter)
        result[int(source_id)] = np.asarray(
            (radius * math.cos(angle), radius * math.sin(angle)),
            dtype=np.float64,
        )
    for source_id, parameter in zip(second_ids, normalized_distances(second)):
        angle = math.pi + math.pi * float(parameter)
        result[int(source_id)] = np.asarray(
            (radius * math.cos(angle), radius * math.sin(angle)),
            dtype=np.float64,
        )
    return result


def triangle_signed_area_2d(first, second, third):
    return 0.5 * float(
        (second[0] - first[0]) * (third[1] - first[1])
        - (second[1] - first[1]) * (third[0] - first[0])
    )


def solve_dirichlet_cg(degrees, neighbor_rows, neighbor_columns, right_hand_side):
    degrees = np.asarray(degrees, dtype=np.float64)
    neighbor_rows = np.asarray(neighbor_rows, dtype=np.int64)
    neighbor_columns = np.asarray(neighbor_columns, dtype=np.int64)
    right_hand_side = np.asarray(right_hand_side, dtype=np.float64)

    def multiply(values):
        result = degrees * values
        np.add.at(
            result,
            neighbor_rows,
            -values[neighbor_columns],
        )
        return result

    solutions = []
    records = []
    for dimension in range(right_hand_side.shape[1]):
        target = right_hand_side[:, dimension]
        solution = np.zeros_like(target)
        residual = target - multiply(solution)
        preconditioned = residual / degrees
        direction = preconditioned.copy()
        rz = float(np.dot(residual, preconditioned))
        target_norm = max(float(np.linalg.norm(target)), 1.0)
        iteration = 0
        relative_residual = float(np.linalg.norm(residual)) / target_norm
        for iteration in range(1, 4001):
            multiplied = multiply(direction)
            denominator = float(np.dot(direction, multiplied))
            require(abs(denominator) > 1.0e-30, "harmonic CG breakdown")
            alpha = rz / denominator
            solution += alpha * direction
            residual -= alpha * multiplied
            relative_residual = float(np.linalg.norm(residual)) / target_norm
            if relative_residual <= 1.0e-11:
                break
            preconditioned = residual / degrees
            following_rz = float(np.dot(residual, preconditioned))
            beta = following_rz / rz
            direction = preconditioned + beta * direction
            rz = following_rz
        require(relative_residual <= 1.0e-9, "harmonic CG did not converge")
        solutions.append(solution)
        records.append(
            {
                "iterations": iteration,
                "relativeResidual": relative_residual,
            }
        )
    return np.column_stack(solutions), records


def harmonic_surface_mapper(
    source_coordinates,
    source_faces,
    left_selected,
    outer,
    outer_back_index,
    bottom,
    bottom_back_index,
):
    left_face_ids = [int(value) for value in np.flatnonzero(left_selected)]
    patch_vertex_ids = sorted(
        {
            int(vertex)
            for face_id in left_face_ids
            for vertex in source_faces[face_id]
        }
    )
    patch_vertex_set = set(patch_vertex_ids)
    boundary_uv = boundary_uv_values(
        bottom, bottom_back_index, UV_INNER_RADIUS, source_coordinates
    )
    boundary_uv.update(
        boundary_uv_values(outer, outer_back_index, UV_OUTER_RADIUS, source_coordinates)
    )
    boundary_ids = set(boundary_uv)
    require(
        boundary_ids == set(bottom) | set(outer),
        "harmonic boundary vertex set mismatch",
    )

    adjacency = {source_id: set() for source_id in patch_vertex_ids}
    for face_id in left_face_ids:
        face = source_faces[face_id]
        for corner in range(len(face)):
            first = int(face[corner])
            second = int(face[(corner + 1) % len(face)])
            adjacency[first].add(second)
            adjacency[second].add(first)
    require(
        all(neighbor in patch_vertex_set for values in adjacency.values() for neighbor in values),
        "harmonic graph escaped the patch",
    )

    interior_ids = sorted(patch_vertex_set - boundary_ids)
    interior_row = {source_id: row for row, source_id in enumerate(interior_ids)}
    degrees = np.zeros(len(interior_ids), dtype=np.float64)
    neighbor_rows = []
    neighbor_columns = []
    right_hand_side = np.zeros((len(interior_ids), 2), dtype=np.float64)
    for source_id in interior_ids:
        row = interior_row[source_id]
        neighbors = sorted(adjacency[source_id])
        require(neighbors, "isolated harmonic vertex")
        degrees[row] = float(len(neighbors))
        for neighbor in neighbors:
            if neighbor in interior_row:
                neighbor_rows.append(row)
                neighbor_columns.append(interior_row[neighbor])
            else:
                right_hand_side[row] += boundary_uv[neighbor]
    solved, solver_records = solve_dirichlet_cg(
        degrees,
        neighbor_rows,
        neighbor_columns,
        right_hand_side,
    )
    solved_u = solved[:, 0]
    solved_v = solved[:, 1]
    require(
        np.isfinite(solved_u).all() and np.isfinite(solved_v).all(),
        "harmonic solve produced non-finite coordinates",
    )
    uv_by_source = dict(boundary_uv)
    for row, source_id in enumerate(interior_ids):
        uv_by_source[source_id] = np.asarray(
            (solved_u[row], solved_v[row]), dtype=np.float64
        )

    local_index = {
        source_id: index for index, source_id in enumerate(patch_vertex_ids)
    }
    local_uv = np.asarray(
        [uv_by_source[source_id] for source_id in patch_vertex_ids],
        dtype=np.float64,
    )
    triangles = []
    triangle_source_ids = []
    signed_areas = []
    locally_inverted_quads = 0
    for face_id in left_face_ids:
        face = [int(value) for value in source_faces[face_id]]
        options = (
            ((face[0], face[1], face[2]), (face[0], face[2], face[3])),
            ((face[0], face[1], face[3]), (face[1], face[2], face[3])),
        )
        best = None
        for option in options:
            areas = [
                triangle_signed_area_2d(
                    uv_by_source[triangle[0]],
                    uv_by_source[triangle[1]],
                    uv_by_source[triangle[2]],
                )
                for triangle in option
            ]
            same_orientation = areas[0] * areas[1] > 0.0
            score = min(abs(areas[0]), abs(areas[1])) if same_orientation else -1.0
            if best is None or score > best[0]:
                best = (score, option, areas)
        if best[0] <= 1.0e-14:
            locally_inverted_quads += 1
        for triangle, area in zip(best[1], best[2]):
            triangles.append(tuple(local_index[index] for index in triangle))
            triangle_source_ids.append(triangle)
            signed_areas.append(area)

    signed_areas = np.asarray(signed_areas, dtype=np.float64)
    positive = int(np.count_nonzero(signed_areas > 0.0))
    negative = int(np.count_nonzero(signed_areas < 0.0))
    inverted = min(positive, negative)
    print(
        "R19_HARMONIC_DIAGNOSTIC="
        + json.dumps(
            {
                "locallyInvertedQuads": locally_inverted_quads,
                "positiveTriangles": positive,
                "negativeTriangles": negative,
                "minimumAbsArea": float(np.min(np.abs(signed_areas))),
                "solver": solver_records,
            },
            separators=(",", ":"),
        )
    )
    require(
        locally_inverted_quads == 0 and inverted == 0,
        "harmonic UV contains inverted triangles",
    )
    uv_bvh = BVHTree.FromPolygons(
        [Vector((float(point[0]), float(point[1]), 0.0)) for point in local_uv],
        triangles,
        all_triangles=True,
    )
    residuals = []

    def map_uv(uv):
        uv = np.asarray(uv, dtype=np.float64)
        location, _normal, triangle_index, distance = uv_bvh.find_nearest(
            Vector((float(uv[0]), float(uv[1]), 0.0))
        )
        require(location is not None and triangle_index is not None, "UV inverse lookup failed")
        residuals.append(float(distance))
        triangle = triangle_source_ids[int(triangle_index)]
        a = uv_by_source[triangle[0]]
        b = uv_by_source[triangle[1]]
        c = uv_by_source[triangle[2]]
        query = np.asarray((float(location.x), float(location.y)), dtype=np.float64)
        denominator = (
            (b[1] - c[1]) * (a[0] - c[0])
            + (c[0] - b[0]) * (a[1] - c[1])
        )
        require(abs(float(denominator)) > 1.0e-16, "UV inverse triangle degenerate")
        weight_a = (
            (b[1] - c[1]) * (query[0] - c[0])
            + (c[0] - b[0]) * (query[1] - c[1])
        ) / denominator
        weight_b = (
            (c[1] - a[1]) * (query[0] - c[0])
            + (a[0] - c[0]) * (query[1] - c[1])
        ) / denominator
        weight_c = 1.0 - weight_a - weight_b
        return (
            source_coordinates[triangle[0]] * weight_a
            + source_coordinates[triangle[1]] * weight_b
            + source_coordinates[triangle[2]] * weight_c
        )

    return map_uv, uv_by_source, residuals, {
        "leftSourceFaceCount": len(left_face_ids),
        "leftSourceVertexCount": len(patch_vertex_ids),
        "boundaryVertexCount": len(boundary_ids),
        "interiorVertexCount": len(interior_ids),
        "triangleCount": len(triangles),
        "positiveTriangleCount": positive,
        "negativeTriangleCount": negative,
        "invertedTriangleCount": inverted,
        "locallyInvertedQuadCount": locally_inverted_quads,
        "minimumAbsTriangleArea": float(np.min(np.abs(signed_areas))),
        "solver": solver_records,
    }


def reconstruct_hip_patch(
    source_coordinates, source_faces, selected, loops, edge_faces
):
    top, bottom_l, bottom_r = canonical_boundaries(loops, source_coordinates)
    require(top[294] == TOP_BACK_SOURCE_ID, "top half split mismatch")
    top_base = top[::2]
    bottom_l_base = bottom_l[::2]
    require(len(top_base) == 294 and len(bottom_l_base) == 130, "base boundary count mismatch")

    (
        left_selected,
        outer,
        outer_back_index,
        left_bottom_half,
        bottom_back_index,
    ) = canonical_left_half_boundaries(
        source_coordinates, source_faces, selected, edge_faces
    )
    require(left_bottom_half == bottom_l, "left bottom canonicalization drift")
    (
        map_uv,
        source_uv,
        uv_inverse_residuals,
        harmonic_report,
    ) = harmonic_surface_mapper(
        source_coordinates,
        source_faces,
        left_selected,
        outer,
        outer_back_index,
        bottom_l,
        bottom_back_index,
    )

    seam_source_ids = outer[: outer_back_index + 1]
    seam_source_points = source_coordinates[
        np.asarray(seam_source_ids, dtype=np.int64)
    ]
    seam_source_length = float(
        np.sum(np.linalg.norm(np.diff(seam_source_points, axis=0), axis=1))
    )
    require(
        float(np.max(np.abs(seam_source_points[:, 0]))) <= 1.0e-6,
        "source center seam is not on the symmetry plane",
    )
    require(
        float(np.min(seam_source_points[:, 2])) <= 0.135,
        "source center seam did not reach crotch saddle",
    )

    coordinate_list = [coordinate.copy() for coordinate in source_coordinates]
    vertex_uv = {}

    def set_source_uv(source_id):
        vertex_uv[int(source_id)] = np.asarray(
            source_uv[int(source_id)], dtype=np.float64
        )

    def append_uv(uv, centerline=False):
        uv = np.asarray(uv, dtype=np.float64)
        coordinate = np.asarray(map_uv(uv), dtype=np.float64)
        if centerline:
            coordinate[0] = 0.0
        coordinate_list.append(coordinate)
        index = len(coordinate_list) - 1
        vertex_uv[index] = uv
        return index

    for source_id in bottom_l:
        set_source_uv(source_id)
    for source_id in outer:
        set_source_uv(source_id)

    rings = [bottom_l]
    generated_ring_count = 47
    for ring_index in range(1, generated_ring_count + 1):
        t = ring_index / (generated_ring_count + 1)
        expansion = int(
            math.floor(
                130.0 * (ring_index - 1) / (generated_ring_count - 1)
                + 0.5
            )
        )
        count = 260 + 2 * expansion
        if ring_index == 1:
            parameters = ring_polar_parameters(bottom_l, vertex_uv)
        elif ring_index == generated_ring_count:
            parameters = ring_polar_parameters(outer, vertex_uv)
        else:
            parameters = np.arange(count, dtype=np.float64) / count
        radius = UV_INNER_RADIUS + (UV_OUTER_RADIUS - UV_INNER_RADIUS) * t
        ring = []
        for parameter in parameters:
            angle = 2.0 * math.pi * float(parameter)
            ring.append(
                append_uv(
                    (radius * math.cos(angle), radius * math.sin(angle))
                )
            )
        rings.append(ring)
    rings.append(outer)

    quad_faces = []
    for strip_index, (inner_ring, outer_ring) in enumerate(
        zip(rings[:-1], rings[1:])
    ):
        phase = (strip_index * 97) % max(len(inner_ring), 1)
        quad_faces.extend(
            expanding_quad_strip(inner_ring, outer_ring, phase=phase)
        )

    shared_seam_vertices = set(seam_source_ids)
    base_histogram = {"4": len(quad_faces)}
    base_faces = quad_faces

    left_new_vertices = set(range(len(source_coordinates), len(coordinate_list)))
    left_external_boundary = {
        int(vertex)
        for face in quad_faces
        for vertex in face
        if int(vertex) < len(source_coordinates)
    } - shared_seam_vertices
    right_external_boundary = set(top) | set(bottom_r)
    tree = KDTree(len(right_external_boundary))
    right_candidates = sorted(right_external_boundary)
    for tree_index, source_id in enumerate(right_candidates):
        tree.insert(Vector(tuple(source_coordinates[source_id])), tree_index)
    tree.balance()
    mirror_map = {}
    used_right_boundary = set()
    for source_id in sorted(left_external_boundary):
        coordinate = source_coordinates[source_id]
        target = Vector((-float(coordinate[0]), float(coordinate[1]), float(coordinate[2])))
        _location, candidate_index, distance = tree.find(target)
        require(float(distance) <= MIRROR_PATCH_TOLERANCE, "right boundary mirror mismatch")
        matched = right_candidates[candidate_index]
        require(matched not in used_right_boundary or matched == source_id, "right boundary mirror is not bijective")
        mirror_map[source_id] = matched
        used_right_boundary.add(matched)

    for source_id in shared_seam_vertices:
        mirror_map[source_id] = source_id
    for source_id in sorted(left_new_vertices - shared_seam_vertices):
        coordinate = np.asarray(coordinate_list[source_id], dtype=np.float64)
        mirrored = coordinate.copy()
        mirrored[0] *= -1.0
        coordinate_list.append(mirrored)
        mirrored_index = len(coordinate_list) - 1
        vertex_uv[mirrored_index] = vertex_uv[source_id].copy()
        mirror_map[source_id] = mirrored_index

    right_quad_faces = [
        tuple(mirror_map[int(vertex)] for vertex in reversed(face))
        for face in quad_faces
    ]
    return {
        "coordinates": coordinate_list,
        "leftFaces": quad_faces,
        "rightFaces": right_quad_faces,
        "top": top,
        "bottomL": bottom_l,
        "bottomR": bottom_r,
        "sharedSeamVertices": shared_seam_vertices,
        "projectionDistances": uv_inverse_residuals,
        "pathSourceVertexCount": len(seam_source_ids),
        "pathLengthMeters": seam_source_length,
        "pathMaximumAbsX": float(np.max(np.abs(seam_source_points[:, 0]))),
        "pathMinimumZ": float(np.min(seam_source_points[:, 2])),
        "baseFaceHistogram": base_histogram,
        "baseFaceCount": len(base_faces),
        "leftQuadCount": len(quad_faces),
        "leftSourceReferenceVertexCount": harmonic_report["leftSourceVertexCount"],
        "harmonicParameterization": harmonic_report,
    }


def compact_mesh(source_coordinates, source_faces, outside_face_ids, patch):
    outside_faces = [tuple(int(vertex) for vertex in source_faces[index]) for index in outside_face_ids]
    combined_faces = outside_faces + patch["leftFaces"] + patch["rightFaces"]
    used = sorted({vertex for face in combined_faces for vertex in face})
    remap = {old: new for new, old in enumerate(used)}
    coordinates = np.asarray([patch["coordinates"][index] for index in used], dtype=np.float64)
    faces = [tuple(remap[index] for index in face) for face in combined_faces]
    source_ids = np.asarray(
        [index if index < len(source_coordinates) else -1 for index in used],
        dtype=np.int64,
    )
    patch_vertices = {
        remap[index]
        for index in set(vertex for face in patch["leftFaces"] + patch["rightFaces"] for vertex in face)
    }
    return coordinates, faces, source_ids, len(outside_faces), patch_vertices


def mesh_geometry_hash(mesh):
    digest = hashlib.sha256()
    coordinates = np.asarray(r12.mesh_coordinates(mesh), dtype="<f4")
    faces = np.asarray([tuple(face.vertices) for face in mesh.polygons], dtype="<i4")
    digest.update(struct.pack("<QQ", len(coordinates), len(faces)))
    digest.update(coordinates.tobytes())
    digest.update(faces.tobytes())
    return digest.hexdigest()


def create_target_mesh(source_body, coordinates, faces):
    mesh = bpy.data.meshes.new(BODY_NAME + "Mesh")
    mesh.from_pydata(
        [tuple(float(value) for value in coordinate) for coordinate in coordinates],
        [],
        faces,
    )
    mesh.validate(verbose=True, clean_customdata=False)
    mesh.update(calc_edges=True)
    for material in source_body.data.materials:
        mesh.materials.append(material)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update(calc_edges=True)
    return mesh


def target_exterior_contract(
    mesh,
    source_ids,
    outside_face_ids,
    outside_face_count,
    source_faces,
    source_coordinates,
):
    target_coordinates = r12.mesh_coordinates(mesh)
    target_faces = [tuple(face.vertices) for face in mesh.polygons]
    require(len(target_faces) >= outside_face_count, "target outside face prefix missing")
    mapped_faces = []
    maximum_delta = 0.0
    for output_face in target_faces[:outside_face_count]:
        mapped = tuple(int(source_ids[index]) for index in output_face)
        require(all(index >= 0 for index in mapped), "new vertex leaked into outside face")
        mapped_faces.append(mapped)
        for output_index, source_index in zip(output_face, mapped):
            maximum_delta = max(
                maximum_delta,
                float(np.max(np.abs(target_coordinates[output_index] - source_coordinates[source_index]))),
            )
    expected_faces = [tuple(int(value) for value in source_faces[index]) for index in outside_face_ids]
    require(mapped_faces == expected_faces, "outside face topology or winding changed")
    preserved = sorted(int(value) for value in source_ids if value >= 0)
    return {
        "faceCount": outside_face_count,
        "sourceVertexCount": len(preserved),
        "maximumCoordinateDeltaMeters": maximum_delta,
        "topologyAndWindingExact": mapped_faces == expected_faces,
    }


def face_aspect_metrics(coordinates, faces):
    face_array = np.asarray(faces, dtype=np.int64)
    points = coordinates[face_array]
    lengths = np.linalg.norm(points - np.roll(points, -1, axis=1), axis=2)
    aspect = np.max(lengths, axis=1) / np.maximum(np.min(lengths, axis=1), 1.0e-12)
    return {
        "minimum": float(np.min(aspect)),
        "p50": float(np.percentile(aspect, 50.0)),
        "p95": float(np.percentile(aspect, 95.0)),
        "p99": float(np.percentile(aspect, 99.0)),
        "maximum": float(np.max(aspect)),
        "worstFaces": [
            {
                "aspect": float(aspect[index]),
                "centroid": [
                    float(value) for value in np.mean(points[index], axis=0)
                ],
            }
            for index in np.argsort(aspect)[-12:][::-1]
        ],
    }


def bvh_for_faces(coordinates, faces):
    used = sorted({int(vertex) for face in faces for vertex in face})
    mapping = {old: new for new, old in enumerate(used)}
    local_coordinates = [Vector(tuple(float(value) for value in coordinates[index])) for index in used]
    local_faces = [[mapping[int(vertex)] for vertex in face] for face in faces]
    return BVHTree.FromPolygons(local_coordinates, local_faces, all_triangles=False), used


def distance_statistics(bvh, points):
    distances = []
    for point in points:
        _location, _normal, _index, distance = bvh.find_nearest(
            Vector(tuple(float(value) for value in point))
        )
        require(distance is not None, "surface distance query failed")
        distances.append(float(distance))
    values = np.asarray(distances, dtype=np.float64)
    return {
        "sampleCount": len(values),
        "p50Meters": float(np.percentile(values, 50.0)),
        "p95Meters": float(np.percentile(values, 95.0)),
        "p99Meters": float(np.percentile(values, 99.0)),
        "maximumMeters": float(np.max(values)),
    }


def surface_fidelity(
    source_coordinates,
    source_faces,
    selected,
    target_coordinates,
    target_faces,
    outside_face_count,
):
    source_patch_faces = [source_faces[index] for index in np.flatnonzero(selected)]
    target_patch_faces = target_faces[outside_face_count:]
    source_bvh, source_used = bvh_for_faces(source_coordinates, source_patch_faces)
    target_bvh, target_used = bvh_for_faces(target_coordinates, target_patch_faces)
    source_to_target = distance_statistics(
        target_bvh, source_coordinates[np.asarray(source_used, dtype=np.int64)]
    )
    target_to_source = distance_statistics(
        source_bvh, target_coordinates[np.asarray(target_used, dtype=np.int64)]
    )
    return {
        "sourceToTarget": source_to_target,
        "targetToSource": target_to_source,
    }


def patch_edge_indices(mesh, patch_vertices):
    return np.asarray(
        [
            tuple(edge.vertices)
            for edge in mesh.edges
            if int(edge.vertices[0]) in patch_vertices
            and int(edge.vertices[1]) in patch_vertices
        ],
        dtype=np.int64,
    )


def pose_record_pass(record, signature, is_rest=False):
    displacement_ok = (
        record["maximumVertexDisplacementMeters"] <= r18.REST_POSITION_TOLERANCE
        if is_rest
        else record["maximumVertexDisplacementMeters"] >= 0.005
    )
    return (
        record["vertices"] == signature["vertices"]
        and record["edges"] == signature["edges"]
        and record["faces"] == signature["faces"]
        and record["manifold"]["result"] == "PASS"
        and record["fold"]["foldoverEdgeCountAt90Degrees"] == 0
        and record["fold"]["hardEdgeCountAt45Degrees"] == 0
        and record["fold"]["adjacentAngleMaximumDegrees"]
        <= r18.MAXIMUM_ADJACENT_ANGLE
        and record["selfIntersection"]["result"] == "PASS"
        and record["edgeStretchRatio"]["minimum"] >= r18.EDGE_STRETCH_MINIMUM
        and record["edgeStretchRatio"]["p01"] >= r18.EDGE_STRETCH_P01_MINIMUM
        and record["edgeStretchRatio"]["p99"] <= r18.EDGE_STRETCH_P99_MAXIMUM
        and record["edgeStretchRatio"]["maximum"] <= r18.EDGE_STRETCH_MAXIMUM
        and displacement_ok
    )


def main():
    blend_path, render_dir, report_path, geometry_only = parse_args()
    for path in (SOURCE_BLEND, APPROVAL_RECORD, R18_BLEND):
        require(os.path.isfile(path), f"missing input: {path}")
    require(file_sha256(SOURCE_BLEND) == SOURCE_SHA256, "r16 source hash mismatch")
    require(file_sha256(APPROVAL_RECORD) == APPROVAL_SHA256, "r16 approval hash mismatch")
    require(file_sha256(R18_BLEND) == R18_BLEND_SHA256, "optimized r18 blend hash mismatch")
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    partial_blend_path = blend_path + ".partial.blend"
    if os.path.isfile(partial_blend_path):
        os.remove(partial_blend_path)
    render_prefix = f"{ASSET_ID}_{REVISION}_"
    for filename in os.listdir(render_dir):
        if filename.startswith(render_prefix) and filename.lower().endswith(".png"):
            os.remove(os.path.join(render_dir, filename))

    source_hash_before = file_sha256(SOURCE_BLEND)
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    scene = bpy.context.scene
    scene.name = "C1BRW019_HipRetopoRigReview"
    source_body = bpy.data.objects[SOURCE_BODY]
    source_head = bpy.data.objects[SOURCE_HEAD]
    source_signature = r12.mesh_signature(source_body)
    source_coordinates = r12.mesh_coordinates(source_body.data).copy()
    source_faces = mesh_face_array(source_body.data)
    source_bounds = (np.min(source_coordinates, axis=0), np.max(source_coordinates, axis=0))
    source_volume = qa.manifold(source_body.data)["signedVolume"]
    require(source_signature == EXPECTED_SOURCE_SIGNATURE, "unexpected r16 body signature")

    face_centers_z = np.mean(source_coordinates[source_faces, 2], axis=1, dtype=np.float64)
    selected = (face_centers_z >= PATCH_Z_MINIMUM) & (face_centers_z <= PATCH_Z_MAXIMUM)
    selected_face_ids = np.flatnonzero(selected).astype(np.int64)
    outside_face_ids = np.flatnonzero(~selected).astype(np.int64)
    selected_vertex_ids = sorted(
        {int(vertex) for face_id in selected_face_ids for vertex in source_faces[face_id]}
    )
    outside_vertex_ids = sorted(
        {int(vertex) for face_id in outside_face_ids for vertex in source_faces[face_id]}
    )
    require(len(selected_face_ids) == PATCH_SOURCE_FACE_COUNT, "hip patch face selector drift")
    require(len(selected_vertex_ids) == PATCH_SOURCE_VERTEX_COUNT, "hip patch vertex selector drift")
    require(
        patch_face_id_hash(selected_face_ids) == EXPECTED_SELECTED_FACE_ID_SHA256,
        "hip patch face-id hash drift",
    )
    require(len(outside_face_ids) == EXPECTED_OUTSIDE_FACE_COUNT, "outside face count drift")
    require(len(outside_vertex_ids) == EXPECTED_OUTSIDE_VERTEX_COUNT, "outside vertex count drift")

    outside_coordinate_sha = exterior_coordinate_hash(
        outside_vertex_ids, source_coordinates
    )
    outside_face_sha = exterior_face_hash(
        outside_face_ids, source_faces, source_body.data
    )
    outside_edge_sha = exterior_edge_hash(outside_face_ids, source_faces)
    require(
        outside_coordinate_sha == EXPECTED_OUTSIDE_COORDINATE_SHA256,
        "outside coordinate contract hash mismatch",
    )
    require(
        outside_face_sha == EXPECTED_OUTSIDE_FACE_SHA256,
        "outside face contract hash mismatch",
    )
    require(
        outside_edge_sha == EXPECTED_OUTSIDE_EDGE_SHA256,
        "outside edge contract hash mismatch",
    )

    edge_faces = build_edge_faces(source_faces)
    loops, boundary_edges = boundary_loops(selected, edge_faces)
    require(len(boundary_edges) == PATCH_BOUNDARY_VERTEX_COUNT, "hip boundary count drift")
    patch = reconstruct_hip_patch(
        source_coordinates, source_faces, selected, loops, edge_faces
    )
    (
        target_coordinates,
        target_faces,
        target_source_ids,
        outside_face_count,
        target_patch_vertices,
    ) = compact_mesh(
        source_coordinates,
        source_faces,
        outside_face_ids,
        patch,
    )
    target_mesh = create_target_mesh(source_body, target_coordinates, target_faces)
    target_signature = {
        "vertices": len(target_mesh.vertices),
        "edges": len(target_mesh.edges),
        "faces": len(target_mesh.polygons),
    }
    require(all(len(face.vertices) == 4 for face in target_mesh.polygons), "r19 target is not all-quads")
    target_coordinates = r12.mesh_coordinates(target_mesh)
    target_faces = [tuple(face.vertices) for face in target_mesh.polygons]
    target_geometry_sha = mesh_geometry_hash(target_mesh)
    outside_contract = target_exterior_contract(
        target_mesh,
        target_source_ids,
        outside_face_ids,
        outside_face_count,
        source_faces,
        source_coordinates,
    )
    fidelity = surface_fidelity(
        source_coordinates,
        source_faces,
        selected,
        target_coordinates,
        target_faces,
        outside_face_count,
    )
    aspect = face_aspect_metrics(
        target_coordinates,
        target_faces[outside_face_count:],
    )
    target_bounds = (np.min(target_coordinates, axis=0), np.max(target_coordinates, axis=0))
    bounds_delta = float(
        max(
            np.max(np.abs(target_bounds[0] - source_bounds[0])),
            np.max(np.abs(target_bounds[1] - source_bounds[1])),
        )
    )
    rest_manifold = qa.manifold(target_mesh)
    rest_folds = qa.folds(target_mesh)
    rest_fold_diagnostic = r12.maximum_adjacent_angle_diagnostic(target_mesh)
    rest_overlap = qa.bvh_self_overlap(target_mesh)
    target_volume = rest_manifold["signedVolume"]
    volume_relative_delta = abs(target_volume - source_volume) / abs(source_volume)
    rest_folds.pop("foldoverEdgesAt90Degrees", None)
    rest_overlap.pop("nonAdjacentOverlapPairs", None)

    geometry_pass = (
        outside_contract["maximumCoordinateDeltaMeters"] == 0.0
        and outside_contract["topologyAndWindingExact"]
        and outside_contract["sourceVertexCount"] == EXPECTED_OUTSIDE_VERTEX_COUNT
        and rest_manifold["result"] == "PASS"
        and rest_manifold["eulerCharacteristic"] == 2
        and rest_folds["foldoverEdgeCountAt90Degrees"] == 0
        and rest_folds["hardEdgeCountAt45Degrees"] == 0
        and rest_folds["adjacentAngleMaximumDegrees"] <= r18.MAXIMUM_ADJACENT_ANGLE
        and rest_overlap["result"] == "PASS"
        and fidelity["sourceToTarget"]["p99Meters"]
        <= SURFACE_SOURCE_TO_TARGET_P99_MAXIMUM
        and fidelity["sourceToTarget"]["maximumMeters"]
        <= SURFACE_SOURCE_TO_TARGET_MAXIMUM
        and fidelity["targetToSource"]["maximumMeters"]
        <= SURFACE_TARGET_TO_SOURCE_MAXIMUM
        and bounds_delta <= BOUNDS_MAXIMUM_DELTA
        and volume_relative_delta <= VOLUME_RELATIVE_MAXIMUM_DELTA
        and aspect["p99"] <= FACE_ASPECT_P99_MAXIMUM
        and aspect["maximum"] <= FACE_ASPECT_MAXIMUM
    )

    if geometry_only:
        geometry_report = {
            "mode": "GEOMETRY_ONLY",
            "revision": REVISION,
            "sourceSha256": source_hash_before,
            "sourceSignature": source_signature,
            "selector": {
                "faceCentroidZInclusive": [PATCH_Z_MINIMUM, PATCH_Z_MAXIMUM],
                "faceCount": len(selected_face_ids),
                "vertexCount": len(selected_vertex_ids),
                "faceIdSha256": patch_face_id_hash(selected_face_ids),
            },
            "boundaryLoopCounts": sorted(len(loop) for loop in loops),
            "outsidePreservation": outside_contract,
            "targetSignature": target_signature,
            "targetGeometrySha256": target_geometry_sha,
            "allQuads": all(len(face.vertices) == 4 for face in target_mesh.polygons),
            "centerSeam": {
                "sourcePathVertexCount": patch["pathSourceVertexCount"],
                "pathLengthMeters": patch["pathLengthMeters"],
                "sourcePathMaximumAbsX": patch["pathMaximumAbsX"],
                "sourcePathMinimumZ": patch["pathMinimumZ"],
                "sharedFinalVertexCount": len(patch["sharedSeamVertices"]),
            },
            "baseFaceHistogram": patch["baseFaceHistogram"],
            "quadCounts": {
                "left": patch["leftQuadCount"],
                "right": len(patch["rightFaces"]),
            },
            "surfaceFidelity": fidelity,
            "boundsMaximumDeltaMeters": bounds_delta,
            "signedVolumeRelativeDelta": volume_relative_delta,
            "patchFaceAspectRatio": aspect,
            "restManifold": rest_manifold,
            "restFold": rest_folds,
            "restFoldDiagnostic": rest_fold_diagnostic,
            "restSelfIntersection": rest_overlap,
            "result": "PASS" if geometry_pass else "FAIL",
        }
        with open(report_path, "w", encoding="utf-8") as stream:
            json.dump(geometry_report, stream, ensure_ascii=False, indent=2)
            stream.write("\n")
        if not geometry_pass:
            raise RuntimeError(
                "r19 geometry-only QA failed: "
                + json.dumps(geometry_report, separators=(",", ":"))
            )
        print(
            "R19_HIP_RETOPO_GEOMETRY_REPORT="
            + json.dumps(geometry_report, separators=(",", ":"))
        )
        print("R19_HIP_RETOPO_GEOMETRY_RESULT=PASS")
        return

    source_body.data = target_mesh
    rig_collection = bpy.data.collections.new(RIG_COLLECTION_NAME)
    scene.collection.children.link(rig_collection)
    armature = r18.create_armature(rig_collection)
    body_coordinates, head_coordinates, body_weights, head_weights = r18.prepare_meshes(
        source_body, source_head, armature
    )
    body = bpy.data.objects[BODY_NAME]
    head = bpy.data.objects[HEAD_NAME]
    body_edges = r12.mesh_edges(body.data)
    patch_edges = patch_edge_indices(body.data, target_patch_vertices)
    body_weight_report = r18.weight_report(body, body_weights, body_coordinates)
    head_weight_report = r18.weight_report(head, head_weights, head_coordinates)
    skeleton = r18.skeleton_report(armature)

    pose_records = {}
    for pose_id in r18.POSE_TESTS:
        r18.apply_pose_test(armature, pose_id)
        record = r18.mesh_pose_metrics(body, body_coordinates, body_edges)
        posed_mesh = r18.evaluated_mesh(body)
        try:
            posed_coordinates = r12.mesh_coordinates(posed_mesh)
            record["hipPatchEdgeStretchRatio"] = r18.edge_stretch_metrics(
                body_coordinates,
                posed_coordinates,
                patch_edges,
            )
        finally:
            bpy.data.meshes.remove(posed_mesh)
        record["result"] = (
            "PASS"
            if pose_record_pass(record, target_signature, pose_id == "Rest")
            else "FAIL"
        )
        pose_records[pose_id] = record
    r18.reset_pose(armature)

    render_files = r18.render_qa_bundle(
        scene, body, head, armature, render_dir
    )
    missing_renders = [
        filename
        for filename in render_files
        if not os.path.isfile(os.path.join(render_dir, filename))
        or os.path.getsize(os.path.join(render_dir, filename)) == 0
    ]

    rest_head_mesh = r18.evaluated_mesh(head)
    try:
        head_rest_delta = r12.maximum_delta(
            head_coordinates, r12.mesh_coordinates(rest_head_mesh)
        )
        evaluated_head_signature = {
            "vertices": len(rest_head_mesh.vertices),
            "edges": len(rest_head_mesh.edges),
            "faces": len(rest_head_mesh.polygons),
        }
    finally:
        bpy.data.meshes.remove(rest_head_mesh)

    r18.reset_pose(armature)
    saved_rest_pose_confirmed = r18.pose_is_rest(armature)
    r18.clean_final_rig_scene(scene, rig_collection, armature, body, head)
    inventory = r18.rig_inventory(armature, body, head)
    source_hash_after = file_sha256(SOURCE_BLEND)
    modifiers_valid = all(
        len(obj.modifiers) == 1
        and obj.modifiers[0].type == "ARMATURE"
        and obj.modifiers[0].object is armature
        and obj.modifiers[0].use_vertex_groups
        and not obj.modifiers[0].use_bone_envelopes
        and not obj.modifiers[0].use_deform_preserve_volume
        for obj in (body, head)
    )
    weights_pass = (
        body_weight_report["finite"]
        and body_weight_report["maximumWeightSumError"] <= r18.WEIGHT_SUM_TOLERANCE
        and body_weight_report["maximumInfluencesPerVertex"] <= r18.MAXIMUM_WEIGHTS_PER_VERTEX
        and body_weight_report["unweightedVertexCount"] == 0
        and body_weight_report["maximumLeftBoneLeakOnRightSide"] <= r18.WEIGHT_EPSILON
        and body_weight_report["maximumRightBoneLeakOnLeftSide"] <= r18.WEIGHT_EPSILON
        and body_weight_report["mirror"]["unmatchedVertexCount"] == 0
        and body_weight_report["mirror"]["maximumMirroredWeightDeviation"]
        <= r18.MIRROR_WEIGHT_TOLERANCE
    )
    inventory_pass = (
        inventory["armatureObjectCount"] == 1
        and inventory["armatureDatablockCount"] == 1
        and inventory["boneCount"] == 20
        and inventory["actionCount"] == 0
        and inventory["shapeKeyDatablockCount"] == 0
        and inventory["latticeObjectCount"] == 0
        and inventory["bodyModifierCount"] == 1
        and inventory["headModifierCount"] == 1
        and inventory["animatedObjectCount"] == 0
        and inventory["rigidBodyObjectCount"] == 0
        and inventory["rigidBodyConstraintObjectCount"] == 0
        and inventory["objectTypeCounts"] == {"ARMATURE": 1, "MESH": 2}
        and inventory["collectionCount"] == 1
        and not inventory["negativeScaleObjects"]
        and not inventory["temporaryDatablocks"]
        and not inventory["colliderObjects"]
    )
    technical_pass = (
        source_hash_before == source_hash_after == SOURCE_SHA256
        and geometry_pass
        and skeleton["bindPoseSha256"] == r18.EXPECTED_BIND_POSE_SHA256
        and r18.skeleton_matches_spec(armature)
        and saved_rest_pose_confirmed
        and weights_pass
        and modifiers_valid
        and all(record["result"] == "PASS" for record in pose_records.values())
        and head_rest_delta <= r18.REST_POSITION_TOLERANCE
        and evaluated_head_signature == {"vertices": 6050, "edges": 12192, "faces": 6144}
        and inventory_pass
        and len(render_files) == 7
        and not missing_renders
    )

    report = {
        "assetId": ASSET_ID,
        "assetVersion": VERSION,
        "revision": REVISION,
        "candidateStatus": "HIP_RETOPO_RIG_REVIEW",
        "source": {
            "revision": "r16",
            "path": SOURCE_BLEND,
            "sha256Before": source_hash_before,
            "sha256After": source_hash_after,
            "unchanged": source_hash_before == source_hash_after,
            "bodySignature": source_signature,
            "approvalRecordPath": APPROVAL_RECORD,
            "approvalRecordSha256": APPROVAL_SHA256,
        },
        "r18Contract": {
            "generatorPath": R18_GENERATOR,
            "generatorSha256": R18_GENERATOR_SHA256,
            "blendPath": R18_BLEND,
            "blendSha256": R18_BLEND_SHA256,
            "boneCount": 20,
            "maximumWeightsPerVertex": 4,
            "skinningMode": "LINEAR_BLEND_UNITY_PARITY",
            "preserveVolume": False,
            "hipDepthBias": r18.HIP_DEPTH_BIAS,
        },
        "retopology": {
            "method": "EXACT_SEAM_MIRRORED_PAIR_OF_PANTS_ZIPPER_SIMPLE_QUADIFY",
            "quadriFlowUsed": False,
            "selector": {
                "faceCentroidZInclusive": [PATCH_Z_MINIMUM, PATCH_Z_MAXIMUM],
                "sourceFaceCount": len(selected_face_ids),
                "sourceVertexCount": len(selected_vertex_ids),
                "faceIdSha256": patch_face_id_hash(selected_face_ids),
            },
            "boundary": {
                "loopCounts": sorted(len(loop) for loop in loops),
                "totalVertexCount": len(boundary_edges),
                "topCount": len(patch["top"]),
                "bottomLCount": len(patch["bottomL"]),
                "bottomRCount": len(patch["bottomR"]),
                "allClosed": True,
                "allEven": True,
            },
            "centerSeam": {
                "sourcePathVertexCount": patch["pathSourceVertexCount"],
                "basePointCount": 148,
                "pathLengthMeters": patch["pathLengthMeters"],
                "sourcePathMaximumAbsX": patch["pathMaximumAbsX"],
                "sourcePathMinimumZ": patch["pathMinimumZ"],
                "sharedFinalVertexCount": len(patch["sharedSeamVertices"]),
            },
            "baseFaceHistogram": patch["baseFaceHistogram"],
            "baseFaceCount": patch["baseFaceCount"],
            "leftQuadCount": patch["leftQuadCount"],
            "rightQuadCount": len(patch["rightFaces"]),
            "projectionSeedDistanceMeters": {
                "p50": float(np.percentile(patch["projectionDistances"], 50.0)),
                "p95": float(np.percentile(patch["projectionDistances"], 95.0)),
                "maximum": float(np.max(patch["projectionDistances"])),
            },
            "targetSignature": target_signature,
            "targetGeometrySha256": target_geometry_sha,
            "allQuads": all(len(face.vertices) == 4 for face in target_mesh.polygons),
            "outsidePreservation": {
                **outside_contract,
                "expectedCoordinateSha256": outside_coordinate_sha,
                "expectedFaceSha256": outside_face_sha,
                "expectedEdgeSha256": outside_edge_sha,
            },
            "surfaceFidelity": fidelity,
            "boundsMaximumDeltaMeters": bounds_delta,
            "signedVolumeSource": source_volume,
            "signedVolumeTarget": target_volume,
            "signedVolumeRelativeDelta": volume_relative_delta,
            "patchFaceAspectRatio": aspect,
            "restManifold": rest_manifold,
            "restFold": rest_folds,
            "restFoldDiagnostic": rest_fold_diagnostic,
            "restSelfIntersection": rest_overlap,
            "result": "PASS" if geometry_pass else "FAIL",
        },
        "skeleton": skeleton,
        "weights": {
            "method": "DETERMINISTIC_C2_ANATOMICAL_ZONES_R18_EXACT",
            "automaticBoneHeatUsed": False,
            "body": body_weight_report,
            "head": head_weight_report,
        },
        "deformationQA": {
            "temporaryPoseOnly": True,
            "savedInRestPose": saved_rest_pose_confirmed,
            "poses": pose_records,
        },
        "finalInventory": inventory,
        "renderFiles": render_files,
        "missingRenderFiles": missing_renders,
        "visualReview": {
            "result": "PENDING_MANUAL_INSPECTION",
            "focus": "HipFlex_L side silhouette and body-to-raised-leg transition",
        },
        "userRigStartAuthorized": True,
        "userHipRetopologyAuthorized": True,
        "userRigApprovalRecorded": False,
        "productionTopologyApproved": False,
        "animationAuthored": False,
        "fbxExportExecuted": False,
        "unityImportExecuted": False,
        "generator": {
            "path": os.path.abspath(__file__),
            "sha256": file_sha256(os.path.abspath(__file__)),
        },
        "technicalResult": "PASS" if technical_pass else "FAIL",
        "savedFileVerification": None,
        "output": None,
    }

    if technical_pass:
        r18.clear_custom_properties(scene)
        scene["asset_id"] = ASSET_ID
        scene["asset_version"] = VERSION
        scene["revision"] = REVISION
        scene["owner_task"] = r18.OWNER_TASK
        scene["source_revision"] = "r16"
        scene["source_sha256"] = SOURCE_SHA256
        scene["rig_type"] = r18.RIG_TYPE
        scene["required_bones_json"] = json.dumps(r18.REQUIRED_BONES)
        scene["animation_authored"] = False
        scene["user_rig_approval_recorded"] = False
        r18.reset_pose(armature)
        bpy.ops.object.select_all(action="DESELECT")
        armature.select_set(True)
        bpy.context.view_layer.objects.active = armature
        bpy.ops.wm.save_as_mainfile(filepath=partial_blend_path, check_existing=False)
        saved = r18.verify_saved_rig_file(
            partial_blend_path,
            body_weights,
            head_weights,
            body_coordinates,
            head_coordinates,
        )
        saved_body = bpy.data.objects.get(BODY_NAME)
        saved_signature = r12.mesh_signature(saved_body) if saved_body else None
        saved_geometry_sha = mesh_geometry_hash(saved_body.data) if saved_body else None
        saved["bodySignature"] = saved_signature
        saved["bodyGeometrySha256"] = saved_geometry_sha
        saved["expectedBodyGeometrySha256"] = target_geometry_sha
        if saved_signature != target_signature:
            saved["errors"].append("BODY_GENERATED_SIGNATURE")
        if saved_geometry_sha != target_geometry_sha:
            saved["errors"].append("BODY_GENERATED_GEOMETRY")
        saved["result"] = "PASS" if not saved["errors"] else "FAIL"
        report["savedFileVerification"] = saved
        technical_pass = saved["result"] == "PASS"
        report["technicalResult"] = "PASS" if technical_pass else "FAIL"
        if technical_pass:
            os.replace(partial_blend_path, blend_path)
            report["output"] = {
                "blendPath": blend_path,
                "blendBytes": os.path.getsize(blend_path),
                "blendSha256": file_sha256(blend_path),
            }
        elif os.path.isfile(partial_blend_path):
            os.remove(partial_blend_path)
    elif os.path.isfile(partial_blend_path):
        os.remove(partial_blend_path)

    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    if not technical_pass:
        raise RuntimeError(
            "r19 hip retopo rig QA failed: "
            + json.dumps(report, separators=(",", ":"))
        )
    print("R19_HIP_RETOPO_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R19_HIP_RETOPO_RESULT=PASS")


if __name__ == "__main__":
    main()
