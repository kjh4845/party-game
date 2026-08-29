#!/usr/bin/env python3

import hashlib
import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Matrix, Vector


ASSET_ID = "CHR_MasterCharacter_C1B_PoseLineup"
ASSET_VERSION = "0.2.0-start"
PROFILE_ID = "CharacterProportionProfile-C1B-002-r01"
PROFILE_REVISION = "r01"
MEASUREMENT_SET_SHA256 = "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722"
SOURCE_OWNER = "kjh4845"
BASE_SOURCE_SHA256 = "b0f4e10e208e60dd07bd91947ef46f09135f602b2ce695becff355cc662837cc"
RENDER_RESOLUTION = 2048

BASE_PARTS = {
    "Head": "CHR_C1B003_Head",
    "Torso": "CHR_C1B003_Torso",
    "Arm_L": "CHR_C1B003_Arm_L",
    "Arm_R": "CHR_C1B003_Arm_R",
    "Leg_L": "CHR_C1B003_Leg_L",
    "Leg_R": "CHR_C1B003_Leg_R",
}

# These are review-only transform pivots at the center of each open proximal ring.
# Keeping the ring fixed inside the torso prevents the linked C1B-003 mesh boundary
# from becoming visible. They are not gameplay Shoulder/Hip anchors or rig values.
ARM_REVIEW_PIVOT = {
    "L": Vector((-0.160, 0.0, 0.715)),
    "R": Vector((0.160, 0.0, 0.715)),
}
LEG_REVIEW_PIVOT = {
    "L": Vector((-0.080, 0.0, 0.345)),
    "R": Vector((0.080, 0.0, 0.345)),
}

POSE_IDS = (
    "Neutral",
    "BothHandsGrab",
    "StrikeReady_L",
    "StrikeReady_R",
    "AirKick_L",
    "AirKick_R",
    "Dropkick",
    "AirHandReach",
)
LINEUP_IDS = ("Lineup_Overlap", "Lineup_Spread")


def parse_args():
    arguments = sys.argv
    if "--" not in arguments:
        raise RuntimeError("expected -- <blend-output> <render-directory>")
    custom = arguments[arguments.index("--") + 1 :]
    if len(custom) != 2:
        raise RuntimeError("expected blend output and render directory")
    return os.path.abspath(custom[0]), os.path.abspath(custom[1])


def repository_root():
    return os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def create_collection(name, parent=None):
    collection = bpy.data.collections.new(name)
    if parent is None:
        bpy.context.scene.collection.children.link(collection)
    else:
        parent.children.link(collection)
    return collection


def around_pivot(pivot, axis, degrees):
    return (
        Matrix.Translation(pivot)
        @ Matrix.Rotation(math.radians(degrees), 4, axis)
        @ Matrix.Translation(-pivot)
    )


def arm_review_transform(side, degrees):
    inward = 0.055 if side == "L" else -0.055
    return Matrix.Translation(Vector((inward, 0.055, -0.035))) @ around_pivot(
        ARM_REVIEW_PIVOT[side], "X", degrees
    )


def leg_review_transform(side, degrees):
    inward = 0.04 if side == "L" else -0.04
    return Matrix.Translation(Vector((inward, 0.03, 0.02))) @ around_pivot(
        LEG_REVIEW_PIVOT[side], "X", degrees
    )


