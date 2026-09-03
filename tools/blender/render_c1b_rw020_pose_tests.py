#!/usr/bin/env python3

"""Render temporary animation-preflight poses from the immutable r20 rig.

No keyframes, Actions, NLA tracks, constraints, or source-file saves are made.
The embedded HipDeform drivers are allowed to evaluate from the posed Thigh
bones exactly as they will during animation authoring.
"""

import hashlib
import importlib.util
import json
import math
import os
import sys

import bpy
from mathutils import Euler, Matrix


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT_DIR = os.path.abspath(os.path.join(HERE, "..", ".."))
R20_GENERATOR = os.path.join(HERE, "create_c1b_rw020_helper_rig.py")
R20_GENERATOR_SHA256 = (
    "67369e1d6a860db0d45b8df11aed43abb5eecd848d6c7bcd243d133bc6a89c62"
)
R20_BLEND_SHA256 = (
    "8d509c816f4095726d092ed7211fe3a6dfa70cbc940f6cc3799543a30cc04579"
)
R16_BLEND = os.path.join(
    ROOT_DIR,
    "BlenderSource",
    "Characters",
    "C1B-RW-016-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r16.blend",
)
R16_BLEND_SHA256 = (
    "9b80276e97aa84f3d2a4ef7689b4ebd241f84124494ec8ff0ca51cc119a676ef"
)
ARMATURE_NAME = "RIG_C1B_R20_Armature"
BODY_NAME = "CHR_C1B_R20_SkinnedBody"
HEAD_NAME = "CHR_C1B_R20_SkinnedHead"
RENDER_RESOLUTION = 1200


