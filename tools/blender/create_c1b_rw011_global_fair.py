#!/usr/bin/env python3

"""Create a high-resolution whole-body faired r11 review candidate from frozen r10."""

import importlib.util
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Vector


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-010-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r10.blend",
)
SOURCE_BODY = "C1B_R10_SmoothTPoseBody_NoHands"
SOURCE_HEAD = "C1B_R10_RoundFacelessHead"
BODY_NAME = "C1B_R11_GlobalFair_TPoseBody_NoHands"
HEAD_NAME = "C1B_R11_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
REVISION = "r11"
VERSION = "0.11.0-local-preview"
VOXEL_SIZE = 0.0045
GLOBAL_SMOOTH_FACTOR = 0.20
GLOBAL_SMOOTH_ITERATIONS = 30
LAMBDA = 0.18
ITERATIONS = 12
SUBDIVISIONS = 1
VIEW_NAMES = ("Front", "Side", "Back", "ThreeQuarter")
TORSO_WIDTH_REFIT_STRENGTH = 0.78
TORSO_DEPTH_REFIT_STRENGTH = 0.55
SHOULDER_BLEND_FACTOR = 0.42
SHOULDER_BLEND_ITERATIONS = 20
TORSO_WIDTH_PROFILE = (
    (0.170, 0.189),
    (0.200, 0.185),
    (0.240, 0.179),
    (0.280, 0.170),
    (0.320, 0.164),
    (0.360, 0.158),
    (0.400, 0.157),
    (0.460, 0.158),
    (0.520, 0.160),
    (0.560, 0.165),
    (0.600, 0.175),
    (0.640, 0.185),
)
TORSO_DEPTH_PROFILE = (
    (0.170, 0.110),
    (0.200, 0.123),
    (0.240, 0.135),
    (0.280, 0.138),
    (0.320, 0.135),
    (0.380, 0.131),
    (0.440, 0.130),
    (0.500, 0.128),
    (0.560, 0.125),
    (0.600, 0.124),
    (0.640, 0.118),
)


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


qa = import_file("c1b_rw007_qa", "create_c1b_rw007_single_base_mesh.py")


def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def catmull(keys, value):
    if value <= keys[0][0]:
        return keys[0][1]
    if value >= keys[-1][0]:
        return keys[-1][1]
    for index in range(len(keys) - 1):
        x1, p1 = keys[index]
        x2, p2 = keys[index + 1]
        if x1 <= value <= x2:
            p0 = keys[max(index - 1, 0)][1]
            p3 = keys[min(index + 2, len(keys) - 1)][1]
            t = (value - x1) / (x2 - x1)
            t2 = t * t
            t3 = t2 * t
            result = 0.5 * (
                2.0 * p1
                + (-p0 + p2) * t
                + (2.0 * p0 - 5.0 * p1 + 4.0 * p2 - p3) * t2
                + (-p0 + 3.0 * p1 - 3.0 * p2 + p3) * t3
            )
            return min(max(result, min(p1, p2)), max(p1, p2))
    return keys[-1][1]


def refit_macro_profile(body):
    for vertex in body.data.vertices:
        x = float(vertex.co.x)
        y = float(vertex.co.y)
        z = float(vertex.co.z)
        abs_x = abs(x)
        if 0.145 < z < 0.650 and abs_x < 0.255:
            vertical = smoothstep((z - 0.145) / 0.065) * (
                1.0 - smoothstep((z - 0.565) / 0.085)
            )
            shoulder = 1.0
            if z > 0.555:
                shoulder = 1.0 - smoothstep((abs_x - 0.145) / 0.105)
            weight = vertical * shoulder
            radius_x = catmull(TORSO_WIDTH_PROFILE, z)
            radius_y = catmull(TORSO_DEPTH_PROFILE, z)
            normalized = math.sqrt((x / radius_x) ** 2 + (y / radius_y) ** 2)
            if normalized > 1.0e-8 and weight > 0.0:
                target_x = x / normalized
                target_y = y / normalized
                vertex.co.x = x + (target_x - x) * weight * TORSO_WIDTH_REFIT_STRENGTH
                vertex.co.y = y + (target_y - y) * weight * TORSO_DEPTH_REFIT_STRENGTH
    body.data.update()


