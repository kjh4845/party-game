#!/usr/bin/env python3

"""Create a rig-only Generic 20-bone r18 review source from approved r16.

The immutable r16 source is opened but never saved.  The output contains a
single Generic armature, deterministic skin weights, no Action datablocks,
and no animation clips.  Temporary deformation poses and visible bone proxy
geometry are used only for QA renders and are removed before the blend saves.
"""

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
from mathutils.kdtree import KDTree


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE_BLEND = os.path.join(
    ROOT_DIR,
    "BlenderSource",
    "Characters",
    "C1B-RW-016-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r16.blend",
)
SOURCE_SHA256 = "9b80276e97aa84f3d2a4ef7689b4ebd241f84124494ec8ff0ca51cc119a676ef"
APPROVAL_RECORD = os.path.join(
    ROOT_DIR,
    "BlenderSource",
    "Characters",
    "C1B-RW-016-preview",
    "NeutralApprovalRecord.json",
)
APPROVAL_SHA256 = "1f89295748cd2e5be9bf9d8625f963d602a035a3a3b43e48e05d7f96a0eb9565"
SOURCE_BODY = "C1B_R16_FullBodyCrotchFair7mm_TPoseBody_NoHands"
SOURCE_HEAD = "C1B_R16_RoundFacelessHead"

ASSET_ID = "CHR_MasterCharacter_C1B_Rig"
REVISION = "r18"
VERSION = "0.18.0-local-rig-preview"
OWNER_TASK = "CHR-001"
MODEL_INTEROP_PROFILE = "ModelInteropProfile-ART-001-r02"
RIG_TYPE = "GENERIC"
ARMATURE_NAME = "RIG_C1B_R18_Armature"
ARMATURE_DATA_NAME = "RIG_C1B_R18_ArmatureData"
BODY_NAME = "CHR_C1B_R18_SkinnedBody"
HEAD_NAME = "CHR_C1B_R18_SkinnedHead"

RENDER_RESOLUTION = 1600
WEIGHT_EPSILON = 1.0e-8
WEIGHT_SUM_TOLERANCE = 1.0e-6
REST_POSITION_TOLERANCE = 2.0e-7
MAXIMUM_WEIGHTS_PER_VERTEX = 4
MAXIMUM_ADJACENT_ANGLE = 30.0
EDGE_STRETCH_MINIMUM = 0.50
EDGE_STRETCH_P01_MINIMUM = 0.80
EDGE_STRETCH_P99_MAXIMUM = 1.25
EDGE_STRETCH_MAXIMUM = 1.80
ELBOW_DEPTH_BIAS = 0.25
HIP_DEPTH_BIAS = 0.25
SIDE_EPSILON = 1.0e-6
MIRROR_POSITION_TOLERANCE = 1.0e-6
MIRROR_WEIGHT_TOLERANCE = 1.0e-7
EXPECTED_BIND_POSE_SHA256 = (
    "b52014474bb03864128ec369f79630664a3c0c12d8e3d19387051c911b03590d"
)


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r16 = import_file("c1b_rw016_for_rig", "create_c1b_rw016_body_crotch_fair.py")
r12 = r16.r12
qa = r16.qa


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <output.blend> <render-dir> <report.json>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 3:
        raise RuntimeError("expected output blend, render directory, and report")
    blend_path, render_dir, report_path = map(os.path.abspath, values)
    if not blend_path.lower().endswith(".blend"):
        raise RuntimeError("output must be a .blend file")
    if not report_path.lower().endswith(".json"):
        raise RuntimeError("report must be a .json file")
    protected_outputs = (SOURCE_BLEND, APPROVAL_RECORD, os.path.abspath(__file__))
    if any(paths_resolve_same(blend_path, path) for path in protected_outputs):
        raise RuntimeError("output blend may not overwrite an input or generator")
    if any(paths_resolve_same(report_path, path) for path in protected_outputs):
        raise RuntimeError("report may not overwrite an input or generator")
    if paths_resolve_same(blend_path, report_path):
        raise RuntimeError("blend and report paths must differ")
    return blend_path, render_dir, report_path


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def paths_resolve_same(first, second):
    first = os.path.abspath(first)
    second = os.path.abspath(second)
    if os.path.exists(first) and os.path.exists(second):
        try:
            return os.path.samefile(first, second)
        except OSError:
            pass
    return os.path.realpath(first) == os.path.realpath(second)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def smootherstep(values):
    values = np.clip(values, 0.0, 1.0)
    return values**3 * (values * (values * 6.0 - 15.0) + 10.0)


def vector(values):
    return Vector(tuple(float(value) for value in values))


BONE_SPECS = (
    {
        "name": "Root",
        "parent": None,
        "head": (0.0, 0.0, 0.000),
        "tail": (0.0, 0.0, 0.100),
        "rollReference": (0.0, -1.0, 0.0),
    },
    {
        "name": "Pelvis",
        "parent": "Root",
        "head": (0.0, 0.0, 0.180),
        "tail": (0.0, 0.0, 0.320),
        "rollReference": (0.0, -1.0, 0.0),
    },
    {
        "name": "Spine",
        "parent": "Pelvis",
        "head": (0.0, 0.0, 0.320),
        "tail": (0.0, 0.0, 0.500),
        "rollReference": (0.0, -1.0, 0.0),
    },
    {
        "name": "Chest",
        "parent": "Spine",
        "head": (0.0, 0.0, 0.500),
        "tail": (0.0, 0.0, 0.640),
        "rollReference": (0.0, -1.0, 0.0),
    },
    {
        "name": "Neck",
        "parent": "Chest",
        "head": (0.0, 0.0, 0.640),
        "tail": (0.0, -0.003, 0.710),
        "rollReference": (1.0, 0.0, 0.0),
    },
    {
        "name": "Head",
        "parent": "Neck",
        "head": (0.0, -0.003, 0.710),
        "tail": (0.0, -0.003, 0.960),
        "rollReference": (1.0, 0.0, 0.0),
    },
    {
        "name": "Clavicle_L",
        "parent": "Chest",
        "head": (-0.070, 0.0, 0.620),
        "tail": (-0.180, 0.0, 0.622),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "UpperArm_L",
        "parent": "Clavicle_L",
        "head": (-0.180, 0.0, 0.622),
        "tail": (-0.360, 0.0, 0.635),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "Forearm_L",
        "parent": "UpperArm_L",
        "head": (-0.360, 0.0, 0.635),
        "tail": (-0.500, 0.0, 0.635),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "HandLogical_L",
        "parent": "Forearm_L",
        "head": (-0.500, 0.0, 0.635),
        "tail": (-0.560, 0.0, 0.635),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "Clavicle_R",
        "parent": "Chest",
        "head": (0.070, 0.0, 0.620),
        "tail": (0.180, 0.0, 0.622),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "UpperArm_R",
        "parent": "Clavicle_R",
        "head": (0.180, 0.0, 0.622),
        "tail": (0.360, 0.0, 0.635),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "Forearm_R",
        "parent": "UpperArm_R",
        "head": (0.360, 0.0, 0.635),
        "tail": (0.500, 0.0, 0.635),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "HandLogical_R",
        "parent": "Forearm_R",
        "head": (0.500, 0.0, 0.635),
        "tail": (0.560, 0.0, 0.635),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "Thigh_L",
        "parent": "Pelvis",
        "head": (-0.090, 0.0, 0.180),
        "tail": (-0.098, 0.0, 0.090),
        "rollReference": (0.0, 1.0, 0.0),
    },
    {
        "name": "Calf_L",
        "parent": "Thigh_L",
        "head": (-0.098, 0.0, 0.090),
        "tail": (-0.100, 0.0, 0.025),
        "rollReference": (0.0, 1.0, 0.0),
    },
    {
        "name": "Foot_L",
        "parent": "Calf_L",
        "head": (-0.100, 0.0, 0.025),
        "tail": (-0.100, -0.055, 0.015),
        "rollReference": (0.0, 0.0, 1.0),
    },
    {
        "name": "Thigh_R",
        "parent": "Pelvis",
        "head": (0.090, 0.0, 0.180),
        "tail": (0.098, 0.0, 0.090),
        "rollReference": (0.0, 1.0, 0.0),
    },
    {
        "name": "Calf_R",
        "parent": "Thigh_R",
        "head": (0.098, 0.0, 0.090),
        "tail": (0.100, 0.0, 0.025),
        "rollReference": (0.0, 1.0, 0.0),
    },
    {
        "name": "Foot_R",
        "parent": "Calf_R",
        "head": (0.100, 0.0, 0.025),
        "tail": (0.100, -0.055, 0.015),
        "rollReference": (0.0, 0.0, 1.0),
    },
)

