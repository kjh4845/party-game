"""Encode PNG frame sequences into an H.264 MP4 with Blender's built-in FFmpeg.

Blender --background --python this.py -- output.mp4 fps [--grid columns] dir1 [dir2 ...]
Each directory holds Frame_0000.png... (or any sorted *.png). With several
directories the clips are tiled side by side (--grid sets the column count),
frame by frame, so before/after captures stay in sync.
"""
import sys
from pathlib import Path

import bpy
import numpy as np

args = sys.argv[sys.argv.index('--') + 1:]
output, fps = Path(args[0]).resolve(), int(args[1])
rest = args[2:]
columns = None
if rest[:1] == ['--grid']:
    columns, rest = int(rest[1]), rest[2:]
dirs = [Path(d) for d in rest]
sequences = [sorted(d.glob('*.png')) for d in dirs]
count = min(len(s) for s in sequences)
columns = columns or len(dirs)
rows = (len(dirs) + columns - 1) // columns
probe = bpy.data.images.load(str(sequences[0][0]))
w, h = probe.size
bpy.data.images.remove(probe)
tmp = output.parent / (output.stem + '_tiles')
tmp.mkdir(parents=True, exist_ok=True)
for i in range(count):
    if (tmp / f'{i:04d}.png').exists():
        continue
    sheet = np.zeros((rows * h, columns * w, 4), np.float32)
    for k, seq in enumerate(sequences):
        img = bpy.data.images.load(str(seq[i]))
        px = np.array(img.pixels[:], np.float32).reshape(img.size[1], img.size[0], 4)
        bpy.data.images.remove(img)
        r, c = divmod(k, columns)
        y0 = (rows - 1 - r) * h
        sheet[y0:y0 + px.shape[0], c * w:c * w + px.shape[1]] = px
    out = bpy.data.images.new('tile', columns * w, rows * h, alpha=False)
    out.pixels = sheet.ravel()
    out.filepath_raw = str(tmp / f'{i:04d}.png')
    out.file_format = 'PNG'
    out.save()
    bpy.data.images.remove(out)

scene = bpy.context.scene
scene.sequence_editor_create()
strips = scene.sequence_editor.strips if hasattr(scene.sequence_editor, 'strips') else scene.sequence_editor.sequences
files = sorted(tmp.glob('*.png'))
strip = strips.new_image('frames', str(files[0]), 1, 1)
for f in files[1:]:
    strip.elements.append(f.name)
scene.frame_start, scene.frame_end = 1, len(files)
scene.render.fps = fps
scene.render.resolution_x, scene.render.resolution_y = columns * w, rows * h
scene.render.resolution_percentage = 100
settings = scene.render.image_settings
if hasattr(settings, 'media_type'):
    settings.media_type = 'VIDEO'  # Blender 5.x: video output is a media type
settings.file_format = 'FFMPEG'
scene.render.ffmpeg.format = 'MPEG4'
scene.render.ffmpeg.codec = 'H264'
scene.render.ffmpeg.constant_rate_factor = 'HIGH'
scene.render.filepath = str(output)
bpy.ops.render.render(animation=True)
for f in files:
    f.unlink()
tmp.rmdir()
print('wrote', output, len(files), 'frames')
