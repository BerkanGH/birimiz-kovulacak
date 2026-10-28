"""Bean character geometry: body shape math, face patches, primitives that follow the body surface, base bodies,
and every accessory used by the 6 player looks and 4(+1) manager looks.

All parts are separate objects with vertex groups named after bones; build_* functions join them into the
exported meshes (BeanBody, FaceEyes, FaceMouth, Acc_*, Prop_*).
"""
import bisect
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Quaternion, Vector

import bk_common as bk
import char_face_atlas as fa
import char_rig as cr

TAU = math.tau

# ------------------------------------------------------------------------------------------ bean shape

# Capsule beans (matches the 2D portraits): slightly flared base, straight sides, round top.
EMPLOYEE_CTRL = [(0.0, 0.05), (0.19, 0.056), (0.27, 0.09), (0.315, 0.16), (0.327, 0.27), (0.325, 0.42),
                 (0.315, 0.58), (0.3, 0.72), (0.285, 0.85), (0.26, 0.96), (0.205, 1.06), (0.118, 1.135), (0.0, 1.168)]
MANAGER_CTRL = [(0.0, 0.05), (0.21, 0.056), (0.3, 0.09), (0.345, 0.16), (0.36, 0.27), (0.358, 0.42),
                (0.35, 0.58), (0.335, 0.72), (0.315, 0.84), (0.285, 0.95), (0.225, 1.05), (0.13, 1.12), (0.0, 1.15)]


def catmull(pts, n):
    out = []
    P = [pts[0]] + list(pts) + [pts[-1]]
    for i in range(1, len(P) - 2):
        p0, p1, p2, p3 = P[i - 1], P[i], P[i + 1], P[i + 2]
        for k in range(n):
            t = k / n
            t2, t3 = t * t, t * t * t
            out.append(tuple(0.5 * ((2 * p1[j]) + (-p0[j] + p2[j]) * t + (2 * p0[j] - 5 * p1[j] + 4 * p2[j] - p3[j]) * t2 +
                                    (-p0[j] + 3 * p1[j] - 3 * p2[j] + p3[j]) * t3) for j in (0, 1)))
    out.append(pts[-1])
    return out


class BeanShape:
    """Surface of revolution flattened front-back by `depth`. theta = 0 is the front (+Y); positive theta goes to
    the viewer's right when facing the character (= character's left = -X)."""

    def __init__(self, ctrl, depth=0.9, s=1.0):
        pts = catmull([(r * s, z * s) for r, z in ctrl], 12)
        zs, rs = [], []
        for r, z in pts:
            if zs and z <= zs[-1]:
                z = zs[-1] + 1e-5
            zs.append(z)
            rs.append(max(0.0, r))
        rs[0] = rs[-1] = 0.0
        self.zs, self.rs, self.D, self.s = zs, rs, depth, s
        self.zmin, self.zmax = zs[0], zs[-1]
        n = 800
        self.tz = [self.zmin + (self.zmax - self.zmin) * i / n for i in range(n + 1)]
        self.ts = [0.0]
        for i in range(1, n + 1):
            dz = self.tz[i] - self.tz[i - 1]
            dy = depth * (self.r(self.tz[i]) - self.r(self.tz[i - 1]))
            self.ts.append(self.ts[-1] + math.hypot(dz, dy))

    def r(self, z):
        if z <= self.zmin or z >= self.zmax:
            return 0.0
        i = bisect.bisect_right(self.zs, z) - 1
        z0, z1 = self.zs[i], self.zs[i + 1]
        t = (z - z0) / (z1 - z0)
        return self.rs[i] + (self.rs[i + 1] - self.rs[i]) * t

    def dr(self, z, h=0.002):
        return (self.r(z + h) - self.r(z - h)) / (2 * h)

    def _interp(self, xs, ys, x):
        i = min(max(bisect.bisect_right(xs, x) - 1, 0), len(xs) - 2)
        t = (x - xs[i]) / (xs[i + 1] - xs[i])
        return ys[i] + (ys[i + 1] - ys[i]) * t

    def arc_to_z(self, z0, d):
        return self._interp(self.ts, self.tz, self._interp(self.tz, self.ts, z0) + d)

    def theta_at(self, px, z):
        r = max(self.r(z), 1e-4)
        target, sgn = abs(px), (1 if px >= 0 else -1)
        th, acc, step = 0.0, 0.0, 0.004
        while acc < target and th < math.pi:
            ds = r * math.sqrt(math.cos(th + step / 2) ** 2 + (self.D * math.sin(th + step / 2)) ** 2) * step
            if acc + ds >= target:
                th += step * (target - acc) / ds
                acc = target
                break
            acc += ds
            th += step
        return th * sgn

    def point(self, th, z):
        r = self.r(z)
        return Vector((-r * math.sin(th), self.D * r * math.cos(th), z))

    def normal(self, th, z):
        r, dr = max(self.r(z), 1e-4), self.dr(z)
        dth = Vector((-r * math.cos(th), -self.D * r * math.sin(th), 0.0))
        dz = Vector((-dr * math.sin(th), self.D * dr * math.cos(th), 1.0))
        n = dth.cross(dz)
        if n.length < 1e-8:
            return Vector((0, 0, 1))
        n.normalize()
        p = self.point(th, z)
        if n.x * p.x + n.y * p.y / self.D < 0 and r > 0.01:
            n = -n
        return n

    def surface(self, th, z, off=0.0):
        return self.point(th, z) + self.normal(th, z) * off

    def face(self, px, py, zc, off=0.0):
        z = self.arc_to_z(zc, py)
        th = self.theta_at(px, z)
        return self.surface(th, z, off), self.normal(th, z)

    def lathe_profile(self, rings):
        """Resample the profile by arc length (more rings where it curves)."""
        acc = [0.0]
        for i in range(1, len(self.zs)):
            acc.append(acc[-1] + math.hypot(self.rs[i] - self.rs[i - 1], self.zs[i] - self.zs[i - 1]))
        out = []
        for k in range(rings + 1):
            a = acc[-1] * k / rings
            out.append((self._interp(acc, self.rs, a), self._interp(acc, self.zs, a)))
        out[0] = (0.0, self.zmin)
        out[-1] = (0.0, self.zmax)
        return out


