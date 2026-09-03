#!/usr/bin/env python3

"""Fair the r13 body base and exactly reapply the approved r14/r15 recess layers."""

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
R13_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-013-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r13.blend",
)
R14_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-014-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r14.blend",
)
R15_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-015-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r15.blend",
)
R13_BODY = "C1B_R13_SideTransitionFair_TPoseBody_NoHands"
R14_BODY = "C1B_R14_CrotchRecess_TPoseBody_NoHands"
R15_BODY = "C1B_R15_CrotchRecess7mm_TPoseBody_NoHands"
R15_HEAD = "C1B_R15_RoundFacelessHead"
BODY_NAME = "C1B_R16_FullBodyCrotchFair7mm_TPoseBody_NoHands"
HEAD_NAME = "C1B_R16_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
REVISION = "r16"
VERSION = "0.16.0-local-preview"
CONSTRUCTION = "R15_3AXIS_LAPLACIAN_BODY_CROTCH_FAIR_WITH_EXACT_3MM_AND_4MM_LAYERS"

PROFILE_STEP = 0.002
PROFILE_MIN_Z = 0.100
PROFILE_MAX_Z = 0.720
WIDTH_LIMIT = 0.225
DEPTH_X_LIMIT = 0.205
WIDTH_PRESENT_SIGMA = 0.003
WIDTH_POLYNOMIAL_DEGREE = 4
DEPTH_PRESENT_SIGMA = 0.003
DEPTH_FIDELITY = 30.0
DEPTH_CURVATURE_WEIGHT = 500000.0
DEPTH_BLEND = 0.70

EDIT_START_Z = 0.1265
EDIT_FULL_Z = 0.1565
EDIT_FADE_START_Z = 0.640
EDIT_END_Z = 0.690
FAIR_START_Z = 0.050
FAIR_FULL_Z = 0.130
X_FULL = 0.190
X_END = 0.225
CORE_FULL_X = 0.030
LOCAL_SMOOTH_ITERATIONS = 120
LOCAL_SMOOTH_FACTOR = 0.20
CROTCH_SMOOTH_ITERATIONS = 240
CROTCH_SMOOTH_FACTOR = 0.20
CROTCH_SMOOTH_FADE_START_Z = 0.240
CROTCH_SMOOTH_END_Z = 0.320
CROTCH_SMOOTH_FULL_X = 0.160
CROTCH_SMOOTH_END_X = 0.200

MAXIMUM_VERTEX_DELTA = 0.00270
MAXIMUM_X_DELTA = 0.00160
MAXIMUM_Y_DELTA = 0.00220
MAXIMUM_Z_DELTA = 0.00210
MAXIMUM_SECTION_CENTER_DELTA = 0.000002
MAXIMUM_VOLUME_RELATIVE_DELTA = 0.002


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r15 = import_file("c1b_rw015", "create_c1b_rw015_crotch_recess_additional.py")
r12 = r15.r12
r14 = r15.r14
qa = r15.qa


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


def array_sha256(values):
    return hashlib.sha256(np.ascontiguousarray(values).tobytes()).hexdigest()


def profile_grid():
    return np.arange(
        PROFILE_MIN_Z,
        PROFILE_MAX_Z + PROFILE_STEP * 0.5,
        PROFILE_STEP,
        dtype=np.float64,
    )


def fill_profile_gaps(grid, values):
    valid = np.isfinite(values)
    if valid.sum() < 2:
        raise RuntimeError("insufficient profile samples")
    return np.interp(grid, grid[valid], values[valid])


