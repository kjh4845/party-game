#!/usr/bin/env python3

"""Create the r19 weight-only rig candidate while preserving r16 geometry.

This is a narrow derivative of the verified r18 Generic-20 rig generator.  It
changes only the deterministic hip-support field, adds a 45-degree left-hip
stress pose to deformation QA, and retains Unity-parity linear blend skinning.
The immutable r16 source and r18 checkpoint are hash-pinned and never saved.
"""

import hashlib
import importlib.util
import json
import os
import shutil
import struct
import sys
import tempfile

import bpy
import numpy as np


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

HIP_START_METERS = 0.0
HIP_END_METERS = 0.255
HIP_FRONT_BIAS = 0.32
HIP_BACK_BIAS = 0.0
HIP_WINDOW_POWER = 0.55
HIP_DEPTH_SMOOTH_METERS = 0.018

HIP45_LIMITS = {
    "minimum": 0.48,
    "p01Minimum": 0.75,
    "p99Maximum": 1.32,
    "maximum": 1.80,
    "maximumAdjacentAngleDegrees": 30.0,
}

EXPECTED_POSE_IDS = (
    "Rest",
    "ShoulderForward",
    "ElbowFlex",
    "HipFlex_L",
    "HipFlex_R",
    "KneeFlex_L",
    "KneeFlex_R",
    "HipFlex_L_45",
)
HIP45_RENDER_FILENAME = (
    "CHR_MasterCharacter_C1B_Rig_r19_HipFlex_L_45_Neutral_Side.png"
)
EXPECTED_RENDER_FILENAMES = (
    "CHR_MasterCharacter_C1B_Rig_r19_Rest_Neutral_Front.png",
    "CHR_MasterCharacter_C1B_Rig_r19_RigOverlay_Front.png",
    "CHR_MasterCharacter_C1B_Rig_r19_RigOverlay_ThreeQuarter.png",
    "CHR_MasterCharacter_C1B_Rig_r19_ShoulderForward_Neutral_ThreeQuarter.png",
    "CHR_MasterCharacter_C1B_Rig_r19_ElbowFlex_Neutral_ThreeQuarter.png",
    "CHR_MasterCharacter_C1B_Rig_r19_HipFlex_L_Neutral_Side.png",
    "CHR_MasterCharacter_C1B_Rig_r19_KneeFlex_L_Neutral_Side.png",
    HIP45_RENDER_FILENAME,
)


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


require(os.path.isfile(R18_GENERATOR), "missing r18 generator")
require(os.path.isfile(R18_BLEND), "missing r18 rig checkpoint")
require(
    file_sha256(R18_GENERATOR) == R18_GENERATOR_SHA256,
    "r18 generator hash mismatch",
)
require(file_sha256(R18_BLEND) == R18_BLEND_SHA256, "r18 blend hash mismatch")

spec = importlib.util.spec_from_file_location("c1b_rw018_weight_base", R18_GENERATOR)
r18 = importlib.util.module_from_spec(spec)
spec.loader.exec_module(r18)


def build_body_weights(coordinates):
    """Return r18 weights with only the hip-support scalar field replaced.

    Blender uses -Y as forward.  The smooth front amount increases upper-thigh
    support only on the forward surface, closing the side-view hip notch without
    pulling the rear torso.  All other limb and torso functions are unchanged.
    """

    x = coordinates[:, 0]
    y = coordinates[:, 1]
    z = coordinates[:, 2]
    absolute_x = np.abs(x)

    arm_support = r18.smootherstep(
        (absolute_x - 0.040) / 0.260
    ) * r18.smootherstep((z - 0.300) / 0.220)
    arm_l_support = arm_support * (x < -r18.SIDE_EPSILON)
    arm_r_support = arm_support * (x > r18.SIDE_EPSILON)
    clavicle, upper, forearm, hand = r18.arm_segment_weights(absolute_x, y)

    hip_span = HIP_END_METERS - HIP_START_METERS
    hip_parameter = (z - HIP_START_METERS) / hip_span
    hip_clipped = np.clip(hip_parameter, 0.0, 1.0)
    hip_window = np.power(
        4.0 * hip_clipped * (1.0 - hip_clipped), HIP_WINDOW_POWER
    )
    depth_gate = np.tanh(y / HIP_DEPTH_SMOOTH_METERS)
    front_amount = (-y) * 0.5 * (1.0 - depth_gate)
    back_amount = y * 0.5 * (1.0 + depth_gate)
    hip_depth_shift = (
        HIP_FRONT_BIAS * front_amount + HIP_BACK_BIAS * back_amount
    ) / hip_span
    leg_vertical = 1.0 - r18.smootherstep(
        hip_parameter - hip_depth_shift * hip_window
    )

    center_mix = r18.smootherstep((z - 0.080) / 0.055)
    center_gate = (1.0 - center_mix) + center_mix * r18.smootherstep(
        (absolute_x - 0.005) / 0.040
    )
    leg_support = leg_vertical * center_gate
    leg_l_support = leg_support * (x < -r18.SIDE_EPSILON)
    leg_r_support = leg_support * (x > r18.SIDE_EPSILON)
    thigh, calf, foot = r18.leg_segment_weights(z)

    limb_sum = arm_l_support + arm_r_support + leg_l_support + leg_r_support
    r18.require(float(limb_sum.max()) <= 1.0 + 1.0e-12, "limb support overlap")
    torso_support = 1.0 - limb_sum

    pelvis_to_spine = r18.smootherstep((z - 0.280) / 0.080)
    spine_to_chest = r18.smootherstep((z - 0.500) / 0.080)
    chest_to_neck = r18.smootherstep((z - 0.640) / 0.060)
    pelvis = torso_support * (1.0 - pelvis_to_spine)
    spine = torso_support * pelvis_to_spine * (1.0 - spine_to_chest)
    chest = torso_support * spine_to_chest * (1.0 - chest_to_neck)
    neck_weight = torso_support * chest_to_neck

    weights = {
        "Pelvis": pelvis,
        "Spine": spine,
        "Chest": chest,
        "Neck": neck_weight,
        "Clavicle_L": arm_l_support * clavicle,
        "UpperArm_L": arm_l_support * upper,
        "Forearm_L": arm_l_support * forearm,
        "HandLogical_L": arm_l_support * hand,
        "Clavicle_R": arm_r_support * clavicle,
        "UpperArm_R": arm_r_support * upper,
        "Forearm_R": arm_r_support * forearm,
        "HandLogical_R": arm_r_support * hand,
        "Thigh_L": leg_l_support * thigh,
        "Calf_L": leg_l_support * calf,
        "Foot_L": leg_l_support * foot,
        "Thigh_R": leg_r_support * thigh,
        "Calf_R": leg_r_support * calf,
        "Foot_R": leg_r_support * foot,
    }
    weight_sum = sum(weights.values())
    r18.require(
        float(np.max(np.abs(weight_sum - 1.0))) <= 1.0e-12,
        "body weights are not normalized",
    )
    mirrored_weights, mirror_pairing = r18.enforce_mirrored_weights(
        coordinates, weights
    )
    build_body_weights.mirror_pairing = mirror_pairing
    return mirrored_weights


