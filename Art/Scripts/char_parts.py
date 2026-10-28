"""Bean bodies, navy outline hulls, accessories (6 player looks, 4+1 manager looks) and hand props.

Art direction follows the 2D portraits (Art/UI/portraits): capsule beans, big cream collars, navy outline,
one signature accessory per player; managers are bigger beans in jackets.
"""
import math
import random

import bmesh
import bpy
from mathutils import Matrix, Vector

import bk_common as bk
import char_face_atlas as fa
import char_rig as cr
from char_models import (TAU, _obj_from_bm, apply_xf, circle_pts, face_material, face_patch, join, rigid, ring_band, shell,
                         surface_poly, tube, wfn)

# ------------------------------------------------------------------------------------------- outline

OUTLINE_MAT = "M_Outline"


def hull_from(obj, thickness, name="Hull"):
    """Inverted-hull outline: copy of `obj` (same vertex groups) pushed out along vertex normals with flipped faces.
    With back-face culling it only shows around the silhouette (navy cartoon outline, no custom shader needed)."""
    bpy.context.view_layer.update()
    me = obj.data.copy()
    h = bpy.data.objects.new(name, me)
    obj.users_collection[0].objects.link(h)
    h.matrix_world = obj.matrix_world.copy()
    for vg in obj.vertex_groups:  # deform weights live in the mesh, group names on the object
        h.vertex_groups.new(name=vg.name)
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    mw = obj.matrix_world
    for v in bm.verts:
        t = thickness(mw @ v.co) if callable(thickness) else thickness
        v.co = v.co + v.normal * t
    bmesh.ops.reverse_faces(bm, faces=bm.faces)
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(bk.mat(OUTLINE_MAT))
    for p in me.polygons:
        p.material_index = 0
        p.use_smooth = True
    return h


# ------------------------------------------------------------------------------------------ base bodies

def smooth01(t):
    t = min(1.0, max(0.0, t))
    return t * t * (3 - 2 * t)


def arm_parts(coll, rig, side, sleeve_mat, hand_mat, s, r0, r1, hand_r):
    sx = -1 if side == "L" else 1
    S = rig.head["Arm." + side]
    E = rig.head["Hand." + side]
    H = rig.tail["Hand." + side]
    d_up = (E - S).normalized()
    d_fo = (H - E).normalized()
    start = S - Vector((sx * 0.05 * s, 0, 0))
    wrist = H - d_fo * hand_r * 0.6 * s
    L1 = (E - start).length
    L2 = (wrist - E).length

    def path(n1, n2):
        pts, ts = [], []
        for i in range(n1):
            pts.append(start.lerp(E, i / n1))
            ts.append(L1 * i / n1)
        for i in range(n2 + 1):
            pts.append(E.lerp(wrist, i / n2))
            ts.append(L1 + L2 * i / n2)
        return pts, [(r0 + (r1 - r0) * t / (L1 + L2)) * s for t in ts]

    def fn(co):
        a = (co - start).dot(d_up)
        b = (co - E).dot(d_fo)
        t = smooth01((b + 0.035 * s) / (0.07 * s))
        if a < L1 - 0.06 * s:
            t = 0.0
        return {"Arm." + side: 1 - t, "Hand." + side: t}
    pts, radii = path(4, 4)
    arm = wfn(tube("Arm" + side, coll, pts, radii, sleeve_mat, sides=8), fn)
    pts, radii = path(2, 3)
    arm_lo = wfn(tube("ArmHullSrc", coll, pts, radii, sleeve_mat, sides=6), fn)
    hand = bk.sphere("Hand" + side, hand_r * s, coll, hand_mat, location=H, scale=(0.8, 0.95, 1.08), segments=10, rings=7)
    thumb = bk.sphere("Thumb" + side, hand_r * 0.42 * s, coll, hand_mat, location=H + Vector((-sx * 0.012, 0.05, 0.02)) * s,
                      scale=(0.9, 1.25, 0.9), segments=6, rings=4)
    hand_lo = bk.sphere("HandHullSrc", hand_r * s, coll, hand_mat, location=H, scale=(0.8, 0.95, 1.08), segments=7, rings=5)
    for o in (hand, thumb, hand_lo):
        rigid(o, "Hand." + side)
    hulls = [hull_from(arm_lo, 0.009 * s), hull_from(hand_lo, 0.011 * s)]
    bpy.data.objects.remove(arm_lo)
    bpy.data.objects.remove(hand_lo)
    return [arm, hand, thumb] + hulls


def feet_parts(coll, rig, shoe_mat, s):
    parts = []
    for side in ("L", "R"):
        h = rig.head["Foot." + side]
        c = Vector((h.x, 0.035 * s, 0.048 * s))
        shoe = bk.sphere("Shoe" + side, 0.075 * s, coll, shoe_mat, location=c, scale=(0.95, 1.5, 0.68), segments=10, rings=7)
        apply_xf(shoe)
        for v in shoe.data.vertices:
            if v.co.z < 0.004 * s:
                v.co.z = 0.004 * s
        cr.weights_rigid(shoe, "Foot." + side)
        leg = bk.cylinder("Leg" + side, 0.045 * s, 0.13 * s, coll, shoe_mat, location=(h.x, -0.005 * s, 0.13 * s), segments=6)
        rigid(leg, "Foot." + side)
        parts += [shoe, leg]
    return parts


