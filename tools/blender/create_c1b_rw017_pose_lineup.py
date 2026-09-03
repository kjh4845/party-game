#!/usr/bin/env python3

"""Create r16-derived static Pose8 and all-Neutral four-player lineups."""

import hashlib
import importlib.util
import json
import math
import os
import sys

import bmesh
import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-016-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r16.blend",
)
SOURCE_SHA256 = "9b80276e97aa84f3d2a4ef7689b4ebd241f84124494ec8ff0ca51cc119a676ef"
SOURCE_BODY = "C1B_R16_FullBodyCrotchFair7mm_TPoseBody_NoHands"
SOURCE_HEAD = "C1B_R16_RoundFacelessHead"
APPROVAL_RECORD = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-016-preview",
    "NeutralApprovalRecord.json",
)
APPROVAL_RECORD_SHA256 = "1f89295748cd2e5be9bf9d8625f963d602a035a3a3b43e48e05d7f96a0eb9565"

ASSET_ID = "CHR_MasterCharacter_C1B_PoseLineup"
REVISION = "r17"
VERSION = "0.17.0-local-preview"
OWNER_TASK = "C1BRW-004"
CONSTRUCTION = "R16_TEMP_REVIEW_RIG_PRESERVE_VOLUME_TO_STATIC_BAKED_POSES"

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
LINEUP_OFFSETS = {
    "Lineup_Overlap": (-0.36, -0.12, 0.12, 0.36),
    "Lineup_Spread": (-0.90, -0.30, 0.30, 0.90),
}
RENDER_RESOLUTION = 2048

ARM_PIVOT = {
    "L": Vector((-0.180, 0.0, 0.620)),
    "R": Vector((0.180, 0.0, 0.620)),
}
LEG_PIVOT = {
    "L": Vector((-0.090, 0.0, 0.220)),
    "R": Vector((0.090, 0.0, 0.220)),
}

ARM_WEIGHT_X_START = 0.120
ARM_WEIGHT_X_FULL = 0.320
ARM_WEIGHT_Z_START = 0.500
ARM_WEIGHT_Z_FULL = 0.560
LEG_WEIGHT_Z_FULL = 0.055
LEG_WEIGHT_Z_END = 0.145
LEG_CENTER_GATE_Z_START = 0.080
LEG_CENTER_GATE_Z_END = 0.130
LEG_CENTER_GATE_X_FULL = 0.035
JOINT_SMOOTH_ITERATIONS = 6
JOINT_SMOOTH_FACTOR = 0.08
TERMINAL_BAND = 0.035
READABILITY_MINIMUM = 0.100
MIRROR_TOLERANCE = 0.00001
MAXIMUM_ADJACENT_ANGLE = 30.0


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r16 = import_file("c1b_rw016", "create_c1b_rw016_body_crotch_fair.py")
r12 = r16.r12
qa = r16.qa


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <blend> <render-dir> <report>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 3:
        raise RuntimeError("expected -- <blend> <render-dir> <report>")
    return tuple(os.path.abspath(value) for value in values)


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def smootherstep(values):
    values = np.clip(values, 0.0, 1.0)
    return values**3 * (values * (values * 6.0 - 15.0) + 10.0)


def create_collection(name, parent=None):
    collection = bpy.data.collections.new(name)
    if parent is None:
        bpy.context.scene.collection.children.link(collection)
    else:
        parent.children.link(collection)
    return collection


def limb_transform(pivot, axis, degrees, translation):
    return {
        "pivot": Vector(pivot),
        "rotation": Quaternion(Vector(axis), math.radians(degrees)),
        "translation": Vector(translation),
    }


def identity_limb_transform(pivot):
    return limb_transform(pivot, (1.0, 0.0, 0.0), 0.0, (0.0, 0.0, 0.0))


def arm_transform(side, degrees):
    horizontal_degrees = -degrees if side == "L" else degrees
    return limb_transform(
        ARM_PIVOT[side],
        (0.0, 0.0, 1.0),
        horizontal_degrees,
        (0.0, 0.0, 0.0),
    )


def arm_reach_transform(side):
    if side == "L":
        rotation = (
            Matrix.Rotation(math.radians(70.0), 4, "Z")
            @ Matrix.Rotation(math.radians(48.0), 4, "Y")
        ).to_quaternion()
    else:
        rotation = (
            Matrix.Rotation(math.radians(-70.0), 4, "Z")
            @ Matrix.Rotation(math.radians(-48.0), 4, "Y")
        ).to_quaternion()
    return {
        "pivot": Vector(ARM_PIVOT[side]),
        "rotation": rotation,
        "translation": Vector((0.0, 0.0, 0.0)),
    }


def leg_transform(side, degrees):
    return limb_transform(
        LEG_PIVOT[side],
        (1.0, 0.0, 0.0),
        degrees,
        (0.0, 0.0, 0.0),
    )


