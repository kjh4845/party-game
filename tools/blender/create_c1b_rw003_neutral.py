#!/usr/bin/env python3

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Vector


ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.3.0-user-review"
ASSET_REVISION = "r03"
SOURCE_OWNER = "kjh4845"
REFERENCE_PATH = "artifacts/review/character/C1_CHARACTER_HYBRID_CORE_v0.13_BELLY_CORRECTED_REVIEW.png"
REFERENCE_SHA256 = "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616"
RENDER_RESOLUTION = 2048
ORTHO_SCALE = 1.2


TORSO_RINGS = (
    # z, full width, full depth. These are taken from the approved front/side
    # silhouettes instead of being inferred from the rejected r01/r02 meshes.
    (0.255, 0.250, 0.190),
    (0.280, 0.315, 0.225),
    (0.315, 0.350, 0.240),
    (0.360, 0.349, 0.244),
    (0.400, 0.348, 0.245),
    (0.475, 0.335, 0.240),
    (0.550, 0.320, 0.235),
    (0.600, 0.312, 0.230),
    (0.650, 0.305, 0.225),
    (0.680, 0.300, 0.220),
    (0.710, 0.280, 0.210),
    (0.730, 0.225, 0.180),
    (0.745, 0.170, 0.145),
    (0.752, 0.150, 0.135),
)

LEG_RINGS = (
    # z, full width, full depth. The bottom cap is the foot; no extra foot
    # primitive or bulb is added.
    (0.000, 0.105, 0.125),
    (0.025, 0.130, 0.155),
    (0.065, 0.140, 0.170),
    (0.170, 0.140, 0.165),
    (0.280, 0.150, 0.170),
    (0.335, 0.150, 0.170),
)

ARM_SWEEP = (
    # abs(x), z, front-view radius, side-view radius. The arm only overlaps
    # the torso inside the shoulder band; all lower samples preserve daylight.
    (0.115, 0.665, 0.066, 0.060),
    (0.155, 0.685, 0.064, 0.059),
    (0.185, 0.640, 0.058, 0.056),
    (0.207, 0.600, 0.055, 0.053),
    (0.226, 0.550, 0.053, 0.052),
    (0.239, 0.490, 0.054, 0.053),
    (0.248, 0.430, 0.055, 0.054),
    (0.252, 0.385, 0.057, 0.056),
    (0.252, 0.367, 0.057, 0.056),
)


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <blend-output> <render-directory>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 2:
        raise RuntimeError("expected blend output and render directory")
    return os.path.abspath(values[0]), os.path.abspath(values[1])


def clear_scene():
    bpy.ops.object.select_all(action="SELECT")
    bpy.ops.object.delete(use_global=False)
    for collection in list(bpy.data.collections):
        bpy.data.collections.remove(collection)
    for datablocks in (
        bpy.data.meshes,
        bpy.data.curves,
        bpy.data.materials,
        bpy.data.cameras,
        bpy.data.lights,
        bpy.data.armatures,
    ):
        for datablock in list(datablocks):
            datablocks.remove(datablock)


def create_collection(name):
    collection = bpy.data.collections.new(name)
    bpy.context.scene.collection.children.link(collection)
    return collection


def link_only(obj, collection):
    for current in list(obj.users_collection):
        current.objects.unlink(obj)
    collection.objects.link(obj)


def make_material(name, color, roughness=0.84):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*color, 1.0)
    principled.inputs["Metallic"].default_value = 0.0
    principled.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in principled.inputs:
        principled.inputs["Specular IOR Level"].default_value = 0.20
    return material


def superellipse_xy(full_width, full_depth, angle, exponent):
    cosine = math.cos(angle)
    sine = math.sin(angle)
    power = 2.0 / exponent
    x = 0.5 * full_width * math.copysign(abs(cosine) ** power, cosine)
    y = 0.5 * full_depth * math.copysign(abs(sine) ** power, sine)
    return x, y