def sample_profiles(coordinates, edges, grid):
    first = coordinates[edges[:, 0]]
    second = coordinates[edges[:, 1]]
    delta_z = second[:, 2] - first[:, 2]
    non_horizontal = np.abs(delta_z) > 1.0e-12
    minimum_z = np.minimum(first[:, 2], second[:, 2])
    maximum_z = np.maximum(first[:, 2], second[:, 2])
    width = np.full(len(grid), np.nan, dtype=np.float64)
    positive = np.full(len(grid), np.nan, dtype=np.float64)
    negative = np.full(len(grid), np.nan, dtype=np.float64)
    for index, height in enumerate(grid):
        crossing = non_horizontal & (minimum_z <= height) & (maximum_z >= height)
        if not np.any(crossing):
            continue
        t = (height - first[crossing, 2]) / delta_z[crossing]
        x = first[crossing, 0] + t * (second[crossing, 0] - first[crossing, 0])
        y = first[crossing, 1] + t * (second[crossing, 1] - first[crossing, 1])
        absolute_x = np.abs(x)
        width_values = absolute_x[absolute_x <= WIDTH_LIMIT]
        depth_values = y[absolute_x <= DEPTH_X_LIMIT]
        if len(width_values):
            width[index] = float(width_values.max())
        if len(depth_values):
            positive[index] = float(depth_values.max())
            negative[index] = float(depth_values.min())
    return (
        fill_profile_gaps(grid, width),
        fill_profile_gaps(grid, positive),
        fill_profile_gaps(grid, negative),
    )


def gaussian_filter(values, sigma):
    sigma_steps = sigma / PROFILE_STEP
    radius = int(math.ceil(3.0 * sigma_steps))
    positions = np.arange(-radius, radius + 1, dtype=np.float64)
    kernel = np.exp(-0.5 * (positions / sigma_steps) ** 2)
    kernel /= kernel.sum()
    return np.convolve(np.pad(values, radius, mode="edge"), kernel, mode="valid")


def c2_bridge_profile(values, grid, segments):
    present = gaussian_filter(values, DEPTH_PRESENT_SIGMA)
    derivative_source = gaussian_filter(present, 0.006)
    first = np.gradient(derivative_source, PROFILE_STEP)
    second = np.gradient(first, PROFILE_STEP)
    target = present.copy()
    endpoint_system = np.array(
        [
            [1.0, 0.0, 0.0, 0.0, 0.0, 0.0],
            [0.0, 1.0, 0.0, 0.0, 0.0, 0.0],
            [0.0, 0.0, 2.0, 0.0, 0.0, 0.0],
            [1.0, 1.0, 1.0, 1.0, 1.0, 1.0],
            [0.0, 1.0, 2.0, 3.0, 4.0, 5.0],
            [0.0, 0.0, 2.0, 6.0, 12.0, 20.0],
        ],
        dtype=np.float64,
    )
    for start, end in segments:
        start_index = int(np.argmin(np.abs(grid - start)))
        end_index = int(np.argmin(np.abs(grid - end)))
        length = end - start
        endpoint_values = np.array(
            [
                present[start_index],
                first[start_index] * length,
                second[start_index] * length * length,
                present[end_index],
                first[end_index] * length,
                second[end_index] * length * length,
            ],
            dtype=np.float64,
        )
        coefficients = np.linalg.solve(endpoint_system, endpoint_values)
        t = (grid - start) / length
        polynomial = sum(
            coefficients[power] * t**power for power in range(6)
        )
        active = (grid >= start) & (grid <= end)
        target[active] = polynomial[active]
    return present, target


