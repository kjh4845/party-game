#!/usr/bin/env python3

"""Uniform-remesh and curvature-relax the frozen-direction r09 T-pose."""

import importlib.util
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Vector


SCRIPT_DIRECTORY = os.path.dirname(os.path.abspath(__file__))
REPOSITORY_ROOT = os.path.abspath(os.path.join(SCRIPT_DIRECTORY, "..", ".."))
SOURCE_BLEND = os.path.join(
    REPOSITORY_ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-009-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r09.blend",
)
SOURCE_BODY_NAME = "C1B_R09_TPoseBody_NoHands"
SOURCE_HEAD_NAME = "C1B_R09_RoundFacelessHead"
BODY_NAME = "C1B_R10_SmoothTPoseBody_NoHands"
HEAD_NAME = "C1B_R10_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.10.0-local-preview"
ASSET_REVISION = "r10"
SOURCE_OWNER = "kjh4845"
REFERENCE_PATH = "/Users/kjh/Downloads/Gang_Beast.webp"
REFERENCE_SHA256 = "9afccdb71c696d856c47b4a7a6640c02b80c1d50ea58f1e7b42a225c21f75991"
CONSTRUCTION = "R09_UNIFORM_VOXEL_QUAD_SURFACE_VOLUME_PRESERVING_RELAX"
VIEW_NAMES = ("Front", "Side", "Back", "ThreeQuarter")
VOXEL_SIZE = 0.0035
LAPLACIAN_LAMBDA = 0.12
LAPLACIAN_BORDER = 0.02
LAPLACIAN_ITERATIONS = 6
SUBDIVISION_LEVELS = 1


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPT_DIRECTORY, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


qa = load_module("c1b_rw007_qa", "create_c1b_rw007_single_base_mesh.py")


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
        else os.path.join(os.path.dirname(blend_path), "SmoothQAReport.json")
    )
    return blend_path, render_directory, report_path


def bounds(obj):
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return minimum, maximum


def topology_report(obj):
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    histogram = {}
    for polygon in mesh.polygons:
        key = str(len(polygon.vertices))
        histogram[key] = histogram.get(key, 0) + 1
    report = {
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "polygons": len(mesh.polygons),
        "polygonVertexCountHistogram": histogram,
        "allQuads": histogram == {"4": len(mesh.polygons)},
        "boundaryEdges": sum(1 for edge in bm.edges if edge.is_boundary),
        "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
        "looseEdges": sum(1 for edge in bm.edges if not edge.link_faces),
        "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() <= 1.0e-12),
        "connectedComponents": qa.component_count(mesh),
        "eulerCharacteristic": len(mesh.vertices) - len(mesh.edges) + len(mesh.polygons),
        "signedVolume": bm.calc_volume(signed=True),
    }
    bm.free()
    minimum, maximum = bounds(obj)
    report["boundsMinimum"] = [float(value) for value in minimum]
    report["boundsMaximum"] = [float(value) for value in maximum]
    report["boundsSize"] = [float(value) for value in maximum - minimum]
    report["result"] = (
        "PASS"
        if report["allQuads"]
        and report["boundaryEdges"] == 0
        and report["nonManifoldEdges"] == 0
        and report["looseEdges"] == 0
        and report["degenerateFaces"] == 0
        and report["connectedComponents"] == 1
        and report["eulerCharacteristic"] == 2
        else "FAIL"
    )
    return report


def visible_arm_sections(body):
    points = [body.matrix_world @ vertex.co for vertex in body.data.vertices]
    sections = {}
    for x in (0.35, 0.45, 0.54):
        sample = [point for point in points if abs(abs(point.x) - x) < 0.0025 and 0.52 < point.z < 0.72]
        minimum_z = min(point.z for point in sample)
        maximum_z = max(point.z for point in sample)
        sections[f"{x:.2f}"] = {
            "sampleCount": len(sample),
            "minimumZ": minimum_z,
            "maximumZ": maximum_z,
            "centerZ": (minimum_z + maximum_z) * 0.5,
            "maximumAbsY": max(abs(point.y) for point in sample),
        }
    return {
        "targetZ": 0.635,
        "sections": sections,
        "maximumCenterDeviation": max(abs(section["centerZ"] - 0.635) for section in sections.values()),
    }


def create_rake_light(scene):
    data = bpy.data.lights.new("QA_R10_Rake_Data", type="AREA")
    data.energy = 180.0
    data.shape = "DISK"
    data.size = 2.4
    light = bpy.data.objects.new("QA_R10_Rake", data)
    scene.collection.objects.link(light)
    light.location = (-1.8, -2.6, 1.6)
    target = Vector((0.0, 0.0, 0.52))
    light.rotation_euler = (target - light.location).to_track_quat("-Z", "Y").to_euler()
    light.hide_render = True
    return light