def create_pose_cap_meshes():
    result = {}
    for part in ("Arm_L", "Arm_R", "Leg_L", "Leg_R"):
        base_object = bpy.data.objects[BASE_PARTS[part]]
        mesh = base_object.data.copy()
        mesh.name = f"C1B004_{part}_BasePlusProximalCap_Mesh"
        bm = bmesh.new()
        bm.from_mesh(mesh)
        maximum_z = max(vertex.co.z for vertex in bm.verts)
        boundary_edges = [
            edge
            for edge in bm.edges
            if len(edge.link_faces) == 1
            and all(abs(vertex.co.z - maximum_z) <= 0.000001 for vertex in edge.verts)
        ]
        if len(boundary_edges) != 24:
            bm.free()
            raise RuntimeError(f"expected 24 proximal boundary edges for {part}")
        bmesh.ops.holes_fill(bm, edges=boundary_edges, sides=0)
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
        bm.to_mesh(mesh)
        bm.free()
        mesh.update()
        if len(mesh.vertices) != len(base_object.data.vertices):
            raise RuntimeError(f"proximal cap unexpectedly changed vertex count for {part}")
        if len(mesh.polygons) != len(base_object.data.polygons) + 1:
            raise RuntimeError(f"proximal cap must add exactly one polygon for {part}")
        mesh["c1b004_geometry_mode"] = "BASE_PLUS_PROXIMAL_CAP"
        mesh["c1b004_derived_from_mesh_datablock"] = base_object.data.name
        mesh["c1b004_added_proximal_cap_polygons"] = 1
        result[part] = mesh
    return result


def pose_definition(pose_id):
    identity = Matrix.Identity(4)
    transforms = {part: identity.copy() for part in BASE_PARTS}
    root_location = Vector((0.0, 0.0, 0.0))
    root_rotation_x = 0.0

    if pose_id == "BothHandsGrab":
        transforms["Arm_L"] = arm_review_transform("L", -70.0)
        transforms["Arm_R"] = arm_review_transform("R", -70.0)
    elif pose_id == "StrikeReady_L":
        transforms["Arm_L"] = arm_review_transform("L", 58.0)
        transforms["Arm_R"] = arm_review_transform("R", -18.0)
    elif pose_id == "StrikeReady_R":
        transforms["Arm_L"] = arm_review_transform("L", -18.0)
        transforms["Arm_R"] = arm_review_transform("R", 58.0)
    elif pose_id == "AirKick_L":
        transforms["Leg_L"] = leg_review_transform("L", -74.0)
        transforms["Arm_L"] = arm_review_transform("L", 22.0)
        transforms["Arm_R"] = arm_review_transform("R", -18.0)
        root_location.z = 0.10
    elif pose_id == "AirKick_R":
        transforms["Leg_R"] = leg_review_transform("R", -74.0)
        transforms["Arm_L"] = arm_review_transform("L", -18.0)
        transforms["Arm_R"] = arm_review_transform("R", 22.0)
        root_location.z = 0.10
    elif pose_id == "Dropkick":
        transforms["Leg_L"] = leg_review_transform("L", -72.0)
        transforms["Leg_R"] = leg_review_transform("R", -72.0)
        transforms["Arm_L"] = arm_review_transform("L", 38.0)
        transforms["Arm_R"] = arm_review_transform("R", 38.0)
        root_location.z = 0.22
        root_rotation_x = -14.0
    elif pose_id == "AirHandReach":
        transforms["Arm_L"] = arm_review_transform("L", -118.0)
        transforms["Arm_R"] = arm_review_transform("R", -118.0)
        transforms["Leg_L"] = leg_review_transform("L", 18.0)
        transforms["Leg_R"] = leg_review_transform("R", 18.0)
        root_location.z = 0.12
    elif pose_id != "Neutral":
        raise RuntimeError(f"unknown pose id: {pose_id}")

    return transforms, root_location, root_rotation_x


