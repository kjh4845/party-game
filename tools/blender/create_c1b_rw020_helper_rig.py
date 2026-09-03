#!/usr/bin/env python3

"""Create the r20 Generic rig with two deterministic hip helper bones.

The approved r16 geometry is copied byte-exactly.  The logical Generic-20
hierarchy remains unchanged and two deforming Pelvis siblings, HipDeform_L/R,
split the H-field Pelvis/Thigh blend.  Review poses move only the logical bones;
the saved file contains eight explicit parent-relative helper driver curves,
but no actions, constraints, or authored clips.
"""

import hashlib
import importlib.util
import json
import math
import os
import shutil
import struct
import sys
import tempfile

import bpy
import numpy as np
from mathutils import Matrix, Quaternion, Vector


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.abspath(os.path.join(HERE, "..", ".."))
R18_GENERATOR = os.path.join(HERE, "create_c1b_rw018_rig.py")
R18_GENERATOR_SHA256 = (
    "e6de3d1974bf11fdb45d7f207c2d1641812e29e00a73b56727fb838bb0fcca02"
)
R19_GENERATOR = os.path.join(HERE, "create_c1b_rw019_weight_rig.py")
R19_GENERATOR_SHA256 = (
    "47a986553108867f948b4916dbf10349974eaf814bc5f87da535815d73270feb"
)
R19_BLEND = os.path.join(
    ROOT_DIR,
    "BlenderSource",
    "Characters",
    "C1B-RW-019-rig-preview",
    "CHR_MasterCharacter_C1B_Rig_r19.blend",
)
R19_REPORT = os.path.join(
    ROOT_DIR,
    "BlenderSource",
    "Characters",
    "C1B-RW-019-rig-preview",
    "RigQAReport.json",
)

ASSET_ID = "CHR_MasterCharacter_C1B_Rig"
REVISION = "r20"
VERSION = "0.20.0-helper-rig-preview"
OWNER_TASK = "CHR-001"
ARMATURE_NAME = "RIG_C1B_R20_Armature"
ARMATURE_DATA_NAME = "RIG_C1B_R20_ArmatureData"
BODY_NAME = "CHR_C1B_R20_SkinnedBody"
HEAD_NAME = "CHR_C1B_R20_SkinnedHead"
RIG_COLLECTION_NAME = "C1BRW020_HelperRig"

HELPER_NAMES = ("HipDeform_L", "HipDeform_R")
HELPER_FRACTION = 0.25
HELPER_GATE_START_Z = 0.050
HELPER_GATE_END_Z = 0.100
HELPER_TRANSLATION_AT_NEGATIVE_25 = (0.0, -0.0025, 0.0005)
HELPER_POSE_MATRIX_TOLERANCE = 2.0e-7
NOTCH_CORE_Z = (0.130, 0.260)
HIP25_NOTCH_REDUCTION_MINIMUM = 0.0010
HIP45_NOTCH_REDUCTION_MINIMUM = 0.0005

