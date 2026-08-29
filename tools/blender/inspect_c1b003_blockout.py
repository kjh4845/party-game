#!/usr/bin/env python3

import json
import math

import bpy
from mathutils import Vector


def round_value(value):
    return round(float(value), 9)


def blender_to_unity(coordinate):
    return [round_value(coordinate.x), round_value(coordinate.z), round_value(-coordinate.y)]


def world_vertices(obj):
    return [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]


def section_measurement(obj, section):
    target_height = float(section["heightH"])
    coordinates = [
        vertex
        for vertex in world_vertices(obj)
        if abs(float(vertex.z) - target_height) <= 0.0000005
    ]
    if not coordinates:
        return {"id": section["id"], "object": obj.name, "vertexCount": 0}
    xs = [float(vertex.x) for vertex in coordinates]
    ys = [float(vertex.y) for vertex in coordinates]
    return {
        "id": section["id"],
        "object": obj.name,
        "heightH": round_value(target_height),
        "frontViewFullWidthH": round_value(max(xs) - min(xs)),
        "sideViewTotalDepthH": round_value(max(ys) - min(ys)),
        "vertexCount": len(coordinates),
    }


def material_contract(material):
    use_nodes = material.node_tree is not None
    principled = material.node_tree.nodes.get("Principled BSDF") if use_nodes else None
    return {
        "useNodes": use_nodes,
        "useFakeUser": bool(material.use_fake_user),
        "baseColor": [round_value(value) for value in principled.inputs["Base Color"].default_value],
        "metallic": round_value(principled.inputs["Metallic"].default_value),
        "roughness": round_value(principled.inputs["Roughness"].default_value),
        "specularIorLevel": round_value(principled.inputs["Specular IOR Level"].default_value),
    }