def build_base(coll, rig, shape, kind, skin_mat, shoe_mat, face_cfg, atlas_path, sleeve_mat=None, body_extra=None):
    """Body (+ arms, hands, feet, outline hulls) joined as BeanBody; face patches FaceEyes / FaceMouth."""
    s = shape.s
    mgr = kind == "manager"
    body = bk.lathe("BeanBody", shape.lathe_profile(18), coll, skin_mat, segments=24, scale=(1, shape.D, 1))
    wfn(body, cr.body_head_fn(s))
    parts = [body] + list(body_extra or [])
    for side in ("L", "R"):
        parts += arm_parts(coll, rig, side, sleeve_mat or skin_mat, skin_mat, s, r0=0.056 if mgr else 0.05,
                           r1=0.048 if mgr else 0.04, hand_r=0.068 if mgr else 0.072)
    parts += feet_parts(coll, rig, shoe_mat, s)
    jacket_top = 0.64 * s

    def off(co):
        if not mgr:
            return 0.013 * s
        return (0.013 + 0.016 * (1 - smooth01((co.z - jacket_top + 0.03 * s) / (0.06 * s)))) * s
    lo = bk.lathe("BodyHullSrc", shape.lathe_profile(11), coll, skin_mat, segments=16, scale=(1, shape.D, 1))
    apply_xf(lo)
    hull = hull_from(lo, off, "BodyHull")
    bpy.data.objects.remove(lo)
    wfn(hull, cr.body_head_fn(s))
    parts.append(hull)
    body = join(parts, "BeanBody")
    cr.bind(body, rig.obj)

    face_material("M_FaceEyes", atlas_path, 0)
    face_material("M_FaceMouth", atlas_path, 8)
    eyes = face_patch("FaceEyes", coll, shape, face_cfg["eyes_zc"], fa.EYE_W * fa.EYE_SCALE, fa.EYE_H * fa.EYE_SCALE, 0, "M_FaceEyes", 10, 6, 0.004 * s)
    mouth = face_patch("FaceMouth", coll, shape, face_cfg["mouth_zc"], fa.MOUTH_W * fa.MOUTH_SCALE, fa.MOUTH_H * fa.MOUTH_SCALE, 8, "M_FaceMouth", 6, 4, 0.004 * s)
    for o in (eyes, mouth):
        wfn(o, cr.body_head_fn(s))
        cr.bind(o, rig.obj)
    return body, eyes, mouth


# ----------------------------------------------------------------------------------- accessory helpers

class Ctx:
    def __init__(self, coll, rig, shape, face_cfg):
        self.coll, self.rig, self.shape, self.face = coll, rig, shape, face_cfg
        self.s = shape.s

    def eye_pt(self, side, dx=0.0, dy=0.0, off=0.0):
        """Point on the face relative to an eye centre (drawing-space offsets; side +1 = screen-right eye)."""
        x, y = fa.eye_pt(fa.EX * side + side * dx, fa.EY + dy)
        return self.shape.face(x, y, self.face["eyes_zc"], off)

    def eye_center(self, side, off=0.0):
        return self.eye_pt(side, 0.0, 0.0, off)

    def front(self, z, off=0.0, px=0.0):
        th = self.shape.theta_at(px, z)
        return self.shape.surface(th, z, off), self.shape.normal(th, z)


def finish(ctx, name, parts):
    o = join(parts, name)
    cr.bind(o, ctx.rig.obj)
    return o


def bh(ctx):
    return cr.body_head_fn(ctx.s)


def ht(ctx, z0=0.98, z1=1.16):
    return cr.head_top_fn(ctx.s, z0, z1)


# ------------------------------------------------------------------------------------- player accessories

def acc_collar(ctx, z=0.585, round_tips=False, name="Acc_Collar", mat="M_CollarCream"):
    """Big cream shirt collar: a flared band all round plus two flaps (pointed, or rounded Peter Pan for Pembe)."""
    s, sh = ctx.s, ctx.shape
    z = z * s
    band = shell("CollarBand", ctx.coll, sh, mat, lambda th: z - 0.01 * s + 0.012 * s * math.cos(th),
                 lambda th, f: (0.006 + 0.016 * f) * s, segs=20, rows=2, top=True, z_end=z + 0.05 * s)
    parts = [wfn(band, bh(ctx))]
    for sgn in (-1, 1):
        if round_tips:
            poly = [(0.01 * sgn * s, z + 0.04 * s), (0.14 * sgn * s, z + 0.035 * s), (0.17 * sgn * s, z - 0.02 * s),
                    (0.135 * sgn * s, z - 0.075 * s), (0.07 * sgn * s, z - 0.085 * s), (0.02 * sgn * s, z - 0.04 * s)]
        else:
            poly = [(0.012 * sgn * s, z + 0.04 * s), (0.165 * sgn * s, z + 0.035 * s), (0.125 * sgn * s, z - 0.04 * s),
                    (0.06 * sgn * s, z - 0.11 * s)]
        flap = wfn(surface_poly("CollarFlap", ctx.coll, sh, poly, 0.018 * s, 0.012 * s, mat, rings=2, sub=2), bh(ctx))
        parts += [flap, hull_from(flap, 0.006 * s)]
    return finish(ctx, name, parts)