REQUIRED_BONES = tuple(spec["name"] for spec in BONE_SPECS)


def create_armature(collection):
    data = bpy.data.armatures.new(ARMATURE_DATA_NAME)
    armature = bpy.data.objects.new(ARMATURE_NAME, data)
    collection.objects.link(armature)
    armature.matrix_world = Matrix.Identity(4)
    armature.show_in_front = True
    armature.display_type = "WIRE"
    data.display_type = "OCTAHEDRAL"
    armature["asset_id"] = ASSET_ID
    armature["asset_version"] = VERSION
    armature["owner_task"] = OWNER_TASK
    armature["rig_type"] = RIG_TYPE
    armature["avatar_setup"] = "CREATE_FROM_THIS_MODEL"
    armature["model_interop_profile_id"] = MODEL_INTEROP_PROFILE
    armature["maximum_weights_per_vertex"] = MAXIMUM_WEIGHTS_PER_VERTEX
    armature["finger_bone_count"] = 0
    armature["toe_bone_count"] = 0
    armature["helper_bone_count"] = 0
    armature["animation_authored"] = False
    armature["root_motion_authored"] = False

    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.object.mode_set(mode="EDIT")
    created = {}
    for spec in BONE_SPECS:
        bone = data.edit_bones.new(spec["name"])
        bone.head = spec["head"]
        bone.tail = spec["tail"]
        if spec["parent"] is not None:
            bone.parent = created[spec["parent"]]
            bone.use_connect = (
                (bone.head - bone.parent.tail).length <= 1.0e-8
            )
        bone.align_roll(vector(spec["rollReference"]))
        bone.use_deform = spec["name"] != "Root"
        created[spec["name"]] = bone
    bpy.ops.object.mode_set(mode="OBJECT")
    for spec in BONE_SPECS:
        bone = data.bones[spec["name"]]
        bone["logical_bone"] = True
        bone["contract_parent"] = spec["parent"] or ""
    return armature


def arm_segment_weights(distance, y):
    clavicle_to_upper = smootherstep((distance - 0.040) / 0.160)
    elbow_parameter = (distance - 0.200) / 0.320
    elbow_clipped = np.clip(elbow_parameter, 0.0, 1.0)
    elbow_window = 4.0 * elbow_clipped * (1.0 - elbow_clipped)
    upper_to_forearm = smootherstep(
        elbow_parameter
        + (ELBOW_DEPTH_BIAS * y / 0.320) * elbow_window
    )
    forearm_to_hand = smootherstep((distance - 0.520) / 0.050)
    clavicle = 1.0 - clavicle_to_upper
    upper = clavicle_to_upper * (1.0 - upper_to_forearm)
    forearm = (
        clavicle_to_upper * upper_to_forearm * (1.0 - forearm_to_hand)
    )
    hand = clavicle_to_upper * upper_to_forearm * forearm_to_hand
    return clavicle, upper, forearm, hand


def leg_segment_weights(z):
    foot_to_calf = smootherstep((z - 0.005) / 0.035)
    calf_to_thigh = smootherstep(z / 0.180)
    foot = 1.0 - foot_to_calf
    calf = foot_to_calf * (1.0 - calf_to_thigh)
    thigh = foot_to_calf * calf_to_thigh
    return thigh, calf, foot


def mirrored_group_name(name):
    if name.endswith("_L"):
        return name[:-2] + "_R"
    if name.endswith("_R"):
        return name[:-2] + "_L"
    return name


def mirror_vertex_pairs(coordinates):
    tree = KDTree(len(coordinates))
    for index, coordinate in enumerate(coordinates):
        tree.insert(vector(coordinate), index)
    tree.balance()
    pairs = []
    maximum_distance = 0.0
    unmatched = 0
    for index, coordinate in enumerate(coordinates):
        target = Vector((-float(coordinate[0]), float(coordinate[1]), float(coordinate[2])))
        _position, mirror_index, distance = tree.find(target)
        if distance > MIRROR_POSITION_TOLERANCE:
            unmatched += 1
            continue
        maximum_distance = max(maximum_distance, float(distance))
        if index <= mirror_index:
            pairs.append((index, mirror_index))
    return pairs, unmatched, maximum_distance


def enforce_mirrored_weights(coordinates, weights):
    names = tuple(weights)
    name_to_column = {name: index for index, name in enumerate(names)}
    mirror_columns = np.array(
        [name_to_column[mirrored_group_name(name)] for name in names],
        dtype=np.int64,
    )
    matrix = np.column_stack([weights[name] for name in names])
    pairs, unmatched, maximum_distance = mirror_vertex_pairs(coordinates)
    for first, second in pairs:
        average = 0.5 * (matrix[first] + matrix[second, mirror_columns])
        matrix[first] = average
        matrix[second, mirror_columns] = average
    result = {name: matrix[:, column] for column, name in enumerate(names)}
    weight_sum = sum(result.values())
    require(
        float(np.max(np.abs(weight_sum - 1.0))) <= 1.0e-12,
        "mirror enforcement changed weight normalization",
    )
    return result, {
        "pairCount": len(pairs),
        "unmatchedVertexCount": unmatched,
        "maximumPairDistanceMeters": maximum_distance,
    }


