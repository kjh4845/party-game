#!/usr/bin/env python3

"""Validate, render, and copy the frozen C1B r06 review surface."""

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
from mathutils.kdtree import KDTree


ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.6.0-local-preview"
ASSET_REVISION = "r06"
SOURCE_OWNER = "kjh4845"
REFERENCE_PATH = "/Users/kjh/Downloads/Gang_Beast.webp"
REFERENCE_SHA256 = "9afccdb71c696d856c47b4a7a6640c02b80c1d50ea58f1e7b42a225c21f75991"
CONSTRUCTION = "DENSE_CONTINUOUS_IMPLICIT_FIELD_BISECT_MIRRORED_132394_QUADS"
SCRIPT_DIR = os.path.dirname(os.path.abspath(__file__))
REPOSITORY_ROOT = os.path.abspath(os.path.join(SCRIPT_DIR, "..", ".."))
CANONICAL_BLEND = os.path.join(
    REPOSITORY_ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-006-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r06.blend",
)
VIEW_NAMES = ("Front", "Side", "Back", "ThreeQuarter")


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <blend-output> <render-directory> [report-output]")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) not in (2, 3):
        raise RuntimeError("expected blend output, render directory and optional report output")
    blend_path = os.path.abspath(values[0])
    render_directory = os.path.abspath(values[1])
    report_path = (
        os.path.abspath(values[2])
        if len(values) == 3
        else os.path.join(os.path.dirname(blend_path), "TopologyReport.json")
    )
    return blend_path, render_directory, report_path


def mesh_copy(obj, evaluated=False):
    if not evaluated:
        return obj.data.copy()
    depsgraph = bpy.context.evaluated_depsgraph_get()
    return bpy.data.meshes.new_from_object(obj.evaluated_get(depsgraph), depsgraph=depsgraph)


def dominant_projection(points, normal):
    axis = max(range(3), key=lambda index: abs(normal[index]))
    if axis == 0:
        return [(point.y, point.z) for point in points]
    if axis == 1:
        return [(point.x, point.z) for point in points]
    return [(point.x, point.y) for point in points]


def concavity(mesh):
    concave = []
    zero_corner = []
    quad_count = 0
    for polygon in mesh.polygons:
        if len(polygon.vertices) != 4:
            continue
        quad_count += 1
        points = [mesh.vertices[index].co for index in polygon.vertices]
        projected = dominant_projection(points, polygon.normal)
        signs = []
        for index in range(4):
            a = projected[index]
            b = projected[(index + 1) % 4]
            c = projected[(index + 2) % 4]
            cross = (
                (b[0] - a[0]) * (c[1] - b[1])
                - (b[1] - a[1]) * (c[0] - b[0])
            )
            if abs(cross) <= 1.0e-12:
                zero_corner.append((polygon.index, index))
            else:
                signs.append(1 if cross > 0.0 else -1)
        if signs and min(signs) != max(signs):
            concave.append(polygon.index)
    return {
        "quadCount": quad_count,
        "concaveQuadCount": len(concave),
        "concaveQuadIndices": concave,
        "zeroCornerCount": len(zero_corner),
        "result": "PASS" if not concave and not zero_corner else "FAIL",
    }


def component_count(mesh):
    adjacency = {vertex.index: set() for vertex in mesh.vertices}
    for edge in mesh.edges:
        left, right = edge.vertices
        adjacency[left].add(right)
        adjacency[right].add(left)
    unseen = set(adjacency)
    count = 0
    while unseen:
        count += 1
        stack = [unseen.pop()]
        while stack:
            current = stack.pop()
            for neighbor in adjacency[current]:
                if neighbor in unseen:
                    unseen.remove(neighbor)
                    stack.append(neighbor)
    return count


def manifold(mesh):
    key_counts = {}
    for polygon in mesh.polygons:
        vertices = polygon.vertices
        for index in range(len(vertices)):
            key = tuple(sorted((vertices[index], vertices[(index + 1) % len(vertices)])))
            key_counts[key] = key_counts.get(key, 0) + 1
    boundary = sum(count == 1 for count in key_counts.values())
    nonmanifold = sum(count != 2 for count in key_counts.values())
    loose = sum(tuple(sorted(edge.vertices)) not in key_counts for edge in mesh.edges)
    components = component_count(mesh)
    euler = len(mesh.vertices) - len(mesh.edges) + len(mesh.polygons)
    analysis = bmesh.new()
    analysis.from_mesh(mesh)
    degenerate = sum(face.calc_area() <= 1.0e-12 for face in analysis.faces)
    signed_volume = analysis.calc_volume(signed=True)
    analysis.free()
    result = (
        boundary == 0
        and nonmanifold == 0
        and loose == 0
        and components == 1
        and euler == 2
        and degenerate == 0
        and abs(signed_volume) > 1.0e-5
    )
    return {
        "boundaryEdgeCount": boundary,
        "nonManifoldEdgeCount": nonmanifold,
        "looseEdgeCount": loose,
        "connectedComponents": components,
        "eulerCharacteristic": euler,
        "degenerateFaceCount": degenerate,
        "signedVolume": signed_volume,
        "result": "PASS" if result else "FAIL",
    }


