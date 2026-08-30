#!/usr/bin/env python3

import json
import math
import os
import sys

import bmesh
import bpy
from mathutils import Euler, Vector


ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
ASSET_VERSION = "0.2.0-start"
ASSET_REVISION = "r02"
OWNER_TASK = "C1BRW-002"
SOURCE_OWNER = "kjh4845"
REFERENCE_PROFILE_ID = "CharacterProportionProfile-C1BRW-001-r02"
REFERENCE_SHA256 = "c1def169cefd59f19339a5b5edbac2dfd0c8fe9a05eba9ee0afb1ae598bab616"
RENDER_RESOLUTION = 2048
ORTHO_SCALE = 1.2


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <blend-output> <render-directory>")
    custom = sys.argv[sys.argv.index("--") + 1 :]
    if len(custom) != 2:
        raise RuntimeError("expected blend output and render directory")
    return os.path.abspath(custom[0]), os.path.abspath(custom[1])


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
        bpy.data.metaballs,
    ):
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


def make_material(name, base_color, roughness=0.82):
    material = bpy.data.materials.new(name)
    material.use_nodes = True
    principled = material.node_tree.nodes.get("Principled BSDF")
    principled.inputs["Base Color"].default_value = (*base_color, 1.0)
    principled.inputs["Metallic"].default_value = 0.0
    principled.inputs["Roughness"].default_value = roughness
    if "Specular IOR Level" in principled.inputs:
        principled.inputs["Specular IOR Level"].default_value = 0.22
    return material


# A single Skin graph is deliberately used for this review mesh. It creates one
# connected surface at shoulders and the pelvis, unlike the rejected six-part
# peg construction. Applied topology is a temporary C1b visual-review input;
# C4 still owns production retopology, UVs, weights and deformation loops.
GRAPH_NODES = [
    # central body. The head is modeled separately and fused into this surface
    # after Skin evaluation so there is no authored neck segment.
    ("Pelvis", (0.000, 0.000, 0.315), (0.145, 0.102)),
    ("Belly", (0.000, -0.004, 0.435), (0.155, 0.108)),
    ("Chest", (0.000, 0.000, 0.565), (0.150, 0.102)),
    ("ShoulderCenter", (0.000, 0.000, 0.665), (0.130, 0.094)),
    ("UpperTorso", (0.000, 0.000, 0.720), (0.112, 0.088)),
    # anatomical left arm: broad blended shoulder, bowed upper arm, rounded terminal
    ("Shoulder_L", (-0.112, 0.000, 0.655), (0.074, 0.068)),
    ("Deltoid_L", (-0.150, 0.000, 0.620), (0.060, 0.058)),
    ("UpperArm_L", (-0.175, 0.000, 0.565), (0.045, 0.048)),
    ("Elbow_L", (-0.190, -0.002, 0.490), (0.041, 0.044)),
    ("Forearm_L", (-0.187, -0.004, 0.415), (0.045, 0.049)),
    ("ArmTerminal_L", (-0.180, -0.005, 0.360), (0.034, 0.039)),
    # right arm
    ("Shoulder_R", (0.112, 0.000, 0.655), (0.074, 0.068)),
    ("Deltoid_R", (0.150, 0.000, 0.620), (0.060, 0.058)),
    ("UpperArm_R", (0.175, 0.000, 0.565), (0.045, 0.048)),
    ("Elbow_R", (0.190, -0.002, 0.490), (0.041, 0.044)),
    ("Forearm_R", (0.187, -0.004, 0.415), (0.045, 0.049)),
    ("ArmTerminal_R", (0.180, -0.005, 0.360), (0.034, 0.039)),
    # left leg: shared pelvis branches into a U crotch and a short rounded leg
    ("Hip_L", (-0.080, 0.000, 0.270), (0.074, 0.078)),
    ("Thigh_L", (-0.088, 0.000, 0.210), (0.064, 0.071)),
    ("Knee_L", (-0.090, -0.001, 0.135), (0.057, 0.062)),
    ("LowerLeg_L", (-0.090, -0.004, 0.065), (0.061, 0.070)),
    ("LegTerminal_L", (-0.089, -0.006, 0.015), (0.046, 0.053)),
    # right leg
    ("Hip_R", (0.080, 0.000, 0.270), (0.074, 0.078)),
    ("Thigh_R", (0.088, 0.000, 0.210), (0.064, 0.071)),
    ("Knee_R", (0.090, -0.001, 0.135), (0.057, 0.062)),
    ("LowerLeg_R", (0.090, -0.004, 0.065), (0.061, 0.070)),
    ("LegTerminal_R", (0.089, -0.006, 0.015), (0.046, 0.053)),
]