def main():
    scene = bpy.context.scene
    model_objects = sorted(
        [obj for obj in bpy.data.objects if obj.type == "MESH" and "c1b_mesh_role" in obj],
        key=lambda obj: obj.name,
    )
    all_vertices = [vertex for obj in model_objects for vertex in world_vertices(obj)]
    xs = [float(vertex.x) for vertex in all_vertices]
    ys = [float(vertex.y) for vertex in all_vertices]
    zs = [float(vertex.z) for vertex in all_vertices]
    bounds_center_blender = Vector(
        ((min(xs) + max(xs)) * 0.5, (min(ys) + max(ys)) * 0.5, (min(zs) + max(zs)) * 0.5)
    )
    bounds = {
        "minimumUnity": blender_to_unity(Vector((min(xs), max(ys), min(zs)))),
        "maximumUnity": blender_to_unity(Vector((max(xs), min(ys), max(zs)))),
        "heightH": round_value(max(zs) - min(zs)),
        "frontViewFullWidthH": round_value(max(xs) - min(xs)),
        "sideViewTotalDepthH": round_value(max(ys) - min(ys)),
        "centerBlender": [round_value(value) for value in bounds_center_blender],
        "centerUnity": blender_to_unity(bounds_center_blender),
    }

    landmarks = {}
    for obj in bpy.data.objects:
        identifier = obj.get("c1b_landmark_id")
        if not identifier:
            continue
        landmarks[identifier] = {
            "positionH": blender_to_unity(obj.matrix_world.translation),
            "semantic": obj.get("semantic"),
            "crossSectionScope": obj.get("cross_section_scope"),
            "frontViewFullWidthH": round_value(obj.get("front_view_full_width_h", 0.0)),
            "sideViewTotalDepthH": round_value(obj.get("side_view_total_depth_h", 0.0)),
        }

    sections = []
    for obj in model_objects:
        for section in json.loads(obj.get("c1b_sections_json", "[]")):
            sections.append(section_measurement(obj, section))
    sections.sort(key=lambda entry: (entry["id"], entry["object"]))

    cameras = {}
    for obj in bpy.data.objects:
        view_id = obj.get("c1b_view_id")
        if not view_id:
            continue
        actual_look_direction = obj.matrix_world.to_quaternion() @ Vector((0.0, 0.0, -1.0))
        actual_look_direction.normalize()
        camera_location = obj.matrix_world.translation
        distance_along_axis = (bounds_center_blender - camera_location).dot(actual_look_direction)
        closest_axis_point = camera_location + actual_look_direction * distance_along_axis
        bounds_center_axis_deviation = (closest_axis_point - bounds_center_blender).length
        cameras[view_id] = {
            "type": obj.data.type,
            "orthoScale": round_value(obj.data.ortho_scale),
            "declaredLookDirectionBlender": [round_value(value) for value in obj.get("look_direction_blender")],
            "actualLookDirectionBlender": [round_value(value) for value in actual_look_direction],
            "declaredTargetBlender": [round_value(value) for value in obj.get("target_blender")],
            "opticalAxisBoundsCenterDeviationH": round_value(bounds_center_axis_deviation),
            "boundsPaddingRatio": round_value(obj.get("bounds_padding_ratio")),
        }

    payload = {
        "scene": {
            "assetId": scene.get("asset_id"),
            "assetVersion": scene.get("asset_version"),
            "ownerTask": scene.get("owner_task"),
            "sourceOwner": scene.get("source_owner"),
            "profileId": scene.get("profile_id"),
            "profileRevision": scene.get("profile_revision"),
            "measurementSetSha256": scene.get("measurement_set_sha256"),
            "state": scene.get("state"),
            "candidateStatus": scene.get("candidate_status"),
            "userVisualApprovalRecorded": bool(scene.get("user_visual_approval_recorded")),
            "lockedValueCount": int(scene.get("locked_value_count", 0)),
            "pixelMeasurementUsed": bool(scene.get("pixel_measurement_used")),
            "referenceReplicaAllowed": bool(scene.get("reference_replica_allowed")),
            "gameplayHeightMeters": scene.get("gameplay_height_meters"),
            "colliderProfile": scene.get("collider_profile"),
            "rigProfile": scene.get("rig_profile"),
            "renderResolution": int(scene.get("render_resolution", 0)),
            "orthographicScale": round_value(scene.get("orthographic_scale", 0.0)),
            "boundsPaddingRatio": round_value(scene.get("bounds_padding_ratio", 0.0)),
            "authoredBoundsCenterBlender": [
                round_value(value) for value in scene.get("authored_bounds_center_blender")
            ],
            "referenceRenderFiles": json.loads(scene.get("reference_render_files_json", "[]")),
        },
        "unitSettings": {
            "system": scene.unit_settings.system,
            "scaleLength": round_value(scene.unit_settings.scale_length),
        },
        "renderSettings": {
            "engine": scene.render.engine,
            "resolutionX": scene.render.resolution_x,
            "resolutionY": scene.render.resolution_y,
            "resolutionPercentage": scene.render.resolution_percentage,
            "filmTransparent": bool(scene.render.film_transparent),
            "viewTransform": scene.view_settings.view_transform,
            "look": scene.view_settings.look,
            "exposure": round_value(scene.view_settings.exposure),
            "gamma": round_value(scene.view_settings.gamma),
        },
        "root": {
            "location": blender_to_unity(bpy.data.objects["CHR_C1B003_Root"].location),
            "scale": [round_value(value) for value in bpy.data.objects["CHR_C1B003_Root"].scale],
        },
        "modelObjects": [obj.name for obj in model_objects],
        "objects": sorted(
            [
                {
                    "name": obj.name,
                    "type": obj.type,
                    "collections": sorted(collection.name for collection in obj.users_collection),
                    "hideRender": bool(obj.hide_render),
                    "parent": obj.parent.name if obj.parent else None,
                }
                for obj in bpy.data.objects
            ],
            key=lambda entry: entry["name"],
        ),
        "datablocks": {
            "meshes": sorted(item.name for item in bpy.data.meshes),
            "materials": sorted(item.name for item in bpy.data.materials),
            "cameras": sorted(item.name for item in bpy.data.cameras),
            "lights": sorted(item.name for item in bpy.data.lights),
            "worlds": sorted(item.name for item in bpy.data.worlds),
            "images": sorted(item.name for item in bpy.data.images),
            "actions": sorted(item.name for item in bpy.data.actions),
            "texts": sorted(item.name for item in bpy.data.texts),
            "sounds": sorted(item.name for item in bpy.data.sounds),
            "movieClips": sorted(item.name for item in bpy.data.movieclips),
            "fonts": sorted(item.name for item in bpy.data.fonts),
            "libraries": sorted(item.name for item in bpy.data.libraries),
        },
        "materials": {
            material.name: material_contract(material)
            for material in sorted(bpy.data.materials, key=lambda item: item.name)
        },
        "lights": {
            obj.name: {
                "type": obj.data.type,
                "energy": round_value(obj.data.energy),
                "color": [round_value(value) for value in obj.data.color],
                "rotationEulerDegrees": [round_value(math.degrees(value)) for value in obj.rotation_euler],
            }
            for obj in sorted(
                (candidate for candidate in bpy.data.objects if candidate.type == "LIGHT"),
                key=lambda item: item.name,
            )
        },
        "world": {
            "name": scene.world.name,
            "backgroundColor": [
                round_value(value) for value in scene.world.node_tree.nodes["Background"].inputs["Color"].default_value
            ],
            "backgroundStrength": round_value(
                scene.world.node_tree.nodes["Background"].inputs["Strength"].default_value
            ),
        },
        "meshObjectCount": len(model_objects),
        "meshVertexCount": sum(len(obj.data.vertices) for obj in model_objects),
        "meshPolygonCount": sum(len(obj.data.polygons) for obj in model_objects),
        "bounds": bounds,
        "landmarks": dict(sorted(landmarks.items())),
        "sections": sections,
        "cameras": dict(sorted(cameras.items())),
        "externalImages": sorted(
            image.filepath for image in bpy.data.images if image.source == "FILE" and image.filepath
        ),
        "packedImageCount": sum(
            1 for image in bpy.data.images
            if image.packed_file is not None or len(image.packed_files) > 0
        ),
        "externalLibraries": sorted(library.filepath for library in bpy.data.libraries if library.filepath),
        "externalFonts": sorted(
            font.filepath for font in bpy.data.fonts
            if font.filepath and font.filepath != "<builtin>"
        ),
        "externalSounds": sorted(sound.filepath for sound in bpy.data.sounds if sound.filepath),
        "externalMovieClips": sorted(clip.filepath for clip in bpy.data.movieclips if clip.filepath),
        "actionCount": len(bpy.data.actions),
        "sceneCount": len(bpy.data.scenes),
        "textBlockCount": len(bpy.data.texts),
        "collectionNames": sorted(collection.name for collection in bpy.data.collections),
        "armatureCount": sum(1 for obj in bpy.data.objects if obj.type == "ARMATURE"),
        "colliderObjectCount": sum(1 for obj in bpy.data.objects if obj.get("collider_profile")),
    }
    print("C1B003_INSPECTION_JSON=" + json.dumps(payload, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