def create_ring_loft(name, rings, collection, x_offset=0.0, y_offset=0.0, exponent=2.6, segments=32):
    vertices = []
    faces = []
    for z, width, depth in rings:
        for index in range(segments):
            angle = 2.0 * math.pi * index / segments
            x, y = superellipse_xy(width, depth, angle, exponent)
            vertices.append((x_offset + x, y_offset + y, z))
    for ring_index in range(len(rings) - 1):
        lower = ring_index * segments
        upper = (ring_index + 1) * segments
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((lower + index, lower + following, upper + following, upper + index))
    bottom_center = len(vertices)
    vertices.append((x_offset, y_offset, rings[0][0]))
    top_center = len(vertices)
    vertices.append((x_offset, y_offset, rings[-1][0]))
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((bottom_center, index, following))
        top = (len(rings) - 1) * segments
        faces.append((top_center, top + following, top + index))

    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    return obj


def create_rounded_box(name, dimensions, location, bevel_width, collection):
    bpy.ops.mesh.primitive_cube_add(location=location)
    obj = bpy.context.object
    obj.name = name
    link_only(obj, collection)
    obj.dimensions = dimensions
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = obj.modifiers.new(f"{name}_SoftCorners", "BEVEL")
    bevel.width = bevel_width
    bevel.segments = 12
    bevel.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def apply_subdivision(obj, name, levels=2):
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    subdivision = obj.modifiers.new(name, "SUBSURF")
    subdivision.subdivision_type = "CATMULL_CLARK"
    subdivision.levels = levels
    subdivision.render_levels = levels
    bpy.ops.object.modifier_apply(modifier=subdivision.name)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def create_ellipsoid(name, location, scale, collection, segments=40, rings=24):
    bpy.ops.mesh.primitive_uv_sphere_add(segments=segments, ring_count=rings, location=location, scale=scale)
    obj = bpy.context.object
    obj.name = name
    link_only(obj, collection)
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    return obj


def create_bezier_arm(name, side, collection):
    control_points = (
        (0.075, 0.000, 0.655, 1.12),
        (0.115, 0.000, 0.680, 1.05),
        (0.170, -0.005, 0.640, 1.00),
        (0.195, -0.010, 0.600, 1.00),
        (0.224, -0.025, 0.550, 0.90),
        (0.244, -0.042, 0.475, 1.00),
        (0.252, -0.045, 0.400, 1.05),
        (0.252, -0.045, 0.367, 1.05),
    )
    curve_data = bpy.data.curves.new(f"{name}Curve", type="CURVE")
    curve_data.dimensions = "3D"
    curve_data.resolution_u = 24
    curve_data.bevel_depth = 0.053
    curve_data.bevel_resolution = 6
    curve_data.resolution_u = 24
    curve_data.use_fill_caps = True
    spline = curve_data.splines.new(type="BEZIER")
    spline.bezier_points.add(len(control_points) - 1)
    coordinates = [Vector((side * x, y, z)) for x, y, z, _radius in control_points]
    for point, coordinate, (_x, _y, _z, radius) in zip(spline.bezier_points, coordinates, control_points):
        point.co = coordinate
        point.radius = radius
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
    terminal = create_ellipsoid(
        f"{name}_RoundedTerminal",
        (side * 0.252, -0.045, 0.367),
        (0.057, 0.056, 0.057),
        collection,
    )
    return [arm, terminal]