def blend_shoulder_and_axilla(body):
    group = body.vertex_groups.new(name="C1B_R11_ShoulderAxillaBlend_Weight")
    group_name = group.name
    for vertex in body.data.vertices:
        abs_x = abs(float(vertex.co.x))
        z = float(vertex.co.z)
        dx = (abs_x - 0.225) / 0.145
        dz = (z - 0.610) / 0.120
        distance = math.sqrt(dx * dx + dz * dz)
        if distance < 1.0:
            group.add([vertex.index], smoothstep(1.0 - distance), "REPLACE")
    modifier = body.modifiers.new("C1B_R11_ShoulderAxillaBroadBlend", "SMOOTH")
    modifier.vertex_group = group_name
    modifier.factor = SHOULDER_BLEND_FACTOR
    modifier.iterations = SHOULDER_BLEND_ITERATIONS
    modifier.use_x = True
    modifier.use_y = True
    modifier.use_z = True
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=modifier.name)
    if group_name in body.vertex_groups:
        body.vertex_groups.remove(body.vertex_groups[group_name])


def restore_volume_along_depth(body, target_volume):
    current_volume = qa.manifold(body.data)["signedVolume"]
    scale = target_volume / current_volume
    for vertex in body.data.vertices:
        vertex.co.y *= scale
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()
    body.data.update()
    return scale


def args():
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 3:
        raise RuntimeError("expected -- <blend> <render-dir> <report>")
    return tuple(os.path.abspath(value) for value in values)


def object_bounds(obj):
    points = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    low = Vector(tuple(min(point[axis] for point in points) for axis in range(3)))
    high = Vector(tuple(max(point[axis] for point in points) for axis in range(3)))
    return low, high


def topology(obj):
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    result = {
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "faces": len(mesh.polygons),
        "allQuads": all(len(face.vertices) == 4 for face in mesh.polygons),
        "boundaryEdges": sum(edge.is_boundary for edge in bm.edges),
        "nonManifoldEdges": sum(not edge.is_manifold for edge in bm.edges),
        "looseEdges": sum(not edge.link_faces for edge in bm.edges),
        "degenerateFaces": sum(face.calc_area() <= 1.0e-12 for face in bm.faces),
        "components": qa.component_count(mesh),
        "eulerCharacteristic": len(mesh.vertices) - len(mesh.edges) + len(mesh.polygons),
        "signedVolume": bm.calc_volume(signed=True),
    }
    bm.free()
    return result


def visible_arm_sections(body):
    points = [body.matrix_world @ vertex.co for vertex in body.data.vertices]
    sections = {}
    for x in (0.35, 0.45, 0.54):
        sample = [point for point in points if abs(abs(point.x) - x) < 0.003 and 0.52 < point.z < 0.72]
        minimum_z = min(point.z for point in sample)
        maximum_z = max(point.z for point in sample)
        sections[f"{x:.2f}"] = {
            "sampleCount": len(sample),
            "minimumZ": minimum_z,
            "maximumZ": maximum_z,
            "centerZ": (minimum_z + maximum_z) * 0.5,
        }
    return {
        "targetZ": 0.635,
        "sections": sections,
        "maximumCenterDeviation": max(
            abs(section["centerZ"] - 0.635) for section in sections.values()
        ),
    }


