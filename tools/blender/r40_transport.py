"""Read skin transports and pose them like the Unity runtime (numpy + mathutils).

Shared by the r40 render/audit/corrective tools.
- Recorded palettes (Unity skin matrices) were all captured with T-pose bound
  24-bone characters; they are converted to bone world matrices through the
  r34 header and re-bound to the transport's own rest pose.
- ShoulderBlend helpers follow the runtime rule (half way between the chest
  frame and the upper arm), from the transport's helper rest.
- Surface pose correctives (version 3) use the runtime weighting.
"""
import json
import struct
from pathlib import Path

import numpy as np
from mathutils import Matrix, Quaternion, Vector

ROOT = Path(__file__).resolve().parents[2]
R34 = ROOT / 'BlenderSource/Characters/C1B-RW-034-fair-body/FairBodyCharacter.bytes'


def quat(m):
    return Matrix(np.asarray(m)[:3, :3].tolist()).to_quaternion()


def angle(a, b):
    return np.degrees(2 * np.arccos(min(1, abs(a.dot(b)))))


def bone_world(bones):
    out = []
    for b in bones:
        x, y, z, w = b['rotation']
        m = Quaternion((w, x, y, z)).to_matrix().to_4x4()
        m.translation = Vector(b['position'])
        out.append(m if b['parent'] < 0 else out[b['parent']] @ m)
    return np.array([np.array(m) for m in out])


def read_header(path):
    data = Path(path).read_bytes()
    size = struct.unpack_from('<i', data)[0]
    return json.loads(data[4:4 + size]), data, 4 + size