# ------------------------------------------------------------------------------------------ primitives

def _obj_from_bm(name, bm, coll, mats, smooth=True):
    if isinstance(mats, str):
        mats = [mats]
    return bk.mesh_obj(name, bm, coll, list(mats), smooth=smooth)


def frames_along(pts, closed=False):
    """Parallel-transport frames (tangent, normal, binormal) along a polyline."""
    n = len(pts)
    tans = []
    for i in range(n):
        if closed:
            t = pts[(i + 1) % n] - pts[i - 1]
        else:
            t = pts[min(i + 1, n - 1)] - pts[max(i - 1, 0)]
        tans.append(t.normalized())
    ref = Vector((0, 0, 1)) if abs(tans[0].z) < 0.9 else Vector((1, 0, 0))
    nrm = (ref - tans[0] * ref.dot(tans[0])).normalized()
    frames = []
    for i in range(n):
        if i > 0:
            q = tans[i - 1].rotation_difference(tans[i])
            nrm = (q @ nrm)
            nrm = (nrm - tans[i] * nrm.dot(tans[i])).normalized()
        frames.append((tans[i], nrm, tans[i].cross(nrm)))
    return frames


def tube(name, coll, pts, radii, mat, sides=8, closed=False, caps=True, flat=None, seg_mats=None):
    """Tube along 3D points. radii: float or list. flat: optional (sx, sy) ellipse scale of the cross-section.
    mat may be a list of materials with seg_mats giving the material index per segment (striped scarves)."""
    pts = [Vector(p) for p in pts]
    if not isinstance(radii, (list, tuple)):
        radii = [radii] * len(pts)
    fx, fy = flat or (1.0, 1.0)
    bm = bmesh.new()
    rings = []
    for p, r, (t, nv, bv) in zip(pts, radii, frames_along(pts, closed)):
        ring = []
        for k in range(sides):
            a = TAU * k / sides
            ring.append(bm.verts.new(p + (nv * math.cos(a) * fx + bv * math.sin(a) * fy) * r))
        rings.append(ring)
    count = len(rings) if closed else len(rings) - 1
    for i in range(count):
        a, b = rings[i], rings[(i + 1) % len(rings)]
        for k in range(sides):
            f = bm.faces.new((a[k], a[(k + 1) % sides], b[(k + 1) % sides], b[k]))
            if seg_mats is not None:
                f.material_index = seg_mats[i % len(seg_mats)]
    if caps and not closed:
        bm.faces.new(list(reversed(rings[0])))
        bm.faces.new(rings[-1])
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj_from_bm(name, bm, coll, mat)