def create_swept_arm(name, side, collection, y_offset=-0.045, segments=32):
    centers = []
    for x, z, _rx, _ry in ARM_SWEEP:
        # The approved arm is fused only at the shoulder. Keep the upper
        # samples centered in torso depth for a clean union, then move the
        # hanging portion slightly forward as seen in the approved side view.
        forward_blend = max(0.0, min(1.0, (0.620 - z) / 0.070))
        centers.append(Vector((side * x, y_offset * forward_blend, z)))
    vertices = []
    faces = []
    for ring_index, (center, (_x, _z, radius_front, radius_side)) in enumerate(zip(centers, ARM_SWEEP)):
        if ring_index == 0:
            tangent = centers[1] - center
        elif ring_index == len(centers) - 1:
            tangent = center - centers[ring_index - 1]
        else:
            tangent = centers[ring_index + 1] - centers[ring_index - 1]
        tangent.normalize()
        front_normal = Vector((tangent.z, 0.0, -tangent.x)).normalized()
        side_normal = Vector((0.0, 1.0, 0.0))
        for index in range(segments):
            angle = 2.0 * math.pi * index / segments
            position = center + front_normal * (radius_front * math.cos(angle)) + side_normal * (radius_side * math.sin(angle))
            vertices.append(tuple(position))

    for ring_index in range(len(centers) - 1):
        lower = ring_index * segments
        upper = (ring_index + 1) * segments
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((lower + index, lower + following, upper + following, upper + index))

    # Top is buried inside the shoulder. The lower end receives a hemisphere
    # so it reads as the approved rounded arm terminal, not a hand or flat cap.
    top_center = len(vertices)
    vertices.append(tuple(centers[0]))
    for index in range(segments):
        faces.append((top_center, (index + 1) % segments, index))

    last_center = centers[-1]
    previous = centers[-2]
    terminal_direction = (last_center - previous).normalized()
    last_front = Vector((terminal_direction.z, 0.0, -terminal_direction.x)).normalized()
    last_side = Vector((0.0, 1.0, 0.0))
    radius_front = ARM_SWEEP[-1][2]
    radius_side = ARM_SWEEP[-1][3]
    hemisphere_rings = 6
    base_ring = (len(centers) - 1) * segments
    previous_ring = base_ring
    for cap_index in range(1, hemisphere_rings):
        phi = (math.pi * 0.5) * cap_index / hemisphere_rings
        cap_center = last_center + terminal_direction * (radius_front * math.sin(phi))
        ring_scale = math.cos(phi)
        ring_start = len(vertices)
        for index in range(segments):
            angle = 2.0 * math.pi * index / segments
            position = cap_center + last_front * (radius_front * ring_scale * math.cos(angle)) + last_side * (radius_side * ring_scale * math.sin(angle))
            vertices.append(tuple(position))
        for index in range(segments):
            following = (index + 1) % segments
            faces.append((previous_ring + index, previous_ring + following, ring_start + following, ring_start + index))
        previous_ring = ring_start
    tip = len(vertices)
    vertices.append(tuple(last_center + terminal_direction * radius_front))
    for index in range(segments):
        following = (index + 1) % segments
        faces.append((previous_ring + index, previous_ring + following, tip))

    mesh = bpy.data.meshes.new(f"{name}Mesh")
    mesh.from_pydata(vertices, [], faces)
    mesh.update()
    obj = bpy.data.objects.new(name, mesh)
    collection.objects.link(obj)
    for polygon in mesh.polygons:
        polygon.use_smooth = True
    return obj


def create_rounded_head(collection):
    bpy.ops.mesh.primitive_cube_add(location=(0.0, -0.003, 0.8725))
    head = bpy.context.object
    head.name = "C1B_R03_ApprovedRoundedCuboidHead"
    link_only(head, collection)
    head.dimensions = (0.245, 0.235, 0.255)
    bpy.context.view_layer.objects.active = head
    head.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)
    bevel = head.modifiers.new("ApprovedSoftHeadCorners", "BEVEL")
    bevel.width = 0.080
    bevel.segments = 12
    bevel.limit_method = "ANGLE"
    bpy.ops.object.modifier_apply(modifier=bevel.name)
    subdivision = head.modifiers.new("ApprovedHeadSurfaceSubdivision", "SUBSURF")
    subdivision.subdivision_type = "CATMULL_CLARK"
    subdivision.levels = 2
    subdivision.render_levels = 2
    bpy.ops.object.modifier_apply(modifier=subdivision.name)
    for polygon in head.data.polygons:
        polygon.use_smooth = True
    return head


