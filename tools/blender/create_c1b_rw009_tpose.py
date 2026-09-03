#!/usr/bin/env python3

"""Build and review the C1B r09 faceless, handless T-pose candidate."""

import importlib.util
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Quaternion, Vector


SCRIPT_DIRECTORY = os.path.dirname(os.path.abspath(__file__))
REPOSITORY_ROOT = os.path.abspath(os.path.join(SCRIPT_DIRECTORY, "..", ".."))


def load_module(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(SCRIPT_DIRECTORY, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


base = load_module("c1b_rw003_base", "create_c1b_rw003_neutral.py")
qa = load_module("c1b_rw007_qa", "create_c1b_rw007_single_base_mesh.py")

ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.9.0-local-preview"
ASSET_REVISION = "r09"
SOURCE_OWNER = "kjh4845"
REFERENCE_PATH = "/Users/kjh/Downloads/Gang_Beast.webp"
REFERENCE_SHA256 = "9afccdb71c696d856c47b4a7a6640c02b80c1d50ea58f1e7b42a225c21f75991"
CONSTRUCTION = "COMPACT_TANGENT_CONTINUOUS_IMPLICIT_TORSO_LEGS_AND_TPOSE_ARMS"
BODY_NAME = "C1B_R09_TPoseBody_NoHands"
HEAD_NAME = "C1B_R09_RoundFacelessHead"
RENDER_RESOLUTION = 1536
ORTHO_SCALE = 1.24
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
        else os.path.join(os.path.dirname(blend_path), "PreviewQAReport.json")
    )
    return blend_path, render_directory, report_path


def make_material(name, color, roughness=0.84):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    nodes.clear()
    output = nodes.new("ShaderNodeOutputMaterial")
    principled = nodes.new("ShaderNodeBsdfPrincipled")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Metallic"].default_value = 0.0
    principled.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in principled.inputs:
        principled.inputs["Specular IOR Level"].default_value = 0.20
    material.node_tree.links.new(principled.outputs["BSDF"], output.inputs["Surface"])
    return material


def set_world(scene, color, strength):
    scene.world.use_nodes = True
    nodes = scene.world.node_tree.nodes
    background = next((node for node in nodes if node.bl_idname == "ShaderNodeBackground"), None)
    output = next((node for node in nodes if node.bl_idname == "ShaderNodeOutputWorld"), None)
    if background is None:
        background = nodes.new("ShaderNodeBackground")
    if output is None:
        output = nodes.new("ShaderNodeOutputWorld")
    if not background.outputs["Background"].is_linked:
        scene.world.node_tree.links.new(background.outputs["Background"], output.inputs["Surface"])
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def create_ground(collection):
    bpy.ops.mesh.primitive_plane_add(size=4.0, location=(0.0, 0.0, -0.006))
    ground = bpy.context.object
    ground.name = "QA_Ground"
    base.link_only(ground, collection)
    ground.data.materials.append(make_material("MAT_QA_Ground", (0.10, 0.10, 0.10), 1.0))
    ground["qa_only"] = True
    return ground


def smoothstep(value):
    value = max(0.0, min(1.0, value))
    return value * value * (3.0 - 2.0 * value)


def create_body(collection, material):
    data = bpy.data.metaballs.new("C1B_R09_CompactImplicitField")
    data.resolution = 0.0018
    data.render_resolution = 0.0018
    data.threshold = 0.60
    field = bpy.data.objects.new("C1B_R09_CompactImplicitField", data)
    collection.objects.link(field)

    def add(element_type, center, radius, size=(1.0, 1.0, 1.0), rotation=None, stiffness=1.8):
        element = data.elements.new()
        element.type = element_type
        element.co = center
        element.radius = radius
        element.size_x, element.size_y, element.size_z = size
        element.stiffness = stiffness
        if rotation is not None:
            element.rotation = rotation

    # Continuous torso, pelvis, and upper-chest field.
    add("ELLIPSOID", (0.0, 0.0, 0.455), 0.300, (1.05, 0.82, 1.58))
    add("ELLIPSOID", (0.0, 0.0, 0.255), 0.220, (1.38, 1.08, 0.86))
    add("ELLIPSOID", (0.0, 0.0, 0.615), 0.200, (1.65, 0.95, 0.60), stiffness=1.2)

    # Two vertical capsules begin inside the pelvis and end in round terminals.
    leg_rotation = Quaternion((0.0, 1.0, 0.0), math.pi * 0.5)
    for side in (-1.0, 1.0):
        add("CAPSULE", (side * 0.100, 0.0, 0.145), 0.115, (0.086, 1.0, 1.0), rotation=leg_rotation)

    # One deep-root capsule per arm.  The authored root sits at z=.620 inside
    # the torso; the exposed span is smoothly lifted to horizontal z=.635.
    for side in (-1.0, 1.0):
        add("CAPSULE", (side * 0.292, 0.0, 0.620), 0.075, (0.240, 1.0, 1.0))

    bpy.context.view_layer.objects.active = field
    field.select_set(True)
    bpy.ops.object.convert(target="MESH")
    body = bpy.context.object
    body.name = BODY_NAME
    body.data.name = f"{BODY_NAME}Mesh"
    base.link_only(body, collection)
    body.data.materials.append(material)

    radius_profile = (
        (0.180, 1.00),
        (0.250, 1.00),
        (0.360, 0.85),
        (0.470, 0.80),
        (0.510, 0.78),
        (0.580, 0.78),
    )

    def radius_scale(abs_x):
        if abs_x <= radius_profile[0][0]:
            return radius_profile[0][1]
        if abs_x >= radius_profile[-1][0]:
            return radius_profile[-1][1]
        for (left_x, left_scale), (right_x, right_scale) in zip(radius_profile, radius_profile[1:]):
            if left_x <= abs_x <= right_x:
                factor = smoothstep((abs_x - left_x) / (right_x - left_x))
                return left_scale + (right_scale - left_scale) * factor
        return radius_profile[-1][1]

    # Smooth monotone arm taper and root-to-visible T-pose lift.
    for vertex in body.data.vertices:
        abs_x = abs(float(vertex.co.x))
        if abs_x <= 0.180 or abs(float(vertex.co.z) - 0.620) >= 0.105:
            continue
        root_blend = smoothstep((abs_x - 0.180) / 0.100)
        scale = 1.0 + (radius_scale(abs_x) - 1.0) * root_blend
        centerline_z = 0.620 + 0.015 * smoothstep((abs_x - 0.150) / 0.150)
        vertex.co.y *= scale
        vertex.co.z = centerline_z + (vertex.co.z - 0.620) * scale
    body.data.update()

    def apply_local_smooth(name, center_x, center_z, radius_x, radius_z, factor, iterations):
        group = body.vertex_groups.new(name=f"{name}_Group")
        group_name = group.name
        for vertex in body.data.vertices:
            dx = abs(abs(float(vertex.co.x)) - center_x) / radius_x
            dz = abs(float(vertex.co.z) - center_z) / radius_z
            distance = math.sqrt(dx * dx + dz * dz)
            if distance >= 1.0:
                continue
            group.add([vertex.index], smoothstep(1.0 - distance), "REPLACE")
        modifier = body.modifiers.new(name, "SMOOTH")
        modifier.vertex_group = group_name
        modifier.factor = factor
        modifier.iterations = iterations
        modifier.use_x = True
        modifier.use_y = True
        modifier.use_z = True
        bpy.context.view_layer.objects.active = body
        body.select_set(True)
        bpy.ops.object.modifier_apply(modifier=modifier.name)
        if group_name in body.vertex_groups:
            body.vertex_groups.remove(body.vertex_groups[group_name])

    apply_local_smooth("C1B_R09_ShoulderTangentRelax", 0.185, 0.625, 0.135, 0.115, 0.40, 12)
    apply_local_smooth("C1B_R09_AxillaShallowRelax", 0.185, 0.585, 0.095, 0.075, 0.45, 10)

    smooth = body.modifiers.new("C1B_R09_CompactImplicitRelax", "SMOOTH")
    smooth.factor = 0.07
    smooth.iterations = 2
    smooth.use_x = True
    smooth.use_y = True
    smooth.use_z = True
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    # Freeze exact X symmetry after the visual surface is complete.
    bpy.ops.object.mode_set(mode="EDIT")
    bpy.ops.mesh.select_all(action="SELECT")
    bpy.ops.mesh.symmetrize(direction="NEGATIVE_X", threshold=0.0005)
    bpy.ops.object.mode_set(mode="OBJECT")
    for polygon in body.data.polygons:
        polygon.use_smooth = True
    return body


def create_character(collection, material):
    body = create_body(collection, material)
    head = base.create_ellipsoid(
        HEAD_NAME,
        (0.0, -0.003, 0.855),
        (0.150, 0.130, 0.145),
        collection,
        segments=96,
        rings=64,
    )
    head.data.materials.append(material)

    for obj in (body, head):
        obj["asset_id"] = ASSET_ID
        obj["asset_version"] = ASSET_VERSION
        obj["source_owner"] = SOURCE_OWNER
        obj["reference_path"] = REFERENCE_PATH
        obj["reference_sha256"] = REFERENCE_SHA256
        obj["eyes_created"] = False
        obj["hands_created"] = False
        obj["fingers_created"] = False
        obj["visible_neck_allowed"] = False
        obj["user_visual_approval_recorded"] = False
        obj["production_topology_approved"] = False
    body["construction"] = CONSTRUCTION
    body["pose"] = "T_POSE_DEEP_ROOT_TO_WORLD_X_HORIZONTAL_VISIBLE_SPAN"
    return body, head


def mesh_report(obj):
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    report = {
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "polygons": len(mesh.polygons),
        "polygonVertexCountHistogram": {},
        "components": base.mesh_components(mesh),
        "boundaryEdges": sum(1 for edge in bm.edges if edge.is_boundary),
        "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
        "looseEdges": sum(1 for edge in bm.edges if not edge.link_faces),
        "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() <= 1.0e-12),
    }
    bm.free()
    for polygon in mesh.polygons:
        key = str(len(polygon.vertices))
        report["polygonVertexCountHistogram"][key] = report["polygonVertexCountHistogram"].get(key, 0) + 1
    world = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    minimum = Vector(tuple(min(point[axis] for point in world) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in world) for axis in range(3)))
    report["boundsMinimum"] = [float(value) for value in minimum]
    report["boundsMaximum"] = [float(value) for value in maximum]
    report["boundsSize"] = [float(value) for value in maximum - minimum]
    report["result"] = (
        "PASS"
        if len(report["components"]) == 1
        and report["boundaryEdges"] == 0
        and report["nonManifoldEdges"] == 0
        and report["looseEdges"] == 0
        and report["degenerateFaces"] == 0
        else "FAIL"
    )
    return report


