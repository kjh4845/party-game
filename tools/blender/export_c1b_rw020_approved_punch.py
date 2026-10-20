#!/usr/bin/env python3
"""Read the approved punch and export local review samples without saving the source."""
import hashlib
import json
from pathlib import Path
import struct
import sys

import bpy
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]


def sha(path):
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def vector(v):
    return dict(zip(('x', 'y', 'z'), v))


def main():
    out = Path(sys.argv[sys.argv.index('--') + 1]).resolve()
    if out.exists():
        raise RuntimeError('Preserve previous exports')
    approval = json.loads((ROOT / 'artifacts/reports/R20_PUNCH_VISUAL_APPROVAL_20260905.json').read_text())
    source = ROOT / approval['candidatePath']
    assert approval['status'] == 'APPROVED' and sha(source) == approval['candidateSha256']
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    rig = bpy.data.objects['RIG_C1B_R20_Armature']
    c = Matrix(((1, 0, 0, 0), (0, 0, 1, 0), (0, -1, 0, 0), (0, 0, 0, 1)))
    ci = c.inverted()
    transport = ROOT / '.codex_tmp/unity-physics-jump-20260905/ApprovedCharacter.bytes'
    with transport.open('rb') as f:
        header = json.loads(f.read(struct.unpack('<i', f.read(4))[0]))
    maximum_rest_error = 0
    for record in header['bones']:
        bone = rig.data.bones[record['name']]
        parent = bone.parent.matrix_local if bone.parent else Matrix.Identity(4)
        local = c @ parent.inverted() @ bone.matrix_local @ ci
        maximum_rest_error = max(maximum_rest_error, max(abs(local.translation[i] - record['position'][i]) for i in range(3)))
        q = local.to_quaternion()
        dot = abs(sum(a * b for a, b in zip((q.x, q.y, q.z, q.w), record['rotation'])))
        assert dot > .999999
    assert maximum_rest_error < 1e-6
    clips = []
    for side in ('L', 'R'):
        rig.animation_data.action = None
        for pb in rig.pose.bones:
            if not pb.name.startswith('HipDeform_'):
                pb.matrix_basis = Matrix.Identity(4)
                pb.rotation_mode = 'QUATERNION'
        # Match the approved candidate's non-keyed arms-down base pose.
        from mathutils import Euler
        for name in ('UpperArm_L', 'UpperArm_R'):
            rig.pose.bones[name].rotation_quaternion = Euler((-85 * 3.141592653589793 / 180, 0, 0), 'XYZ').to_quaternion()
        action = bpy.data.actions[f'REVIEW_ElbowPunch_{side}_r02']
        names = [f'{part}_{side}' for part in ('Clavicle', 'UpperArm', 'Forearm', 'HandLogical')]
        curves = action.layers[0].strips[0].channelbag(action.slots[0]).fcurves
        assert {curve.data_path for curve in curves} == {f'pose.bones["{name}"].rotation_quaternion' for name in names}
        start, end = action.frame_range
        duration = (end - start) / 30
        assert abs(duration - .7) < 1e-6
        rig.animation_data.action = action
        rig.animation_data.action_slot = action.slots[0]
        frames = []
        for index in range(85):
            time = index / 120
            frame = start + time * 30
            bpy.context.scene.frame_set(int(frame), subframe=frame - int(frame))
            bpy.context.view_layer.update()
            rotations, heads, tails = [], [], []
            for name in names:
                bone = rig.pose.bones[name]
                local = c @ bone.parent.matrix.inverted() @ bone.matrix @ ci
                q = local.to_quaternion().normalized()
                rotations.append(dict(zip(('x', 'y', 'z', 'w'), (q.x, q.y, q.z, q.w))))
                heads.append(vector(c @ bone.head))
                tails.append(vector(c @ bone.tail))
            frames.append({'time': time, 'rotations': rotations, 'heads': heads, 'tails': tails})
        clips.append({'side': side, 'duration': duration, 'bones': names, 'frames': frames})
    assert sha(source) == approval['candidateSha256']
    out.parent.mkdir(parents=True, exist_ok=True)
    out.write_text(json.dumps({'version': 1, 'sourceSha256': approval['candidateSha256'], 'sampleRate': 120,
                              'maximumRestPositionError': maximum_rest_error, 'sourceSaved': False, 'clips': clips}, indent=2) + '\n')
    print(json.dumps({'output': str(out), 'sha256': sha(out), 'sourceUnchanged': True,
                      'restError': maximum_rest_error, 'clips': 2, 'samplesPerClip': 85}), flush=True)


if __name__ == '__main__':
    main()