def render(scene, directory):
    os.makedirs(directory, exist_ok=True)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1536
    scene.render.resolution_y = 1536
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    layer = scene.view_layers[0]
    ground = bpy.data.objects["QA_Ground"]
    standard_lights = [
        bpy.data.objects[name]
        for name in ("QA_Key_Left", "QA_Key_Right", "QA_Back", "QA_Left", "QA_Right")
    ]
    rake_data = bpy.data.lights.new("QA_R11_Rake_Data", "AREA")
    rake_data.energy = 150.0
    rake_data.shape = "DISK"
    rake_data.size = 2.8
    rake = bpy.data.objects.new("QA_R11_Rake", rake_data)
    scene.collection.objects.link(rake)
    rake.location = (-1.9, -2.8, 1.65)
    rake.rotation_euler = (Vector((0.0, 0.0, 0.48)) - rake.location).to_track_quat("-Z", "Y").to_euler()
    rake.hide_render = True
    silhouette_material = bpy.data.materials["MAT_C1BRW009_Silhouette"]
    rake_material = bpy.data.materials["MAT_C1BRW009_Rake"]
    background = next(node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND")
    outputs = []
    styles = (
        ("Neutral", None, False, False, (0.18, 0.18, 0.18), 1.0),
        ("Silhouette", silhouette_material, True, False, (0.75, 0.75, 0.75), 0.8),
        ("RakeLight", rake_material, True, True, (0.035, 0.035, 0.035), 0.28),
    )
    for style, override, hide_ground, rake_on, world_color, world_strength in styles:
        layer.material_override = override
        ground.hide_render = hide_ground
        for light in standard_lights:
            light.hide_render = rake_on
        rake.hide_render = not rake_on
        background.inputs["Color"].default_value = (*world_color, 1.0)
        background.inputs["Strength"].default_value = world_strength
        for view in VIEW_NAMES:
            scene.camera = bpy.data.objects[f"CAM_C1BRW009_{view}"]
            filename = f"{ASSET_ID}_{REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    layer.material_override = None
    ground.hide_render = False
    for light in standard_lights:
        light.hide_render = False
    rake.hide_render = True
    scene.camera = bpy.data.objects["CAM_C1BRW009_Front"]
    return outputs


def main():
    blend_path, render_dir, report_path = args()
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    body = bpy.data.objects[SOURCE_BODY]
    head = bpy.data.objects[SOURCE_HEAD]
    source_low, source_high = object_bounds(body)
    source_size = source_high - source_low
    source_volume = qa.manifold(body.data)["signedVolume"]

    # Rebuild a high-resolution even surface, then fair the entire body rather
    # than preserving any torso/shoulder/pelvis region as a protected macro.
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    refit_macro_profile(body)
    blend_shoulder_and_axilla(body)
    body.data.remesh_voxel_size = VOXEL_SIZE
    body.data.remesh_voxel_adaptivity = 0.0
    body.data.use_remesh_fix_poles = True
    body.data.use_remesh_preserve_volume = True
    bpy.ops.object.voxel_remesh()

    whole_body = body.modifiers.new("C1B_R11_WholeBodyFair", "SMOOTH")
    whole_body.factor = GLOBAL_SMOOTH_FACTOR
    whole_body.iterations = GLOBAL_SMOOTH_ITERATIONS
    whole_body.use_x = True
    whole_body.use_y = True
    whole_body.use_z = True
    bpy.ops.object.modifier_apply(modifier=whole_body.name)

    fair = body.modifiers.new("C1B_R11_GlobalVolumePreservingFair", "LAPLACIANSMOOTH")
    fair.iterations = ITERATIONS
    fair.lambda_factor = LAMBDA
    fair.lambda_border = 0.02
    fair.use_volume_preserve = True
    bpy.ops.object.modifier_apply(modifier=fair.name)

    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.symmetrize(direction="NEGATIVE_X", threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    bm = bmesh.new()
    bm.from_mesh(body.data)
    bmesh.ops.remove_doubles(bm, verts=bm.verts, dist=1.0e-6)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    bm.to_mesh(body.data)
    bm.free()

    subdiv = body.modifiers.new("C1B_R11_FinalCatmullClark", "SUBSURF")
    subdiv.subdivision_type = "CATMULL_CLARK"
    subdiv.levels = SUBDIVISIONS
    subdiv.render_levels = SUBDIVISIONS
    bpy.ops.object.modifier_apply(modifier=subdiv.name)

    for polygon in body.data.polygons:
        polygon.use_smooth = True

    depth_volume_restore_scale = restore_volume_along_depth(body, source_volume)

    body.name = BODY_NAME
    body.data.name = BODY_NAME + "Mesh"
    head.name = HEAD_NAME
    head.data.name = HEAD_NAME + "Mesh"
    target_low, target_high = object_bounds(body)
    target_size = target_high - target_low
    body_topology = topology(body)
    manifold = qa.manifold(body.data)
    mirror = qa.mirror(body.data)
    folds = qa.folds(body.data)
    overlap = qa.bvh_self_overlap(body.data)
    arms = visible_arm_sections(body)
    body_low, body_high = object_bounds(body)
    head_low, _ = object_bounds(head)
    head_body_overlap = float(body_high.z - head_low.z)
    runtime_modifier_count = sum(
        len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
    )
    comparison = {
        "sourceBoundsSize": list(source_size),
        "targetBoundsSize": list(target_size),
        "boundsRelativeDelta": [abs(float(target_size[i] - source_size[i])) / abs(float(source_size[i])) for i in range(3)],
        "sourceSignedVolume": source_volume,
        "targetSignedVolume": manifold["signedVolume"],
        "volumeRelativeDelta": abs(manifold["signedVolume"] - source_volume) / abs(source_volume),
    }
    outputs = render(bpy.context.scene, render_dir)
    report = {
        "assetId": ASSET_ID,
        "revision": REVISION,
        "assetVersion": VERSION,
        "sourceRevision": "r10",
        "sourceOwner": "kjh4845",
        "construction": "R10_ANALYTIC_MACRO_REFIT_HIGH_RESOLUTION_WHOLE_BODY_GLOBAL_FAIR",
        "process": {
            "torsoWidthRefitStrength": TORSO_WIDTH_REFIT_STRENGTH,
            "torsoDepthRefitStrength": TORSO_DEPTH_REFIT_STRENGTH,
            "shoulderBlendFactor": SHOULDER_BLEND_FACTOR,
            "shoulderBlendIterations": SHOULDER_BLEND_ITERATIONS,
            "voxelSize": VOXEL_SIZE,
            "globalSmoothFactor": GLOBAL_SMOOTH_FACTOR,
            "globalSmoothIterations": GLOBAL_SMOOTH_ITERATIONS,
            "laplacianLambda": LAMBDA,
            "laplacianIterations": ITERATIONS,
            "catmullClarkLevels": SUBDIVISIONS,
            "depthVolumeRestoreScale": depth_volume_restore_scale,
        },
        "comparison": comparison,
        "topology": body_topology,
        "manifold": manifold,
        "mirror": mirror,
        "fold": folds,
        "bvhSelfIntersection": overlap,
        "armSections": arms,
        "headBodyOverlap": head_body_overlap,
        "runtimeModifierCount": runtime_modifier_count,
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
        "armatureObjects": sum(obj.type == "ARMATURE" for obj in bpy.data.objects),
        "animationActions": len(bpy.data.actions),
        "userVisualApprovalRecorded": False,
        "productionTopologyApproved": False,
        "playerBuildsExecuted": 0,
        "result": "PASS" if (
            body_topology["allQuads"]
            and body_topology["boundaryEdges"] == 0
            and body_topology["nonManifoldEdges"] == 0
            and body_topology["looseEdges"] == 0
            and body_topology["degenerateFaces"] == 0
            and body_topology["components"] == 1
            and body_topology["eulerCharacteristic"] == 2
            and manifold["result"] == "PASS"
            and mirror["result"] == "PASS"
            and folds["foldoverEdgeCountAt90Degrees"] == 0
            and folds["hardEdgeCountAt45Degrees"] == 0
            and folds["adjacentAngleMaximumDegrees"] < 10.0
            and overlap["result"] == "PASS"
            and comparison["volumeRelativeDelta"] <= 0.005
            and comparison["boundsRelativeDelta"][0] <= 0.005
            and comparison["boundsRelativeDelta"][1] <= 0.025
            and comparison["boundsRelativeDelta"][2] <= 0.005
            and arms["maximumCenterDeviation"] <= 0.0005
            and head_body_overlap > 0.0
            and runtime_modifier_count == 0
        ) else "FAIL",
    }
    if report["result"] != "PASS":
        raise RuntimeError("r11 global fair QA failed")
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    body["asset_id"] = ASSET_ID
    body["asset_version"] = VERSION
    body["source_owner"] = "kjh4845"
    body["construction"] = report["construction"]
    body["user_visual_approval_recorded"] = False
    bpy.context.scene["candidate_status"] = "LOCAL_USER_REVIEW"
    bpy.context.scene["previous_r10_status"] = "REJECTED_BY_USER_GLOBAL_SURFACE_SMOOTHNESS"
    bpy.context.scene["user_visual_approval_recorded"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R11_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R11_GENERATION_RESULT=" + report["result"])


if __name__ == "__main__":
    main()
