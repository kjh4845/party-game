#!/usr/bin/env python3

"""Fair the r11 torso and its leg transition without changing the remaining mesh."""

import hashlib
import importlib.util
import json
import math
import os
import sys

import bpy
import bmesh
import numpy as np
from mathutils import Vector


HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
SOURCE_BLEND = os.path.join(
    ROOT,
    "BlenderSource",
    "Characters",
    "C1B-RW-011-preview",
    "CHR_MasterCharacter_C1B_NeutralRework_r11.blend",
)
SOURCE_BODY = "C1B_R11_GlobalFair_TPoseBody_NoHands"
SOURCE_HEAD = "C1B_R11_RoundFacelessHead"
BODY_NAME = "C1B_R12_TorsoLegFair_TPoseBody_NoHands"
HEAD_NAME = "C1B_R12_RoundFacelessHead"
ASSET_ID = "CHR_MasterCharacter_C1B_NeutralRework"
REVISION = "r12"
VERSION = "0.12.0-local-preview"
CONSTRUCTION = "R11_SINGLE_SCREENED_BIHARMONIC_TORSO_AND_LEG_TRANSITION_FAIR"
VIEW_NAMES = ("Front", "Side", "Back", "ThreeQuarter")

# The lower leg core and the arm core are exact-frozen preservation regions.
LOWER_LEG_FREEZE_Z = 0.100
TORSO_FAIR_END_Z = 0.700
TORSO_FAIR_FADE_Z = 0.690
UPPER_TORSO_CORE_X = 0.190
UPPER_TORSO_FADE_X = 0.220
ARM_FREEZE_X = 0.250

# r11's two leg cross-sections become one body cross-section at this height.
TRANSITION_START_Z = 0.1265
TRANSITION_END_Z = 0.2800
TRANSITION_BLEND_END_Z = 0.4000

# Reuse r11's fairing strength, but only inside the requested torso mask.
LOCAL_SMOOTH_FACTOR = 0.20
LOCAL_SMOOTH_ITERATIONS = 80

# The outline target is derived from r11 itself. Gaussian filtering removes
# high-frequency outline variation; the connection interval is replaced by
# the minimum-curvature curve between its existing endpoint sections.
PROFILE_Z_MIN = 0.100
PROFILE_Z_MAX = 0.560
PROFILE_STEP = 0.002
PROFILE_HALF_WINDOW = 0.0015
PROFILE_GAUSSIAN_SIGMA = 0.012
PROFILE_PRESENT_SIGMA = 0.003
PROFILE_X_LIMIT = 0.225
PROFILE_Y_X_LIMIT = 0.205
PROFILE_GLOBAL_CURVATURE_WEIGHT = 200000.0
PROFILE_TRANSITION_CURVATURE_WEIGHT = 5000000.0
PROFILE_OUTSIDE_FIDELITY = 40.0
PIN_DILATION_RINGS = 3
HARMONIC_MASK_ITERATIONS = 250
SCREEN_KEEP_WEIGHT = 0.10
SCREEN_TARGET_WEIGHT = 5.0
BIHARMONIC_WEIGHT = 5.0
CG_MAX_ITERATIONS = 340
CG_RELATIVE_TOLERANCE = 1.0e-9


def import_file(name, filename):
    spec = importlib.util.spec_from_file_location(name, os.path.join(HERE, filename))
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


r11 = import_file("c1b_rw011", "create_c1b_rw011_global_fair.py")
qa = r11.qa


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


def smootherstep(values):
    values = np.clip(values, 0.0, 1.0)
    return values * values * values * (values * (values * 6.0 - 15.0) + 10.0)


def mesh_coordinates(mesh):
    values = np.empty(len(mesh.vertices) * 3, dtype=np.float64)
    mesh.vertices.foreach_get("co", values)
    return values.reshape((-1, 3))


def set_mesh_coordinates(mesh, coordinates):
    mesh.vertices.foreach_set("co", np.asarray(coordinates, dtype=np.float32).reshape(-1))
    mesh.update()


def mesh_edges(mesh):
    values = np.empty(len(mesh.edges) * 2, dtype=np.int32)
    mesh.edges.foreach_get("vertices", values)
    return values.reshape((-1, 2))


