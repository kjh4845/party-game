#!/usr/bin/env python3

import hashlib
import json
import math

import bpy
from mathutils import Vector


REQUIRED_POSES = (
    "Neutral",
    "BothHandsGrab",
    "StrikeReady_L",
    "StrikeReady_R",
    "AirKick_L",
    "AirKick_R",
    "Dropkick",
    "AirHandReach",
)
REQUIRED_LINEUPS = ("Lineup_Overlap", "Lineup_Spread")
BASE_PARTS = {
    "Head": "CHR_C1B003_Head",
    "Torso": "CHR_C1B003_Torso",
    "Arm_L": "CHR_C1B003_Arm_L",
    "Arm_R": "CHR_C1B003_Arm_R",
    "Leg_L": "CHR_C1B003_Leg_L",
    "Leg_R": "CHR_C1B003_Leg_R",
}
TERMINAL_POINTS = {
    "Arm_L": Vector((-0.235, -0.005, 0.405)),
    "Arm_R": Vector((0.235, -0.005, 0.405)),
    "Leg_L": Vector((-0.110, -0.012, 0.065)),
    "Leg_R": Vector((0.110, -0.012, 0.065)),
}


def normalized(value):
    if isinstance(value, float):
        return round(value, 9)
    return value


def vector_values(vector):
    return [normalized(float(component)) for component in vector]


def matrix_values(matrix):
    return [[normalized(float(value)) for value in row] for row in matrix]


def mesh_fingerprint(mesh):
    digest = hashlib.sha256()
    for vertex in mesh.vertices:
        digest.update(
            ("v:" + ",".join(f"{value:.9f}" for value in vertex.co) + "\n").encode("ascii")
        )
    for polygon in mesh.polygons:
        digest.update(("p:" + ",".join(str(index) for index in polygon.vertices) + "\n").encode("ascii"))
    return digest.hexdigest()


def scenario_collections():
    result = {}
    for collection in bpy.data.collections:
        scenario_id = collection.get("c1b004_scenario_id")
        if scenario_id:
            result[str(scenario_id)] = collection
    return result


def scenario_roots(scenario_id):
    return sorted(
        [
            obj
            for obj in bpy.data.objects
            if obj.type == "EMPTY"
            and obj.get("c1b004_scenario_id") == scenario_id
            and obj.get("c1b004_review_only") is True
        ],
        key=lambda obj: int(obj.get("c1b004_instance_index", 0)),
    )


def scenario_mesh_objects(scenario_id, instance_index=None):
    result = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.get("c1b004_scenario_id") == scenario_id
        and obj.get("c1b004_review_only") is True
    ]
    if instance_index is not None:
        result = [obj for obj in result if int(obj.get("c1b004_instance_index", -1)) == instance_index]
    return sorted(result, key=lambda obj: str(obj.get("c1b004_body_part", "")))


def object_for_part(scenario_id, part, instance_index=0):
    matches = [
        obj
        for obj in scenario_mesh_objects(scenario_id, instance_index)
        if obj.get("c1b004_body_part") == part
    ]
    if len(matches) != 1:
        return None
    return matches[0]


def geometry_contract(obj, part, base_meshes):
    base = base_meshes.get(part)
    if base is None:
        return {"mode": None, "linkedToBase": False, "validBaseDerivation": False}
    mode = str(obj.get("c1b004_geometry_mode", ""))
    linked = obj.data.name == base["meshDatablock"]
    capped = (
        mode == "BASE_PLUS_PROXIMAL_CAP"
        and obj.data.get("c1b004_geometry_mode") == "BASE_PLUS_PROXIMAL_CAP"
        and obj.data.get("c1b004_derived_from_mesh_datablock") == base["meshDatablock"]
        and int(obj.data.get("c1b004_added_proximal_cap_polygons", -1)) == 1
        and len(obj.data.vertices) == base["vertices"]
        and len(obj.data.polygons) == base["polygons"] + 1
    )
    return {
        "mode": mode,
        "linkedToBase": linked,
        "validBaseDerivation": (mode == "BASE_LINKED" and linked) or capped,
        "vertices": len(obj.data.vertices),
        "polygons": len(obj.data.polygons),
    }


def terminal_position(scenario_id, part, instance_index=0):
    obj = object_for_part(scenario_id, part, instance_index)
    if obj is None:
        return None
    return obj.matrix_world @ TERMINAL_POINTS[part]