def folds(mesh):
    face_normals = [polygon.normal.normalized() for polygon in mesh.polygons]
    edge_faces = {}
    for polygon in mesh.polygons:
        vertices = polygon.vertices
        for index in range(len(vertices)):
            key = tuple(sorted((vertices[index], vertices[(index + 1) % len(vertices)])))
            edge_faces.setdefault(key, []).append(polygon.index)
    angles = []
    foldovers = []
    hard = []
    for key, linked in edge_faces.items():
        if len(linked) != 2:
            continue
        angle = math.degrees(face_normals[linked[0]].angle(face_normals[linked[1]], 0.0))
        angles.append(angle)
        if angle >= 90.0:
            foldovers.append((key, linked, angle))
        if angle >= 45.0:
            hard.append((key, linked, angle))
    return {
        "adjacentAngleMaximumDegrees": max(angles, default=0.0),
        "foldoverEdgeCountAt90Degrees": len(foldovers),
        "foldoverEdgesAt90Degrees": foldovers,
        "hardEdgeCountAt45Degrees": len(hard),
        "result": "PASS" if not foldovers else "FAIL",
    }


def bvh_self_overlap(mesh):
    mesh.calc_loop_triangles()
    vertices = [vertex.co.copy() for vertex in mesh.vertices]
    triangles = [tuple(triangle.vertices) for triangle in mesh.loop_triangles]
    tree = BVHTree.FromPolygons(vertices, triangles, all_triangles=True, epsilon=1.0e-9)
    raw = tree.overlap(tree)
    unique = set()
    penetrating = []
    for left, right in raw:
        if left >= right:
            continue
        unique.add((left, right))
        if set(triangles[left]).isdisjoint(triangles[right]):
            penetrating.append((left, right))
    return {
        "triangleCount": len(triangles),
        "rawUniqueOverlapPairCount": len(unique),
        "nonAdjacentOverlapPairCount": len(penetrating),
        "nonAdjacentOverlapPairs": penetrating[:100],
        "result": "PASS" if not penetrating else "FAIL",
    }


def mirror(mesh, tolerance=1.0e-6):
    points = [vertex.co.copy() for vertex in mesh.vertices]
    tree = KDTree(len(points))
    for index, point in enumerate(points):
        tree.insert(point, index)
    tree.balance()
    deviations = []
    unmatched = 0
    for point in points:
        _, _, distance = tree.find(Vector((-point.x, point.y, point.z)))
        deviations.append(distance)
        unmatched += int(distance > tolerance)
    maximum = max(deviations, default=0.0)
    return {
        "axis": "X",
        "tolerance": tolerance,
        "unmatchedVertexCount": unmatched,
        "maximumPositionDeviation": maximum,
        "result": "PASS" if unmatched == 0 and maximum <= tolerance else "FAIL",
    }


def analyze(mesh):
    histogram = {}
    for polygon in mesh.polygons:
        key = str(len(polygon.vertices))
        histogram[key] = histogram.get(key, 0) + 1
    sections = {
        "manifold": manifold(mesh),
        "mirror": mirror(mesh),
        "nonConvex": concavity(mesh),
        "fold": folds(mesh),
        "bvhSelfIntersection": bvh_self_overlap(mesh),
    }
    all_quads = histogram == {"4": len(mesh.polygons)}
    result = all(section["result"] == "PASS" for section in sections.values()) and all_quads
    return {
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "polygons": len(mesh.polygons),
        "polygonVertexCountHistogram": histogram,
        "allQuads": all_quads,
        **sections,
        "result": "PASS" if result else "FAIL",
    }