def join_and_remesh_body(parts, collection, material):
    bpy.ops.object.select_all(action="DESELECT")
    for part in parts:
        part.select_set(True)
    bpy.context.view_layer.objects.active = parts[0]
    bpy.ops.object.join()
    body = bpy.context.object
    body.name = "C1B_R03_ApprovedBodyField"
    body.data.name = "C1B_R03_ApprovedBodyFieldMesh"
    link_only(body, collection)
    body.data.materials.clear()
    body.data.materials.append(material)
    body.data.remesh_voxel_size = 0.003
    body.data.remesh_voxel_adaptivity = 0.0
    body.data.use_remesh_fix_poles = True
    body.data.use_remesh_preserve_volume = True
    bpy.context.view_layer.objects.active = body
    body.select_set(True)
    bpy.ops.object.voxel_remesh()
    relax = body.modifiers.new("MinimalPostUnionRelax", "SMOOTH")
    relax.factor = 0.18
    relax.iterations = 4
    relax.use_x = True
    relax.use_y = True
    relax.use_z = True
    bpy.ops.object.modifier_apply(modifier=relax.name)
    preserve_volume = body.modifiers.new("ApprovedSoftSurface", "LAPLACIANSMOOTH")
    preserve_volume.iterations = 6
    preserve_volume.lambda_factor = 0.16
    preserve_volume.lambda_border = 0.04
    preserve_volume.use_volume_preserve = True
    bpy.ops.object.modifier_apply(modifier=preserve_volume.name)
    for polygon in body.data.polygons:
        polygon.use_smooth = True
    return body


def mesh_components(mesh):
    adjacency = {vertex.index: set() for vertex in mesh.vertices}
    for edge in mesh.edges:
        left, right = edge.vertices
        adjacency[left].add(right)
        adjacency[right].add(left)
    unseen = set(adjacency)
    sizes = []
    while unseen:
        first = unseen.pop()
        members = {first}
        stack = [first]
        while stack:
            current = stack.pop()
            for neighbor in adjacency[current]:
                if neighbor in unseen:
                    unseen.remove(neighbor)
                    members.add(neighbor)
                    stack.append(neighbor)
        sizes.append(len(members))
    return sorted(sizes, reverse=True)


def normalize_and_validate(character):
    world = [character.matrix_world @ vertex.co for vertex in character.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in world) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in world) for axis in range(3)))
    height = maximum.z - minimum.z
    character.scale = (1.0 / height,) * 3
    bpy.context.view_layer.update()
    world = [character.matrix_world @ vertex.co for vertex in character.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in world) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in world) for axis in range(3)))
    character.location.x -= (minimum.x + maximum.x) * 0.5
    character.location.y -= (minimum.y + maximum.y) * 0.5
    character.location.z -= minimum.z
    bpy.context.view_layer.update()
    bpy.context.view_layer.objects.active = character
    character.select_set(True)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bm = bmesh.new()
    bm.from_mesh(character.data)
    report = {
        "vertices": len(character.data.vertices),
        "edges": len(character.data.edges),
        "polygons": len(character.data.polygons),
        "components": mesh_components(character.data),
        "boundaryEdges": sum(1 for edge in bm.edges if edge.is_boundary),
        "nonManifoldEdges": sum(1 for edge in bm.edges if not edge.is_manifold),
        "looseEdges": sum(1 for edge in bm.edges if not edge.link_faces),
        "degenerateFaces": sum(1 for face in bm.faces if face.calc_area() <= 1e-12),
    }
    bm.free()
    if len(report["components"]) != 2:
        raise RuntimeError(f"expected body plus direct head components, got {report['components']}")
    if any(report[key] for key in ("boundaryEdges", "nonManifoldEdges", "looseEdges", "degenerateFaces")):
        raise RuntimeError(f"invalid mesh topology: {report}")
    world = [character.matrix_world @ vertex.co for vertex in character.data.vertices]
    minimum = Vector(tuple(min(point[axis] for point in world) for axis in range(3)))
    maximum = Vector(tuple(max(point[axis] for point in world) for axis in range(3)))
    report["boundsMinimum"] = list(minimum)
    report["boundsMaximum"] = list(maximum)
    report["boundsSize"] = list(maximum - minimum)
    return report