def build_profile_corrections(grid, width, positive, negative):
    present_width = gaussian_filter(width, WIDTH_PRESENT_SIGMA)
    polynomial_mask = (grid >= 0.126) & (grid <= 0.560)
    normalized_z = (grid - 0.340) / 0.220
    coefficients = np.polyfit(
        normalized_z[polynomial_mask],
        present_width[polynomial_mask],
        WIDTH_POLYNOMIAL_DEGREE,
    )
    fair_width = np.polyval(coefficients, normalized_z)
    width_window = r12.smootherstep((grid - EDIT_START_Z) / 0.035) * (
        1.0 - r12.smootherstep((grid - 0.525) / 0.035)
    )
    width_correction = width_window * (fair_width - present_width)

    center = (positive + negative) * 0.5
    half_depth = (positive - negative) * 0.5
    present_depth, crotch_depth_target = c2_bridge_profile(
        half_depth,
        grid,
        ((0.126, 0.180), (0.180, 0.280)),
    )
    crotch_depth_correction = crotch_depth_target - present_depth
    count = len(grid)
    second_difference = np.zeros((count - 2, count), dtype=np.float64)
    rows = np.arange(count - 2)
    second_difference[rows, rows] = 1.0
    second_difference[rows, rows + 1] = -2.0
    second_difference[rows, rows + 2] = 1.0
    fidelity = np.full(count, DEPTH_FIDELITY, dtype=np.float64)
    active = (grid >= 0.260) & (grid <= EDIT_END_Z)
    fidelity[~active] = 1.0e12
    collars = (grid >= 0.680) & (grid <= 0.695)
    fidelity[collars] = 1.0e9
    system = np.diag(fidelity) + DEPTH_CURVATURE_WEIGHT * (
        second_difference.T @ second_difference
    )
    target_depth = np.linalg.solve(system, fidelity * present_depth)
    torso_window = r12.smootherstep((grid - 0.260) / 0.060) * (
        1.0 - r12.smootherstep((grid - 0.640) / 0.050)
    )
    depth_correction = crotch_depth_correction + DEPTH_BLEND * torso_window * (
        target_depth - present_depth
    )
    return {
        "center": center,
        "presentWidth": present_width,
        "presentDepth": present_depth,
        "widthCorrection": width_correction,
        "depthCorrection": depth_correction,
        "targetDepth": target_depth,
        "crotchDepthTarget": crotch_depth_target,
        "torsoDepthWindow": torso_window,
        "widthWindow": width_window,
    }


def neighbor_average(values, edges, degree):
    first = edges[:, 0]
    second = edges[:, 1]
    sums = np.zeros_like(values)
    np.add.at(sums, first, values[second])
    np.add.at(sums, second, values[first])
    if values.ndim == 1:
        return sums / degree
    return sums / degree[:, None]


def fair_final_surface(reference, edges, grid, profiles):
    z = reference[:, 2]
    absolute_x = np.abs(reference[:, 0])
    local_center = np.interp(z, grid, profiles["center"])
    local_width = np.interp(z, grid, profiles["presentWidth"])
    local_depth = np.interp(z, grid, profiles["presentDepth"])
    local_width_correction = np.interp(z, grid, profiles["widthCorrection"])
    local_depth_correction = np.interp(z, grid, profiles["depthCorrection"])
    x_weight = 1.0 - r12.smootherstep((absolute_x - X_FULL) / (X_END - X_FULL))

    outside = (
        (z <= FAIR_START_Z)
        | (z >= EDIT_END_Z)
        | (absolute_x >= X_END)
    )
    result = reference.copy()
    result[:, 0] += x_weight * reference[:, 0] * (
        local_width_correction / local_width
    )
    result[:, 1] += x_weight * (reference[:, 1] - local_center) * (
        local_depth_correction / local_depth
    )

    result[:, 2] = reference[:, 2]
    result[outside] = reference[outside]

    z_weight = r12.smootherstep(
        (z - FAIR_START_Z) / (FAIR_FULL_Z - FAIR_START_Z)
    ) * (
        1.0
        - r12.smootherstep(
            (z - EDIT_FADE_START_Z) / (EDIT_END_Z - EDIT_FADE_START_Z)
        )
    )
    weight = z_weight * x_weight
    weight[outside] = 0.0
    degree = np.bincount(edges.reshape(-1), minlength=len(reference)).astype(np.float64)
    for _ in range(LOCAL_SMOOTH_ITERATIONS):
        average = neighbor_average(result, edges, degree)
        result += LOCAL_SMOOTH_FACTOR * weight[:, None] * (average - result)
        result[outside] = reference[outside]
    crotch_weight = r12.smootherstep(
        (z - FAIR_START_Z) / (0.120 - FAIR_START_Z)
    ) * (
        1.0
        - r12.smootherstep(
            (z - CROTCH_SMOOTH_FADE_START_Z)
            / (CROTCH_SMOOTH_END_Z - CROTCH_SMOOTH_FADE_START_Z)
        )
    ) * (
        1.0
        - r12.smootherstep(
            (absolute_x - CROTCH_SMOOTH_FULL_X)
            / (CROTCH_SMOOTH_END_X - CROTCH_SMOOTH_FULL_X)
        )
    )
    crotch_weight[outside] = 0.0
    for _ in range(CROTCH_SMOOTH_ITERATIONS):
        average = neighbor_average(result, edges, degree)
        result += CROTCH_SMOOTH_FACTOR * crotch_weight[:, None] * (
            average - result
        )
        result[outside] = reference[outside]
    return result, outside, weight