def scenario_bounds(scenario_id):
    points = []
    for obj in scenario_mesh_objects(scenario_id):
        for vertex in obj.data.vertices:
            points.append(obj.matrix_world @ vertex.co)
    if not points:
        return None
    minimum = Vector((min(point.x for point in points), min(point.y for point in points), min(point.z for point in points)))
    maximum = Vector((max(point.x for point in points), max(point.y for point in points), max(point.z for point in points)))
    return {
        "minimum": vector_values(minimum),
        "maximum": vector_values(maximum),
        "size": vector_values(maximum - minimum),
    }


def mirror_deviation(left, right):
    if left is None or right is None:
        return None
    return normalized(max(abs(left.x + right.x), abs(left.y - right.y), abs(left.z - right.z)))


def camera_record(camera):
    look_direction = camera.rotation_euler.to_matrix() @ Vector((0.0, 0.0, -1.0))
    target = Vector(camera.get("target_blender", (0.0, 0.0, 0.0)))
    offset = target - camera.location
    optical_axis_deviation = 0.0
    if offset.length > 0.0:
        optical_axis_deviation = (offset.normalized() - look_direction.normalized()).length
    return {
        "name": camera.name,
        "type": camera.data.type,
        "orthoScale": normalized(float(camera.data.ortho_scale)),
        "location": vector_values(camera.location),
        "rotationEuler": vector_values(camera.rotation_euler),
        "lookDirection": vector_values(look_direction.normalized()),
        "target": vector_values(target),
        "opticalAxisDeviation": normalized(float(optical_axis_deviation)),
    }


