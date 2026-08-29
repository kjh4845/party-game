#!/usr/bin/env python3

"""Export the C1B-005 neutral blockout without mutating its Blender source.

Run with Blender 5.2 LTS:

    blender --background --factory-startup \
      --python tools/blender/export_c1b005_neutral_fbx.py -- \
      <C1B-004-source.blend> <output.fbx>

Only the Neutral BASE_LINKED six-part character and the seventeen measurement
landmarks are copied into a transient hierarchy.  The source file is opened but
never saved. Blender 5.2 writes process-dependent FBX object IDs and a creation
timestamp, so a rerun is verified semantically by the companion inspector rather
than promised to reproduce the same binary SHA-256.
"""

import hashlib
import json
import os
import sys

import bpy
import bmesh
from mathutils import Matrix


EXPECTED_SOURCE_SHA256 = (
    "83c2e100c74cf75a7faed11dd0ad65c3d07677684e02696e72455fdee4e17c2b"
)
EXPECTED_PROFILE_ID = "CharacterProportionProfile-C1B-002-r01"
EXPECTED_MEASUREMENT_SET_SHA256 = (
    "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722"
)
MODEL_INTEROP_PROFILE_ID = "ModelInteropProfile-ART-001-r02"
BLENDER_EXPORT_PRESET_ID = "PHX-FBX-MODEL-r01"
BLENDER_EXPORT_OVERRIDE_ID = "PHX-FBX-C1B-BLOCKOUT-r02"
BLENDER_EXPORT_SETTINGS_SHA256 = (
    "5707996b33f8ac6773e309c60d05b236655bb4b17e4cc3261642f142a0062ce4"
)
BLENDER_EXPORT_OVERRIDE_SETTINGS_SHA256 = (
    "21b50c577b30f79d5717806f0687550c267181ddb3e8d0b9b9213e6133a02f29"
)

PARTS = ("Head", "Torso", "Arm_L", "Arm_R", "Leg_L", "Leg_R")
LANDMARK_IDS = (
    "Crown",
    "Chin",
    "Shoulder_L",
    "Shoulder_R",
    "Elbow_L",
    "Elbow_R",
    "ForearmTerminal_L",
    "ForearmTerminal_R",
    "Chest",
    "Pelvis",
    "Crotch",
    "Hip_L",
    "Hip_R",
    "Knee_L",
    "Knee_R",
    "LowerLegTerminal_L",
    "LowerLegTerminal_R",
)

FBX_SETTINGS = {
    "global_scale": 1.0,
    "apply_unit_scale": True,
    "apply_scale_options": "FBX_SCALE_UNITS",
    "use_space_transform": True,
    "bake_space_transform": True,
    "axis_forward": "-Z",
    "axis_up": "Y",
    "use_selection": True,
    "object_types": {"ARMATURE", "EMPTY", "MESH"},
    "use_mesh_modifiers": True,
    "mesh_smooth_type": "FACE",
    "use_tspace": True,
    "add_leaf_bones": False,
    "use_armature_deform_only": True,
    "primary_bone_axis": "Y",
    "secondary_bone_axis": "X",
    "bake_anim": False,
    "path_mode": "STRIP",
    "embed_textures": False,
    "colors_type": "SRGB",
}


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <C1B-004-source.blend> <output.fbx>")
    custom = sys.argv[sys.argv.index("--") + 1 :]
    if len(custom) != 2:
        raise RuntimeError("expected source blend and output FBX paths")
    source_path, output_path = map(os.path.abspath, custom)
    if not source_path.lower().endswith(".blend"):
        raise RuntimeError("source path must be a .blend file")
    if not output_path.lower().endswith(".fbx"):
        raise RuntimeError("output path must be a .fbx file")
    if source_path == output_path:
        raise RuntimeError("source and output paths must differ")
    return source_path, output_path


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def operator_settings_are_supported():
    properties = {
        prop.identifier: prop
        for prop in bpy.ops.export_scene.fbx.get_rna_type().properties
        if prop.identifier != "rna_type"
    }
    missing = sorted(set(FBX_SETTINGS) - set(properties))
    require(not missing, f"PHX-FBX-MODEL-r01 unsupported properties: {missing}")

    enum_values = {
        "apply_scale_options": "FBX_SCALE_UNITS",
        "axis_forward": "-Z",
        "axis_up": "Y",
        "mesh_smooth_type": "FACE",
        "primary_bone_axis": "Y",
        "secondary_bone_axis": "X",
        "path_mode": "STRIP",
        "colors_type": "SRGB",
    }
    for identifier, value in enum_values.items():
        allowed = {item.identifier for item in properties[identifier].enum_items}
        require(
            value in allowed,
            f"PHX-FBX-MODEL-r01 enum unavailable: {identifier}={value}",
        )