def acc_tie(ctx, mat="M_TieGreenDark", name="Acc_Tie", zt=0.605, bottom=0.34, w=0.05):
    s, sh = ctx.s, ctx.shape
    zt = zt * s
    knot = surface_poly("TieKnot", ctx.coll, sh, [(-0.03 * s, zt + 0.012 * s), (0.03 * s, zt + 0.012 * s), (0.02 * s, zt - 0.034 * s),
                                                   (-0.02 * s, zt - 0.034 * s)], 0.016 * s, 0.018 * s, mat, rings=1, sub=2)
    blade = surface_poly("TieBlade", ctx.coll, sh, [(-0.018 * s, zt - 0.034 * s), (0.018 * s, zt - 0.034 * s), (w * s, bottom * s + 0.05 * s),
                                                     (0.0, bottom * s), (-w * s, bottom * s + 0.05 * s)], 0.012 * s, 0.01 * s, mat, rings=2, sub=3)
    return finish(ctx, name, [wfn(knot, bh(ctx)), wfn(blade, bh(ctx))])


def acc_glasses_round(ctx, name="Acc_GlassesRound", mat="M_GlassesNavy"):
    s = ctx.s
    k = fa.EYE_SCALE
    parts = []
    for side in (-1, 1):
        c, n = ctx.eye_center(side, 0.024 * s)
        rim = tube("Rim", ctx.coll, circle_pts(c, n, 0.082 * k, 14, sx=1.0, sy=1.04), 0.013 * s, mat, sides=5, closed=True)
        parts.append(wfn(rim, bh(ctx)))
        outer, _ = ctx.eye_pt(side, 0.08, 0.01, 0.024 * s)
        back = ctx.shape.surface(side * 1.5, outer.z, 0.012 * s)
        mid = ctx.shape.surface(side * 1.0, outer.z + 0.004 * s, 0.02 * s)
        parts.append(wfn(tube("Temple", ctx.coll, [outer, mid, back], 0.009 * s, mat, sides=4), bh(ctx)))
    a, _ = ctx.shape.face(-0.014 * k, (fa.EY + 0.02) * k, ctx.face["eyes_zc"], 0.03 * s)
    b, _ = ctx.shape.face(0.014 * k, (fa.EY + 0.02) * k, ctx.face["eyes_zc"], 0.03 * s)
    parts.append(wfn(tube("Bridge", ctx.coll, [a, (a + b) / 2 + Vector((0, 0.006 * s, 0.006 * s)), b], 0.01 * s, mat, sides=4), bh(ctx)))
    return finish(ctx, name, parts)


def acc_hair_bun(ctx):
    """Pembe: navy bun on the top-back of the head, slightly to one side like the portrait."""
    s, sh = ctx.s, ctx.shape
    base = bk.sphere("BunBase", 0.075 * s, ctx.coll, "M_HairNavy", location=(-0.06 * s, -0.08 * s, sh.zmax - 0.02 * s), scale=(1.2, 1.2, 0.6), segments=10, rings=6)
    bun = bk.sphere("Bun", 0.115 * s, ctx.coll, "M_HairNavy", location=(-0.085 * s, -0.1 * s, sh.zmax + 0.085 * s), segments=12, rings=8)
    parts = [rigid(base, "Top"), rigid(bun, "Top")]
    parts.append(hull_from(bun, 0.012 * s))
    return finish(ctx, "Acc_HairBun", parts)


def acc_headset(ctx):
    s, sh = ctx.s, ctx.shape
    zc = 0.86 * s
    r = sh.r(zc)
    rad = r + 0.035 * s
    height = sh.zmax - zc + 0.03 * s
    pts = [Vector((math.cos(a) * rad, -0.02 * s, zc + math.sin(a) * height)) for a in [math.pi * i / 12 for i in range(13)]]
    parts = [wfn(tube("Band", ctx.coll, pts, 0.02 * s, "M_HeadsetNavy", sides=6, flat=(1.0, 0.6)), ht(ctx))]
    for sgn in (-1, 1):
        cup = bk.cylinder("Cup", 0.075 * s, 0.055 * s, ctx.coll, "M_HeadsetNavy", location=(sgn * (r + 0.022 * s), -0.02 * s, zc),
                          rotation=(0, math.radians(90), 0), segments=12)
        parts.append(wfn(cup, bh(ctx)))
        parts.append(hull_from(cup, 0.008 * s))
    mic, _ = ctx.shape.face(0.09 * fa.MOUTH_SCALE, 0.0, ctx.face["mouth_zc"], 0.035 * s)
    start = Vector((-(r + 0.03 * s), 0.02 * s, zc - 0.03 * s))
    mid = ctx.shape.surface(0.95, zc - 0.12 * s, 0.04 * s)
    parts.append(wfn(tube("Boom", ctx.coll, [start, mid, mic], 0.011 * s, "M_HeadsetNavy", sides=5), bh(ctx)))
    parts.append(wfn(bk.sphere("Mic", 0.028 * s, ctx.coll, "M_PlasticDark", location=mic, segments=8, rings=6), bh(ctx)))
    return finish(ctx, "Acc_Headset", parts)


def acc_beanie(ctx):
    """Turuncu: navy knit beanie sitting back on the head, thick cuff just above the brows."""
    s, sh = ctx.s, ctx.shape

    def cut(th):  # tilted back: higher at the front
        return (1.03 - 0.1 * (1 - math.cos(th)) * 0.5) * s
    dome = wfn(shell("Dome", ctx.coll, sh, "M_Beanie", cut, lambda th, f: (0.03 + 0.03 * math.sin(f * math.pi * 0.6)) * s, segs=16, rows=5), ht(ctx))
    cuff = wfn(ring_band("Cuff", ctx.coll, sh, lambda th: cut(th) + 0.03 * s, "M_BeanieCuff", 0.045 * s, 0.035 * s, n=16, sides=5, flat=(0.8, 1.0)), ht(ctx))
    return finish(ctx, "Acc_Beanie", [dome, cuff, hull_from(dome, 0.012 * s), hull_from(cuff, 0.01 * s)])


