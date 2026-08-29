#!/usr/bin/env python3

import hashlib
import json
import math
import os
import re
import sys

import bpy
from mathutils import Vector


REPORT_PREFIX = "C1B005_FBX_INSPECTION_JSON="
EXPECTED_BLENDER_VERSION = "5.2.0 LTS"
START_TOLERANCE_H = 0.005
TRANSFORM_TOLERANCE = 0.00001
QUANTIZATION = 1_000_000
ROOT_NAME = "CHR_C1B005_ExportRoot"
PARTS = ("Head", "Torso", "Arm_L", "Arm_R", "Leg_L", "Leg_R")
MESH_NAMES = {part: f"CHR_C1B005_{part}" for part in PARTS}
LANDMARK_POSITIONS = {
    # PHX-FBX-C1B-BLOCKOUT-r02 transport coordinates after transient ReflectX
    # and baked space conversion. Unity's bakeAxisConversion=false resolves
    # these back to the normalized Unity-frame profile coordinates.
    "Crown": (0.0, 0.0, 1.0),
    "Chin": (0.0, 0.0, 0.800),
    "Shoulder_L": (0.205, 0.0, 0.690),
    "Shoulder_R": (-0.205, 0.0, 0.690),
    "Elbow_L": (0.235, 0.0, 0.520),
    "Elbow_R": (-0.235, 0.0, 0.520),
    "ForearmTerminal_L": (0.235, -0.005, 0.405),
    "ForearmTerminal_R": (-0.235, -0.005, 0.405),
    "Chest": (0.0, 0.0, 0.585),
    "Pelvis": (0.0, 0.0, 0.395),
    "Crotch": (0.0, 0.0, 0.310),
    "Hip_L": (0.095, 0.0, 0.315),
    "Hip_R": (-0.095, 0.0, 0.315),
    "Knee_L": (0.105, 0.0, 0.170),
    "Knee_R": (-0.105, 0.0, 0.170),
    "LowerLegTerminal_L": (0.110, -0.012, 0.065),
    "LowerLegTerminal_R": (-0.110, -0.012, 0.065),
}
EXPECTED_BOUNDS = {
    "heightH": 1.0,
    "frontViewFullWidthH": 0.58,
    "sideViewTotalDepthH": 0.265,
    "groundMinimumH": 0.0,
    "crownMaximumH": 1.0,
}
EXPECTED_MESH_CONTRACT = {
    # Filled from the immutable C1B-003 canonical geometry after an r01 FBX
    # export/import round trip. The signatures deliberately do not use FBX
    # custom properties, which Blender's FBX importer does not preserve.
    "Head": {
        "vertices": 193,
        "edges": 384,
        "polygons": 192,
        "loops": 744,
        "geometryTopologySha256": "0c3105c2f744a4e2b4220877881826c2543785e46d16be76aae62a755e9fb998",
        "orientedGeometryTopologySha256": "b3c63dbd01bd0b55404de9a92472ee41b8cffe5d7f8b3677dc9ba42eeef450fd",
    },
    "Torso": {
        "vertices": 193,
        "edges": 384,
        "polygons": 192,
        "loops": 744,
        "geometryTopologySha256": "1c55614057aa344726629526be2ec27ef7956bbe63074ac59bdeedc5746e6ebe",
        "orientedGeometryTopologySha256": "5615f7f1a2ace729c35d1776d78076cb53e26538ca3340767b5d6b8e995eb820",
    },
    "Arm_L": {
        "vertices": 193,
        "edges": 384,
        "polygons": 192,
        "loops": 744,
        "geometryTopologySha256": "6b18be7ea2cf865108bd1ae2b739d3d4cb2703c2e1e881877e6070378a46ea40",
        "orientedGeometryTopologySha256": "b44f7b8ded69053ce0fe303c233bb3058d16a6d3e06230dbf7e8f53f8ced3ca6",
    },
    "Arm_R": {
        "vertices": 193,
        "edges": 384,
        "polygons": 192,
        "loops": 744,
        "geometryTopologySha256": "d43933f28acc8702ad929dce07d4c118e15f670c0af103757f1752765348342b",
        "orientedGeometryTopologySha256": "e39c263ef2556222ff9323ad7c85b3b4cf565416acf2419bffca9b60fb3ad2bb",
    },
    "Leg_L": {
        "vertices": 193,
        "edges": 384,
        "polygons": 192,
        "loops": 744,
        "geometryTopologySha256": "e1f498e0286840234c68b8c4052691287cd59a60638d00a3075d08e3efdd184e",
        "orientedGeometryTopologySha256": "ac6339ff6b37b63763bd434e8bd725f936b5c579c68ad7e3ed9668cb74067a76",
    },
    "Leg_R": {
        "vertices": 193,
        "edges": 384,
        "polygons": 192,
        "loops": 744,
        "geometryTopologySha256": "540cd703b4ceef7ddfc672db158ef37babc457deceb5d5f8b371a5d1da7bfe8d",
        "orientedGeometryTopologySha256": "6f1ef054fa7259c0a302c73bd9cc1bbd00c42ba95d42750db539b5d08fc7a23e",
    },
}