def pose_definition(pose_id):
    transforms = {
        "Arm_L": identity_limb_transform(ARM_PIVOT["L"]),
        "Arm_R": identity_limb_transform(ARM_PIVOT["R"]),
        "Leg_L": identity_limb_transform(LEG_PIVOT["L"]),
        "Leg_R": identity_limb_transform(LEG_PIVOT["R"]),
    }
    root_location = Vector((0.0, 0.0, 0.0))
    root_rotation_x = 0.0
    if pose_id == "BothHandsGrab":
        transforms["Arm_L"] = arm_transform("L", -70.0)
        transforms["Arm_R"] = arm_transform("R", -70.0)
    elif pose_id == "StrikeReady_L":
        transforms["Arm_L"] = arm_transform("L", 58.0)
        transforms["Arm_R"] = arm_transform("R", -18.0)
    elif pose_id == "StrikeReady_R":
        transforms["Arm_L"] = arm_transform("L", -18.0)
        transforms["Arm_R"] = arm_transform("R", 58.0)
    elif pose_id == "AirKick_L":
        transforms["Leg_L"] = leg_transform("L", -74.0)
        transforms["Arm_L"] = arm_transform("L", 22.0)
        transforms["Arm_R"] = arm_transform("R", -18.0)
        root_location.z = 0.10
    elif pose_id == "AirKick_R":
        transforms["Leg_R"] = leg_transform("R", -74.0)
        transforms["Arm_L"] = arm_transform("L", -18.0)
        transforms["Arm_R"] = arm_transform("R", 22.0)
        root_location.z = 0.10
    elif pose_id == "Dropkick":
        transforms["Leg_L"] = leg_transform("L", -72.0)
        transforms["Leg_R"] = leg_transform("R", -72.0)
        transforms["Arm_L"] = arm_transform("L", 38.0)
        transforms["Arm_R"] = arm_transform("R", 38.0)
        root_location.z = 0.22
        root_rotation_x = -14.0
    elif pose_id == "AirHandReach":
        transforms["Arm_L"] = arm_reach_transform("L")
        transforms["Arm_R"] = arm_reach_transform("R")
        transforms["Leg_L"] = leg_transform("L", 18.0)
        transforms["Leg_R"] = leg_transform("R", 18.0)
        root_location.z = 0.12
    elif pose_id != "Neutral":
        raise RuntimeError(f"unknown pose id: {pose_id}")
    return transforms, root_location, root_rotation_x


def build_skin_weights(coordinates):
    x = coordinates[:, 0]
    z = coordinates[:, 2]
    absolute_x = np.abs(x)
    arm_height = smootherstep(
        (z - ARM_WEIGHT_Z_START) / (ARM_WEIGHT_Z_FULL - ARM_WEIGHT_Z_START)
    )
    arm_support = smootherstep(
        (absolute_x - ARM_WEIGHT_X_START)
        / (ARM_WEIGHT_X_FULL - ARM_WEIGHT_X_START)
    ) * arm_height
    arm_l = arm_support * (x < 0.0)
    arm_r = arm_support * (x > 0.0)
    leg_vertical = 1.0 - smootherstep(
        (z - LEG_WEIGHT_Z_FULL) / (LEG_WEIGHT_Z_END - LEG_WEIGHT_Z_FULL)
    )
    center_mix = smootherstep(
        (z - LEG_CENTER_GATE_Z_START)
        / (LEG_CENTER_GATE_Z_END - LEG_CENTER_GATE_Z_START)
    )
    center_gate = (1.0 - center_mix) + center_mix * smootherstep(
        absolute_x / LEG_CENTER_GATE_X_FULL
    )
    leg_support = leg_vertical * center_gate
    leg_l = leg_support * (x < 0.0)
    leg_r = leg_support * (x > 0.0)
    limb_total = arm_l + arm_r + leg_l + leg_r
    if float(limb_total.max()) > 1.0 + 1.0e-12:
        raise RuntimeError("deterministic limb weights overlap")
    body = 1.0 - (arm_l + arm_r + leg_l + leg_r)
    return {
        "Body": body,
        "Arm_L": arm_l,
        "Arm_R": arm_r,
        "Leg_L": leg_l,
        "Leg_R": leg_r,
    }


def build_joint_smooth_weight(weights):
    transition_weights = [
        4.0 * weights[name] * (1.0 - weights[name])
        for name in ("Arm_L", "Arm_R", "Leg_L", "Leg_R")
    ]
    return np.maximum.reduce(transition_weights)


def transform_coordinates(coordinates, matrix):
    homogeneous = np.column_stack((coordinates, np.ones(len(coordinates))))
    return (homogeneous @ np.array(matrix, dtype=np.float64).T)[:, :3]


def transform_coordinates_preserve_volume(coordinates, weights, transform):
    rotation = transform["rotation"].normalized()
    angle = float(rotation.angle)
    axis = np.asarray(tuple(rotation.axis), dtype=np.float64)
    pivot = np.asarray(tuple(transform["pivot"]), dtype=np.float64)
    translation = np.asarray(tuple(transform["translation"]), dtype=np.float64)
    if abs(angle) <= 1.0e-12 and float(np.linalg.norm(translation)) <= 1.0e-12:
        return coordinates.copy()
    relative = coordinates - pivot[None, :]
    weighted_angles = weights * angle
    cosine = np.cos(weighted_angles)[:, None]
    sine = np.sin(weighted_angles)[:, None]
    axis_rows = np.broadcast_to(axis[None, :], relative.shape)
    axis_projection = (relative @ axis)[:, None]
    rotated = (
        relative * cosine
        + np.cross(axis_rows, relative) * sine
        + axis_rows * axis_projection * (1.0 - cosine)
    )
    return (
        pivot[None, :]
        + rotated
        + weights[:, None] * translation[None, :]
    )


