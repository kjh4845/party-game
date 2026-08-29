#!/usr/bin/env python3

import hashlib
import json
import os
import sys

from PIL import Image, ImageChops


MAXIMUM_CHANNEL_DIFFERENCE = 1
MAXIMUM_CHANGED_CHANNEL_RATIO = 0.000001


def pixel_sha256(path):
    with Image.open(path) as image:
        converted = image.convert("RGB")
        prefix = f"RGB:{converted.width}x{converted.height}:".encode("ascii")
        return hashlib.sha256(prefix + converted.tobytes()).hexdigest(), [converted.width, converted.height]


def main():
    if len(sys.argv) != 3:
        raise SystemExit("usage: compare_c1b003_render_pixels.py EXPECTED_DIR ACTUAL_DIR")
    expected_directory = os.path.abspath(sys.argv[1])
    actual_directory = os.path.abspath(sys.argv[2])
    expected_names = sorted(name for name in os.listdir(expected_directory) if name.endswith(".png"))
    actual_names = sorted(name for name in os.listdir(actual_directory) if name.endswith(".png"))
    outputs = []
    for name in sorted(set(expected_names) | set(actual_names)):
        expected_path = os.path.join(expected_directory, name)
        actual_path = os.path.join(actual_directory, name)
        entry = {"name": name, "expectedExists": os.path.isfile(expected_path), "actualExists": os.path.isfile(actual_path)}
        if entry["expectedExists"] and entry["actualExists"]:
            expected_hash, expected_dimensions = pixel_sha256(expected_path)
            actual_hash, actual_dimensions = pixel_sha256(actual_path)
            with Image.open(expected_path) as expected_image, Image.open(actual_path) as actual_image:
                expected_rgb = expected_image.convert("RGB")
                actual_rgb = actual_image.convert("RGB")
                difference = ImageChops.difference(expected_rgb, actual_rgb)
                histogram = difference.histogram()
                channel_count = expected_rgb.width * expected_rgb.height * 3
                total_absolute_difference = sum((index % 256) * count for index, count in enumerate(histogram))
                changed_channels = sum(count for index, count in enumerate(histogram) if index % 256 != 0)
                maximum_channel_difference = max(
                    (index % 256 for index, count in enumerate(histogram) if count), default=0
                )
                mean_absolute_channel_difference = total_absolute_difference / channel_count
                changed_channel_ratio = changed_channels / channel_count
                within_tolerance = (
                    expected_dimensions == actual_dimensions
                    and maximum_channel_difference <= MAXIMUM_CHANNEL_DIFFERENCE
                    and changed_channel_ratio <= MAXIMUM_CHANGED_CHANNEL_RATIO
                )
            entry.update(
                {
                    "expectedPixelSha256": expected_hash,
                    "actualPixelSha256": actual_hash,
                    "expectedDimensions": expected_dimensions,
                    "actualDimensions": actual_dimensions,
                    "exactPixelMatch": expected_hash == actual_hash and expected_dimensions == actual_dimensions,
                    "maximumChannelDifference": maximum_channel_difference,
                    "meanAbsoluteChannelDifference": mean_absolute_channel_difference,
                    "changedChannelRatio": changed_channel_ratio,
                    "matches": within_tolerance,
                }
            )
        else:
            entry["matches"] = False
        outputs.append(entry)
    payload = {
        "expectedNames": expected_names,
        "actualNames": actual_names,
        "outputs": outputs,
        "tolerance": {
            "maximumChannelDifference": MAXIMUM_CHANNEL_DIFFERENCE,
            "maximumChangedChannelRatio": MAXIMUM_CHANGED_CHANNEL_RATIO,
        },
        "allMatch": expected_names == actual_names and all(entry["matches"] for entry in outputs),
    }
    print("C1B003_RENDER_REPRODUCTION_JSON=" + json.dumps(payload, sort_keys=True, separators=(",", ":")))


if __name__ == "__main__":
    main()