def acc_curls(ctx, mat="M_HairNavy", name="Acc_Curls", seed=11):
    """Mor: big curly hair. A lumpy cap (cheap) plus a ring of round curls that make the silhouette."""
    s, sh = ctx.s, ctx.shape

    def cut(th):
        c = math.cos(th)
        return (1.02 if c > 0.5 else 0.95 - 0.06 * (0.5 - c)) * s

    def off(th, f):
        return (0.05 + 0.05 * math.sin(f * math.pi * 0.7) + 0.022 * abs(math.sin(4 * th + 3 * f))) * s
    cap = wfn(shell("CurlCap", ctx.coll, sh, mat, cut, off, segs=18, rows=5), ht(ctx))
    cap_lo = wfn(shell("CurlHullSrc", ctx.coll, sh, mat, cut, off, segs=12, rows=3), ht(ctx))
    parts = [cap, hull_from(cap_lo, 0.012 * s)]
    bpy.data.objects.remove(cap_lo)
    rnd = random.Random(seed)
    for i in range(10):
        th = -math.pi + TAU * (i + 0.5) / 10 + rnd.uniform(-0.12, 0.12)
        if abs(th) < 0.6:
            th = math.copysign(0.6 + rnd.uniform(0, 0.15), th if th != 0 else 1)
        z = cut(th) + rnd.uniform(0.0, 0.04) * s
        p = sh.surface(th, z, 0.075 * s)
        r = rnd.uniform(0.075, 0.09) * s
        blob = bk.sphere("Curl", r, ctx.coll, mat, location=p, scale=(1, 1, 0.9), segments=7, rings=5)
        parts.append(wfn(blob, ht(ctx, 0.95, 1.2)))
    for i, (x, y) in enumerate(((0.0, 0.08), (-0.11, -0.04), (0.11, -0.03))):
        top = bk.sphere("CurlTop", 0.1 * s, ctx.coll, mat, location=(x * s, y * s, sh.zmax + 0.06 * s), scale=(1, 1, 0.85), segments=7, rings=5)
        parts.append(rigid(top, "Top"))
    return finish(ctx, name, parts)


def acc_scarf(ctx):
    """Mor: chunky two-tone teal scarf (one closed striped tube), a knot and a hanging tail (Tail bone)."""
    s, sh = ctx.s, ctx.shape
    z = 0.6 * s
    n = 16

    def zf(th):
        return z + 0.015 * s * math.cos(th)
    pts = [sh.surface(-math.pi + TAU * i / n, zf(-math.pi + TAU * i / n), 0.04 * s) for i in range(n)]
    wrap = wfn(tube("Wrap", ctx.coll, pts, 0.058 * s, ["M_ScarfTeal", "M_ScarfTealDark"], sides=6, closed=True, flat=(1.0, 0.72),
                    seg_mats=[0, 0, 1, 1]), bh(ctx))
    knot_p = sh.surface(-0.4, z - 0.015 * s, 0.08 * s)
    knot = wfn(bk.sphere("Knot", 0.06 * s, ctx.coll, "M_ScarfTeal", location=knot_p, scale=(1, 0.8, 1), segments=8, rings=6), bh(ctx))
    tail_root = knot_p + Vector((0, 0.01 * s, -0.03 * s))
    seg_pts = [tail_root + Vector((0.01 * s * f, 0.025 * s * f, -0.26 * s * f)) for f in (0.0, 0.25, 0.5, 0.75, 1.0)]
    tail = wfn(tube("Tail", ctx.coll, seg_pts, 0.05 * s, ["M_ScarfTeal", "M_ScarfTealDark"], sides=6, flat=(1.0, 0.35), seg_mats=[0, 1]),
               lambda co, top=tail_root.z: {"Body": max(0.0, 1 - (top - co.z) / (0.12 * s)), "Tail": min(1.0, (top - co.z) / (0.12 * s))})
    lo_pts = [sh.surface(-math.pi + TAU * i / 10, zf(-math.pi + TAU * i / 10), 0.04 * s) for i in range(10)]
    wrap_lo = wfn(tube("WrapHullSrc", ctx.coll, lo_pts, 0.058 * s, "M_ScarfTeal", sides=5, closed=True, flat=(1.0, 0.72)), bh(ctx))
    hull = hull_from(wrap_lo, 0.01 * s)
    bpy.data.objects.remove(wrap_lo)
    return finish(ctx, "Acc_Scarf", [wrap, knot, tail, hull])


# ------------------------------------------------------------------------------------- manager outfits