def mesh_signature(obj):
    return {
        "vertices": len(obj.data.vertices),
        "edges": len(obj.data.edges),
        "faces": len(obj.data.polygons),
    }


def maximum_adjacent_angle_diagnostic(mesh):
    bm = bmesh.new()
    bm.from_mesh(mesh)
    maximum = -1.0
    maximum_edge = None
    for edge in bm.edges:
        if len(edge.link_faces) != 2:
            continue
        angle = math.degrees(edge.link_faces[0].normal.angle(edge.link_faces[1].normal))
        if angle > maximum:
            maximum = angle
            maximum_edge = edge
    if maximum_edge is None:
        result = {"angleDegrees": 0.0}
    else:
        points = [vertex.co.copy() for vertex in maximum_edge.verts]
        midpoint = (points[0] + points[1]) * 0.5
        result = {
            "angleDegrees": maximum,
            "vertexCoordinates": [[float(value) for value in point] for point in points],
            "midpoint": [float(value) for value in midpoint],
        }
    bm.free()
    return result


def locally_smooth(
    coordinates,
    edges,
    weights,
    factor=LOCAL_SMOOTH_FACTOR,
    iterations=LOCAL_SMOOTH_ITERATIONS,
):
    result = coordinates.copy()
    first = edges[:, 0]
    second = edges[:, 1]
    degree = np.bincount(edges.reshape(-1), minlength=len(result)).astype(np.float64)
    coordinate_weights = weights[:, None] if weights.ndim == 1 else weights
    for _ in range(iterations):
        sums = np.zeros_like(result)
        np.add.at(sums, first, result[second])
        np.add.at(sums, second, result[first])
        average = sums / degree[:, None]
        result += factor * coordinate_weights * (average - result)
    return result


def profile_grid():
    count = int(round((PROFILE_Z_MAX - PROFILE_Z_MIN) / PROFILE_STEP)) + 1
    return np.linspace(PROFILE_Z_MIN, PROFILE_Z_MAX, count, dtype=np.float64)


def fill_profile_gaps(grid, values):
    valid = np.isfinite(values)
    if valid.sum() < 2:
        raise RuntimeError("insufficient cross-section samples")
    return np.interp(grid, grid[valid], values[valid])


def sample_envelopes(coordinates, grid):
    absolute_x = np.abs(coordinates[:, 0])
    absolute_y = np.abs(coordinates[:, 1])
    z = coordinates[:, 2]
    width = np.full(len(grid), np.nan, dtype=np.float64)
    depth = np.full(len(grid), np.nan, dtype=np.float64)
    for index, height in enumerate(grid):
        slab = np.abs(z - height) <= PROFILE_HALF_WINDOW
        width_values = absolute_x[slab & (absolute_x <= PROFILE_X_LIMIT)]
        depth_values = absolute_y[slab & (absolute_x <= PROFILE_Y_X_LIMIT)]
        if len(width_values):
            width[index] = float(width_values.max())
        if len(depth_values):
            depth[index] = float(depth_values.max())
    return fill_profile_gaps(grid, width), fill_profile_gaps(grid, depth)


def sample_edge_envelopes(coordinates, edges, grid):
    """Measure exact Z-plane contour extrema by intersecting mesh edges."""

    first = coordinates[edges[:, 0]]
    second = coordinates[edges[:, 1]]
    delta_z = second[:, 2] - first[:, 2]
    non_horizontal = np.abs(delta_z) > 1.0e-12
    minimum_z = np.minimum(first[:, 2], second[:, 2])
    maximum_z = np.maximum(first[:, 2], second[:, 2])
    width = np.full(len(grid), np.nan, dtype=np.float64)
    depth = np.full(len(grid), np.nan, dtype=np.float64)
    for index, height in enumerate(grid):
        crossing = non_horizontal & (minimum_z <= height) & (maximum_z >= height)
        if not np.any(crossing):
            continue
        t = (height - first[crossing, 2]) / delta_z[crossing]
        x = first[crossing, 0] + t * (second[crossing, 0] - first[crossing, 0])
        y = first[crossing, 1] + t * (second[crossing, 1] - first[crossing, 1])
        absolute_x = np.abs(x)
        width_values = absolute_x[absolute_x <= PROFILE_X_LIMIT]
        depth_values = np.abs(y[absolute_x <= PROFILE_Y_X_LIMIT])
        if len(width_values):
            width[index] = float(width_values.max())
        if len(depth_values):
            depth[index] = float(depth_values.max())
    return fill_profile_gaps(grid, width), fill_profile_gaps(grid, depth)