class Transport:
    def __init__(self, path):
        self.header, data, offset = read_header(path)
        self.names = [b['name'] for b in self.header['bones']]
        self.index = {n: i for i, n in enumerate(self.names)}
        self.meshes = []
        for record in self.header['meshes']:
            n, t = record['vertices'], record['triangleIndices']
            mesh = dict(name=record['name'],
                        rest=np.frombuffer(data, '<f4', n * 3, offset).reshape(n, 3).astype(np.float64),
                        normals=np.frombuffer(data, '<f4', n * 3, offset + n * 12).reshape(n, 3).astype(np.float64),
                        ids=np.frombuffer(data, '<i4', n * 4, offset + n * 24).reshape(n, 4),
                        weights=np.frombuffer(data, '<f4', n * 4, offset + n * 40).reshape(n, 4).astype(np.float64),
                        tri=np.frombuffer(data, '<i4', t, offset + n * 56).reshape(-1, 3))
            self.meshes.append(mesh)
            offset += n * 56 + t * 4
        self.surface = []
        if self.header['version'] == 3:
            n = self.meshes[0]['rest'].shape[0]
            for ref in self.header['surfacePoses']:
                delta = None
                if ref['hasDelta']:
                    delta = np.frombuffer(data, '<f4', n * 3, offset).reshape(n, 3).astype(np.float64)
                    offset += n * 24
                self.surface.append((ref, delta))
        self.bind = bone_world(self.header['bones'])
        reference = read_header(R34)[0]
        self.recorded_bind = bone_world(reference['bones'])
        self.recorded_names = [b['name'] for b in reference['bones']]

    # --- poses -------------------------------------------------------------
    def world_from_recorded(self, path):
        raw = Path(path).read_bytes()
        palette = np.frombuffer(raw, '<f4', offset=4).reshape(-1, 4, 4).astype(np.float64)
        palette = np.linalg.inv(palette[0]) @ palette
        recorded = palette @ self.recorded_bind[:len(palette)]
        # Bones the recording did not have (extra skin helpers) are derived by with_helpers().
        world = np.repeat(np.eye(4)[None], len(self.names), 0)
        for i, name in enumerate(self.recorded_names[:len(palette)]):
            if name in self.index:
                world[self.index[name]] = recorded[i]
        return world

    def world_from_own_recording(self, path):
        """Palette captured from this same transport in Unity (skin = world @ inverse bind)."""
        raw = Path(path).read_bytes()
        palette = np.frombuffer(raw, '<f4', offset=4).reshape(-1, 4, 4).astype(np.float64)
        palette = np.linalg.inv(palette[0]) @ palette
        return palette @ self.bind

    def retarget(self, recorded_world):
        """Recorded bone rotations on this rig's own bone lengths (for rigs with moved joints)."""
        out = np.zeros_like(recorded_world)
        for i, b in enumerate(self.header['bones']):
            p = b['parent']
            if p < 0:
                out[i] = recorded_world[i]
                continue
            local = np.linalg.inv(self.bind[p]) @ self.bind[i]
            m = np.eye(4)
            m[:3, :3] = np.linalg.inv(recorded_world[p][:3, :3]) @ recorded_world[i][:3, :3]
            m[:3, 3] = local[:3, 3]
            out[i] = out[p] @ m
        return out

    def idle_world(self):
        """Bind pose with both upper arms turned to the header idleArmDirection (A-pose binds)."""
        world = self.rest_world()
        d = self.header.get('idleArmDirection')
        if not d:
            return world
        local = [np.linalg.inv(self.bind[b['parent']]) @ self.bind[i] if b['parent'] >= 0 else self.bind[i]
                 for i, b in enumerate(self.header['bones'])]
        for s, sign in (('L', -1), ('R', 1)):
            u, f = self.index['UpperArm_' + s], self.index['Forearm_' + s]
            target = Vector((sign * abs(d[0]), d[1], d[2])).normalized()
            current = Vector(world[f, :3, 3] - world[u, :3, 3]).normalized()
            m = np.array(current.rotation_difference(target).to_matrix().to_4x4())
            world[u, :3, :3] = m[:3, :3] @ world[u, :3, :3]
        for i, b in enumerate(self.header['bones']):
            if b['parent'] >= 0 and not b['name'].startswith('UpperArm_'):
                world[i] = world[b['parent']] @ local[i]
        return world

    def rest_world(self):
        return self.bind.copy()

    def with_helpers(self, world):
        world = world.copy()
        I = self.index
        helpers = self.header.get('shoulderHelpers') or [{'name': 'ShoulderBlend_' + s, 'fraction': .5} for s in 'LR']
        for helper in helpers:
            name = helper['name']
            if name not in I:
                continue
            side = name[-1]
            h, c, u = I[name], I['Chest'], I['UpperArm_' + side]
            rest_local = np.linalg.inv(self.bind[c]) @ self.bind[h]
            rot = quat(world[c] @ rest_local).slerp(quat(world[u]), helper['fraction'])
            m = rot.to_matrix().to_4x4()
            m.translation = Vector(world[u, :3, 3])
            world[h] = np.array(m)
        return world

    def correction(self, world):
        rest = self.meshes[0]['rest']
        total = np.zeros_like(rest)
        if not self.surface:
            return total
        I = self.index
        for side in range(2):
            s = 'LR'[side]
            c, u, f, sp = I['Chest'], I['UpperArm_' + s], I['Forearm_' + s], I['Spine']
            shoulder = quat(np.linalg.inv(world[c]) @ world[u])
            elbow = quat(np.linalg.inv(world[u]) @ world[f])
            torso = quat(np.linalg.inv(world[sp]) @ world[c])
            rows = []
            for ref, delta in self.surface:
                if ref['side'] != side:
                    continue
                q, e, tq = ref['rotation'], ref['elbowRotation'], ref.get('torsoRotation', [0, 0, 0, 1])
                da = angle(shoulder, Quaternion((q[3], *q[:3])))
                db = angle(elbow, Quaternion((e[3], *e[:3])))
                dc = angle(torso, Quaternion((tq[3], *tq[:3])))
                mode = ref.get('mode', 0)
                if mode == 1:
                    sq = da * da
                elif mode == 2:
                    sq = db * db
                else:
                    sq = da * da + .5 * db * db + 4 * dc * dc
                rows.append((delta, sq, 1 / max(.0025, sq) ** 2, mode))
            # Each mode is an independent interpolation group (runtime rule).
            for mode in (0, 1, 2):
                group = [r for r in rows if r[3] == mode]
                if not group:
                    continue
                weight = sum(r[2] for r in group)
                x = np.clip((np.sqrt(min(r[1] for r in group)) - 45) / 40, 0, 1)
                coverage = 1 - x * x * (3 - 2 * x)
                for delta, _, value, _ in group:
                    if delta is not None:
                        total += delta * (coverage * value / weight)
        return total

    def palette(self, world):
        return self.with_helpers(world) @ np.linalg.inv(self.bind)

    def skin(self, world, mesh=0, correct=True, dqs=False):
        m = self.meshes[mesh]
        palette = self.palette(world)
        src = m['rest'] + (self.correction(world) if (correct and mesh == 0) else 0)
        if dqs:
            return dual_quaternion_skin(palette, m['ids'], m['weights'], src)
        t = (palette[m['ids']] * m['weights'][:, :, None, None]).sum(1)
        return np.einsum('nij,nj->ni', t[:, :3, :3], src) + t[:, :3, 3]


    def synthetic(self, side, elevation, azimuth, flex=0.0, twist=0.0):
        """Upper arm swung from the idle direction in the chest frame (Unity axes)."""
        I = self.index
        s = 'LR'[side]
        sign = -1 if s == 'L' else 1
        local = [np.linalg.inv(self.bind[b['parent']]) @ self.bind[i] if b['parent'] >= 0 else self.bind[i]
                 for i, b in enumerate(self.header['bones'])]
        u, f = I['UpperArm_' + s], I['Forearm_' + s]
        d = self.header.get('idleArmDirection')
        idle = np.array((sign * abs(d[0]), d[1], d[2])) if d else np.array((sign * .087, -1, 0))
        idle /= np.linalg.norm(idle)
        current = self.bind[f, :3, 3] - self.bind[u, :3, 3]
        current /= np.linalg.norm(current)
        e, a = np.radians(elevation), np.radians(azimuth)
        forward, out = np.array((0, 0, 1)), np.array((sign, 0, 0))
        target = idle * np.cos(e) + np.sin(e) * (forward * np.cos(a) + out * np.sin(a))
        target /= np.linalg.norm(target)
        swing = Vector(current).rotation_difference(Vector(target))
        rot = (Quaternion(Vector(target), np.radians(twist)) @ swing).to_matrix() @ Matrix(self.bind[u][:3, :3].tolist())
        base = self.idle_world()
        world = [None] * len(local)
        for i, b in enumerate(self.header['bones']):
            if b['name'].startswith('UpperArm_') and i != u:
                world[i] = base[i]
                continue
            if i == u:
                m = rot.to_4x4()
                m.translation = Vector(self.bind[u, :3, 3])
                world[i] = np.array(m)
                continue
            lm = local[i]
            if i == f and flex:
                # Physical elbow hinge: forearm local +Y (L) / -Y (R), measured from Unity captures.
                axis = (0, 1, 0) if s == 'L' else (0, -1, 0)
                lm = lm @ np.array(Quaternion(axis, np.radians(flex)).to_matrix().to_4x4())
            world[i] = lm if b['parent'] < 0 else world[b['parent']] @ lm
        return np.array(world)