def build_body_weights(coordinates):
    x = coordinates[:, 0]
    y = coordinates[:, 1]
    z = coordinates[:, 2]
    absolute_x = np.abs(x)

    arm_support = smootherstep((absolute_x - 0.040) / 0.260) * smootherstep(
        (z - 0.300) / 0.220
    )
    arm_l_support = arm_support * (x < -SIDE_EPSILON)
    arm_r_support = arm_support * (x > SIDE_EPSILON)
    clavicle, upper, forearm, hand = arm_segment_weights(absolute_x, y)

    hip_parameter = (z - 0.030) / 0.270
    hip_clipped = np.clip(hip_parameter, 0.0, 1.0)
    hip_window = 4.0 * hip_clipped * (1.0 - hip_clipped)
    leg_vertical = 1.0 - smootherstep(
        hip_parameter - (HIP_DEPTH_BIAS * y / 0.270) * hip_window
    )
    center_mix = smootherstep((z - 0.080) / 0.055)
    center_gate = (1.0 - center_mix) + center_mix * smootherstep(
        (absolute_x - 0.005) / 0.040
    )
    leg_support = leg_vertical * center_gate
    leg_l_support = leg_support * (x < -SIDE_EPSILON)
    leg_r_support = leg_support * (x > SIDE_EPSILON)
    thigh, calf, foot = leg_segment_weights(z)

    limb_sum = arm_l_support + arm_r_support + leg_l_support + leg_r_support
    require(float(limb_sum.max()) <= 1.0 + 1.0e-12, "limb support overlap")
    torso_support = 1.0 - limb_sum

    pelvis_to_spine = smootherstep((z - 0.280) / 0.080)
    spine_to_chest = smootherstep((z - 0.500) / 0.080)
    chest_to_neck = smootherstep((z - 0.640) / 0.060)
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
    require(
        float(np.max(np.abs(weight_sum - 1.0))) <= 1.0e-12,
        "body weights are not normalized",
    )
    mirrored_weights, mirror_pairing = enforce_mirrored_weights(
        coordinates, weights
    )
    build_body_weights.mirror_pairing = mirror_pairing
    return mirrored_weights


def assign_weights(obj, weights):
    for group in list(obj.vertex_groups):
        obj.vertex_groups.remove(group)
    group_indices = {
        name: obj.vertex_groups.new(name=name).index for name in weights
    }
    mesh = obj.data
    original_coordinates = r12.mesh_coordinates(mesh)
    bm = bmesh.new()
    bm.from_mesh(mesh)
    bm.verts.ensure_lookup_table()
    deform_layer = bm.verts.layers.deform.verify()
    for vertex in bm.verts:
        deform = vertex[deform_layer]
        index = vertex.index
        for name, values in weights.items():
            weight = float(values[index])
            if weight > WEIGHT_EPSILON:
                deform[group_indices[name]] = weight
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()
    require(
        r12.maximum_delta(original_coordinates, r12.mesh_coordinates(mesh)) == 0.0,
        f"weight assignment moved vertices: {obj.name}",
    )


def add_armature_modifier(obj, armature):
    for modifier in list(obj.modifiers):
        obj.modifiers.remove(modifier)
    modifier = obj.modifiers.new("C1BRW018_Armature", "ARMATURE")
    modifier.object = armature
    modifier.use_vertex_groups = True
    modifier.use_bone_envelopes = False
    modifier.use_deform_preserve_volume = False
    return modifier


def clear_custom_properties(datablock):
    for key in list(datablock.keys()):
        del datablock[key]


def prepare_meshes(body, head, armature):
    body.data = body.data.copy()
    body.name = BODY_NAME
    body.data.name = BODY_NAME + "Mesh"
    body.matrix_world = Matrix.Identity(4)
    clear_custom_properties(body)
    clear_custom_properties(body.data)

    head_world = head.matrix_world.copy()
    head.data = head.data.copy()
    head.data.transform(head_world)
    head.matrix_world = Matrix.Identity(4)
    head.name = HEAD_NAME
    head.data.name = HEAD_NAME + "Mesh"
    clear_custom_properties(head)
    clear_custom_properties(head.data)

    body_coordinates = r12.mesh_coordinates(body.data)
    head_coordinates = r12.mesh_coordinates(head.data)
    body_weights = build_body_weights(body_coordinates)
    head_weights = {"Head": np.ones(len(head_coordinates), dtype=np.float64)}
    assign_weights(body, body_weights)
    assign_weights(head, head_weights)

    for obj in (body, head):
        obj.parent = armature
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_local = Matrix.Identity(4)
        obj["rig_review_only"] = True
        obj["source_revision"] = "r16"
        obj["skin_weight_method"] = "DETERMINISTIC_C2_ANATOMICAL_ZONES"
        obj["maximum_weights_per_vertex"] = MAXIMUM_WEIGHTS_PER_VERTEX
        add_armature_modifier(obj, armature)
    return body_coordinates, head_coordinates, body_weights, head_weights


def actual_weight_matrix(obj, ordered_names):
    group_to_column = {
        obj.vertex_groups[name].index: column
        for column, name in enumerate(ordered_names)
    }
    result = np.zeros(
        (len(obj.data.vertices), len(ordered_names)), dtype=np.float64
    )
    for vertex in obj.data.vertices:
        for assignment in vertex.groups:
            column = group_to_column.get(assignment.group)
            if column is not None:
                result[vertex.index, column] = float(assignment.weight)
    return result


def mirror_weight_metrics(coordinates, names, matrix):
    name_to_column = {name: index for index, name in enumerate(names)}
    mirror_columns = np.array(
        [name_to_column[mirrored_group_name(name)] for name in names],
        dtype=np.int64,
    )
    pairs, unmatched, maximum_distance = mirror_vertex_pairs(coordinates)
    maximum = 0.0
    for index, mirror_index in pairs:
        difference = np.max(
            np.abs(matrix[index] - matrix[mirror_index, mirror_columns])
        )
        maximum = max(maximum, float(difference))
    return {
        "positionToleranceMeters": MIRROR_POSITION_TOLERANCE,
        "pairCount": len(pairs),
        "matchedVertexCount": len(coordinates) - unmatched,
        "unmatchedVertexCount": unmatched,
        "maximumPairDistanceMeters": maximum_distance,
        "maximumMirroredWeightDeviation": maximum,
    }


def weight_report(obj, expected_weights, coordinates):
    names = tuple(expected_weights)
    actual = actual_weight_matrix(obj, names)
    expected = np.column_stack([expected_weights[name] for name in names])
    sums = actual.sum(axis=1)
    influence_counts = np.count_nonzero(actual > WEIGHT_EPSILON, axis=1)
    left_columns = [i for i, name in enumerate(names) if name.endswith("_L")]
    right_columns = [i for i, name in enumerate(names) if name.endswith("_R")]
    left_leak = (
        float(actual[coordinates[:, 0] > 0.0][:, left_columns].max())
        if left_columns and np.any(coordinates[:, 0] > 0.0)
        else 0.0
    )
    right_leak = (
        float(actual[coordinates[:, 0] < 0.0][:, right_columns].max())
        if right_columns and np.any(coordinates[:, 0] < 0.0)
        else 0.0
    )
    return {
        "object": obj.name,
        "vertexCount": len(obj.data.vertices),
        "groupNames": list(names),
        "groupNonzeroVertexCounts": {
            name: int(np.count_nonzero(actual[:, column] > WEIGHT_EPSILON))
            for column, name in enumerate(names)
        },
        "finite": bool(np.isfinite(actual).all()),
        "minimumWeight": float(actual.min()),
        "maximumWeight": float(actual.max()),
        "minimumWeightSum": float(sums.min()),
        "maximumWeightSum": float(sums.max()),
        "maximumWeightSumError": float(np.max(np.abs(sums - 1.0))),
        "maximumInfluencesPerVertex": int(influence_counts.max()),
        "unweightedVertexCount": int(np.count_nonzero(sums <= WEIGHT_EPSILON)),
        "maximumAssignmentRoundtripError": float(np.max(np.abs(actual - expected))),
        "maximumLeftBoneLeakOnRightSide": left_leak,
        "maximumRightBoneLeakOnLeftSide": right_leak,
        "mirror": mirror_weight_metrics(coordinates, names, actual),
    }


