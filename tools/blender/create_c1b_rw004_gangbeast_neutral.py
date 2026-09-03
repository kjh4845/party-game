#!/usr/bin/env python3

import importlib.util
import json
import os
import sys

import bmesh
import bpy
from mathutils import Vector


BASE_PATH = os.path.join(os.path.dirname(__file__), "create_c1b_rw003_neutral.py")
SPEC = importlib.util.spec_from_file_location("c1b_rw003_base", BASE_PATH)
base = importlib.util.module_from_spec(SPEC)
SPEC.loader.exec_module(base)


ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.4.0-user-review"
ASSET_REVISION = "r04"
SOURCE_OWNER = "kjh4845"
REFERENCE_PATH = "/Users/kjh/Downloads/Gang_Beast.webp"
REFERENCE_SHA256 = "9afccdb71c696d856c47b4a7a6640c02b80c1d50ea58f1e7b42a225c21f75991"


TORSO_RINGS = (
    # z, full width, full depth.  Small buried lower rings form the crotch
    # apex while the wider rings overlap the upper legs inside the torso.
    (0.165, 0.050, 0.090),
    (0.185, 0.205, 0.165),
    (0.205, 0.335, 0.215),
    (0.230, 0.380, 0.230),
    (0.300, 0.340, 0.230),
    (0.400, 0.319, 0.225),
    (0.500, 0.315, 0.218),
    (0.600, 0.315, 0.212),
    (0.675, 0.325, 0.205),
    (0.715, 0.270, 0.180),
    (0.737, 0.180, 0.145),
)


LEG_RINGS = (
    # z, abs(center x), full width, full depth
    (0.000, 0.162, 0.050, 0.095),
    (0.018, 0.161, 0.064, 0.108),
    (0.050, 0.158, 0.076, 0.125),
    (0.090, 0.151, 0.090, 0.142),
    (0.130, 0.140, 0.108, 0.155),
    (0.165, 0.125, 0.135, 0.165),
    (0.200, 0.112, 0.150, 0.170),
    (0.240, 0.100, 0.170, 0.178),
    (0.285, 0.092, 0.170, 0.175),
)


ARM_SWEEP = (
    # abs(center x), z, front radius, side radius. The last radius is the
    # original wrist width; everything below it is a generated hemisphere,
    # not a hand/fist object.
    (0.145, 0.680, 0.060, 0.052),
    (0.170, 0.650, 0.055, 0.050),
    (0.192, 0.585, 0.047, 0.046),
    (0.210, 0.510, 0.040, 0.043),
    (0.225, 0.430, 0.036, 0.039),
    (0.240, 0.345, 0.033, 0.036),
    (0.246, 0.270, 0.032, 0.035),
)


ARM_CURVE = (
    # abs(center x), y, z, radius
    (0.095, 0.000, 0.645, 0.068),
    (0.145, 0.000, 0.680, 0.062),
    (0.170, -0.002, 0.650, 0.058),
    (0.185, -0.010, 0.585, 0.047),
    (0.198, -0.016, 0.510, 0.040),
    (0.210, -0.020, 0.460, 0.039),
    (0.225, -0.022, 0.400, 0.031),
    (0.240, -0.022, 0.360, 0.026),
    (0.252, -0.022, 0.320, 0.024),
)


CORE_HALF_WIDTH = (
    (0.000, 0.190),
    (0.050, 0.190),
    (0.100, 0.195),
    (0.170, 0.190),
    (0.220, 0.184),
    (0.300, 0.169),
    (0.400, 0.160),
    (0.500, 0.157),
    (0.600, 0.160),
    (0.670, 0.165),
    (0.720, 0.120),
    (0.750, 0.090),
)

CORE_HALF_DEPTH = (
    (0.000, 0.055),
    (0.100, 0.080),
    (0.170, 0.100),
    (0.220, 0.115),
    (0.350, 0.118),
    (0.500, 0.112),
    (0.650, 0.105),
    (0.720, 0.080),
    (0.750, 0.065),
)