def main():
    scene = bpy.context.scene
    bpy.context.view_layer.update()
    scenarios = scenario_collections()
    render_jobs = json.loads(scene.get("render_jobs_json", "[]"))

    base_meshes = {}
    for part, object_name in BASE_PARTS.items():
        obj = bpy.data.objects.get(object_name)
        if obj is not None and obj.type == "MESH":
            base_meshes[part] = {
                "object": obj.name,
                "meshDatablock": obj.data.name,
                "vertices": len(obj.data.vertices),
                "polygons": len(obj.data.polygons),
                "geometrySha256": mesh_fingerprint(obj.data),
            }

    pose_records = []
    linked_mesh_mismatches = []
    for pose_id in REQUIRED_POSES:
        roots = scenario_roots(pose_id)
        objects = scenario_mesh_objects(pose_id, 0)
        parts = {}
        for obj in objects:
            part = str(obj.get("c1b004_body_part", ""))
            contract = geometry_contract(obj, part, base_meshes)
            if not contract["validBaseDerivation"]:
                linked_mesh_mismatches.append(obj.name)
            parts[part] = {
                "object": obj.name,
                "meshDatablock": obj.data.name,
                **contract,
                "matrixLocal": matrix_values(obj.matrix_local),
            }
        terminal_positions = {
            part: vector_values(position)
            for part in TERMINAL_POINTS
            if (position := terminal_position(pose_id, part)) is not None
        }
        pose_records.append(
            {
                "poseId": pose_id,
                "collectionExists": pose_id in scenarios,
                "rootCount": len(roots),
                "rootScale": vector_values(roots[0].scale) if len(roots) == 1 else None,
                "rootDisplayLocation": vector_values(roots[0].location) if len(roots) == 1 else None,
                "rootDisplayRotation": vector_values(roots[0].rotation_euler) if len(roots) == 1 else None,
                "meshObjectCount": len(objects),
                "parts": parts,
                "terminalPositions": terminal_positions,
                "bounds": scenario_bounds(pose_id),
            }
        )

    lineup_records = []
    for lineup_id in REQUIRED_LINEUPS:
        roots = scenario_roots(lineup_id)
        instances = []
        for root in roots:
            index = int(root.get("c1b004_instance_index", 0))
            objects = scenario_mesh_objects(lineup_id, index)
            parts = {}
            for obj in objects:
                part = str(obj.get("c1b004_body_part", ""))
                contract = geometry_contract(obj, part, base_meshes)
                if not contract["validBaseDerivation"]:
                    linked_mesh_mismatches.append(obj.name)
                parts[part] = {"meshDatablock": obj.data.name, **contract}
            instances.append(
                {
                    "instanceIndex": index,
                    "rootLocation": vector_values(root.location),
                    "rootScale": vector_values(root.scale),
                    "meshObjectCount": len(objects),
                    "parts": parts,
                }
            )
        lineup_records.append(
            {
                "lineupId": lineup_id,
                "collectionExists": lineup_id in scenarios,
                "participantCountDeclared": int(
                    scenarios[lineup_id].get("c1b004_participant_count", -1)
                )
                if lineup_id in scenarios
                else None,
                "rootCount": len(roots),
                "instances": instances,
                "bounds": scenario_bounds(lineup_id),
            }
        )

    neutral_arm_l = terminal_position("Neutral", "Arm_L")
    neutral_arm_r = terminal_position("Neutral", "Arm_R")
    neutral_leg_l = terminal_position("Neutral", "Leg_L")
    neutral_leg_r = terminal_position("Neutral", "Leg_R")
    readability = {
        "grabForwardDeltaLeftH": normalized(
            neutral_arm_l.y - terminal_position("BothHandsGrab", "Arm_L").y
        ),
        "grabForwardDeltaRightH": normalized(
            neutral_arm_r.y - terminal_position("BothHandsGrab", "Arm_R").y
        ),
        "strikeReadyBackDeltaLeftH": normalized(
            terminal_position("StrikeReady_L", "Arm_L").y - neutral_arm_l.y
        ),
        "strikeReadyBackDeltaRightH": normalized(
            terminal_position("StrikeReady_R", "Arm_R").y - neutral_arm_r.y
        ),
        "airKickForwardDeltaLeftH": normalized(
            neutral_leg_l.y - terminal_position("AirKick_L", "Leg_L").y
        ),
        "airKickForwardDeltaRightH": normalized(
            neutral_leg_r.y - terminal_position("AirKick_R", "Leg_R").y
        ),
        "dropkickForwardDeltaLeftH": normalized(
            neutral_leg_l.y - terminal_position("Dropkick", "Leg_L").y
        ),
        "dropkickForwardDeltaRightH": normalized(
            neutral_leg_r.y - terminal_position("Dropkick", "Leg_R").y
        ),
        "airReachHeightDeltaLeftH": normalized(
            terminal_position("AirHandReach", "Arm_L").z - neutral_arm_l.z
        ),
        "airReachHeightDeltaRightH": normalized(
            terminal_position("AirHandReach", "Arm_R").z - neutral_arm_r.z
        ),
        "strikeMirrorMaximumDeviationH": mirror_deviation(
            terminal_position("StrikeReady_L", "Arm_L"),
            terminal_position("StrikeReady_R", "Arm_R"),
        ),
        "kickMirrorMaximumDeviationH": mirror_deviation(
            terminal_position("AirKick_L", "Leg_L"),
            terminal_position("AirKick_R", "Leg_R"),
        ),
        "grabMirrorMaximumDeviationH": mirror_deviation(
            terminal_position("BothHandsGrab", "Arm_L"),
            terminal_position("BothHandsGrab", "Arm_R"),
        ),
        "dropkickMirrorMaximumDeviationH": mirror_deviation(
            terminal_position("Dropkick", "Leg_L"),
            terminal_position("Dropkick", "Leg_R"),
        ),
        "airReachMirrorMaximumDeviationH": mirror_deviation(
            terminal_position("AirHandReach", "Arm_L"),
            terminal_position("AirHandReach", "Arm_R"),
        ),
    }

    cameras = sorted(
        [camera_record(obj) for obj in bpy.data.objects if obj.type == "CAMERA" and obj.name.startswith("CAM_C1B004_")],
        key=lambda entry: entry["name"],
    )
    c1b004_model_objects = [
        obj for obj in bpy.data.objects if obj.type == "MESH" and obj.get("c1b004_review_only") is True
    ]
    proximal_cap_meshes = [
        mesh
        for mesh in bpy.data.meshes
        if mesh.get("c1b004_geometry_mode") == "BASE_PLUS_PROXIMAL_CAP"
    ]
    proximal_cap_review_objects = [
        obj
        for obj in c1b004_model_objects
        if obj.get("c1b004_geometry_mode") == "BASE_PLUS_PROXIMAL_CAP"
    ]
    negative_scale_objects = [
        obj.name for obj in bpy.data.objects if any(component < 0.0 for component in obj.scale)
    ]
    non_unit_review_roots = [
        root.name
        for scenario_id in (*REQUIRED_POSES, *REQUIRED_LINEUPS)
        for root in scenario_roots(scenario_id)
        if max(abs(component - 1.0) for component in root.scale) > 0.0000001
    ]
    forbidden_visible_mesh_tokens = ("hand", "finger", "fist", "foot", "shoe", "toe")
    separate_hand_foot_meshes = [
        obj.name
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.get("c1b004_body_part") not in BASE_PARTS
        and any(token in obj.name.lower() for token in forbidden_visible_mesh_tokens)
    ]
    collider_objects = [
        obj.name
        for obj in bpy.data.objects
        if "collider" in obj.name.lower() or obj.get("collider_role") is not None
    ]
    external_images = [
        image.filepath
        for image in bpy.data.images
        if image.source == "FILE" and image.filepath
    ]
    packed_images = [image.name for image in bpy.data.images if image.packed_file is not None]

    payload = {
        "file": bpy.data.filepath,
        "scene": {
            "name": scene.name,
            "assetId": scene.get("asset_id"),
            "assetVersion": scene.get("asset_version"),
            "ownerTask": scene.get("owner_task"),
            "sourceOwner": scene.get("source_owner"),
            "profileId": scene.get("profile_id"),
            "profileRevision": scene.get("profile_revision"),
            "measurementSetSha256": scene.get("measurement_set_sha256"),
            "state": scene.get("state"),
            "candidateStatus": scene.get("candidate_status"),
            "derivedFromTask": scene.get("derived_from_task"),
            "derivedFromSourceSha256": scene.get("derived_from_source_sha256"),
            "sameGeometryLineage": scene.get("same_geometry_lineage"),
            "userVisualApprovalRecorded": scene.get("user_visual_approval_recorded"),
            "lockedValueCount": scene.get("locked_value_count"),
            "gameplayRigAuthored": scene.get("gameplay_rig_authored"),
            "gameplayColliderAuthored": scene.get("gameplay_collider_authored"),
            "gameplayAnchorAuthored": scene.get("gameplay_anchor_authored"),
            "rootMotionAuthored": scene.get("root_motion_authored"),
            "physicsOrHitSemanticsAuthored": scene.get("physics_or_hit_semantics_authored"),
            "fbxExportExecuted": scene.get("fbx_export_executed"),
            "unityImportExecuted": scene.get("unity_import_executed"),
            "poseReviewGeometryMode": scene.get("pose_review_geometry_mode"),
            "proximalCapMeshCount": scene.get("proximal_cap_mesh_count"),
            "addedPolygonsPerProximalCapMesh": scene.get(
                "added_polygons_per_proximal_cap_mesh"
            ),
            "productionTopologyApproved": scene.get("production_topology_approved"),
        },
        "counts": {
            "objects": len(bpy.data.objects),
            "collections": len(bpy.data.collections),
            "meshObjects": len([obj for obj in bpy.data.objects if obj.type == "MESH"]),
            "c1b004ModelMeshObjects": len(c1b004_model_objects),
            "meshDatablocks": len(bpy.data.meshes),
            "proximalCapMeshDatablocks": len(proximal_cap_meshes),
            "proximalCapDerivedReviewMeshObjects": len(proximal_cap_review_objects),
            "materials": len(bpy.data.materials),
            "cameras": len(bpy.data.cameras),
            "c1b004Cameras": len(cameras),
            "lights": len(bpy.data.lights),
            "worlds": len(bpy.data.worlds),
            "armatures": len(bpy.data.armatures),
            "actions": len(bpy.data.actions),
            "scenes": len(bpy.data.scenes),
            "embeddedTextBlocks": len(bpy.data.texts),
            "externalImages": len(external_images),
            "packedImages": len(packed_images),
            "externalLibraries": len(bpy.data.libraries),
            "externalFonts": len([font for font in bpy.data.fonts if font.filepath]),
            "externalSounds": len([sound for sound in bpy.data.sounds if sound.filepath]),
            "externalMovieClips": len([clip for clip in bpy.data.movieclips if clip.filepath]),
            "colliderObjects": len(collider_objects),
            "separateHandFootMeshObjects": len(separate_hand_foot_meshes),
            "negativeScaleObjects": len(negative_scale_objects),
            "nonUnitReviewRoots": len(non_unit_review_roots),
        },
        "baseMeshes": base_meshes,
        "proximalCapMeshes": sorted(
            [
                {
                    "meshDatablock": mesh.name,
                    "derivedFromMeshDatablock": mesh.get("c1b004_derived_from_mesh_datablock"),
                    "vertices": len(mesh.vertices),
                    "polygons": len(mesh.polygons),
                    "addedProximalCapPolygons": int(
                        mesh.get("c1b004_added_proximal_cap_polygons", -1)
                    ),
                    "geometrySha256": mesh_fingerprint(mesh),
                }
                for mesh in proximal_cap_meshes
            ],
            key=lambda entry: entry["meshDatablock"],
        ),
        "poses": pose_records,
        "lineups": lineup_records,
        "readability": readability,
        "cameras": cameras,
        "renderJobs": render_jobs,
        "linkedMeshMismatchObjects": sorted(set(linked_mesh_mismatches)),
        "negativeScaleObjects": negative_scale_objects,
        "nonUnitReviewRoots": non_unit_review_roots,
        "separateHandFootMeshObjects": separate_hand_foot_meshes,
        "colliderObjects": collider_objects,
        "externalImages": external_images,
        "packedImages": packed_images,
    }
    print("C1B004_INSPECTION_JSON=" + json.dumps(payload, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