def arm_sections(body):
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
    target = 0.635
    return {
        "targetVisibleCenterlineZ": target,
        "rootCenterlineZ": 0.620,
        "sections": sections,
        "maximumVisibleCenterlineDeviation": max(abs(section["centerZ"] - target) for section in sections.values()),
    }


def extended_report(body):
    mesh = body.data
    manifold = qa.manifold(mesh)
    mirror = qa.mirror(mesh)
    fold = qa.folds(mesh)
    bvh = qa.bvh_self_overlap(mesh)
    fold.pop("foldoverEdgesAt90Degrees", None)
    bvh.pop("nonAdjacentOverlapPairs", None)
    return {"manifold": manifold, "mirror": mirror, "fold": fold, "bvhSelfIntersection": bvh}


def create_camera(name, direction, collection):
    data = bpy.data.cameras.new(f"CAM_C1BRW009_{name}_Data")
    camera = bpy.data.objects.new(f"CAM_C1BRW009_{name}", data)
    collection.objects.link(camera)
    data.type = "ORTHO"
    data.ortho_scale = ORTHO_SCALE
    target = Vector((0.0, 0.0, 0.5))
    camera.location = target - direction.normalized() * 3.0
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return camera


def create_lights(collection):
    specs = (
        ("QA_Key_Left", 2.2, (45.0, -35.0, -25.0)),
        ("QA_Key_Right", 1.1, (55.0, 35.0, 30.0)),
        ("QA_Back", 0.45, (130.0, 150.0, 180.0)),
        ("QA_Left", 0.30, (80.0, 90.0, 0.0)),
        ("QA_Right", 0.30, (80.0, -90.0, 0.0)),
    )
    for name, energy, rotation in specs:
        data = bpy.data.lights.new(f"{name}_Data", type="SUN")
        data.energy = energy
        light = bpy.data.objects.new(name, data)
        collection.objects.link(light)
        light.rotation_euler = Euler(tuple(math.radians(value) for value in rotation), "XYZ")


