#!/usr/bin/env python3

"""Generate and measure one global QuadriFlow r16 body candidate."""

import hashlib
import importlib.util
import json
import os
import sys
import time

import bpy
import numpy as np
from mathutils import Vector
from mathutils.bvhtree import BVHTree


HERE = os.path.dirname(os.path.abspath(__file__))
SOURCE_SHA256 = "9b80276e97aa84f3d2a4ef7689b4ebd241f84124494ec8ff0ca51cc119a676ef"
SOURCE_BODY = "C1B_R16_FullBodyCrotchFair7mm_TPoseBody_NoHands"


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r18 = import_file("c1b_rw018_for_quad_sweep", "create_c1b_rw018_rig.py")
r12 = r18.r12
qa = r18.qa


def sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def parse_args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <source.blend> <target-faces> <candidate.blend> <report.json>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 4:
        raise RuntimeError("expected source, target faces, candidate, report")
    source, target, candidate, report = values
    source = os.path.abspath(source)
    candidate = os.path.abspath(candidate)
    report = os.path.abspath(report)
    target = int(target)
    if os.path.realpath(source) == os.path.realpath(candidate):
        raise RuntimeError("candidate must not overwrite source")
    return source, target, candidate, report


def triangulated_polygons(mesh):
    result = []
    for polygon in mesh.polygons:
        vertices = tuple(polygon.vertices)
        for index in range(1, len(vertices) - 1):
            result.append((vertices[0], vertices[index], vertices[index + 1]))
    return result


def bvh_for_mesh(mesh):
    coordinates = [tuple(vertex.co) for vertex in mesh.vertices]
    return BVHTree.FromPolygons(coordinates, triangulated_polygons(mesh), all_triangles=True)


def distance_summary(points, tree):
    distances = np.empty(len(points), dtype=np.float64)
    missing = 0
    for index, point in enumerate(points):
        result = tree.find_nearest(Vector(tuple(float(value) for value in point)))
        if result is None:
            distances[index] = np.inf
            missing += 1
        else:
            distances[index] = float(result[3])
    finite = distances[np.isfinite(distances)]
    return {
        "count": len(points),
        "missing": missing,
        "maximumMeters": float(finite.max()) if len(finite) else None,
        "p99Meters": float(np.percentile(finite, 99.0)) if len(finite) else None,
        "p95Meters": float(np.percentile(finite, 95.0)) if len(finite) else None,
        "meanMeters": float(finite.mean()) if len(finite) else None,
    }


def bounds(coordinates):
    minimum = coordinates.min(axis=0)
    maximum = coordinates.max(axis=0)
    return {
        "minimum": [float(value) for value in minimum],
        "maximum": [float(value) for value in maximum],
        "size": [float(value) for value in maximum - minimum],
    }


def main():
    source_path, target_faces, candidate_path, report_path = parse_args()
    if sha256(source_path) != SOURCE_SHA256:
        raise RuntimeError("source hash mismatch")
    os.makedirs(os.path.dirname(candidate_path), exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)
    for path in (candidate_path, report_path):
        if os.path.isfile(path):
            os.remove(path)

    bpy.ops.wm.open_mainfile(filepath=source_path)
    source = bpy.data.objects[SOURCE_BODY]
    source_coordinates = r12.mesh_coordinates(source.data)
    source_bounds = bounds(source_coordinates)

    candidate = source.copy()
    candidate.data = source.data.copy()
    candidate.name = f"C1B_R19_QuadriFlow_{target_faces}"
    candidate.data.name = candidate.name + "Mesh"
    bpy.context.scene.collection.objects.link(candidate)
    for obj in bpy.context.selected_objects:
        obj.select_set(False)
    candidate.hide_set(False)
    candidate.hide_viewport = False
    candidate.select_set(True)
    bpy.context.view_layer.objects.active = candidate

    started = time.time()
    result = bpy.ops.object.quadriflow_remesh(
        mode="FACES",
        target_faces=target_faces,
        use_mesh_symmetry=True,
        use_preserve_sharp=False,
        use_preserve_boundary=False,
        preserve_attributes=False,
        smooth_normals=True,
        seed=17,
    )
    elapsed = time.time() - started
    if "FINISHED" not in result:
        raise RuntimeError(f"QuadriFlow failed: {result}")
    candidate.data.update(calc_edges=True)

    shrinkwrap = candidate.modifiers.new("TMP_R19_ProjectToR16", "SHRINKWRAP")
    shrinkwrap.target = source
    shrinkwrap.wrap_method = "NEAREST_SURFACEPOINT"
    shrinkwrap.wrap_mode = "ON_SURFACE"
    bpy.context.view_layer.objects.active = candidate
    apply_result = bpy.ops.object.modifier_apply(modifier=shrinkwrap.name)
    if "FINISHED" not in apply_result:
        raise RuntimeError(f"Shrinkwrap apply failed: {apply_result}")
    candidate.data.update(calc_edges=True)

    candidate_coordinates = r12.mesh_coordinates(candidate.data)
    source_tree = bvh_for_mesh(source.data)
    candidate_tree = bvh_for_mesh(candidate.data)
    candidate_to_source = distance_summary(candidate_coordinates, source_tree)
    source_to_candidate = distance_summary(source_coordinates, candidate_tree)
    topology = r12.r11.topology(candidate)
    manifold = qa.manifold(candidate.data)
    folds = qa.folds(candidate.data)
    overlap = qa.bvh_self_overlap(candidate.data)
    folds.pop("foldoverEdgesAt90Degrees", None)
    overlap.pop("nonAdjacentOverlapPairs", None)

    for obj in list(bpy.data.objects):
        if obj is not candidate:
            bpy.data.objects.remove(obj, do_unlink=True)
    for collection in list(bpy.data.collections):
        if collection.objects.get(candidate.name) is None:
            bpy.data.collections.remove(collection)
    bpy.ops.wm.save_as_mainfile(filepath=candidate_path, check_existing=False)

    report = {
        "source": source_path,
        "sourceSha256": SOURCE_SHA256,
        "targetFaces": target_faces,
        "quadriFlowElapsedSeconds": elapsed,
        "candidate": candidate_path,
        "candidateSha256": sha256(candidate_path),
        "topology": topology,
        "manifold": manifold,
        "fold": folds,
        "selfIntersection": overlap,
        "sourceBounds": source_bounds,
        "candidateBounds": bounds(candidate_coordinates),
        "candidateToSource": candidate_to_source,
        "sourceToCandidate": source_to_candidate,
    }
    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, indent=2)
        stream.write("\n")
    print("R19_QUAD_SWEEP=" + json.dumps(report, separators=(",", ":")))


if __name__ == "__main__":
    main()