def skeleton_report(armature):
    records = []
    for spec in BONE_SPECS:
        bone = armature.data.bones[spec["name"]]
        records.append(
            {
                "name": bone.name,
                "parent": bone.parent.name if bone.parent else None,
                "useConnect": bool(bone.use_connect),
                "useDeform": bool(bone.use_deform),
                "headLocal": [float(value) for value in bone.head_local],
                "tailLocal": [float(value) for value in bone.tail_local],
                "matrixLocal": [
                    float(bone.matrix_local[row][column])
                    for row in range(4)
                    for column in range(4)
                ],
            }
        )
    encoded = json.dumps(records, sort_keys=True, separators=(",", ":")).encode(
        "utf-8"
    )
    return {
        "armatureObject": armature.name,
        "armatureDatablock": armature.data.name,
        "rigType": RIG_TYPE,
        "boneCount": len(armature.data.bones),
        "requiredBoneNames": list(REQUIRED_BONES),
        "bones": records,
        "bindPoseSha256": hashlib.sha256(encoded).hexdigest(),
        "rootBoneDeforms": bool(armature.data.bones["Root"].use_deform),
        "fingerBoneCount": 0,
        "toeBoneCount": 0,
        "helperBoneCount": 0,
    }


def evaluated_mesh(obj):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    bpy.context.view_layer.update()
    evaluated = obj.evaluated_get(depsgraph)
    return bpy.data.meshes.new_from_object(
        evaluated, preserve_all_data_layers=False, depsgraph=depsgraph
    )


def edge_stretch_metrics(rest_coordinates, posed_coordinates, edges):
    rest_lengths = np.linalg.norm(
        rest_coordinates[edges[:, 1]] - rest_coordinates[edges[:, 0]], axis=1
    )
    posed_lengths = np.linalg.norm(
        posed_coordinates[edges[:, 1]] - posed_coordinates[edges[:, 0]], axis=1
    )
    ratio = posed_lengths / np.maximum(rest_lengths, 1.0e-12)
    return {
        "minimum": float(ratio.min()),
        "p01": float(np.percentile(ratio, 1.0)),
        "p50": float(np.percentile(ratio, 50.0)),
        "p99": float(np.percentile(ratio, 99.0)),
        "maximum": float(ratio.max()),
    }


def mesh_pose_metrics(obj, rest_coordinates, rest_edges):
    mesh = evaluated_mesh(obj)
    try:
        coordinates = r12.mesh_coordinates(mesh)
        manifold = qa.manifold(mesh)
        folds = qa.folds(mesh)
        overlap = qa.bvh_self_overlap(mesh)
        folds.pop("foldoverEdgesAt90Degrees", None)
        overlap.pop("nonAdjacentOverlapPairs", None)
        return {
            "vertices": len(mesh.vertices),
            "edges": len(mesh.edges),
            "faces": len(mesh.polygons),
            "maximumVertexDisplacementMeters": r12.maximum_delta(
                rest_coordinates, coordinates
            ),
            "manifold": manifold,
            "fold": folds,
            "selfIntersection": overlap,
            "edgeStretchRatio": edge_stretch_metrics(
                rest_coordinates, coordinates, rest_edges
            ),
        }
    finally:
        bpy.data.meshes.remove(mesh)


def reset_pose(armature):
    for pose_bone in armature.pose.bones:
        pose_bone.matrix_basis = Matrix.Identity(4)
        pose_bone.rotation_mode = "QUATERNION"
    bpy.context.view_layer.update()


POSE_TESTS = {
    "Rest": (),
    "ShoulderForward": (
        ("UpperArm_L", "Z", 25.0),
        ("UpperArm_R", "Z", -25.0),
    ),
    "ElbowFlex": (
        ("Forearm_L", "Z", 35.0),
        ("Forearm_R", "Z", -35.0),
    ),
    "HipFlex_L": (("Thigh_L", "X", -25.0),),
    "HipFlex_R": (("Thigh_R", "X", -25.0),),
    "KneeFlex_L": (
        ("Thigh_L", "X", -10.0),
        ("Calf_L", "X", 30.0),
    ),
    "KneeFlex_R": (
        ("Thigh_R", "X", -10.0),
        ("Calf_R", "X", 30.0),
    ),
}


def apply_pose_test(armature, pose_id):
    reset_pose(armature)
    axis_vectors = {
        "X": Vector((1.0, 0.0, 0.0)),
        "Y": Vector((0.0, 1.0, 0.0)),
        "Z": Vector((0.0, 0.0, 1.0)),
    }
    for bone_name, axis, degrees in POSE_TESTS[pose_id]:
        pose_bone = armature.pose.bones[bone_name]
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.rotation_quaternion = Quaternion(
            axis_vectors[axis], math.radians(degrees)
        )
    bpy.context.view_layer.update()


def create_material(name, color, emission_strength=0.0):
    material = bpy.data.materials.new(name)
    material.diffuse_color = (*color, 1.0)
    material.use_nodes = True
    principled = next(
        node
        for node in material.node_tree.nodes
        if node.type == "BSDF_PRINCIPLED"
    )
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Roughness"].default_value = 0.55
    if emission_strength > 0.0:
        principled.inputs["Emission Color"].default_value = (*color, 1.0)
        principled.inputs["Emission Strength"].default_value = emission_strength
    return material


def create_bone_proxy_segment(collection, name, start, end, material):
    direction = end - start
    length = direction.length
    mesh = bpy.data.meshes.new(name + "Mesh")
    bm = bmesh.new()
    bmesh.ops.create_cone(
        bm,
        cap_ends=True,
        cap_tris=False,
        segments=12,
        radius1=0.007,
        radius2=0.007,
        depth=length,
    )
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.matrix_world = (
        Matrix.Translation((start + end) * 0.5)
        @ direction.to_track_quat("Z", "Y").to_matrix().to_4x4()
    )
    obj.data.materials.append(material)
    return obj


def create_bone_proxy_joint(collection, name, position, material):
    mesh = bpy.data.meshes.new(name + "Mesh")
    bm = bmesh.new()
    bmesh.ops.create_icosphere(bm, subdivisions=2, radius=0.012)
    bm.to_mesh(mesh)
    bm.free()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.location = position
    obj.data.materials.append(material)
    return obj


def create_overlay_proxies(collection, view_direction, materials):
    offset = view_direction.normalized() * 0.165
    objects = []
    for spec in BONE_SPECS:
        if spec["name"].endswith("_L"):
            material = materials["L"]
        elif spec["name"].endswith("_R"):
            material = materials["R"]
        else:
            material = materials["Center"]
        start = vector(spec["head"]) + offset
        end = vector(spec["tail"]) + offset
        objects.append(
            create_bone_proxy_segment(
                collection, "TMP_R18_Bone_" + spec["name"], start, end, material
            )
        )
        objects.append(
            create_bone_proxy_joint(
                collection,
                "TMP_R18_Joint_" + spec["name"],
                start,
                material,
            )
        )
    return objects