def bilaplacian_metrics(coordinates, edges, mask):
    degree = np.bincount(edges.reshape(-1), minlength=len(coordinates)).astype(np.float64)
    laplacian = coordinates - neighbor_average(coordinates, edges, degree)
    bilaplacian = laplacian - neighbor_average(laplacian, edges, degree)
    values = np.linalg.norm(bilaplacian[mask], axis=1)
    return {
        "sampleCount": int(len(values)),
        "rms": float(np.sqrt(np.mean(values * values))),
        "p95": float(np.percentile(values, 95.0)),
        "p99": float(np.percentile(values, 99.0)),
        "maximum": float(values.max()),
    }


def derivative_metrics(values, grid, start, end):
    smooth = gaussian_filter(values, 0.006)
    mask = (grid >= start) & (grid <= end)
    samples = smooth[mask]
    first = np.gradient(samples, PROFILE_STEP)
    second = np.gradient(first, PROFILE_STEP)
    third = np.gradient(second, PROFILE_STEP)
    signs = np.sign(second)
    nonzero = signs[signs != 0.0]
    return {
        "sampleCount": int(len(samples)),
        "secondDerivativeRms": float(np.sqrt(np.mean(second * second))),
        "secondDerivativeMaximum": float(np.max(np.abs(second))),
        "thirdDerivativeRms": float(np.sqrt(np.mean(third * third))),
        "curvatureSignChanges": int(
            np.count_nonzero(nonzero[1:] != nonzero[:-1])
        ),
    }


def profile_metrics(width, positive, negative, grid):
    depth = (positive - negative) * 0.5
    return {
        "crotchWidth": derivative_metrics(width, grid, 0.135, 0.280),
        "crotchDepth": derivative_metrics(depth, grid, 0.135, 0.280),
        "torsoWidth": derivative_metrics(width, grid, 0.280, 0.540),
        "torsoDepth": derivative_metrics(depth, grid, 0.280, 0.640),
    }


def core_profile_metrics(positive, negative, grid):
    return {
        "front": derivative_metrics(positive, grid, 0.126, 0.240),
        "back": derivative_metrics(-negative, grid, 0.126, 0.240),
    }


