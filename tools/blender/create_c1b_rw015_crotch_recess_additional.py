#!/usr/bin/env python3

"""Apply an additional 4 mm fair C2 crotch recess to the approved r14 support."""

import hashlib
import importlib.util
import json
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
    "C1B-RW-014-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r14.blend",
)
SOURCE_BODY = "C1B_R14_CrotchRecess_TPoseBody_NoHands"
SOURCE_HEAD = "C1B_R14_RoundFacelessHead"
BODY_NAME = "C1B_R15_CrotchRecess7mm_TPoseBody_NoHands"
HEAD_NAME = "C1B_R15_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
REVISION = "r15"
VERSION = "0.15.0-local-preview"
CONSTRUCTION = "R14_SAME_CROTCH_SUPPORT_ADDITIONAL_4MM_FAIR_C2_RECESS"
ADDITIONAL_RECESS_DISTANCE = 0.004
CUMULATIVE_NOMINAL_RECESS = 0.007
MAXIMUM_SECTION_CENTER_DELTA = 0.000003


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r14 = import_file("c1b_rw014", "create_c1b_rw014_crotch_recess.py")
r13 = r14.r13
r12 = r14.r12
qa = r14.qa


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


def lower_ramp_c2_fair(values):
    """C2 ramp with reduced low-boundary curvature and the same 0/1 endpoints."""
    t = np.clip(values, 0.0, 1.0)
    return 21.0 * t**5 - 35.0 * t**6 + 15.0 * t**7


def build_additional_recess_weights(source, grid, section_center, half_depth):
    z = source[:, 2]
    absolute_x = np.abs(source[:, 0])
    local_center = np.interp(z, grid, section_center)
    local_half_depth = np.interp(z, grid, half_depth)
    normalized_side = np.abs(source[:, 1] - local_center) / local_half_depth
    lower_weight = lower_ramp_c2_fair(
        (z - r14.Z_SUPPORT_START) / (r14.Z_PEAK - r14.Z_SUPPORT_START)
    )
    upper_weight = 1.0 - r12.smootherstep(
        (z - r14.Z_PEAK) / (r14.Z_SUPPORT_END - r14.Z_PEAK)
    )
    x_weight = 1.0 - r12.smootherstep(
        (absolute_x - r14.X_CORE) / (r14.X_SUPPORT - r14.X_CORE)
    )
    side_weight = r12.smootherstep(
        (normalized_side - r14.SIDE_WEIGHT_START)
        / (r14.SIDE_WEIGHT_FULL - r14.SIDE_WEIGHT_START)
    )
    return lower_weight * upper_weight * x_weight * side_weight, local_center


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
    grid = r14.profile_grid()
    source_positive, source_negative = r14.sample_signed_y(
        source_coordinates, edges, grid, r14.X_SUPPORT
    )
    section_center = (source_positive + source_negative) * 0.5
    half_depth = (source_positive - source_negative) * 0.5
    weight, local_center = build_additional_recess_weights(
        source_coordinates, grid, section_center, half_depth
    )
    original_distance = r14.RECESS_DISTANCE
    r14.RECESS_DISTANCE = ADDITIONAL_RECESS_DISTANCE
    final_coordinates = r14.apply_recess(
        source_coordinates, weight, local_center
    )
    r14.RECESS_DISTANCE = original_distance
    r12.set_mesh_coordinates(body.data, final_coordinates)
    final_coordinates = r12.mesh_coordinates(body.data)
    delta = final_coordinates - source_coordinates
    displacement_y = delta[:, 1]
    absolute_delta_y = np.abs(displacement_y)
    support = (
        (source_coordinates[:, 2] > r14.Z_SUPPORT_START)
        & (source_coordinates[:, 2] < r14.Z_SUPPORT_END)
        & (np.abs(source_coordinates[:, 0]) < r14.X_SUPPORT)
    )
    modified = absolute_delta_y > 0.0

    final_positive, final_negative = r14.sample_signed_y(
        final_coordinates, edges, grid, r14.X_SUPPORT
    )
    source_core_positive, source_core_negative = r14.sample_signed_y(
        source_coordinates, edges, grid, r14.X_CORE
    )
    final_core_positive, final_core_negative = r14.sample_signed_y(
        final_coordinates, edges, grid, r14.X_CORE
    )
    core_samples = (grid >= r14.Z_CORE_START) & (grid <= r14.Z_CORE_END)
    back_reduction = source_core_positive - final_core_positive
    front_reduction = final_core_negative - source_core_negative
    source_profile_fairness = r14.profile_fairness(
        grid, source_core_positive, source_core_negative
    )
    final_profile_fairness = r14.profile_fairness(
        grid, final_core_positive, final_core_negative
    )
    source_boundary_bilaplacian = r14.boundary_bilaplacian(
        source_coordinates, edges
    )
    final_boundary_bilaplacian = r14.boundary_bilaplacian(
        final_coordinates, edges
    )
    source_center = (source_positive + source_negative) * 0.5
    final_center = (final_positive + final_negative) * 0.5

    centered = source_coordinates[:, 1] - local_center
    toward_center = centered * displacement_y <= 1.0e-12
    no_center_crossing = (
        centered * (final_coordinates[:, 1] - local_center)
    ) >= -1.0e-12
    minimum_remaining_to_center = float(
        np.min(np.abs(final_coordinates[modified, 1] - local_center[modified]))
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
        and float(absolute_delta_y.max())
        <= ADDITIONAL_RECESS_DISTANCE + 1.0e-6
        and np.all(toward_center[modified])
        and np.all(no_center_crossing[modified])
        and minimum_remaining_to_center > 0.0
        and minimum_core_reduction >= -1.0e-6
        and maximum_core_reduction
        >= ADDITIONAL_RECESS_DISTANCE - 2.0e-5
        and front_back_reduction_difference <= 4.0e-5
        and float(np.max(np.abs(final_center - source_center)))
        <= MAXIMUM_SECTION_CENTER_DELTA
        and profile_fairness_pass
        and final_boundary_bilaplacian["rms"]
        <= source_boundary_bilaplacian["rms"] * 1.05
        and final_boundary_bilaplacian["p99"]
        <= source_boundary_bilaplacian["p99"] * 1.05
        and volume_relative_delta <= 0.005
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
        "sourceRevision": "r14",
        "sourceIdentity": {
            "path": SOURCE_BLEND,
            "sha256Before": source_sha_before,
            "sha256After": source_sha_after,
            "sizeBytes": source_size_bytes,
            "unchanged": source_sha_before == source_sha_after,
        },
        "construction": CONSTRUCTION,
        "process": {
            "additionalRecessDistance": ADDITIONAL_RECESS_DISTANCE,
            "cumulativeNominalRecessFromR13": CUMULATIVE_NOMINAL_RECESS,
            "zSupport": [r14.Z_SUPPORT_START, r14.Z_SUPPORT_END],
            "zPeak": r14.Z_PEAK,
            "lowerRamp": "21t^5-35t^6+15t^7_C2",
            "xCore": r14.X_CORE,
            "xSupport": r14.X_SUPPORT,
            "sideWeightStart": r14.SIDE_WEIGHT_START,
            "sideWeightFull": r14.SIDE_WEIGHT_FULL,
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
            "minimumRemainingToCenter": minimum_remaining_to_center,
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
            "r15 additional crotch recess QA failed: "
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
    scene["source_revision"] = "r14"
    scene["user_visual_approval_recorded"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R15_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R15_GENERATION_RESULT=" + report["technicalResult"])


if __name__ == "__main__":
    main()