def create_character(collection, material):
    torso = create_ring_loft("C1B_R03_Torso", TORSO_RINGS, collection, exponent=2.20, segments=48)
    apply_subdivision(torso, "ApprovedContinuousTorsoSurface", levels=2)
    left_leg = create_rounded_box(
        "C1B_R03_Leg_L", (0.140, 0.170, 0.340), (-0.107, -0.023, 0.170), 0.064, collection
    )
    right_leg = create_rounded_box(
        "C1B_R03_Leg_R", (0.140, 0.170, 0.340), (0.107, -0.023, 0.170), 0.064, collection
    )
    arm_parts = create_bezier_arm("C1B_R03_Arm_L", -1.0, collection)
    arm_parts += create_bezier_arm("C1B_R03_Arm_R", 1.0, collection)
    body = join_and_remesh_body(
        [torso, left_leg, right_leg, *arm_parts], collection, material
    )
    head = create_rounded_head(collection)
    head.data.materials.append(material)

    bpy.ops.object.select_all(action="DESELECT")
    body.select_set(True)
    head.select_set(True)
    bpy.context.view_layer.objects.active = body
    bpy.ops.object.join()
    character = bpy.context.object
    character.name = ASSET_ID
    character.data.name = f"{ASSET_ID}_r03_ReviewMesh"
    while character.data.uv_layers:
        character.data.uv_layers.remove(character.data.uv_layers[0])
    character.data.materials.clear()
    character.data.materials.append(material)
    for polygon in character.data.polygons:
        polygon.use_smooth = True

    report = normalize_and_validate(character)
    character["asset_id"] = ASSET_ID
    character["asset_version"] = ASSET_VERSION
    character["source_owner"] = SOURCE_OWNER
    character["reference_path"] = REFERENCE_PATH
    character["reference_sha256"] = REFERENCE_SHA256
    character["construction"] = "APPROVED_IMAGE_RING_LOFT_BODY_SWEEP_ARMS_ROUNDED_CUBOID_HEAD"
    character["head_shape"] = "APPROVED_SOFT_ROUNDED_CUBOID"
    character["visible_neck_allowed"] = False
    character["head_attachment"] = "DIRECT_TORSO_OVERLAP_NO_NECK_ELEMENT"
    character["arm_body_gap_required_below_z_h"] = 0.60
    character["user_visual_approval_recorded"] = False
    character["production_topology_approved"] = False
    return character, report


def create_ground(collection):
    bpy.ops.mesh.primitive_plane_add(size=4.0, location=(0.0, 0.0, -0.002))
    ground = bpy.context.object
    ground.name = "QA_Ground"
    link_only(ground, collection)
    ground.data.materials.append(make_material("MAT_QA_Ground", (0.10, 0.10, 0.10), 1.0))
    ground["qa_only"] = True
    return ground


VIEW_DIRECTIONS = {
    "Front": Vector((0.0, 1.0, 0.0)),
    "Side": Vector((-1.0, 0.0, 0.0)),
    "Back": Vector((0.0, -1.0, 0.0)),
    "ThreeQuarter": Vector((-math.sqrt(0.5), math.sqrt(0.5), 0.0)),
}


def create_camera(name, direction, collection):
    data = bpy.data.cameras.new(f"CAM_C1BRW003_{name}_Data")
    camera = bpy.data.objects.new(f"CAM_C1BRW003_{name}", data)
    collection.objects.link(camera)
    data.type = "ORTHO"
    data.ortho_scale = ORTHO_SCALE
    target = Vector((0.0, 0.0, 0.5))
    camera.location = target - direction.normalized() * 3.0
    camera.rotation_euler = direction.to_track_quat("-Z", "Y").to_euler()
    return camera