def main():
    blend_path, render_dir, report_path = args()
    for path in (R13_BLEND, R14_BLEND, R15_BLEND):
        if not os.path.exists(path):
            raise FileNotFoundError(path)
    os.makedirs(os.path.dirname(blend_path), exist_ok=True)
    os.makedirs(render_dir, exist_ok=True)
    os.makedirs(os.path.dirname(report_path), exist_ok=True)

    source_paths = (R13_BLEND, R14_BLEND, R15_BLEND)
    source_hashes_before = {path: file_sha256(path) for path in source_paths}
    source_sizes = {path: os.path.getsize(path) for path in source_paths}

    bpy.ops.wm.open_mainfile(filepath=R13_BLEND)
    body13 = bpy.data.objects[R13_BODY]
    coordinates13 = r12.mesh_coordinates(body13.data).copy()
    edges13 = r12.mesh_edges(body13.data).copy()
    signature13 = r12.mesh_signature(body13)

    bpy.ops.wm.open_mainfile(filepath=R14_BLEND)
    body14 = bpy.data.objects[R14_BODY]
    coordinates14 = r12.mesh_coordinates(body14.data).copy()
    edges14 = r12.mesh_edges(body14.data).copy()
    signature14 = r12.mesh_signature(body14)

    bpy.ops.wm.open_mainfile(filepath=R15_BLEND)
    body = bpy.data.objects[R15_BODY]
    head = bpy.data.objects[R15_HEAD]
    coordinates15 = r12.mesh_coordinates(body.data).copy()
    edges15 = r12.mesh_edges(body.data).copy()
    signature15 = r12.mesh_signature(body)
    source_volume = qa.manifold(body.data)["signedVolume"]
    source_folds = qa.folds(body.data)
    source_head_coordinates = r12.mesh_coordinates(head.data).copy()
    source_head_matrix = np.array(head.matrix_world, dtype=np.float64)
    source_head_overlap = float(
        r12.r11.object_bounds(body)[1].z - r12.r11.object_bounds(head)[0].z
    )

    if not (signature13 == signature14 == signature15):
        raise RuntimeError("r13/r14/r15 topology signatures differ")
    if not (
        np.array_equal(edges13, edges14)
        and np.array_equal(edges14, edges15)
    ):
        raise RuntimeError("r13/r14/r15 edge arrays differ")
    if not (
        np.array_equal(coordinates13[:, 0], coordinates14[:, 0])
        and np.array_equal(coordinates14[:, 0], coordinates15[:, 0])
        and np.array_equal(coordinates13[:, 2], coordinates14[:, 2])
        and np.array_equal(coordinates14[:, 2], coordinates15[:, 2])
    ):
        raise RuntimeError("recess lineage changed X or Z")

    layer3 = coordinates14 - coordinates13
    layer4 = coordinates15 - coordinates14
    total_recess_layer = layer3 + layer4
    grid = profile_grid()
    source_width, source_positive, source_negative = sample_profiles(
        coordinates15, edges15, grid
    )
    profile_targets = build_profile_corrections(
        grid, source_width, source_positive, source_negative
    )
    core_grid = r14.profile_grid()
    source_core_positive, source_core_negative = r14.sample_signed_y(
        coordinates15, edges15, core_grid, CORE_FULL_X
    )
    smooth_final, outside, edit_weight = fair_final_surface(
        coordinates15, edges15, grid, profile_targets
    )
    source_center_profile = (source_positive + source_negative) * 0.5
    for _ in range(2):
        _, smooth_positive, smooth_negative = sample_profiles(
            smooth_final, edges15, grid
        )
        smooth_center_profile = (smooth_positive + smooth_negative) * 0.5
        center_drift = smooth_center_profile - source_center_profile
        local_center_drift = np.interp(
            smooth_final[:, 2], grid, center_drift
        )
        smooth_final[~outside, 1] -= local_center_drift[~outside]
        smooth_final[outside] = coordinates15[outside]
    surface_correction = smooth_final - coordinates15
    smooth_base = coordinates13 + surface_correction
    final_target = smooth_base + total_recess_layer
    final_target[outside] = coordinates15[outside]
    hypothetical_smooth_r14 = smooth_base + layer3
    reconstruction_error = float(np.max(np.abs(final_target - smooth_final)))

    layer_reapplication_error = float(
        np.max(np.abs((final_target - smooth_base) - total_recess_layer))
    )
    additional_layer_error = float(
        np.max(np.abs((final_target - hypothetical_smooth_r14) - layer4))
    )
    outside_target_delta = r12.maximum_delta(coordinates15, final_target, outside)
    additional_peak = np.abs(layer4[:, 1]) >= (
        float(np.max(np.abs(layer4[:, 1]))) - 1.0e-9
    )
    additional_peak_absolute_delta = r12.maximum_delta(
        coordinates15, final_target, additional_peak
    )

    r12.set_mesh_coordinates(body.data, final_target)
    final_coordinates = r12.mesh_coordinates(body.data)
    delta = final_coordinates - coordinates15
    delta_norm = np.linalg.norm(delta, axis=1)
    modified = delta_norm > 0.0
    source_crotch_mask = (
        (coordinates15[:, 2] >= EDIT_START_Z)
        & (coordinates15[:, 2] <= 0.280)
        & (np.abs(coordinates15[:, 0]) <= 0.120)
    )
    source_torso_mask = (
        (coordinates15[:, 2] >= 0.280)
        & (coordinates15[:, 2] <= EDIT_FADE_START_Z)
        & (np.abs(coordinates15[:, 0]) <= X_END)
    )
    source_bilaplacian = {
        "crotch": bilaplacian_metrics(coordinates15, edges15, source_crotch_mask),
        "torso": bilaplacian_metrics(coordinates15, edges15, source_torso_mask),
    }
    final_bilaplacian = {
        "crotch": bilaplacian_metrics(final_coordinates, edges15, source_crotch_mask),
        "torso": bilaplacian_metrics(final_coordinates, edges15, source_torso_mask),
    }
    final_width, final_positive, final_negative = sample_profiles(
        final_coordinates, edges15, grid
    )
    source_profile_metrics = profile_metrics(
        source_width, source_positive, source_negative, grid
    )
    final_profile_metrics = profile_metrics(
        final_width, final_positive, final_negative, grid
    )
    final_core_positive, final_core_negative = r14.sample_signed_y(
        final_coordinates, edges15, core_grid, CORE_FULL_X
    )
    source_core_metrics = core_profile_metrics(
        source_core_positive, source_core_negative, core_grid
    )
    final_core_metrics = core_profile_metrics(
        final_core_positive, final_core_negative, core_grid
    )
    source_center = (source_positive + source_negative) * 0.5
    final_center = (final_positive + final_negative) * 0.5

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
    volume_relative_delta = abs(manifold["signedVolume"] - source_volume) / abs(
        source_volume
    )
    head_coordinate_delta = r12.maximum_delta(
        source_head_coordinates, r12.mesh_coordinates(head.data)
    )
    head_matrix_delta = float(
        np.max(
            np.abs(
                np.array(head.matrix_world, dtype=np.float64) - source_head_matrix
            )
        )
    )
    head_overlap = float(
        r12.r11.object_bounds(body)[1].z - r12.r11.object_bounds(head)[0].z
    )
    runtime_modifier_count = sum(
        len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
    )

    r15.r13.r12.REVISION = REVISION
    outputs = r15.r13.r12.render(bpy.context.scene, render_dir)
    missing_renders = [
        name for name in outputs if not os.path.isfile(os.path.join(render_dir, name))
    ]
    source_hashes_after = {path: file_sha256(path) for path in source_paths}

    torso_profile_pass = all(
        final_profile_metrics[name]["secondDerivativeRms"]
        <= source_profile_metrics[name]["secondDerivativeRms"]
        and final_profile_metrics[name]["thirdDerivativeRms"]
        <= source_profile_metrics[name]["thirdDerivativeRms"]
        and final_profile_metrics[name]["curvatureSignChanges"]
        <= source_profile_metrics[name]["curvatureSignChanges"]
        for name in ("torsoWidth", "torsoDepth")
    )
    crotch_profile_pass = (
        final_profile_metrics["crotchWidth"]["secondDerivativeRms"]
        <= source_profile_metrics["crotchWidth"]["secondDerivativeRms"] * 1.05
        and final_profile_metrics["crotchWidth"]["thirdDerivativeRms"]
        <= source_profile_metrics["crotchWidth"]["thirdDerivativeRms"]
        and final_profile_metrics["crotchWidth"]["curvatureSignChanges"]
        <= source_profile_metrics["crotchWidth"]["curvatureSignChanges"]
        and final_profile_metrics["crotchDepth"]["secondDerivativeRms"]
        <= source_profile_metrics["crotchDepth"]["secondDerivativeRms"]
        and final_profile_metrics["crotchDepth"]["thirdDerivativeRms"]
        <= source_profile_metrics["crotchDepth"]["thirdDerivativeRms"] * 1.20
        and final_profile_metrics["crotchDepth"]["curvatureSignChanges"]
        <= source_profile_metrics["crotchDepth"]["curvatureSignChanges"]
    )
    core_profile_pass = all(
        final_core_metrics[name]["secondDerivativeRms"]
        <= source_core_metrics[name]["secondDerivativeRms"]
        and final_core_metrics[name]["thirdDerivativeRms"]
        <= source_core_metrics[name]["thirdDerivativeRms"]
        and final_core_metrics[name]["curvatureSignChanges"]
        <= source_core_metrics[name]["curvatureSignChanges"]
        for name in ("front", "back")
    )
    profile_pass = torso_profile_pass and crotch_profile_pass and core_profile_pass
    technical_pass = (
        all(source_hashes_before[path] == source_hashes_after[path] for path in source_paths)
        and signature13 == signature14 == signature15 == target_signature
        and np.array_equal(edges13, edges14)
        and np.array_equal(edges14, edges15)
        and float(np.max(np.abs(layer3[:, 0]))) == 0.0
        and float(np.max(np.abs(layer3[:, 2]))) == 0.0
        and float(np.max(np.abs(layer4[:, 0]))) == 0.0
        and float(np.max(np.abs(layer4[:, 2]))) == 0.0
        and float(np.max(np.abs(layer3[:, 1]))) <= 0.003001
        and float(np.max(np.abs(layer4[:, 1]))) <= 0.004001
        and float(np.max(np.abs(total_recess_layer[:, 1]))) >= 0.006999
        and float(np.max(np.abs(total_recess_layer[:, 1]))) <= 0.007001
        and layer_reapplication_error <= 1.0e-12
        and additional_layer_error <= 1.0e-12
        and reconstruction_error <= 1.0e-12
        and outside_target_delta == 0.0
        and np.array_equal(final_coordinates[outside], coordinates15[outside])
        and r12.maximum_delta(coordinates15, final_coordinates) <= MAXIMUM_VERTEX_DELTA
        and float(np.max(np.abs(delta[:, 0]))) <= MAXIMUM_X_DELTA
        and float(np.max(np.abs(delta[:, 1]))) <= MAXIMUM_Y_DELTA
        and float(np.max(np.abs(delta[:, 2]))) <= MAXIMUM_Z_DELTA
        and float(np.max(np.abs(final_center - source_center)))
        <= MAXIMUM_SECTION_CENTER_DELTA
        and final_bilaplacian["crotch"]["rms"]
        <= source_bilaplacian["crotch"]["rms"] * 0.85
        and final_bilaplacian["crotch"]["p99"]
        <= source_bilaplacian["crotch"]["p99"] * 0.85
        and final_bilaplacian["torso"]["rms"]
        <= source_bilaplacian["torso"]["rms"] * 0.75
        and final_bilaplacian["torso"]["p99"]
        <= source_bilaplacian["torso"]["p99"] * 0.75
        and profile_pass
        and volume_relative_delta <= MAXIMUM_VOLUME_RELATIVE_DELTA
        and head_coordinate_delta == 0.0
        and head_matrix_delta == 0.0
        and abs(head_overlap - source_head_overlap) <= 1.0e-12
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
        and folds["adjacentAngleMaximumDegrees"] <= 7.0
        and overlap["result"] == "PASS"
        and runtime_modifier_count == 0
        and not missing_renders
    )

    report = {
        "assetId": ASSET_ID,
        "revision": REVISION,
        "assetVersion": VERSION,
        "sourceRevision": "r15",
        "sourceIdentity": {
            os.path.basename(path): {
                "path": path,
                "sha256Before": source_hashes_before[path],
                "sha256After": source_hashes_after[path],
                "sizeBytes": source_sizes[path],
                "unchanged": source_hashes_before[path] == source_hashes_after[path],
            }
            for path in source_paths
        },
        "construction": CONSTRUCTION,
        "process": {
            "baseRevision": "r13",
            "preservedRecessLayers": ["r14-r13_3mm", "r15-r14_4mm"],
            "profileStep": PROFILE_STEP,
            "widthPresentSigma": WIDTH_PRESENT_SIGMA,
            "widthPolynomialDegree": WIDTH_POLYNOMIAL_DEGREE,
            "depthPresentSigma": DEPTH_PRESENT_SIGMA,
            "depthFidelity": DEPTH_FIDELITY,
            "depthCurvatureWeight": DEPTH_CURVATURE_WEIGHT,
            "depthBlend": DEPTH_BLEND,
            "editZ": [EDIT_START_Z, EDIT_END_Z],
            "editX": [X_FULL, X_END],
            "threeAxisFairZ": [FAIR_START_Z, EDIT_END_Z],
            "threeAxisFairFullStartZ": FAIR_FULL_Z,
            "localSmoothIterations": LOCAL_SMOOTH_ITERATIONS,
            "localSmoothFactor": LOCAL_SMOOTH_FACTOR,
            "crotchSmoothIterations": CROTCH_SMOOTH_ITERATIONS,
            "crotchSmoothFactor": CROTCH_SMOOTH_FACTOR,
            "crotchSmoothZ": [FAIR_START_Z, CROTCH_SMOOTH_END_Z],
            "crotchSmoothX": [
                CROTCH_SMOOTH_FULL_X,
                CROTCH_SMOOTH_END_X,
            ],
            "topologyChangingOperations": 0,
        },
        "lineage": {
            "topologySignaturesEqual": signature13 == signature14 == signature15,
            "edgeArraySha256": {
                "r13": array_sha256(edges13),
                "r14": array_sha256(edges14),
                "r15": array_sha256(edges15),
            },
            "layer3MaximumAbsY": float(np.max(np.abs(layer3[:, 1]))),
            "layer4MaximumAbsY": float(np.max(np.abs(layer4[:, 1]))),
            "totalRecessLayerMaximumAbsY": float(
                np.max(np.abs(total_recess_layer[:, 1]))
            ),
            "layer3MaximumAbsX": float(np.max(np.abs(layer3[:, 0]))),
            "layer3MaximumAbsZ": float(np.max(np.abs(layer3[:, 2]))),
            "layer4MaximumAbsX": float(np.max(np.abs(layer4[:, 0]))),
            "layer4MaximumAbsZ": float(np.max(np.abs(layer4[:, 2]))),
            "layerReapplicationMaximumError": layer_reapplication_error,
            "additional4mmLayerMaximumError": additional_layer_error,
            "surfaceReconstructionMaximumError": reconstruction_error,
            "additional4mmPeakVertexCount": int(additional_peak.sum()),
            "additional4mmPeakAbsoluteMaximumDeltaFromR15": (
                additional_peak_absolute_delta
            ),
        },
        "edit": {
            "modifiedVertexCount": int(modified.sum()),
            "frozenVertexCount": int(outside.sum()),
            "threeAxisFairVertexCount": int((edit_weight > 0.0).sum()),
            "maximumVertexDelta": r12.maximum_delta(
                coordinates15, final_coordinates
            ),
            "maximumAbsXDelta": float(np.max(np.abs(delta[:, 0]))),
            "maximumAbsYDelta": float(np.max(np.abs(delta[:, 1]))),
            "maximumAbsZDelta": float(np.max(np.abs(delta[:, 2]))),
            "p95ModifiedVertexDelta": float(np.percentile(delta_norm[modified], 95.0)),
            "p99ModifiedVertexDelta": float(np.percentile(delta_norm[modified], 99.0)),
            "frozenMaximumDelta": r12.maximum_delta(
                coordinates15, final_coordinates, outside
            ),
            "maximumSectionCenterDelta": float(
                np.max(np.abs(final_center - source_center))
            ),
            "volumeRelativeDelta": volume_relative_delta,
            "editWeightMaximum": float(edit_weight.max()),
        },
        "surfaceBilaplacian": {
            "source": source_bilaplacian,
            "final": final_bilaplacian,
        },
        "profileFairness": {
            "source": source_profile_metrics,
            "final": final_profile_metrics,
        },
        "coreCrotchProfileFairness": {
            "source": source_core_metrics,
            "final": final_core_metrics,
        },
        "preservation": {
            "headCoordinateMaximumDelta": head_coordinate_delta,
            "headMatrixMaximumDelta": head_matrix_delta,
            "headBodyOverlap": head_overlap,
        },
        "sourceTopology": signature15,
        "targetTopology": target_signature,
        "topology": topology,
        "manifold": manifold,
        "mirror": mirror,
        "sourceFold": source_folds,
        "fold": folds,
        "foldMaximumDiagnostic": fold_diagnostic,
        "bvhSelfIntersection": overlap,
        "runtimeModifierCount": runtime_modifier_count,
        "renderFiles": outputs,
        "missingRenderFiles": missing_renders,
        "userVisualApprovalRecorded": False,
        "productionTopologyApproved": False,
        "technicalResult": "PASS" if technical_pass else "FAIL",
    }
    if not technical_pass:
        raise RuntimeError(
            "r16 body/crotch fair QA failed: "
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
    scene["source_revision"] = "r15"
    scene["user_visual_approval_recorded"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R16_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R16_GENERATION_RESULT=" + report["technicalResult"])


if __name__ == "__main__":
    main()
