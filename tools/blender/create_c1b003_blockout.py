#!/usr/bin/env python3

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Vector


ASSET_ID = "CHR_MasterCharacter_C1B_Blockout"
ASSET_VERSION = "0.1.0-start"
PROFILE_ID = "CharacterProportionProfile-C1B-002-r01"
PROFILE_REVISION = "r01"
MEASUREMENT_SET_SHA256 = "76c98acfe8cfbf01b51936b29c2f6ba2e78c26222dfd53c033fe84233e562722"
SOURCE_OWNER = "kjh4845"
ORTHO_SCALE = 1.2
RENDER_RESOLUTION = 2048
RING_SEGMENTS = 24
AUTHORED_BOUNDS_CENTER_BLENDER = Vector((0.0, -0.0075, 0.5))


def parse_args():
    arguments = sys.argv
    if "--" not in arguments:
        raise RuntimeError("expected -- <blend-output> <render-directory>")
    custom = arguments[arguments.index("--") + 1 :]
    if len(custom) != 2:
        raise RuntimeError("expected blend output and render directory")
    return os.path.abspath(custom[0]), os.path.abspath(custom[1])


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for datablocks in (bpy.data.meshes, bpy.data.curves, bpy.data.materials, bpy.data.cameras, bpy.data.lights):
        for datablock in list(datablocks):
            datablocks.remove(datablock)


def create_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def link_object_to_collection(obj, collection):
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)


def make_material(name, base_color, roughness=0.75, metallic=0.0):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    nodes = material.node_tree.nodes
    principled = nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*base_color, 1.0)
    principled.inputs["Metallic"].default_value = metallic
    principled.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in principled.inputs:
        principled.inputs["Specular IOR Level"].default_value = 0.25
    return material


def ring_vertices(ring, segments):
    width = float(ring["width"])
    depth = float(ring["depth"])
    if width == 0.0 or depth == 0.0:
        return [(float(ring["x"]), float(ring["y"]), float(ring["z"]))]
    result = []
    for index in range(segments):
        angle = 2.0 * math.pi * index / segments
        result.append(
            (
                float(ring["x"]) + width * 0.5 * math.cos(angle),
                float(ring["y"]) + depth * 0.5 * math.sin(angle),
                float(ring["z"]),
            )
        )
    return result


def connect_rings(faces, lower, upper):
    if len(lower) == 1 and len(upper) == 1:
        return
    if len(lower) == 1:
        point = lower[0]
        for index in range(len(upper)):
            faces.append((point, upper[index], upper[(index + 1) % len(upper)]))
        return
    if len(upper) == 1:
        point = upper[0]
        for index in range(len(lower)):
            faces.append((lower[index], point, lower[(index + 1) % len(lower)]))
        return
    for index in range(len(lower)):
        next_index = (index + 1) % len(lower)
        faces.append((lower[index], upper[index], upper[next_index], lower[next_index]))


def cap_ring(vertices, faces, ring_indices, ring, top):
    if len(ring_indices) == 1:
        return
    center_index = len(vertices)
    vertices.append((float(ring["x"]), float(ring["y"]), float(ring["z"])))
    for index in range(len(ring_indices)):
        next_index = (index + 1) % len(ring_indices)
        if top:
            faces.append((ring_indices[index], center_index, ring_indices[next_index]))
        else:
            faces.append((ring_indices[index], ring_indices[next_index], center_index))