NODE_INDEX = {name: index for index, (name, _position, _radius) in enumerate(GRAPH_NODES)}

GRAPH_EDGES = [
    ("Pelvis", "Belly"),
    ("Belly", "Chest"),
    ("Chest", "ShoulderCenter"),
    ("ShoulderCenter", "UpperTorso"),
    ("ShoulderCenter", "Shoulder_L"),
    ("Shoulder_L", "Deltoid_L"),
    ("Deltoid_L", "UpperArm_L"),
    ("UpperArm_L", "Elbow_L"),
    ("Elbow_L", "Forearm_L"),
    ("Forearm_L", "ArmTerminal_L"),
    ("ShoulderCenter", "Shoulder_R"),
    ("Shoulder_R", "Deltoid_R"),
    ("Deltoid_R", "UpperArm_R"),
    ("UpperArm_R", "Elbow_R"),
    ("Elbow_R", "Forearm_R"),
    ("Forearm_R", "ArmTerminal_R"),
    ("Pelvis", "Hip_L"),
    ("Hip_L", "Thigh_L"),
    ("Thigh_L", "Knee_L"),
    ("Knee_L", "LowerLeg_L"),
    ("LowerLeg_L", "LegTerminal_L"),
    ("Pelvis", "Hip_R"),
    ("Hip_R", "Thigh_R"),
    ("Thigh_R", "Knee_R"),
    ("Knee_R", "LowerLeg_R"),
    ("LowerLeg_R", "LegTerminal_R"),
]


META_ELEMENTS = [
    # id, center, ellipsoid size. All elements share one implicit field, so
    # there are no mesh seams between torso, shoulders, arms, pelvis or legs.
    ("Torso", (0.000, 0.000, 0.485), (0.240, 0.185, 0.405)),
    ("ShoulderMass", (0.000, 0.000, 0.665), (0.270, 0.185, 0.150)),
    ("PelvisMass", (0.000, -0.003, 0.315), (0.240, 0.190, 0.160)),
    # left arm
    ("Shoulder_L", (-0.145, 0.000, 0.635), (0.085, 0.085, 0.105)),
    ("UpperArm_L", (-0.178, 0.000, 0.565), (0.070, 0.075, 0.120)),
    ("Forearm_L", (-0.195, -0.002, 0.455), (0.065, 0.072, 0.125)),
    ("ArmTerminal_L", (-0.192, -0.004, 0.370), (0.068, 0.075, 0.068)),
    # right arm
    ("Shoulder_R", (0.145, 0.000, 0.635), (0.085, 0.085, 0.105)),
    ("UpperArm_R", (0.178, 0.000, 0.565), (0.070, 0.075, 0.120)),
    ("Forearm_R", (0.195, -0.002, 0.455), (0.065, 0.072, 0.125)),
    ("ArmTerminal_R", (0.192, -0.004, 0.370), (0.068, 0.075, 0.068)),
    # left leg
    ("Hip_L", (-0.075, 0.000, 0.285), (0.120, 0.120, 0.140)),
    ("Thigh_L", (-0.085, 0.000, 0.220), (0.108, 0.115, 0.150)),
    ("LowerLeg_L", (-0.090, -0.003, 0.105), (0.100, 0.110, 0.155)),
    ("LegTerminal_L", (-0.090, -0.006, 0.030), (0.105, 0.118, 0.060)),
    # right leg
    ("Hip_R", (0.075, 0.000, 0.285), (0.120, 0.120, 0.140)),
    ("Thigh_R", (0.085, 0.000, 0.220), (0.108, 0.115, 0.150)),
    ("LowerLeg_R", (0.090, -0.003, 0.105), (0.100, 0.110, 0.155)),
    ("LegTerminal_R", (0.090, -0.006, 0.030), (0.105, 0.118, 0.060)),
]