def gaussian_fair(values, sigma=PROFILE_GAUSSIAN_SIGMA):
    sigma_steps = sigma / PROFILE_STEP
    radius = int(math.ceil(3.0 * sigma_steps))
    positions = np.arange(-radius, radius + 1, dtype=np.float64)
    kernel = np.exp(-0.5 * (positions / sigma_steps) ** 2)
    kernel /= kernel.sum()
    padded = np.pad(values, radius, mode="edge")
    return np.convolve(padded, kernel, mode="valid")


def minimum_curvature_transition(grid, faired):
    """Solve one continuous profile with stronger curvature removal at the join."""

    count = len(faired)
    second_difference = np.zeros((count - 2, count), dtype=np.float64)
    rows = np.arange(count - 2)
    second_difference[rows, rows] = 1.0
    second_difference[rows, rows + 1] = -2.0
    second_difference[rows, rows + 2] = 1.0
    centers = grid[1:-1]

    transition_window = smootherstep(
        (centers - (TRANSITION_START_Z - 0.025)) / 0.050
    ) * (
        1.0
        - smootherstep(
            (centers - (TRANSITION_END_Z - 0.025))
            / (TRANSITION_BLEND_END_Z - (TRANSITION_END_Z - 0.025))
        )
    )
    curvature_weight = (
        PROFILE_GLOBAL_CURVATURE_WEIGHT
        + PROFILE_TRANSITION_CURVATURE_WEIGHT * transition_window
    )

    sample_window = np.interp(grid, centers, transition_window, left=0.0, right=0.0)
    fidelity = 1.0 + PROFILE_OUTSIDE_FIDELITY * (1.0 - sample_window)
    fidelity[:2] = 1.0e8
    fidelity[-2:] = 1.0e8
    system = np.diag(fidelity) + second_difference.T @ (
        curvature_weight[:, None] * second_difference
    )
    return np.linalg.solve(system, fidelity * faired)


def build_target_profiles(source_coordinates, edges, grid):
    source_width, source_depth = sample_edge_envelopes(source_coordinates, edges, grid)
    target_width = minimum_curvature_transition(grid, gaussian_fair(source_width))
    regularized_depth = minimum_curvature_transition(grid, gaussian_fair(source_depth))
    source_entry_depth = gaussian_fair(source_depth, PROFILE_PRESENT_SIGMA)
    depth_entry = smootherstep((grid - 0.140) / 0.120)
    target_depth = source_entry_depth + depth_entry * (
        regularized_depth - source_entry_depth
    )
    return source_width, source_depth, target_width, target_depth


def neighbor_average(values, edges, degree):
    first = edges[:, 0]
    second = edges[:, 1]
    sums = np.zeros_like(values)
    np.add.at(sums, first, values[second])
    np.add.at(sums, second, values[first])
    if values.ndim == 1:
        return sums / degree
    return sums / degree[:, None]


def dilate_vertex_mask(mask, edges, rings):
    result = mask.copy()
    first = edges[:, 0]
    second = edges[:, 1]
    for _ in range(rings):
        expanded = result.copy()
        linked = result[first] | result[second]
        expanded[first[linked]] = True
        expanded[second[linked]] = True
        result = expanded
    return result


