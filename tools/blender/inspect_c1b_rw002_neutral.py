#!/usr/bin/env python3

import hashlib
import json
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.kdtree import KDTree


MODEL_OBJECT = "CHR_MasterCharacter_C1B_NeutralRework"
EXPECTED_VIEWS = ("Back", "Front", "Side", "ThreeQuarter")
QUANTIZATION = 1_000_000


def rounded(value):
    return round(float(value), 9)


def vector_values(value):
    return [rounded(component) for component in value]


def quantized(value):
    return tuple(int(round(float(component) * QUANTIZATION)) for component in value)


def canonical_sha(value):
    return hashlib.sha256(
        json.dumps(value, sort_keys=True, separators=(",", ":")).encode("ascii")
    ).hexdigest()


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


def mesh_fingerprints(obj):
    coordinates = [quantized(obj.matrix_world @ vertex.co) for vertex in obj.data.vertices]
    edges = sorted(tuple(sorted((coordinates[a], coordinates[b]))) for a, b in (edge.vertices for edge in obj.data.edges))
    polygons = sorted(tuple(coordinates[index] for index in polygon.vertices) for polygon in obj.data.polygons)
    position_payload = sorted(coordinates)
    topology_payload = {"vertices": sorted(coordinates), "edges": edges, "polygons": polygons}
    return {
        "positionSha256": canonical_sha(position_payload),
        "orientedTopologySha256": canonical_sha(topology_payload),
    }


def symmetry_contract(obj):
    coordinates = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    tree = KDTree(len(coordinates))
    for index, coordinate in enumerate(coordinates):
        tree.insert(coordinate, index)
    tree.balance()
    mirror_map = {}
    deviations = []
    missing_vertices = []
    for index, coordinate in enumerate(coordinates):
        _nearest, mirror_index, deviation = tree.find((-coordinate.x, coordinate.y, coordinate.z))
        deviations.append(float(deviation))
        if deviation > 0.000001:
            missing_vertices.append(index)
        else:
            mirror_map[index] = mirror_index
    edge_set = {tuple(sorted(edge.vertices)) for edge in obj.data.edges}
    missing_edges = []
    for edge in edge_set:
        if edge[0] not in mirror_map or edge[1] not in mirror_map:
            missing_edges.append(edge)
            continue
        mirrored = tuple(sorted((mirror_map[edge[0]], mirror_map[edge[1]])))
        if mirrored not in edge_set:
            missing_edges.append(edge)
    polygon_set = {tuple(sorted(polygon.vertices)) for polygon in obj.data.polygons}
    missing_polygons = []
    for polygon in polygon_set:
        if any(index not in mirror_map for index in polygon):
            missing_polygons.append(polygon)
            continue
        mirrored = tuple(sorted(mirror_map[index] for index in polygon))
        if mirrored not in polygon_set:
            missing_polygons.append(polygon)
    return {
        "maximumPositionDeviationH": rounded(max(deviations) if deviations else 0.0),
        "positionToleranceH": 0.000001,
        "missingMirroredVertices": len(missing_vertices),
        "missingMirroredEdges": len(missing_edges),
        "missingMirroredPolygons": len(missing_polygons),
    }


def camera_record(camera, target):
    direction = camera.rotation_euler.to_matrix() @ Vector((0.0, 0.0, -1.0))
    offset = target - camera.location
    deviation = (direction.normalized() - offset.normalized()).length if offset.length else math.inf
    return {
        "name": camera.name,
        "type": camera.data.type,
        "orthoScale": rounded(camera.data.ortho_scale),
        "location": vector_values(camera.location),
        "lookDirection": vector_values(direction.normalized()),
        "target": vector_values(target),
        "opticalAxisDeviation": rounded(deviation),
    }