def set_world(scene, color, strength):
    background = next(node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND")
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def set_light_pass(lights, rake_enabled):
    for light in lights["standard"]:
        light.hide_render = rake_enabled
    lights["rake"].hide_render = not rake_enabled


def render_views(scene, directory):
    os.makedirs(directory, exist_ok=True)
    cameras = {name: bpy.data.objects[f"CAM_C1BRW005_{name}"] for name in VIEW_NAMES}
    standard_names = ("QA_Key_Left", "QA_Key_Right", "QA_Back", "QA_Left", "QA_Right")
    lights = {
        "standard": [bpy.data.objects[name] for name in standard_names],
        "rake": bpy.data.objects["QA_Rake"],
    }
    ground = bpy.data.objects["QA_Ground"]
    silhouette = bpy.data.materials["MAT_C1BRW005_Silhouette"]
    rake = bpy.data.materials["MAT_C1BRW005_SemiGlossRake"]
    layer = scene.view_layers[0]
    outputs = []
    styles = (
        ("Neutral", None, False, False, (0.18, 0.18, 0.18), 1.0),
        ("Silhouette", silhouette, True, False, (0.75, 0.75, 0.75), 0.8),
        ("RakeLight", rake, True, True, (0.08, 0.08, 0.08), 0.55),
    )
    for style, override, hide_ground, rake_enabled, color, strength in styles:
        layer.material_override = override
        ground.hide_render = hide_ground
        set_light_pass(lights, rake_enabled)
        set_world(scene, color, strength)
        for view in VIEW_NAMES:
            scene.camera = cameras[view]
            filename = f"{ASSET_ID}_{ASSET_REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    layer.material_override = None
    ground.hide_render = False
    set_light_pass(lights, False)
    set_world(scene, (0.18, 0.18, 0.18), 1.0)
    scene.camera = cameras["Front"]
    return outputs


def head_report(head):
    coordinates = [head.matrix_world @ vertex.co for vertex in head.data.vertices]
    minimum = [min(point[axis] for point in coordinates) for axis in range(3)]
    maximum = [max(point[axis] for point in coordinates) for axis in range(3)]
    return {
        "object": head.name,
        "vertices": len(head.data.vertices),
        "edges": len(head.data.edges),
        "polygons": len(head.data.polygons),
        "boundsMinimum": minimum,
        "boundsMaximum": maximum,
        "eyesCreated": False,
        "visibleNeckAllowed": False,
        "directBodyContact": True,
    }


def main():
    blend_path, render_directory, report_path = parse_args()
    if not os.path.exists(CANONICAL_BLEND):
        raise FileNotFoundError(CANONICAL_BLEND)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=CANONICAL_BLEND)
    body = bpy.data.objects["C1B_R06_SingleQuadBody"]
    head = bpy.data.objects["C1B_R06_RoundFacelessHead"]
    if body.modifiers:
        raise RuntimeError("canonical frozen body must not have runtime modifiers")
    base_mesh = mesh_copy(body, evaluated=False)
    evaluated_mesh = mesh_copy(body, evaluated=True)
    base = analyze(base_mesh)
    evaluated = analyze(evaluated_mesh)
    bpy.data.meshes.remove(base_mesh)
    bpy.data.meshes.remove(evaluated_mesh)
    if base["result"] != "PASS" or evaluated["result"] != "PASS":
        raise RuntimeError("canonical exact QA failed")
    outputs = render_views(bpy.context.scene, render_directory)
    report = {
        "assetId": ASSET_ID,
        "assetVersion": ASSET_VERSION,
        "revision": ASSET_REVISION,
        "sourceOwner": SOURCE_OWNER,
        "reference": {"path": REFERENCE_PATH, "sha256": REFERENCE_SHA256},
        "construction": CONSTRUCTION,
        "bodyAuthoredPartCount": 1,
        "runtimeModifierCount": len(body.modifiers),
        "baseControlCage": base,
        "evaluatedRenderSurface": evaluated,
        "head": head_report(head),
        "renderMeshObjects": 2,
        "renderPasses": ["Neutral", "Silhouette", "RakeLight"],
        "renderFiles": outputs,
        "eyesCreated": False,
        "handsCreated": False,
        "fingersCreated": False,
        "visibleNeckAllowed": False,
        "playerBuildsExecuted": 0,
        "result": "PASS",
    }
    with open(report_path, "w", encoding="utf-8") as output:
        json.dump(report, output, ensure_ascii=False, indent=2, sort_keys=True)
        output.write("\n")
    scene = bpy.context.scene
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["candidate_status"] = "LOCAL_USER_REVIEW"
    scene["source_owner"] = SOURCE_OWNER
    scene["reference_path"] = REFERENCE_PATH
    scene["reference_sha256"] = REFERENCE_SHA256
    scene["construction"] = CONSTRUCTION
    scene["body_authored_part_count"] = 1
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["exact_qa_result"] = "PASS"
    scene["topology_report_json"] = json.dumps(report, sort_keys=True, separators=(",", ":"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print(f"C1B_RW006_BLEND={blend_path}")
    print(f"C1B_RW006_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW006_REPORT={report_path}")
    print(f"C1B_RW006_RENDER_COUNT={len(outputs)}")
    print("C1B_RW006_BASE_EXACT_QA=" + json.dumps(base, sort_keys=True, separators=(",", ":")))
    print("C1B_RW006_EVALUATED_EXACT_QA=" + json.dumps(evaluated, sort_keys=True, separators=(",", ":")))
    print("C1B_RW006_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