def look_at(obj, target):
    obj.rotation_euler = (vector(target) - obj.location).to_track_quat(
        "-Z", "Y"
    ).to_euler()


def create_camera(collection, name, location, target, scale):
    data = bpy.data.cameras.new(name + "Data")
    camera = bpy.data.objects.new(name, data)
    collection.objects.link(camera)
    camera.location = location
    look_at(camera, target)
    data.type = "ORTHO"
    data.ortho_scale = scale
    return camera


def create_area_light(collection, name, location, energy, size):
    data = bpy.data.lights.new(name + "Data", "AREA")
    data.energy = energy
    data.shape = "DISK"
    data.size = size
    light = bpy.data.objects.new(name, data)
    collection.objects.link(light)
    light.location = location
    look_at(light, (0.0, 0.0, 0.48))
    return light


def remove_objects(objects):
    for obj in list(objects):
        data = obj.data
        bpy.data.objects.remove(obj, do_unlink=True)
        if data is not None and data.users == 0:
            if isinstance(data, bpy.types.Mesh):
                bpy.data.meshes.remove(data)
            elif isinstance(data, bpy.types.Camera):
                bpy.data.cameras.remove(data)
            elif isinstance(data, bpy.types.Light):
                bpy.data.lights.remove(data)


def configure_render(scene):
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = RENDER_RESOLUTION
    scene.render.resolution_y = RENDER_RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    scene.render.image_settings.compression = 15
    if scene.world is None:
        scene.world = bpy.data.worlds.new("C1BRW018_RigWorld")
    scene.world.use_nodes = True
    background = next(
        node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND"
    )
    background.inputs["Color"].default_value = (0.075, 0.075, 0.075, 1.0)
    background.inputs["Strength"].default_value = 0.7


def render_to(scene, camera, path):
    scene.camera = camera
    scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    require(os.path.isfile(path), f"render missing: {path}")
    require(os.path.getsize(path) > 0, f"render empty: {path}")


def render_qa_bundle(scene, body, head, armature, render_dir):
    os.makedirs(render_dir, exist_ok=True)
    original_visibility = {obj.name: bool(obj.hide_render) for obj in bpy.data.objects}
    existing_lights = [obj for obj in bpy.data.objects if obj.type == "LIGHT"]
    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj not in (body, head):
            obj.hide_render = True
    for light in existing_lights:
        light.hide_render = True
    body.hide_render = False
    head.hide_render = False

    collection = bpy.data.collections.new("TMP_C1BRW018_RigRender")
    scene.collection.children.link(collection)
    cameras = {
        "Front": create_camera(
            collection,
            "TMP_CAM_R18_Front",
            (0.0, -4.0, 0.50),
            (0.0, 0.0, 0.50),
            1.24,
        ),
        "ThreeQuarter": create_camera(
            collection,
            "TMP_CAM_R18_ThreeQuarter",
            (2.9, -2.9, 0.62),
            (0.0, 0.0, 0.50),
            1.32,
        ),
        "Side": create_camera(
            collection,
            "TMP_CAM_R18_Side",
            (-4.0, 0.0, 0.50),
            (0.0, 0.0, 0.50),
            1.22,
        ),
    }
    lights = [
        create_area_light(
            collection, "TMP_LIGHT_R18_Key", (-2.4, -3.0, 3.0), 420.0, 4.0
        ),
        create_area_light(
            collection, "TMP_LIGHT_R18_Fill", (2.8, -1.8, 1.6), 220.0, 3.0
        ),
        create_area_light(
            collection, "TMP_LIGHT_R18_Rim", (0.0, 2.8, 2.2), 300.0, 2.5
        ),
    ]
    materials = {
        "Center": create_material("TMP_MAT_R18_Center", (1.0, 0.72, 0.08), 0.7),
        "L": create_material("TMP_MAT_R18_L", (0.10, 0.42, 1.0), 0.7),
        "R": create_material("TMP_MAT_R18_R", (1.0, 0.16, 0.10), 0.7),
    }
    configure_render(scene)
    outputs = []

    reset_pose(armature)
    rest_filename = f"{ASSET_ID}_{REVISION}_Rest_Neutral_Front.png"
    render_to(scene, cameras["Front"], os.path.join(render_dir, rest_filename))
    outputs.append(rest_filename)

    for view_name in ("Front", "ThreeQuarter"):
        camera = cameras[view_name]
        view_direction = camera.location - Vector((0.0, 0.0, 0.50))
        proxies = create_overlay_proxies(collection, view_direction, materials)
        filename = f"{ASSET_ID}_{REVISION}_RigOverlay_{view_name}.png"
        render_to(scene, camera, os.path.join(render_dir, filename))
        outputs.append(filename)
        remove_objects(proxies)

    render_jobs = (
        ("ShoulderForward", "ThreeQuarter"),
        ("ElbowFlex", "ThreeQuarter"),
        ("HipFlex_L", "Side"),
        ("KneeFlex_L", "Side"),
    )
    for pose_id, view_name in render_jobs:
        apply_pose_test(armature, pose_id)
        filename = f"{ASSET_ID}_{REVISION}_{pose_id}_Neutral_{view_name}.png"
        render_to(
            scene,
            cameras[view_name],
            os.path.join(render_dir, filename),
        )
        outputs.append(filename)
    reset_pose(armature)

    remove_objects([*cameras.values(), *lights])
    for material in materials.values():
        if material.users == 0:
            bpy.data.materials.remove(material)
    scene.collection.children.unlink(collection)
    bpy.data.collections.remove(collection)
    for name, hidden in original_visibility.items():
        obj = bpy.data.objects.get(name)
        if obj is not None:
            obj.hide_render = hidden
    body.hide_render = False
    head.hide_render = False
    return outputs


def purge_unused_datablocks():
    datablock_collections = (
        bpy.data.meshes,
        bpy.data.cameras,
        bpy.data.lights,
        bpy.data.armatures,
        bpy.data.materials,
        bpy.data.curves,
        bpy.data.lattices,
    )
    for datablocks in datablock_collections:
        for datablock in list(datablocks):
            if datablock.users == 0:
                datablocks.remove(datablock)


def clean_final_rig_scene(scene, rig_collection, armature, body, head):
    keep = {armature, body, head}
    for obj in keep:
        if rig_collection not in obj.users_collection:
            rig_collection.objects.link(obj)
        for collection in list(obj.users_collection):
            if collection is not rig_collection:
                collection.objects.unlink(obj)
    for obj in list(bpy.data.objects):
        if obj not in keep:
            bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        if collection is rig_collection:
            continue
        bpy.data.collections.remove(collection)
    if scene.collection.children.get(rig_collection.name) is None:
        scene.collection.children.link(rig_collection)
    purge_unused_datablocks()


def pose_is_rest(armature, tolerance=1.0e-8):
    identity = np.array(Matrix.Identity(4), dtype=np.float64)
    return all(
        float(
            np.max(
                np.abs(
                    np.array(pose_bone.matrix_basis, dtype=np.float64) - identity
                )
            )
        )
        <= tolerance
        for pose_bone in armature.pose.bones
    )


