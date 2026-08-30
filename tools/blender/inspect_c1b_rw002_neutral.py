#!/usr/bin/env python3

import hashlib
import json
import math

import bmesh
import bpy
from mathutils import Vector
from mathutils.bvhtree import BVHTree
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


def percentile(values, fraction):
    values = sorted(values)
    if not values:
        return 0.0
    return values[min(len(values) - 1, int((len(values) - 1) * fraction))]


def geometry_gates(obj):
    """Geometry-level gates: deliberately independent of metaball tessellation correspondence."""
    world = [obj.matrix_world @ vertex.co for vertex in obj.data.vertices]
    components = mesh_components(obj.data)
    # Sampled left/right envelope: topology indices need not mirror for an implicit surface.
    envelope_deviations = []
    for band in range(100):
        points = [p for p in world if band / 100.0 <= p.z <= (band + 1) / 100.0]
        if points:
            envelope_deviations.append(abs(max(p.x for p in points) + min(p.x for p in points)))

    bm = bmesh.new(); bm.from_mesh(obj.data)
    shoulder_angles = []
    severe_edges = 0
    for edge in bm.edges:
        midpoint = obj.matrix_world @ ((edge.verts[0].co + edge.verts[1].co) * 0.5)
        if 0.50 <= midpoint.z <= 0.72 and 0.10 <= abs(midpoint.x) <= 0.25 and edge.is_manifold:
            angle = math.degrees(edge.calc_face_angle(0.0))
            shoulder_angles.append(angle)
            severe_edges += int(angle > 75.0)
    bm.free()

    # The smaller closed component is the round head. Roundness is radial spread around its centroid.
    adjacency = {v.index: set() for v in obj.data.vertices}
    for edge in obj.data.edges:
        a, b = edge.vertices; adjacency[a].add(b); adjacency[b].add(a)
    unseen = set(adjacency); groups = []
    while unseen:
        first = unseen.pop(); group = {first}; stack = [first]
        while stack:
            for neighbor in adjacency[stack.pop()]:
                if neighbor in unseen:
                    unseen.remove(neighbor); group.add(neighbor); stack.append(neighbor)
        groups.append(group)
    head = min(groups, key=len)
    hp = [world[index] for index in head]
    center = sum(hp, Vector()) / len(hp)
    radii = [(p - center).length for p in hp]
    mean_radius = sum(radii) / len(radii)
    radial_cv = math.sqrt(sum((r - mean_radius) ** 2 for r in radii) / len(radii)) / mean_radius
    head_min = Vector(tuple(min(p[i] for p in hp) for i in range(3)))
    head_max = Vector(tuple(max(p[i] for p in hp) for i in range(3)))
    body_group = max(groups, key=len)
    bp = [world[index] for index in body_group]
    body_min = Vector(tuple(min(p[i] for p in bp) for i in range(3)))
    body_max = Vector(tuple(max(p[i] for p in bp) for i in range(3)))
    overlap = [max(0.0, min(head_max[i], body_max[i]) - max(head_min[i], body_min[i])) for i in range(3)]
    def component_bvh(group):
        ordered = sorted(group); remap = {old: new for new, old in enumerate(ordered)}
        vertices = [world[index] for index in ordered]
        polygons = [tuple(remap[index] for index in polygon.vertices) for polygon in obj.data.polygons if all(index in group for index in polygon.vertices)]
        return BVHTree.FromPolygons(vertices, polygons, all_triangles=False)
    surface_intersections = component_bvh(head).overlap(component_bvh(body_group))
    contract_text = obj.get("construction_contract_json", "")
    try:
        contract = json.loads(contract_text)
    except (TypeError, json.JSONDecodeError):
        contract = None
    element_ids = [entry.get("id") for entry in (contract or {}).get("elements", [])]
    return {
        "constructionContract": contract,
        "constructionContractSha256": canonical_sha(contract) if contract else None,
        "semanticElementIds": element_ids,
        "neckSemanticElementCount": sum(1 for value in element_ids if isinstance(value, str) and "neck" in value.lower()),
        "sampledEnvelopeMaximumMirrorDeviationH": rounded(max(envelope_deviations) if envelope_deviations else math.inf),
        "sampledEnvelopeToleranceH": 0.003,
        "topologyMirrorMappingRequired": False,
        "shoulderDihedralP95Degrees": rounded(percentile(shoulder_angles, 0.95)),
        "shoulderDihedralMaximumDegrees": rounded(max(shoulder_angles) if shoulder_angles else 0.0),
        "shoulderSevereEdgeThresholdDegrees": 75.0,
        "shoulderSevereEdgeCount": severe_edges,
        "headRadialCoefficientOfVariation": rounded(radial_cv),
        "headRadialCoefficientMaximum": 0.12,
        "headBounds": {"minimum": vector_values(head_min), "maximum": vector_values(head_max)},
        "bodyBounds": {"minimum": vector_values(body_min), "maximum": vector_values(body_max)},
        "headBodyOverlapH": vector_values(overlap),
        "headBodyTriangleIntersectionPairs": len(surface_intersections),
        "minimumHeadBodyTriangleIntersectionPairs": 1,
        "minimumAttachmentOverlapH": 0.005,
        "componentVertexCounts": components,
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
        gates = geometry_gates(model)

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
        "geometryGates": gates if model is not None and model.type == "MESH" else {},
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