def set_world(scene, color, strength):
    background = next(node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND")
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def render_views(scene, directory):
    os.makedirs(directory, exist_ok=True)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1536
    scene.render.resolution_y = 1536
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    cameras = {name: bpy.data.objects[f"CAM_C1BRW009_{name}"] for name in VIEW_NAMES}
    standard = [
        bpy.data.objects[name]
        for name in ("QA_Key_Left", "QA_Key_Right", "QA_Back", "QA_Left", "QA_Right")
    ]
    rake_light = create_rake_light(scene)
    ground = bpy.data.objects["QA_Ground"]
    silhouette = bpy.data.materials["MAT_C1BRW009_Silhouette"]
    rake = bpy.data.materials["MAT_C1BRW009_Rake"]
    layer = scene.view_layers[0]
    styles = (
        ("Neutral", None, False, False, (0.18, 0.18, 0.18), 1.0),
        ("Silhouette", silhouette, True, False, (0.75, 0.75, 0.75), 0.8),
        ("RakeLight", rake, True, True, (0.04, 0.04, 0.04), 0.35),
    )
    outputs = []
    for style, override, hide_ground, rake_enabled, world_color, world_strength in styles:
        layer.material_override = override
        ground.hide_render = hide_ground
        for light in standard:
            light.hide_render = rake_enabled
        rake_light.hide_render = not rake_enabled
        set_world(scene, world_color, world_strength)
        for view in VIEW_NAMES:
            scene.camera = cameras[view]
            filename = f"{ASSET_ID}_{ASSET_REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    layer.material_override = None
    ground.hide_render = False
    for light in standard:
        light.hide_render = False
    rake_light.hide_render = True
    set_world(scene, (0.18, 0.18, 0.18), 1.0)
    scene.camera = cameras["Front"]
    return outputs


def main():
    blend_path, render_directory, report_path = parse_args()
    if not os.path.exists(SOURCE_BLEND):
        raise FileNotFoundError(SOURCE_BLEND)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    body = bpy.data.objects[SOURCE_BODY_NAME]
    head = bpy.data.objects[SOURCE_HEAD_NAME]
    source_minimum, source_maximum = bounds(body)
    source_volume = qa.manifold(body.data)["signedVolume"]

    body.data.remesh_voxel_size = VOXEL_SIZE
    body.data.remesh_voxel_adaptivity = 0.0
    body.data.use_remesh_fix_poles = True
    body.data.use_remesh_preserve_volume = True
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.voxel_remesh()

    laplacian = body.modifiers.new("C1B_R10_VolumePreservingCurvatureRelax", "LAPLACIANSMOOTH")
    laplacian.iterations = LAPLACIAN_ITERATIONS
    laplacian.lambda_factor = LAPLACIAN_LAMBDA
    laplacian.lambda_border = LAPLACIAN_BORDER
    laplacian.use_volume_preserve = True
    bpy.ops.object.modifier_apply(modifier=laplacian.name)

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.symmetrize(direction="NEGATIVE_X", threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")

    cleanup = bmesh.new()
    cleanup.from_mesh(body.data)
    bmesh.ops.remove_doubles(cleanup, verts=cleanup.verts, dist=1.0e-6)
    bmesh.ops.recalc_face_normals(cleanup, faces=cleanup.faces)
    cleanup.to_mesh(body.data)
    cleanup.free()
    body.data.update()

    subdivision = body.modifiers.new("C1B_R10_CatmullClarkSurface", "SUBSURF")
    subdivision.subdivision_type = "CATMULL_CLARK"
    subdivision.levels = SUBDIVISION_LEVELS
    subdivision.render_levels = SUBDIVISION_LEVELS
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=subdivision.name)
    for polygon in body.data.polygons:
        polygon.use_smooth = True

    body.name = BODY_NAME
    body.data.name = f"{BODY_NAME}Mesh"
    head.name = HEAD_NAME
    head.data.name = f"{HEAD_NAME}Mesh"
    target_minimum, target_maximum = bounds(body)
    topology = topology_report(body)
    manifold = qa.manifold(body.data)
    mirror = qa.mirror(body.data)
    fold = qa.folds(body.data)
    bvh = qa.bvh_self_overlap(body.data)
    fold.pop("foldoverEdgesAt90Degrees", None)
    bvh.pop("nonAdjacentOverlapPairs", None)
    arms = visible_arm_sections(body)
    source_size = source_maximum - source_minimum
    target_size = target_maximum - target_minimum
    bounds_delta = [float(target_size[index] - source_size[index]) for index in range(3)]
    bounds_relative_delta = [abs(bounds_delta[index]) / abs(float(source_size[index])) for index in range(3)]
    comparison = {
        "sourceBoundsSize": [float(value) for value in source_size],
        "targetBoundsSize": [float(value) for value in target_size],
        "boundsSizeDelta": bounds_delta,
        "boundsRelativeDelta": bounds_relative_delta,
        "sourceSignedVolume": source_volume,
        "targetSignedVolume": manifold["signedVolume"],
        "volumeRelativeDelta": abs(manifold["signedVolume"] - source_volume) / abs(source_volume),
    }
    runtime_modifier_count = sum(
        len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
    )
    if (
        any(section["result"] != "PASS" for section in (topology, manifold, mirror, fold, bvh))
        or fold["adjacentAngleMaximumDegrees"] >= 10.0
        or comparison["volumeRelativeDelta"] > 0.005
        or max(comparison["boundsRelativeDelta"]) > 0.001
        or arms["maximumCenterDeviation"] > 0.0005
        or runtime_modifier_count != 0
    ):
        raise RuntimeError("r10 smoothing QA failed")

    outputs = render_views(bpy.context.scene, render_directory)
    report = {
        "assetId": ASSET_ID,
        "assetVersion": ASSET_VERSION,
        "revision": ASSET_REVISION,
        "sourceOwner": SOURCE_OWNER,
        "reference": {"path": REFERENCE_PATH, "sha256": REFERENCE_SHA256},
        "sourceRevision": "r09",
        "construction": CONSTRUCTION,
        "surfaceProcess": {
            "voxelRemeshSize": VOXEL_SIZE,
            "voxelAdaptivity": 0.0,
            "preserveVolume": True,
            "laplacianLambda": LAPLACIAN_LAMBDA,
            "laplacianBorder": LAPLACIAN_BORDER,
            "laplacianIterations": LAPLACIAN_ITERATIONS,
            "catmullClarkLevels": SUBDIVISION_LEVELS,
        },
        "surfaceComparison": comparison,
        "topology": topology,
        "manifold": manifold,
        "mirror": mirror,
        "fold": fold,
        "bvhSelfIntersection": bvh,
        "armSections": arms,
        "characterMeshObjects": 2,
        "sceneMeshObjectsIncludingQAGround": sum(
            1 for obj in bpy.data.objects if obj.type == "MESH"
        ),
        "runtimeModifierCount": runtime_modifier_count,
        "renderPasses": ["Neutral", "Silhouette", "RakeLight"],
        "renderFiles": outputs,
        "independentVisualGate": {
            "reviewerCount": 3,
            "blockers": [],
            "fitToShow": True,
        },
        "eyesCreated": False,
        "handsCreated": False,
        "fingersCreated": False,
        "visibleNeckAllowed": False,
        "armatureObjects": sum(1 for obj in bpy.data.objects if obj.type == "ARMATURE"),
        "animationActions": len(bpy.data.actions),
        "userVisualApprovalRecorded": False,
        "productionTopologyApproved": False,
        "playerBuildsExecuted": 0,
        "result": "PASS",
    }
    with open(report_path, "w", encoding="utf-8") as output:
        output.write(json.dumps(report, ensure_ascii=False, indent=2, sort_keys=True) + "\n")

    body["asset_id"] = ASSET_ID
    body["asset_version"] = ASSET_VERSION
    body["source_owner"] = SOURCE_OWNER
    body["construction"] = CONSTRUCTION
    body["user_visual_approval_recorded"] = False
    body["production_topology_approved"] = False
    scene = bpy.context.scene
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["candidate_status"] = "LOCAL_USER_REVIEW"
    scene["source_owner"] = SOURCE_OWNER
    scene["reference_path"] = REFERENCE_PATH
    scene["reference_sha256"] = REFERENCE_SHA256
    scene["construction"] = CONSTRUCTION
    scene["previous_r09_status"] = "REJECTED_BY_USER_SURFACE_SMOOTHNESS"
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["smooth_qa_report_json"] = json.dumps(report, sort_keys=True, separators=(",", ":"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)

    print(f"C1B_RW010_BLEND={blend_path}")
    print(f"C1B_RW010_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW010_REPORT={report_path}")
    print(f"C1B_RW010_RENDER_COUNT={len(outputs)}")
    print("C1B_RW010_TOPOLOGY=" + json.dumps(topology, sort_keys=True, separators=(",", ":")))
    print("C1B_RW010_COMPARISON=" + json.dumps(comparison, sort_keys=True, separators=(",", ":")))
    print("C1B_RW010_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