def build_harmonic_edit_field(source, edges, pin_gap):
    absolute_x = np.abs(source[:, 0])
    z = source[:, 2]
    allowed = (
        (z > LOWER_LEG_FREEZE_Z)
        & (z < TORSO_FAIR_END_Z)
        & (absolute_x < UPPER_TORSO_FADE_X)
    )
    hard_pin = ~allowed
    hard_pin |= absolute_x >= ARM_FREEZE_X
    hard_pin |= z <= LOWER_LEG_FREEZE_Z
    hard_pin |= z >= TORSO_FAIR_END_Z
    hard_pin = dilate_vertex_mask(hard_pin, edges, PIN_DILATION_RINGS)
    if pin_gap:
        hard_pin |= (z <= TRANSITION_START_Z) & (absolute_x <= 0.055)

    transition_core_start = 0.145 if pin_gap else TRANSITION_START_Z
    transition_core = (
        (z > transition_core_start)
        & (z < 0.340)
        & (absolute_x < 0.215)
    )
    if pin_gap:
        transition_core &= absolute_x > 0.055
    core = (
        (
            (z >= 0.175)
            & (z <= PROFILE_Z_MAX)
            & (absolute_x <= UPPER_TORSO_CORE_X)
        )
        | transition_core
        | (
            (z > PROFILE_Z_MAX)
            & (z < TORSO_FAIR_FADE_Z)
            & (absolute_x <= UPPER_TORSO_CORE_X)
        )
    )
    core &= ~hard_pin
    degree = np.bincount(edges.reshape(-1), minlength=len(source)).astype(np.float64)
    field = core.astype(np.float64)
    free = ~(hard_pin | core)
    for _ in range(HARMONIC_MASK_ITERATIONS):
        average = neighbor_average(field, edges, degree)
        field[free] = average[free]
        field[hard_pin] = 0.0
        field[core] = 1.0
    return smootherstep(field), hard_pin, core, degree


def graph_laplacian(values, edges, degree):
    first = edges[:, 0]
    second = edges[:, 1]
    result = degree * values
    np.add.at(result, first, -values[second])
    np.add.at(result, second, -values[first])
    return result


def conjugate_gradient(operator, right_hand_side, hard_pin):
    solution = np.zeros_like(right_hand_side)
    right_hand_side = right_hand_side.copy()
    right_hand_side[hard_pin] = 0.0
    residual = right_hand_side - operator(solution)
    residual[hard_pin] = 0.0
    direction = residual.copy()
    residual_squared = float(np.dot(residual, residual))
    initial_norm = math.sqrt(max(residual_squared, 1.0e-30))
    iterations = 0
    for iterations in range(1, CG_MAX_ITERATIONS + 1):
        applied = operator(direction)
        denominator = float(np.dot(direction, applied))
        if abs(denominator) <= 1.0e-30:
            break
        step = residual_squared / denominator
        solution += step * direction
        solution[hard_pin] = 0.0
        residual -= step * applied
        residual[hard_pin] = 0.0
        new_residual_squared = float(np.dot(residual, residual))
        if math.sqrt(max(new_residual_squared, 0.0)) <= (
            CG_RELATIVE_TOLERANCE * initial_norm
        ):
            residual_squared = new_residual_squared
            break
        direction = residual + (new_residual_squared / residual_squared) * direction
        direction[hard_pin] = 0.0
        residual_squared = new_residual_squared
    return solution, {
        "iterations": iterations,
        "relativeResidual": math.sqrt(max(residual_squared, 0.0)) / initial_norm,
    }