def skeleton_matches_spec(armature, tolerance=1.0e-6):
    if set(armature.data.bones.keys()) != set(REQUIRED_BONES):
        return False
    for spec in BONE_SPECS:
        bone = armature.data.bones[spec["name"]]
        expected_parent = spec["parent"]
        actual_parent = bone.parent.name if bone.parent else None
        if actual_parent != expected_parent:
            return False
        if bone.use_deform != (spec["name"] != "Root"):
            return False
        if (bone.head_local - vector(spec["head"])).length > tolerance:
            return False
        if (bone.tail_local - vector(spec["tail"])).length > tolerance:
            return False
    return True


def rig_inventory(armature, body, head):
    collider_objects = [
        obj.name
        for obj in bpy.data.objects
        if "collider" in obj.name.lower() or obj.get("collider_role") is not None
    ]
    temporary_datablocks = []
    for label, datablocks in (
        ("Object", bpy.data.objects),
        ("Mesh", bpy.data.meshes),
        ("Camera", bpy.data.cameras),
        ("Light", bpy.data.lights),
        ("Material", bpy.data.materials),
        ("Collection", bpy.data.collections),
    ):
        temporary_datablocks.extend(
            f"{label}:{datablock.name}"
            for datablock in datablocks
            if datablock.name.startswith("TMP_")
        )
    return {
        "armatureObjectCount": len(
            [obj for obj in bpy.data.objects if obj.type == "ARMATURE"]
        ),
        "armatureDatablockCount": len(bpy.data.armatures),
        "boneCount": len(armature.data.bones),
        "actionCount": len(bpy.data.actions),
        "shapeKeyDatablockCount": len(bpy.data.shape_keys),
        "latticeObjectCount": len(
            [obj for obj in bpy.data.objects if obj.type == "LATTICE"]
        ),
        "bodyModifierCount": len(body.modifiers),
        "headModifierCount": len(head.modifiers),
        "bodyVertexGroupCount": len(body.vertex_groups),
        "headVertexGroupCount": len(head.vertex_groups),
        "animatedObjectCount": len(
            [obj for obj in bpy.data.objects if obj.animation_data is not None]
        ),
        "rigidBodyObjectCount": len(
            [obj for obj in bpy.data.objects if obj.rigid_body is not None]
        ),
        "rigidBodyConstraintObjectCount": len(
            [obj for obj in bpy.data.objects if obj.rigid_body_constraint is not None]
        ),
        "objectTypeCounts": {
            object_type: len(
                [obj for obj in bpy.data.objects if obj.type == object_type]
            )
            for object_type in sorted({obj.type for obj in bpy.data.objects})
        },
        "collectionCount": len(bpy.data.collections),
        "negativeScaleObjects": [
            obj.name
            for obj in bpy.data.objects
            if any(float(component) < 0.0 for component in obj.scale)
        ],
        "temporaryDatablocks": temporary_datablocks,
        "colliderObjects": collider_objects,
    }


def pose_record_pass(record, is_rest=False):
    displacement_ok = (
        record["maximumVertexDisplacementMeters"] <= REST_POSITION_TOLERANCE
        if is_rest
        else record["maximumVertexDisplacementMeters"] >= 0.005
    )
    return (
        record["vertices"] == 227942
        and record["edges"] == 455880
        and record["faces"] == 227940
        and record["manifold"]["result"] == "PASS"
        and record["fold"]["foldoverEdgeCountAt90Degrees"] == 0
        and record["fold"]["hardEdgeCountAt45Degrees"] == 0
        and record["fold"]["adjacentAngleMaximumDegrees"]
        <= MAXIMUM_ADJACENT_ANGLE
        and record["selfIntersection"]["result"] == "PASS"
        and record["edgeStretchRatio"]["minimum"] >= EDGE_STRETCH_MINIMUM
        and record["edgeStretchRatio"]["p01"] >= EDGE_STRETCH_P01_MINIMUM
        and record["edgeStretchRatio"]["p99"] <= EDGE_STRETCH_P99_MAXIMUM
        and record["edgeStretchRatio"]["maximum"] <= EDGE_STRETCH_MAXIMUM
        and displacement_ok
    )


def matrix_identity_deviation(matrix):
    return float(
        np.max(
            np.abs(
                np.array(matrix, dtype=np.float64)
                - np.array(Matrix.Identity(4), dtype=np.float64)
            )
        )
    )


