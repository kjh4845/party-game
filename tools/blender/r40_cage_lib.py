"""Shared helpers for the r40 control-cage body.

The body is a low resolution all-quad control cage. Smoothness comes from
Catmull-Clark subdivision (a linear operator on cage positions), so shape,
skin weights and pose correctives are all authored on the cage and carried
through the same matrix. Coordinates are Blender space: +Z up, front is -Y.
"""
import numpy as np

TORSO_SEGMENTS = 24


def superellipse(a, b, phi, exponent):
    s, c = np.sin(phi), np.cos(phi)
    return (a * np.sign(s) * np.abs(s) ** (2 / exponent), -b * np.sign(c) * np.abs(c) ** (2 / exponent))


class Cage:
    def __init__(self):
        self.verts = []
        self.faces = []
        self.tags = []  # per vertex dict: region information used for weights

    def add(self, p, **tag):
        self.verts.append(np.asarray(p, dtype=np.float64))
        self.tags.append(tag)
        return len(self.verts) - 1

    def quad(self, a, b, c, d):
        self.faces.append((a, b, c, d))

    def bridge(self, ring_a, ring_b):
        n = len(ring_a)
        for i in range(n):
            j = (i + 1) % n
            self.quad(ring_a[i], ring_a[j], ring_b[j], ring_b[i])

    def cap(self, boundary, m, n, apex_offset, **tag):
        """Close a loop of 2(m+n) vertices with an m x n quad grid (Coons patch)."""
        assert len(boundary) == 2 * (m + n)
        grid = {}
        idx = 0
        for i in range(m + 1):  # side 0: (i, 0)
            grid[(i, 0)] = boundary[idx]; idx += 1
        idx -= 1
        for j in range(n + 1):  # side 1: (m, j)
            grid[(m, j)] = boundary[idx]; idx += 1
        idx -= 1
        for i in range(m, -1, -1):  # side 2: (i, n)
            grid[(i, n)] = boundary[idx % len(boundary)]; idx += 1
        idx -= 1
        for j in range(n, -1, -1):  # side 3: (0, j)
            grid[(0, j)] = boundary[idx % len(boundary)]; idx += 1
        P = lambda k: self.verts[grid[k]]
        for i in range(1, m):
            for j in range(1, n):
                u, v = i / m, j / n
                coons = ((1 - v) * P((i, 0)) + v * P((i, n)) + (1 - u) * P((0, j)) + u * P((m, j))
                         - ((1 - u) * (1 - v) * P((0, 0)) + u * (1 - v) * P((m, 0)) + (1 - u) * v * P((0, n))
                            + u * v * P((m, n))))
                bump = np.sin(np.pi * u) * np.sin(np.pi * v)
                grid[(i, j)] = self.add(coons + bump * np.asarray(apex_offset), **tag)
        for i in range(m):
            for j in range(n):
                self.quad(grid[(i, j)], grid[(i + 1, j)], grid[(i + 1, j + 1)], grid[(i, j + 1)])
        return grid


def catmull_clark_operator(n_verts, faces):
    """One Catmull-Clark level for a closed quad mesh: returns (matrix, new_faces)."""
    edges = {}
    edge_faces = {}
    vert_faces = [[] for _ in range(n_verts)]
    vert_edges = [set() for _ in range(n_verts)]
    for fi, f in enumerate(faces):
        for k in range(4):
            a, b = f[k], f[(k + 1) % 4]
            key = (min(a, b), max(a, b))
            if key not in edges:
                edges[key] = len(edges)
            edge_faces.setdefault(key, []).append(fi)
            vert_edges[a].add(key); vert_edges[b].add(key)
            vert_faces[a].append(fi)
    for key, fs in edge_faces.items():
        if len(fs) != 2:
            raise ValueError(f'non-manifold or boundary edge {key}: {fs}')
    nf, ne = len(faces), len(edges)
    M = np.zeros((n_verts + nf + ne, n_verts))
    face_rows = np.zeros((nf, n_verts))
    for fi, f in enumerate(faces):
        face_rows[fi, list(f)] = .25
    M[n_verts:n_verts + nf] = face_rows
    for key, ei in edges.items():
        row = M[n_verts + nf + ei]
        row[key[0]] += .25; row[key[1]] += .25
        for fi in edge_faces[key]:
            row += .25 * face_rows[fi]
    for v in range(n_verts):
        k = len(vert_edges[v])
        q = face_rows[vert_faces[v]].mean(0)
        r = np.zeros(n_verts)
        for a, b in vert_edges[v]:
            r[a] += .5 / k; r[b] += .5 / k
        p = np.zeros(n_verts); p[v] = 1
        M[v] = (q + 2 * r + (k - 3) * p) / k
    new_faces = []
    for fi, f in enumerate(faces):
        fp = n_verts + fi
        for k in range(4):
            a, b, c = f[(k - 1) % 4], f[k], f[(k + 1) % 4]
            e_prev = n_verts + nf + edges[(min(a, b), max(a, b))]
            e_next = n_verts + nf + edges[(min(b, c), max(b, c))]
            new_faces.append((b, e_next, fp, e_prev))
    return M, new_faces