def transform_is_active(transform):
    return (
        abs(float(transform["rotation"].angle)) > 1.0e-12
        or float(transform["translation"].length) > 1.0e-12
    )


def bake_pose_coordinates(
    source,
    edges,
    degree,
    weights,
    joint_smooth_weight,
    transforms,
):
    del joint_smooth_weight
    result = source.copy()
    active_transition_weights = []
    for name in ("Arm_L", "Arm_R", "Leg_L", "Leg_R"):
        if not transform_is_active(transforms[name]):
            continue
        transformed = transform_coordinates_preserve_volume(
            source, weights[name], transforms[name]
        )
        result += transformed - source
        active_transition_weights.append(
            4.0 * weights[name] * (1.0 - weights[name])
        )
    if not active_transition_weights:
        return result
    active_joint_smooth_weight = np.maximum.reduce(active_transition_weights)
    posed_base = result.copy()
    pinned = active_joint_smooth_weight <= 1.0e-6
    for _ in range(JOINT_SMOOTH_ITERATIONS):
        average = r12.neighbor_average(result, edges, degree)
        result += (
            JOINT_SMOOTH_FACTOR
            * active_joint_smooth_weight[:, None]
            * (average - result)
        )
        result[pinned] = posed_base[pinned]
    return result


def create_temporary_review_rig(collection):
    armature_data = bpy.data.armatures.new("TMP_C1BRW017_ReviewRig_Data")
    armature = bpy.data.objects.new("TMP_C1BRW017_ReviewRig", armature_data)
    collection.objects.link(armature)
    bpy.context.view_layer.objects.active = armature
    armature.select_set(True)
    bpy.ops.object.mode_set(mode="EDIT")
    body = armature_data.edit_bones.new("Body")
    body.head = (0.0, 0.0, 0.30)
    body.tail = (0.0, 0.0, 0.55)
    for name, pivot in (
        ("Arm_L", ARM_PIVOT["L"]),
        ("Arm_R", ARM_PIVOT["R"]),
        ("Leg_L", LEG_PIVOT["L"]),
        ("Leg_R", LEG_PIVOT["R"]),
    ):
        bone = armature_data.edit_bones.new(name)
        bone.head = pivot
        if name == "Arm_L":
            bone.tail = pivot + Vector((-0.10, 0.0, 0.0))
        elif name == "Arm_R":
            bone.tail = pivot + Vector((0.10, 0.0, 0.0))
        else:
            bone.tail = pivot + Vector((0.0, 0.0, -0.10))
        bone.parent = body
        bone.use_connect = False
    bpy.ops.object.mode_set(mode="OBJECT")
    armature["review_only"] = True
    armature["temporary_helper"] = True
    armature["not_gameplay_rig"] = True
    return armature


def destroy_temporary_review_rig(armature, collection):
    armature_data = armature.data
    bpy.data.objects.remove(armature, do_unlink=True)
    bpy.data.armatures.remove(armature_data)
    bpy.data.collections.remove(collection)


def create_root(name, collection, scenario_id, kind, instance_index=0):
    root = bpy.data.objects.new(name, None)
    collection.objects.link(root)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.08
    root["review_only"] = True
    root["not_gameplay_root"] = True
    root["scenario_id"] = scenario_id
    root["scenario_kind"] = kind
    root["instance_index"] = instance_index
    return root


def link_head(source_head, collection, root, scenario_id, instance_index):
    head = bpy.data.objects.new(
        f"CHR_C1BRW017_{scenario_id}_{instance_index:02d}_Head",
        source_head.data,
    )
    collection.objects.link(head)
    head.parent = root
    head.matrix_local = source_head.matrix_world.copy()
    head["review_only"] = True
    head["scenario_id"] = scenario_id
    head["instance_index"] = instance_index
    head["body_part"] = "Head"
    head["geometry_mode"] = "R16_HEAD_LINKED"
    return head


def link_neutral_body(source_body, collection, root, scenario_id, instance_index):
    body = bpy.data.objects.new(
        f"CHR_C1BRW017_{scenario_id}_{instance_index:02d}_Body",
        source_body.data,
    )
    collection.objects.link(body)
    body.parent = root
    body.matrix_local = Matrix.Identity(4)
    body["review_only"] = True
    body["scenario_id"] = scenario_id
    body["instance_index"] = instance_index
    body["body_part"] = "Body"
    body["geometry_mode"] = "R16_BODY_LINKED"
    return body


def create_baked_body(
    source_body,
    collection,
    root,
    scenario_id,
    coordinates,
):
    mesh = source_body.data.copy()
    mesh.name = f"C1BRW017_{scenario_id}_StaticBakedBodyMesh"
    body = bpy.data.objects.new(
        f"CHR_C1BRW017_{scenario_id}_00_Body",
        mesh,
    )
    collection.objects.link(body)
    r12.set_mesh_coordinates(mesh, coordinates)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    body.parent = root
    body.matrix_local = Matrix.Identity(4)
    body["review_only"] = True
    body["scenario_id"] = scenario_id
    body["instance_index"] = 0
    body["body_part"] = "Body"
    body["geometry_mode"] = "R16_PRESERVE_VOLUME_STATIC_BAKED"
    body["temporary_rig_removed"] = True
    return body