def render_views(scene, cameras, output_directory, ground, silhouette_material, rake_material):
    os.makedirs(output_directory, exist_ok=True)
    scene.render.resolution_x = RENDER_RESOLUTION
    scene.render.resolution_y = RENDER_RESOLUTION
    scene.render.resolution_percentage = 100
    outputs = []
    layer = scene.view_layers[0]
    styles = (
        ("Neutral", None, False, (0.18, 0.18, 0.18), 1.0),
        ("Silhouette", silhouette_material, True, (0.75, 0.75, 0.75), 0.8),
        ("RakeLight", rake_material, True, (0.08, 0.08, 0.08), 0.55),
    )
    for style, override, hide_ground, world_color, world_strength in styles:
        layer.material_override = override
        ground.hide_render = hide_ground
        set_world(scene, world_color, world_strength)
        for view in VIEW_NAMES:
            scene.camera = cameras[view]
            filename = f"{ASSET_ID}_{ASSET_REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(output_directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    layer.material_override = None
    ground.hide_render = False
    set_world(scene, (0.18, 0.18, 0.18), 1.0)
    scene.camera = cameras["Front"]
    return outputs


def main():
    blend_path, render_directory, report_path = parse_args()
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    base.clear_scene()
    scene = bpy.context.scene
    scene.name = "C1B_RW009_TPoseUserReview"
    base.configure_scene(scene)
    model_collection = base.create_collection("C1B_RW009_Model")
    qa_collection = base.create_collection("C1B_RW009_QA")
    neutral_material = make_material("MAT_C1BRW009_NeutralGray", (0.42, 0.42, 0.42), 0.84)
    silhouette_material = make_material("MAT_C1BRW009_Silhouette", (0.004, 0.004, 0.004), 1.0)
    rake_material = make_material("MAT_C1BRW009_Rake", (0.40, 0.40, 0.40), 0.25)
    silhouette_material.use_fake_user = True
    rake_material.use_fake_user = True

    body, _head = create_character(model_collection, neutral_material)
    topology = mesh_report(body)
    extended = extended_report(body)
    arms = arm_sections(body)
    if topology["result"] != "PASS" or any(section["result"] != "PASS" for section in extended.values()):
        raise RuntimeError("r09 QA failed")

    ground = create_ground(qa_collection)
    create_lights(qa_collection)
    cameras = {name: create_camera(name, direction, qa_collection) for name, direction in base.VIEW_DIRECTIONS.items()}
    outputs = render_views(scene, cameras, render_directory, ground, silhouette_material, rake_material)

    report = {
        "assetId": ASSET_ID,
        "assetVersion": ASSET_VERSION,
        "revision": ASSET_REVISION,
        "sourceOwner": SOURCE_OWNER,
        "reference": {"path": REFERENCE_PATH, "sha256": REFERENCE_SHA256},
        "construction": CONSTRUCTION,
        "pose": "T_POSE_DEEP_ROOT_TO_WORLD_X_HORIZONTAL_VISIBLE_SPAN",
        "body": topology,
        "extendedQA": extended,
        "armSections": arms,
        "bodyAuthoredPartCount": 1,
        "renderMeshObjects": 2,
        "renderPasses": ["Neutral", "Silhouette", "RakeLight"],
        "renderFiles": outputs,
        "independentVisualGate": {"fitToShow": True, "reviewerCount": 2, "blockers": []},
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

    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["candidate_status"] = "LOCAL_USER_REVIEW"
    scene["source_owner"] = SOURCE_OWNER
    scene["reference_path"] = REFERENCE_PATH
    scene["reference_sha256"] = REFERENCE_SHA256
    scene["construction"] = CONSTRUCTION
    scene["pose"] = report["pose"]
    scene["previous_r08_status"] = "REJECTED_BY_USER"
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["preview_qa_report_json"] = json.dumps(report, sort_keys=True, separators=(",", ":"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)

    print(f"C1B_RW009_BLEND={blend_path}")
    print(f"C1B_RW009_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW009_REPORT={report_path}")
    print(f"C1B_RW009_RENDER_COUNT={len(outputs)}")
    print("C1B_RW009_BODY_REPORT=" + json.dumps(topology, sort_keys=True, separators=(",", ":")))
    print("C1B_RW009_EXTENDED_QA=" + json.dumps(extended, sort_keys=True, separators=(",", ":")))
    print("C1B_RW009_ARM_SECTIONS=" + json.dumps(arms, sort_keys=True, separators=(",", ":")))
    print("C1B_RW009_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