POSES = (
    {
        "id": "01_RelaxedIdle",
        "label": "Relaxed Idle",
        "camera": "ThreeQuarterRight",
        "rotations": {
            "Root": (0, 4, 0),
            "Pelvis": (0, 0, -3),
            "Spine": (0, 0, 3),
            "Chest": (0, -3, 0),
            "Clavicle_L": (-4, 0, 0),
            "Clavicle_R": (-4, 0, 0),
            "UpperArm_L": (-42, 0, 4),
            "UpperArm_R": (-38, 0, -5),
            "Forearm_L": (0, 0, 14),
            "Forearm_R": (0, 0, -18),
            "Thigh_L": (-3, 0, 0),
            "Thigh_R": (2, 0, 0),
            "Calf_L": (8, 0, 0),
            "Calf_R": (4, 0, 0),
        },
    },
    {
        "id": "02_WalkStride",
        "label": "Walk Stride",
        "camera": "ThreeQuarterLeft",
        "rotations": {
            "Root": (0, -5, 0),
            "Pelvis": (0, 6, 2),
            "Spine": (0, -4, -1),
            "Chest": (0, -4, 0),
            "UpperArm_L": (-40, 0, -14),
            "UpperArm_R": (-40, 0, -18),
            "Forearm_L": (0, 0, 16),
            "Forearm_R": (0, 0, -20),
            "Thigh_L": (-22, 0, 0),
            "Thigh_R": (14, 0, 0),
            "Calf_L": (10, 0, 0),
            "Calf_R": (18, 0, 0),
        },
    },
    {
        "id": "03_CrouchBrace",
        "label": "Crouch / Brace",
        "camera": "SideLeft",
        "rotations": {
            "Pelvis": (5, 0, 0),
            "Spine": (8, 0, 0),
            "Chest": (-3, 0, 0),
            "UpperArm_L": (-28, 0, 22),
            "UpperArm_R": (-28, 0, -22),
            "Forearm_L": (0, 0, 35),
            "Forearm_R": (0, 0, -35),
            "Thigh_L": (-18, 0, 0),
            "Thigh_R": (-18, 0, 0),
            "Calf_L": (38, 0, 0),
            "Calf_R": (38, 0, 0),
        },
    },
    {
        "id": "04_HighReach",
        "label": "High Reach",
        "camera": "ThreeQuarterRight",
        "rotations": {
            "Root": (0, 0, -2),
            "Pelvis": (0, 0, -4),
            "Spine": (0, 0, 8),
            "Chest": (-2, 0, 6),
            "Clavicle_L": (12, 0, 0),
            "Clavicle_R": (-5, 0, 0),
            "UpperArm_L": (42, 0, 5),
            "UpperArm_R": (-42, 0, -5),
            "Forearm_L": (0, 0, 12),
            "Forearm_R": (0, 0, -18),
            "Thigh_L": (-4, 0, 0),
            "Thigh_R": (4, 0, 0),
            "Calf_L": (6, 0, 0),
            "Calf_R": (3, 0, 0),
        },
    },
    {
        "id": "05_TwoHandGuard",
        "label": "Two-hand Guard",
        "camera": "ThreeQuarterSide",
        "rotations": {
            "Root": (2, 0, 0),
            "Pelvis": (4, 0, 0),
            "Spine": (7, 0, 0),
            "Chest": (3, 0, 0),
            "Clavicle_L": (-8, 0, 0),
            "Clavicle_R": (-8, 0, 0),
            "UpperArm_L": (-22, 0, 30),
            "UpperArm_R": (-22, 0, -30),
            "Forearm_L": (0, 0, 42),
            "Forearm_R": (0, 0, -42),
            "Thigh_L": (-6, 0, 0),
            "Thigh_R": (-6, 0, 0),
            "Calf_L": (12, 0, 0),
            "Calf_R": (12, 0, 0),
        },
    },
    {
        "id": "06_KneeLiftStep",
        "label": "Knee Lift / Step",
        "camera": "SideLeft",
        "rotations": {
            "Pelvis": (0, 0, -4),
            "Spine": (0, 0, 4),
            "Chest": (0, -5, 0),
            "UpperArm_L": (-35, 0, -12),
            "UpperArm_R": (-35, 0, -18),
            "Forearm_L": (0, 0, 25),
            "Forearm_R": (0, 0, -25),
            "Thigh_L": (-35, 0, 0),
            "Thigh_R": (4, 0, 0),
            "Calf_L": (40, 0, 0),
            "Calf_R": (6, 0, 0),
        },
    },
    {
        "id": "07_WideStance",
        "label": "Wide Stance",
        "camera": "Front",
        "rotations": {
            "Pelvis": (2, 0, 0),
            "Spine": (-2, 0, 0),
            "UpperArm_L": (-25, 0, 12),
            "UpperArm_R": (-25, 0, -12),
            "Forearm_L": (0, 0, 18),
            "Forearm_R": (0, 0, -18),
            "Thigh_L": (0, 0, 10),
            "Thigh_R": (0, 0, -10),
            "Calf_L": (8, 0, 0),
            "Calf_R": (8, 0, 0),
        },
    },
    {
        "id": "08_TurnAndPoint",
        "label": "Turn and Point",
        "camera": "ThreeQuarterLeft",
        "rotations": {
            "Root": (0, 10, 0),
            "Pelvis": (0, -7, 0),
            "Spine": (0, 7, 0),
            "Chest": (0, 10, 0),
            "Neck": (0, 6, 0),
            "Head": (0, 6, 0),
            "UpperArm_L": (-40, 0, 5),
            "UpperArm_R": (-18, 0, -32),
            "Forearm_L": (0, 0, 20),
            "Forearm_R": (0, 0, -10),
            "Thigh_L": (-7, 0, 0),
            "Thigh_R": (6, 0, 0),
            "Calf_L": (8, 0, 0),
            "Calf_R": (12, 0, 0),
        },
    },
)


def sha256(path):
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


