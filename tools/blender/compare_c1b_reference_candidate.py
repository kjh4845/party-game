#!/usr/bin/env python3

import json
import os
import sys

import bpy
import numpy as np


GRID = 1000
REFERENCE_CROPS = {
    "Front": (60, 100, 560, 850),
    "Side": (640, 100, 1020, 850),
}


def args():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <reference> <candidate-front> <candidate-side>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 3:
        raise RuntimeError("expected reference, front candidate and side candidate")
    return [os.path.abspath(value) for value in values]


def luminance(path):
    image = bpy.data.images.load(path, check_existing=False)
    width, height = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(height, width, 4)
    return np.flipud(pixels[:, :, :3].mean(axis=2))


def bounds(mask):
    ys, xs = np.nonzero(mask)
    if not len(xs):
        raise RuntimeError("empty silhouette mask")
    return int(xs.min()), int(ys.min()), int(xs.max()), int(ys.max())


def normalize(mask, box):
    left, top, right, bottom = box
    height = bottom - top + 1
    center_x = (left + right) * 0.5
    output_y, output_x = np.indices((GRID, GRID))
    source_x = np.rint(center_x + (output_x - GRID * 0.5) * height / GRID).astype(np.int32)
    source_y = np.rint(bottom - output_y * height / (GRID - 1)).astype(np.int32)
    valid = (
        (source_x >= 0)
        & (source_x < mask.shape[1])
        & (source_y >= 0)
        & (source_y < mask.shape[0])
    )
    result = np.zeros((GRID, GRID), dtype=bool)
    result[valid] = mask[source_y[valid], source_x[valid]]
    return result


def row_runs(row):
    indices = np.flatnonzero(row)
    if not len(indices):
        return []
    cuts = np.where(np.diff(indices) > 1)[0]
    starts = np.r_[indices[0], indices[cuts + 1]]
    ends = np.r_[indices[cuts], indices[-1]]
    return [(int(start), int(end)) for start, end in zip(starts, ends) if end - start >= 2]


def compare(reference, candidate):
    intersection = np.logical_and(reference, candidate).sum()
    union = np.logical_or(reference, candidate).sum()
    symmetric_difference = np.logical_xor(reference, candidate).sum()
    row_errors = []
    run_samples = []
    for height_h in (0.10, 0.20, 0.30, 0.35, 0.40, 0.45, 0.50, 0.55, 0.60, 0.70, 0.80, 0.90):
        row_index = int(round(height_h * (GRID - 1)))
        reference_runs = row_runs(reference[row_index])
        candidate_runs = row_runs(candidate[row_index])
        run_samples.append(
            {
                "heightH": height_h,
                "referenceRunsH": [[round((a - GRID * 0.5) / GRID, 4), round((b - GRID * 0.5) / GRID, 4)] for a, b in reference_runs],
                "candidateRunsH": [[round((a - GRID * 0.5) / GRID, 4), round((b - GRID * 0.5) / GRID, 4)] for a, b in candidate_runs],
            }
        )
    for row_index in range(GRID):
        reference_indices = np.flatnonzero(reference[row_index])
        candidate_indices = np.flatnonzero(candidate[row_index])
        if len(reference_indices) and len(candidate_indices):
            row_errors.extend(
                (
                    abs(int(reference_indices.min()) - int(candidate_indices.min())) / GRID,
                    abs(int(reference_indices.max()) - int(candidate_indices.max())) / GRID,
                )
            )
    return {
        "iou": round(float(intersection / union), 6),
        "symmetricDifferenceRatio": round(float(symmetric_difference / union), 6),
        "outerBoundaryP95H": round(float(np.percentile(row_errors, 95)), 6),
        "runSamples": run_samples,
    }


def main():
    reference_path, front_path, side_path = args()
    reference_luma = luminance(reference_path)
    candidate_luma = {"Front": luminance(front_path), "Side": luminance(side_path)}
    report = {}
    for view in ("Front", "Side"):
        left, top, right, bottom = REFERENCE_CROPS[view]
        reference_mask = reference_luma >= 0.50
        local = np.zeros_like(reference_mask)
        local[top:bottom, left:right] = reference_mask[top:bottom, left:right]
        reference_box = bounds(local)
        candidate_mask = candidate_luma[view] <= 0.30
        candidate_box = bounds(candidate_mask)
        report[view] = {
            "referenceBounds": reference_box,
            "candidateBounds": candidate_box,
            **compare(normalize(local, reference_box), normalize(candidate_mask, candidate_box)),
        }
    print("C1B_REFERENCE_COMPARISON=" + json.dumps(report, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