ORIGINAL_ADD_ARMATURE_MODIFIER = r18.add_armature_modifier


def add_armature_modifier(obj, armature):
    modifier = ORIGINAL_ADD_ARMATURE_MODIFIER(obj, armature)
    modifier.name = "C1BRW019_Armature"
    return modifier


ORIGINAL_PREPARE_MESHES = r18.prepare_meshes


def prepare_meshes(body, head, armature):
    bpy.context.scene.name = "C1BRW019_RigReview"
    collection = bpy.data.collections.get("C1BRW018_Rig")
    if collection is not None:
        collection.name = "C1BRW019_Rig"
    result = ORIGINAL_PREPARE_MESHES(body, head, armature)
    for obj in (bpy.data.objects[r18.BODY_NAME], bpy.data.objects[r18.HEAD_NAME]):
        obj["skin_weight_method"] = "DETERMINISTIC_C2_FRONT_HIP_SUPPORT"
        obj["weight_revision"] = "r19"
    return result


LAST_POSE_ID = None
ORIGINAL_APPLY_POSE_TEST = r18.apply_pose_test
ORIGINAL_POSE_RECORD_PASS = r18.pose_record_pass


def apply_pose_test(armature, pose_id):
    global LAST_POSE_ID
    LAST_POSE_ID = pose_id
    return ORIGINAL_APPLY_POSE_TEST(armature, pose_id)


def pose_record_pass(record, is_rest=False):
    if LAST_POSE_ID != "HipFlex_L_45":
        return ORIGINAL_POSE_RECORD_PASS(record, is_rest)
    edge = record["edgeStretchRatio"]
    return (
        record["vertices"] == 227942
        and record["edges"] == 455880
        and record["faces"] == 227940
        and record["manifold"]["result"] == "PASS"
        and record["fold"]["foldoverEdgeCountAt90Degrees"] == 0
        and record["fold"]["hardEdgeCountAt45Degrees"] == 0
        and record["fold"]["adjacentAngleMaximumDegrees"]
        <= HIP45_LIMITS["maximumAdjacentAngleDegrees"]
        and record["selfIntersection"]["result"] == "PASS"
        and edge["minimum"] >= HIP45_LIMITS["minimum"]
        and edge["p01"] >= HIP45_LIMITS["p01Minimum"]
        and edge["p99"] <= HIP45_LIMITS["p99Maximum"]
        and edge["maximum"] <= HIP45_LIMITS["maximum"]
        and record["maximumVertexDisplacementMeters"] >= 0.005
    )


def parse_output_args():
    require("--" in sys.argv, "expected output arguments after --")
    values = sys.argv[sys.argv.index("--") + 1 :]
    require(len(values) == 3, "expected output blend, render dir, and report")
    blend_path, render_dir, report_path = tuple(
        os.path.abspath(value) for value in values
    )
    protected_files = (
        r18.SOURCE_BLEND,
        r18.APPROVAL_RECORD,
        R18_GENERATOR,
        R18_BLEND,
        os.path.abspath(__file__),
    )
    for output_path, label in (
        (blend_path, "output blend"),
        (render_dir, "output render directory"),
        (report_path, "output report"),
    ):
        require(
            all(
                not r18.paths_resolve_same(output_path, protected)
                for protected in protected_files
            ),
            f"{label} may not overwrite an input or generator",
        )
    require(
        not r18.paths_resolve_same(blend_path, report_path),
        "output blend and report must be different files",
    )
    require(
        not r18.paths_resolve_same(render_dir, blend_path)
        and not r18.paths_resolve_same(render_dir, report_path),
        "render directory must differ from output files",
    )
    output_parent = os.path.realpath(os.path.dirname(blend_path))
    require(
        output_parent == os.path.realpath(os.path.dirname(render_dir))
        == os.path.realpath(os.path.dirname(report_path)),
        "blend, Renders, and report must share one dedicated package directory",
    )
    require(
        os.path.basename(render_dir) == "Renders",
        "render directory must be the package's dedicated Renders directory",
    )
    protected_package_directories = (
        os.path.dirname(r18.SOURCE_BLEND),
        os.path.dirname(R18_BLEND),
    )
    require(
        all(
            not r18.paths_resolve_same(output_parent, protected_directory)
            for protected_directory in protected_package_directories
        ),
        "r19 outputs may not use an upstream source package directory",
    )
    return blend_path, render_dir, report_path