def single_field_fair(
    source,
    edges,
    grid,
    source_width,
    source_depth,
    target_width,
    target_depth,
):
    edit_field_x, hard_pin_x, core_x, degree = build_harmonic_edit_field(
        source, edges, pin_gap=True
    )
    edit_field_y, hard_pin_y, core_y, _ = build_harmonic_edit_field(
        source, edges, pin_gap=False
    )
    reference_width = gaussian_fair(source_width, PROFILE_PRESENT_SIGMA)
    reference_depth = gaussian_fair(source_depth, PROFILE_PRESENT_SIGMA)
    z = source[:, 2]
    clipped_z = np.clip(z, PROFILE_Z_MIN, PROFILE_Z_MAX)
    current_width = np.interp(clipped_z, grid, reference_width)
    current_depth = np.interp(clipped_z, grid, reference_depth)
    desired_width = np.interp(clipped_z, grid, target_width)
    desired_depth = np.interp(clipped_z, grid, target_depth)

    scaled_profile = source.copy()
    scaled_profile[:, 0] *= desired_width / current_width
    scaled_profile[:, 1] *= desired_depth / current_depth
    profile_blend = smootherstep((z - PROFILE_Z_MIN) / 0.070) * (
        1.0 - smootherstep((z - 0.500) / 0.100)
    )
    profile_target = source + profile_blend[:, None] * (scaled_profile - source)

    all_xy = np.column_stack(
        (np.ones(len(source)), np.ones(len(source)), np.zeros(len(source)))
    )
    target = locally_smooth(profile_target, edges, all_xy)
    target[:, 2] = source[:, 2]
    result = source.copy()
    solver = {}
    for axis, label in ((0, "x"), (1, "y")):
        edit_field = edit_field_x if axis == 0 else edit_field_y
        hard_pin = hard_pin_x if axis == 0 else hard_pin_y
        target_displacement = edit_field * (target[:, axis] - source[:, axis])
        screen = SCREEN_KEEP_WEIGHT + SCREEN_TARGET_WEIGHT * edit_field
        source_axis = source[:, axis]
        right_hand_side = screen * target_displacement

        def operator(values):
            projected = values.copy()
            projected[hard_pin] = 0.0
            lap = graph_laplacian(projected, edges, degree)
            applied = screen * projected + BIHARMONIC_WEIGHT * graph_laplacian(
                lap, edges, degree
            )
            applied[hard_pin] = projected[hard_pin]
            return applied

        displacement, solver[label] = conjugate_gradient(
            operator, right_hand_side, hard_pin
        )
        result[:, axis] = source_axis + displacement
    result[:, 2] = source[:, 2]
    result[hard_pin_x, 0] = source[hard_pin_x, 0]
    result[hard_pin_y, 1] = source[hard_pin_y, 1]
    protected = hard_pin_x & hard_pin_y
    edit_field = np.maximum(edit_field_x, edit_field_y)
    core = core_x | core_y
    return result, edit_field, protected, core, solver


def maximum_delta(before, after, mask=None):
    if mask is None:
        mask = np.ones(len(before), dtype=bool)
    if not np.any(mask):
        return 0.0
    return float(np.linalg.norm(after[mask] - before[mask], axis=1).max())


def maximum_axis_delta(before, after, axis, mask):
    if not np.any(mask):
        return 0.0
    return float(np.abs(after[mask, axis] - before[mask, axis]).max())


def ellipse_residual(coordinates, grid):
    width, depth = sample_envelopes(coordinates, grid)
    z = coordinates[:, 2]
    mask = (z >= 0.205) & (z <= 0.515) & (np.abs(coordinates[:, 0]) <= 0.205)
    local_width = np.interp(z[mask], grid, width)
    local_depth = np.interp(z[mask], grid, depth)
    radius = np.sqrt(
        (coordinates[mask, 0] / local_width) ** 2
        + (coordinates[mask, 1] / local_depth) ** 2
    )
    residual = np.abs(radius - 1.0)
    return {
        "sampleCount": int(len(residual)),
        "mean": float(residual.mean()),
        "p95": float(np.percentile(residual, 95.0)),
        "p99": float(np.percentile(residual, 99.0)),
    }


def transition_line_deviation(grid, values):
    mask = (grid >= TRANSITION_START_Z) & (grid <= TRANSITION_END_Z)
    heights = grid[mask]
    samples = values[mask]
    start = float(np.interp(TRANSITION_START_Z, grid, values))
    end = float(np.interp(TRANSITION_END_Z, grid, values))
    t = (heights - TRANSITION_START_Z) / (TRANSITION_END_Z - TRANSITION_START_Z)
    line = start + (end - start) * t
    deviation = np.abs(samples - line)
    return {
        "maximum": float(deviation.max()),
        "rms": float(np.sqrt(np.mean(deviation * deviation))),
    }


def outline_roughness(grid, values, start=0.340, end=0.540):
    mask = (grid >= start) & (grid <= end)
    samples = gaussian_fair(values)[mask]
    second = np.diff(samples, n=2) / (PROFILE_STEP * PROFILE_STEP)
    return {
        "sampleCount": int(len(samples)),
        "secondDerivativeRms": float(np.sqrt(np.mean(second * second))),
        "secondDerivativeMaximum": float(np.max(np.abs(second))),
    }