def graph_payload():
    return {
        "nodes": [
            {"id": name, "position": list(position), "radius": list(radius)}
            for name, position, radius in GRAPH_NODES
        ],
        "edges": [[left, right] for left, right in GRAPH_EDGES],
    }


def meta_payload():
    return {
        "construction": "SEAMLESS_BODY_FIELD_PLUS_DIRECT_HEAD_OVERLAP",
        "visibleNeckAllowed": False,
        "headAttachment": "DIRECT_OVERLAP_TORSO_ATTACHMENT",
        "headShape": "ROUND",
        "torsoArmVisibleSeamAllowed": False,
        "head": {"type": "ROUND_UV_SPHERE", "center": [0.0, -0.002, 0.835], "scale": [0.115, 0.105, 0.130]},
        "elements": [
            {"id": name, "center": list(center), "size": list(size)}
            for name, center, size in META_ELEMENTS
        ],
    }


def create_seamless_metaball_review_mesh(model_collection, material):
    meta = bpy.data.metaballs.new("C1B_R02_ContinuousField")
    meta.resolution = 0.010
    meta.render_resolution = 0.010
    meta.threshold = 0.72
    obj = bpy.data.objects.new(ASSET_ID, meta)
    model_collection.objects.link(obj)

    for name, center, size in META_ELEMENTS:
        element = meta.elements.new()
        element.type = "ELLIPSOID"
        element.co = center
        element.radius = 1.0
        element.size_x, element.size_y, element.size_z = size
        element.stiffness = 2.0

    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.convert(target="MESH")
    obj = bpy.context.object
    obj.name = ASSET_ID
    obj.data.name = f"{ASSET_ID}_ReviewMesh"

    surface_smooth = obj.modifiers.new("C1B_ImplicitSurfaceRelax", "SMOOTH")
    surface_smooth.factor = 0.32
    surface_smooth.iterations = 3
    surface_smooth.use_x = True
    surface_smooth.use_y = True
    surface_smooth.use_z = True
    bpy.ops.object.modifier_apply(modifier=surface_smooth.name)

    cleanup = bmesh.new()
    cleanup.from_mesh(obj.data)
    isolated_vertices = [vertex for vertex in cleanup.verts if not vertex.link_edges]
    if isolated_vertices:
        bmesh.ops.delete(cleanup, geom=isolated_vertices, context="VERTS")
    cleanup.to_mesh(obj.data)
    cleanup.free()
    obj.data.update()

    # The head is a round closed mass placed directly into the upper torso.
    # It is joined into the same render object but deliberately not blended
    # through an intermediate neck field. This matches the requested visual
    # construction: direct attachment, visible neck geometry zero.
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=32,
        ring_count=20,
        location=(0.0, -0.002, 0.835),
        scale=(0.115, 0.105, 0.130),
    )
    head = bpy.context.object
    head.name = "C1B_RoundHead_DirectAttachment"
    head.data.name = "C1B_RoundHead_DirectAttachmentMesh"
    link_object_to_collection(head, model_collection)
    head.data.materials.append(material)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    head.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()
    obj.name = ASSET_ID
    obj.data.name = f"{ASSET_ID}_ReviewMesh"

    # UV authoring belongs to the deferred production-topology pass. The UV
    # sphere primitive creates a layer automatically, so remove it from this
    # shape-only Neutral review source.
    while obj.data.uv_layers:
        obj.data.uv_layers.remove(obj.data.uv_layers[0])

    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    obj.data.materials.clear()
    obj.data.materials.append(material)

    world_vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    minimum = Vector(tuple(min(vertex[index] for vertex in world_vertices) for index in range(3)))
    maximum = Vector(tuple(max(vertex[index] for vertex in world_vertices) for index in range(3)))
    height = maximum.z - minimum.z
    if height <= 0.0:
        raise RuntimeError("generated metaball mesh has zero height")
    scale = 1.0 / height
    obj.scale = (scale, scale, scale)
    bpy.context.view_layer.update()
    scaled = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    scaled_minimum = Vector(tuple(min(vertex[index] for vertex in scaled) for index in range(3)))
    scaled_maximum = Vector(tuple(max(vertex[index] for vertex in scaled) for index in range(3)))
    obj.location.x -= (scaled_minimum.x + scaled_maximum.x) * 0.5
    obj.location.y -= (scaled_minimum.y + scaled_maximum.y) * 0.5
    obj.location.z -= scaled_minimum.z
    bpy.context.view_layer.update()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    obj["asset_id"] = ASSET_ID
    obj["asset_version"] = ASSET_VERSION
    obj["owner_task"] = OWNER_TASK
    obj["source_owner"] = SOURCE_OWNER
    obj["reference_profile_id"] = REFERENCE_PROFILE_ID
    obj["geometry_role"] = "C1B_CONTINUOUS_NEUTRAL_REVIEW_MESH"
    obj["production_topology_approved"] = False
    obj["skinning_approved"] = False
    obj["construction_contract_json"] = json.dumps(meta_payload(), sort_keys=True, separators=(",", ":"))
    obj["visible_neck_allowed"] = False
    obj["head_attachment"] = "DIRECT_OVERLAP_TORSO_ATTACHMENT"
    obj["head_shape"] = "ROUND"
    obj["torso_arm_visible_seam_allowed"] = False
    return obj