def configure_r19():
    r18.REVISION = "r19"
    r18.VERSION = "0.19.0-weight-only-rig-preview"
    r18.ARMATURE_NAME = "RIG_C1B_R19_Armature"
    r18.ARMATURE_DATA_NAME = "RIG_C1B_R19_ArmatureData"
    r18.BODY_NAME = "CHR_C1B_R19_SkinnedBody"
    r18.HEAD_NAME = "CHR_C1B_R19_SkinnedHead"
    r18.build_body_weights = build_body_weights
    r18.add_armature_modifier = add_armature_modifier
    r18.prepare_meshes = prepare_meshes
    r18.apply_pose_test = apply_pose_test
    r18.pose_record_pass = pose_record_pass
    r18.POSE_TESTS = dict(r18.POSE_TESTS)
    r18.POSE_TESTS["HipFlex_L_45"] = (("Thigh_L", "X", -45.0),)
    # Make the base generator's protection and provenance point at this wrapper.
    r18.__file__ = os.path.abspath(__file__)


def object_geometry_snapshot(obj):
    mesh = obj.data
    local = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", local)
    local = local.reshape((-1, 3))
    matrix = np.asarray(obj.matrix_world, dtype=np.float64)
    world = (
        local @ matrix[:3, :3].T + matrix[:3, 3]
    ).astype("<f4", copy=False)
    edges = np.empty(len(mesh.edges) * 2, dtype="<i4")
    mesh.edges.foreach_get("vertices", edges)
    edges = edges.reshape((-1, 2))
    polygon_bytes = bytearray()
    for polygon in mesh.polygons:
        cycle = tuple(int(value) for value in polygon.vertices)
        polygon_bytes.extend(struct.pack("<I", len(cycle)))
        polygon_bytes.extend(struct.pack("<" + "I" * len(cycle), *cycle))
    polygon_bytes = bytes(polygon_bytes)
    digest = hashlib.sha256()
    digest.update(b"C1BRW019_WORLD_GEOMETRY_V1")
    digest.update(
        struct.pack(
            "<QQQ",
            len(mesh.vertices),
            len(mesh.edges),
            len(mesh.polygons),
        )
    )
    digest.update(world.tobytes())
    digest.update(edges.tobytes())
    digest.update(polygon_bytes)
    return {
        "signature": {
            "vertices": len(mesh.vertices),
            "edges": len(mesh.edges),
            "faces": len(mesh.polygons),
        },
        "worldCoordinates": world,
        "edges": edges,
        "polygonBytes": polygon_bytes,
        "worldGeometrySha256": digest.hexdigest(),
    }


def compare_geometry(label, source, target):
    signatures_equal = source["signature"] == target["signature"]
    coordinate_bits_equal = (
        signatures_equal
        and np.array_equal(source["worldCoordinates"], target["worldCoordinates"])
    )
    maximum_delta = (
        float(
            np.max(
                np.abs(
                    source["worldCoordinates"].astype(np.float64)
                    - target["worldCoordinates"].astype(np.float64)
                )
            )
        )
        if signatures_equal and len(source["worldCoordinates"])
        else float("inf")
    )
    edge_order_equal = signatures_equal and np.array_equal(
        source["edges"], target["edges"]
    )
    polygon_cycles_equal = (
        signatures_equal and source["polygonBytes"] == target["polygonBytes"]
    )
    result = (
        signatures_equal
        and coordinate_bits_equal
        and maximum_delta == 0.0
        and edge_order_equal
        and polygon_cycles_equal
        and source["worldGeometrySha256"] == target["worldGeometrySha256"]
    )
    return {
        "label": label,
        "sourceSignature": source["signature"],
        "targetSignature": target["signature"],
        "coordinateFloat32BitsEqual": coordinate_bits_equal,
        "maximumCoordinateDeltaMeters": maximum_delta,
        "edgeIndexOrderEqual": edge_order_equal,
        "orderedPolygonCyclesEqual": polygon_cycles_equal,
        "sourceWorldGeometrySha256": source["worldGeometrySha256"],
        "targetWorldGeometrySha256": target["worldGeometrySha256"],
        "worldGeometrySha256Equal": (
            source["worldGeometrySha256"] == target["worldGeometrySha256"]
        ),
        "result": "PASS" if result else "FAIL",
    }


