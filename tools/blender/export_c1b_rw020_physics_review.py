#!/usr/bin/env python3
"""Write a review-only neutral skin transport; no FBX, Actions or source saves."""
import json
from pathlib import Path
import struct
import sys
import bpy
import numpy as np
from mathutils import Matrix

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT/'tools/blender'))
import create_c1b_rw020_lower_weight_candidate as w


def main():
    out = Path(sys.argv[sys.argv.index('--')+1]).resolve()
    if out.exists(): raise RuntimeError('preserve previous transport')
    approval = json.loads((ROOT/'artifacts/reports/R20_LOWER_WEIGHT_VISUAL_APPROVAL_20260905.json').read_text())
    source = ROOT/approval['candidatePath']; assert w.sha(source) == approval['candidateSha256']
    bpy.ops.wm.open_mainfile(filepath=str(source), load_ui=False)
    rig = bpy.data.objects['RIG_C1B_R20_Armature']; rig.animation_data.action = None
    for bone in rig.pose.bones:
        if not bone.name.startswith('HipDeform_'): bone.matrix_basis = Matrix.Identity(4)
    bpy.context.view_layer.update()
    # Direct review coordinate transport: Blender -Y forward/+Z up to Unity +Z/+Y.
    c = Matrix(((1,0,0,0),(0,0,1,0),(0,-1,0,0),(0,0,0,1)))
    bones = list(rig.data.bones); indices = {b.name:i for i,b in enumerate(bones)}
    bone_records = []
    for bone in bones:
        world = c @ bone.matrix_local @ c.inverted()
        parent = c @ bone.parent.matrix_local @ c.inverted() if bone.parent else Matrix.Identity(4)
        local = parent.inverted() @ world; q = local.to_quaternion()
        bone_records.append({'name':bone.name, 'parent':indices[bone.parent.name] if bone.parent else -1,
                             'position':list(local.translation), 'rotation':[q.x,q.y,q.z,q.w]})
    meshes = []; blocks = []
    for name in ('CHR_C1B_R20_SkinnedBody','CHR_C1B_R20_SkinnedHead'):
        obj = bpy.data.objects[name]; mesh = obj.data; mesh.calc_loop_triangles()
        positions = np.asarray([tuple(c @ (obj.matrix_world @ v.co)) for v in mesh.vertices],dtype='<f4')
        normals = np.asarray([tuple(c.to_3x3() @ v.normal) for v in mesh.vertices],dtype='<f4')
        triangles = np.asarray([tuple(t.vertices) for t in mesh.loop_triangles],dtype='<i4')
        bone_ids = np.zeros((len(mesh.vertices),4),dtype='<i4'); weights = np.zeros((len(mesh.vertices),4),dtype='<f4')
        for vertex in mesh.vertices:
            entries = [g for g in vertex.groups if g.weight>0]
            assert len(entries)<=4
            for slot,entry in enumerate(entries):
                bone_ids[vertex.index,slot]=indices[obj.vertex_groups[entry.group].name]
                weights[vertex.index,slot]=entry.weight
        assert np.max(np.abs(weights.sum(axis=1)-1))<1e-6
        meshes.append({'name':name,'vertices':len(mesh.vertices),'triangleIndices':triangles.size})
        blocks.extend([positions.tobytes(),normals.tobytes(),bone_ids.tobytes(),weights.tobytes(),triangles.tobytes()])
    header={'version':1,'sourceSha256':approval['candidateSha256'],'bones':bone_records,'meshes':meshes,'animationClips':0}
    encoded=json.dumps(header).encode('utf8');out.parent.mkdir(parents=True,exist_ok=True)
    with out.open('wb') as f:
        f.write(struct.pack('<i',len(encoded)));f.write(encoded)
        for block in blocks:f.write(block)
    assert w.sha(source)==approval['candidateSha256']
    report={'sourcePath':str(source),'sourceSha256':approval['candidateSha256'],'transportPath':str(out),'transportSha256':w.sha(out),'generatorSha256':w.sha(__file__),'bones':len(bones),'meshes':meshes,'sourceSaved':False,'actionsExported':0,'scope':'Local Unity Editor physics review transport; not canonical model/FBX parity approval.'}
    out.with_suffix('.json').write_text(json.dumps(report,indent=2)+'\n')
    print(json.dumps(report),flush=True)


if __name__=='__main__':main()