HIP45_LIMITS = {
    "minimum": 0.48,
    "p01Minimum": 0.75,
    "p99Maximum": 1.32,
    "maximum": 1.80,
    "maximumAdjacentAngleDegrees": 30.0,
}
EXPECTED_BIND_POSE_SHA256 = (
    "99fa68157c387b424bc532cc2766e504230aee2897dd6a2ed07a1ef73ce6be96"
)

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
    "HipFlex_L_45": (("Thigh_L", "X", -45.0),),
    "HipFlex_R_45": (("Thigh_R", "X", -45.0),),
    "PelvisYaw_HipFlex_L": (
        ("Pelvis", "Z", 20.0),
        ("Thigh_L", "X", -25.0),
    ),
    "PelvisYaw_HipFlex_R": (
        ("Pelvis", "Z", -20.0),
        ("Thigh_R", "X", -25.0),
    ),
    "RootYaw_HipFlex_L": (
        ("Root", "Z", 20.0),
        ("Thigh_L", "X", -25.0),
    ),
    "RootYaw_HipFlex_R": (
        ("Root", "Z", -20.0),
        ("Thigh_R", "X", -25.0),
    ),
}
EXPECTED_POSE_IDS = tuple(POSE_TESTS)
EXPECTED_RENDER_FILENAMES = (
    f"{ASSET_ID}_{REVISION}_Rest_Neutral_Front.png",
    f"{ASSET_ID}_{REVISION}_RigOverlay_Front.png",
    f"{ASSET_ID}_{REVISION}_RigOverlay_ThreeQuarter.png",
    f"{ASSET_ID}_{REVISION}_ShoulderForward_Neutral_ThreeQuarter.png",
    f"{ASSET_ID}_{REVISION}_ElbowFlex_Neutral_ThreeQuarter.png",
    f"{ASSET_ID}_{REVISION}_HipFlex_L_Neutral_Side.png",
    f"{ASSET_ID}_{REVISION}_KneeFlex_L_Neutral_Side.png",
    f"{ASSET_ID}_{REVISION}_HipFlex_L_45_Neutral_Side.png",
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


def import_file(name, path):
    spec = importlib.util.spec_from_file_location(name, path)
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


for pinned_path, pinned_sha, label in (
    (R18_GENERATOR, R18_GENERATOR_SHA256, "r18 generator"),
    (R19_GENERATOR, R19_GENERATOR_SHA256, "r19 generator"),
):
    require(os.path.isfile(pinned_path), f"missing {label}")
    require(file_sha256(pinned_path) == pinned_sha, f"{label} hash mismatch")

r19 = import_file("c1b_rw019_weight_base_for_r20", R19_GENERATOR)
r18 = r19.r18
SOURCE_BLEND = r18.SOURCE_BLEND
SOURCE_SHA256 = r18.SOURCE_SHA256
APPROVAL_RECORD = r18.APPROVAL_RECORD
APPROVAL_SHA256 = r18.APPROVAL_SHA256
R18_BLEND = r19.R18_BLEND
R18_BLEND_SHA256 = r19.R18_BLEND_SHA256

LOGICAL_BONE_SPECS = tuple(dict(spec) for spec in r18.BONE_SPECS)
LOGICAL_BONES = tuple(spec["name"] for spec in LOGICAL_BONE_SPECS)
THIGH_SPECS = {
    side: next(spec for spec in LOGICAL_BONE_SPECS if spec["name"] == f"Thigh_{side}")
    for side in ("L", "R")
}
HELPER_BONE_SPECS = tuple(
    {
        "name": f"HipDeform_{side}",
        "parent": "Pelvis",
        "head": THIGH_SPECS[side]["head"],
        "tail": THIGH_SPECS[side]["tail"],
        "rollReference": THIGH_SPECS[side]["rollReference"],
    }
    for side in ("L", "R")
)
ALL_BONE_SPECS = LOGICAL_BONE_SPECS + HELPER_BONE_SPECS
ALL_BONES = tuple(spec["name"] for spec in ALL_BONE_SPECS)


def configure_base_module():
    r18.ASSET_ID = ASSET_ID
    r18.REVISION = REVISION
    r18.VERSION = VERSION
    r18.OWNER_TASK = OWNER_TASK
    r18.ARMATURE_NAME = ARMATURE_NAME
    r18.ARMATURE_DATA_NAME = ARMATURE_DATA_NAME
    r18.BODY_NAME = BODY_NAME
    r18.HEAD_NAME = HEAD_NAME
    r18.BONE_SPECS = ALL_BONE_SPECS
    r18.REQUIRED_BONES = ALL_BONES
    r18.POSE_TESTS = POSE_TESTS
    r18.apply_pose_test = apply_pose_test
    r18.reset_pose = reset_pose
    r18.__file__ = os.path.abspath(__file__)


def parse_args():
    require("--" in sys.argv, "expected -- <output.blend> <Renders> <report.json>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    require(len(values) == 3, "expected output blend, render directory, report")
    blend_path, render_dir, report_path = map(os.path.abspath, values)
    require(blend_path.lower().endswith(".blend"), "output must be .blend")
    require(report_path.lower().endswith(".json"), "report must be .json")
    require(os.path.basename(render_dir) == "Renders", "render dir must be Renders")
    parents = {
        os.path.realpath(os.path.dirname(blend_path)),
        os.path.realpath(os.path.dirname(render_dir)),
        os.path.realpath(os.path.dirname(report_path)),
    }
    require(len(parents) == 1, "all outputs must share a package directory")
    protected = (
        SOURCE_BLEND,
        APPROVAL_RECORD,
        R18_GENERATOR,
        R19_GENERATOR,
        R18_BLEND,
        R19_BLEND,
        R19_REPORT,
        os.path.abspath(__file__),
    )
    for output in (blend_path, render_dir, report_path):
        require(
            all(not r18.paths_resolve_same(output, path) for path in protected),
            "output may not overwrite an input",
        )
    require(
        not r18.paths_resolve_same(blend_path, report_path)
        and not r18.paths_resolve_same(render_dir, blend_path)
        and not r18.paths_resolve_same(render_dir, report_path),
        "output paths must be mutually distinct",
    )
    output_parent = os.path.realpath(os.path.dirname(blend_path))
    protected_packages = (
        os.path.dirname(SOURCE_BLEND),
        os.path.dirname(R18_BLEND),
        os.path.dirname(R19_BLEND),
    )
    require(
        all(
            not r18.paths_resolve_same(output_parent, package)
            for package in protected_packages
        ),
        "r20 outputs may not use an upstream package directory",
    )
    return blend_path, render_dir, report_path


def helper_drive_contract():
    return {
        "mode": "BLENDER_DRIVER_AND_BAKE_OR_RUNTIME",
        "logicalThighRotationFraction": 1.0,
        "helperParentRelativeRotation": (
            "slerp(identity, thigh.matrix_basis rotation, 0.25)"
        ),
        "helperRotationFraction": HELPER_FRACTION,
        "helperScale": [1.0, 1.0, 1.0],
        "signedTranslationFormula": (
            "translation = (thighDegrees / -25) * (0,-0.0025,+0.0005) meters"
        ),
        "translationFrame": (
            "armature-rest vector converted through the inverse helper rest "
            "basis into parent-relative PoseBone.location"
        ),
        "parentInheritance": "Pelvis and Root inherited exactly once",
        "samples": {
            "-25": [0.0, -0.0025, 0.0005],
            "-45": [0.0, -0.0045, 0.0009],
        },
        "constraintsEmbedded": False,
        "driversEmbedded": True,
        "actionsEmbedded": False,
        "driverFCurveCount": 8,
        "driverInput": "corresponding Thigh local-space ROT_X",
        "unityExportRequiresBakedHelperCurves": True,
        "runtimeDriveOptional": True,
    }


def bernstein_parameter(moment, fraction):
    coefficient = 1.0 - 2.0 * fraction
    if abs(coefficient) <= 1.0e-12:
        return moment.copy()
    discriminant = np.maximum(
        fraction * fraction + coefficient * moment,
        0.0,
    )
    return moment / np.maximum(
        fraction + np.sqrt(discriminant),
        1.0e-15,
    )


def build_body_weights(coordinates):
    base = r19.build_body_weights(coordinates)
    result = {name: values.copy() for name, values in base.items()}
    result["HipDeform_L"] = np.zeros(len(coordinates), dtype=np.float64)
    result["HipDeform_R"] = np.zeros(len(coordinates), dtype=np.float64)
    z = coordinates[:, 2]
    gate = r18.smootherstep(
        (z - HELPER_GATE_START_Z)
        / (HELPER_GATE_END_Z - HELPER_GATE_START_Z)
    )
    for side in ("L", "R"):
        side_mask = (
            coordinates[:, 0] < -r18.SIDE_EPSILON
            if side == "L"
            else coordinates[:, 0] > r18.SIDE_EPSILON
        )
        pelvis = result["Pelvis"].copy()
        thigh_name = f"Thigh_{side}"
        thigh = result[thigh_name].copy()
        pair = pelvis + thigh
        active = side_mask & (gate > 0.0) & (pair > 1.0e-15)
        moment = np.zeros(len(coordinates), dtype=np.float64)
        moment[active] = thigh[active] / pair[active]
        q = np.clip(
            bernstein_parameter(moment, HELPER_FRACTION),
            0.0,
            1.0,
        )
        pelvis_stage = (1.0 - q) ** 2
        helper_stage = 2.0 * q * (1.0 - q)
        thigh_stage = q**2
        blend = gate * active
        result["Pelvis"] = pelvis * (1.0 - blend) + pair * pelvis_stage * blend
        result[thigh_name] = thigh * (1.0 - blend) + pair * thigh_stage * blend
        result[f"HipDeform_{side}"] = pair * helper_stage * blend
    sums = sum(result.values())
    require(
        float(np.max(np.abs(sums - 1.0))) <= 1.0e-12,
        "r20 helper weights are not normalized",
    )
    matrix = np.column_stack(list(result.values()))
    require(
        int(np.max(np.count_nonzero(matrix > r18.WEIGHT_EPSILON, axis=1))) <= 4,
        "r20 helper weights exceed four influences",
    )
    build_body_weights.mirror_pairing = r19.build_body_weights.mirror_pairing
    return result


def create_armature(collection):
    armature = r18.create_armature(collection)
    armature["logical_bone_count"] = len(LOGICAL_BONES)
    armature["helper_bone_count"] = len(HELPER_NAMES)
    armature["helper_bone_names_json"] = json.dumps(HELPER_NAMES)
    armature["helper_drive_contract_json"] = json.dumps(helper_drive_contract())
    armature["runtime_helper_drive_required"] = False
    armature["runtime_helper_drive_optional"] = True
    armature["drivers_embedded"] = True
    armature["unity_helper_curve_bake_required"] = True
    armature["animation_authored"] = False
    armature["root_motion_authored"] = False
    for name in HELPER_NAMES:
        bone = armature.data.bones[name]
        side = name.rsplit("_", 1)[-1]
        bone["logical_bone"] = False
        bone["helper_bone"] = True
        bone["runtime_driven"] = False
        bone["runtime_drive_optional"] = True
        bone["driver_embedded"] = True
        bone["contract_parent"] = "Pelvis"
        bone["source_logical_bone"] = f"Thigh_{side}"
        bone["rotation_fraction"] = HELPER_FRACTION
        bone["signed_translation_model_json"] = json.dumps(
            helper_drive_contract()["samples"]
        )
    for name in LOGICAL_BONES:
        armature.data.bones[name]["logical_bone"] = True
        armature.data.bones[name]["helper_bone"] = False
    add_helper_drivers(armature)
    return armature


def prepare_meshes(body, head, armature):
    body.data = body.data.copy()
    body.name = BODY_NAME
    body.data.name = BODY_NAME + "Mesh"
    body.matrix_world = Matrix.Identity(4)
    r18.clear_custom_properties(body)
    r18.clear_custom_properties(body.data)

    head_world = head.matrix_world.copy()
    head.data = head.data.copy()
    head.data.transform(head_world)
    head.matrix_world = Matrix.Identity(4)
    head.name = HEAD_NAME
    head.data.name = HEAD_NAME + "Mesh"
    r18.clear_custom_properties(head)
    r18.clear_custom_properties(head.data)

    body_coordinates = r18.r12.mesh_coordinates(body.data)
    head_coordinates = r18.r12.mesh_coordinates(head.data)
    body_weights = build_body_weights(body_coordinates)
    head_weights = {"Head": np.ones(len(head_coordinates), dtype=np.float64)}
    r18.assign_weights(body, body_weights)
    r18.assign_weights(head, head_weights)
    for obj in (body, head):
        obj.parent = armature
        obj.matrix_parent_inverse = Matrix.Identity(4)
        obj.matrix_local = Matrix.Identity(4)
        obj["rig_review_only"] = True
        obj["source_revision"] = "r16"
        obj["skin_weight_method"] = "H_PLUS_BERNSTEIN_HIP_HELPERS"
        obj["maximum_weights_per_vertex"] = 4
        modifier = r18.add_armature_modifier(obj, armature)
        modifier.name = "C1BRW020_Armature"
    return body_coordinates, head_coordinates, body_weights, head_weights


def logical_rotation_degrees(pose_id, side):
    target = f"Thigh_{side}"
    for bone_name, _axis, degrees in POSE_TESTS[pose_id]:
        if bone_name == target:
            return float(degrees)
    return None


def helper_local_translation(armature, side, thigh_degrees):
    helper = armature.pose.bones[f"HipDeform_{side}"]
    parent_space_translation = (
        Vector(HELPER_TRANSLATION_AT_NEGATIVE_25)
        * (float(thigh_degrees) / -25.0)
    )
    return helper.bone.matrix_local.to_3x3().inverted() @ parent_space_translation


def helper_driver_specs(armature):
    records = []
    negative_25_radians = math.radians(-25.0)
    for side in ("L", "R"):
        local_at_negative_25 = helper_local_translation(armature, side, -25.0)
        records.append(
            {
                "side": side,
                "channel": "rotation_euler",
                "index": 0,
                "expression": f"thigh_angle*{HELPER_FRACTION:.17g}",
                "coefficient": HELPER_FRACTION,
            }
        )
        for index, value in enumerate(local_at_negative_25):
            coefficient = float(value) / negative_25_radians
            records.append(
                {
                    "side": side,
                    "channel": "location",
                    "index": index,
                    "expression": f"thigh_angle*{coefficient:.17g}",
                    "coefficient": coefficient,
                }
            )
    return records


def add_helper_drivers(armature):
    for side in ("L", "R"):
        helper = armature.pose.bones[f"HipDeform_{side}"]
        helper.rotation_mode = "XYZ"
        helper.scale = (1.0, 1.0, 1.0)
    for spec in helper_driver_specs(armature):
        helper = armature.pose.bones[f"HipDeform_{spec['side']}"]
        fcurve = helper.driver_add(spec["channel"], spec["index"])
        fcurve.keyframe_points.clear()
        driver = fcurve.driver
        driver.type = "SCRIPTED"
        driver.expression = spec["expression"]
        variable = driver.variables.new()
        variable.name = "thigh_angle"
        variable.type = "TRANSFORMS"
        target = variable.targets[0]
        target.id = armature
        target.bone_target = f"Thigh_{spec['side']}"
        target.transform_type = "ROT_X"
        target.transform_space = "LOCAL_SPACE"
    armature["driver_fcurve_count"] = 8
    armature["unity_helper_curve_bake_required"] = True
    armature["runtime_helper_drive_optional"] = True
    bpy.context.view_layer.update()


def driver_contract_report(armature):
    expected = {}
    for spec in helper_driver_specs(armature):
        helper = armature.pose.bones[f"HipDeform_{spec['side']}"]
        key = (helper.path_from_id(spec["channel"]), spec["index"])
        expected[key] = spec
    actual_fcurves = (
        list(armature.animation_data.drivers)
        if armature.animation_data is not None
        else []
    )
    actual = {(curve.data_path, curve.array_index): curve for curve in actual_fcurves}
    records = []
    valid = len(actual_fcurves) == 8 and set(actual) == set(expected)
    for key in sorted(expected):
        spec = expected[key]
        curve = actual.get(key)
        record = {
            "dataPath": key[0],
            "arrayIndex": key[1],
            "expectedExpression": spec["expression"],
            "targetBone": f"Thigh_{spec['side']}",
            "targetTransformType": "ROT_X",
            "targetTransformSpace": "LOCAL_SPACE",
            "result": "FAIL",
        }
        if curve is not None:
            driver = curve.driver
            variables = list(driver.variables)
            target = variables[0].targets[0] if len(variables) == 1 else None
            curve_valid = (
                driver.type == "SCRIPTED"
                and driver.expression == spec["expression"]
                and len(variables) == 1
                and variables[0].name == "thigh_angle"
                and variables[0].type == "TRANSFORMS"
                and target is not None
                and target.id is armature
                and target.bone_target == f"Thigh_{spec['side']}"
                and target.transform_type == "ROT_X"
                and target.transform_space == "LOCAL_SPACE"
                and len(curve.keyframe_points) == 0
                and len(curve.modifiers) == 0
            )
            record.update(
                {
                    "actualExpression": driver.expression,
                    "variableCount": len(variables),
                    "result": "PASS" if curve_valid else "FAIL",
                }
            )
            valid = valid and curve_valid
        records.append(record)
    animation = armature.animation_data
    valid = (
        valid
        and animation is not None
        and animation.action is None
        and len(animation.nla_tracks) == 0
    )
    return {
        "expectedCount": 8,
        "actualCount": len(actual_fcurves),
        "records": records,
        "action": animation.action.name if animation and animation.action else None,
        "nlaTrackCount": len(animation.nla_tracks) if animation else 0,
        "result": "PASS" if valid else "FAIL",
    }


def reset_pose(armature):
    for bone_name in LOGICAL_BONES:
        pose_bone = armature.pose.bones[bone_name]
        pose_bone.matrix_basis = Matrix.Identity(4)
        pose_bone.rotation_mode = "QUATERNION"
    for helper_name in HELPER_NAMES:
        helper = armature.pose.bones[helper_name]
        helper.rotation_mode = "XYZ"
        helper.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()


def apply_pose_test(armature, pose_id):
    require(pose_id in POSE_TESTS, f"unknown pose: {pose_id}")
    reset_pose(armature)
    axes = {
        "X": Vector((1.0, 0.0, 0.0)),
        "Y": Vector((0.0, 1.0, 0.0)),
        "Z": Vector((0.0, 0.0, 1.0)),
    }
    for bone_name, axis, degrees in POSE_TESTS[pose_id]:
        pose_bone = armature.pose.bones[bone_name]
        pose_bone.rotation_mode = "QUATERNION"
        pose_bone.rotation_quaternion = Quaternion(
            axes[axis], math.radians(float(degrees))
        )
    bpy.context.view_layer.update()
    bpy.context.view_layer.update()


def pose_record_pass(record, pose_id):
    edge = record["edgeStretchRatio"]
    if pose_id.endswith("_45"):
        limits = HIP45_LIMITS
    else:
        limits = {
            "minimum": r18.EDGE_STRETCH_MINIMUM,
            "p01Minimum": r18.EDGE_STRETCH_P01_MINIMUM,
            "p99Maximum": r18.EDGE_STRETCH_P99_MAXIMUM,
            "maximum": r18.EDGE_STRETCH_MAXIMUM,
            "maximumAdjacentAngleDegrees": r18.MAXIMUM_ADJACENT_ANGLE,
        }
    displacement_ok = (
        record["maximumVertexDisplacementMeters"] <= r18.REST_POSITION_TOLERANCE
        if pose_id == "Rest"
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
        <= limits["maximumAdjacentAngleDegrees"]
        and record["selfIntersection"]["result"] == "PASS"
        and edge["minimum"] >= limits["minimum"]
        and edge["p01"] >= limits["p01Minimum"]
        and edge["p99"] <= limits["p99Maximum"]
        and edge["maximum"] <= limits["maximum"]
        and displacement_ok
    )


def evaluated_coordinates(body):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = body.evaluated_get(depsgraph)
    mesh = bpy.data.meshes.new_from_object(
        evaluated,
        preserve_all_data_layers=False,
        depsgraph=depsgraph,
    )
    try:
        return r18.r12.mesh_coordinates(mesh).copy()
    finally:
        bpy.data.meshes.remove(mesh)


def projected_contour(coordinates, edges):
    z_values = np.linspace(0.10, 0.32, 221)
    screen_right = np.full(len(z_values), np.nan, dtype=np.float64)
    first = coordinates[edges[:, 0]]
    second = coordinates[edges[:, 1]]
    low = np.minimum(first[:, 2], second[:, 2])
    high = np.maximum(first[:, 2], second[:, 2])
    for index, z_value in enumerate(z_values):
        crossing = np.flatnonzero((low <= z_value) & (high >= z_value))
        require(len(crossing) > 0, "side contour sampling gap")
        a = first[crossing]
        b = second[crossing]
        dz = b[:, 2] - a[:, 2]
        parameter = np.zeros(len(crossing), dtype=np.float64)
        nonzero = np.abs(dz) > 1.0e-15
        parameter[nonzero] = (z_value - a[nonzero, 2]) / dz[nonzero]
        y = a[:, 1] + parameter * (b[:, 1] - a[:, 1])
        screen_right[index] = -float(np.min(y))
    core = (z_values >= NOTCH_CORE_Z[0]) & (z_values <= NOTCH_CORE_Z[1])
    median = float(np.median(screen_right[core]))
    minimum = float(np.min(screen_right[core]))
    smooth = np.convolve(
        screen_right,
        np.ones(9, dtype=np.float64) / 9.0,
        mode="same",
    )
    second_difference = np.zeros_like(smooth)
    second_difference[1:-1] = smooth[:-2] - 2.0 * smooth[1:-1] + smooth[2:]
    return {
        "zStartMeters": float(z_values[0]),
        "zStepMeters": float(z_values[1] - z_values[0]),
        "coreZRangeMeters": list(NOTCH_CORE_Z),
        "coreMedianMeters": median,
        "coreMinimumMeters": minimum,
        "notchDepthFromCoreMedianMeters": median - minimum,
        "focusSecondDifferenceRms": float(
            np.sqrt(np.mean(second_difference[core] ** 2))
        ),
        "focusMaximumPositiveSecondDifference": float(
            np.max(second_difference[core])
        ),
    }


def contour_for_pose(body, armature, edges, pose_id):
    apply_pose_test(armature, pose_id)
    result = projected_contour(evaluated_coordinates(body), edges)
    r18.reset_pose(armature)
    return result


def notch_comparison(baseline, candidate):
    reduction = (
        baseline["notchDepthFromCoreMedianMeters"]
        - candidate["notchDepthFromCoreMedianMeters"]
    )
    return {
        "baselineH": baseline,
        "candidate": candidate,
        "notchDepthReductionMeters": reduction,
    }


def render_hip45(scene, body, head, armature, render_dir):
    original_visibility = {obj.name: bool(obj.hide_render) for obj in bpy.data.objects}
    existing_lights = [obj for obj in bpy.data.objects if obj.type == "LIGHT"]
    for obj in bpy.data.objects:
        if obj.type == "MESH" and obj not in (body, head):
            obj.hide_render = True
    for light in existing_lights:
        light.hide_render = True
    body.hide_render = False
    head.hide_render = False
    collection = bpy.data.collections.new("TMP_C1BRW020_Hip45Render")
    scene.collection.children.link(collection)
    camera = r18.create_camera(
        collection,
        "TMP_CAM_R20_Hip45Side",
        (-4.0, 0.0, 0.50),
        (0.0, 0.0, 0.50),
        1.22,
    )
    lights = [
        r18.create_area_light(
            collection, "TMP_LIGHT_R20_Hip45Key", (-2.4, -3.0, 3.0), 420.0, 4.0
        ),
        r18.create_area_light(
            collection, "TMP_LIGHT_R20_Hip45Fill", (2.8, -1.8, 1.6), 220.0, 3.0
        ),
        r18.create_area_light(
            collection, "TMP_LIGHT_R20_Hip45Rim", (0.0, 2.8, 2.2), 300.0, 2.5
        ),
    ]
    filename = EXPECTED_RENDER_FILENAMES[-1]
    try:
        apply_pose_test(armature, "HipFlex_L_45")
        r18.render_to(scene, camera, os.path.join(render_dir, filename))
    finally:
        r18.reset_pose(armature)
        r18.remove_objects([camera, *lights])
        scene.collection.children.unlink(collection)
        bpy.data.collections.remove(collection)
        for name, hidden in original_visibility.items():
            obj = bpy.data.objects.get(name)
            if obj is not None:
                obj.hide_render = hidden
    return filename


def helper_contract_report(armature):
    records = {}
    for side in ("L", "R"):
        helper = armature.data.bones[f"HipDeform_{side}"]
        thigh = armature.data.bones[f"Thigh_{side}"]
        matrix_delta = float(
            np.max(
                np.abs(
                    np.asarray(helper.matrix_local, dtype=np.float64)
                    - np.asarray(thigh.matrix_local, dtype=np.float64)
                )
            )
        )
        records[side] = {
            "name": helper.name,
            "parent": helper.parent.name if helper.parent else None,
            "useDeform": bool(helper.use_deform),
            "useConnect": bool(helper.use_connect),
            "logicalBone": bool(helper.get("logical_bone", True)),
            "helperBone": bool(helper.get("helper_bone", False)),
            "runtimeDriven": bool(helper.get("runtime_driven", False)),
            "runtimeDriveOptional": bool(
                helper.get("runtime_drive_optional", False)
            ),
            "driverEmbedded": bool(helper.get("driver_embedded", False)),
            "sourceLogicalBone": helper.get("source_logical_bone", ""),
            "rotationFraction": float(helper.get("rotation_fraction", -1.0)),
            "restMatrixMaximumDeltaFromThigh": matrix_delta,
        }
    drivers = driver_contract_report(armature)
    valid = (
        set(armature.data.bones.keys()) == set(ALL_BONES)
        and len(armature.data.bones) == 22
        and all(record["parent"] == "Pelvis" for record in records.values())
        and all(record["useDeform"] for record in records.values())
        and all(not record["useConnect"] for record in records.values())
        and all(not record["logicalBone"] for record in records.values())
        and all(record["helperBone"] for record in records.values())
        and all(not record["runtimeDriven"] for record in records.values())
        and all(record["runtimeDriveOptional"] for record in records.values())
        and all(record["driverEmbedded"] for record in records.values())
        and all(
            record["sourceLogicalBone"] == f"Thigh_{side}"
            and abs(record["rotationFraction"] - HELPER_FRACTION) <= 1.0e-12
            for side, record in records.items()
        )
        and drivers["result"] == "PASS"
        and all(
            record["restMatrixMaximumDeltaFromThigh"] <= 1.0e-7
            for record in records.values()
        )
        and all(
            bool(armature.data.bones[name].get("logical_bone", False))
            for name in LOGICAL_BONES
        )
    )
    return {
        "logicalBoneCount": len(LOGICAL_BONES),
        "helperBoneCount": len(HELPER_NAMES),
        "totalBoneCount": len(armature.data.bones),
        "helpers": records,
        "drive": helper_drive_contract(),
        "drivers": drivers,
        "result": "PASS" if valid else "FAIL",
    }


def skeleton_report(armature):
    report = r18.skeleton_report(armature)
    report["logicalBoneCount"] = len(LOGICAL_BONES)
    report["helperBoneCount"] = len(HELPER_NAMES)
    report["totalBoneCount"] = len(armature.data.bones)
    report["helperBoneNames"] = list(HELPER_NAMES)
    return report


def all_ids_with_animation_data():
    datablocks = [
        *list(bpy.data.scenes),
        *list(bpy.data.objects),
        *list(bpy.data.meshes),
        *list(bpy.data.armatures),
        *list(bpy.data.materials),
        *list(bpy.data.worlds),
    ]
    return sorted(
        datablock.name
        for datablock in datablocks
        if getattr(datablock, "animation_data", None) is not None
    )


def strict_saved_contract(path, expected_bind_hash):
    bpy.ops.wm.open_mainfile(filepath=path)
    scene = bpy.context.scene
    armature = bpy.data.objects.get(ARMATURE_NAME)
    body = bpy.data.objects.get(BODY_NAME)
    head = bpy.data.objects.get(HEAD_NAME)
    require(armature is not None and armature.type == "ARMATURE", "saved armature missing")
    require(body is not None and body.type == "MESH", "saved body missing")
    require(head is not None and head.type == "MESH", "saved head missing")
    body_coordinates = r18.r12.mesh_coordinates(body.data)
    head_coordinates = r18.r12.mesh_coordinates(head.data)
    expected_body_weights = build_body_weights(body_coordinates)
    expected_head_weights = {"Head": np.ones(len(head_coordinates), dtype=np.float64)}
    body_weight = r18.weight_report(body, expected_body_weights, body_coordinates)
    head_weight = r18.weight_report(head, expected_head_weights, head_coordinates)
    assignments = r19.actual_assignment_metrics(body)
    head_assignments = r19.actual_assignment_metrics(head)
    inventory = r18.rig_inventory(armature, body, head)
    skeleton = skeleton_report(armature)
    helper = helper_contract_report(armature)
    actual_groups = tuple(group.name for group in body.vertex_groups)
    expected_groups = tuple(expected_body_weights)
    deform_bones = {bone.name for bone in armature.data.bones if bone.use_deform}
    modifiers_valid = all(
        len(obj.modifiers) == 1
        and obj.modifiers[0].type == "ARMATURE"
        and obj.modifiers[0].object is armature
        and obj.modifiers[0].use_vertex_groups
        and not obj.modifiers[0].use_bone_envelopes
        and not obj.modifiers[0].use_deform_preserve_volume
        for obj in (body, head)
    )
    constraints = sum(len(pose_bone.constraints) for pose_bone in armature.pose.bones)
    constraints += len(armature.constraints)
    ids_with_animation = all_ids_with_animation_data()
    exact_objects = {
        obj.name: obj.type for obj in bpy.data.objects
    } == {
        ARMATURE_NAME: "ARMATURE",
        BODY_NAME: "MESH",
        HEAD_NAME: "MESH",
    }
    exact_parenting = body.parent is armature and head.parent is armature
    exact_data_names = (
        armature.data.name == ARMATURE_DATA_NAME
        and body.data.name == BODY_NAME + "Mesh"
        and head.data.name == HEAD_NAME + "Mesh"
    )
    result = (
        r18.skeleton_matches_spec(armature)
        and skeleton["bindPoseSha256"] == expected_bind_hash
        and helper["result"] == "PASS"
        and actual_groups == expected_groups
        and tuple(group.name for group in head.vertex_groups) == ("Head",)
        and set(expected_groups) | {"Head"} == deform_bones
        and "Root" not in deform_bones
        and body_weight["maximumWeightSumError"] <= r18.WEIGHT_SUM_TOLERANCE
        and body_weight["maximumInfluencesPerVertex"] <= 4
        and body_weight["unweightedVertexCount"] == 0
        and body_weight["maximumAssignmentRoundtripError"] <= r18.WEIGHT_SUM_TOLERANCE
        and body_weight["mirror"]["unmatchedVertexCount"] == 0
        and body_weight["mirror"]["maximumMirroredWeightDeviation"] <= 1.0e-7
        and body_weight["maximumLeftBoneLeakOnRightSide"] == 0.0
        and body_weight["maximumRightBoneLeakOnLeftSide"] == 0.0
        and head_weight["maximumInfluencesPerVertex"] == 1
        and head_weight["maximumAssignmentRoundtripError"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and assignments["maximumStoredAssignmentsPerVertex"] <= 4
        and assignments["maximumInfluencesPerVertexAcrossAllGroups"] <= 4
        and assignments["unweightedVertexCountAcrossAllGroups"] == 0
        and assignments["invalidGroupAssignmentCount"] == 0
        and assignments["nonfiniteAssignmentCount"] == 0
        and assignments["minimumStoredWeight"] >= 0.0
        and assignments["maximumStoredWeight"] <= 1.0
        and assignments["maximumWeightSumErrorAcrossAllGroups"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and head_assignments["maximumStoredAssignmentsPerVertex"] == 1
        and head_assignments["maximumInfluencesPerVertexAcrossAllGroups"] == 1
        and head_assignments["unweightedVertexCountAcrossAllGroups"] == 0
        and head_assignments["invalidGroupAssignmentCount"] == 0
        and head_assignments["nonfiniteAssignmentCount"] == 0
        and head_assignments["minimumStoredWeight"] == 1.0
        and head_assignments["maximumStoredWeight"] == 1.0
        and head_assignments["maximumWeightSumErrorAcrossAllGroups"] == 0.0
        and modifiers_valid
        and exact_objects
        and exact_parenting
        and exact_data_names
        and r18.pose_is_rest(armature)
        and inventory["objectTypeCounts"] == {"ARMATURE": 1, "MESH": 2}
        and inventory["collectionCount"] == 1
        and inventory["armatureObjectCount"] == 1
        and inventory["armatureDatablockCount"] == 1
        and inventory["boneCount"] == 22
        and inventory["actionCount"] == 0
        and inventory["animatedObjectCount"] == 1
        and inventory["shapeKeyDatablockCount"] == 0
        and inventory["latticeObjectCount"] == 0
        and inventory["bodyModifierCount"] == 1
        and inventory["headModifierCount"] == 1
        and inventory["bodyVertexGroupCount"] == len(expected_groups)
        and inventory["headVertexGroupCount"] == 1
        and inventory["rigidBodyObjectCount"] == 0
        and inventory["rigidBodyConstraintObjectCount"] == 0
        and len(bpy.data.scenes) == 1
        and len(bpy.data.meshes) == 2
        and len(bpy.data.armatures) == 1
        and len(bpy.data.cameras) == 0
        and len(bpy.data.lights) == 0
        and len(bpy.data.curves) == 0
        and len(bpy.data.lattices) == 0
        and len(bpy.data.shape_keys) == 0
        and not inventory["temporaryDatablocks"]
        and not inventory["negativeScaleObjects"]
        and not inventory["colliderObjects"]
        and constraints == 0
        and ids_with_animation == [ARMATURE_NAME]
        and scene.get("animation_authored") is False
        and scene.get("runtime_helper_drive_required") is False
        and scene.get("runtime_helper_drive_optional") is True
        and scene.get("drivers_embedded") is True
        and scene.get("unity_helper_curve_bake_required") is True
        and scene.get("helper_drive_contract_json")
        == json.dumps(helper_drive_contract())
    )
    return {
        "result": "PASS" if result else "FAIL",
        "bodyWeight": body_weight,
        "headWeight": head_weight,
        "actualAssignments": assignments,
        "headActualAssignments": head_assignments,
        "bodyVertexGroupOrder": list(actual_groups),
        "skeleton": skeleton,
        "helperContract": helper,
        "modifiersValid": modifiers_valid,
        "deformBoneNames": sorted(deform_bones),
        "objectNamesAndTypesExact": exact_objects,
        "parentingExact": exact_parenting,
        "datablockNamesExact": exact_data_names,
        "constraintCount": constraints,
        "idsWithAnimationData": ids_with_animation,
        "restPoseConfirmed": r18.pose_is_rest(armature),
        "inventory": inventory,
    }


def saved_pose_verification(path):
    bpy.ops.wm.open_mainfile(filepath=path)
    armature = bpy.data.objects[ARMATURE_NAME]
    body = bpy.data.objects[BODY_NAME]
    rest = r18.r12.mesh_coordinates(body.data)
    edges = r18.r12.mesh_edges(body.data)
    records = {}
    for pose_id in EXPECTED_POSE_IDS:
        apply_pose_test(armature, pose_id)
        record = r18.mesh_pose_metrics(body, rest, edges)
        record["result"] = "PASS" if pose_record_pass(record, pose_id) else "FAIL"
        helper_drives = {}
        for side in ("L", "R"):
            degrees = logical_rotation_degrees(pose_id, side)
            if degrees is None:
                continue
            thigh = armature.pose.bones[f"Thigh_{side}"]
            helper_bone = armature.pose.bones[f"HipDeform_{side}"]
            expected_rotation = Quaternion((1.0, 0.0, 0.0, 0.0)).slerp(
                thigh.matrix_basis.to_quaternion(), HELPER_FRACTION
            )
            expected_parent_translation = (
                Vector(HELPER_TRANSLATION_AT_NEGATIVE_25)
                * (float(degrees) / -25.0)
            )
            expected_local_translation = (
                helper_bone.bone.matrix_local.to_3x3().inverted()
                @ expected_parent_translation
            )
            expected_basis = (
                Matrix.Translation(expected_local_translation)
                @ expected_rotation.to_matrix().to_4x4()
            )
            actual_basis = helper_bone.matrix_basis
            matrix_delta = float(
                np.max(
                    np.abs(
                        np.asarray(actual_basis, dtype=np.float64)
                        - np.asarray(expected_basis, dtype=np.float64)
                    )
                )
            )
            scale = helper_bone.scale
            scale_delta = max(abs(float(value) - 1.0) for value in scale)
            parent_pose_active = any(
                float(
                    np.max(
                        np.abs(
                            np.asarray(
                                armature.pose.bones[parent_name].matrix_basis,
                                dtype=np.float64,
                            )
                            - np.asarray(Matrix.Identity(4), dtype=np.float64)
                        )
                    )
                )
                > 1.0e-8
                for parent_name in ("Pelvis", "Root")
            )
            expects_parent_probe = pose_id.startswith(
                ("PelvisYaw_", "RootYaw_")
            )
            helper_drives[side] = {
                "thighDegrees": float(degrees),
                "rotationFraction": HELPER_FRACTION,
                "signedModelTranslationMeters": [
                    float(value) for value in expected_parent_translation
                ],
                "poseBoneLocalLocation": [
                    float(value) for value in expected_local_translation
                ],
                "matrixBasisMaximumDeltaFromContract": matrix_delta,
                "maximumScaleDeltaFromOne": scale_delta,
                "parentPoseActive": parent_pose_active,
                "parentInheritanceProbeRequired": expects_parent_probe,
                "result": (
                    "PASS"
                    if matrix_delta <= HELPER_POSE_MATRIX_TOLERANCE
                    and scale_delta <= HELPER_POSE_MATRIX_TOLERANCE
                    and (not expects_parent_probe or parent_pose_active)
                    else "FAIL"
                ),
            }
        record["helperDriveVerification"] = helper_drives
        if any(value["result"] != "PASS" for value in helper_drives.values()):
            record["result"] = "FAIL"
        records[pose_id] = record
    r18.reset_pose(armature)
    return {
        "poses": records,
        "restPoseRestored": r18.pose_is_rest(armature),
        "result": (
            "PASS"
            if all(record["result"] == "PASS" for record in records.values())
            and tuple(records) == EXPECTED_POSE_IDS
            and r18.pose_is_rest(armature)
            else "FAIL"
        ),
    }


def source_geometry_snapshots():
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    return (
        r19.object_geometry_snapshot(bpy.data.objects[r18.SOURCE_BODY]),
        r19.object_geometry_snapshot(bpy.data.objects[r18.SOURCE_HEAD]),
    )


def exact_geometry_verification(path, source_body_snapshot, source_head_snapshot):
    bpy.ops.wm.open_mainfile(filepath=path)
    body = r19.compare_geometry(
        "body",
        source_body_snapshot,
        r19.object_geometry_snapshot(bpy.data.objects[BODY_NAME]),
    )
    head = r19.compare_geometry(
        "head",
        source_head_snapshot,
        r19.object_geometry_snapshot(bpy.data.objects[HEAD_NAME]),
    )
    return {
        "sourcePath": SOURCE_BLEND,
        "targetPath": path,
        "body": body,
        "head": head,
        "result": "PASS" if body["result"] == head["result"] == "PASS" else "FAIL",
    }


def write_json(path, value):
    r19.write_json_atomic(path, value)


def generate(stage_blend, stage_render_dir, stage_report, final_blend):
    configure_base_module()
    require(os.path.isfile(APPROVAL_RECORD), "approval record missing")
    require(
        file_sha256(APPROVAL_RECORD) == APPROVAL_SHA256,
        "approval record hash mismatch",
    )
    source_hash_before = file_sha256(SOURCE_BLEND)
    source_body_snapshot, source_head_snapshot = source_geometry_snapshots()
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    scene = bpy.context.scene
    scene.name = "C1BRW020_HelperRigReview"
    source_body = bpy.data.objects[r18.SOURCE_BODY]
    source_head = bpy.data.objects[r18.SOURCE_HEAD]
    source_body_signature = r18.r12.mesh_signature(source_body)
    source_head_signature = r18.r12.mesh_signature(source_head)

    rig_collection = bpy.data.collections.new(RIG_COLLECTION_NAME)
    scene.collection.children.link(rig_collection)
    armature = create_armature(rig_collection)
    body_coordinates, head_coordinates, body_weights, head_weights = prepare_meshes(
        source_body, source_head, armature
    )
    body = bpy.data.objects[BODY_NAME]
    head = bpy.data.objects[HEAD_NAME]
    edges = r18.r12.mesh_edges(body.data)

    baseline_weights = r19.build_body_weights(body_coordinates)
    baseline_weights["HipDeform_L"] = np.zeros(len(body_coordinates), dtype=np.float64)
    baseline_weights["HipDeform_R"] = np.zeros(len(body_coordinates), dtype=np.float64)
    r18.assign_weights(body, baseline_weights)
    baseline_contours = {
        pose_id: contour_for_pose(body, armature, edges, pose_id)
        for pose_id in (
            "HipFlex_L",
            "HipFlex_R",
            "HipFlex_L_45",
            "HipFlex_R_45",
        )
    }
    r18.assign_weights(body, body_weights)

    pose_records = {}
    contour_comparisons = {}
    for pose_id in EXPECTED_POSE_IDS:
        apply_pose_test(armature, pose_id)
        record = r18.mesh_pose_metrics(body, body_coordinates, edges)
        record["result"] = "PASS" if pose_record_pass(record, pose_id) else "FAIL"
        if pose_id.startswith("HipFlex_"):
            candidate_contour = projected_contour(evaluated_coordinates(body), edges)
            comparison = notch_comparison(
                baseline_contours[pose_id], candidate_contour
            )
            record["sideContour"] = candidate_contour
            record["notchDepthReductionMeters"] = comparison[
                "notchDepthReductionMeters"
            ]
            contour_comparisons[pose_id] = comparison
        pose_records[pose_id] = record
    r18.reset_pose(armature)

    require(
        contour_comparisons["HipFlex_L"]["notchDepthReductionMeters"]
        >= HIP25_NOTCH_REDUCTION_MINIMUM,
        "Hip25 notch reduction below 1.0 mm",
    )
    require(
        contour_comparisons["HipFlex_R"]["notchDepthReductionMeters"]
        >= HIP25_NOTCH_REDUCTION_MINIMUM,
        "Hip25 right notch reduction below 1.0 mm",
    )
    require(
        contour_comparisons["HipFlex_L_45"]["notchDepthReductionMeters"]
        >= HIP45_NOTCH_REDUCTION_MINIMUM,
        "Hip45 notch reduction below 0.5 mm",
    )
    require(
        contour_comparisons["HipFlex_R_45"]["notchDepthReductionMeters"]
        >= HIP45_NOTCH_REDUCTION_MINIMUM,
        "Hip45 right notch reduction below 0.5 mm",
    )

    body_weight_report = r18.weight_report(body, body_weights, body_coordinates)
    head_weight_report = r18.weight_report(head, head_weights, head_coordinates)
    assignment_report = r19.actual_assignment_metrics(body)
    skeleton = skeleton_report(armature)
    helper = helper_contract_report(armature)
    expected_bind_hash = EXPECTED_BIND_POSE_SHA256
    require(
        skeleton["bindPoseSha256"] == expected_bind_hash,
        "r20 bind-pose hash drift",
    )
    require(tuple(pose_records) == EXPECTED_POSE_IDS, "pose contract drift")
    require(
        body_weight_report["maximumInfluencesPerVertex"] <= 4
        and body_weight_report["maximumWeightSumError"]
        <= r18.WEIGHT_SUM_TOLERANCE
        and body_weight_report["unweightedVertexCount"] == 0
        and body_weight_report["mirror"]["unmatchedVertexCount"] == 0
        and body_weight_report["mirror"]["maximumMirroredWeightDeviation"]
        <= 1.0e-7
        and body_weight_report["maximumLeftBoneLeakOnRightSide"] == 0.0
        and body_weight_report["maximumRightBoneLeakOnLeftSide"] == 0.0
        and assignment_report["maximumStoredAssignmentsPerVertex"] <= 4
        and assignment_report["maximumInfluencesPerVertexAcrossAllGroups"] <= 4,
        "pre-save helper weight contract failed",
    )

    os.makedirs(stage_render_dir, exist_ok=True)
    render_files = r18.render_qa_bundle(
        scene, body, head, armature, stage_render_dir
    )
    render_files.append(
        render_hip45(scene, body, head, armature, stage_render_dir)
    )
    require(tuple(render_files) == EXPECTED_RENDER_FILENAMES, "render contract drift")
    require(
        all(
            os.path.isfile(os.path.join(stage_render_dir, name))
            and os.path.getsize(os.path.join(stage_render_dir, name)) > 0
            for name in render_files
        ),
        "render output missing",
    )

    r18.reset_pose(armature)
    r18.clean_final_rig_scene(scene, rig_collection, armature, body, head)
    r18.clear_custom_properties(scene)
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = VERSION
    scene["revision"] = REVISION
    scene["owner_task"] = OWNER_TASK
    scene["source_revision"] = "r16"
    scene["source_sha256"] = SOURCE_SHA256
    scene["rig_type"] = r18.RIG_TYPE
    scene["logical_bones_json"] = json.dumps(LOGICAL_BONES)
    scene["helper_bones_json"] = json.dumps(HELPER_NAMES)
    scene["helper_drive_contract_json"] = json.dumps(helper_drive_contract())
    scene["runtime_helper_drive_required"] = False
    scene["runtime_helper_drive_optional"] = True
    scene["unity_helper_curve_bake_required"] = True
    scene["animation_authored"] = False
    scene["animation_clip_count"] = 0
    scene["root_motion_authored"] = False
    scene["constraints_embedded"] = False
    scene["drivers_embedded"] = True
    inventory = r18.rig_inventory(armature, body, head)
    require(helper["result"] == "PASS", "helper contract failed before save")
    require(
        all(record["result"] == "PASS" for record in pose_records.values()),
        "pose QA failed",
    )
    require(inventory["boneCount"] == 22, "pre-save bone count")
    require(inventory["actionCount"] == 0, "pre-save action count")
    require(
        all_ids_with_animation_data() == [ARMATURE_NAME],
        "pre-save driver animation-data contract",
    )
    require(
        sum(len(pose_bone.constraints) for pose_bone in armature.pose.bones) == 0
        and len(armature.constraints) == 0,
        "pre-save constraints",
    )
    bpy.ops.object.select_all(action="DESELECT")
    armature.select_set(True)
    bpy.context.view_layer.objects.active = armature
    bpy.ops.wm.save_as_mainfile(filepath=stage_blend, check_existing=False)

    strict = strict_saved_contract(stage_blend, expected_bind_hash)
    exact = exact_geometry_verification(
        stage_blend, source_body_snapshot, source_head_snapshot
    )
    exact["targetPath"] = final_blend
    saved_poses = saved_pose_verification(stage_blend)
    print(
        "R20_SAVED_POSE_DIAGNOSTIC="
        + json.dumps(
            {
                "result": saved_poses["result"],
                "restPoseRestored": saved_poses["restPoseRestored"],
                "poses": {
                    pose_id: {
                        "result": record["result"],
                        "helperDriveVerification": record[
                            "helperDriveVerification"
                        ],
                    }
                    for pose_id, record in saved_poses["poses"].items()
                },
            },
            separators=(",", ":"),
        )
    )
    require(strict["result"] == "PASS", "saved strict contract failed")
    require(exact["result"] == "PASS", "saved exact geometry failed")
    require(saved_poses["result"] == "PASS", "saved helper pose verification failed")
    require(file_sha256(SOURCE_BLEND) == source_hash_before == SOURCE_SHA256, "source changed")
    require(file_sha256(R18_GENERATOR) == R18_GENERATOR_SHA256, "r18 generator changed")
    require(file_sha256(R19_GENERATOR) == R19_GENERATOR_SHA256, "r19 generator changed")
    require(file_sha256(R18_BLEND) == R18_BLEND_SHA256, "r18 blend changed")

    report = {
        "assetId": ASSET_ID,
        "assetVersion": VERSION,
        "revision": REVISION,
        "ownerTask": OWNER_TASK,
        "candidateStatus": "HELPER_RIG_REVIEW",
        "source": {
            "revision": "r16",
            "path": SOURCE_BLEND,
            "sha256Before": source_hash_before,
            "sha256After": file_sha256(SOURCE_BLEND),
            "unchanged": True,
            "approvalRecordPath": APPROVAL_RECORD,
            "approvalRecordSha256": APPROVAL_SHA256,
            "bodySignature": source_body_signature,
            "headSignature": source_head_signature,
        },
        "upstreamPins": {
            "r18Generator": {
                "path": R18_GENERATOR,
                "sha256": R18_GENERATOR_SHA256,
            },
            "r19Generator": {
                "path": R19_GENERATOR,
                "sha256": R19_GENERATOR_SHA256,
            },
            "r18Checkpoint": {
                "path": R18_BLEND,
                "sha256": R18_BLEND_SHA256,
            },
        },
        "contract": {
            "rigType": "GENERIC_PLUS_RUNTIME_HIP_HELPERS",
            "logicalBoneCount": 20,
            "helperBoneCount": 2,
            "totalBoneCount": 22,
            "maximumWeightsPerVertex": 4,
            "skinningMode": "LINEAR_BLEND_UNITY_PARITY",
            "preserveVolume": False,
            "animationAuthored": False,
            "animationClipCount": 0,
            "rootMotionAuthored": False,
            "constraintsEmbedded": False,
            "driversEmbedded": True,
            "driverFCurveCount": 8,
            "unityHelperCurveBakeRequired": True,
            "runtimeHelperDriveOptional": True,
            "runtimeHelperDriveRequired": False,
        },
        "skeleton": skeleton,
        "helperContract": helper,
        "weights": {
            "method": "H_PLUS_BERNSTEIN_MOMENT_HIP_HELPERS",
            "baseMethod": "DETERMINISTIC_C2_FRONT_HIP_SUPPORT",
            "gateFormula": "smootherstep((z-0.05)/0.05)",
            "pairFormula": (
                "q=t/(f+sqrt(f^2+(1-2f)t)); "
                "Pelvis=(1-q)^2, Helper=2q(1-q), Thigh=q^2; f=0.25"
            ),
            "body": body_weight_report,
            "head": head_weight_report,
            "actualAssignments": assignment_report,
        },
        "deformationQA": {
            "poseContract": list(EXPECTED_POSE_IDS),
            "poses": pose_records,
            "baselineHContours": baseline_contours,
            "notchComparisons": contour_comparisons,
            "notchLimitsMeters": {
                "Hip25": HIP25_NOTCH_REDUCTION_MINIMUM,
                "Hip45": HIP45_NOTCH_REDUCTION_MINIMUM,
            },
            "savedPoseVerification": saved_poses,
            "temporaryPoseOnly": True,
            "savedInRestPose": True,
        },
        "exactGeometryVerification": exact,
        "strictSavedContract": strict,
        "finalInventory": strict["inventory"],
        "renderFiles": render_files,
        "renderVerification": {
            "count": len(render_files),
            "files": {
                name: {
                    "bytes": os.path.getsize(os.path.join(stage_render_dir, name)),
                    "sha256": file_sha256(os.path.join(stage_render_dir, name)),
                }
                for name in render_files
            },
            "result": "PASS",
        },
        "generator": {
            "path": os.path.abspath(__file__),
            "sha256": file_sha256(os.path.abspath(__file__)),
        },
        "output": {
            "blendPath": final_blend,
            "blendBytes": os.path.getsize(stage_blend),
            "blendSha256": file_sha256(stage_blend),
        },
        "technicalResult": "PASS",
        "stagingValidation": {
            "runId": os.path.basename(os.path.dirname(stage_blend)),
            "generatedAndFullyVerifiedBeforePromotion": True,
            "previousFinalDeletedBeforeValidation": False,
            "promotionPolicy": (
                "VALIDATED_RENDER_AND_BLEND_THEN_REPORT_LAST_WITH_ROLLBACK"
            ),
            "result": "PASS",
        },
    }
    write_json(stage_report, report)
    return report


def main():
    final_blend, final_render_dir, final_report = parse_args()
    package_dir = os.path.dirname(final_blend)
    os.makedirs(package_dir, exist_ok=True)
    stage_root = tempfile.mkdtemp(prefix=".r20-stage-", dir=package_dir)
    stage_blend = os.path.join(stage_root, os.path.basename(final_blend))
    stage_renders = os.path.join(stage_root, "Renders")
    stage_report = os.path.join(stage_root, os.path.basename(final_report))
    try:
        report = generate(stage_blend, stage_renders, stage_report, final_blend)
        require(report["technicalResult"] == "PASS", "stage did not pass")
        r19.promote_validated_outputs(
            stage_root,
            stage_blend,
            stage_renders,
            stage_report,
            final_blend,
            final_render_dir,
            final_report,
            report["output"]["blendSha256"],
            report["renderFiles"],
        )
    finally:
        if os.path.isdir(stage_root):
            shutil.rmtree(stage_root)
    print("R20_HELPER_RIG_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R20_HELPER_RIG_RESULT=PASS")


if __name__ == "__main__":
    main()