BODY_LEG_OUTLINE = (
    (-0.090, 0.735),
    (-0.140, 0.720),
    (-0.175, 0.690),
    (-0.190, 0.650),
    (-0.170, 0.580),
    (-0.158, 0.500),
    (-0.160, 0.420),
    (-0.170, 0.300),
    (-0.184, 0.220),
    (-0.190, 0.170),
    (-0.190, 0.100),
    (-0.190, 0.040),
    (-0.180, 0.000),
    (-0.140, 0.000),
    (-0.130, 0.050),
    (-0.120, 0.100),
    (-0.105, 0.140),
    (-0.070, 0.165),
    (0.000, 0.170),
    (0.070, 0.165),
    (0.105, 0.140),
    (0.120, 0.100),
    (0.130, 0.050),
    (0.140, 0.000),
    (0.180, 0.000),
    (0.190, 0.040),
    (0.190, 0.100),
    (0.190, 0.170),
    (0.184, 0.220),
    (0.170, 0.300),
    (0.160, 0.420),
    (0.158, 0.500),
    (0.170, 0.580),
    (0.190, 0.650),
    (0.175, 0.690),
    (0.140, 0.720),
    (0.090, 0.735),
)


FULL_BODY_OUTLINE = (
    (-0.090, 0.735),
    (-0.145, 0.720),
    (-0.190, 0.690),
    (-0.215, 0.650),
    (-0.220, 0.600),
    (-0.225, 0.520),
    (-0.238, 0.440),
    (-0.252, 0.360),
    (-0.270, 0.315),
    (-0.276, 0.300),
    (-0.273, 0.286),
    (-0.262, 0.278),
    (-0.248, 0.280),
    (-0.235, 0.294),
    (-0.220, 0.340),
    (-0.205, 0.410),
    (-0.188, 0.480),
    (-0.175, 0.550),
    (-0.165, 0.610),
    (-0.158, 0.630),
    (-0.158, 0.500),
    (-0.160, 0.420),
    (-0.170, 0.300),
    (-0.184, 0.220),
    (-0.190, 0.170),
    (-0.190, 0.100),
    (-0.190, 0.040),
    (-0.180, 0.000),
    (-0.140, 0.000),
    (-0.130, 0.050),
    (-0.120, 0.100),
    (-0.105, 0.140),
    (-0.070, 0.165),
    (0.000, 0.170),
    (0.070, 0.165),
    (0.105, 0.140),
    (0.120, 0.100),
    (0.130, 0.050),
    (0.140, 0.000),
    (0.180, 0.000),
    (0.190, 0.040),
    (0.190, 0.100),
    (0.190, 0.170),
    (0.184, 0.220),
    (0.170, 0.300),
    (0.160, 0.420),
    (0.158, 0.500),
    (0.158, 0.630),
    (0.165, 0.610),
    (0.175, 0.550),
    (0.188, 0.480),
    (0.205, 0.410),
    (0.220, 0.340),
    (0.235, 0.294),
    (0.248, 0.280),
    (0.262, 0.278),
    (0.273, 0.286),
    (0.276, 0.300),
    (0.270, 0.315),
    (0.252, 0.360),
    (0.238, 0.440),
    (0.225, 0.520),
    (0.220, 0.600),
    (0.215, 0.650),
    (0.190, 0.690),
    (0.145, 0.720),
    (0.090, 0.735),
)


def create_variable_leg(name, side, collection, segments=40):
    vertices = []
    faces = []
    for z, center_x, width, depth in LEG_RINGS:
        for index in range(segments):
            angle = 2.0 * base.math.pi * index / segments
            x, y = base.superellipse_xy(width, depth, angle, 2.1)
            vertices.append((side * center_x + x, -0.012 + y, z))
    for ring_index in range(len(LEG_RINGS) - 1):
        lower = ring_index * segments
        upper = (ring_index + 1) * segments
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((lower + index, lower + following, upper + following, upper + index))
    bottom = len(vertices)
    vertices.append((side * LEG_RINGS[0][1], -0.012, LEG_RINGS[0][0]))
    top = len(vertices)
    vertices.append((side * LEG_RINGS[-1][1], -0.012, LEG_RINGS[-1][0]))
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((bottom, index, following))
        start = (len(LEG_RINGS) - 1) * segments
        faces.append((top, start + following, start + index))
    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    return obj