def create_continuous_review_mesh(model_collection, material):
    vertices = [position for _name, position, _radius in GRAPH_NODES]
    edges = [(NODE_INDEX[left], NODE_INDEX[right]) for left, right in GRAPH_EDGES]
    mesh = bpy.data.meshes.new(f"{ASSET_ID}_GraphMesh")
    mesh.from_pydata(vertices, edges, [])
    mesh.update()

    obj = bpy.data.objects.new(ASSET_ID, mesh)
    model_collection.objects.link(obj)
    obj.data.materials.append(material)

    skin = obj.modifiers.new("C1B_ContinuousSkin", "SKIN")
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.context.view_layer.update()

    skin_data = mesh.skin_vertices[0].data
    for index, (_name, _position, radius) in enumerate(GRAPH_NODES):
        skin_data[index].radius = radius
    skin_data[NODE_INDEX["Pelvis"]].use_root = True

    subdivision = obj.modifiers.new("C1B_SoftHeroSubdivision", "SUBSURF")
    subdivision.subdivision_type = "CATMULL_CLARK"
    subdivision.levels = 2
    subdivision.render_levels = 2
    subdivision.show_only_control_edges = True

    bpy.ops.object.modifier_apply(modifier=skin.name)
    bpy.ops.object.modifier_apply(modifier=subdivision.name)

    smooth = obj.modifiers.new("C1B_BodyRelax", "SMOOTH")
    smooth.factor = 0.25
    smooth.iterations = 2
    smooth.use_x = True
    smooth.use_y = True
    smooth.use_z = True
    bpy.ops.object.modifier_apply(modifier=smooth.name)

    # The user-approved correction requires a round head directly attached to
    # the torso, with no visible neck. The ellipsoid overlaps UpperTorso and a
    # voxel remesh fuses both surfaces while also relaxing Skin branch creases
    # at torso-to-shoulder-to-arm transitions.
    bpy.ops.mesh.primitive_uv_sphere_add(
        segments=32,
        ring_count=20,
        location=(0.0, -0.002, 0.820),
        scale=(0.115, 0.105, 0.130),
    )
    head = bpy.context.object
    head.name = "C1B_RoundHead_Construction"
    head.data.name = "C1B_RoundHead_ConstructionMesh"
    link_object_to_collection(head, model_collection)
    head.data.materials.append(material)
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    bpy.ops.object.select_all(action="DESELECT")
    obj.select_set(True)
    head.select_set(True)
    bpy.context.view_layer.objects.active = obj
    bpy.ops.object.join()
    obj.name = ASSET_ID
    obj.data.name = f"{ASSET_ID}_ReviewMesh"

    obj.data.remesh_voxel_size = 0.005
    obj.data.remesh_voxel_adaptivity = 0.0
    obj.data.use_remesh_fix_poles = True
    obj.data.use_remesh_preserve_volume = True
    bpy.context.view_layer.objects.active = obj
    obj.select_set(True)
    bpy.ops.object.voxel_remesh()

    seamless_smooth = obj.modifiers.new("C1B_SeamlessSurfaceRelax", "SMOOTH")
    seamless_smooth.factor = 0.42
    seamless_smooth.iterations = 6
    seamless_smooth.use_x = True
    seamless_smooth.use_y = True
    seamless_smooth.use_z = True
    bpy.ops.object.modifier_apply(modifier=seamless_smooth.name)

    obj.data.materials.clear()
    obj.data.materials.append(material)

    cleanup = bmesh.new()
    cleanup.from_mesh(obj.data)
    isolated_vertices = [vertex for vertex in cleanup.verts if not vertex.link_edges]
    if isolated_vertices:
        bmesh.ops.delete(cleanup, geom=isolated_vertices, context="VERTS")
    cleanup.to_mesh(obj.data)
    cleanup.free()
    obj.data.update()

    for polygon in obj.data.polygons:
        polygon.use_smooth = True
    obj.data.set_sharp_from_angle(angle=math.radians(70.0))

    world_vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    minimum = Vector((min(v.x for v in world_vertices), min(v.y for v in world_vertices), min(v.z for v in world_vertices)))
    maximum = Vector((max(v.x for v in world_vertices), max(v.y for v in world_vertices), max(v.z for v in world_vertices)))
    height = maximum.z - minimum.z
    if height <= 0.0:
        raise RuntimeError("generated mesh has zero height")
    uniform_scale = 1.0 / height
    obj.scale = (uniform_scale, uniform_scale, uniform_scale)
    bpy.context.view_layer.update()

    scaled_vertices = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    scaled_minimum = Vector((min(v.x for v in scaled_vertices), min(v.y for v in scaled_vertices), min(v.z for v in scaled_vertices)))
    scaled_maximum = Vector((max(v.x for v in scaled_vertices), max(v.y for v in scaled_vertices), max(v.z for v in scaled_vertices)))
    obj.location.x -= (scaled_minimum.x + scaled_maximum.x) * 0.5
    obj.location.y -= (scaled_minimum.y + scaled_maximum.y) * 0.5
    obj.location.z -= scaled_minimum.z
    bpy.context.view_layer.update()
    bpy.ops.object.transform_apply(location=False, rotation=False, scale=True)

    obj["asset_id"] = ASSET_ID
    obj["asset_version"] = ASSET_VERSION
    obj["owner_task"] = OWNER_TASK
    obj["source_owner"] = SOURCE_OWNER
    obj["reference_profile_id"] = REFERENCE_PROFILE_ID
    obj["geometry_role"] = "C1B_CONTINUOUS_NEUTRAL_REVIEW_MESH"
    obj["visible_neck_allowed"] = False
    obj["head_attachment"] = "DIRECT_OVERLAP_TORSO_ATTACHMENT"
    obj["head_shape"] = "ROUND"
    obj["torso_arm_visible_seam_allowed"] = False
    obj["production_topology_approved"] = False
    obj["skinning_approved"] = False
    obj["graph_contract_json"] = json.dumps(graph_payload(), sort_keys=True, separators=(",", ":"))
    return obj