def neutral_source_objects():
    result = {}
    candidates = [
        obj
        for obj in bpy.data.objects
        if obj.type == "MESH"
        and obj.get("c1b004_scenario_id") == "Neutral"
        and int(obj.get("c1b004_instance_index", -1)) == 0
        and obj.get("c1b004_review_only") is True
    ]
    for obj in candidates:
        part = str(obj.get("c1b004_body_part", ""))
        require(part in PARTS, f"unexpected Neutral body part: {obj.name} ({part})")
        require(part not in result, f"duplicate Neutral body part: {part}")
        require(
            obj.get("c1b004_geometry_mode") == "BASE_LINKED",
            f"Neutral part is not BASE_LINKED: {obj.name}",
        )
        base_object_name = str(obj.get("c1b004_base_object", ""))
        base_object = bpy.data.objects.get(base_object_name)
        require(
            base_object is not None and base_object.type == "MESH",
            f"missing C1B-003 base object for {obj.name}",
        )
        require(obj.data is base_object.data, f"Neutral geometry lineage mismatch: {obj.name}")
        require(
            tuple(round(float(value), 9) for value in obj.scale) == (1.0, 1.0, 1.0),
            f"Neutral part has non-unit scale: {obj.name}",
        )
        require(not obj.modifiers, f"Neutral part has unexpected modifiers: {obj.name}")
        result[part] = obj
    require(set(result) == set(PARTS), f"Neutral part set mismatch: {sorted(result)}")
    return result


def source_landmarks():
    result = {}
    for obj in bpy.data.objects:
        landmark_id = obj.get("c1b_landmark_id")
        if landmark_id is None:
            continue
        landmark_id = str(landmark_id)
        require(landmark_id in LANDMARK_IDS, f"unexpected source landmark: {landmark_id}")
        require(landmark_id not in result, f"duplicate source landmark: {landmark_id}")
        require(obj.type == "EMPTY", f"landmark is not an Empty: {obj.name}")
        result[landmark_id] = obj
    require(set(result) == set(LANDMARK_IDS), f"landmark set mismatch: {sorted(result)}")
    return result