def create_forearm_capsule(name, side, collection):
    curve_data = bpy.data.curves.new(f"{name}Curve", type="CURVE")
    curve_data.dimensions = "3D"
    curve_data.resolution_u = 24
    curve_data.bevel_depth = 0.040
    curve_data.bevel_resolution = 6
    curve_data.use_fill_caps = True
    spline = curve_data.splines.new(type="BEZIER")
    spline.bezier_points.add(len(ARM_CURVE) - 1)
    coordinates = [Vector((side * x, y, z)) for x, y, z, _radius in ARM_CURVE]
    for point, coordinate, (_x, _y, _z, radius) in zip(spline.bezier_points, coordinates, ARM_CURVE):
        point.co = coordinate
        point.radius = radius / 0.040
        point.handle_left_type = "FREE"
        point.handle_right_type = "FREE"
    for index, point in enumerate(spline.bezier_points):
        if index == 0:
            tangent = (coordinates[1] - coordinates[0]) / 3.0
        elif index == len(coordinates) - 1:
            tangent = (coordinates[-1] - coordinates[-2]) / 3.0
        else:
            tangent = (coordinates[index + 1] - coordinates[index - 1]) / 6.0
        point.handle_left = point.co - tangent
        point.handle_right = point.co + tangent
    arm = bpy.data.objects.new(name, curve_data)
    collection.objects.link(arm)
    bpy.context.view_layer.objects.active = arm
    arm.select_set(True)
    bpy.ops.object.convert(target="MESH")
    arm = bpy.context.object
    arm.name = name
    terminal = base.create_ellipsoid(
        f"{name}_IntegratedRoundCap",
        (side * 0.252, -0.022, 0.320),
        (0.024, 0.025, 0.024),
        collection,
        segments=32,
        rings=20,
    )
    return [arm, terminal]


