#!/usr/bin/env python3

"""Recess only the r13 central crotch front/back surfaces by up to 3 mm."""

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
    "C1B-RW-013-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r13.blend",
)
SOURCE_BODY = "C1B_R13_SideTransitionFair_TPoseBody_NoHands"
SOURCE_HEAD = "C1B_R13_RoundFacelessHead"
BODY_NAME = "C1B_R14_CrotchRecess_TPoseBody_NoHands"
HEAD_NAME = "C1B_R14_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
REVISION = "r14"
VERSION = "0.14.0-local-preview"
CONSTRUCTION = "R13_CENTRAL_CROTCH_FRONT_BACK_3MM_C2_RECESS"

RECESS_DISTANCE = 0.003
Z_SUPPORT_START = 0.1265
Z_PEAK = 0.180
Z_CORE_START = 0.155
Z_CORE_END = 0.220
Z_SUPPORT_END = 0.260
X_CORE = 0.030
X_SUPPORT = 0.090
SIDE_WEIGHT_START = 0.65
SIDE_WEIGHT_FULL = 0.95
PROFILE_STEP = 0.002
PROFILE_SIGMA = 0.006
PROFILE_QA_START = 0.135
PROFILE_QA_END = 0.240
MAXIMUM_SECTION_CENTER_DELTA = 0.000002
MAXIMUM_VOLUME_RELATIVE_DELTA = 0.005


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r13 = import_file("c1b_rw013", "create_c1b_rw013_side_transition_fair.py")
r12 = r13.r12
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
    return np.arange(0.100, 0.561, PROFILE_STEP, dtype=np.float64)


def sample_signed_y(coordinates, edges, grid, x_limit):
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
        selected = np.abs(x) <= x_limit
        if np.any(selected):
            positive[index] = float(y[selected].max())
            negative[index] = float(y[selected].min())
    return (
        r12.fill_profile_gaps(grid, positive),
        r12.fill_profile_gaps(grid, negative),
    )


def build_recess_weights(source, grid, section_center, half_depth):
    z = source[:, 2]
    absolute_x = np.abs(source[:, 0])
    local_center = np.interp(z, grid, section_center)
    local_half_depth = np.interp(z, grid, half_depth)
    normalized_side = np.abs(source[:, 1] - local_center) / local_half_depth
    z_weight = r12.smootherstep(
        (z - Z_SUPPORT_START) / (Z_PEAK - Z_SUPPORT_START)
    ) * (
        1.0
        - r12.smootherstep(
            (z - Z_PEAK) / (Z_SUPPORT_END - Z_PEAK)
        )
    )
    x_weight = 1.0 - r12.smootherstep(
        (absolute_x - X_CORE) / (X_SUPPORT - X_CORE)
    )
    side_weight = r12.smootherstep(
        (normalized_side - SIDE_WEIGHT_START)
        / (SIDE_WEIGHT_FULL - SIDE_WEIGHT_START)
    )
    return z_weight * x_weight * side_weight, local_center


def apply_recess(source, weight, local_center):
    result = source.copy()
    centered = source[:, 1] - local_center
    direction = np.sign(centered)
    result[:, 1] = source[:, 1] - direction * RECESS_DISTANCE * weight
    crossed = centered * (result[:, 1] - local_center) < 0.0
    if np.any(crossed):
        raise RuntimeError("crotch recess crossed the section center")
    return result


def gaussian_filter(values, sigma, step):
    sigma_steps = sigma / step
    radius = int(math.ceil(3.0 * sigma_steps))
    positions = np.arange(-radius, radius + 1, dtype=np.float64)
    kernel = np.exp(-0.5 * (positions / sigma_steps) ** 2)
    kernel /= kernel.sum()
    return np.convolve(np.pad(values, radius, mode="edge"), kernel, mode="valid")


def profile_fairness(grid, positive, negative):
    mask = (grid >= PROFILE_QA_START) & (grid <= PROFILE_QA_END)
    result = {}
    for name, values in (("back", positive), ("front", -negative)):
        smooth = gaussian_filter(values, PROFILE_SIGMA, PROFILE_STEP)[mask]
        first = np.gradient(smooth, PROFILE_STEP)
        second = np.gradient(first, PROFILE_STEP)
        third = np.gradient(second, PROFILE_STEP)
        signs = np.sign(second)
        nonzero = signs[signs != 0.0]
        result[name] = {
            "sampleCount": int(len(smooth)),
            "secondDerivativeRms": float(np.sqrt(np.mean(second * second))),
            "thirdDerivativeRms": float(np.sqrt(np.mean(third * third))),
            "curvatureSignChanges": int(
                np.count_nonzero(nonzero[1:] != nonzero[:-1])
            ),
        }
    return result