def create_transient_export_hierarchy(neutral_objects, landmark_objects):
    collection = bpy.data.collections.new("C1B005_Export_Transient")
    bpy.context.scene.collection.children.link(collection)

    root = bpy.data.objects.new("CHR_C1B005_ExportRoot", None)
    collection.objects.link(root)
    root.matrix_world = Matrix.Identity(4)
    root.empty_display_type = "PLAIN_AXES"
    root.empty_display_size = 0.08
    root["asset_id"] = "CHR_MasterCharacter_C1B_Neutral"
    root["asset_version"] = "0.2.0-start"
    root["owner_task"] = "C1B-005"
    root["profile_id"] = EXPECTED_PROFILE_ID
    root["measurement_set_sha256"] = EXPECTED_MEASUREMENT_SET_SHA256
    root["model_interop_profile_id"] = MODEL_INTEROP_PROFILE_ID
    root["blender_export_preset_id"] = BLENDER_EXPORT_PRESET_ID
    root["blender_export_override_id"] = BLENDER_EXPORT_OVERRIDE_ID
    root["blender_export_override_settings_sha256"] = (
        BLENDER_EXPORT_OVERRIDE_SETTINGS_SHA256
    )
    root["transient_handedness_reflection"] = "X"
    root["canonical_source_mutated"] = False
    root["source_pivot"] = "Neutral midpoint between lower-leg ground contacts"
    root["character_forward_axis_blender"] = "-Y"
    root["up_axis_blender"] = "+Z"
    root["gameplay_rig_authored"] = False
    root["gameplay_collider_authored"] = False
    root["animation_authored"] = False

    exported = [root]
    part_records = []
    for part in PARTS:
        source = neutral_objects[part]
        mesh = source.data.copy()
        mesh.name = f"CHR_C1B005_{part}_Mesh"
        mesh.materials.clear()
        # PHX-FBX-C1B-BLOCKOUT-r02 performs handedness transport only on the
        # transient export copy. Reflecting X compensates Unity's FBX handedness
        # conversion; reversing every face preserves outward winding/normals.
        for vertex in mesh.vertices:
            vertex.co.x = -vertex.co.x
        edit_mesh = bmesh.new()
        try:
            edit_mesh.from_mesh(mesh)
            bmesh.ops.reverse_faces(edit_mesh, faces=list(edit_mesh.faces))
            edit_mesh.normal_update()
            edit_mesh.to_mesh(mesh)
        finally:
            edit_mesh.free()
        mesh.update(calc_edges=True)
        clone = bpy.data.objects.new(f"CHR_C1B005_{part}", mesh)
        collection.objects.link(clone)
        clone.parent = root
        clone.matrix_world = source.matrix_world.copy()
        clone["c1b005_body_part"] = part
        clone["c1b005_geometry_mode"] = "NEUTRAL_BASE_GEOMETRY_COPY"
        clone["c1b005_source_object"] = source.name
        clone["c1b005_source_mesh"] = source.data.name
        clone["profile_id"] = EXPECTED_PROFILE_ID
        clone["c1b005_transient_reflect_x"] = True
        clone["c1b005_polygon_winding_reversed"] = True
        require(len(mesh.materials) == 0, f"material slot leaked into {clone.name}")
        require(len(mesh.vertices) == 193, f"unexpected vertex count for {part}")
        require(len(mesh.polygons) == 192, f"unexpected polygon count for {part}")
        exported.append(clone)
        part_records.append(
            {
                "part": part,
                "object": clone.name,
                "vertices": len(mesh.vertices),
                "polygons": len(mesh.polygons),
                "materialSlots": len(mesh.materials),
            }
        )

    for landmark_id in LANDMARK_IDS:
        source = landmark_objects[landmark_id]
        # The C1B-004 scene already owns the canonical LM_* object names.  Rename
        # those excluded source objects only in memory so the exported copies keep
        # stable logical names instead of Blender's incidental ".001" suffix.
        source.name = f"C1B005_SourceExcluded_LM_{landmark_id}"
        clone = bpy.data.objects.new(f"LM_{landmark_id}", None)
        collection.objects.link(clone)
        clone.parent = root
        clone.matrix_world = source.matrix_world.copy()
        clone.location.x = -clone.location.x
        clone.empty_display_type = "SPHERE"
        clone.empty_display_size = 0.008
        clone["c1b_landmark_id"] = landmark_id
        exported.append(clone)

    return root, exported, part_records


def select_only(objects, active):
    bpy.ops.object.select_all(action="DESELECT")
    for obj in objects:
        obj.hide_set(False)
        obj.hide_viewport = False
        obj.select_set(True)
    bpy.context.view_layer.objects.active = active
    bpy.context.view_layer.update()
    selected_names = {obj.name for obj in bpy.context.selected_objects}
    require(selected_names == {obj.name for obj in objects}, "export selection drift")