def round_value(value):
    return round(float(value), 9)


def vector_values(vector):
    return [round_value(component) for component in vector]


def maximum_deviation(actual, expected):
    return max(abs(float(a) - float(e)) for a, e in zip(actual, expected))


def normalize_fbx_name(name):
    normalized = str(name)
    while True:
        candidate = re.sub(r"(?:\.\d{3,}|_\d{3,})$", "", normalized)
        if candidate == normalized:
            return normalized
        normalized = candidate


def quantized_coordinate(coordinate):
    return tuple(int(round(float(component) * QUANTIZATION)) for component in coordinate)


def minimum_rotation(values):
    values = tuple(values)
    rotations = (values[index:] + values[:index] for index in range(len(values)))
    return min(rotations)


def mesh_signatures(mesh):
    coordinates = [quantized_coordinate(vertex.co) for vertex in mesh.vertices]
    vertices = sorted(coordinates)
    unoriented_polygons = []
    oriented_polygons = []
    for polygon in mesh.polygons:
        cycle = tuple(coordinates[index] for index in polygon.vertices)
        unoriented_polygons.append(tuple(sorted(cycle)))
        oriented_polygons.append(minimum_rotation(cycle))
    base = {"vertices": vertices, "polygons": sorted(unoriented_polygons)}
    oriented = {"vertices": vertices, "polygons": sorted(oriented_polygons)}
    return {
        "geometryTopologySha256": hashlib.sha256(
            json.dumps(base, sort_keys=True, separators=(",", ":")).encode("ascii")
        ).hexdigest(),
        "orientedGeometryTopologySha256": hashlib.sha256(
            json.dumps(oriented, sort_keys=True, separators=(",", ":")).encode("ascii")
        ).hexdigest(),
    }


