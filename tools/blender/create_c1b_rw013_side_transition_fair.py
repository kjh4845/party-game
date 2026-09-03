#!/usr/bin/env python3

"""Slightly fair only the r12 side-depth profile at the torso-to-leg join."""

import hashlib
import importlib.util
import json
import math
import os
import sys

import bpy
import numpy as np


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-012-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r12.blend",
)
SOURCE_BODY = "C1B_R12_TorsoLegFair_TPoseBody_NoHands"
SOURCE_HEAD = "C1B_R12_RoundFacelessHead"
BODY_NAME = "C1B_R13_SideTransitionFair_TPoseBody_NoHands"
HEAD_NAME = "C1B_R13_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
REVISION = "r13"
VERSION = "0.13.0-local-preview"
CONSTRUCTION = "R12_Y_ONLY_GAUSSIAN_SIDE_TRANSITION_FAIR"

PROFILE_STEP = 0.002
PROFILE_SIGMA_PRESENT = 0.003
PROFILE_SIGMA_FAIR = 0.012
FAIR_STRENGTH = 0.30
EDIT_START_Z = 0.1265
EDIT_FULL_Z = EDIT_START_Z + 0.035
EDIT_FADE_START_Z = 0.280
EDIT_END_Z = EDIT_FADE_START_Z + 0.060
EDIT_X_LIMIT = 0.205
MAXIMUM_Y_DELTA = 0.001
MAXIMUM_SECTION_CENTER_DELTA = 0.000002
SURFACE_SMOOTH_FACTOR = 0.10
SURFACE_SMOOTH_ITERATIONS = 15
SURFACE_X_FADE_START = 0.185
SIDE_WEIGHT_START = 0.65
SIDE_WEIGHT_FULL = 0.95

CURVATURE_START_Z = 0.140
CURVATURE_END_Z = 0.340
CURVATURE_SIGMA = 0.006
CURVATURE_THIRD_DERIVATIVE_RATIO = 0.97


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r12 = import_file("c1b_rw012", "create_c1b_rw012_torso_leg_fair.py")
qa = r12.qa


def args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <blend> <render-dir> <report>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 3:
        raise RuntimeError("expected -- <blend> <render-dir> <report>")
    return tuple(os.path.abspath(value) for value in values)