def create_section_mesh(name, rings, sections, collection, material, root, cap_bottom=True, cap_top=True):
    ordered = sorted(rings, key=lambda entry: float(entry["z"]))
    vertices = []
    ring_indices = []
    for ring in ordered:
        indices = []
        for coordinate in ring_vertices(ring, RING_SEGMENTS):
            indices.append(len(vertices))
            vertices.append(coordinate)
        ring_indices.append(indices)

    faces = []
    if cap_bottom:
        cap_ring(vertices, faces, ring_indices[0], ordered[0], top=False)
    for lower, upper in zip(ring_indices, ring_indices[1:]):
        connect_rings(faces, lower, upper)
    if cap_top:
        cap_ring(vertices, faces, ring_indices[-1], ordered[-1], top=True)

    mesh = bpy.data.meshes.new(f"{name}_Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.validate(verbose=False)
    mesh.update()

    bm = bmesh.new()
    bm.from_mesh(mesh)
    if bm.faces:
        bmesh.ops.recalc_face_normals(bm, faces=list(bm.faces))
    bm.to_mesh(mesh)
    bm.free()
    mesh.update()

    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    obj.parent = root
    obj.data.materials.append(material)
    obj["c1b_mesh_role"] = name.removeprefix("CHR_C1B003_")
    obj["c1b_sections_json"] = json.dumps(sections, sort_keys=True, separators=(",", ":"))
    obj["c1b_profile_id"] = PROFILE_ID
    obj["c1b_measurement_set_sha256"] = MEASUREMENT_SET_SHA256
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    mesh.set_sharp_from_angle(angle=math.radians(35.0))
    return obj


def asymmetric_center_y(front_extent, rear_extent):
    return (float(rear_extent) - float(front_extent)) * 0.5


def section(identifier, height, width, depth):
    return {"id": identifier, "heightH": height, "frontViewFullWidthH": width, "sideViewTotalDepthH": depth}


def create_geometry(model_collection, material, root):
    head_rings = [
        {"z": 0.800, "x": 0.0, "y": 0.0, "width": 0.190, "depth": 0.170},
        {"z": 0.840, "x": 0.0, "y": -0.0025, "width": 0.235, "depth": 0.205},
        {"z": 0.900, "x": 0.0, "y": -0.0025, "width": 0.255, "depth": 0.225},
        {"z": 0.960, "x": 0.0, "y": -0.0025, "width": 0.210, "depth": 0.190},
        {"z": 0.975, "x": 0.0, "y": -0.0015, "width": 0.175, "depth": 0.155},
        {"z": 0.985, "x": 0.0, "y": -0.0010, "width": 0.135, "depth": 0.120},
        {"z": 0.992, "x": 0.0, "y": -0.0005, "width": 0.075, "depth": 0.067},
        {"z": 0.997, "x": 0.0, "y": -0.0002, "width": 0.030, "depth": 0.027},
        {"z": 1.000, "x": 0.0, "y": 0.0, "width": 0.0, "depth": 0.0},
    ]
    head_sections = [
        section("Chin", 0.800, 0.190, 0.170),
        section("HeadMax", 0.900, 0.255, 0.225),
        section("Crown", 1.000, 0.0, 0.0),
    ]
    create_section_mesh(
        "CHR_C1B003_Head", head_rings, head_sections, model_collection, material, root,
        cap_bottom=False,
    )

    torso_specs = [
        (0.310, 0.250, 0.120, 0.115, "CrotchBridge"),
        (0.395, 0.350, 0.140, 0.125, "PelvisBody"),
        (0.585, 0.385, 0.140, 0.120, "ChestBody"),
        (0.690, 0.400, 0.130, 0.120, "ShoulderBody"),
        (0.720, 0.360, 0.125, 0.115, None),
        (0.750, 0.305, 0.115, 0.105, None),
        (0.780, 0.235, 0.100, 0.090, None),
        (0.800, 0.190, 0.090, 0.080, None),
    ]
    torso_rings = [
        {
            "z": height,
            "x": 0.0,
            "y": asymmetric_center_y(front, rear),
            "width": width,
            "depth": front + rear,
        }
        for height, width, front, rear, _identifier in torso_specs
    ]
    torso_sections = [
        section(identifier, height, width, front + rear)
        for height, width, front, rear, identifier in torso_specs
        if identifier
    ]
    create_section_mesh(
        "CHR_C1B003_Torso", torso_rings, torso_sections, model_collection, material, root,
        cap_top=False,
    )

    for side, sign in (("L", -1.0), ("R", 1.0)):
        arm_specs = [
            (0.355, 0.235, -0.005, 0.060, 0.065, f"ForearmTerminalBottom_{side}"),
            (0.372, 0.235, -0.005, 0.092, 0.098, None),
            (0.405, 0.235, -0.005, 0.110, 0.115, f"ForearmTerminal_{side}"),
            (0.460, 0.235, -0.002, 0.105, 0.110, None),
            (0.520, 0.235, 0.0, 0.100, 0.105, f"Elbow_{side}"),
            (0.605, 0.220, 0.0, 0.105, 0.110, f"UpperArm_{side}"),
            (0.690, 0.205, 0.0, 0.105, 0.110, f"Shoulder_{side}"),
            (0.715, 0.160, 0.0, 0.020, 0.040, None),
        ]
        rings = [
            {"z": height, "x": sign * abs(x), "y": y, "width": width, "depth": depth}
            for height, x, y, width, depth, _identifier in arm_specs
        ]
        sections = [
            section(identifier, height, width, depth)
            for height, _x, _y, width, depth, identifier in arm_specs
            if identifier
        ]
        create_section_mesh(
            f"CHR_C1B003_Arm_{side}", rings, sections, model_collection, material, root,
            cap_top=False,
        )

    for side, sign in (("L", -1.0), ("R", 1.0)):
        leg_specs = [
            (0.000, 0.110, -0.012, 0.070, 0.080, f"LowerLegTerminalBottom_{side}"),
            (0.015, 0.110, -0.012, 0.105, 0.120, None),
            (0.035, 0.110, -0.012, 0.135, 0.155, None),
            (0.065, 0.110, -0.012, 0.145, 0.165, f"LowerLegTerminal_{side}"),
            (0.170, 0.105, 0.0, 0.130, 0.140, f"Knee_{side}"),
            (0.245, 0.100, 0.0, 0.150, 0.170, f"UpperThigh_{side}"),
            (0.315, 0.095, 0.0, 0.150, 0.170, f"Hip_{side}"),
            (0.345, 0.080, 0.0, 0.090, 0.120, None),
        ]
        rings = [
            {"z": height, "x": sign * abs(x), "y": y, "width": width, "depth": depth}
            for height, x, y, width, depth, _identifier in leg_specs
        ]
        sections = [
            section(identifier, height, width, depth)
            for height, _x, _y, width, depth, identifier in leg_specs
            if identifier
        ]
        create_section_mesh(
            f"CHR_C1B003_Leg_{side}", rings, sections, model_collection, material, root,
            cap_top=False,
        )


LANDMARKS = [
    ("Crown", "SURFACE_TANGENT", "POINT_TANGENT", (0.0, 1.0, 0.0), 0.0, 0.0),
    ("Chin", "CENTERLINE_HEIGHT_PLANE", "CORE", (0.0, 0.800, 0.0), 0.190, 0.170),
    ("Shoulder_L", "LIMB_CENTER", "EACH_LIMB", (-0.205, 0.690, 0.0), 0.105, 0.110),
    ("Shoulder_R", "LIMB_CENTER", "EACH_LIMB", (0.205, 0.690, 0.0), 0.105, 0.110),
    ("Elbow_L", "LIMB_CENTER", "EACH_LIMB", (-0.235, 0.520, 0.0), 0.100, 0.105),
    ("Elbow_R", "LIMB_CENTER", "EACH_LIMB", (0.235, 0.520, 0.0), 0.100, 0.105),
    ("ForearmTerminal_L", "LIMB_CENTER", "EACH_LIMB", (-0.235, 0.405, 0.005), 0.110, 0.115),
    ("ForearmTerminal_R", "LIMB_CENTER", "EACH_LIMB", (0.235, 0.405, 0.005), 0.110, 0.115),
    ("Chest", "CENTERLINE_HEIGHT_PLANE", "CORE", (0.0, 0.585, 0.0), 0.385, 0.260),
    ("Pelvis", "CENTERLINE_HEIGHT_PLANE", "CORE", (0.0, 0.395, 0.0), 0.350, 0.265),
    ("Crotch", "CENTERLINE_HEIGHT_PLANE", "CORE", (0.0, 0.310, 0.0), 0.250, 0.235),
    ("Hip_L", "LIMB_CENTER", "EACH_LIMB", (-0.095, 0.315, 0.0), 0.150, 0.170),
    ("Hip_R", "LIMB_CENTER", "EACH_LIMB", (0.095, 0.315, 0.0), 0.150, 0.170),
    ("Knee_L", "LIMB_CENTER", "EACH_LIMB", (-0.105, 0.170, 0.0), 0.130, 0.140),
    ("Knee_R", "LIMB_CENTER", "EACH_LIMB", (0.105, 0.170, 0.0), 0.130, 0.140),
    ("LowerLegTerminal_L", "LIMB_CENTER", "EACH_LIMB", (-0.110, 0.065, 0.012), 0.145, 0.165),
    ("LowerLegTerminal_R", "LIMB_CENTER", "EACH_LIMB", (0.110, 0.065, 0.012), 0.145, 0.165),
]


def create_landmarks(landmark_collection):
    for identifier, semantic, scope, unity_position, width, depth in LANDMARKS:
        unity_x, unity_y, unity_z = unity_position
        blender_position = (unity_x, -unity_z, unity_y)
        empty = bpy.data.objects.new(f"LM_{identifier}", None)
        landmark_collection.objects.link(empty)
        empty.location = blender_position
        empty.empty_display_type = "SPHERE"
        empty.empty_display_size = 0.008
        empty.hide_render = True
        empty["c1b_landmark_id"] = identifier
        empty["semantic"] = semantic
        empty["cross_section_scope"] = scope
        empty["front_view_full_width_h"] = width
        empty["side_view_total_depth_h"] = depth


def create_ground(qa_collection):
    bpy.ops.mesh.primitive_plane_add(size=4.0, location=(0.0, 0.0, -0.002))
    ground = bpy.context.object
    ground.name = "QA_Ground"
    ground.data.name = "QA_Ground_Mesh"
    link_object_to_collection(ground, qa_collection)
    material = make_material("MAT_QA_Ground", (0.12, 0.12, 0.12), roughness=1.0)
    ground.data.materials.append(material)
    ground["qa_only"] = True
    return ground


VIEW_DIRECTIONS = {
    "Front": Vector((0.0, 1.0, 0.0)),
    "Side": Vector((-1.0, 0.0, 0.0)),
    "Back": Vector((0.0, -1.0, 0.0)),
    "ThreeQuarter": Vector((-math.sqrt(0.5), math.sqrt(0.5), 0.0)),
}


def create_camera(name, look_direction, target, qa_collection):
    camera_data = bpy.data.cameras.new(f"CAM_C1B003_{name}_Data")
    camera = bpy.data.objects.new(f"CAM_C1B003_{name}", camera_data)
    qa_collection.objects.link(camera)
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = ORTHO_SCALE
    camera_data.lens = 50.0
    camera.location = target - look_direction.normalized() * 3.0
    camera.rotation_euler = look_direction.to_track_quat("-Z", "Y").to_euler()
    camera["c1b_view_id"] = name
    camera["look_direction_blender"] = list(look_direction)
    camera["target_blender"] = list(target)
    camera["ortho_scale"] = ORTHO_SCALE
    camera["bounds_padding_ratio"] = 0.10
    return camera


def create_lighting(qa_collection):
    light_data = bpy.data.lights.new("QA_Key_Data", type="SUN")
    light_data.energy = 3.0
    light_data.color = (1.0, 1.0, 1.0)
    light = bpy.data.objects.new("QA_Key", light_data)
    qa_collection.objects.link(light)
    light.rotation_euler = Euler(tuple(math.radians(value) for value in (50.0, -30.0, 0.0)), "XYZ")
    light["qa_only"] = True
    fill_specs = [
        ("QA_Fill_Back", (130.0, 150.0, 180.0)),
        ("QA_Fill_Left", (80.0, 90.0, 0.0)),
        ("QA_Fill_Right", (80.0, -90.0, 0.0)),
    ]
    for name, rotation_degrees in fill_specs:
        fill_data = bpy.data.lights.new(f"{name}_Data", type="SUN")
        fill_data.energy = 0.35
        fill_data.color = (1.0, 1.0, 1.0)
        fill = bpy.data.objects.new(name, fill_data)
        qa_collection.objects.link(fill)
        fill.rotation_euler = Euler(tuple(math.radians(value) for value in rotation_degrees), "XYZ")
        fill["qa_only"] = True
        fill["relative_fill_component"] = 0.116666667
    return light


def configure_scene(scene):
    scene.unit_settings.system = "METRIC"
    scene.unit_settings.scale_length = 1.0
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = RENDER_RESOLUTION
    scene.render.resolution_y = RENDER_RESOLUTION
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    scene.render.image_settings.color_mode = "RGB"
    scene.render.image_settings.color_depth = "8"
    scene.render.image_settings.compression = 50
    scene.render.film_transparent = False
    scene.render.use_file_extension = True
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    world = bpy.data.worlds.new("C1B003_QA_World")
    world.use_nodes = True
    scene.world = world
    background = world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (0.18, 0.18, 0.18, 1.0)
    background.inputs["Strength"].default_value = 1.05


def set_world(scene, color, strength):
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def render_views(scene, cameras, render_directory, ground, silhouette_material):
    os.makedirs(render_directory, exist_ok=True)
    view_layer = scene.view_layers[0]
    outputs = []
    for style in ("Neutral", "Silhouette"):
        if style == "Neutral":
            view_layer.material_override = None
            ground.hide_render = False
            set_world(scene, (0.18, 0.18, 0.18), 1.05)
        else:
            view_layer.material_override = silhouette_material
            ground.hide_render = True
            set_world(scene, (0.75, 0.75, 0.75), 0.8)
        for view_name in ("Front", "Side", "Back", "ThreeQuarter"):
            scene.camera = cameras[view_name]
            filename = f"{ASSET_ID}_r01_{style}_{view_name}.png"
            scene.render.filepath = os.path.join(render_directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    view_layer.material_override = None
    ground.hide_render = False
    set_world(scene, (0.18, 0.18, 0.18), 1.05)
    scene.camera = cameras["Front"]
    return outputs


def main():
    output_blend, render_directory = parse_args()
    os.makedirs(os.path.dirname(output_blend), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)

    clear_scene()
    scene = bpy.context.scene
    scene.name = "C1B003_Blockout"
    configure_scene(scene)

    model_collection = create_collection("C1B003_Blockout")
    landmark_collection = create_collection("C1B003_Landmarks")
    qa_collection = create_collection("C1B003_QA")

    root = bpy.data.objects.new("CHR_C1B003_Root", None)
    model_collection.objects.link(root)
    root.location = (0.0, 0.0, 0.0)
    root.scale = (1.0, 1.0, 1.0)
    root["source_pivot"] = "Neutral midpoint between lower-leg terminal ground contacts"
    root["character_forward_axis_blender"] = "-Y"
    root["up_axis_blender"] = "+Z"

    neutral_material = make_material("MAT_C1B003_NeutralWhite", (0.82, 0.80, 0.76), roughness=0.75)
    silhouette_material = make_material("MAT_C1B003_Silhouette", (0.005, 0.005, 0.005), roughness=1.0)
    silhouette_material.use_fake_user = True
    create_geometry(model_collection, neutral_material, root)
    create_landmarks(landmark_collection)
    ground = create_ground(qa_collection)
    create_lighting(qa_collection)

    target = AUTHORED_BOUNDS_CENTER_BLENDER.copy()
    cameras = {
        name: create_camera(name, direction, target, qa_collection)
        for name, direction in VIEW_DIRECTIONS.items()
    }

    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["owner_task"] = "C1B-003"
    scene["source_owner"] = SOURCE_OWNER
    scene["profile_id"] = PROFILE_ID
    scene["profile_revision"] = PROFILE_REVISION
    scene["measurement_set_sha256"] = MEASUREMENT_SET_SHA256
    scene["state"] = "START"
    scene["candidate_status"] = "BLOCKOUT_CANDIDATE"
    scene["user_visual_approval_recorded"] = False
    scene["locked_value_count"] = 0
    scene["pixel_measurement_used"] = False
    scene["reference_replica_allowed"] = False
    scene["gameplay_height_meters"] = "DEFERRED_C1B006"
    scene["collider_profile"] = "DEFERRED_CHR002"
    scene["rig_profile"] = "DEFERRED_CHR001"
    scene["render_resolution"] = RENDER_RESOLUTION
    scene["orthographic_scale"] = ORTHO_SCALE
    scene["bounds_padding_ratio"] = 0.10
    scene["authored_bounds_center_blender"] = list(AUTHORED_BOUNDS_CENTER_BLENDER)

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    render_outputs = render_views(scene, cameras, render_directory, ground, silhouette_material)
    scene["reference_render_files_json"] = json.dumps(render_outputs, separators=(",", ":"))
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)

    backup_candidates = [output_blend + str(index) for index in range(1, 10)]
    existing_backups = [path for path in backup_candidates if os.path.exists(path)]
    if existing_backups:
        raise RuntimeError(f"unexpected Blender backup files: {existing_backups}")
    print(f"C1B003_BLEND={output_blend}")
    print(f"C1B003_RENDER_DIRECTORY={render_directory}")
    print(f"C1B003_RENDER_COUNT={len(render_outputs)}")
    print("C1B003_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