def create_body_leg_field(collection):
    elements = (
        # center, implicit ellipsoid half-size.  Every sample belongs to one
        # metaball field, so torso, pelvis, legs and forearms are born as one
        # continuous surface rather than intersecting parts.
        ((0.000, 0.000, 0.695), (0.190, 0.140, 0.055)),
        ((0.000, 0.000, 0.655), (0.220, 0.145, 0.100)),
        ((0.000, 0.000, 0.585), (0.240, 0.155, 0.140)),
        ((0.000, 0.000, 0.490), (0.260, 0.165, 0.160)),
        ((0.000, 0.000, 0.390), (0.285, 0.172, 0.160)),
        ((0.000, -0.002, 0.300), (0.300, 0.178, 0.150)),
        ((0.000, -0.004, 0.215), (0.290, 0.175, 0.135)),
        ((-0.115, -0.006, 0.160), (0.110, 0.098, 0.125)),
        ((0.115, -0.006, 0.160), (0.110, 0.098, 0.125)),
        ((-0.150, -0.010, 0.085), (0.072, 0.072, 0.100)),
        ((0.150, -0.010, 0.085), (0.072, 0.072, 0.100)),
        ((-0.168, -0.012, 0.020), (0.052, 0.060, 0.055)),
        ((0.168, -0.012, 0.020), (0.052, 0.060, 0.055)),
        ((-0.115, 0.000, 0.640), (0.115, 0.082, 0.105)),
        ((0.115, 0.000, 0.640), (0.115, 0.082, 0.105)),
        ((-0.160, -0.002, 0.600), (0.092, 0.072, 0.105)),
        ((0.160, -0.002, 0.600), (0.092, 0.072, 0.105)),
        ((-0.185, -0.005, 0.550), (0.072, 0.062, 0.095)),
        ((0.185, -0.005, 0.550), (0.072, 0.062, 0.095)),
        ((-0.202, -0.008, 0.495), (0.058, 0.054, 0.088)),
        ((0.202, -0.008, 0.495), (0.058, 0.054, 0.088)),
        ((-0.216, -0.012, 0.440), (0.058, 0.056, 0.082)),
        ((0.216, -0.012, 0.440), (0.058, 0.056, 0.082)),
        ((-0.228, -0.015, 0.385), (0.052, 0.052, 0.074)),
        ((0.228, -0.015, 0.385), (0.052, 0.052, 0.074)),
        ((-0.239, -0.018, 0.335), (0.047, 0.048, 0.064)),
        ((0.239, -0.018, 0.335), (0.047, 0.048, 0.064)),
        ((-0.246, -0.020, 0.292), (0.045, 0.046, 0.050)),
        ((0.246, -0.020, 0.292), (0.045, 0.046, 0.050)),
    )
    data = bpy.data.metaballs.new("C1B_R04_OrganicBodyLegField")
    data.resolution = 0.004
    data.render_resolution = 0.004
    data.threshold = 0.62
    obj = bpy.data.objects.new("C1B_R04_OrganicBodyLegField", data)
    collection.objects.link(obj)
    for center, size in elements:
        element = data.elements.new()
        element.type = "ELLIPSOID"
        element.co = center
        element.radius = 1.0
        element.size_x, element.size_y, element.size_z = size
        element.stiffness = 1.5
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.object
    obj.name = "C1B_R04_OrganicBodyLegField"
    minimum_z = min(float(vertex.co.z) for vertex in obj.data.vertices)
    flat_floor = minimum_z + 0.012
    for vertex in obj.data.vertices:
        if vertex.co.z < flat_floor:
            vertex.co.z = flat_floor
    obj.data.update()
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def create_single_body_leg_shell(collection, depth=0.190, outline=BODY_LEG_OUTLINE):
    vertices = []
    faces = []
    half_depth = depth * 0.5
    for y in (-half_depth, half_depth):
        vertices.extend((x, y, z) for x, z in outline)
    count = len(outline)
    faces.append(tuple(range(count - 1, -1, -1)))
    faces.append(tuple(range(count, count * 2)))
    for index in range(count):
        following = (index + 1) % count
        faces.append((index, following, count + following, count + index))
    mesh = bpy.data.meshes.new("C1B_R04_SingleBodyLegShellMesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new("C1B_R04_SingleBodyLegShell", mesh)
    collection.objects.link(obj)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bevel = obj.modifiers.new("SingleSurfaceRoundedEdges", "BEVEL")
    # Use almost the full half-depth so the body reads as one soft volume from
    # the side instead of a flat extruded plate.  The same bevel also rounds
    # the integrated forearm terminals without introducing separate hand parts.
    bevel.width = 0.092
    bevel.segments = 16
    bevel.limit_method = "ANGLE"
    bevel.use_clamp_overlap = True
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def interpolate_envelope(points, z):
    if z <= points[0][0]:
        return points[0][1]
    if z >= points[-1][0]:
        return points[-1][1]
    for (left_z, left_value), (right_z, right_value) in zip(points, points[1:]):
        if left_z <= z <= right_z:
            factor = (z - left_z) / (right_z - left_z)
            return left_value + (right_value - left_value) * factor
    return points[-1][1]


def fit_core_envelope(obj, bins=180):
    vertices = obj.data.vertices
    minimum_z = min(float(vertex.co.z) for vertex in vertices)
    maximum_z = max(float(vertex.co.z) for vertex in vertices)
    span = max(maximum_z - minimum_z, 1e-6)
    maximum_x = [0.0] * bins
    maximum_y = [0.0] * bins
    for vertex in vertices:
        index = min(bins - 1, max(0, int((float(vertex.co.z) - minimum_z) / span * (bins - 1))))
        maximum_x[index] = max(maximum_x[index], abs(float(vertex.co.x)))
        maximum_y[index] = max(maximum_y[index], abs(float(vertex.co.y)))
    for values in (maximum_x, maximum_y):
        for index in range(bins):
            if values[index] > 1e-6:
                continue
            nearest = min((other for other in range(bins) if values[other] > 1e-6), key=lambda other: abs(other - index))
            values[index] = values[nearest]
        smoothed = []
        for index in range(bins):
            window = values[max(0, index - 12) : min(bins, index + 13)]
            smoothed.append(sum(window) / len(window))
        values[:] = smoothed
    for vertex in vertices:
        z = float(vertex.co.z)
        position = min(float(bins - 1), max(0.0, (z - minimum_z) / span * (bins - 1)))
        lower_index = int(position)
        upper_index = min(bins - 1, lower_index + 1)
        blend = position - lower_index
        current_x = maximum_x[lower_index] + (maximum_x[upper_index] - maximum_x[lower_index]) * blend
        current_y = maximum_y[lower_index] + (maximum_y[upper_index] - maximum_y[lower_index]) * blend
        target_x = interpolate_envelope(CORE_HALF_WIDTH, z)
        target_y = interpolate_envelope(CORE_HALF_DEPTH, z)
        vertex.co.x *= target_x / max(current_x, 1e-6)
        vertex.co.y *= target_y / max(current_y, 1e-6)
    obj.data.update()
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def join_body(parts, collection, material):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    body = bpy.context.object
    body.name = "C1B_R04_GangBeastBody_NoHands"
    body.data.name = "C1B_R04_GangBeastBody_NoHandsMesh"
    base.link_only(body, collection)
    body.data.materials.clear()
    body.data.materials.append(material)
    body.data.remesh_voxel_size = 0.0025
    body.data.remesh_voxel_adaptivity = 0.0
    body.data.use_remesh_fix_poles = True
    body.data.use_remesh_preserve_volume = True
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.voxel_remesh()
    relax = body.modifiers.new("ReferenceSurfaceRelax", "SMOOTH")
    relax.factor = 0.07
    relax.iterations = 2
    relax.use_x = True
    relax.use_y = True
    relax.use_z = True
    bpy.ops.object.modifier_apply(modifier=relax.name)
    for polygon in body.data.polygons:
        polygon.use_smooth = True
    return body


def create_character(collection, material):
    body = create_body_leg_field(collection)
    body.name = "C1B_R04_GangBeastBody_NoHands"
    body.data.name = "C1B_R04_GangBeastBody_NoHandsMesh"
    body.data.materials.append(material)

    head = base.create_ellipsoid(
        "C1B_R04_FacelessHead_NoEyes",
        (0.0, -0.003, 0.832),
        (0.150, 0.130, 0.145),
        collection,
        segments=64,
        rings=32,
    )
    head.data.materials.append(material)
    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    head.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    character = bpy.context.object
    character.name = ASSET_ID
    character.data.name = f"{ASSET_ID}_{ASSET_REVISION}_ReviewMesh"
    while character.data.uv_layers:
        character.data.uv_layers.remove(character.data.uv_layers[0])
    character.data.materials.clear()
    character.data.materials.append(material)
    for polygon in character.data.polygons:
        polygon.use_smooth = True
    cleanup = bmesh.new()
    cleanup.from_mesh(character.data)
    bmesh.ops.remove_doubles(cleanup, verts=cleanup.verts, dist=1e-6)
    bmesh.ops.dissolve_degenerate(cleanup, edges=cleanup.edges, dist=1e-6)
    cleanup.to_mesh(character.data)
    cleanup.free()
    character.data.update()
    report = base.normalize_and_validate(character)

    forbidden = ("eye", "hand", "finger", "fist", "palm", "knuckle", "foot", "toe")
    forbidden_objects = [obj.name for obj in bpy.data.objects if any(token in obj.name.lower() for token in forbidden)]
    if forbidden_objects:
        raise RuntimeError(f"forbidden semantic objects: {forbidden_objects}")
    character["asset_id"] = ASSET_ID
    character["asset_version"] = ASSET_VERSION
    character["source_owner"] = SOURCE_OWNER
    character["reference_path"] = REFERENCE_PATH
    character["reference_sha256"] = REFERENCE_SHA256
    character["eyes_created"] = False
    character["hands_created"] = False
    character["fingers_created"] = False
    character["forearm_terminal"] = "INTEGRATED_HEMISPHERE"
    character["visible_neck_allowed"] = False
    character["user_visual_approval_recorded"] = False
    character["production_topology_approved"] = False
    return character, report


def main():
    output_blend, render_directory = base.parse_args()
    os.makedirs(os.path.dirname(output_blend), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    base.clear_scene()
    scene = bpy.context.scene
    scene.name = "C1B_RW004_GangBeastNoEyesNoHandsReview"
    base.configure_scene(scene)
    scene.world.name = "C1BRW004_QA_World"
    model_collection = base.create_collection("C1B_RW004_Model")
    qa_collection = base.create_collection("C1B_RW004_QA")
    neutral_material = base.make_material("MAT_C1BRW004_NeutralGray", (0.42, 0.42, 0.42), 0.84)
    silhouette_material = base.make_material("MAT_C1BRW004_Silhouette", (0.004, 0.004, 0.004), 1.0)
    silhouette_material.use_fake_user = True
    _character, mesh_report = create_character(model_collection, neutral_material)
    ground = base.create_ground(qa_collection)
    base.create_lights(qa_collection)
    cameras = {}
    for name, direction in base.VIEW_DIRECTIONS.items():
        camera = base.create_camera(name, direction, qa_collection)
        camera.name = f"CAM_C1BRW004_{name}"
        camera.data.name = f"CAM_C1BRW004_{name}_Data"
        cameras[name] = camera
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["candidate_status"] = "LOCAL_USER_REVIEW"
    scene["source_owner"] = SOURCE_OWNER
    scene["reference_path"] = REFERENCE_PATH
    scene["reference_sha256"] = REFERENCE_SHA256
    scene["eyes_created"] = False
    scene["hands_created"] = False
    scene["fingers_created"] = False
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["mesh_report_json"] = json.dumps(mesh_report, sort_keys=True, separators=(",", ":"))
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    base.ASSET_ID = ASSET_ID
    base.ASSET_REVISION = ASSET_REVISION
    outputs = base.render_views(scene, cameras, render_directory, ground, silhouette_material)
    scene["reference_render_files_json"] = json.dumps(outputs, separators=(",", ":"))
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    print(f"C1B_RW004_BLEND={output_blend}")
    print(f"C1B_RW004_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW004_RENDER_COUNT={len(outputs)}")
    print("C1B_RW004_MESH_REPORT=" + json.dumps(mesh_report, sort_keys=True, separators=(",", ":")))
    print("C1B_RW004_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