def inspect():
    bpy.context.view_layer.update()
    scene = bpy.context.scene
    model = bpy.data.objects.get(MODEL_OBJECT)
    errors = []
    if model is None or model.type != "MESH":
        errors.append("MODEL_OBJECT")
        model_objects = []
    else:
        model_objects = [obj for obj in bpy.data.objects if obj.type == "MESH" and obj.get("geometry_role") == "C1B_CONTINUOUS_NEUTRAL_REVIEW_MESH"]
    if len(model_objects) != 1:
        errors.append("MODEL_OBJECT_SET")

    mesh_record = {}
    symmetry = {}
    bounds = {}
    if model is not None and model.type == "MESH":
        mesh = model.data
        bm = bmesh.new()
        bm.from_mesh(mesh)
        boundary = sum(1 for edge in bm.edges if edge.is_boundary)
        non_manifold = sum(1 for edge in bm.edges if not edge.is_manifold)
        loose = sum(1 for edge in bm.edges if len(edge.link_faces) == 0)
        degenerate = sum(1 for face in bm.faces if face.calc_area() <= 1e-12)
        bm.free()
        non_finite = sum(
            1 for vertex in mesh.vertices
            if not all(math.isfinite(float(component)) for component in vertex.co)
        )
        world = [model.matrix_world @ vertex.co for vertex in mesh.vertices]
        minimum = Vector(tuple(min(point[index] for point in world) for index in range(3)))
        maximum = Vector(tuple(max(point[index] for point in world) for index in range(3)))
        size = maximum - minimum
        bounds = {"minimum": vector_values(minimum), "maximum": vector_values(maximum), "size": vector_values(size)}
        fingerprints = mesh_fingerprints(model)
        mesh_record = {
            "object": model.name,
            "meshDatablock": mesh.name,
            "vertices": len(mesh.vertices),
            "edges": len(mesh.edges),
            "polygons": len(mesh.polygons),
            "connectedComponents": len(mesh_components(mesh)),
            "componentVertexCounts": mesh_components(mesh),
            "boundaryEdges": boundary,
            "nonManifoldEdges": non_manifold,
            "looseEdges": loose,
            "degenerateFaces": degenerate,
            "nonFiniteVertices": non_finite,
            "uvLayers": len(mesh.uv_layers),
            "vertexGroups": len(model.vertex_groups),
            "weightedVertexAssignments": sum(len(vertex.groups) for vertex in mesh.vertices),
            "modifiers": len(model.modifiers),
            "shapeKeys": 0 if mesh.shape_keys is None else len(mesh.shape_keys.key_blocks),
            "materialSlots": len(model.material_slots),
            **fingerprints,
        }
        symmetry = symmetry_contract(model)

    external_images = sorted(image.filepath for image in bpy.data.images if image.source == "FILE" and image.filepath)
    packed_images = sorted(image.name for image in bpy.data.images if image.packed_file is not None)
    collider_objects = sorted(
        obj.name for obj in bpy.data.objects
        if "collider" in obj.name.lower() or obj.get("collider_role") is not None
    )
    forbidden_tokens = ("c1b003", "c1b004", "baseplusproximalcap", "hand", "finger", "fist", "foot", "shoe", "toe")
    forbidden_model_names = sorted(
        name for name in ([obj.name for obj in model_objects] + [obj.data.name for obj in model_objects])
        if any(token in name.lower() for token in forbidden_tokens)
    )
    lod_objects = sorted(obj.name for obj in bpy.data.objects if "lod" in obj.name.lower())

    target = Vector((0.0, 0.0, 0.5))
    cameras = sorted(
        [camera_record(obj, target) for obj in bpy.data.objects if obj.type == "CAMERA"],
        key=lambda value: value["name"],
    )
    graph_text = model.get("graph_contract_json", "") if model else ""
    try:
        graph = json.loads(graph_text)
    except (TypeError, json.JSONDecodeError):
        graph = None

    payload = {
        "file": bpy.data.filepath,
        "scene": {
            "name": scene.name,
            "assetId": scene.get("asset_id"),
            "assetVersion": scene.get("asset_version"),
            "ownerTask": scene.get("owner_task"),
            "sourceOwner": scene.get("source_owner"),
            "state": scene.get("state"),
            "candidateStatus": scene.get("candidate_status"),
            "referenceProfileId": scene.get("reference_profile_id"),
            "directionReferenceSha256": scene.get("direction_reference_sha256"),
            "pixelMeasurementUsed": scene.get("pixel_measurement_used"),
            "referenceReplicaAllowed": scene.get("reference_replica_allowed"),
            "userVisualApprovalRecorded": scene.get("user_visual_approval_recorded"),
            "productionTopologyApproved": scene.get("production_topology_approved"),
            "rejectedPredecessor": scene.get("rejected_predecessor"),
            "rejectedFailureClass": scene.get("rejected_failure_class"),
        },
        "counts": {
            "objects": len(bpy.data.objects),
            "collections": len(bpy.data.collections),
            "modelMeshObjects": len(model_objects),
            "modelMeshDatablocks": len({obj.data.name for obj in model_objects}),
            "allMeshDatablocks": len(bpy.data.meshes),
            "materials": len(bpy.data.materials),
            "cameras": len(bpy.data.cameras),
            "lights": len(bpy.data.lights),
            "worlds": len(bpy.data.worlds),
            "scenes": len(bpy.data.scenes),
            "armatures": len(bpy.data.armatures),
            "actions": len(bpy.data.actions),
            "colliderObjects": len(collider_objects),
            "externalImages": len(external_images),
            "packedImages": len(packed_images),
            "externalLibraries": len(bpy.data.libraries),
            "embeddedTextBlocks": len(bpy.data.texts),
            "lodObjects": len(lod_objects),
        },
        "mesh": mesh_record,
        "bounds": bounds,
        "symmetry": symmetry,
        "graphContractSha256": canonical_sha(graph) if graph is not None else None,
        "graphNodeCount": len(graph.get("nodes", [])) if graph else 0,
        "graphEdgeCount": len(graph.get("edges", [])) if graph else 0,
        "cameras": cameras,
        "renderSettings": {
            "engine": scene.render.engine,
            "resolution": [scene.render.resolution_x, scene.render.resolution_y],
            "resolutionPercentage": scene.render.resolution_percentage,
            "fileFormat": scene.render.image_settings.file_format,
            "colorMode": scene.render.image_settings.color_mode,
            "colorDepth": scene.render.image_settings.color_depth,
        },
        "externalImages": external_images,
        "packedImages": packed_images,
        "colliderObjects": collider_objects,
        "forbiddenModelNames": forbidden_model_names,
        "lodObjects": lod_objects,
        "errors": errors,
    }
    print("C1BRW002_INSPECTION_JSON=" + json.dumps(payload, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    inspect()