def new_review_root(name, collection, scenario_kind, scenario_id, instance_index=0):
    root = bpy.data.objects.new(name, None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.08
    root["c1b004_review_only"] = True
    root["c1b004_not_gameplay_root"] = True
    root["c1b004_scenario_kind"] = scenario_kind
    root["c1b004_scenario_id"] = scenario_id
    root["c1b004_instance_index"] = instance_index
    root["profile_id"] = PROFILE_ID
    root["profile_revision"] = PROFILE_REVISION
    root["root_motion_authored"] = False
    root["physics_or_hit_semantics_authored"] = False
    return root


def link_character_instance(
    collection,
    root,
    scenario_id,
    instance_index,
    transforms=None,
    pose_cap_meshes=None,
):
    transforms = transforms or {part: Matrix.Identity(4) for part in BASE_PARTS}
    objects = []
    for part, base_name in BASE_PARTS.items():
        base_object = bpy.data.objects[base_name]
        use_pose_cap = pose_cap_meshes is not None and part in pose_cap_meshes
        mesh_data = pose_cap_meshes[part] if use_pose_cap else base_object.data
        duplicate = bpy.data.objects.new(
            f"CHR_C1B004_{scenario_id}_{instance_index:02d}_{part}", mesh_data
        )
        collection.objects.link(duplicate)
        duplicate.parent = root
        duplicate.matrix_local = transforms[part]
        duplicate["c1b004_review_only"] = True
        duplicate["c1b004_scenario_id"] = scenario_id
        duplicate["c1b004_instance_index"] = instance_index
        duplicate["c1b004_body_part"] = part
        duplicate["c1b004_base_object"] = base_name
        duplicate["c1b004_base_mesh_datablock"] = base_object.data.name
        duplicate["c1b004_geometry_mode"] = (
            "BASE_PLUS_PROXIMAL_CAP" if use_pose_cap else "BASE_LINKED"
        )
        duplicate["c1b004_linked_geometry"] = duplicate.data is base_object.data
        duplicate["c1b004_added_proximal_cap_polygons"] = 1 if use_pose_cap else 0
        duplicate["profile_id"] = PROFILE_ID
        duplicate["profile_revision"] = PROFILE_REVISION
        objects.append(duplicate)
    return objects


def create_pose_bundle(parent_collection, pose_cap_meshes):
    scenarios = {}
    for pose_id in POSE_IDS:
        collection = create_collection(f"C1B004_Pose_{pose_id}", parent_collection)
        collection["c1b004_scenario_kind"] = "STATIC_POSE"
        collection["c1b004_scenario_id"] = pose_id
        collection["state"] = "START"
        collection["candidate_status"] = "POSE_REVIEW_CANDIDATE"
        root = new_review_root(
            f"CHR_C1B004_Root_{pose_id}", collection, "STATIC_POSE", pose_id
        )
        transforms, location, rotation_x = pose_definition(pose_id)
        root.location = location
        root.rotation_euler.x = math.radians(rotation_x)
        root["c1b004_display_offset_blender"] = list(location)
        root["c1b004_display_rotation_degrees"] = [rotation_x, 0.0, 0.0]
        root["c1b004_candidate_transform_only"] = True
        link_character_instance(
            collection,
            root,
            pose_id,
            0,
            transforms,
            pose_cap_meshes=None if pose_id == "Neutral" else pose_cap_meshes,
        )
        scenarios[pose_id] = collection
    return scenarios


def create_lineup_bundle(parent_collection):
    layouts = {
        "Lineup_Overlap": [-0.36, -0.12, 0.12, 0.36],
        "Lineup_Spread": [-0.90, -0.30, 0.30, 0.90],
    }
    scenarios = {}
    for lineup_id, offsets in layouts.items():
        collection = create_collection(f"C1B004_{lineup_id}", parent_collection)
        collection["c1b004_scenario_kind"] = "FOUR_PLAYER_LINEUP"
        collection["c1b004_scenario_id"] = lineup_id
        collection["c1b004_participant_count"] = 4
        collection["c1b004_center_offsets_h_json"] = json.dumps(offsets, separators=(",", ":"))
        collection["state"] = "START"
        collection["candidate_status"] = "LINEUP_REVIEW_CANDIDATE"
        for index, offset_x in enumerate(offsets, start=1):
            root = new_review_root(
                f"CHR_C1B004_Root_{lineup_id}_{index:02d}",
                collection,
                "FOUR_PLAYER_LINEUP",
                lineup_id,
                index,
            )
            root.location.x = offset_x
            root["c1b004_display_offset_blender"] = [offset_x, 0.0, 0.0]
            root["c1b004_candidate_transform_only"] = True
            link_character_instance(collection, root, lineup_id, index)
        scenarios[lineup_id] = collection
    return scenarios


def create_camera(name, look_direction, target, ortho_scale, qa_collection):
    camera_data = bpy.data.cameras.new(f"{name}_Data")
    camera = bpy.data.objects.new(name, camera_data)
    qa_collection.objects.link(camera)
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = ortho_scale
    camera.location = target - look_direction.normalized() * 4.0
    camera.rotation_euler = look_direction.to_track_quat("-Z", "Y").to_euler()
    camera["c1b004_review_only"] = True
    camera["look_direction_blender"] = list(look_direction)
    camera["target_blender"] = list(target)
    camera["ortho_scale"] = ortho_scale
    return camera


def configure_render(scene):
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = RENDER_RESOLUTION
    scene.render.resolution_y = RENDER_RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 50
    scene.render.film_transparent = False
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0


def set_world(scene, color, strength):
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def build_render_jobs():
    view_by_scenario = {
        "Neutral": ("Front", "CAM_C1B004_Pose_Front"),
        "BothHandsGrab": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter"),
        "StrikeReady_L": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter_Mirror"),
        "StrikeReady_R": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter"),
        "AirKick_L": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter"),
        "AirKick_R": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter"),
        "Dropkick": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter"),
        "AirHandReach": ("ThreeQuarter", "CAM_C1B004_Pose_ThreeQuarter"),
        "Lineup_Overlap": ("Front", "CAM_C1B004_Lineup_Overlap_Front"),
        "Lineup_Spread": ("Front", "CAM_C1B004_Lineup_Spread_Front"),
    }
    jobs = []
    for scenario_id in (*POSE_IDS, *LINEUP_IDS):
        view, camera = view_by_scenario[scenario_id]
        for style in ("Neutral", "Silhouette"):
            filename = f"{ASSET_ID}_r02_{scenario_id}_{style}_{view}.png"
            jobs.append(
                {
                    "scenarioId": scenario_id,
                    "style": style,
                    "view": view,
                    "camera": camera,
                    "filename": filename,
                }
            )
    return jobs


def set_scenario_visibility(scenarios, active_id):
    for scenario_id, collection in scenarios.items():
        collection.hide_render = scenario_id != active_id


def render_jobs(scene, scenarios, jobs, render_directory):
    os.makedirs(render_directory, exist_ok=True)
    view_layer = scene.view_layers[0]
    ground = bpy.data.objects["QA_Ground"]
    silhouette_material = bpy.data.materials["MAT_C1B003_Silhouette"]
    outputs = []
    for job in jobs:
        set_scenario_visibility(scenarios, job["scenarioId"])
        scene.camera = bpy.data.objects[job["camera"]]
        if job["style"] == "Neutral":
            view_layer.material_override = None
            ground.hide_render = False
            set_world(scene, (0.18, 0.18, 0.18), 1.05)
        else:
            view_layer.material_override = silhouette_material
            ground.hide_render = True
            set_world(scene, (0.75, 0.75, 0.75), 0.8)
        scene.render.filepath = os.path.join(render_directory, job["filename"])
        bpy.ops.render.render(write_still=True)
        outputs.append(job["filename"])
    view_layer.material_override = None
    ground.hide_render = False
    set_world(scene, (0.18, 0.18, 0.18), 1.05)
    set_scenario_visibility(scenarios, "Neutral")
    scene.camera = bpy.data.objects["CAM_C1B004_Pose_Front"]
    return outputs


def main():
    output_blend, render_directory = parse_args()
    base_source = os.path.join(
        repository_root(),
        "BlenderSource",
        "Characters",
        "C1B-003",
        "CHR_MasterCharacter_C1B_Blockout_r01.blend",
    )
    if sha256_file(base_source) != BASE_SOURCE_SHA256:
        raise RuntimeError("C1B-003 base source hash does not match the immutable baseline")

    bpy.ops.wm.open_mainfile(filepath=base_source)
    scene = bpy.context.scene
    scene.name = "C1B004_PoseLineup"
    configure_render(scene)

    for collection_name in ("C1B003_Blockout", "C1B003_Landmarks"):
        bpy.data.collections[collection_name].hide_render = True

    pose_parent = create_collection("C1B004_PoseBundle")
    lineup_parent = create_collection("C1B004_LineupBundle")
    qa_collection = create_collection("C1B004_QA")
    pose_cap_meshes = create_pose_cap_meshes()
    pose_scenarios = create_pose_bundle(pose_parent, pose_cap_meshes)
    lineup_scenarios = create_lineup_bundle(lineup_parent)
    scenarios = {**pose_scenarios, **lineup_scenarios}

    front = Vector((0.0, 1.0, 0.0))
    three_quarter = Vector((-math.sqrt(0.5), math.sqrt(0.5), 0.0))
    create_camera(
        "CAM_C1B004_Pose_Front", front, Vector((0.0, -0.01, 0.58)), 1.42, qa_collection
    )
    create_camera(
        "CAM_C1B004_Pose_ThreeQuarter",
        three_quarter,
        Vector((0.0, -0.01, 0.58)),
        1.42,
        qa_collection,
    )
    create_camera(
        "CAM_C1B004_Pose_ThreeQuarter_Mirror",
        Vector((math.sqrt(0.5), math.sqrt(0.5), 0.0)),
        Vector((0.0, -0.01, 0.58)),
        1.42,
        qa_collection,
    )
    create_camera(
        "CAM_C1B004_Lineup_Overlap_Front",
        front,
        Vector((0.0, -0.01, 0.50)),
        1.55,
        qa_collection,
    )
    create_camera(
        "CAM_C1B004_Lineup_Spread_Front",
        front,
        Vector((0.0, -0.01, 0.50)),
        2.55,
        qa_collection,
    )

    jobs = build_render_jobs()
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["owner_task"] = "C1B-004"
    scene["source_owner"] = SOURCE_OWNER
    scene["profile_id"] = PROFILE_ID
    scene["profile_revision"] = PROFILE_REVISION
    scene["measurement_set_sha256"] = MEASUREMENT_SET_SHA256
    scene["state"] = "START"
    scene["candidate_status"] = "POSE_LINEUP_CANDIDATE"
    scene["derived_from_task"] = "C1B-003"
    scene["derived_from_source_sha256"] = BASE_SOURCE_SHA256
    scene["same_geometry_lineage"] = True
    scene["pose_ids_json"] = json.dumps(POSE_IDS, separators=(",", ":"))
    scene["lineup_ids_json"] = json.dumps(LINEUP_IDS, separators=(",", ":"))
    scene["render_jobs_json"] = json.dumps(jobs, sort_keys=True, separators=(",", ":"))
    scene["user_visual_approval_recorded"] = False
    scene["locked_value_count"] = 0
    scene["gameplay_rig_authored"] = False
    scene["gameplay_collider_authored"] = False
    scene["gameplay_anchor_authored"] = False
    scene["root_motion_authored"] = False
    scene["physics_or_hit_semantics_authored"] = False
    scene["fbx_export_executed"] = False
    scene["unity_import_executed"] = False
    scene["pose_review_geometry_mode"] = "C1B003_BASE_PLUS_INTERNAL_PROXIMAL_CAP"
    scene["proximal_cap_mesh_count"] = 4
    scene["added_polygons_per_proximal_cap_mesh"] = 1
    scene["production_topology_approved"] = False

    os.makedirs(os.path.dirname(output_blend), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    bpy.context.preferences.filepaths.save_version = 0
    set_scenario_visibility(scenarios, "Neutral")
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    outputs = render_jobs(scene, scenarios, jobs, render_directory)
    scene["reference_render_files_json"] = json.dumps(outputs, separators=(",", ":"))
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)

    backup_candidates = [output_blend + str(index) for index in range(1, 10)]
    existing_backups = [path for path in backup_candidates if os.path.exists(path)]
    if existing_backups:
        raise RuntimeError(f"unexpected Blender backup files: {existing_backups}")
    print(f"C1B004_BLEND={output_blend}")
    print(f"C1B004_RENDER_DIRECTORY={render_directory}")
    print(f"C1B004_POSE_COUNT={len(POSE_IDS)}")
    print(f"C1B004_LINEUP_COUNT={len(LINEUP_IDS)}")
    print(f"C1B004_RENDER_COUNT={len(outputs)}")
    print("C1B004_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