def create_pose_scenarios(
    parent,
    source_body,
    source_head,
    source_coordinates,
    source_edges,
    source_degree,
    weights,
    joint_smooth_weight,
):
    scenarios = {}
    pose_objects = {}
    for pose_id in POSE_IDS:
        collection = create_collection(f"C1BRW017_Pose_{pose_id}", parent)
        collection["scenario_id"] = pose_id
        collection["scenario_kind"] = "STATIC_POSE"
        collection["candidate_status"] = "POSE_REVIEW_CANDIDATE"
        root = create_root(
            f"CHR_C1BRW017_Root_{pose_id}",
            collection,
            pose_id,
            "STATIC_POSE",
        )
        transforms, location, rotation_x = pose_definition(pose_id)
        root.location = location
        root.rotation_euler.x = math.radians(rotation_x)
        root["display_offset"] = list(location)
        root["display_rotation_degrees"] = [rotation_x, 0.0, 0.0]
        if pose_id == "Neutral":
            body = link_neutral_body(source_body, collection, root, pose_id, 0)
        else:
            coordinates = bake_pose_coordinates(
                source_coordinates,
                source_edges,
                source_degree,
                weights,
                joint_smooth_weight,
                transforms,
            )
            body = create_baked_body(
                source_body, collection, root, pose_id, coordinates
            )
        head = link_head(source_head, collection, root, pose_id, 0)
        scenarios[pose_id] = collection
        pose_objects[pose_id] = {"root": root, "body": body, "head": head}
    return scenarios, pose_objects


def create_lineup_scenarios(parent, source_body, source_head):
    scenarios = {}
    lineup_objects = {}
    for lineup_id in LINEUP_IDS:
        collection = create_collection(f"C1BRW017_{lineup_id}", parent)
        collection["scenario_id"] = lineup_id
        collection["scenario_kind"] = "FOUR_PLAYER_LINEUP"
        collection["participant_count"] = 4
        collection["participant_pose"] = "Neutral"
        collection["center_offsets_h_json"] = json.dumps(
            LINEUP_OFFSETS[lineup_id], separators=(",", ":")
        )
        participants = []
        for index, offset in enumerate(LINEUP_OFFSETS[lineup_id], start=1):
            root = create_root(
                f"CHR_C1BRW017_Root_{lineup_id}_{index:02d}",
                collection,
                lineup_id,
                "FOUR_PLAYER_LINEUP",
                index,
            )
            root.location.x = offset
            body = link_neutral_body(
                source_body, collection, root, lineup_id, index
            )
            head = link_head(source_head, collection, root, lineup_id, index)
            participants.append({"root": root, "body": body, "head": head})
        scenarios[lineup_id] = collection
        lineup_objects[lineup_id] = participants
    return scenarios, lineup_objects


def set_scenario_visibility(scenarios, active_id):
    for scenario_id, collection in scenarios.items():
        collection.hide_render = scenario_id != active_id


def scenario_world_points(objects):
    points = []
    for obj in objects:
        if obj.type != "MESH":
            continue
        points.extend(obj.matrix_world @ vertex.co for vertex in obj.data.vertices)
    return points


def create_camera(name, look_direction, collection):
    data = bpy.data.cameras.new(name + "_Data")
    camera = bpy.data.objects.new(name, data)
    collection.objects.link(camera)
    data.type = "ORTHO"
    camera["look_direction"] = list(look_direction)
    return camera


def fit_camera(camera, look_direction, object_groups, margin=1.12):
    points = []
    for objects in object_groups:
        points.extend(scenario_world_points(objects))
    minimum = Vector(tuple(min(point[index] for point in points) for index in range(3)))
    maximum = Vector(tuple(max(point[index] for point in points) for index in range(3)))
    target = (minimum + maximum) * 0.5
    camera.location = target - look_direction.normalized() * 4.0
    camera.rotation_euler = look_direction.to_track_quat("-Z", "Y").to_euler()
    bpy.context.view_layer.update()
    inverse = camera.matrix_world.inverted()
    local = [inverse @ point for point in points]
    width = max(point.x for point in local) - min(point.x for point in local)
    height = max(point.y for point in local) - min(point.y for point in local)
    camera.data.ortho_scale = max(width, height) * margin
    camera["target"] = list(target)
    camera["ortho_scale"] = float(camera.data.ortho_scale)


