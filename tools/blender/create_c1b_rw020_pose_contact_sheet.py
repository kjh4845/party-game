#!/usr/bin/env python3

"""Build a labeled contact sheet from the r20 temporary pose-test report."""

import json
import os
import sys

from PIL import Image, ImageDraw, ImageFont


COLS = 4
ROWS = 2
CELL_WIDTH = 600
IMAGE_HEIGHT = 600
LABEL_HEIGHT = 54
MARGIN = 24
GAP = 16
BACKGROUND = (31, 31, 31)
LABEL_BACKGROUND = (45, 45, 45)
TEXT = (242, 242, 242)


def require(condition, message):
    if not condition:
        raise RuntimeError(message)


def load_font(size):
    candidates = (
        "/System/Library/Fonts/Supplemental/Arial.ttf",
        "/Library/Fonts/Arial.ttf",
        "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf",
    )
    for path in candidates:
        if os.path.isfile(path):
            return ImageFont.truetype(path, size)
    return ImageFont.load_default()


def main():
    require(len(sys.argv) == 3, "usage: script <PoseTestReport.json> <output.png>")
    report_path = os.path.abspath(sys.argv[1])
    output_path = os.path.abspath(sys.argv[2])
    with open(report_path, "r", encoding="utf-8") as stream:
        report = json.load(stream)
    poses = report.get("poses", [])
    require(report.get("result") == "PASS", "pose-test report did not pass")
    require(len(poses) == COLS * ROWS, "expected exactly eight pose renders")

    width = MARGIN * 2 + COLS * CELL_WIDTH + (COLS - 1) * GAP
    height = MARGIN * 2 + ROWS * (IMAGE_HEIGHT + LABEL_HEIGHT) + (ROWS - 1) * GAP
    sheet = Image.new("RGB", (width, height), BACKGROUND)
    draw = ImageDraw.Draw(sheet)
    font = load_font(28)

    for index, pose in enumerate(poses):
        row, col = divmod(index, COLS)
        x = MARGIN + col * (CELL_WIDTH + GAP)
        y = MARGIN + row * (IMAGE_HEIGHT + LABEL_HEIGHT + GAP)
        image_path = pose["image"]["path"]
        require(os.path.isfile(image_path), f"pose image missing: {image_path}")
        with Image.open(image_path) as source:
            preview = source.convert("RGB").resize(
                (CELL_WIDTH, IMAGE_HEIGHT), Image.Resampling.LANCZOS
            )
        sheet.paste(preview, (x, y))
        draw.rectangle(
            (x, y + IMAGE_HEIGHT, x + CELL_WIDTH, y + IMAGE_HEIGHT + LABEL_HEIGHT),
            fill=LABEL_BACKGROUND,
        )
        label = f"{index + 1:02d}  {pose['label']}"
        box = draw.textbbox((0, 0), label, font=font)
        text_y = y + IMAGE_HEIGHT + (LABEL_HEIGHT - (box[3] - box[1])) // 2 - box[1]
        draw.text((x + 18, text_y), label, fill=TEXT, font=font)

    os.makedirs(os.path.dirname(output_path), exist_ok=True)
    sheet.save(output_path, format="PNG", optimize=True)
    print(output_path)


if __name__ == "__main__":
    main()