def mesh_component_reports(mesh):
    adjacency = {vertex.index: set() for vertex in mesh.vertices}
    for edge in mesh.edges:
        left, right = edge.vertices
        adjacency[left].add(right)
        adjacency[right].add(left)
    unseen = set(adjacency)
    components = []
    while unseen:
        members = {unseen.pop()}
        stack = list(members)
        while stack:
            current = stack.pop()
            for neighbor in adjacency[current]:
                if neighbor in unseen:
                    unseen.remove(neighbor)
                    members.add(neighbor)
                    stack.append(neighbor)
        points = [mesh.vertices[index].co for index in members]
        components.append(
            {
                "vertices": len(members),
                "minimum": [min(point[axis] for point in points) for axis in range(3)],
                "maximum": [max(point[axis] for point in points) for axis in range(3)],
            }
        )
    return sorted(components, key=lambda entry: entry["vertices"], reverse=True)


def validate_mesh(obj):
    mesh = obj.data
    bm = bmesh.new()
    bm.from_mesh(mesh)
    boundary_edges = [edge for edge in bm.edges if edge.is_boundary]
    non_manifold_edges = [edge for edge in bm.edges if not edge.is_manifold]
    loose_edges = [edge for edge in bm.edges if len(edge.link_faces) == 0]
    degenerate_faces = [face for face in bm.faces if face.calc_area() <= 1e-12]
    bm.free()

    if boundary_edges:
        raise RuntimeError(f"boundary edges remain: {len(boundary_edges)}")
    if non_manifold_edges:
        raise RuntimeError(f"non-manifold edges remain: {len(non_manifold_edges)}")
    if loose_edges:
        raise RuntimeError(f"loose edges remain: {len(loose_edges)}")
    if degenerate_faces:
        raise RuntimeError(f"degenerate faces remain: {len(degenerate_faces)}")
    component_reports = mesh_component_reports(mesh)
    component_count = len(component_reports)
    if component_count != 2:
        raise RuntimeError(
            f"review mesh must contain body plus directly attached head components: {json.dumps(component_reports, separators=(',', ':'))}"
        )

    world_vertices = [obj.matrix_world @ vertex.co for vertex in mesh.vertices]
    minimum = Vector((min(v.x for v in world_vertices), min(v.y for v in world_vertices), min(v.z for v in world_vertices)))
    maximum = Vector((max(v.x for v in world_vertices), max(v.y for v in world_vertices), max(v.z for v in world_vertices)))
    size = maximum - minimum
    if abs(size.z - 1.0) > 0.000001 or abs(minimum.z) > 0.000001:
        raise RuntimeError(f"normalization mismatch: minZ={minimum.z} height={size.z}")
    if abs(minimum.x + maximum.x) > 0.000001:
        raise RuntimeError("left/right bounds are not symmetric")
    return {
        "vertices": len(mesh.vertices),
        "edges": len(mesh.edges),
        "polygons": len(mesh.polygons),
        "connectedComponents": 2,
        "boundaryEdges": 0,
        "nonManifoldEdges": 0,
        "looseEdges": 0,
        "degenerateFaces": 0,
        "boundsMinimum": list(minimum),
        "boundsMaximum": list(maximum),
        "boundsSize": list(size),
    }