def mgr_suit_parts(ctx):
    """Jacket (slot M_Suit*, swapped per profile), cream shirt with collar points and lapels. Joined into BeanBody."""
    s, sh = ctx.s, ctx.shape

    def vcut(th):
        c = max(0.0, math.cos(th))
        return (0.66 - 0.22 * c ** 6) * s
    jacket = shell("Jacket", ctx.coll, sh, "M_SuitDefault", vcut, lambda th, f: 0.014 * s, segs=24, rows=7, top=False)
    shirt = shell("Shirt", ctx.coll, sh, "M_CollarCream", lambda th: 0.42 * s, lambda th, f: 0.006 * s, segs=20, rows=3, top=True, z_end=0.68 * s)
    parts = [wfn(jacket, bh(ctx)), wfn(shirt, bh(ctx))]
    for sgn in (-1, 1):
        lap = surface_poly("Lapel", ctx.coll, sh, [(0.025 * sgn * s, 0.45 * s), (0.105 * sgn * s, 0.665 * s), (0.18 * sgn * s, 0.65 * s), (0.11 * sgn * s, 0.43 * s)],
                           0.014 * s, 0.012 * s, "M_SuitDefault", rings=2, sub=2)
        parts.append(wfn(lap, bh(ctx)))
        pt = surface_poly("CollarPoint", ctx.coll, sh, [(0.012 * sgn * s, 0.685 * s), (0.09 * sgn * s, 0.69 * s), (0.04 * sgn * s, 0.62 * s)],
                          0.012 * s, 0.01 * s, "M_CollarCream", rings=1, sub=2)
        parts.append(wfn(pt, bh(ctx)))
    for i in range(2):
        p, _ = ctx.front((0.4 - 0.1 * i) * s, 0.02 * s, 0.0)
        parts.append(wfn(bk.sphere("Button", 0.016 * s, ctx.coll, "M_PlasticDark", location=p, scale=(1, 0.5, 1), segments=6, rings=4), bh(ctx)))
    return parts


def acc_mgr_tie(ctx):
    """Manager tie; colour swapped per profile via the M_Tie* slot."""
    return acc_tie(ctx, "M_TieRed", "Acc_MgrTie", zt=0.66, bottom=0.42, w=0.042)


def acc_moustache(ctx, name="Acc_Moustache", big=False):
    s = ctx.s
    parts = []
    zc = ctx.face["mouth_zc"]
    for sgn in (-1, 1):
        if big:
            pts, radii = [], []
            for k in range(7):
                f = k / 6
                px = sgn * (0.012 + 0.16 * f) * s
                py = (0.05 + 0.025 * math.sin(f * math.pi) + 0.05 * f ** 3) * s
                p, _ = ctx.shape.face(px, py, zc, (0.024 + 0.012 * (1 - f)) * s)
                pts.append(p)
                radii.append((0.036 * (1 - f) + 0.011) * s)
            parts.append(wfn(tube("Handlebar", ctx.coll, pts, radii, "M_MoustacheNavy", sides=7, flat=(1.0, 0.7)), bh(ctx)))
            parts.append(wfn(bk.sphere("Curl", 0.018 * s, ctx.coll, "M_MoustacheNavy", location=pts[-1] + Vector((0, 0, 0.012 * s)), segments=6, rings=4), bh(ctx)))
        else:
            p, _ = ctx.shape.face(sgn * 0.05 * s, 0.05 * s, zc, 0.02 * s)
            m = bk.sphere("Mous", 0.06 * s, ctx.coll, "M_Mustache", location=p, scale=(1.4, 0.6, 0.55), segments=10, rings=6)
            m.rotation_euler = (0, math.radians(-12 * sgn), 0)
            parts.append(wfn(m, bh(ctx)))
    return finish(ctx, name, parts)


def acc_watch(ctx, side="L"):
    s = ctx.s
    rig = ctx.rig
    E, H = rig.head["Hand." + side], rig.tail["Hand." + side]
    d = (H - E).normalized()
    c = H - d * 0.075 * s
    band = tube("WatchBand", ctx.coll, circle_pts(c, d, 0.052 * s, 12), 0.014 * s, "M_Gold", sides=5, closed=True)
    out = Vector((-1, 0, 0)) if side == "L" else Vector((1, 0, 0))
    dial = bk.cylinder("WatchFace", 0.03 * s, 0.014 * s, ctx.coll, "M_Gold", location=c + out * 0.055 * s, rotation=(0, math.radians(90), 0), segments=10)
    return finish(ctx, "Acc_Watch", [rigid(band, "Hand." + side), rigid(dial, "Hand." + side)])


def acc_glasses_cateye(ctx):
    s = ctx.s
    k = fa.EYE_SCALE
    parts = []
    for side in (-1, 1):
        poly = [(-0.065, -0.05), (0.055, -0.058), (0.085, 0.035), (0.07, 0.065), (-0.06, 0.04)]
        pts = [ctx.eye_pt(side, px, py, 0.026 * s)[0] for px, py in poly]
        parts.append(wfn(tube("Frame", ctx.coll, pts, 0.012 * s, "M_GlassesNavy", sides=5, closed=True), bh(ctx)))
        outer = pts[3]
        back = ctx.shape.surface(side * 1.5, outer.z, 0.012 * s)
        mid = ctx.shape.surface(side * 1.0, outer.z, 0.02 * s)
        parts.append(wfn(tube("Temple", ctx.coll, [outer, mid, back], 0.008 * s, "M_GlassesNavy", sides=4), bh(ctx)))
    a, _ = ctx.shape.face(-0.02 * k, (fa.EY + 0.02) * k, ctx.face["eyes_zc"], 0.03 * s)
    b, _ = ctx.shape.face(0.02 * k, (fa.EY + 0.02) * k, ctx.face["eyes_zc"], 0.03 * s)
    parts.append(wfn(tube("Bridge", ctx.coll, [a, b], 0.009 * s, "M_GlassesNavy", sides=4), bh(ctx)))
    return finish(ctx, "Acc_GlassesCatEye", parts)