def exact_geometry_verification(stage_blend_path):
    source_hash_before = file_sha256(r18.SOURCE_BLEND)
    bpy.ops.wm.open_mainfile(filepath=r18.SOURCE_BLEND)
    source_body = object_geometry_snapshot(bpy.data.objects[r18.SOURCE_BODY])
    source_head = object_geometry_snapshot(bpy.data.objects[r18.SOURCE_HEAD])
    source_hash_after_read = file_sha256(r18.SOURCE_BLEND)

    bpy.ops.wm.open_mainfile(filepath=stage_blend_path)
    target_body = object_geometry_snapshot(bpy.data.objects[r18.BODY_NAME])
    target_head = object_geometry_snapshot(bpy.data.objects[r18.HEAD_NAME])
    body = compare_geometry("body", source_body, target_body)
    head = compare_geometry("head", source_head, target_head)
    source_hash_after = file_sha256(r18.SOURCE_BLEND)
    result = (
        source_hash_before
        == source_hash_after_read
        == source_hash_after
        == r18.SOURCE_SHA256
        and body["result"] == "PASS"
        and head["result"] == "PASS"
    )
    return {
        "sourcePath": r18.SOURCE_BLEND,
        "targetPath": stage_blend_path,
        "sourceSha256Before": source_hash_before,
        "sourceSha256After": source_hash_after,
        "sourceUnchanged": source_hash_before == source_hash_after,
        "body": body,
        "head": head,
        "result": "PASS" if result else "FAIL",
    }


def actual_assignment_metrics(obj):
    valid_indices = {group.index for group in obj.vertex_groups}
    maximum_influences = 0
    maximum_stored_assignments = 0
    unweighted = 0
    invalid_group_assignments = 0
    nonfinite_assignments = 0
    maximum_sum_error = 0.0
    minimum_stored_weight = float("inf")
    maximum_stored_weight = float("-inf")
    for vertex in obj.data.vertices:
        maximum_stored_assignments = max(
            maximum_stored_assignments, len(vertex.groups)
        )
        positive = []
        for assignment in vertex.groups:
            if assignment.group not in valid_indices:
                invalid_group_assignments += 1
            weight = float(assignment.weight)
            minimum_stored_weight = min(minimum_stored_weight, weight)
            maximum_stored_weight = max(maximum_stored_weight, weight)
            if not np.isfinite(weight):
                nonfinite_assignments += 1
            if weight > r18.WEIGHT_EPSILON:
                positive.append(weight)
        maximum_influences = max(maximum_influences, len(positive))
        if not positive:
            unweighted += 1
        maximum_sum_error = max(maximum_sum_error, abs(sum(positive) - 1.0))
    return {
        "vertexCount": len(obj.data.vertices),
        "maximumStoredAssignmentsPerVertex": maximum_stored_assignments,
        "maximumInfluencesPerVertexAcrossAllGroups": maximum_influences,
        "unweightedVertexCountAcrossAllGroups": unweighted,
        "invalidGroupAssignmentCount": invalid_group_assignments,
        "nonfiniteAssignmentCount": nonfinite_assignments,
        "maximumWeightSumErrorAcrossAllGroups": maximum_sum_error,
        "minimumStoredWeight": (
            minimum_stored_weight
            if minimum_stored_weight != float("inf")
            else 0.0
        ),
        "maximumStoredWeight": (
            maximum_stored_weight
            if maximum_stored_weight != float("-inf")
            else 0.0
        ),
    }