def export_atomic(output_path):
    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    temporary_path = output_path + ".partial.fbx"
    if os.path.exists(temporary_path):
        os.remove(temporary_path)
    try:
        result = bpy.ops.export_scene.fbx(
            filepath=temporary_path,
            check_existing=False,
            **FBX_SETTINGS,
        )
        require(result == {"FINISHED"}, f"FBX export did not finish: {result}")
        require(os.path.isfile(temporary_path), "FBX exporter did not create output")
        require(os.path.getsize(temporary_path) > 1024, "FBX output is unexpectedly small")
        with open(temporary_path, "rb") as handle:
            require(
                handle.read(18).startswith(b"Kaydara FBX Binary"),
                "FBX output is not a binary FBX",
            )
        os.replace(temporary_path, output_path)
    finally:
        if os.path.exists(temporary_path):
            os.remove(temporary_path)


def main():
    source_path, output_path = parse_args()
    require(os.path.isfile(source_path), f"missing source: {source_path}")
    source_hash_before = sha256_file(source_path)
    require(
        source_hash_before == EXPECTED_SOURCE_SHA256,
        "C1B-004 source SHA does not match the immutable r02 baseline",
    )
    operator_settings_are_supported()

    bpy.ops.wm.open_mainfile(filepath=source_path)
    require(
        bpy.context.scene.get("profile_id") == EXPECTED_PROFILE_ID,
        "source Character proportion profile mismatch",
    )
    require(
        bpy.context.scene.get("measurement_set_sha256")
        == EXPECTED_MEASUREMENT_SET_SHA256,
        "source measurement set mismatch",
    )
    require(len(bpy.data.armatures) == 0, "source unexpectedly contains an Armature")
    require(len(bpy.data.actions) == 0, "source unexpectedly contains an Action")

    neutral_objects = neutral_source_objects()
    landmark_objects = source_landmarks()
    root, exported_objects, part_records = create_transient_export_hierarchy(
        neutral_objects, landmark_objects
    )
    require(len(exported_objects) == 24, "expected root + mesh6 + landmark17")
    select_only(exported_objects, root)
    export_atomic(output_path)

    source_hash_after = sha256_file(source_path)
    require(source_hash_after == source_hash_before, "source file changed during export")
    output_hash = sha256_file(output_path)
    summary = {
        "result": "PASS",
        "sourcePath": source_path,
        "sourceSha256Before": source_hash_before,
        "sourceSha256After": source_hash_after,
        "sourceUnchanged": True,
        "outputPath": output_path,
        "outputBytes": os.path.getsize(output_path),
        "outputSha256": output_hash,
        "binaryByteIdenticalReexportGuaranteed": False,
        "binaryVariabilitySource": "Blender 5.2 FBX UUID hash seed and CreationTimeStamp",
        "semanticReproductionVerifier": "tools/blender/inspect_c1b005_fbx.py",
        "modelInteropProfileId": MODEL_INTEROP_PROFILE_ID,
        "blenderExportPresetId": BLENDER_EXPORT_PRESET_ID,
        "blenderExportOverrideId": BLENDER_EXPORT_OVERRIDE_ID,
        "blenderExportOverrideSettingsSha256": BLENDER_EXPORT_OVERRIDE_SETTINGS_SHA256,
        "transientReflectAxis": "X",
        "polygonWindingReversed": True,
        "canonicalSourceMutated": False,
        "blenderExportSettingsSha256": BLENDER_EXPORT_SETTINGS_SHA256,
        "selectedObjectCount": len(exported_objects),
        "meshCount": len(PARTS),
        "landmarkCount": len(LANDMARK_IDS),
        "armatureCount": 0,
        "actionCount": 0,
        "materialSlotCount": 0,
        "parts": part_records,
    }
    print("C1B005_EXPORT_JSON=" + json.dumps(summary, sort_keys=True, separators=(",", ":")))
    print("C1B005_EXPORT_RESULT=PASS")


if __name__ == "__main__":
    main()
