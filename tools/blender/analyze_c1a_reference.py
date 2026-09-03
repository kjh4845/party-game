#!/usr/bin/env python3

import json
import os
import sys

import bpy
import numpy as np


THRESHOLD = 0.50
CROPS = {
    "Front": (60, 100, 560, 850),
    "Side": (640, 100, 1020, 850),
    "ThreeQuarter": (1090, 100, 1580, 850),
}
SAMPLE_HEIGHTS = (0.02, 0.08, 0.16, 0.24, 0.32, 0.40, 0.48, 0.56, 0.64, 0.72, 0.80, 0.88, 0.96)


def parse_path():
    if "--" not in sys.argv:
        raise RuntimeError("expected -- <reference-image>")
    values = sys.argv[sys.argv.index("--") + 1 :]
    if len(values) != 1:
        raise RuntimeError("expected one reference image")
    return os.path.abspath(values[0])


def runs(row):
    indices = np.flatnonzero(row)
    if not len(indices):
        return []
    breaks = np.where(np.diff(indices) > 1)[0]
    starts = np.r_[indices[0], indices[breaks + 1]]
    ends = np.r_[indices[breaks], indices[-1]]
    return [(int(start), int(end)) for start, end in zip(starts, ends) if end - start >= 3]


def analyze_crop(mask, crop):
    left, top, right, bottom = crop
    local = mask[top:bottom, left:right]
    ys, xs = np.nonzero(local)
    if not len(xs):
        raise RuntimeError(f"empty crop: {crop}")
    minimum_x = int(xs.min())
    maximum_x = int(xs.max())
    minimum_y = int(ys.min())
    maximum_y = int(ys.max())
    height = maximum_y - minimum_y + 1
    width = maximum_x - minimum_x + 1
    samples = []
    for normalized_height in SAMPLE_HEIGHTS:
        row_y = int(round(maximum_y - normalized_height * (height - 1)))
        row_runs = runs(local[row_y])
        samples.append(
            {
                "heightH": normalized_height,
                "row": top + row_y,
                "runsPx": [[left + start, left + end] for start, end in row_runs],
                "runsNormalizedToHeight": [
                    [round((start - minimum_x) / height, 6), round((end - minimum_x) / height, 6)]
                    for start, end in row_runs
                ],
            }
        )
    return {
        "threshold": THRESHOLD,
        "boundsPx": [left + minimum_x, top + minimum_y, left + maximum_x, top + maximum_y],
        "widthPx": width,
        "heightPx": height,
        "widthOverHeight": round(width / height, 6),
        "samples": samples,
    }


def main():
    path = parse_path()
    image = bpy.data.images.load(path, check_existing=False)
    width, height = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(height, width, 4)
    pixels = np.flipud(pixels)
    luminance = pixels[:, :, :3].mean(axis=2)
    mask = luminance >= THRESHOLD
    report = {
        "path": path,
        "size": [width, height],
        "threshold": THRESHOLD,
        "views": {name: analyze_crop(mask, crop) for name, crop in CROPS.items()},
    }
    print("C1A_REFERENCE_ANALYSIS=" + json.dumps(report, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