def parse_args():
    require("--" in sys.argv, "expected -- <r20.blend> <output-dir> <report>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    require(len(values) == 3, "expected r20 blend, output directory, report")
    blend_path, output_dir, report_path = map(os.path.abspath, values)
    require(blend_path.lower().endswith(".blend"), "input must be .blend")
    require(report_path.lower().endswith(".json"), "report must be .json")
    require(
        os.path.realpath(blend_path) != os.path.realpath(report_path),
        "report may not overwrite the input",
    )
    return blend_path, output_dir, report_path


def reset_pose(armature):
    for pose_bone in armature.pose.bones:
        if pose_bone.name.startswith("HipDeform_"):
            pose_bone.rotation_mode = "XYZ"
            pose_bone.scale = (1.0, 1.0, 1.0)
            continue
        pose_bone.matrix_basis = Matrix.Identity(4)
        pose_bone.rotation_mode = "XYZ"
        pose_bone.rotation_euler = (0.0, 0.0, 0.0)
        pose_bone.location = (0.0, 0.0, 0.0)
        pose_bone.scale = (1.0, 1.0, 1.0)
    bpy.context.view_layer.update()
    bpy.context.view_layer.update()


def apply_pose(armature, rotations):
    reset_pose(armature)
    for bone_name, degrees in rotations.items():
        require(bone_name in armature.pose.bones, f"missing pose bone: {bone_name}")
        require(not bone_name.startswith("HipDeform_"), "helper bone is driver-owned")
        pose_bone = armature.pose.bones[bone_name]
        pose_bone.rotation_mode = "XYZ"
        pose_bone.rotation_euler = Euler(
            tuple(math.radians(float(value)) for value in degrees), "XYZ"
        )
    bpy.context.view_layer.update()
    bpy.context.view_layer.update()


def evaluated_helper_record(armature):
    depsgraph = bpy.context.evaluated_depsgraph_get()
    evaluated = armature.evaluated_get(depsgraph)
    return {
        side: {
            "rotationXDegrees": math.degrees(
                float(evaluated.pose.bones[f"HipDeform_{side}"].rotation_euler.x)
            ),
            "location": [
                float(value)
                for value in evaluated.pose.bones[f"HipDeform_{side}"].location
            ],
            "scale": [
                float(value)
                for value in evaluated.pose.bones[f"HipDeform_{side}"].scale
            ],
        }
        for side in ("L", "R")
    }


def configure_scene(scene, r18):
    collection = bpy.data.collections.new("TMP_C1BRW020_PoseTestRender")
    scene.collection.children.link(collection)
    cameras = {
        "Front": r18.create_camera(
            collection, "TMP_POSE_CAM_Front", (0.0, -4.0, 0.50), (0, 0, 0.50), 1.34
        ),
        "ThreeQuarterRight": r18.create_camera(
            collection,
            "TMP_POSE_CAM_ThreeQuarterRight",
            (2.9, -2.9, 0.62),
            (0, 0, 0.50),
            1.38,
        ),
        "ThreeQuarterLeft": r18.create_camera(
            collection,
            "TMP_POSE_CAM_ThreeQuarterLeft",
            (-2.9, -2.9, 0.62),
            (0, 0, 0.50),
            1.38,
        ),
        "ThreeQuarterSide": r18.create_camera(
            collection,
            "TMP_POSE_CAM_ThreeQuarterSide",
            (-3.5, -1.8, 0.58),
            (0, 0, 0.48),
            1.36,
        ),
        "SideLeft": r18.create_camera(
            collection, "TMP_POSE_CAM_SideLeft", (-4.0, 0.0, 0.50), (0, 0, 0.50), 1.34
        ),
    }
    lights = [
        r18.create_area_light(
            collection, "TMP_POSE_LIGHT_Key", (-2.4, -3.0, 3.0), 420.0, 4.0
        ),
        r18.create_area_light(
            collection, "TMP_POSE_LIGHT_Fill", (2.8, -1.8, 1.6), 220.0, 3.0
        ),
        r18.create_area_light(
            collection, "TMP_POSE_LIGHT_Rim", (0.0, 2.8, 2.2), 300.0, 2.5
        ),
    ]
    r18.configure_render(scene)
    scene.render.resolution_x = RENDER_RESOLUTION
    scene.render.resolution_y = RENDER_RESOLUTION
    scene.render.resolution_percentage = 100
    return collection, cameras, lights


def main():
    blend_path, output_dir, report_path = parse_args()
    require(os.path.isfile(blend_path), "r20 input missing")
    require(sha256(R20_GENERATOR) == R20_GENERATOR_SHA256, "r20 generator drift")
    require(sha256(blend_path) == R20_BLEND_SHA256, "r20 blend drift")
    require(sha256(R16_BLEND) == R16_BLEND_SHA256, "r16 source drift")
    os.makedirs(output_dir, exist_ok=True)
    input_hash_before = sha256(blend_path)
    source_hash_before = sha256(R16_BLEND)
    r20 = import_file("c1b_rw020_pose_test", R20_GENERATOR)
    r18 = r20.r18

    bpy.ops.wm.open_mainfile(filepath=blend_path)
    scene = bpy.context.scene
    armature = bpy.data.objects[ARMATURE_NAME]
    body = bpy.data.objects[BODY_NAME]
    head = bpy.data.objects[HEAD_NAME]
    require(len(armature.data.bones) == 22, "unexpected r20 bone count")
    require(len(armature.animation_data.drivers) == 8, "helper driver count drift")
    require(len(bpy.data.actions) == 0, "pose tests require zero Actions")
    rest_coordinates = r18.r12.mesh_coordinates(body.data)
    rest_edges = r18.r12.mesh_edges(body.data)

    collection, cameras, lights = configure_scene(scene, r18)
    records = []
    try:
        for obj in bpy.data.objects:
            if obj.type == "MESH" and obj not in (body, head):
                obj.hide_render = True
        body.hide_render = False
        head.hide_render = False
        for pose in POSES:
            apply_pose(armature, pose["rotations"])
            camera = cameras[pose["camera"]]
            filename = f"C1B_R20_PoseTest_{pose['id']}_{pose['camera']}.png"
            path = os.path.join(output_dir, filename)
            r18.render_to(scene, camera, path)
            metrics = r18.mesh_pose_metrics(body, rest_coordinates, rest_edges)
            quality_pass = (
                metrics["manifold"]["result"] == "PASS"
                and metrics["fold"]["foldoverEdgeCountAt90Degrees"] == 0
                and metrics["fold"]["hardEdgeCountAt45Degrees"] == 0
                and metrics["selfIntersection"]["result"] == "PASS"
            )
            records.append(
                {
                    "id": pose["id"],
                    "label": pose["label"],
                    "camera": pose["camera"],
                    "rotationsLocalEulerXYZDegrees": pose["rotations"],
                    "helperEvaluation": evaluated_helper_record(armature),
                    "metrics": metrics,
                    "image": {
                        "filename": filename,
                        "path": path,
                        "bytes": os.path.getsize(path),
                        "sha256": sha256(path),
                    },
                    "result": "PASS" if quality_pass else "FAIL",
                }
            )
    finally:
        reset_pose(armature)
        r18.remove_objects([*cameras.values(), *lights])
        if scene.collection.children.get(collection.name) is not None:
            scene.collection.children.unlink(collection)
        if collection.users == 0:
            bpy.data.collections.remove(collection)

    input_hash_after = sha256(blend_path)
    source_hash_after = sha256(R16_BLEND)
    driver_count_after = len(armature.animation_data.drivers)
    action_count_after = len(bpy.data.actions)
    result = (
        all(record["result"] == "PASS" for record in records)
        and len(records) == len(POSES) == 8
        and input_hash_before == input_hash_after == R20_BLEND_SHA256
        and source_hash_before == source_hash_after == R16_BLEND_SHA256
        and driver_count_after == 8
        and action_count_after == 0
        and r18.pose_is_rest(armature)
    )
    report = {
        "mode": "TEMPORARY_STATIC_POSE_PREVIEW",
        "animationClipCreated": False,
        "keyframesCreated": False,
        "sourceSaved": False,
        "input": {
            "path": blend_path,
            "sha256Before": input_hash_before,
            "sha256After": input_hash_after,
            "unchanged": input_hash_before == input_hash_after,
        },
        "r16": {
            "path": R16_BLEND,
            "sha256Before": source_hash_before,
            "sha256After": source_hash_after,
            "unchanged": source_hash_before == source_hash_after,
        },
        "driverCountBeforeAndAfter": [8, driver_count_after],
        "actionCountBeforeAndAfter": [0, action_count_after],
        "savedInRestPose": r18.pose_is_rest(armature),
        "poses": records,
        "result": "PASS" if result else "FAIL",
    }
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")
    print("R20_POSE_TEST_RESULT=" + report["result"])
    print("R20_POSE_TEST_REPORT=" + report_path)
    if not result:
        raise RuntimeError("one or more temporary pose previews failed QA")


if __name__ == "__main__":
    main()