def _qmul(a, b):
    w1, x1, y1, z1 = np.moveaxis(a, -1, 0)
    w2, x2, y2, z2 = np.moveaxis(b, -1, 0)
    return np.stack((w1 * w2 - x1 * x2 - y1 * y2 - z1 * z2, w1 * x2 + x1 * w2 + y1 * z2 - z1 * y2,
                     w1 * y2 - x1 * z2 + y1 * w2 + z1 * x2, w1 * z2 + x1 * y2 - y1 * x2 + z1 * w2), -1)


def dual_quaternion_skin(palette, ids, weights, points):
    real = np.array([list(quat(m)) for m in palette])  # (w, x, y, z)
    trans = np.concatenate((np.zeros((len(palette), 1)), palette[:, :3, 3]), 1)
    dual = .5 * _qmul(trans, real)
    r = real[ids]                      # n x 4 x 4
    d = dual[ids]
    sign = np.sign((r * r[:, :1]).sum(-1, keepdims=True))
    sign[sign == 0] = 1
    br = (r * sign * weights[:, :, None]).sum(1)
    bd = (d * sign * weights[:, :, None]).sum(1)
    norm = np.linalg.norm(br, axis=1, keepdims=True)
    br, bd = br / norm, bd / norm
    w, v = br[:, :1], br[:, 1:]
    rotated = points + 2 * np.cross(v, np.cross(v, points) + w * points)
    conj = br * np.array((1, -1, -1, -1))
    t = 2 * _qmul(bd, conj)[:, 1:]
    return rotated + t


def idle_bones(bones):
    """Header bones with the upper arms at the runtime idle direction (bind in idle).

    Matches PhysicsJumpReview.LoadSkin: FromToRotation(forearm - upper, (+-.087, -1, 0)).
    ShoulderBlend rest follows the idle upper arm so the helper is identity at idle.
    """
    bones = json.loads(json.dumps(bones))
    names = [b['name'] for b in bones]
    world = bone_world(bones)
    for side, sign in (('L', -1), ('R', 1)):
        u, f = names.index('UpperArm_' + side), names.index('Forearm_' + side)
        parent = bones[u]['parent']
        target = Vector((sign * .087, -1, 0)).normalized()
        current = Vector(world[f, :3, 3] - world[u, :3, 3]).normalized()
        new_world = current.rotation_difference(target) @ quat(world[u])
        local = quat(world[parent]).inverted() @ new_world
        bones[u]['rotation'] = [local.x, local.y, local.z, local.w]
        for hi, b in enumerate(bones):
            if not (b['name'].startswith('ShoulderBlend') and b['name'].endswith('_' + side)):
                continue
            chest = b['parent']
            hl = quat(world[chest]).inverted() @ new_world
            pos = np.linalg.inv(world[chest]) @ np.append(world[u, :3, 3], 1)
            b['rotation'] = [hl.x, hl.y, hl.z, hl.w]
            b['position'] = [float(v) for v in pos[:3]]
    return bones