def file_sha256(path):
    digest = hashlib.sha256()
    with open(path, "rb") as stream:
        for chunk in iter(lambda: stream.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def profile_grid():
    count = int(round((0.560 - 0.100) / PROFILE_STEP)) + 1
    return np.linspace(0.100, 0.560, count, dtype=np.float64)


def gaussian_filter(values, sigma, step):
    sigma_steps = sigma / step
    radius = int(math.ceil(3.0 * sigma_steps))
    positions = np.arange(-radius, radius + 1, dtype=np.float64)
    kernel = np.exp(-0.5 * (positions / sigma_steps) ** 2)
    kernel /= kernel.sum()
    return np.convolve(np.pad(values, radius, mode="edge"), kernel, mode="valid")


def sample_signed_y(coordinates, edges, grid):
    first = coordinates[edges[:, 0]]
    second = coordinates[edges[:, 1]]
    delta_z = second[:, 2] - first[:, 2]
    non_horizontal = np.abs(delta_z) > 1.0e-12
    minimum_z = np.minimum(first[:, 2], second[:, 2])
    maximum_z = np.maximum(first[:, 2], second[:, 2])
    positive = np.full(len(grid), np.nan, dtype=np.float64)
    negative = np.full(len(grid), np.nan, dtype=np.float64)
    for index, height in enumerate(grid):
        crossing = non_horizontal & (minimum_z <= height) & (maximum_z >= height)
        if not np.any(crossing):
            continue
        t = (height - first[crossing, 2]) / delta_z[crossing]
        x = first[crossing, 0] + t * (second[crossing, 0] - first[crossing, 0])
        y = first[crossing, 1] + t * (second[crossing, 1] - first[crossing, 1])
        central = np.abs(x) <= EDIT_X_LIMIT
        if np.any(central):
            positive[index] = float(y[central].max())
            negative[index] = float(y[central].min())
    return (
        r12.fill_profile_gaps(grid, positive),
        r12.fill_profile_gaps(grid, negative),
    )


def build_target_profile(positive, negative, grid):
    center = (positive + negative) * 0.5
    half_depth = (positive - negative) * 0.5
    present = gaussian_filter(half_depth, PROFILE_SIGMA_PRESENT, PROFILE_STEP)
    fair = gaussian_filter(present, PROFILE_SIGMA_FAIR, PROFILE_STEP)
    window = r12.smootherstep((grid - EDIT_START_Z) / (EDIT_FULL_Z - EDIT_START_Z)) * (
        1.0
        - r12.smootherstep(
            (grid - EDIT_FADE_START_Z) / (EDIT_END_Z - EDIT_FADE_START_Z)
        )
    )
    target = present + FAIR_STRENGTH * window * (fair - present)
    return center, present, target, window


def apply_y_only_fair(source, grid, center, present, target):
    result = source.copy()
    z = source[:, 2]
    absolute_x = np.abs(source[:, 0])
    allowed = (
        (z > EDIT_START_Z)
        & (z < EDIT_END_Z)
        & (absolute_x < EDIT_X_LIMIT)
    )
    local_center = np.interp(z[allowed], grid, center)
    local_present = np.interp(z[allowed], grid, present)
    local_target = np.interp(z[allowed], grid, target)
    profile_target_y = local_center + (
        source[allowed, 1] - local_center
    ) * (local_target / local_present)
    normalized_side = np.abs(source[allowed, 1] - local_center) / local_present
    side_weight = r12.smootherstep(
        (normalized_side - SIDE_WEIGHT_START)
        / (SIDE_WEIGHT_FULL - SIDE_WEIGHT_START)
    )
    result[allowed, 1] = source[allowed, 1] + side_weight * (
        profile_target_y - source[allowed, 1]
    )
    return result, allowed


def smooth_allowed_y_surface(result, source, edges, allowed, grid, center, present):
    z = source[:, 2]
    absolute_x = np.abs(source[:, 0])
    profile_center = np.interp(z, grid, center)
    profile_present = np.interp(z, grid, present)
    normalized_side = np.abs(source[:, 1] - profile_center) / profile_present
    side_weight = r12.smootherstep(
        (normalized_side - SIDE_WEIGHT_START)
        / (SIDE_WEIGHT_FULL - SIDE_WEIGHT_START)
    )
    weight = r12.smootherstep(
        (z - EDIT_START_Z) / (EDIT_FULL_Z - EDIT_START_Z)
    ) * (
        1.0
        - r12.smootherstep(
            (z - EDIT_FADE_START_Z) / (EDIT_END_Z - EDIT_FADE_START_Z)
        )
    ) * (
        1.0
        - r12.smootherstep(
            (absolute_x - SURFACE_X_FADE_START)
            / (EDIT_X_LIMIT - SURFACE_X_FADE_START)
        )
    ) * side_weight
    degree = np.bincount(edges.reshape(-1), minlength=len(source)).astype(np.float64)
    y = result[:, 1].copy()
    for _ in range(SURFACE_SMOOTH_ITERATIONS):
        average = r12.neighbor_average(y, edges, degree)
        y[allowed] += SURFACE_SMOOTH_FACTOR * weight[allowed] * (
            average[allowed] - y[allowed]
        )
        y[~allowed] = source[~allowed, 1]
        y = source[:, 1] + np.clip(
            y - source[:, 1], -MAXIMUM_Y_DELTA, MAXIMUM_Y_DELTA
        )
    result[:, 1] = y
    return result


def curvature_metrics(grid, positive, negative):
    mask = (grid >= CURVATURE_START_Z) & (grid <= CURVATURE_END_Z)
    local_grid = grid[mask]
    result = {}
    for name, values in (("front", positive), ("back", -negative)):
        smooth = gaussian_filter(values, CURVATURE_SIGMA, PROFILE_STEP)[mask]
        first = np.gradient(smooth, PROFILE_STEP)
        second = np.gradient(first, PROFILE_STEP)
        third = np.gradient(second, PROFILE_STEP)
        curvature = np.abs(second) / np.power(1.0 + first * first, 1.5)
        signs = np.sign(second)
        nonzero = signs[signs != 0.0]
        sign_changes = int(np.count_nonzero(nonzero[1:] != nonzero[:-1]))
        maximum_index = int(np.argmax(curvature))
        result[name] = {
            "sampleCount": int(len(smooth)),
            "secondDerivativeRms": float(np.sqrt(np.mean(second * second))),
            "secondDerivativeMaximum": float(np.max(np.abs(second))),
            "thirdDerivativeRms": float(np.sqrt(np.mean(third * third))),
            "curvatureRms": float(np.sqrt(np.mean(curvature * curvature))),
            "curvatureMaximum": float(curvature[maximum_index]),
            "curvatureMaximumZ": float(local_grid[maximum_index]),
            "curvatureSignChanges": sign_changes,
        }
    return result


def surface_bilaplacian_metrics(coordinates, edges):
    degree = np.bincount(edges.reshape(-1), minlength=len(coordinates)).astype(np.float64)
    y = coordinates[:, 1]
    laplacian = y - r12.neighbor_average(y, edges, degree)
    bilaplacian = laplacian - r12.neighbor_average(laplacian, edges, degree)
    mask = (
        (coordinates[:, 2] >= CURVATURE_START_Z)
        & (coordinates[:, 2] <= CURVATURE_END_Z)
        & (np.abs(coordinates[:, 0]) <= 0.120)
        & (np.abs(coordinates[:, 1]) >= 0.055)
    )
    values = np.abs(bilaplacian[mask])
    return {
        "sampleCount": int(len(values)),
        "rms": float(np.sqrt(np.mean(values * values))),
        "p99": float(np.percentile(values, 99.0)),
        "maximum": float(values.max()),
    }


def object_bounds(obj):
    return r12.r11.object_bounds(obj)


def main():
    blend_path, render_dir, report_path = args()
    if not os.path.exists(SOURCE_BLEND):
        raise FileNotFoundError(SOURCE_BLEND)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)

    source_sha_before = file_sha256(SOURCE_BLEND)
    source_size_bytes = os.path.getsize(SOURCE_BLEND)
    bpy.ops.wm.open_mainfile(filepath=SOURCE_BLEND)
    body = bpy.data.objects[SOURCE_BODY]
    head = bpy.data.objects[SOURCE_HEAD]
    source_signature = r12.mesh_signature(body)
    source_coordinates = r12.mesh_coordinates(body.data)
    source_head_coordinates = r12.mesh_coordinates(head.data)
    source_head_matrix = np.array(head.matrix_world, dtype=np.float64)
    source_head_overlap = float(object_bounds(body)[1].z - object_bounds(head)[0].z)
    source_volume = qa.manifold(body.data)["signedVolume"]
    edges = r12.mesh_edges(body.data)

    grid = profile_grid()
    source_positive, source_negative = sample_signed_y(source_coordinates, edges, grid)
    center, present, target, window = build_target_profile(
        source_positive, source_negative, grid
    )
    final_coordinates, allowed_y = apply_y_only_fair(
        source_coordinates, grid, center, present, target
    )
    final_coordinates = smooth_allowed_y_surface(
        final_coordinates,
        source_coordinates,
        edges,
        allowed_y,
        grid,
        center,
        present,
    )
    r12.set_mesh_coordinates(body.data, final_coordinates)
    final_coordinates = r12.mesh_coordinates(body.data)

    final_positive, final_negative = sample_signed_y(final_coordinates, edges, grid)
    source_curvature = curvature_metrics(grid, source_positive, source_negative)
    final_curvature = curvature_metrics(grid, final_positive, final_negative)
    source_surface = surface_bilaplacian_metrics(source_coordinates, edges)
    final_surface = surface_bilaplacian_metrics(final_coordinates, edges)
    source_center = (source_positive + source_negative) * 0.5
    final_center = (final_positive + final_negative) * 0.5

    delta = final_coordinates - source_coordinates
    delta_y = np.abs(delta[:, 1])
    frozen_y = ~allowed_y
    arm_core = np.abs(source_coordinates[:, 0]) >= EDIT_X_LIMIT
    lower_core = source_coordinates[:, 2] <= EDIT_START_Z
    head_coordinate_delta = r12.maximum_delta(
        source_head_coordinates, r12.mesh_coordinates(head.data)
    )
    head_matrix_delta = float(
        np.abs(np.array(head.matrix_world, dtype=np.float64) - source_head_matrix).max()
    )

    body.name = BODY_NAME
    body.data.name = BODY_NAME + "Mesh"
    head.name = HEAD_NAME
    head.data.name = HEAD_NAME + "Mesh"
    target_signature = r12.mesh_signature(body)
    topology = r12.r11.topology(body)
    manifold = qa.manifold(body.data)
    mirror = qa.mirror(body.data)
    folds = qa.folds(body.data)
    fold_diagnostic = r12.maximum_adjacent_angle_diagnostic(body.data)
    overlap = qa.bvh_self_overlap(body.data)
    folds.pop("foldoverEdgesAt90Degrees", None)
    overlap.pop("nonAdjacentOverlapPairs", None)
    target_volume = manifold["signedVolume"]
    volume_relative_delta = abs(target_volume - source_volume) / abs(source_volume)
    head_overlap = float(object_bounds(body)[1].z - object_bounds(head)[0].z)
    runtime_modifier_count = sum(
        len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
    )

    r12.REVISION = REVISION
    outputs = r12.render(bpy.context.scene, render_dir)
    missing_renders = [
        name for name in outputs if not os.path.isfile(os.path.join(render_dir, name))
    ]
    source_sha_after = file_sha256(SOURCE_BLEND)

    curvature_pass = all(
        final_curvature[side]["secondDerivativeRms"]
        <= source_curvature[side]["secondDerivativeRms"]
        and final_curvature[side]["secondDerivativeMaximum"]
        <= source_curvature[side]["secondDerivativeMaximum"]
        and final_curvature[side]["thirdDerivativeRms"]
        <= source_curvature[side]["thirdDerivativeRms"]
        * CURVATURE_THIRD_DERIVATIVE_RATIO
        and final_curvature[side]["curvatureSignChanges"]
        <= source_curvature[side]["curvatureSignChanges"]
        for side in ("front", "back")
    )
    technical_pass = (
        source_sha_before == source_sha_after
        and source_signature == target_signature
        and np.array_equal(final_coordinates[:, 0], source_coordinates[:, 0])
        and np.array_equal(final_coordinates[:, 2], source_coordinates[:, 2])
        and np.array_equal(final_coordinates[frozen_y, 1], source_coordinates[frozen_y, 1])
        and float(delta_y.max()) <= MAXIMUM_Y_DELTA
        and r12.maximum_delta(source_coordinates, final_coordinates, arm_core) == 0.0
        and r12.maximum_delta(source_coordinates, final_coordinates, lower_core) == 0.0
        and head_coordinate_delta == 0.0
        and head_matrix_delta == 0.0
        and float(np.max(np.abs(final_center - source_center)))
        <= MAXIMUM_SECTION_CENTER_DELTA
        and curvature_pass
        and final_surface["rms"] < source_surface["rms"]
        and volume_relative_delta <= 0.005
        and topology["allQuads"]
        and topology["boundaryEdges"] == 0
        and topology["nonManifoldEdges"] == 0
        and topology["looseEdges"] == 0
        and topology["degenerateFaces"] == 0
        and topology["components"] == 1
        and topology["eulerCharacteristic"] == 2
        and manifold["result"] == "PASS"
        and mirror["result"] == "PASS"
        and folds["foldoverEdgeCountAt90Degrees"] == 0
        and folds["hardEdgeCountAt45Degrees"] == 0
        and folds["adjacentAngleMaximumDegrees"] <= 6.6016177432119205
        and overlap["result"] == "PASS"
        and abs(head_overlap - source_head_overlap) <= 1.0e-12
        and runtime_modifier_count == 0
        and not missing_renders
    )

    report = {
        "assetId": ASSET_ID,
        "revision": REVISION,
        "assetVersion": VERSION,
        "sourceRevision": "r12",
        "sourceIdentity": {
            "path": SOURCE_BLEND,
            "sha256Before": source_sha_before,
            "sha256After": source_sha_after,
            "sizeBytes": source_size_bytes,
            "unchanged": source_sha_before == source_sha_after,
        },
        "construction": CONSTRUCTION,
        "process": {
            "profileStep": PROFILE_STEP,
            "presentSigma": PROFILE_SIGMA_PRESENT,
            "fairSigma": PROFILE_SIGMA_FAIR,
            "fairStrength": FAIR_STRENGTH,
            "surfaceSmoothFactor": SURFACE_SMOOTH_FACTOR,
            "surfaceSmoothIterations": SURFACE_SMOOTH_ITERATIONS,
            "surfaceXFadeStart": SURFACE_X_FADE_START,
            "sideWeightStart": SIDE_WEIGHT_START,
            "sideWeightFull": SIDE_WEIGHT_FULL,
            "maximumSectionCenterDelta": MAXIMUM_SECTION_CENTER_DELTA,
            "editStartZ": EDIT_START_Z,
            "editFullZ": EDIT_FULL_Z,
            "editFadeStartZ": EDIT_FADE_START_Z,
            "editEndZ": EDIT_END_Z,
            "editXLimit": EDIT_X_LIMIT,
            "windowMaximum": float(window.max()),
            "topologyChangingOperations": 0,
        },
        "edit": {
            "allowedVertexCount": int(allowed_y.sum()),
            "frozenVertexCount": int(frozen_y.sum()),
            "modifiedVertexCount": int((delta_y > 0.0).sum()),
            "maximumAbsXDelta": float(np.abs(delta[:, 0]).max()),
            "maximumAbsYDelta": float(delta_y.max()),
            "p95AbsYDelta": float(np.percentile(delta_y[allowed_y], 95.0)),
            "maximumAbsZDelta": float(np.abs(delta[:, 2]).max()),
            "frozenMaximumAbsYDelta": float(delta_y[frozen_y].max()),
            "maximumSectionCenterDelta": float(
                np.max(np.abs(final_center - source_center))
            ),
            "volumeRelativeDelta": volume_relative_delta,
        },
        "preservation": {
            "armCoreMaximumDelta": r12.maximum_delta(
                source_coordinates, final_coordinates, arm_core
            ),
            "lowerBodyCoreMaximumDelta": r12.maximum_delta(
                source_coordinates, final_coordinates, lower_core
            ),
            "headCoordinateMaximumDelta": head_coordinate_delta,
            "headMatrixMaximumDelta": head_matrix_delta,
        },
        "sideCurvature": {
            "source": source_curvature,
            "final": final_curvature,
        },
        "sideSurfaceBilaplacian": {
            "source": source_surface,
            "final": final_surface,
        },
        "sourceTopology": source_signature,
        "targetTopology": target_signature,
        "topology": topology,
        "manifold": manifold,
        "mirror": mirror,
        "fold": folds,
        "foldMaximumDiagnostic": fold_diagnostic,
        "bvhSelfIntersection": overlap,
        "headBodyOverlap": head_overlap,
        "runtimeModifierCount": runtime_modifier_count,
        "renderFiles": outputs,
        "missingRenderFiles": missing_renders,
        "userVisualApprovalRecorded": False,
        "productionTopologyApproved": False,
        "technicalResult": "PASS" if technical_pass else "FAIL",
    }
    if not technical_pass:
        raise RuntimeError(
            "r13 side transition QA failed: "
            + json.dumps(report, separators=(",", ":"))
        )

    with open(report_path, "w", encoding="utf-8") as stream:
        json.dump(report, stream, ensure_ascii=False, indent=2)
        stream.write("\n")

    for obj in (body, head):
        obj["asset_id"] = ASSET_ID
        obj["asset_version"] = VERSION
        obj["source_owner"] = "kjh4845"
    body["construction"] = CONSTRUCTION
    body["user_visual_approval_recorded"] = False
    scene = bpy.context.scene
    scene["asset_id"] = ASSET_ID
    scene["asset_version"] = VERSION
    scene["construction"] = CONSTRUCTION
    scene["candidate_status"] = "LOCAL_USER_REVIEW"
    scene["source_revision"] = "r12"
    scene["user_visual_approval_recorded"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R13_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R13_GENERATION_RESULT=" + report["technicalResult"])


if __name__ == "__main__":
    main()