def boundary_bilaplacian(coordinates, edges):
    degree = np.bincount(edges.reshape(-1), minlength=len(coordinates)).astype(np.float64)
    y = coordinates[:, 1]
    laplacian = y - r12.neighbor_average(y, edges, degree)
    bilaplacian = laplacian - r12.neighbor_average(laplacian, edges, degree)
    z = coordinates[:, 2]
    absolute_x = np.abs(coordinates[:, 0])
    boundary = (
        (
            ((z >= Z_SUPPORT_START) & (z <= Z_SUPPORT_START + 0.010))
            | ((z >= Z_SUPPORT_END - 0.010) & (z <= Z_SUPPORT_END))
            | ((absolute_x >= X_SUPPORT - 0.010) & (absolute_x <= X_SUPPORT))
        )
        & (z >= Z_SUPPORT_START)
        & (z <= Z_SUPPORT_END)
        & (absolute_x <= X_SUPPORT)
    )
    values = np.abs(bilaplacian[boundary])
    return {
        "sampleCount": int(len(values)),
        "rms": float(np.sqrt(np.mean(values * values))),
        "p99": float(np.percentile(values, 99.0)),
        "maximum": float(values.max()),
    }


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
    source_head_overlap = float(
        r12.r11.object_bounds(body)[1].z - r12.r11.object_bounds(head)[0].z
    )
    source_volume = qa.manifold(body.data)["signedVolume"]
    edges = r12.mesh_edges(body.data)
    grid = profile_grid()
    source_positive, source_negative = sample_signed_y(
        source_coordinates, edges, grid, X_SUPPORT
    )
    section_center = (source_positive + source_negative) * 0.5
    half_depth = (source_positive - source_negative) * 0.5
    weight, local_center = build_recess_weights(
        source_coordinates, grid, section_center, half_depth
    )
    final_coordinates = apply_recess(source_coordinates, weight, local_center)
    r12.set_mesh_coordinates(body.data, final_coordinates)
    final_coordinates = r12.mesh_coordinates(body.data)
    delta = final_coordinates - source_coordinates
    displacement_y = delta[:, 1]
    absolute_delta_y = np.abs(displacement_y)
    support = (
        (source_coordinates[:, 2] > Z_SUPPORT_START)
        & (source_coordinates[:, 2] < Z_SUPPORT_END)
        & (np.abs(source_coordinates[:, 0]) < X_SUPPORT)
    )
    modified = absolute_delta_y > 0.0

    final_positive, final_negative = sample_signed_y(
        final_coordinates, edges, grid, X_SUPPORT
    )
    source_core_positive, source_core_negative = sample_signed_y(
        source_coordinates, edges, grid, X_CORE
    )
    final_core_positive, final_core_negative = sample_signed_y(
        final_coordinates, edges, grid, X_CORE
    )
    core_samples = (grid >= Z_CORE_START) & (grid <= Z_CORE_END)
    back_reduction = source_core_positive - final_core_positive
    front_reduction = final_core_negative - source_core_negative
    source_profile_fairness = profile_fairness(
        grid, source_core_positive, source_core_negative
    )
    final_profile_fairness = profile_fairness(
        grid, final_core_positive, final_core_negative
    )
    source_boundary_bilaplacian = boundary_bilaplacian(source_coordinates, edges)
    final_boundary_bilaplacian = boundary_bilaplacian(final_coordinates, edges)
    source_center = (source_positive + source_negative) * 0.5
    final_center = (final_positive + final_negative) * 0.5

    linked = modified[edges[:, 0]] | modified[edges[:, 1]]
    edge_displacement_difference = np.abs(
        displacement_y[edges[linked, 0]] - displacement_y[edges[linked, 1]]
    )
    toward_center = (
        (source_coordinates[:, 1] - local_center) * displacement_y
    ) <= 1.0e-12
    no_center_crossing = (
        (source_coordinates[:, 1] - local_center)
        * (final_coordinates[:, 1] - local_center)
    ) >= -1.0e-12

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
    head_overlap = float(
        r12.r11.object_bounds(body)[1].z - r12.r11.object_bounds(head)[0].z
    )
    head_coordinate_delta = r12.maximum_delta(
        source_head_coordinates, r12.mesh_coordinates(head.data)
    )
    head_matrix_delta = float(
        np.abs(np.array(head.matrix_world, dtype=np.float64) - source_head_matrix).max()
    )
    runtime_modifier_count = sum(
        len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
    )

    r13.r12.REVISION = REVISION
    outputs = r13.r12.render(bpy.context.scene, render_dir)
    missing_renders = [
        name for name in outputs if not os.path.isfile(os.path.join(render_dir, name))
    ]
    source_sha_after = file_sha256(SOURCE_BLEND)

    minimum_core_reduction = float(
        min(back_reduction[core_samples].min(), front_reduction[core_samples].min())
    )
    maximum_core_reduction = float(
        max(back_reduction[core_samples].max(), front_reduction[core_samples].max())
    )
    front_back_reduction_difference = float(
        np.max(np.abs(back_reduction[core_samples] - front_reduction[core_samples]))
    )
    profile_fairness_pass = all(
        final_profile_fairness[side]["thirdDerivativeRms"]
        <= source_profile_fairness[side]["thirdDerivativeRms"] * 1.05
        and final_profile_fairness[side]["curvatureSignChanges"]
        <= source_profile_fairness[side]["curvatureSignChanges"]
        for side in ("front", "back")
    )
    technical_pass = (
        source_sha_before == source_sha_after
        and source_signature == target_signature
        and np.array_equal(final_coordinates[:, 0], source_coordinates[:, 0])
        and np.array_equal(final_coordinates[:, 2], source_coordinates[:, 2])
        and np.array_equal(final_coordinates[~support, 1], source_coordinates[~support, 1])
        and float(absolute_delta_y.max()) <= RECESS_DISTANCE + 1.0e-6
        and np.all(toward_center[modified])
        and np.all(no_center_crossing[modified])
        and minimum_core_reduction >= -1.0e-6
        and maximum_core_reduction >= RECESS_DISTANCE - 2.0e-5
        and front_back_reduction_difference <= 3.0e-5
        and float(np.max(np.abs(final_center - source_center)))
        <= MAXIMUM_SECTION_CENTER_DELTA
        and profile_fairness_pass
        and final_boundary_bilaplacian["rms"]
        <= source_boundary_bilaplacian["rms"] * 1.05
        and final_boundary_bilaplacian["p99"]
        <= source_boundary_bilaplacian["p99"] * 1.05
        and volume_relative_delta <= MAXIMUM_VOLUME_RELATIVE_DELTA
        and head_coordinate_delta == 0.0
        and head_matrix_delta == 0.0
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
        "sourceRevision": "r13",
        "sourceIdentity": {
            "path": SOURCE_BLEND,
            "sha256Before": source_sha_before,
            "sha256After": source_sha_after,
            "sizeBytes": source_size_bytes,
            "unchanged": source_sha_before == source_sha_after,
        },
        "construction": CONSTRUCTION,
        "process": {
            "recessDistance": RECESS_DISTANCE,
            "zSupport": [Z_SUPPORT_START, Z_SUPPORT_END],
            "zPeak": Z_PEAK,
            "qaCore": [Z_CORE_START, Z_CORE_END],
            "xCore": X_CORE,
            "xSupport": X_SUPPORT,
            "sideWeightStart": SIDE_WEIGHT_START,
            "sideWeightFull": SIDE_WEIGHT_FULL,
            "topologyChangingOperations": 0,
        },
        "edit": {
            "supportVertexCount": int(support.sum()),
            "modifiedVertexCount": int(modified.sum()),
            "maximumAbsXDelta": float(np.abs(delta[:, 0]).max()),
            "maximumAbsYDelta": float(absolute_delta_y.max()),
            "maximumAbsZDelta": float(np.abs(delta[:, 2]).max()),
            "frozenMaximumAbsYDelta": float(np.abs(delta[~support, 1]).max()),
            "maximumSectionCenterDelta": float(
                np.max(np.abs(final_center - source_center))
            ),
            "minimumCoreReduction": minimum_core_reduction,
            "maximumCoreReduction": maximum_core_reduction,
            "frontBackReductionMaximumDifference": front_back_reduction_difference,
            "edgeDisplacementDifferenceMaximum": float(
                edge_displacement_difference.max()
            ),
            "edgeDisplacementDifferenceP99": float(
                np.percentile(edge_displacement_difference, 99.0)
            ),
            "volumeRelativeDelta": volume_relative_delta,
        },
        "profileFairness": {
            "source": source_profile_fairness,
            "final": final_profile_fairness,
        },
        "boundaryBilaplacian": {
            "source": source_boundary_bilaplacian,
            "final": final_boundary_bilaplacian,
        },
        "preservation": {
            "headCoordinateMaximumDelta": head_coordinate_delta,
            "headMatrixMaximumDelta": head_matrix_delta,
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
            "r14 crotch recess QA failed: "
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
    scene["source_revision"] = "r13"
    scene["user_visual_approval_recorded"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R14_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R14_GENERATION_RESULT=" + report["technicalResult"])


if __name__ == "__main__":
    main()