def create_ground(qa_collection):
    bpy.ops.mesh.primitive_plane_add(size=4.0, location=(0.0, 0.0, -0.002))
    ground = bpy.context.object
    ground.name = "QA_Ground"
    link_object_to_collection(ground, qa_collection)
    ground.data.materials.append(make_material("MAT_QA_Ground", (0.105, 0.105, 0.105), 1.0))
    ground["qa_only"] = True
    return ground


VIEW_DIRECTIONS = {
    "Front": Vector((0.0, 1.0, 0.0)),
    "Side": Vector((-1.0, 0.0, 0.0)),
    "Back": Vector((0.0, -1.0, 0.0)),
    "ThreeQuarter": Vector((-math.sqrt(0.5), math.sqrt(0.5), 0.0)),
}


def create_camera(view_id, look_direction, target, qa_collection):
    camera_data = bpy.data.cameras.new(f"CAM_C1BRW002_{view_id}_Data")
    camera = bpy.data.objects.new(f"CAM_C1BRW002_{view_id}", camera_data)
    qa_collection.objects.link(camera)
    camera_data.type = "ORTHO"
    camera_data.ortho_scale = ORTHO_SCALE
    camera.location = target - look_direction.normalized() * 3.0
    camera.rotation_euler = look_direction.to_track_quat("-Z", "Y").to_euler()
    camera["view_id"] = view_id
    camera["bounds_padding_ratio"] = 0.10
    return camera