def configure_cameras(qa_collection, pose_objects, lineup_objects):
    front_direction = Vector((0.0, 1.0, 0.0))
    three_quarter = Vector((-math.sqrt(0.5), math.sqrt(0.5), 0.0))
    mirror_three_quarter = Vector((math.sqrt(0.5), math.sqrt(0.5), 0.0))
    cameras = {
        "Pose_Front": create_camera(
            "CAM_C1BRW017_Pose_Front", front_direction, qa_collection
        ),
        "Pose_ThreeQuarter": create_camera(
            "CAM_C1BRW017_Pose_ThreeQuarter", three_quarter, qa_collection
        ),
        "Pose_ThreeQuarter_Mirror": create_camera(
            "CAM_C1BRW017_Pose_ThreeQuarter_Mirror",
            mirror_three_quarter,
            qa_collection,
        ),
        "Lineup_Overlap": create_camera(
            "CAM_C1BRW017_Lineup_Overlap_Front", front_direction, qa_collection
        ),
        "Lineup_Spread": create_camera(
            "CAM_C1BRW017_Lineup_Spread_Front", front_direction, qa_collection
        ),
    }
    fit_camera(
        cameras["Pose_Front"],
        front_direction,
        [[pose_objects["Neutral"]["body"], pose_objects["Neutral"]["head"]]],
    )
    action_groups = [
        [pose_objects[pose_id]["body"], pose_objects[pose_id]["head"]]
        for pose_id in POSE_IDS
        if pose_id not in ("Neutral", "StrikeReady_L")
    ]
    fit_camera(cameras["Pose_ThreeQuarter"], three_quarter, action_groups)
    fit_camera(
        cameras["Pose_ThreeQuarter_Mirror"],
        mirror_three_quarter,
        [[pose_objects["StrikeReady_L"]["body"], pose_objects["StrikeReady_L"]["head"]]],
    )
    for lineup_id in LINEUP_IDS:
        groups = [
            [participant["body"], participant["head"]]
            for participant in lineup_objects[lineup_id]
        ]
        fit_camera(cameras[lineup_id], front_direction, groups, margin=1.08)
    return cameras


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
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0


def set_world(scene, color, strength):
    background = next(
        node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND"
    )
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def build_render_jobs(cameras):
    view_by_scenario = {
        "Neutral": ("Front", cameras["Pose_Front"].name),
        "BothHandsGrab": ("ThreeQuarter", cameras["Pose_ThreeQuarter"].name),
        "StrikeReady_L": (
            "ThreeQuarter",
            cameras["Pose_ThreeQuarter_Mirror"].name,
        ),
        "StrikeReady_R": ("ThreeQuarter", cameras["Pose_ThreeQuarter"].name),
        "AirKick_L": ("ThreeQuarter", cameras["Pose_ThreeQuarter"].name),
        "AirKick_R": ("ThreeQuarter", cameras["Pose_ThreeQuarter"].name),
        "Dropkick": ("ThreeQuarter", cameras["Pose_ThreeQuarter"].name),
        "AirHandReach": ("ThreeQuarter", cameras["Pose_ThreeQuarter"].name),
        "Lineup_Overlap": ("Front", cameras["Lineup_Overlap"].name),
        "Lineup_Spread": ("Front", cameras["Lineup_Spread"].name),
    }
    jobs = []
    for scenario_id in (*POSE_IDS, *LINEUP_IDS):
        view, camera = view_by_scenario[scenario_id]
        for style in ("Neutral", "Silhouette"):
            jobs.append(
                {
                    "scenarioId": scenario_id,
                    "style": style,
                    "view": view,
                    "camera": camera,
                    "filename": (
                        f"{ASSET_ID}_{REVISION}_{scenario_id}_{style}_{view}.png"
                    ),
                }
            )
    return jobs


def render_jobs(scene, scenarios, jobs, directory):
    os.makedirs(directory, exist_ok=True)
    layer = scene.view_layers[0]
    ground = bpy.data.objects.get("QA_Ground")
    silhouette = bpy.data.materials["MAT_C1BRW009_Silhouette"]
    standard_lights = [
        obj
        for obj in bpy.data.objects
        if obj.type == "LIGHT" and obj.name.startswith("QA_")
    ]
    for light in standard_lights:
        light.hide_render = "Rake" in light.name
    outputs = []
    for job in jobs:
        set_scenario_visibility(scenarios, job["scenarioId"])
        scene.camera = bpy.data.objects[job["camera"]]
        if job["style"] == "Neutral":
            layer.material_override = None
            if ground is not None:
                ground.hide_render = False
            set_world(scene, (0.18, 0.18, 0.18), 1.0)
        else:
            layer.material_override = silhouette
            if ground is not None:
                ground.hide_render = True
            set_world(scene, (0.75, 0.75, 0.75), 0.8)
        scene.render.filepath = os.path.join(directory, job["filename"])
        bpy.ops.render.render(write_still=True)
        outputs.append(job["filename"])
    layer.material_override = None
    if ground is not None:
        ground.hide_render = False
    set_world(scene, (0.18, 0.18, 0.18), 1.0)
    set_scenario_visibility(scenarios, "Neutral")
    scene.camera = bpy.data.objects[jobs[0]["camera"]]
    return outputs


def terminal_index_masks(coordinates):
    minimum_x = float(coordinates[:, 0].min())
    maximum_x = float(coordinates[:, 0].max())
    minimum_z = float(coordinates[:, 2].min())
    return {
        "Arm_L": np.flatnonzero(coordinates[:, 0] <= minimum_x + TERMINAL_BAND),
        "Arm_R": np.flatnonzero(coordinates[:, 0] >= maximum_x - TERMINAL_BAND),
        "Leg_L": np.flatnonzero(
            (coordinates[:, 2] <= minimum_z + TERMINAL_BAND)
            & (coordinates[:, 0] < 0.0)
        ),
        "Leg_R": np.flatnonzero(
            (coordinates[:, 2] <= minimum_z + TERMINAL_BAND)
            & (coordinates[:, 0] > 0.0)
        ),
    }


def terminal_position(obj, indices):
    coordinates = r12.mesh_coordinates(obj.data)[indices]
    centroid = Vector(tuple(coordinates.mean(axis=0)))
    return obj.matrix_world @ centroid