def circle_pts(center, normal, radius, n=16, up=None, sx=1.0, sy=1.0):
    normal = Vector(normal).normalized()
    up = Vector(up) if up is not None else Vector((0, 0, 1))
    u = (up - normal * up.dot(normal))
    if u.length < 1e-6:
        u = Vector((1, 0, 0)) - normal * normal.x
    u.normalize()
    v = normal.cross(u)
    return [Vector(center) + (v * math.cos(TAU * i / n) * sx + u * math.sin(TAU * i / n) * sy) * radius for i in range(n)]


def surface_poly(name, coll, shape, poly, off, thick, mat, rings=3, sub=3, zc=None):
    """Slab lying on the body surface. poly: convex list of (px, z) (px = arc length to the viewer's right, z absolute)
    or (px, py) relative to zc (arc length) when zc is given."""
    bnd = []
    for i in range(len(poly)):
        a, b = poly[i], poly[(i + 1) % len(poly)]
        for k in range(sub):
            t = k / sub
            bnd.append((a[0] + (b[0] - a[0]) * t, a[1] + (b[1] - a[1]) * t))
    cx = sum(p[0] for p in bnd) / len(bnd)
    cy = sum(p[1] for p in bnd) / len(bnd)

    def to3d(px, py, o):
        if zc is not None:
            z = shape.arc_to_z(zc, py)
        else:
            z = py
        th = shape.theta_at(px, z)
        return shape.surface(th, z, o)

    bm = bmesh.new()
    layers = {}
    for side, o in (("top", off + thick), ("bot", off)):
        grid = [[bm.verts.new(to3d(cx, cy, o))]]
        for k in range(1, rings + 1):
            f = k / rings
            grid.append([bm.verts.new(to3d(cx + (x - cx) * f, cy + (y - cy) * f, o)) for x, y in bnd])
        layers[side] = grid
    n = len(bnd)
    for side in ("top", "bot"):
        g = layers[side]
        for j in range(n):
            f = (g[0][0], g[1][j], g[1][(j + 1) % n])
            bm.faces.new(f if side == "top" else tuple(reversed(f)))
        for k in range(1, rings):
            for j in range(n):
                f = (g[k][j], g[k + 1][j], g[k + 1][(j + 1) % n], g[k][(j + 1) % n])
                bm.faces.new(f if side == "top" else tuple(reversed(f)))
    t, b = layers["top"][-1], layers["bot"][-1]
    for j in range(n):
        bm.faces.new((b[j], b[(j + 1) % n], t[(j + 1) % n], t[j]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj_from_bm(name, bm, coll, mat, smooth=True)


def shell(name, coll, shape, mat, cut_fn, off_fn, segs=24, rows=8, top=True, z_end=None):
    """Cap/band following the body surface. top=True: from the cut line (cut_fn(theta) -> z) up to the top pole.
    top=False: from the bottom pole up to the cut line. off_fn(theta, f) -> outward offset (f = 0 at the cut, 1 at
    the pole)."""
    bm = bmesh.new()
    cols = []
    z_pole = shape.zmax if top else shape.zmin
    z_lim = (shape.zmax - 0.035 * shape.s) if top else (shape.zmin + 0.02 * shape.s)
    for i in range(segs):
        th = -math.pi + TAU * i / segs
        zc = cut_fn(th)
        zt = z_end if z_end is not None else z_lim
        col = [bm.verts.new(shape.surface(th, zc, -0.012 * shape.s))]
        for k in range(rows + 1):
            f = k / rows
            z = zc + (zt - zc) * f
            col.append(bm.verts.new(shape.surface(th, z, off_fn(th, f))))
        cols.append(col)
    pole = None
    if z_end is None:
        offp = off_fn(0.0, 1.0)
        pole = bm.verts.new(Vector((0, 0, z_pole + (offp if top else -offp))))
    for i in range(segs):
        a, b = cols[i], cols[(i + 1) % segs]
        for k in range(len(a) - 1):
            f = (a[k], b[k], b[k + 1], a[k + 1])
            bm.faces.new(f if top else tuple(reversed(f)))
        if pole is not None:
            f = (a[-1], b[-1], pole)
            bm.faces.new(f if top else tuple(reversed(f)))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return _obj_from_bm(name, bm, coll, mat)


def ring_band(name, coll, shape, z_fn, mat, tube_r, off, th0=-math.pi, th1=math.pi, n=28, sides=8, flat=None):
    """Tube that wraps around the body at height z_fn(theta), offset outward."""
    closed = (th1 - th0) >= TAU - 1e-4
    cnt = n if closed else n + 1
    pts = []
    for i in range(cnt):
        th = th0 + (th1 - th0) * i / n
        z = z_fn(th)
        pts.append(shape.surface(th, z, off))
    return tube(name, coll, pts, tube_r, mat, sides=sides, closed=closed, flat=flat)


def ico_blob(name, coll, center, radius, mat, scale=(1, 1, 1), subdiv=2, seed=1, jitter=0.0):
    return bk.ico(name, radius, coll, mat, location=center, scale=scale, subdiv=subdiv, smooth=True, jitter=jitter, seed=seed)


def apply_xf(obj):
    bpy.context.view_layer.update()
    obj.data.transform(obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    return obj


# ----------------------------------------------------------------------------------------- assembly

def rigid(obj, bone):
    apply_xf(obj)
    cr.weights_rigid(obj, bone)
    return obj


def wfn(obj, fn):
    apply_xf(obj)
    cr.weights_fn(obj, fn)
    return obj


def join(objs, name):
    bpy.ops.object.select_all(action="DESELECT")
    for o in objs:
        o.select_set(True)
    bpy.context.view_layer.objects.active = objs[0]
    if len(objs) > 1:
        bpy.ops.object.join()
    o = objs[0]
    o.name = name
    o.data.name = name
    return o


def tri_count(obj):
    return sum(len(p.vertices) - 2 for p in obj.data.polygons)


# --------------------------------------------------------------------------------------------- face

EMP_FACE = {"eyes_zc": 0.89, "mouth_zc": 0.72}
MGR_FACE = {"eyes_zc": 1.07, "mouth_zc": 0.87}  # metres (manager scale 1.2 applied)


def face_patch(name, coll, shape, zc, w, h, cell, mat, nu, nv, off):
    u0, v0, u1, v1 = fa.cell_uv_rect(cell)
    bm = bmesh.new()
    uv = bm.loops.layers.uv.new("UVMap")
    grid = []
    for j in range(nv + 1):
        row = []
        py = (j / nv - 0.5) * h
        for i in range(nu + 1):
            px = (i / nu - 0.5) * w
            p, _ = shape.face(px, py, zc, off)
            row.append(bm.verts.new(p))
        grid.append(row)
    for j in range(nv):
        for i in range(nu):
            f = bm.faces.new((grid[j][i], grid[j][i + 1], grid[j + 1][i + 1], grid[j + 1][i]))
            for loop, (ii, jj) in zip(f.loops, ((i, j), (i + 1, j), (i + 1, j + 1), (i, j + 1))):
                loop[uv].uv = (u0 + (u1 - u0) * ii / nu, v0 + (v1 - v0) * jj / nv)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    # make sure normals face outward (+Y-ish)
    if sum(f.normal.y for f in bm.faces) < 0:
        bmesh.ops.reverse_faces(bm, faces=bm.faces)
    return _obj_from_bm(name, bm, coll, mat)


def face_material(name, atlas_path, cell0):
    """Preview material: atlas with alpha clip. A Mapping node offsets the UVs to show other cells."""
    m = bpy.data.materials.get(name)
    if m is not None:
        return m
    m = bpy.data.materials.new(name)
    m.use_nodes = True
    nt = m.node_tree
    bsdf = nt.nodes.get("Principled BSDF")
    bsdf.inputs["Roughness"].default_value = 0.45
    img = bpy.data.images.load(atlas_path, check_existing=True)
    tex = nt.nodes.new("ShaderNodeTexImage")
    tex.image = img
    tex.interpolation = "Linear"
    tex.extension = "CLIP"
    mapping = nt.nodes.new("ShaderNodeMapping")
    mapping.name = "CellOffset"
    coord = nt.nodes.new("ShaderNodeTexCoord")
    nt.links.new(coord.outputs["UV"], mapping.inputs["Vector"])
    nt.links.new(mapping.outputs["Vector"], tex.inputs["Vector"])
    nt.links.new(tex.outputs["Color"], bsdf.inputs["Base Color"])
    gt = nt.nodes.new("ShaderNodeMath")
    gt.operation = "GREATER_THAN"
    gt.inputs[1].default_value = 0.5
    nt.links.new(tex.outputs["Alpha"], gt.inputs[0])
    nt.links.new(gt.outputs[0], bsdf.inputs["Alpha"])
    try:
        m.surface_render_method = "DITHERED"
    except AttributeError:
        pass
    m["cell0"] = cell0
    return m


def set_face_cell(mat, cell, base_cell):
    """Shift the preview material's mapping so `cell` is shown (same convention as Unity's _BaseMap offset)."""
    k = cell - base_cell
    off = ((k % 4) / 4.0, -(k // 4) / 4.0)
    node = mat.node_tree.nodes["CellOffset"]
    node.inputs["Location"].default_value = (off[0], off[1], 0)