def verify_saved_rig_file(
    path,
    expected_body_weights,
    expected_head_weights,
    expected_body_coordinates,
    expected_head_coordinates,
):
    bpy.ops.wm.open_mainfile(filepath=path)
    errors = []

    def check(condition, code):
        if not condition:
            errors.append(code)

    scene = bpy.context.scene
    armature = bpy.data.objects.get(ARMATURE_NAME)
    body = bpy.data.objects.get(BODY_NAME)
    head = bpy.data.objects.get(HEAD_NAME)
    check(armature is not None and armature.type == "ARMATURE", "ARMATURE_OBJECT")
    check(body is not None and body.type == "MESH", "BODY_OBJECT")
    check(head is not None and head.type == "MESH", "HEAD_OBJECT")
    if armature is None or body is None or head is None:
        return {"result": "FAIL", "errors": errors}

    skeleton = skeleton_report(armature)
    body_weight = weight_report(
        body, expected_body_weights, expected_body_coordinates
    )
    head_weight = weight_report(
        head, expected_head_weights, expected_head_coordinates
    )
    inventory = rig_inventory(armature, body, head)
    modifiers_valid = all(
        len(obj.modifiers) == 1
        and obj.modifiers[0].type == "ARMATURE"
        and obj.modifiers[0].object is armature
        and obj.modifiers[0].use_vertex_groups
        and not obj.modifiers[0].use_bone_envelopes
        and not obj.modifiers[0].use_deform_preserve_volume
        for obj in (body, head)
    )
    check(skeleton_matches_spec(armature), "SKELETON_SPEC")
    check(
        skeleton["bindPoseSha256"] == EXPECTED_BIND_POSE_SHA256,
        "BIND_POSE_HASH",
    )
    check(pose_is_rest(armature), "REST_POSE")
    check(modifiers_valid, "LINEAR_ARMATURE_MODIFIERS")
    check(
        r12.maximum_delta(
            expected_body_coordinates, r12.mesh_coordinates(body.data)
        )
        == 0.0,
        "BODY_SOURCE_COORDINATES",
    )
    check(
        r12.maximum_delta(
            expected_head_coordinates, r12.mesh_coordinates(head.data)
        )
        == 0.0,
        "HEAD_SOURCE_COORDINATES",
    )
    check(
        body_weight["maximumWeightSumError"] <= WEIGHT_SUM_TOLERANCE
        and body_weight["maximumInfluencesPerVertex"]
        <= MAXIMUM_WEIGHTS_PER_VERTEX
        and body_weight["unweightedVertexCount"] == 0
        and body_weight["maximumAssignmentRoundtripError"]
        <= WEIGHT_SUM_TOLERANCE
        and body_weight["mirror"]["unmatchedVertexCount"] == 0
        and body_weight["mirror"]["maximumMirroredWeightDeviation"]
        <= MIRROR_WEIGHT_TOLERANCE
        and body_weight["maximumLeftBoneLeakOnRightSide"] <= WEIGHT_EPSILON
        and body_weight["maximumRightBoneLeakOnLeftSide"] <= WEIGHT_EPSILON,
        "BODY_WEIGHTS",
    )
    check(
        head_weight["maximumWeightSumError"] <= WEIGHT_SUM_TOLERANCE
        and head_weight["maximumInfluencesPerVertex"] == 1
        and head_weight["minimumWeight"] >= 1.0 - WEIGHT_SUM_TOLERANCE,
        "HEAD_WEIGHTS",
    )
    check(
        inventory["objectTypeCounts"] == {"ARMATURE": 1, "MESH": 2}
        and inventory["collectionCount"] == 1
        and inventory["actionCount"] == 0
        and inventory["rigidBodyObjectCount"] == 0
        and inventory["rigidBodyConstraintObjectCount"] == 0
        and not inventory["temporaryDatablocks"]
        and not inventory["colliderObjects"],
        "FINAL_INVENTORY",
    )
    transform_deviation = max(
        matrix_identity_deviation(armature.matrix_world),
        matrix_identity_deviation(body.matrix_world),
        matrix_identity_deviation(head.matrix_world),
        matrix_identity_deviation(body.matrix_local),
        matrix_identity_deviation(head.matrix_local),
    )
    check(transform_deviation <= 1.0e-7, "OBJECT_TRANSFORMS")
    check(
        scene.get("asset_id") == ASSET_ID
        and scene.get("asset_version") == VERSION
        and scene.get("rig_type") == RIG_TYPE
        and scene.get("animation_authored") is False,
        "SCENE_METADATA",
    )
    legacy_keys = sorted(
        key
        for key in scene.keys()
        if "qa_report" in key.lower()
        or "rejected" in key.lower()
        or key.startswith("preview_")
        or key.startswith("smooth_")
    )
    check(not legacy_keys, "LEGACY_SCENE_METADATA")
    check(
        scene.unit_settings.system == "METRIC"
        and abs(float(scene.unit_settings.scale_length) - 1.0) <= 1.0e-12,
        "UNIT_SCALE",
    )
    return {
        "result": "PASS" if not errors else "FAIL",
        "errors": errors,
        "bindPoseSha256": skeleton["bindPoseSha256"],
        "restPoseConfirmed": pose_is_rest(armature),
        "linearSkinningConfirmed": modifiers_valid,
        "maximumObjectTransformDeviation": transform_deviation,
        "bodyWeightMaximumSumError": body_weight["maximumWeightSumError"],
        "bodyMaximumInfluencesPerVertex": body_weight[
            "maximumInfluencesPerVertex"
        ],
        "bodyMirrorWeightMaximumDeviation": body_weight["mirror"][
            "maximumMirroredWeightDeviation"
        ],
        "bodyMirrorUnmatchedVertexCount": body_weight["mirror"][
            "unmatchedVertexCount"
        ],
        "headWeightMinimum": head_weight["minimumWeight"],
        "inventory": inventory,
        "legacySceneMetadataKeys": legacy_keys,
    }


