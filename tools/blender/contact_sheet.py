"""Tile PNG renders into one contact sheet.

Blender --background --python this.py -- output.png columns scale image.png ...
"""
import sys

import bpy
import numpy as np

args = sys.argv[sys.argv.index('--') + 1:]
output, columns, scale, paths = args[0], int(args[1]), float(args[2]), args[3:]
tiles = []
for path in paths:
    image = bpy.data.images.load(path)
    w, h = image.size
    pixels = np.array(image.pixels[:], dtype=np.float32).reshape(h, w, 4)
    step = max(1, int(round(1 / scale)))
    tiles.append(pixels[::step, ::step])
    bpy.data.images.remove(image)
h = max(t.shape[0] for t in tiles)
w = max(t.shape[1] for t in tiles)
rows = (len(tiles) + columns - 1) // columns
sheet = np.ones((rows * h, columns * w, 4), dtype=np.float32)
for i, tile in enumerate(tiles):
    r, c = divmod(i, columns)
    y0 = (rows - 1 - r) * h  # Blender pixel rows start at the bottom.
    sheet[y0 + h - tile.shape[0]:y0 + h, c * w:c * w + tile.shape[1]] = tile
image = bpy.data.images.new('sheet', columns * w, rows * h, alpha=True)
image.pixels = sheet.ravel()
image.filepath_raw = output
image.file_format = 'PNG'
image.save()