def acc_tight_bun(ctx):
    s, sh = ctx.s, ctx.shape
    bun = bk.sphere("Bun", 0.1 * s, ctx.coll, "M_HairNavy", location=(0.03 * s, -0.11 * s, sh.zmax + 0.03 * s), scale=(1, 0.95, 0.9), segments=10, rings=7)
    base = bk.sphere("BunBase", 0.08 * s, ctx.coll, "M_HairNavy", location=(0.02 * s, -0.09 * s, sh.zmax - 0.04 * s), scale=(1.2, 1.2, 0.6), segments=8, rings=5)
    rigid(bun, "Top")
    rigid(base, "Top")
    return finish(ctx, "Acc_TightBun", [bun, base, hull_from(bun, 0.012 * s)])


def acc_sunglasses_up(ctx):
    """Selin: dark sunglasses perched on top of the head."""
    s, sh = ctx.s, ctx.shape
    parts = []
    z = sh.zmax - 0.01 * s
    for sgn in (-1, 1):
        lens = bk.sphere("Lens", 0.085 * s, ctx.coll, "M_LensDark", location=(sgn * 0.1 * s, 0.07 * s, z), scale=(1.2, 0.95, 0.28), segments=10, rings=5)
        lens.rotation_euler = (math.radians(-28), 0, math.radians(-8 * sgn))
        parts.append(rigid(lens, "Top"))
    parts.append(rigid(bk.box("Bridge", (0.06 * s, 0.014 * s, 0.014 * s), ctx.coll, "M_GlassesNavy", location=(0, 0.095 * s, z + 0.02 * s)), "Top"))
    return finish(ctx, "Acc_SunglassesUp", parts)


def acc_silk_scarf(ctx):
    """Selin: flashy yellow scarf with teal stripes (one closed striped tube), knotted at the side."""
    s, sh = ctx.s, ctx.shape
    z = 0.67 * s
    n = 15
    pts = [sh.surface(-math.pi + TAU * i / n, z + 0.012 * s * math.cos(-math.pi + TAU * i / n), 0.03 * s) for i in range(n)]
    wrap = wfn(tube("Wrap", ctx.coll, pts, 0.045 * s, ["M_ScarfYellow", "M_ScarfTeal"], sides=6, closed=True, flat=(1.0, 0.7),
                    seg_mats=[0, 0, 1]), bh(ctx))
    knot_p = sh.surface(-0.45, z - 0.02 * s, 0.07 * s)
    parts = [wrap, wfn(bk.sphere("Knot", 0.05 * s, ctx.coll, "M_ScarfYellow", location=knot_p, segments=8, rings=6), bh(ctx))]
    for k, (dx, ln, mat) in enumerate(((-0.07, 0.2, "M_ScarfYellow"), (0.05, 0.16, "M_ScarfTeal"))):
        a = knot_p
        b = knot_p + Vector((dx * s, 0.04 * s, -ln * s))
        tp = [a, a.lerp(b, 0.5) + Vector((dx * 0.4 * s, 0.02 * s, 0)), b]
        parts.append(wfn(tube("Tail%d" % k, ctx.coll, tp, [0.03 * s, 0.045 * s, 0.055 * s], mat, sides=5, flat=(1.0, 0.3)),
                         lambda co, top=a.z: {"Body": max(0.0, 1 - (top - co.z) / (0.1 * s)), "Tail": min(1.0, (top - co.z) / (0.1 * s))}))
    return finish(ctx, "Acc_SilkScarf", parts)


def acc_earrings(ctx):
    s, sh = ctx.s, ctx.shape
    parts = []
    z = 0.86 * s
    for sgn in (-1, 1):
        p = sh.surface(sgn * 1.45, z, 0.02 * s)
        parts.append(wfn(bk.sphere("Earring", 0.03 * s, ctx.coll, "M_Gold", location=p - Vector((0, 0, 0.03 * s)), segments=8, rings=6), bh(ctx)))
    return finish(ctx, "Acc_Earrings", parts)


def acc_dark_glasses(ctx):
    """Kemal: black shades sitting a little low so the brows and the top of the eyes peek over."""
    s = ctx.s
    k = fa.EYE_SCALE
    zc = ctx.face["eyes_zc"]
    parts = []
    for side in (-1, 1):
        poly = [(-0.072, -0.072), (0.068, -0.078), (0.08, 0.0), (-0.07, 0.006)]
        lens = surface_poly("Lens", ctx.coll, ctx.shape, [fa.eye_pt(fa.EX * side + side * x, fa.EY + y) for x, y in poly],
                            0.022 * s, 0.014 * s, "M_LensDark", rings=2, sub=2, zc=zc)
        parts.append(wfn(lens, bh(ctx)))
        outer, _ = ctx.eye_pt(side, 0.078, -0.005, 0.03 * s)
        back = ctx.shape.surface(side * 1.5, outer.z, 0.012 * s)
        mid = ctx.shape.surface(side * 1.0, outer.z, 0.02 * s)
        parts.append(wfn(tube("Temple", ctx.coll, [outer, mid, back], 0.009 * s, "M_PlasticDark", sides=4), bh(ctx)))
    a, _ = ctx.shape.face(-0.012 * k, (fa.EY - 0.005) * k, zc, 0.034 * s)
    b, _ = ctx.shape.face(0.012 * k, (fa.EY - 0.005) * k, zc, 0.034 * s)
    parts.append(wfn(tube("Bridge", ctx.coll, [a, b], 0.011 * s, "M_PlasticDark", sides=4), bh(ctx)))
    return finish(ctx, "Acc_DarkGlasses", parts)