def subdivision(n_verts, faces, levels):
    S = np.eye(n_verts)
    current = faces
    n = n_verts
    for _ in range(levels):
        M, current = catmull_clark_operator(n, current)
        S = M @ S
        n = M.shape[0]
    return S, current


def limit_operator(n_verts, faces):
    """Catmull-Clark limit positions for the vertices of a closed quad mesh."""
    vert_faces = [[] for _ in range(n_verts)]
    neighbours = [set() for _ in range(n_verts)]
    for f in faces:
        for k in range(4):
            vert_faces[f[k]].append(f)
            neighbours[f[k]].add(f[(k + 1) % 4]); neighbours[f[k]].add(f[(k - 1) % 4])
    L = np.zeros((n_verts, n_verts))
    for v in range(n_verts):
        n = len(neighbours[v])
        L[v, v] += n * n
        for u in neighbours[v]:
            L[v, u] += 4
        for f in vert_faces[v]:
            k = f.index(v)
            L[v, f[(k + 2) % 4]] += 1
        L[v] /= n * (n + 5)
    return L


def umbrella(n_verts, faces):
    neighbours = [set() for _ in range(n_verts)]
    for f in faces:
        for k in range(4):
            neighbours[f[k]].add(f[(k + 1) % 4]); neighbours[f[k]].add(f[(k - 1) % 4])
    L = np.eye(n_verts)
    for v, ns in enumerate(neighbours):
        for u in ns:
            L[v, u] -= 1 / len(ns)
    return L, neighbours


def triangulate(faces):
    tris = []
    for a, b, c, d in faces:
        tris += [(a, b, c), (a, c, d)]
    return np.array(tris, dtype=np.int32)


def vertex_normals(P, faces):
    N = np.zeros_like(P)
    for a, b, c, d in faces:
        n = np.cross(P[c] - P[a], P[d] - P[b])
        for v in (a, b, c, d):
            N[v] += n
    return N / np.linalg.norm(N, axis=1, keepdims=True)


def face_angles(P, faces):
    """Maximum dihedral deviation between adjacent faces, in degrees, per edge."""
    F = np.array(faces)
    n = np.cross(P[F[:, 2]] - P[F[:, 0]], P[F[:, 3]] - P[F[:, 1]])
    n /= np.linalg.norm(n, axis=1, keepdims=True) + 1e-20
    edge_face = {}
    for fi, f in enumerate(faces):
        for k in range(4):
            a, b = f[k], f[(k + 1) % 4]
            edge_face.setdefault((min(a, b), max(a, b)), []).append(fi)
    pairs = np.array([fs for fs in edge_face.values() if len(fs) == 2])
    cos = np.clip((n[pairs[:, 0]] * n[pairs[:, 1]]).sum(1), -1, 1)
    return np.degrees(np.arccos(cos)), pairs


def orient_outward(P, faces):
    """Make face winding consistent and outward (positive signed volume)."""
    faces = [list(f) for f in faces]
    edge_face = {}
    for fi, f in enumerate(faces):
        for k in range(4):
            a, b = f[k], f[(k + 1) % 4]
            edge_face.setdefault((min(a, b), max(a, b)), []).append(fi)
    done = [False] * len(faces)
    for seed in range(len(faces)):
        if done[seed]:
            continue
        done[seed] = True
        stack = [seed]
        while stack:
            fi = stack.pop()
            f = faces[fi]
            for k in range(4):
                a, b = f[k], f[(k + 1) % 4]
                for gi in edge_face[(min(a, b), max(a, b))]:
                    if gi == fi or done[gi]:
                        continue
                    g = faces[gi]
                    # A consistent neighbour traverses the shared edge as b->a.
                    for m in range(4):
                        if g[m] == a and g[(m + 1) % 4] == b:
                            g.reverse()
                            break
                    done[gi] = True
                    stack.append(gi)
    volume = 0
    for a, b, c, d in faces:
        for x, y, z in ((a, b, c), (a, c, d)):
            volume += np.dot(P[x], np.cross(P[y], P[z])) / 6
    if volume < 0:
        faces = [f[::-1] for f in faces]
    return [tuple(f) for f in faces]