def sha256_file(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def clear_to_fresh_scene():
    bpy.ops.wm.read_factory_settings(use_empty=True)
    if not bpy.data.scenes:
        scene = bpy.data.scenes.new("C1B005_FBX_Inspection")
        bpy.context.window.scene = scene


def parse_args():
    if "--" not in sys.argv:
        raise ValueError("expected -- <canonical-fbx>")
    custom = sys.argv[sys.argv.index("--") + 1 :]
    if len(custom) != 1:
        raise ValueError("expected exactly one canonical FBX path")
    path = os.path.abspath(custom[0])
    if not os.path.isfile(path):
        raise ValueError(f"FBX does not exist: {path}")
    if os.path.splitext(path)[1].lower() != ".fbx":
        raise ValueError(f"input must have .fbx extension: {path}")
    return path


def add_error(errors, code, detail):
    errors.append({"code": code, "detail": str(detail)})


def imported_object_map(objects, errors):
    by_name = {}
    for obj in objects:
        normalized = normalize_fbx_name(obj.name)
        if normalized in by_name:
            add_error(errors, "DUPLICATE_NORMALIZED_OBJECT_NAME", normalized)
        else:
            by_name[normalized] = obj
    return by_name


def inspect_fbx(path):
    errors = []
    with open(path, "rb") as stream:
        binary_header = stream.read(18).startswith(b"Kaydara FBX Binary")
    if not binary_header:
        add_error(errors, "FBX_BINARY_FORMAT", "expected Kaydara FBX Binary header")
    clear_to_fresh_scene()
    import_result = bpy.ops.import_scene.fbx(
        filepath=path,
        use_custom_normals=True,
        use_image_search=False,
        use_anim=True,
        ignore_leaf_bones=False,
        automatic_bone_orientation=False,
    )
    if "FINISHED" not in import_result:
        add_error(errors, "FBX_IMPORT", sorted(import_result))

    bpy.context.view_layer.update()
    objects = sorted(bpy.data.objects, key=lambda obj: obj.name)
    object_map = imported_object_map(objects, errors)
    expected_names = {ROOT_NAME, *MESH_NAMES.values(), *(f"LM_{name}" for name in LANDMARK_POSITIONS)}
    actual_names = set(object_map)
    for name in sorted(expected_names - actual_names):
        add_error(errors, "MISSING_OBJECT", name)
    for name in sorted(actual_names - expected_names):
        add_error(errors, "UNEXPECTED_OBJECT", name)
    if len(objects) != 24:
        add_error(errors, "OBJECT_COUNT", f"expected=24 actual={len(objects)}")

    root = object_map.get(ROOT_NAME)
    if root is not None:
        if root.type != "EMPTY":
            add_error(errors, "ROOT_TYPE", f"expected=EMPTY actual={root.type}")
        if root.parent is not None:
            add_error(errors, "ROOT_PARENT", root.parent.name)
        if maximum_deviation(root.matrix_world.translation, (0.0, 0.0, 0.0)) > TRANSFORM_TOLERANCE:
            add_error(errors, "ROOT_LOCATION", vector_values(root.matrix_world.translation))
        if maximum_deviation(root.scale, (1.0, 1.0, 1.0)) > TRANSFORM_TOLERANCE:
            add_error(errors, "ROOT_SCALE", vector_values(root.scale))

    expected_children = expected_names - {ROOT_NAME}
    hierarchy_mismatches = []
    for name in sorted(expected_children):
        obj = object_map.get(name)
        if obj is not None and (root is None or obj.parent != root):
            hierarchy_mismatches.append(obj.name)
            add_error(
                errors,
                "DIRECT_ROOT_CHILD",
                f"object={obj.name} parent={obj.parent.name if obj.parent else None}",
            )

    mesh_records = {}
    mesh_objects = [obj for obj in objects if obj.type == "MESH"]
    if len(mesh_objects) != 6:
        add_error(errors, "MESH_OBJECT_COUNT", f"expected=6 actual={len(mesh_objects)}")
    for part, expected_name in MESH_NAMES.items():
        obj = object_map.get(expected_name)
        if obj is None:
            continue
        if obj.type != "MESH":
            add_error(errors, "MESH_OBJECT_TYPE", f"object={obj.name} actual={obj.type}")
            continue
        mesh = obj.data
        signatures = mesh_signatures(mesh)
        record = {
            "object": obj.name,
            "meshDatablock": mesh.name,
            "vertices": len(mesh.vertices),
            "edges": len(mesh.edges),
            "polygons": len(mesh.polygons),
            "loops": len(mesh.loops),
            "materialSlots": len(obj.material_slots),
            "uvLayers": len(mesh.uv_layers),
            "location": vector_values(obj.location),
            "rotationEuler": vector_values(obj.rotation_euler),
            "scale": vector_values(obj.scale),
            **signatures,
        }
        mesh_records[part] = record
        expected = EXPECTED_MESH_CONTRACT[part]
        for field in ("vertices", "edges", "polygons", "loops"):
            if record[field] != expected[field]:
                add_error(
                    errors,
                    "MESH_TOPOLOGY_COUNT",
                    f"part={part} field={field} expected={expected[field]} actual={record[field]}",
                )
        for field in ("geometryTopologySha256", "orientedGeometryTopologySha256"):
            if record[field] != expected[field]:
                add_error(
                    errors,
                    "MESH_LINEAGE_SIGNATURE",
                    f"part={part} field={field} expected={expected[field]} actual={record[field]}",
                )
        if len(obj.material_slots) != 0:
            add_error(errors, "MESH_MATERIAL_SLOT", f"part={part} actual={len(obj.material_slots)}")
        if maximum_deviation(obj.scale, (1.0, 1.0, 1.0)) > TRANSFORM_TOLERANCE:
            add_error(errors, "MESH_SCALE", f"part={part} actual={vector_values(obj.scale)}")
        if maximum_deviation(obj.location, (0.0, 0.0, 0.0)) > TRANSFORM_TOLERANCE:
            add_error(errors, "MESH_LOCATION", f"part={part} actual={vector_values(obj.location)}")
        if maximum_deviation(obj.rotation_euler, (0.0, 0.0, 0.0)) > TRANSFORM_TOLERANCE:
            add_error(
                errors,
                "MESH_ROTATION",
                f"part={part} actual={vector_values(obj.rotation_euler)}",
            )

    landmark_records = {}
    maximum_landmark_deviation = 0.0
    for identifier, expected_position in LANDMARK_POSITIONS.items():
        expected_name = f"LM_{identifier}"
        obj = object_map.get(expected_name)
        if obj is None:
            continue
        if obj.type != "EMPTY":
            add_error(errors, "LANDMARK_TYPE", f"object={obj.name} actual={obj.type}")
            continue
        position = obj.matrix_world.translation
        deviation = maximum_deviation(position, expected_position)
        maximum_landmark_deviation = max(maximum_landmark_deviation, deviation)
        landmark_records[identifier] = {
            "object": obj.name,
            "positionTransportH": vector_values(position),
            "expectedPositionTransportH": list(expected_position),
            "maximumAbsoluteDeviationH": round_value(deviation),
        }
        if deviation > START_TOLERANCE_H:
            add_error(
                errors,
                "LANDMARK_POSITION",
                f"landmark={identifier} deviationH={round_value(deviation)}",
            )

    world_points = [obj.matrix_world @ vertex.co for obj in mesh_objects for vertex in obj.data.vertices]
    bounds = None
    if world_points:
        minimum = Vector(tuple(min(point[index] for point in world_points) for index in range(3)))
        maximum = Vector(tuple(max(point[index] for point in world_points) for index in range(3)))
        size = maximum - minimum
        bounds = {
            "minimumBlenderH": vector_values(minimum),
            "maximumBlenderH": vector_values(maximum),
            "centerBlenderH": vector_values((minimum + maximum) * 0.5),
            "heightH": round_value(size.z),
            "frontViewFullWidthH": round_value(size.x),
            "sideViewTotalDepthH": round_value(size.y),
            "groundMinimumH": round_value(minimum.z),
            "crownMaximumH": round_value(maximum.z),
        }
        for field, expected in EXPECTED_BOUNDS.items():
            deviation = abs(float(bounds[field]) - expected)
            if deviation > START_TOLERANCE_H:
                add_error(
                    errors,
                    "BOUNDS",
                    f"field={field} expected={expected} actual={bounds[field]} deviationH={round_value(deviation)}",
                )

    root_axes = None
    axis_reversal_count = 0
    if root is not None:
        axis_matrix = root.matrix_world.to_3x3().normalized()
        right = axis_matrix @ Vector((1.0, 0.0, 0.0))
        forward = axis_matrix @ Vector((0.0, -1.0, 0.0))
        up = axis_matrix @ Vector((0.0, 0.0, 1.0))
        axis_contract = (
            ("right", right, Vector((1.0, 0.0, 0.0))),
            ("forward", forward, Vector((0.0, 0.0, -1.0))),
            ("up", up, Vector((0.0, -1.0, 0.0))),
        )
        root_axes = {name: vector_values(actual.normalized()) for name, actual, _expected in axis_contract}
        for name, actual, expected in axis_contract:
            dot = actual.normalized().dot(expected)
            deviation = maximum_deviation(actual.normalized(), expected)
            if deviation > TRANSFORM_TOLERANCE:
                axis_reversal_count += 1
                add_error(
                    errors,
                    "AXIS",
                    f"axis={name} maximumDeviation={round_value(deviation)} dot={round_value(dot)}",
                )

    negative_scale_objects = []
    non_unit_scale_objects = []
    non_finite_vertex_count = 0
    non_finite_normal_count = 0
    degenerate_polygon_count = 0
    for obj in objects:
        if any(abs(float(component) - 1.0) > TRANSFORM_TOLERANCE for component in obj.scale):
            non_unit_scale_objects.append(obj.name)
        if any(float(component) < 0.0 for component in obj.scale) or obj.matrix_world.to_3x3().determinant() < 0.0:
            negative_scale_objects.append(obj.name)
        if obj.type != "MESH":
            continue
        non_finite_vertex_count += sum(
            1 for vertex in obj.data.vertices if not all(math.isfinite(float(value)) for value in vertex.co)
        )
        for polygon in obj.data.polygons:
            if not all(math.isfinite(float(value)) for value in polygon.normal):
                non_finite_normal_count += 1
            if len(polygon.vertices) < 3 or float(polygon.area) <= 0.000000000001:
                degenerate_polygon_count += 1
    if negative_scale_objects:
        add_error(errors, "NEGATIVE_SCALE", negative_scale_objects)
    if non_unit_scale_objects:
        add_error(errors, "NON_UNIT_SCALE", non_unit_scale_objects)
    if non_finite_vertex_count:
        add_error(errors, "NON_FINITE_VERTEX", non_finite_vertex_count)
    if non_finite_normal_count:
        add_error(errors, "NON_FINITE_NORMAL", non_finite_normal_count)
    if degenerate_polygon_count:
        add_error(errors, "DEGENERATE_POLYGON", degenerate_polygon_count)

    collider_objects = [
        obj.name
        for obj in objects
        if any(token in obj.name.lower() for token in ("collider", "collision", "hitbox", "physics"))
    ]
    external_images = sorted(
        image.filepath for image in bpy.data.images if image.source == "FILE" and image.filepath
    )
    packed_images = sorted(
        image.name
        for image in bpy.data.images
        if image.packed_file is not None or len(image.packed_files) > 0
    )
    external_libraries = sorted(library.filepath for library in bpy.data.libraries if library.filepath)
    external_fonts = sorted(
        font.filepath for font in bpy.data.fonts if font.filepath and font.filepath != "<builtin>"
    )
    external_sounds = sorted(sound.filepath for sound in bpy.data.sounds if sound.filepath)
    external_movie_clips = sorted(clip.filepath for clip in bpy.data.movieclips if clip.filepath)
    forbidden_counts = {
        "armatures": len(bpy.data.armatures),
        "actions": len(bpy.data.actions),
        "colliderObjects": len(collider_objects),
        "materials": len(bpy.data.materials),
        "cameras": len(bpy.data.cameras),
        "lights": len(bpy.data.lights),
        "curves": len(bpy.data.curves),
        "images": len(bpy.data.images),
        "externalImages": len(external_images),
        "packedImages": len(packed_images),
        "externalLibraries": len(external_libraries),
        "externalFonts": len(external_fonts),
        "externalSounds": len(external_sounds),
        "externalMovieClips": len(external_movie_clips),
    }
    for field, count in forbidden_counts.items():
        if count != 0:
            add_error(errors, "FORBIDDEN_DATA", f"field={field} actual={count}")
    if len(bpy.data.meshes) != 6:
        add_error(errors, "MESH_DATABLOCK_COUNT", f"expected=6 actual={len(bpy.data.meshes)}")

    root_to_ground = None
    if root is not None and bounds is not None:
        root_to_ground = abs(float(root.matrix_world.translation.z) - float(bounds["groundMinimumH"]))
        if root_to_ground > START_TOLERANCE_H:
            add_error(errors, "GROUND_PIVOT", f"deviationH={round_value(root_to_ground)}")

    payload = {
        "schemaVersion": 1,
        "inspector": "C1B005-FBX-Inspector-r02",
        "modelInteropProfileId": "ModelInteropProfile-ART-001-r02",
        "blenderExportOverrideId": "PHX-FBX-C1B-BLOCKOUT-r02",
        "blenderExportOverrideSettingsSha256": "21b50c577b30f79d5717806f0687550c267181ddb3e8d0b9b9213e6133a02f29",
        "file": {"path": path, "bytes": os.path.getsize(path), "sha256": sha256_file(path)},
        "toolchain": {
            "blenderVersion": bpy.app.version_string,
            "expectedBlenderVersion": EXPECTED_BLENDER_VERSION,
        },
        "import": {
            "binaryFbx": binary_header,
            "freshScene": True,
            "operator": "bpy.ops.import_scene.fbx",
            "useCustomNormals": True,
            "useImageSearch": False,
            "useAnimation": True,
        },
        "hierarchy": {
            "root": root.name if root else None,
            "expectedDirectChildren": 23,
            "actualDirectChildren": len(root.children) if root else 0,
            "mismatchObjects": hierarchy_mismatches,
        },
        "counts": {
            "objects": len(objects),
            "meshObjects": len(mesh_objects),
            "meshDatablocks": len(bpy.data.meshes),
            "landmarks": len(landmark_records),
            "scenes": len(bpy.data.scenes),
            **forbidden_counts,
        },
        "objects": [
            {
                "name": obj.name,
                "normalizedName": normalize_fbx_name(obj.name),
                "type": obj.type,
                "parent": obj.parent.name if obj.parent else None,
                "location": vector_values(obj.location),
                "rotationEuler": vector_values(obj.rotation_euler),
                "scale": vector_values(obj.scale),
            }
            for obj in objects
        ],
        "meshes": mesh_records,
        "landmarks": landmark_records,
        "maximumLandmarkPositionDeviationH": round_value(maximum_landmark_deviation),
        "bounds": bounds,
        "axes": {
            "fbxTransportFrameInBlenderInspector": {"right": "+X", "forward": "-Z", "up": "-Y"},
            "expectedUnityTargetFrame": {"right": "+X", "forward": "+Z", "up": "+Y"},
            "rootWorldDirections": root_axes,
            "axisReversalCount": axis_reversal_count,
        },
        "groundPivot": {
            "rootLocationBlenderH": vector_values(root.matrix_world.translation) if root else None,
            "rootToGroundPlaneDeviationH": round_value(root_to_ground) if root_to_ground is not None else None,
        },
        "negativeScaleObjects": negative_scale_objects,
        "nonUnitScaleObjects": non_unit_scale_objects,
        "invalidGeometry": {
            "nonFiniteVertexCount": non_finite_vertex_count,
            "nonFiniteNormalCount": non_finite_normal_count,
            "degeneratePolygonCount": degenerate_polygon_count,
        },
        "external": {
            "images": external_images,
            "packedImages": packed_images,
            "libraries": external_libraries,
            "fonts": external_fonts,
            "sounds": external_sounds,
            "movieClips": external_movie_clips,
        },
        "colliderObjects": collider_objects,
        "errors": errors,
        "result": "PASS" if not errors else "FAIL",
    }
    return payload


def main():
    try:
        path = parse_args()
        payload = inspect_fbx(path)
        if bpy.app.version_string != EXPECTED_BLENDER_VERSION:
            payload["errors"].append(
                {
                    "code": "BLENDER_VERSION",
                    "detail": f"expected={EXPECTED_BLENDER_VERSION} actual={bpy.app.version_string}",
                }
            )
            payload["result"] = "FAIL"
    except Exception as error:
        payload = {
            "schemaVersion": 1,
            "inspector": "C1B005-FBX-Inspector-r02",
            "errors": [{"code": "INSPECTOR_EXCEPTION", "detail": f"{type(error).__name__}: {error}"}],
            "result": "FAIL",
        }
    print(REPORT_PREFIX + json.dumps(payload, sort_keys=True, separators=(",", ":")))
    if payload["result"] != "PASS":
        raise SystemExit(1)


if __name__ == "__main__":
    main()