def profile_samples(grid, source_width, source_depth, final_width, final_depth):
    result = []
    for height in (0.130, 0.140, 0.150, 0.160, 0.170, 0.180, 0.200, 0.220, 0.240, 0.260, 0.280, 0.320, 0.400, 0.480, 0.540):
        result.append(
            {
                "z": height,
                "sourceWidth": float(2.0 * np.interp(height, grid, source_width)),
                "finalWidth": float(2.0 * np.interp(height, grid, final_width)),
                "sourceDepth": float(2.0 * np.interp(height, grid, source_depth)),
                "finalDepth": float(2.0 * np.interp(height, grid, final_depth)),
            }
        )
    return result


def render(scene, directory):
    os.makedirs(directory, exist_ok=True)
    scene.render.engine = "BLENDER_EEVEE"
    scene.render.resolution_x = 1536
    scene.render.resolution_y = 1536
    scene.render.resolution_percentage = 100
    scene.render.image_settings.file_format = "PNG"
    layer = scene.view_layers[0]
    ground = bpy.data.objects["QA_Ground"]
    standard_lights = [
        bpy.data.objects[name]
        for name in ("QA_Key_Left", "QA_Key_Right", "QA_Back", "QA_Left", "QA_Right")
    ]
    for obj in bpy.data.objects:
        if obj.type == "LIGHT" and obj.name.startswith(("QA_R10_Rake", "QA_R11_Rake")):
            obj.hide_render = True

    rake_data = bpy.data.lights.new("QA_R12_Rake_Data", "AREA")
    rake_data.energy = 150.0
    rake_data.shape = "DISK"
    rake_data.size = 2.8
    rake = bpy.data.objects.new("QA_R12_Rake", rake_data)
    scene.collection.objects.link(rake)
    rake.location = (-1.9, -2.8, 1.65)
    rake.rotation_euler = (Vector((0.0, 0.0, 0.48)) - rake.location).to_track_quat("-Z", "Y").to_euler()
    rake.hide_render = True

    silhouette_material = bpy.data.materials["MAT_C1BRW009_Silhouette"]
    rake_material = bpy.data.materials["MAT_C1BRW009_Rake"]
    background = next(node for node in scene.world.node_tree.nodes if node.type == "BACKGROUND")
    outputs = []
    styles = (
        ("Neutral", None, False, False, (0.18, 0.18, 0.18), 1.0),
        ("Silhouette", silhouette_material, True, False, (0.75, 0.75, 0.75), 0.8),
        ("RakeLight", rake_material, True, True, (0.035, 0.035, 0.035), 0.28),
    )
    for style, override, hide_ground, rake_on, world_color, world_strength in styles:
        layer.material_override = override
        ground.hide_render = hide_ground
        for light in standard_lights:
            light.hide_render = rake_on
        rake.hide_render = not rake_on
        background.inputs["Color"].default_value = (*world_color, 1.0)
        background.inputs["Strength"].default_value = world_strength
        for view in VIEW_NAMES:
            scene.camera = bpy.data.objects[f"CAM_C1BRW009_{view}"]
            filename = f"{ASSET_ID}_{REVISION}_{style}_{view}.png"
            scene.render.filepath = os.path.join(directory, filename)
            bpy.ops.render.render(write_still=True)
            outputs.append(filename)

    layer.material_override = None
    ground.hide_render = False
    for light in standard_lights:
        light.hide_render = False
    rake.hide_render = True
    background.inputs["Color"].default_value = (0.18, 0.18, 0.18, 1.0)
    background.inputs["Strength"].default_value = 1.0
    scene.camera = bpy.data.objects["CAM_C1BRW009_Front"]
    bpy.data.objects.remove(rake, do_unlink=True)
    bpy.data.lights.remove(rake_data)
    return outputs


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
    source_signature = mesh_signature(body)
    source_coordinates = mesh_coordinates(body.data)
    source_head_coordinates = mesh_coordinates(head.data)
    source_head_matrix = np.array(head.matrix_world, dtype=np.float64)
    source_head_overlap = float(r11.object_bounds(body)[1].z - r11.object_bounds(head)[0].z)

    grid = profile_grid()
    edges = mesh_edges(body.data)
    source_width, source_depth, target_width, target_depth = build_target_profiles(
        source_coordinates, edges, grid
    )
    final_coordinates, edit_field, protected, edit_core, solver = single_field_fair(
        source_coordinates,
        edges,
        grid,
        source_width,
        source_depth,
        target_width,
        target_depth,
    )
    set_mesh_coordinates(body.data, final_coordinates)
    final_coordinates = mesh_coordinates(body.data)

    arm_core = np.abs(source_coordinates[:, 0]) >= ARM_FREEZE_X
    lower_leg_core = source_coordinates[:, 2] <= LOWER_LEG_FREEZE_Z
    gap_x_core = (
        (source_coordinates[:, 2] <= TRANSITION_START_Z)
        & (np.abs(source_coordinates[:, 0]) <= 0.055)
    )
    vertex_delta = np.linalg.norm(final_coordinates - source_coordinates, axis=1)
    preservation = {
        "protectedVertexCount": int(protected.sum()),
        "protectedMaximumDelta": maximum_delta(source_coordinates, final_coordinates, protected),
        "armCoreVertexCount": int(arm_core.sum()),
        "armCoreMaximumDelta": maximum_delta(source_coordinates, final_coordinates, arm_core),
        "lowerLegCoreVertexCount": int(lower_leg_core.sum()),
        "lowerLegCoreMaximumDelta": maximum_delta(source_coordinates, final_coordinates, lower_leg_core),
        "legGapCoreVertexCount": int(gap_x_core.sum()),
        "legGapCoreMaximumXDelta": maximum_axis_delta(
            source_coordinates, final_coordinates, 0, gap_x_core
        ),
        "headCoordinateMaximumDelta": maximum_delta(
            source_head_coordinates, mesh_coordinates(head.data)
        ),
        "headMatrixMaximumDelta": float(
            np.abs(np.array(head.matrix_world, dtype=np.float64) - source_head_matrix).max()
        ),
    }

    final_width, final_depth = sample_edge_envelopes(final_coordinates, edges, grid)
    source_surface = ellipse_residual(source_coordinates, grid)
    final_surface = ellipse_residual(final_coordinates, grid)
    transition = {
        "width": {
            "source": transition_line_deviation(grid, source_width),
            "final": transition_line_deviation(grid, final_width),
        },
        "depth": {
            "source": transition_line_deviation(grid, source_depth),
            "final": transition_line_deviation(grid, final_depth),
        },
    }
    outline = {
        "width": {
            "source": outline_roughness(grid, source_width),
            "final": outline_roughness(grid, final_width),
        },
        "depth": {
            "source": outline_roughness(grid, source_depth),
            "final": outline_roughness(grid, final_depth),
        },
    }

    body.name = BODY_NAME
    body.data.name = BODY_NAME + "Mesh"
    head.name = HEAD_NAME
    head.data.name = HEAD_NAME + "Mesh"
    topology = r11.topology(body)
    manifold = qa.manifold(body.data)
    mirror = qa.mirror(body.data)
    folds = qa.folds(body.data)
    fold_diagnostic = maximum_adjacent_angle_diagnostic(body.data)
    overlap = qa.bvh_self_overlap(body.data)
    folds.pop("foldoverEdgesAt90Degrees", None)
    overlap.pop("nonAdjacentOverlapPairs", None)
    target_signature = mesh_signature(body)
    body_high = r11.object_bounds(body)[1]
    head_low = r11.object_bounds(head)[0]
    head_overlap = float(body_high.z - head_low.z)
    runtime_modifier_count = sum(
        len(obj.modifiers) for obj in bpy.data.objects if obj.type == "MESH"
    )
    outputs = render(bpy.context.scene, render_dir)
    missing_renders = [name for name in outputs if not os.path.isfile(os.path.join(render_dir, name))]
    source_sha_after = file_sha256(SOURCE_BLEND)

    technical_pass = (
        source_sha_after == source_sha_before
        and source_signature == target_signature
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
        and folds["adjacentAngleMaximumDegrees"] < 10.0
        and overlap["result"] == "PASS"
        and preservation["protectedMaximumDelta"] <= 1.0e-9
        and preservation["armCoreMaximumDelta"] <= 1.0e-9
        and preservation["lowerLegCoreMaximumDelta"] <= 1.0e-9
        and preservation["legGapCoreMaximumXDelta"] <= 1.0e-9
        and preservation["headCoordinateMaximumDelta"] <= 1.0e-12
        and preservation["headMatrixMaximumDelta"] <= 1.0e-12
        and transition["width"]["final"]["maximum"]
        < transition["width"]["source"]["maximum"]
        and transition["depth"]["final"]["maximum"]
        < transition["depth"]["source"]["maximum"]
        and outline["width"]["final"]["secondDerivativeRms"]
        < outline["width"]["source"]["secondDerivativeRms"]
        and outline["depth"]["final"]["secondDerivativeRms"]
        < outline["depth"]["source"]["secondDerivativeRms"]
        and final_surface["p95"] < source_surface["p95"]
        and abs(head_overlap - source_head_overlap) <= 1.0e-9
        and runtime_modifier_count == 0
        and not missing_renders
    )

    report = {
        "assetId": ASSET_ID,
        "revision": REVISION,
        "assetVersion": VERSION,
        "sourceRevision": "r11",
        "sourceIdentity": {
            "path": SOURCE_BLEND,
            "sha256Before": source_sha_before,
            "sha256After": source_sha_after,
            "sizeBytes": source_size_bytes,
            "unchanged": source_sha_after == source_sha_before,
        },
        "construction": CONSTRUCTION,
        "process": {
            "targetSurfaceSmoothFactor": LOCAL_SMOOTH_FACTOR,
            "targetSurfaceSmoothIterations": LOCAL_SMOOTH_ITERATIONS,
            "profileStep": PROFILE_STEP,
            "profileGaussianSigma": PROFILE_GAUSSIAN_SIGMA,
            "profilePresentSigma": PROFILE_PRESENT_SIGMA,
            "transitionStartZ": TRANSITION_START_Z,
            "transitionEndZ": TRANSITION_END_Z,
            "transitionBlendEndZ": TRANSITION_BLEND_END_Z,
            "transitionMethod": "CONTINUOUS_CURVATURE_REGULARIZATION_OF_THE_R11_SECTION_PROFILE",
            "globalCurvatureWeight": PROFILE_GLOBAL_CURVATURE_WEIGHT,
            "transitionCurvatureWeight": PROFILE_TRANSITION_CURVATURE_WEIGHT,
            "pinDilationRings": PIN_DILATION_RINGS,
            "harmonicMaskIterations": HARMONIC_MASK_ITERATIONS,
            "screenKeepWeight": SCREEN_KEEP_WEIGHT,
            "screenTargetWeight": SCREEN_TARGET_WEIGHT,
            "biharmonicWeight": BIHARMONIC_WEIGHT,
            "cgMaximumIterations": CG_MAX_ITERATIONS,
            "cgRelativeTolerance": CG_RELATIVE_TOLERANCE,
            "solver": solver,
            "topologyChangingOperations": 0,
        },
        "edit": {
            "modifiedVertexCount": int((vertex_delta > 1.0e-9).sum()),
            "editCoreVertexCount": int(edit_core.sum()),
            "editFieldNonzeroVertexCount": int((edit_field > 0.0).sum()),
            "maximumVertexDelta": float(vertex_delta.max()),
            "meanModifiedVertexDelta": float(vertex_delta[vertex_delta > 1.0e-9].mean()),
        },
        "preservation": preservation,
        "profileSamples": profile_samples(
            grid, source_width, source_depth, final_width, final_depth
        ),
        "transitionLineDeviation": transition,
        "outlineRoughness": outline,
        "torsoEllipseResidual": {
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
        raise RuntimeError("r12 torso/leg fair QA failed: " + json.dumps(report, separators=(",", ":")))

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
    scene["source_revision"] = "r11"
    scene["user_visual_approval_recorded"] = False
    bpy.context.preferences.filepaths.save_version = 0
    bpy.ops.wm.save_as_mainfile(filepath=blend_path, compress=True)
    print("R12_REPORT=" + json.dumps(report, separators=(",", ":")))
    print("R12_GENERATION_RESULT=" + report["technicalResult"])


if __name__ == "__main__":
    main()