def acc_trench(ctx):
    """Kemal: popped trench collar round the back and sides + belt (the jacket slot is swapped to the trench colour)."""
    s, sh = ctx.s, ctx.shape
    parts = []
    for sgn in (-1, 1):
        lo_pts, hi_pts = [], []
        for k in range(7):
            th = sgn * (0.5 + 2.64 * k / 6)
            lo_pts.append(sh.surface(th, 0.64 * s, 0.025 * s))
            hi_pts.append(sh.surface(th, 0.8 * s, 0.07 * s) + Vector((0, 0, 0.025 * s * (1 - k / 6))))
        bm = bmesh.new()
        lo = [bm.verts.new(p) for p in lo_pts]
        hi = [bm.verts.new(p) for p in hi_pts]
        inward = [(-Vector((p.x, p.y, 0))).normalized() * 0.018 * s for p in lo_pts]
        lo_in = [bm.verts.new(p + d) for p, d in zip(lo_pts, inward)]
        hi_in = [bm.verts.new(p + d) for p, d in zip(hi_pts, inward)]
        for k in range(6):
            bm.faces.new((lo[k], lo[k + 1], hi[k + 1], hi[k]))
            bm.faces.new((lo_in[k + 1], lo_in[k], hi_in[k], hi_in[k + 1]))
            bm.faces.new((hi[k], hi[k + 1], hi_in[k + 1], hi_in[k]))
        bm.faces.new((lo[0], hi[0], hi_in[0], lo_in[0]))
        bm.faces.new((lo[-1], lo_in[-1], hi_in[-1], hi[-1]))
        bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
        col = wfn(_obj_from_bm("Collar", bm, ctx.coll, "M_SuitDefault"), bh(ctx))
        parts += [col, hull_from(col, 0.008 * s)]
    belt = ring_band("Belt", ctx.coll, sh, lambda th: 0.36 * s, "M_TrenchBelt", 0.024 * s, 0.026 * s, n=16, sides=4, flat=(1.0, 0.45))
    parts.append(wfn(belt, bh(ctx)))
    parts.append(wfn(bk.box("Buckle", (0.065 * s, 0.016 * s, 0.055 * s), ctx.coll, "M_Gold", location=ctx.front(0.36 * s, 0.045 * s, 0.0)[0]), bh(ctx)))
    for row in range(2):
        for sgn in (-1, 1):
            p, _ = ctx.front((0.53 - 0.08 * row) * s, 0.024 * s, sgn * 0.075 * s)
            parts.append(wfn(bk.sphere("Btn", 0.017 * s, ctx.coll, "M_TrenchBelt", location=p, scale=(1, 0.5, 1), segments=6, rings=4), bh(ctx)))
    return finish(ctx, "Acc_Trench", parts)


# ------------------------------------------------------------------------------------------ hand props

def posed_to_rest(rig, bone, obj):
    """Bake an object modelled in the *current pose* back into rest space of `bone` (rigidly bound later)."""
    bpy.context.view_layer.update()
    pb = rig.obj.pose.bones[bone]
    deform = pb.matrix @ rig.obj.data.bones[bone].matrix_local.inverted()
    obj.data.transform(deform.inverted() @ obj.matrix_world)
    obj.matrix_world = Matrix.Identity(4)
    cr.weights_rigid(obj, bone)
    return obj


def hand_pos(rig, side):
    bpy.context.view_layer.update()
    return rig.obj.pose.bones["Hand." + side].tail.copy()


def head_point(rig, local):
    """A point given in rest space, carried by the Head bone in the current pose."""
    bpy.context.view_layer.update()
    pb = rig.obj.pose.bones["Head"]
    deform = pb.matrix @ rig.obj.data.bones["Head"].matrix_local.inverted()
    return deform @ Vector(local)


def prop_handset(ctx, coll):
    """Desk phone handset at the ear (modelled in the current = phone pose)."""
    s = ctx.s
    hand = hand_pos(ctx.rig, "R")
    ear = head_point(ctx.rig, (0.31 * s, 0.02 * s, 0.86 * s))
    mouth = head_point(ctx.rig, (0.17 * s, 0.25 * s, 0.68 * s))
    mid = (ear + mouth) / 2 + (hand - (ear + mouth) / 2) * 0.35
    bar = tube("HandsetBar", coll, [ear, mid, mouth], 0.024 * s, "M_PlasticDark", sides=6)
    e = bk.sphere("Ear", 0.042 * s, coll, "M_PlasticDark", location=ear, scale=(1, 1, 0.6), segments=8, rings=5)
    mo = bk.sphere("Mouthpiece", 0.04 * s, coll, "M_PlasticDark", location=mouth, scale=(1, 1, 0.6), segments=8, rings=5)
    parts = [posed_to_rest(ctx.rig, "Hand.R", apply_xf(o)) for o in (bar, e, mo)]
    o = join(parts, "Prop_Handset")
    cr.bind(o, ctx.rig.obj)
    return o