def vector_record(value):
    return [float(component) for component in value]


def mirror_deviation(left, right):
    return float(max(abs(left.x + right.x), abs(left.y - right.y), abs(left.z - right.z)))


def object_bounds(objects):
    points = scenario_world_points(objects)
    minimum = [min(point[index] for point in points) for index in range(3)]
    maximum = [max(point[index] for point in points) for index in range(3)]
    return {
        "minimum": [float(value) for value in minimum],
        "maximum": [float(value) for value in maximum],
        "size": [float(maximum[index] - minimum[index]) for index in range(3)],
    }


def inspect_pose_geometry(pose_objects, terminal_masks):
    records = {}
    for pose_id, objects in pose_objects.items():
        body = objects["body"]
        manifold = qa.manifold(body.data)
        folds = qa.folds(body.data)
        overlap = qa.bvh_self_overlap(body.data)
        folds.pop("foldoverEdgesAt90Degrees", None)
        overlap.pop("nonAdjacentOverlapPairs", None)
        records[pose_id] = {
            "geometryMode": body.get("geometry_mode"),
            "topology": r12.r11.topology(body),
            "manifold": manifold,
            "fold": folds,
            "selfIntersection": overlap,
            "runtimeModifierCount": len(body.modifiers),
            "vertexGroupCount": len(body.vertex_groups),
            "hasShapeKeys": body.data.shape_keys is not None,
            "terminalPositions": {
                name: vector_record(terminal_position(body, indices))
                for name, indices in terminal_masks.items()
            },
            "bounds": object_bounds([body, objects["head"]]),
        }
    return records


def readability_metrics(records):
    terminal = {
        pose_id: {
            name: Vector(values)
            for name, values in record["terminalPositions"].items()
        }
        for pose_id, record in records.items()
    }
    neutral = terminal["Neutral"]
    result = {
        "grabForwardDeltaLeft": neutral["Arm_L"].y
        - terminal["BothHandsGrab"]["Arm_L"].y,
        "grabForwardDeltaRight": neutral["Arm_R"].y
        - terminal["BothHandsGrab"]["Arm_R"].y,
        "strikeReadyBackDeltaLeft": terminal["StrikeReady_L"]["Arm_L"].y
        - neutral["Arm_L"].y,
        "strikeReadyBackDeltaRight": terminal["StrikeReady_R"]["Arm_R"].y
        - neutral["Arm_R"].y,
        "airKickForwardDeltaLeft": neutral["Leg_L"].y
        - terminal["AirKick_L"]["Leg_L"].y,
        "airKickForwardDeltaRight": neutral["Leg_R"].y
        - terminal["AirKick_R"]["Leg_R"].y,
        "dropkickForwardDeltaLeft": neutral["Leg_L"].y
        - terminal["Dropkick"]["Leg_L"].y,
        "dropkickForwardDeltaRight": neutral["Leg_R"].y
        - terminal["Dropkick"]["Leg_R"].y,
        "airReachHeightDeltaLeft": terminal["AirHandReach"]["Arm_L"].z
        - neutral["Arm_L"].z,
        "airReachHeightDeltaRight": terminal["AirHandReach"]["Arm_R"].z
        - neutral["Arm_R"].z,
        "strikeMirrorMaximumDeviation": mirror_deviation(
            terminal["StrikeReady_L"]["Arm_L"],
            terminal["StrikeReady_R"]["Arm_R"],
        ),
        "kickMirrorMaximumDeviation": mirror_deviation(
            terminal["AirKick_L"]["Leg_L"],
            terminal["AirKick_R"]["Leg_R"],
        ),
        "grabMirrorMaximumDeviation": mirror_deviation(
            terminal["BothHandsGrab"]["Arm_L"],
            terminal["BothHandsGrab"]["Arm_R"],
        ),
        "dropkickMirrorMaximumDeviation": mirror_deviation(
            terminal["Dropkick"]["Leg_L"],
            terminal["Dropkick"]["Leg_R"],
        ),
        "airReachMirrorMaximumDeviation": mirror_deviation(
            terminal["AirHandReach"]["Arm_L"],
            terminal["AirHandReach"]["Arm_R"],
        ),
        "positiveDirectionalDeltaMinimum": READABILITY_MINIMUM,
        "mirrorDeviationMaximum": MIRROR_TOLERANCE,
    }
    directional_names = (
        "grabForwardDeltaLeft",
        "grabForwardDeltaRight",
        "strikeReadyBackDeltaLeft",
        "strikeReadyBackDeltaRight",
        "airKickForwardDeltaLeft",
        "airKickForwardDeltaRight",
        "dropkickForwardDeltaLeft",
        "dropkickForwardDeltaRight",
        "airReachHeightDeltaLeft",
        "airReachHeightDeltaRight",
    )
    mirror_names = (
        "strikeMirrorMaximumDeviation",
        "kickMirrorMaximumDeviation",
        "grabMirrorMaximumDeviation",
        "dropkickMirrorMaximumDeviation",
        "airReachMirrorMaximumDeviation",
    )
    result["result"] = (
        "PASS"
        if all(result[name] >= READABILITY_MINIMUM for name in directional_names)
        and all(result[name] <= MIRROR_TOLERANCE for name in mirror_names)
        else "FAIL"
    )
    return result