def strict_saved_contract(stage_blend_path):
    bpy.ops.wm.open_mainfile(filepath=stage_blend_path)
    body = bpy.data.objects.get(r18.BODY_NAME)
    head = bpy.data.objects.get(r18.HEAD_NAME)
    armature = bpy.data.objects.get(r18.ARMATURE_NAME)
    require(body is not None and body.type == "MESH", "strict body missing")
    require(head is not None and head.type == "MESH", "strict head missing")
    require(
        armature is not None and armature.type == "ARMATURE",
        "strict armature missing",
    )

    coordinates = r18.r12.mesh_coordinates(body.data)
    expected_body_weights = build_body_weights(coordinates)
    body_weight_roundtrip = r18.weight_report(
        body, expected_body_weights, coordinates
    )
    head_coordinates = r18.r12.mesh_coordinates(head.data)
    expected_head_weights = {
        "Head": np.ones(len(head_coordinates), dtype=np.float64)
    }
    head_weight_roundtrip = r18.weight_report(
        head, expected_head_weights, head_coordinates
    )
    expected_body_groups = set(expected_body_weights)
    expected_head_groups = {"Head"}
    expected_body_group_order = tuple(expected_body_weights)
    expected_head_group_order = ("Head",)
    actual_body_groups = {group.name for group in body.vertex_groups}
    actual_head_groups = {group.name for group in head.vertex_groups}
    actual_body_group_order = tuple(group.name for group in body.vertex_groups)
    actual_head_group_order = tuple(group.name for group in head.vertex_groups)
    body_assignments = actual_assignment_metrics(body)
    head_assignments = actual_assignment_metrics(head)
    inventory = r18.rig_inventory(armature, body, head)
    body_modifiers_valid = (
        len(body.modifiers) == 1
        and body.modifiers[0].type == "ARMATURE"
        and body.modifiers[0].object is armature
        and body.modifiers[0].use_vertex_groups
        and not body.modifiers[0].use_bone_envelopes
        and not body.modifiers[0].use_deform_preserve_volume
    )
    head_modifiers_valid = (
        len(head.modifiers) == 1
        and head.modifiers[0].type == "ARMATURE"
        and head.modifiers[0].object is armature
        and head.modifiers[0].use_vertex_groups
        and not head.modifiers[0].use_bone_envelopes
        and not head.modifiers[0].use_deform_preserve_volume
    )
    bone_names = set(armature.data.bones.keys())
    deform_bone_names = {
        bone.name for bone in armature.data.bones if bone.use_deform
    }
    exact_objects = {
        obj.name: obj.type for obj in bpy.data.objects
    } == {
        r18.ARMATURE_NAME: "ARMATURE",
        r18.BODY_NAME: "MESH",
        r18.HEAD_NAME: "MESH",
    }
    exact_parenting = body.parent is armature and head.parent is armature
    exact_data_names = (
        body.data.name == r18.BODY_NAME + "Mesh"
        and head.data.name == r18.HEAD_NAME + "Mesh"
        and armature.data.name == r18.ARMATURE_DATA_NAME
    )
    ids_with_animation_data = []
    animation_ids = [
        bpy.context.scene,
        *list(bpy.data.objects),
        *list(bpy.data.meshes),
        *list(bpy.data.armatures),
        *list(bpy.data.materials),
        *list(bpy.data.worlds),
    ]
    for datablock in animation_ids:
        if getattr(datablock, "animation_data", None) is not None:
            ids_with_animation_data.append(datablock.name)
    result = (
        actual_body_groups == expected_body_groups
        and actual_head_groups == expected_head_groups
        and actual_body_group_order == expected_body_group_order
        and actual_head_group_order == expected_head_group_order
        and expected_body_groups | expected_head_groups == deform_bone_names
        and "Root" not in deform_bone_names
        and body_assignments["maximumStoredAssignmentsPerVertex"]
        <= r18.MAXIMUM_WEIGHTS_PER_VERTEX
        and body_assignments["maximumInfluencesPerVertexAcrossAllGroups"]
        <= r18.MAXIMUM_WEIGHTS_PER_VERTEX
        and body_assignments["unweightedVertexCountAcrossAllGroups"] == 0
        and body_assignments["invalidGroupAssignmentCount"] == 0
        and body_assignments["nonfiniteAssignmentCount"] == 0
        and body_assignments["minimumStoredWeight"] >= 0.0
        and body_assignments["maximumStoredWeight"] <= 1.0
        and body_assignments["maximumWeightSumErrorAcrossAllGroups"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and body_weight_roundtrip["maximumAssignmentRoundtripError"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and head_assignments["maximumInfluencesPerVertexAcrossAllGroups"] == 1
        and head_assignments["maximumStoredAssignmentsPerVertex"] == 1
        and head_assignments["unweightedVertexCountAcrossAllGroups"] == 0
        and head_assignments["invalidGroupAssignmentCount"] == 0
        and head_assignments["nonfiniteAssignmentCount"] == 0
        and head_assignments["minimumStoredWeight"] >= 0.0
        and head_assignments["maximumStoredWeight"] <= 1.0
        and head_assignments["maximumWeightSumErrorAcrossAllGroups"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and head_weight_roundtrip["maximumAssignmentRoundtripError"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and bone_names == set(r18.REQUIRED_BONES)
        and len(bone_names) == 20
        and r18.skeleton_matches_spec(armature)
        and body_modifiers_valid
        and head_modifiers_valid
        and exact_objects
        and exact_parenting
        and exact_data_names
        and len(bpy.data.scenes) == 1
        and len(bpy.data.meshes) == 2
        and len(bpy.data.armatures) == 1
        and len(bpy.data.cameras) == 0
        and len(bpy.data.lights) == 0
        and len(bpy.data.curves) == 0
        and len(bpy.data.lattices) == 0
        and len(bpy.data.shape_keys) == 0
        and inventory["armatureObjectCount"] == 1
        and inventory["armatureDatablockCount"] == 1
        and inventory["boneCount"] == 20
        and inventory["actionCount"] == 0
        and inventory["shapeKeyDatablockCount"] == 0
        and inventory["latticeObjectCount"] == 0
        and inventory["bodyVertexGroupCount"] == len(expected_body_groups)
        and inventory["headVertexGroupCount"] == len(expected_head_groups)
        and inventory["objectTypeCounts"] == {"ARMATURE": 1, "MESH": 2}
        and inventory["collectionCount"] == 1
        and not inventory["temporaryDatablocks"]
        and not inventory["negativeScaleObjects"]
        and all(len(pose_bone.constraints) == 0 for pose_bone in armature.pose.bones)
        and len(armature.constraints) == 0
        and not ids_with_animation_data
        and r18.pose_is_rest(armature)
    )
    return {
        "bodyExpectedVertexGroups": sorted(expected_body_groups),
        "bodyActualVertexGroups": sorted(actual_body_groups),
        "headExpectedVertexGroups": sorted(expected_head_groups),
        "headActualVertexGroups": sorted(actual_head_groups),
        "bodyVertexGroupOrder": list(actual_body_group_order),
        "headVertexGroupOrder": list(actual_head_group_order),
        "deformBoneNames": sorted(deform_bone_names),
        "bodyAssignments": body_assignments,
        "headAssignments": head_assignments,
        "bodyExpectedWeightRoundtrip": body_weight_roundtrip,
        "headExpectedWeightRoundtrip": head_weight_roundtrip,
        "bodyLinearModifierValid": body_modifiers_valid,
        "headLinearModifierValid": head_modifiers_valid,
        "boneNamesExact": bone_names == set(r18.REQUIRED_BONES),
        "objectNamesAndTypesExact": exact_objects,
        "parentingExact": exact_parenting,
        "datablockNamesExact": exact_data_names,
        "idsWithAnimationData": ids_with_animation_data,
        "poseBoneConstraintCount": sum(
            len(pose_bone.constraints) for pose_bone in armature.pose.bones
        ),
        "armatureObjectConstraintCount": len(armature.constraints),
        "inventory": inventory,
        "restPoseConfirmed": r18.pose_is_rest(armature),
        "result": "PASS" if result else "FAIL",
    }


def hip45_record_pass_explicit(record):
    edge = record["edgeStretchRatio"]
    return (
        record["vertices"] == 227942
        and record["edges"] == 455880
        and record["faces"] == 227940
        and record["manifold"]["result"] == "PASS"
        and record["fold"]["foldoverEdgeCountAt90Degrees"] == 0
        and record["fold"]["hardEdgeCountAt45Degrees"] == 0
        and record["fold"]["adjacentAngleMaximumDegrees"]
        <= HIP45_LIMITS["maximumAdjacentAngleDegrees"]
        and record["selfIntersection"]["result"] == "PASS"
        and edge["minimum"] >= HIP45_LIMITS["minimum"]
        and edge["p01"] >= HIP45_LIMITS["p01Minimum"]
        and edge["p99"] <= HIP45_LIMITS["p99Maximum"]
        and edge["maximum"] <= HIP45_LIMITS["maximum"]
        and record["maximumVertexDisplacementMeters"] >= 0.005
    )


def render_saved_hip45(stage_blend_path, render_dir):
    bpy.ops.wm.open_mainfile(filepath=stage_blend_path)
    scene = bpy.context.scene
    body = bpy.data.objects[r18.BODY_NAME]
    head = bpy.data.objects[r18.HEAD_NAME]
    armature = bpy.data.objects[r18.ARMATURE_NAME]
    rest_coordinates = r18.r12.mesh_coordinates(body.data)
    rest_edges = r18.r12.mesh_edges(body.data)
    original_visibility = {
        obj.name: bool(obj.hide_render) for obj in bpy.data.objects
    }
    existing_lights = [obj for obj in bpy.data.objects if obj.type == "LIGHT"]
    for light in existing_lights:
        light.hide_render = True
    body.hide_render = False
    head.hide_render = False
    collection = bpy.data.collections.new("TMP_C1BRW019_Hip45Render")
    scene.collection.children.link(collection)
    camera = r18.create_camera(
        collection,
        "TMP_CAM_R19_Hip45Side",
        (-4.0, 0.0, 0.50),
        (0.0, 0.0, 0.50),
        1.22,
    )
    lights = [
        r18.create_area_light(
            collection,
            "TMP_LIGHT_R19_Hip45Key",
            (-2.4, -3.0, 3.0),
            420.0,
            4.0,
        ),
        r18.create_area_light(
            collection,
            "TMP_LIGHT_R19_Hip45Fill",
            (2.8, -1.8, 1.6),
            220.0,
            3.0,
        ),
        r18.create_area_light(
            collection,
            "TMP_LIGHT_R19_Hip45Rim",
            (0.0, 2.8, 2.2),
            300.0,
            2.5,
        ),
    ]
    r18.configure_render(scene)
    try:
        apply_pose_test(armature, "HipFlex_L_45")
        record = r18.mesh_pose_metrics(body, rest_coordinates, rest_edges)
        record["result"] = (
            "PASS" if hip45_record_pass_explicit(record) else "FAIL"
        )
        require(record["result"] == "PASS", "saved Hip45 QA failed")
        output_path = os.path.join(render_dir, HIP45_RENDER_FILENAME)
        r18.render_to(scene, camera, output_path)
        require(
            os.path.isfile(output_path) and os.path.getsize(output_path) > 0,
            "Hip45 side render missing or empty",
        )
    finally:
        r18.reset_pose(armature)
        r18.remove_objects([camera, *lights])
        scene.collection.children.unlink(collection)
        bpy.data.collections.remove(collection)
        for name, hidden in original_visibility.items():
            obj = bpy.data.objects.get(name)
            if obj is not None:
                obj.hide_render = hidden
        for light in existing_lights:
            if bpy.data.objects.get(light.name) is not None:
                light.hide_render = original_visibility.get(light.name, False)
    return HIP45_RENDER_FILENAME, record


def write_json_atomic(path, value):
    temporary = path + ".wrapper.partial"
    if os.path.isfile(temporary):
        os.remove(temporary)
    with open(temporary, "w", encoding="utf-8") as stream:
        json.dump(value, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    os.replace(temporary, path)


def enrich_report(
    report_path,
    stage_blend_path,
    stage_render_dir,
    final_blend_path,
    exact_geometry,
    strict_contract,
    saved_hip45,
    hip45_render_filename,
    r18_blend_before,
    run_id,
):
    with open(report_path, "r", encoding="utf-8") as stream:
        report = json.load(stream)
    report["weights"]["method"] = "DETERMINISTIC_C2_FRONT_HIP_SUPPORT"
    report["weights"]["hipField"] = {
        "startMeters": HIP_START_METERS,
        "endMeters": HIP_END_METERS,
        "frontBias": HIP_FRONT_BIAS,
        "backBias": HIP_BACK_BIAS,
        "windowPower": HIP_WINDOW_POWER,
        "depthSmoothMeters": HIP_DEPTH_SMOOTH_METERS,
        "formula": (
            "u=z/0.255; c=clip01(u); window=(4*c*(1-c))^0.55; "
            "g=tanh(y/0.018); front=(-y)*0.5*(1-g); "
            "legVertical=1-smootherstep(u-(0.32*front/0.255)*window)"
        ),
        "unchangedWeightEquations": [
            "centerGate",
            "legSegmentWeights",
            "armWeights",
            "torsoDistribution",
        ],
    }
    report["deformationQA"]["stressPoseLimits"] = {
        "HipFlex_L_45": HIP45_LIMITS
    }
    report.pop("weightSelection", None)
    pose_ids = tuple(report["deformationQA"]["poses"].keys())
    require(pose_ids == EXPECTED_POSE_IDS, "r19 pose-id contract mismatch")
    require(
        all(
            record.get("result") == "PASS"
            for record in report["deformationQA"]["poses"].values()
        ),
        "one or more r19 pose records failed",
    )
    require(
        hip45_record_pass_explicit(
            report["deformationQA"]["poses"]["HipFlex_L_45"]
        ),
        "base Hip45 record failed explicit limits",
    )
    require(
        hip45_record_pass_explicit(saved_hip45),
        "saved Hip45 record failed explicit limits",
    )
    report["deformationQA"]["savedHip45Verification"] = saved_hip45
    report["deformationQA"]["poseContract"] = {
        "expected": list(EXPECTED_POSE_IDS),
        "actual": list(pose_ids),
        "exactOrderAndSet": pose_ids == EXPECTED_POSE_IDS,
        "result": "PASS",
    }
    if hip45_render_filename not in report["renderFiles"]:
        report["renderFiles"].append(hip45_render_filename)
    render_names = tuple(report["renderFiles"])
    require(
        render_names == EXPECTED_RENDER_FILENAMES,
        "r19 render contract is not the exact ordered eight-file bundle",
    )
    missing_renders = [
        filename
        for filename in report["renderFiles"]
        if not os.path.isfile(os.path.join(stage_render_dir, filename))
        or os.path.getsize(os.path.join(stage_render_dir, filename)) <= 0
    ]
    report["missingRenderFiles"] = missing_renders
    require(not missing_renders, "staged render bundle is incomplete")
    report["renderVerification"] = {
        "expected": list(EXPECTED_RENDER_FILENAMES),
        "actual": list(render_names),
        "exactOrderAndSet": render_names == EXPECTED_RENDER_FILENAMES,
        "files": {
            filename: {
                "bytes": os.path.getsize(os.path.join(stage_render_dir, filename)),
                "sha256": file_sha256(os.path.join(stage_render_dir, filename)),
            }
            for filename in render_names
        },
        "result": "PASS",
    }

    exact_geometry = dict(exact_geometry)
    exact_geometry["targetPath"] = final_blend_path
    report["exactGeometryVerification"] = exact_geometry
    report["strictSavedContract"] = strict_contract
    require(exact_geometry["result"] == "PASS", "exact geometry gate failed")
    require(strict_contract["result"] == "PASS", "strict saved contract failed")

    r18_blend_after = file_sha256(R18_BLEND)
    r18_generator_after = file_sha256(R18_GENERATOR)
    report["upstreamR18"] = {
        "generatorPath": R18_GENERATOR,
        "generatorSha256Before": R18_GENERATOR_SHA256,
        "generatorSha256After": r18_generator_after,
        "checkpointPath": R18_BLEND,
        "checkpointSha256Before": r18_blend_before,
        "checkpointSha256After": r18_blend_after,
        "unchanged": (
            r18_generator_after == R18_GENERATOR_SHA256
            and r18_blend_before == r18_blend_after == R18_BLEND_SHA256
        ),
    }
    require(report["technicalResult"] == "PASS", "base r19 technical QA failed")
    require(
        report["savedFileVerification"]["result"] == "PASS",
        "saved-file reopen verification failed",
    )
    require(report["upstreamR18"]["unchanged"], "r18 checkpoint changed")
    require(os.path.isfile(stage_blend_path), "staged r19 blend missing")
    require(
        report["generator"]["sha256"] == file_sha256(os.path.abspath(__file__)),
        "report generator hash does not match wrapper",
    )
    report["output"] = {
        "blendPath": final_blend_path,
        "blendBytes": os.path.getsize(stage_blend_path),
        "blendSha256": file_sha256(stage_blend_path),
    }
    report["stagingValidation"] = {
        "runId": run_id,
        "baseGeneratedInIsolatedSiblingDirectory": True,
        "wrapperChecksCompletedBeforePromotion": True,
        "previousFinalDeletedBeforeValidation": False,
        "promotionPolicy": "VALIDATED_RENDER_AND_BLEND_THEN_REPORT_LAST_WITH_ROLLBACK",
        "result": "PASS",
    }
    report["wrapperTechnicalResult"] = "PASS"
    report["technicalResult"] = "PASS"
    write_json_atomic(report_path, report)
    return report


def promote_validated_outputs(
    stage_root,
    stage_blend,
    stage_render_dir,
    stage_report,
    final_blend,
    final_render_dir,
    final_report,
    expected_blend_sha256,
    render_files,
):
    final_targets = (final_blend, final_render_dir, final_report)
    for target in final_targets:
        os.makedirs(os.path.dirname(target), exist_ok=True)
        require(
            os.stat(os.path.dirname(target)).st_dev == os.stat(stage_root).st_dev,
            "staging and final outputs must share a filesystem",
        )
    require(
        not os.path.exists(final_render_dir) or os.path.isdir(final_render_dir),
        "final render path exists but is not a directory",
    )
    render_backup = os.path.join(stage_root, "previous-renders")
    blend_backup = os.path.join(stage_root, "previous.blend")
    report_backup = os.path.join(stage_root, "previous-report.json")
    with open(stage_report, "r", encoding="utf-8") as stream:
        staged_report = json.load(stream)
    render_promoted = False
    render_backed_up = False
    blend_promoted = False
    report_promoted = False
    had_render = os.path.exists(final_render_dir)
    had_blend = os.path.isfile(final_blend)
    had_report = os.path.isfile(final_report)
    try:
        if had_render:
            os.replace(final_render_dir, render_backup)
            render_backed_up = True
        os.replace(stage_render_dir, final_render_dir)
        render_promoted = True

        if had_blend:
            shutil.copy2(final_blend, blend_backup)
        os.replace(stage_blend, final_blend)
        blend_promoted = True
        require(file_sha256(final_blend) == expected_blend_sha256, "promoted blend hash mismatch")
        require(
            all(
                os.path.isfile(os.path.join(final_render_dir, filename))
                and os.path.getsize(os.path.join(final_render_dir, filename)) > 0
                and file_sha256(os.path.join(final_render_dir, filename))
                == staged_report["renderVerification"]["files"][filename][
                    "sha256"
                ]
                for filename in render_files
            ),
            "promoted render bundle is incomplete or changed",
        )

        if had_report:
            shutil.copy2(final_report, report_backup)
        os.replace(stage_report, final_report)
        report_promoted = True
        require(os.path.isfile(final_report), "promoted report missing")
        with open(final_report, "r", encoding="utf-8") as stream:
            promoted_report = json.load(stream)
        require(
            promoted_report["output"]["blendSha256"] == expected_blend_sha256,
            "promoted report blend hash mismatch",
        )
    except Exception:
        if report_promoted:
            if had_report and os.path.isfile(report_backup):
                os.replace(report_backup, final_report)
            elif os.path.isfile(final_report):
                os.replace(final_report, stage_report)
        if blend_promoted:
            if had_blend and os.path.isfile(blend_backup):
                os.replace(blend_backup, final_blend)
            elif os.path.isfile(final_blend):
                os.replace(final_blend, stage_blend)
        if render_promoted:
            if os.path.isdir(final_render_dir):
                os.replace(final_render_dir, stage_render_dir)
        if render_backed_up and os.path.isdir(render_backup):
            os.replace(render_backup, final_render_dir)
        raise
    if os.path.isdir(render_backup):
        shutil.rmtree(render_backup)
    for backup in (blend_backup, report_backup):
        if os.path.isfile(backup):
            os.remove(backup)


def main():
    blend_path, render_dir, report_path = parse_output_args()
    output_parent = os.path.dirname(blend_path)
    os.makedirs(output_parent, exist_ok=True)
    stage_root = tempfile.mkdtemp(
        prefix=".c1brw019-stage-",
        dir=output_parent,
    )
    stage_blend = os.path.join(stage_root, "candidate.blend")
    stage_render_dir = os.path.join(stage_root, "Renders")
    stage_report = os.path.join(stage_root, "RigQAReport.json")
    r18_blend_before = file_sha256(R18_BLEND)
    configure_r19()
    original_argv = list(sys.argv)
    report = None
    try:
        separator = original_argv.index("--")
        sys.argv = original_argv[: separator + 1] + [
            stage_blend,
            stage_render_dir,
            stage_report,
        ]
        r18.main()
        sys.argv = original_argv

        hip45_render_filename, saved_hip45 = render_saved_hip45(
            stage_blend, stage_render_dir
        )
        exact_geometry = exact_geometry_verification(stage_blend)
        strict_contract = strict_saved_contract(stage_blend)
        report = enrich_report(
            stage_report,
            stage_blend,
            stage_render_dir,
            blend_path,
            exact_geometry,
            strict_contract,
            saved_hip45,
            hip45_render_filename,
            r18_blend_before,
            os.path.basename(stage_root),
        )
        promote_validated_outputs(
            stage_root,
            stage_blend,
            stage_render_dir,
            stage_report,
            blend_path,
            render_dir,
            report_path,
            report["output"]["blendSha256"],
            report["renderFiles"],
        )
    finally:
        sys.argv = original_argv
        if os.path.isdir(stage_root):
            shutil.rmtree(stage_root)
    require(report is not None, "r19 wrapper did not produce a validated report")
    print(
        "R19_WEIGHT_ONLY_RESULT=PASS "
        + json.dumps(
            {
                "blend": blend_path,
                "blendSha256": report["output"]["blendSha256"],
                "report": report_path,
                "poses": len(report["deformationQA"]["poses"]),
                "renders": len(report["renderFiles"]),
            },
            separators=(",", ":"),
        )
    )


if __name__ == "__main__":
    main()