def create_lighting(qa_collection):
    light_specs = [
        ("QA_Key", 3.0, (50.0, -30.0, 0.0)),
        ("QA_Fill_Back", 0.35, (130.0, 150.0, 180.0)),
        ("QA_Fill_Left", 0.35, (80.0, 90.0, 0.0)),
        ("QA_Fill_Right", 0.35, (80.0, -90.0, 0.0)),
    ]
    for name, energy, rotation_degrees in light_specs:
        data = bpy.data.lights.new(f"{name}_Data", type="SUN")
        data.energy = energy
        data.color = (1.0, 1.0, 1.0)
        light = bpy.data.objects.new(name, data)
        qa_collection.objects.link(light)
        light.rotation_euler = Euler(tuple(math.radians(value) for value in rotation_degrees), "XYZ")
        light["qa_only"] = True


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
    scene.view_settings.view_transform = "Standard"
    scene.view_settings.look = "None"
    scene.view_settings.exposure = 0.0
    scene.view_settings.gamma = 1.0
    world = bpy.data.worlds.new("C1BRW002_QA_World")
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
        for view_id in ("Front", "Side", "Back", "ThreeQuarter"):
            scene.camera = cameras[view_id]
            filename = f"{ASSET_ID}_{ASSET_REVISION}_{style}_{view_id}.png"
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
    scene.name = "C1B_RW002_NeutralReview"
    configure_scene(scene)

    model_collection = create_collection("C1B_RW002_Model")
    qa_collection = create_collection("C1B_RW002_QA")
    neutral_material = make_material("MAT_C1BRW002_Neutral", (0.83, 0.81, 0.77), 0.82)
    silhouette_material = make_material("MAT_C1BRW002_Silhouette", (0.004, 0.004, 0.004), 1.0)
    silhouette_material.use_fake_user = True

    character = create_seamless_metaball_review_mesh(model_collection, neutral_material)
    mesh_report = validate_mesh(character)
    ground = create_ground(qa_collection)
    create_lighting(qa_collection)

    target = Vector((0.0, 0.0, 0.5))
    cameras = {
        view_id: create_camera(view_id, direction, target, qa_collection)
        for view_id, direction in VIEW_DIRECTIONS.items()
    }

    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = ASSET_VERSION
    scene["owner_task"] = OWNER_TASK
    scene["source_owner"] = SOURCE_OWNER
    scene["state"] = "START"
    scene["candidate_status"] = "USER_REVIEW"
    scene["reference_profile_id"] = REFERENCE_PROFILE_ID
    scene["direction_reference_sha256"] = REFERENCE_SHA256
    scene["pixel_measurement_used"] = False
    scene["reference_replica_allowed"] = False
    scene["user_visual_approval_recorded"] = False
    scene["production_topology_approved"] = False
    scene["armature_count"] = 0
    scene["action_count"] = 0
    scene["collider_count"] = 0
    scene["mesh_report_json"] = json.dumps(mesh_report, sort_keys=True, separators=(",", ":"))
    scene["rejected_predecessor"] = "CHR_MasterCharacter_C1B_NeutralRework_r01"
    scene["rejected_failure_class"] = "TORSO_ARM_SEAM_SQUARE_HEAD_AND_UNREQUESTED_NECK"
    scene["visible_neck_allowed"] = False
    scene["head_attachment"] = "DIRECT_OVERLAP_TORSO_ATTACHMENT"
    scene["head_shape"] = "ROUND"
    scene["torso_arm_visible_seam_allowed"] = False

    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)
    outputs = render_views(scene, cameras, render_directory, ground, silhouette_material)
    scene["reference_render_files_json"] = json.dumps(outputs, separators=(",", ":"))
    bpy.ops.wm.save_as_mainfile(filepath=output_blend, compress=True)

    backup_candidates = [output_blend + str(index) for index in range(1, 10)]
    existing_backups = [path for path in backup_candidates if os.path.exists(path)]
    if existing_backups:
        raise RuntimeError(f"unexpected Blender backup files: {existing_backups}")

    print(f"C1B_RW002_BLEND={output_blend}")
    print(f"C1B_RW002_RENDER_DIRECTORY={render_directory}")
    print(f"C1B_RW002_RENDER_COUNT={len(outputs)}")
    print(f"C1B_RW002_MESH_REPORT={json.dumps(mesh_report, sort_keys=True, separators=(',', ':'))}")
    print("C1B_RW002_GENERATION_RESULT=PASS")


if __name__ == "__main__":
    main()