def final_inventory():
    return {
        "armatureObjects": len(
            [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
        ),
        "armatureDatablocks": len(bpy.data.armatures),
        "actions": len(bpy.data.actions),
        "latticeObjects": len(
            [obj for obj in bpy.data.objects if obj.type == "LATTICE"]
        ),
        "shapeKeyMeshes": len(
            [mesh for mesh in bpy.data.meshes if mesh.shape_keys is not None]
        ),
        "meshModifierCount": sum(
            len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
        ),
        "meshVertexGroupCount": sum(
            len(obj.vertex_groups) for obj in bpy.data.objects if obj.type == "MESH"
        ),
        "meshConstraintCount": sum(
            len(obj.constraints) for obj in bpy.data.objects if obj.type == "MESH"
        ),
        "animatedObjects": len(
            [obj for obj in bpy.data.objects if obj.animation_data is not None]
        ),
        "negativeScaleObjects": len(
            [
                obj
                for obj in bpy.data.objects
                if any(float(component) < 0.0 for component in obj.scale)
            ]
        ),
        "temporaryNamedDatablocks": sum(
            name.startswith("TMP_C1BRW017")
            for name in (
                [obj.name for obj in bpy.data.objects]
                + [data.name for data in bpy.data.armatures]
                + [collection.name for collection in bpy.data.collections]
            )
        ),
        "colliderObjects": len(
            [
                obj
                for obj in bpy.data.objects
                if "collider" in obj.name.lower()
                or obj.get("collider_role") is not None
            ]
        ),
    }


def main():
    blend_path, render_dir, report_path = parse_args()
    for path in (SOURCE_BLEND, APPROVAL_RECORD):
        if not os.path.exists(path):
            raise FileNotFoundError(path)
    if file_sha256(SOURCE_BLEND) != SOURCE_SHA256:
        raise RuntimeError("r16 source hash does not match approved baseline")
    if file_sha256(APPROVAL_RECORD) != APPROVAL_RECORD_SHA256:
        raise RuntimeError("r16 approval record hash does not match")
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)

    source_hash_before = file_sha256(SOURCE_BLEND)
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    scene = bpy.context.scene
    scene.name = "C1BRW017_PoseLineup"
    source_body = bpy.data.objects[SOURCE_BODY]
    source_head = bpy.data.objects[SOURCE_HEAD]
    source_body.hide_render = True
    source_head.hide_render = True
    source_coordinates = r12.mesh_coordinates(source_body.data)
    source_edges = r12.mesh_edges(source_body.data)
    source_degree = np.bincount(
        source_edges.reshape(-1), minlength=len(source_coordinates)
    ).astype(np.float64)
    source_signature = r12.mesh_signature(source_body)
    terminal_masks = terminal_index_masks(source_coordinates)
    weights = build_skin_weights(source_coordinates)
    joint_smooth_weight = build_joint_smooth_weight(weights)
    weight_sums = sum(weights.values())

    pose_parent = create_collection("C1BRW017_PoseBundle")
    lineup_parent = create_collection("C1BRW017_LineupBundle")
    qa_collection = create_collection("C1BRW017_QA")
    temporary_collection = create_collection("TMP_C1BRW017_Helper")
    temporary_rig = create_temporary_review_rig(temporary_collection)

    pose_scenarios, pose_objects = create_pose_scenarios(
        pose_parent,
        source_body,
        source_head,
        source_coordinates,
        source_edges,
        source_degree,
        weights,
        joint_smooth_weight,
    )
    lineup_scenarios, lineup_objects = create_lineup_scenarios(
        lineup_parent, source_body, source_head
    )
    destroy_temporary_review_rig(temporary_rig, temporary_collection)
    scenarios = {**pose_scenarios, **lineup_scenarios}
    cameras = configure_cameras(qa_collection, pose_objects, lineup_objects)
    jobs = build_render_jobs(cameras)
    configure_render(scene)
    bpy.context.view_layer.update()

    pose_records = inspect_pose_geometry(pose_objects, terminal_masks)
    readability = readability_metrics(pose_records)
    lineup_records = {
        lineup_id: {
            "participantCount": len(lineup_objects[lineup_id]),
            "participantPose": "Neutral",
            "centerOffsetsH": list(LINEUP_OFFSETS[lineup_id]),
            "rootScales": [
                list(participant["root"].scale)
                for participant in lineup_objects[lineup_id]
            ],
            "bounds": object_bounds(
                [
                    obj
                    for participant in lineup_objects[lineup_id]
                    for obj in (participant["body"], participant["head"])
                ]
            ),
        }
        for lineup_id in LINEUP_IDS
    }
    inventory = final_inventory()
    outputs = render_jobs(scene, scenarios, jobs, render_dir)
    missing_renders = [
        name for name in outputs if not os.path.isfile(os.path.join(render_dir, name))
    ]
    source_hash_after = file_sha256(SOURCE_BLEND)

    pose_geometry_pass = all(
        record["topology"]["vertices"] == source_signature["vertices"]
        and record["topology"]["edges"] == source_signature["edges"]
        and record["topology"]["faces"] == source_signature["faces"]
        and record["topology"]["allQuads"]
        and record["topology"]["boundaryEdges"] == 0
        and record["topology"]["nonManifoldEdges"] == 0
        and record["topology"]["looseEdges"] == 0
        and record["topology"]["degenerateFaces"] == 0
        and record["manifold"]["result"] == "PASS"
        and record["fold"]["foldoverEdgeCountAt90Degrees"] == 0
        and record["fold"]["hardEdgeCountAt45Degrees"] == 0
        and record["fold"]["adjacentAngleMaximumDegrees"]
        <= MAXIMUM_ADJACENT_ANGLE
        and record["selfIntersection"]["result"] == "PASS"
        and record["runtimeModifierCount"] == 0
        and record["vertexGroupCount"] == 0
        and not record["hasShapeKeys"]
        for record in pose_records.values()
    )
    inventory_pass = all(
        inventory[name] == 0
        for name in (
            "armatureObjects",
            "armatureDatablocks",
            "actions",
            "latticeObjects",
            "shapeKeyMeshes",
            "meshModifierCount",
            "meshVertexGroupCount",
            "meshConstraintCount",
            "animatedObjects",
            "negativeScaleObjects",
            "temporaryNamedDatablocks",
            "colliderObjects",
        )
    )
    lineup_pass = all(
        record["participantCount"] == 4
        and record["participantPose"] == "Neutral"
        and all(
            max(abs(float(component) - 1.0) for component in scale) <= 1.0e-12
            for scale in record["rootScales"]
        )
        for record in lineup_records.values()
    )
    technical_pass = (
        source_hash_before == source_hash_after == SOURCE_SHA256
        and source_signature
        == {"vertices": 227942, "edges": 455880, "faces": 227940}
        and float(np.max(np.abs(weight_sums - 1.0))) <= 1.0e-12
        and len(pose_records) == 8
        and pose_geometry_pass
        and readability["result"] == "PASS"
        and lineup_pass
        and inventory_pass
        and len(outputs) == 20
        and not missing_renders
    )

    report = {
        "assetId": ASSET_ID,
        "revision": REVISION,
        "assetVersion": VERSION,
        "ownerTask": OWNER_TASK,
        "sourceRevision": "r16",
        "sourceIdentity": {
            "path": SOURCE_BLEND,
            "sha256Before": source_hash_before,
            "sha256After": source_hash_after,
            "unchanged": source_hash_before == source_hash_after,
            "approvalRecordPath": APPROVAL_RECORD,
            "approvalRecordSha256": APPROVAL_RECORD_SHA256,
        },
        "construction": CONSTRUCTION,
        "process": {
            "finalArtifactMode": "STATIC_BAKED_REVIEW_MESHES",
            "temporaryHelperRigCreated": 1,
            "temporaryHelperRigRetained": 0,
            "historicalPoseAnglesUsedAsReviewStart": True,
            "lineupMode": "ALL_NEUTRAL_HISTORICAL_OFFSETS",
            "renderTemplate": "10_SCENARIOS_X_2_STYLES_20_FILES",
            "armPivots": {name: list(value) for name, value in ARM_PIVOT.items()},
            "legPivots": {name: list(value) for name, value in LEG_PIVOT.items()},
            "weightMaximumSumError": float(np.max(np.abs(weight_sums - 1.0))),
            "jointSmoothing": {
                "iterations": JOINT_SMOOTH_ITERATIONS,
                "factor": JOINT_SMOOTH_FACTOR,
                "maximumTransitionWeight": float(np.max(joint_smooth_weight)),
            },
            "topologyChangingOperations": 0,
        },
        "poseIds": list(POSE_IDS),
        "poses": pose_records,
        "readability": readability,
        "lineupIds": list(LINEUP_IDS),
        "lineups": lineup_records,
        "cameras": {
            name: {
                "object": camera.name,
                "orthoScale": float(camera.data.ortho_scale),
                "location": list(camera.location),
                "rotationEuler": list(camera.rotation_euler),
            }
            for name, camera in cameras.items()
        },
        "finalInventory": inventory,
        "renderFiles": outputs,
        "missingRenderFiles": missing_renders,
        "userPoseApprovalRecorded": False,
        "productionTopologyApproved": False,
        "animationClaimed": False,
        "fbxExportExecuted": False,
        "unityImportExecuted": False,
        "technicalResult": "PASS" if technical_pass else "FAIL",
    }
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    if not technical_pass:
        raise RuntimeError(
            "r17 Pose8/lineup QA failed: "
            + json.dumps(report, separators=(",", ":"))
        )

    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = VERSION
    scene["owner_task"] = OWNER_TASK
    scene["source_revision"] = "r16"
    scene["source_sha256"] = SOURCE_SHA256
    scene["construction"] = CONSTRUCTION
    scene["candidate_status"] = "POSE_LINEUP_USER_REVIEW"
    scene["pose_ids_json"] = json.dumps(POSE_IDS, separators=(",", ":"))
    scene["lineup_ids_json"] = json.dumps(LINEUP_IDS, separators=(",", ":"))
    scene["render_jobs_json"] = json.dumps(jobs, separators=(",", ":"))
    scene["user_pose_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["gameplay_rig_authored"] = False
    scene["animation_authored"] = False
    scene["collider_authored"] = False
    scene["fbx_export_executed"] = False
    scene["unity_import_executed"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R17_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R17_GENERATION_RESULT=" + report["technicalResult"])


if __name__ == "__main__":
    main()