def prop_mobile(ctx, coll):
    """Smartphone pressed to the ear (same pose)."""
    s = ctx.s
    hand = hand_pos(ctx.rig, "R")
    ear = head_point(ctx.rig, (0.3 * s, 0.05 * s, 0.84 * s))
    c = ear.lerp(hand, 0.5)
    body = bk.box("Phone", (0.02 * s, 0.08 * s, 0.16 * s), coll, "M_PlasticDark", location=c)
    body.rotation_euler = (math.radians(-20), 0, 0)
    scr = bk.box("Screen", (0.004 * s, 0.068 * s, 0.135 * s), coll, "M_Screen", location=c + Vector((0.012 * s, 0, 0)))
    scr.rotation_euler = body.rotation_euler
    parts = [posed_to_rest(ctx.rig, "Hand.R", apply_xf(o)) for o in (body, scr)]
    o = join(parts, "Prop_Mobile")
    cr.bind(o, ctx.rig.obj)
    return o


def prop_clipboard(ctx, coll, side="L"):
    """Clipboard held at the chest (modelled in the current pose)."""
    s = ctx.s
    hand = hand_pos(ctx.rig, side)
    sx = -1 if side == "L" else 1
    c = hand + Vector((-sx * 0.1 * s, 0.03 * s, 0.08 * s))
    board = bk.box("Board", (0.24 * s, 0.014 * s, 0.32 * s), coll, "M_Clipboard", location=c)
    paper = bk.box("Paper", (0.2 * s, 0.004 * s, 0.25 * s), coll, "M_Paper", location=c + Vector((0, 0.009 * s, -0.015 * s)))
    clip = bk.box("Clip", (0.1 * s, 0.02 * s, 0.04 * s), coll, "M_Metal", location=c + Vector((0, 0.01 * s, 0.15 * s)))
    lines = [bk.box("Line", (0.15 * s, 0.002 * s, 0.009 * s), coll, "M_PlasticDark", location=c + Vector((0, 0.0115 * s, (0.07 - 0.045 * i) * s))) for i in range(4)]
    tilt = Matrix.Translation(c) @ Matrix.Rotation(math.radians(-35), 4, "X") @ Matrix.Translation(-c)
    objs = [board, paper, clip] + lines
    for o in objs:
        bpy.context.view_layer.update()
        o.matrix_world = tilt @ o.matrix_world
    parts = [posed_to_rest(ctx.rig, "Hand." + side, apply_xf(o)) for o in objs]
    o = join(parts, "Prop_Clipboard")
    cr.bind(o, ctx.rig.obj)
    return o


def prop_box(ctx, coll):
    """Cardboard box of desk belongings for the fired walk-out (bound to Body, modelled in the fired pose)."""
    s = ctx.s
    hl, hr = hand_pos(ctx.rig, "L"), hand_pos(ctx.rig, "R")
    c = (hl + hr) / 2
    w = max(0.3 * s, abs(hr.x - hl.x) - 0.11 * s)
    d, h = 0.26 * s, 0.19 * s
    base = Vector((c.x, c.y + 0.02 * s, c.z - 0.075 * s))
    objs = [bk.box("Bottom", (w, d, 0.012 * s), coll, "M_Cardboard", location=base)]
    for sy in (-1, 1):
        objs.append(bk.box("WallY", (w, 0.012 * s, h), coll, "M_Cardboard", location=base + Vector((0, sy * d / 2, h / 2))))
    for sx in (-1, 1):
        objs.append(bk.box("WallX", (0.012 * s, d, h), coll, "M_Cardboard", location=base + Vector((sx * w / 2, 0, h / 2))))
    flap = bk.box("Flap", (w, 0.012 * s, 0.1 * s), coll, "M_Cardboard", location=base + Vector((0, d / 2 + 0.035 * s, h + 0.03 * s)))
    flap.rotation_euler = (math.radians(-55), 0, 0)
    objs.append(flap)
    objs.append(bk.cylinder("Pot", 0.045 * s, 0.08 * s, coll, "M_Pot", location=base + Vector((-w * 0.25, -0.03 * s, h + 0.01 * s)), segments=8))
    for i in range(4):
        a = i * 1.6
        leaf = bk.sphere("Leaf", 0.05 * s, coll, "M_LeafLight",
                         location=base + Vector((-w * 0.25 + 0.03 * s * math.cos(a), -0.03 * s + 0.03 * s * math.sin(a), h + 0.09 * s + 0.025 * s * i)),
                         scale=(0.5, 1.0, 0.35), segments=6, rings=4)
        leaf.rotation_euler = (0.3 * i, 0.6, a)
        objs.append(leaf)
    objs.append(bk.cylinder("Mug", 0.042 * s, 0.1 * s, coll, "M_BossMug", location=base + Vector((w * 0.24, 0.02 * s, h - 0.01 * s)), segments=8))
    papers = bk.box("Papers", (0.2 * s, 0.16 * s, 0.03 * s), coll, "M_Paper", location=base + Vector((0.0, 0.0, h - 0.015 * s)))
    papers.rotation_euler = (0.15, 0.1, 0.2)
    objs.append(papers)
    frame = bk.box("Frame", (0.11 * s, 0.016 * s, 0.085 * s), coll, "M_DarkWood", location=base + Vector((0.03 * s, -0.08 * s, h + 0.03 * s)))
    frame.rotation_euler = (-0.3, 0, 0.1)
    objs.append(frame)
    parts = [posed_to_rest(ctx.rig, "Body", apply_xf(o)) for o in objs]
    o = join(parts, "Prop_Box")
    cr.bind(o, ctx.rig.obj)
    return o