def create_lights(collection):
    specs = (
        ("QA_Key", 3.0, (50.0, -30.0, 0.0)),
        ("QA_Back", 0.35, (130.0, 150.0, 180.0)),
        ("QA_Left", 0.35, (80.0, 90.0, 0.0)),
        ("QA_Right", 0.35, (80.0, -90.0, 0.0)),
    )
    for name, energy, rotation in specs:
        data = bpy.data.lights.new(f"{name}_Data", type="SUN")
        data.energy = energy
        light = bpy.data.objects.new(name, data)
        collection.objects.link(light)
        light.rotation_euler = Euler(tuple(math.radians(value) for value in rotation), "XYZ")


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
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    world = bpy.data.worlds.new("C1BRW003_QA_World")
    world.use_nodes = True
    scene.world = world


def set_world(scene, color, strength):
    background = scene.world.node_tree.nodes.get("Background")
    background.inputs["Color"].default_value = (*color, 1.0)
    background.inputs["Strength"].default_value = strength


def render_views(scene, cameras, output_directory, ground, silhouette_material):
    os.makedirs(output_directory, exist_ok=True)
    outputs = []
    layer = scene.view_layers[0]
    for style in ("Neutral", "Silhouette"):
        if style == "Neutral":
            layer.material_override = None
            ground.hide_render = False
            set_world(scene, (0.18, 0.18, 0.18), 1.0)
        else:
            layer.material_override = silhouette_material
            ground.hide_render = True
            set_world(scene, (0.75, 0.75, 0.75), 0.8)
        for view in ("Front", "Side", "Back", "ThreeQuarter"):
            scene.camera = cameras[view]
            filename = f"{ASSET_ID}_{ASSET_REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(output_directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)
    layer.material_override = None
    ground.hide_render = False
    scene.camera = cameras["Front"]
    return outputs


def main():
    output_blend, render_directory = parse_args()
    os.makedirs(os.path.dirname(output_blend), exist_ok=True)
    os.makedirs(render_directory, exist_ok=True)
    clear_scene()
    scene = bpy.context.scene
    scene.name = "C1B_RW003_ApprovedImageNeutralReview"
    configure_scene(scene)
    model_collection = create_collection("C1B_RW003_Model")
    qa_collection = create_collection("C1B_RW003_QA")
    neutral_material = make_material("MAT_C1BRW003_Neutral", (0.83, 0.81, 0.77), 0.84)
    silhouette_material = make_material("MAT_C1BRW003_Silhouette", (0.004, 0.004, 0.004), 1.0)
    silhouette_material.use_fake_user = True
    character, mesh_report = create_character(model_collection, neutral_material)
    ground = create_ground(qa_collection)
    create_lights(qa_collection)
    cameras = {name: create_camera(name, direction, qa_collection) for name, direction in VIEW_DIRECTIONS.items()}

    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["candidate_status"] = "USER_REVIEW"
    scene["source_owner"] = SOURCE_OWNER
    scene["reference_path"] = REFERENCE_PATH
    scene["reference_sha256"] = REFERENCE_SHA256
    scene["pixel_measurement_used"] = True
    scene["reference_replica_requested"] = True
    scene["rejected_predecessor"] = "CHR_MasterCharacter_C1B_NeutralRework_r02"
    scene["rejected_failure_class"] = "REFERENCE_DRIFT_ARM_BODY_CONTACT_AND_BULBOUS_TORSO"
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["mesh_report_json"] = json.dumps(mesh_report, sort_keys=True, separators=(",", ":"))

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    outputs = render_views(scene, cameras, render_directory, ground, silhouette_material)
    scene["reference_render_files_json"] = json.dumps(outputs, separators=(",", ":"))
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    print(f"C1B_RW003_BLEND={output_blend}")
    print(f"C1B_RW003_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW003_RENDER_COUNT={len(outputs)}")
    print("C1B_RW003_MESH_REPORT=" + json.dumps(mesh_report, sort_keys=True, separators=(",", ":")))
    print("C1B_RW003_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