def main():
    blend_path, render_dir, report_path = parse_args()
    for path in (SOURCE_BLEND, APPROVAL_RECORD):
        require(os.path.isfile(path), f"missing input: {path}")
    require(file_sha256(SOURCE_BLEND) == SOURCE_SHA256, "r16 source hash mismatch")
    require(
        file_sha256(APPROVAL_RECORD) == APPROVAL_SHA256,
        "r16 approval record hash mismatch",
    )
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    partial_blend_path = blend_path + ".partial.blend"
    for generated_path in (blend_path, partial_blend_path):
        if os.path.isfile(generated_path):
            os.remove(generated_path)
    render_prefix = f"{ASSET_ID}_{REVISION}_"
    for name in os.listdir(render_dir):
        if name.startswith(render_prefix) and name.lower().endswith(".png"):
            os.remove(os.path.join(render_dir, name))

    source_hash_before = file_sha256(SOURCE_BLEND)
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    scene = bpy.context.scene
    scene.name = "C1BRW018_RigReview"
    require(scene.unit_settings.system == "METRIC", "r16 unit system is not Metric")
    source_body = bpy.data.objects[SOURCE_BODY]
    source_head = bpy.data.objects[SOURCE_HEAD]
    source_body_signature = r12.mesh_signature(source_body)
    source_head_signature = r12.mesh_signature(source_head)
    source_body_matrix = source_body.matrix_world.copy()
    source_head_matrix = source_head.matrix_world.copy()

    rig_collection = bpy.data.collections.new("C1BRW018_Rig")
    scene.collection.children.link(rig_collection)
    armature = create_armature(rig_collection)
    body_coordinates, head_coordinates, body_weights, head_weights = prepare_meshes(
        source_body, source_head, armature
    )
    body = bpy.data.objects[BODY_NAME]
    head = bpy.data.objects[HEAD_NAME]
    body_edges = r12.mesh_edges(body.data)

    body_weight_report = weight_report(body, body_weights, body_coordinates)
    head_weight_report = weight_report(head, head_weights, head_coordinates)
    skeleton = skeleton_report(armature)

    pose_records = {}
    for pose_id in POSE_TESTS:
        apply_pose_test(armature, pose_id)
        pose_records[pose_id] = mesh_pose_metrics(
            body, body_coordinates, body_edges
        )
        pose_records[pose_id]["result"] = (
            "PASS"
            if pose_record_pass(pose_records[pose_id], pose_id == "Rest")
            else "FAIL"
        )
    reset_pose(armature)

    render_files = render_qa_bundle(
        scene, body, head, armature, render_dir
    )
    missing_renders = [
        name
        for name in render_files
        if not os.path.isfile(os.path.join(render_dir, name))
        or os.path.getsize(os.path.join(render_dir, name)) == 0
    ]

    rest_head_mesh = evaluated_mesh(head)
    try:
        rest_head_coordinates = r12.mesh_coordinates(rest_head_mesh)
        head_rest_delta = r12.maximum_delta(
            head_coordinates, rest_head_coordinates
        )
        evaluated_head_signature = {
            "vertices": len(rest_head_mesh.vertices),
            "edges": len(rest_head_mesh.edges),
            "faces": len(rest_head_mesh.polygons),
        }
    finally:
        bpy.data.meshes.remove(rest_head_mesh)

    reset_pose(armature)
    saved_rest_pose_confirmed = pose_is_rest(armature)
    clean_final_rig_scene(scene, rig_collection, armature, body, head)
    inventory = rig_inventory(armature, body, head)
    source_hash_after = file_sha256(SOURCE_BLEND)
    hierarchy_matches = all(
        (armature.data.bones[spec["name"]].parent.name
         if armature.data.bones[spec["name"]].parent else None)
        == spec["parent"]
        for spec in BONE_SPECS
    )
    modifiers_valid = all(
        len(obj.modifiers) == 1
        and obj.modifiers[0].type == "ARMATURE"
        and obj.modifiers[0].object is armature
        and obj.modifiers[0].use_vertex_groups
        and not obj.modifiers[0].use_bone_envelopes
        and not obj.modifiers[0].use_deform_preserve_volume
        for obj in (body, head)
    )
    body_weights_pass = (
        body_weight_report["finite"]
        and body_weight_report["minimumWeight"] >= 0.0
        and body_weight_report["maximumWeightSumError"] <= WEIGHT_SUM_TOLERANCE
        and body_weight_report["maximumInfluencesPerVertex"]
        <= MAXIMUM_WEIGHTS_PER_VERTEX
        and body_weight_report["unweightedVertexCount"] == 0
        and body_weight_report["maximumLeftBoneLeakOnRightSide"] <= WEIGHT_EPSILON
        and body_weight_report["maximumRightBoneLeakOnLeftSide"] <= WEIGHT_EPSILON
        and body_weight_report["maximumAssignmentRoundtripError"]
        <= WEIGHT_SUM_TOLERANCE
        and body_weight_report["mirror"]["unmatchedVertexCount"] == 0
        and body_weight_report["mirror"]["maximumMirroredWeightDeviation"]
        <= MIRROR_WEIGHT_TOLERANCE
    )
    head_weights_pass = (
        head_weight_report["finite"]
        and head_weight_report["minimumWeight"] >= 1.0 - WEIGHT_SUM_TOLERANCE
        and head_weight_report["maximumWeight"] <= 1.0 + WEIGHT_SUM_TOLERANCE
        and head_weight_report["maximumWeightSumError"] <= WEIGHT_SUM_TOLERANCE
        and head_weight_report["maximumInfluencesPerVertex"] == 1
        and head_weight_report["unweightedVertexCount"] == 0
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
        and source_body_signature
        == {"vertices": 227942, "edges": 455880, "faces": 227940}
        and source_head_signature
        == {"vertices": 6050, "edges": 12192, "faces": 6144}
        and source_body_matrix == Matrix.Identity(4)
        and abs(float(scene.unit_settings.scale_length) - 1.0) <= 1.0e-12
        and len(REQUIRED_BONES) == 20
        and set(armature.data.bones.keys()) == set(REQUIRED_BONES)
        and hierarchy_matches
        and skeleton_matches_spec(armature)
        and skeleton["bindPoseSha256"] == EXPECTED_BIND_POSE_SHA256
        and not skeleton["rootBoneDeforms"]
        and saved_rest_pose_confirmed
        and body_weights_pass
        and head_weights_pass
        and modifiers_valid
        and all(record["result"] == "PASS" for record in pose_records.values())
        and head_rest_delta <= REST_POSITION_TOLERANCE
        and evaluated_head_signature
        == {"vertices": 6050, "edges": 12192, "faces": 6144}
        and inventory_pass
        and len(render_files) == 7
        and not missing_renders
    )

    report = {
        "assetId": ASSET_ID,
        "assetVersion": VERSION,
        "revision": REVISION,
        "ownerTask": OWNER_TASK,
        "candidateStatus": "RIG_REVIEW",
        "source": {
            "revision": "r16",
            "path": SOURCE_BLEND,
            "sha256Before": source_hash_before,
            "sha256After": source_hash_after,
            "unchanged": source_hash_before == source_hash_after,
            "approvalRecordPath": APPROVAL_RECORD,
            "approvalRecordSha256": APPROVAL_SHA256,
            "bodySignature": source_body_signature,
            "headSignature": source_head_signature,
            "headSourceMatrix": [
                float(source_head_matrix[row][column])
                for row in range(4)
                for column in range(4)
            ],
        },
        "contract": {
            "modelInteropProfileId": MODEL_INTEROP_PROFILE,
            "rigType": RIG_TYPE,
            "avatarSetup": "CreateFromThisModel",
            "blenderAxes": "+X Right / -Y Forward / +Z Up",
            "unitScaleMeters": float(scene.unit_settings.scale_length),
            "maximumWeightsPerVertex": MAXIMUM_WEIGHTS_PER_VERTEX,
            "fingerBoneCount": 0,
            "toeBoneCount": 0,
            "extraLeafBoneCount": 0,
            "animationClipCount": 0,
            "rootMotionAuthored": False,
        },
        "skeleton": skeleton,
        "weights": {
            "method": "DETERMINISTIC_C2_ANATOMICAL_ZONES",
            "automaticBoneHeatUsed": False,
            "mirrorPairing": build_body_weights.mirror_pairing,
            "body": body_weight_report,
            "head": head_weight_report,
        },
        "modifiers": {
            "body": body.modifiers[0].name,
            "head": head.modifiers[0].name,
            "skinningMode": "LINEAR_BLEND_UNITY_PARITY",
            "preserveVolume": False,
            "valid": modifiers_valid,
        },
        "deformationQA": {
            "temporaryPoseOnly": True,
            "actionDatablocksCreated": 0,
            "savedInRestPose": saved_rest_pose_confirmed,
            "maximumAdjacentAngleDegrees": MAXIMUM_ADJACENT_ANGLE,
            "edgeStretchRatioLimits": {
                "minimum": EDGE_STRETCH_MINIMUM,
                "p01Minimum": EDGE_STRETCH_P01_MINIMUM,
                "p99Maximum": EDGE_STRETCH_P99_MAXIMUM,
                "maximum": EDGE_STRETCH_MAXIMUM,
            },
            "poses": pose_records,
            "headRestMaximumDeltaMeters": head_rest_delta,
        },
        "finalInventory": inventory,
        "renderFiles": render_files,
        "missingRenderFiles": missing_renders,
        "userRigStartAuthorized": True,
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
    }

    if technical_pass:
        clear_custom_properties(scene)
        scene["asset_id"] = ASSET_ID
        scene["asset_version"] = VERSION
        scene["revision"] = REVISION
        scene["owner_task"] = OWNER_TASK
        scene["source_revision"] = "r16"
        scene["source_sha256"] = SOURCE_SHA256
        scene["rig_type"] = RIG_TYPE
        scene["required_bones_json"] = json.dumps(REQUIRED_BONES)
        scene["animation_authored"] = False
        scene["user_rig_approval_recorded"] = False
        reset_pose(armature)
        bpy.ops.object.select_all(action="DESELECT")
        armature.select_set(True)
        bpy.context.view_layer.objects.active = armature
        bpy.ops.wm.save_as_mainfile(
            filepath=partial_blend_path, check_existing=False
        )
        saved_verification = verify_saved_rig_file(
            partial_blend_path,
            body_weights,
            head_weights,
            body_coordinates,
            head_coordinates,
        )
        report["savedFileVerification"] = saved_verification
        technical_pass = saved_verification["result"] == "PASS"
        report["technicalResult"] = "PASS" if technical_pass else "FAIL"
        if technical_pass:
            os.replace(partial_blend_path, blend_path)
            report["output"] = {
                "blendPath": blend_path,
                "blendBytes": os.path.getsize(blend_path),
                "blendSha256": file_sha256(blend_path),
            }
        else:
            if os.path.isfile(partial_blend_path):
                os.remove(partial_blend_path)
            report["output"] = None
    else:
        report["output"] = None

    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    if not technical_pass:
        raise RuntimeError(
            "r18 rig QA failed: " + json.dumps(report, separators=(",", ":"))
        )
    print("R18_RIG_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R18_RIG_RESULT=PASS")


if __name__ == "__main__":
    main()
