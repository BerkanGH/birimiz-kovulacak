"""v3 room props: the private office (seen up close in first person) and the meeting room.

Conventions (bk_common): metres, Blender axes, a prop's front/usable side faces -Y (Unity -Z), pivot at the bottom
centre; wall-mounted props: pivot at the back centre at their mounting height. Chairs: the backrest is at -Y, the
sitter faces +Y (the Unity builder rotates them by the sitter's yaw).
Named sub-objects in DRIVEN_ROOMS are kept separate on export and driven by Scripts/Rooms (screen, handset, ring light,
drawer, clock hands, lamp glow, decal surfaces, chair fabric...). Everything else is merged per prop.
These props are close to the camera, so they are rounder and more detailed than the v2 map props.
"""
import math
import random

import bmesh
import bpy
from mathutils import Vector

import bk_common as bk
from map_kit import uv_plane, cable, multi_box_obj

R = math.radians
B = bk.box
CY = bk.cylinder
SP = bk.sphere

ROOM_PROPS = []

# Sub-objects the runtime finds by name. Merged with map_kit.DRIVEN by build_rooms.py before export.
DRIVEN_ROOMS = {
    "Screen", "PcLed", "Handset", "RingLight", "PhoneDisplay", "Plug", "Drawer", "Coffee", "Stripe", "HourHand",
    "MinuteHand", "Glow", "CorkSurface", "Photo", "Fabric", "Canvas", "BoardSurface", "Spill", "Mitten",
    # v5 cassette recorder
    "RecButton", "RecLight", "TapeIn", "ReelL", "ReelR", "Cassette0", "Cassette1", "Cassette2",
}
# Driven meshes that carry their own 0..1 UVs (never box-projected).
OWN_UV = {"Screen", "CorkSurface", "Photo", "Canvas", "BoardSurface"}


def room_prop(fn):
    ROOM_PROPS.append(fn)
    return fn


def root(name, c):
    return bk.empty(name, c)


# ================================================================================================ geometry helpers

def rrect_pts(w, d, r, seg=5):
    r = max(0.0005, min(r, w / 2 - 1e-4, d / 2 - 1e-4))
    pts = []
    for cx, cy, a0 in ((w / 2 - r, d / 2 - r, 0), (-w / 2 + r, d / 2 - r, 90), (-w / 2 + r, -d / 2 + r, 180), (w / 2 - r, -d / 2 + r, 270)):
        for i in range(seg + 1):
            a = R(a0 + 90 * i / seg)
            pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    return pts


def stadium_pts(length, width, seg=14):
    """Racetrack outline, long along Y."""
    r = width / 2
    half = length / 2 - r
    pts = []
    for i in range(seg + 1):
        a = R(180 * i / seg)
        pts.append((r * math.cos(a), half + r * math.sin(a)))
    for i in range(seg + 1):
        a = R(180 + 180 * i / seg)
        pts.append((r * math.cos(a), -half + r * math.sin(a)))
    return pts


def ellipse_pts(rx, ry, seg=24):
    return [(rx * math.cos(2 * math.pi * i / seg), ry * math.sin(2 * math.pi * i / seg)) for i in range(seg)]


def slab(name, pts, h, coll, mat, parent=None, location=(0, 0, 0), bevel=0.0, rotation=(0, 0, 0), bevel_seg=2):
    """Extrude a closed XY outline from z=0 to z=h."""
    bm = bmesh.new()
    bot = [bm.verts.new((x, y, 0.0)) for x, y in pts]
    top = [bm.verts.new((x, y, h)) for x, y in pts]
    bm.faces.new(top)
    bm.faces.new(list(reversed(bot)))
    n = len(pts)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((bot[i], bot[j], top[j], top[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    obj = bk.mesh_obj(name, bm, coll, mat, parent, location)
    obj.rotation_euler = rotation
    if bevel > 0:
        bk.add_bevel(obj, bevel, bevel_seg, 30)
    return obj


def vslab(name, w, hgt, t, r, coll, mat, parent=None, location=(0, 0, 0), bevel=0.0, seg=5):
    """Vertical rounded panel facing -Y: width w (X), height hgt (Z up from location.z), thickness t extending from
    location.y toward -Y (location = bottom centre of its BACK face; front face at location.y - t)."""
    pts = [(x, y + hgt / 2) for x, y in rrect_pts(w, hgt, r, seg)]
    # rotation +90 about X maps local y (height) to +Z and local z (thickness) to -Y
    return slab(name, pts, t, coll, mat, parent, location, bevel, rotation=(R(90), 0, 0))


def prism_x(name, profile_yz, width, coll, mat, parent=None, location=(0, 0, 0), bevel=0.0):
    """Extrude a closed YZ profile along X (centred)."""
    bm = bmesh.new()
    l = [bm.verts.new((-width / 2, y, z)) for y, z in profile_yz]
    r = [bm.verts.new((width / 2, y, z)) for y, z in profile_yz]
    bm.faces.new(l)
    bm.faces.new(list(reversed(r)))
    n = len(profile_yz)
    for i in range(n):
        j = (i + 1) % n
        bm.faces.new((l[i], l[j], r[j], r[i]))
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    o = bk.mesh_obj(name, bm, coll, mat, parent, location)
    if bevel > 0:
        bk.add_bevel(o, bevel, 2, 30)
    return o


def half_tube_x(name, radius, length, thick, coll, mat, parent=None, location=(0, 0, 0), seg=12):
    """Half cylinder shell along X, open side down (banker's lamp shade)."""
    bm = bmesh.new()
    outer_l, outer_r, inner_l, inner_r = [], [], [], []
    for i in range(seg + 1):
        a = math.pi * i / seg
        y, z = math.cos(a), math.sin(a)
        outer_l.append(bm.verts.new((-length / 2, y * radius, z * radius)))
        outer_r.append(bm.verts.new((length / 2, y * radius, z * radius)))
        inner_l.append(bm.verts.new((-length / 2 + thick, y * (radius - thick), z * (radius - thick))))
        inner_r.append(bm.verts.new((length / 2 - thick, y * (radius - thick), z * (radius - thick))))
    for i in range(seg):
        bm.faces.new((outer_l[i], outer_r[i], outer_r[i + 1], outer_l[i + 1]))
        bm.faces.new((inner_l[i + 1], inner_r[i + 1], inner_r[i], inner_l[i]))
        bm.faces.new((outer_l[i + 1], inner_l[i + 1], inner_l[i], outer_l[i]))
        bm.faces.new((outer_r[i], inner_r[i], inner_r[i + 1], outer_r[i + 1]))
    for side in ((outer_l[0], outer_r[0], inner_r[0], inner_l[0]), (outer_l[seg], inner_l[seg], inner_r[seg], outer_r[seg])):
        bm.faces.new(side)
    bmesh.ops.recalc_face_normals(bm, faces=bm.faces)
    return bk.mesh_obj(name, bm, coll, mat, parent, location, smooth=True)


def leaf(name, length, width, coll, mat, parent=None, location=(0, 0, 0), rotation=(0, 0, 0), bend=0.25, seg=6, split=False):
    """Thin curved leaf: blade along +Z, width along X, folded along the midrib, drooping toward -Y.
    Front and back are separate vertex sets (1.5 mm apart) so smooth normals stay correct on both sides."""
    bm = bmesh.new()
    front, back = [], []
    for i in range(seg + 1):
        t = i / seg
        wdt = width * math.sin(math.pi * min(1.0, t * 1.08)) ** 0.8 * (1.0 if t < 0.95 else 0.4)
        z = length * t
        y = -bend * length * t * t
        rib = -0.003 - width * 0.06
        front.append([bm.verts.new((-wdt / 2, y, z)), bm.verts.new((0, y + rib, z)), bm.verts.new((wdt / 2, y, z))])
        back.append([bm.verts.new((-wdt / 2, y + 0.0015, z)), bm.verts.new((0, y + rib + 0.0015, z)), bm.verts.new((wdt / 2, y + 0.0015, z))])
    for i in range(seg):
        a, b2 = front[i], front[i + 1]
        bm.faces.new((a[0], a[1], b2[1], b2[0]))
        bm.faces.new((a[1], a[2], b2[2], b2[1]))
        a, b2 = back[i], back[i + 1]
        bm.faces.new((b2[0], b2[1], a[1], a[0]))
        bm.faces.new((b2[1], b2[2], a[2], a[1]))
    o = bk.mesh_obj(name, bm, coll, mat, parent, location, smooth=True)
    o.rotation_euler = rotation
    return o


def tube(name, pts, radius, coll, mat, parent=None, segments=8):
    return cable(name, pts, radius, coll, mat, parent, segments=segments)


# ================================================================================================ office: furniture

@room_prop
def rm_desk(c):
    """Mid-century walnut desk, 1.6 x 0.75, top at 0.75. Tapered legs, pencil drawer box on the left."""
    r = root("RmDesk", c)
    w, d, top = 1.6, 0.75, 0.75
    slab("Top", rrect_pts(w, d, 0.035, 4), 0.04, c, "M_RmWalnut", r, (0, 0, top - 0.04), bevel=0.008)
    B("Apron", (w - 0.12, d - 0.12, 0.08), c, "M_RmWalnutDark", r, (0, 0.0, top - 0.08), bevel=0.004)
    for sx in (-1, 1):
        for sy in (-1, 1):
            leg = CY("Leg", 0.026, 0.68, c, "M_RmWalnutDark", r, (sx * (w / 2 - 0.1), sy * (d / 2 - 0.09), 0.34), segments=12, radius_top=0.018)
            leg.rotation_euler = (R(-4 * sy), R(4 * sx), 0)
            CY("Foot", 0.02, 0.025, c, "M_RmBrass", r, (sx * (w / 2 - 0.1) + sx * 0.02, sy * (d / 2 - 0.09) + sy * 0.02, 0.0125), segments=10)
    # pencil drawer box (left), with a tiny brass pull
    B("DrawerBox", (0.5, 0.5, 0.11), c, "M_RmWalnutDark", r, (-0.45, 0.02, top - 0.095), bevel=0.006)
    B("DrawerFace", (0.46, 0.012, 0.085), c, "M_RmWalnut", r, (-0.45, -0.235, top - 0.095), bevel=0.004)
    B("DrawerPull", (0.09, 0.012, 0.012), c, "M_RmBrass", r, (-0.45, -0.246, top - 0.09), bevel=0.003)
    B("Modesty", (w - 0.24, 0.018, 0.3), c, "M_RmWalnutDark", r, (0, d / 2 - 0.1, top - 0.24), bevel=0.004)
    CY("Grommet", 0.028, 0.006, c, "M_RmBlack", r, (0.12, d / 2 - 0.08, top + 0.001), segments=14)
    return r


@room_prop
def rm_chair(c):
    """Task chair (the camera sits in it). Fabric parts take the player's colour at runtime."""
    r = root("RmChair", c)
    fab = bk.empty("Fabric", c, (0, 0, 0), r)
    slab("Seat", rrect_pts(0.5, 0.48, 0.09, 5), 0.08, c, "M_RmChairFabric", fab, (0, 0.03, 0.44), bevel=0.03)
    back = vslab("Back", 0.46, 0.5, 0.07, 0.12, c, "M_RmChairFabric", fab, (0, -0.22, 0.56), bevel=0.03)
    back.rotation_euler = (R(90 + 8), 0, 0)
    B("BackShell", (0.4, 0.03, 0.42), c, "M_RmBlack", r, (0, -0.315, 0.82), bevel=0.012).rotation_euler = (R(8), 0, 0)
    B("Spine", (0.05, 0.04, 0.3), c, "M_RmBlack", r, (0, -0.29, 0.52), bevel=0.01)
    for sx in (-1, 1):
        B("ArmPad", (0.06, 0.26, 0.035), c, "M_RmBlack", r, (sx * 0.29, 0.02, 0.66), bevel=0.014)
        B("ArmPost", (0.035, 0.035, 0.18), c, "M_RmGraphite", r, (sx * 0.29, -0.04, 0.56), bevel=0.006)
    CY("Gas", 0.026, 0.3, c, "M_Metal", r, (0, 0.02, 0.28), segments=14)
    CY("Hub", 0.05, 0.05, c, "M_RmBlack", r, (0, 0.02, 0.1), segments=14)
    for i in range(5):
        a = 2 * math.pi * i / 5 + 0.3
        leg = B("Leg", (0.3, 0.045, 0.035), c, "M_RmBlack", r, (math.cos(a) * 0.15, 0.02 + math.sin(a) * 0.15, 0.085), bevel=0.01)
        leg.rotation_euler = (0, 0, a)
        SP("Caster", 0.028, c, "M_RmBlack", r, (math.cos(a) * 0.29, 0.02 + math.sin(a) * 0.29, 0.03), segments=10, rings=6)
    return r


@room_prop
def rm_drawer_unit(c):
    """Three-drawer pedestal beside the desk; the top drawer (Drawer) slides out toward -Y when clicked."""
    r = root("RmDrawerUnit", c)
    w, d, h = 0.42, 0.56, 0.66
    B("Carcass", (w, d, h - 0.03), c, "M_RmOakLight", r, (0, 0, (h - 0.03) / 2 + 0.03), bevel=0.008)
    slab("TopBoard", rrect_pts(w + 0.02, d + 0.02, 0.012, 3), 0.025, c, "M_RmWalnut", r, (0, 0, h - 0.025), bevel=0.005)
    B("Plinth", (w - 0.04, d - 0.04, 0.03), c, "M_RmBlack", r, (0, 0, 0.015))
    for i, (z, hh) in enumerate(((0.2, 0.27), (0.43, 0.15))):
        B("Front", (w - 0.03, 0.02, hh), c, "M_RmWalnut", r, (0, -d / 2 - 0.005, z), bevel=0.005)
        B("Pull", (0.12, 0.018, 0.016), c, "M_RmBrass", r, (0, -d / 2 - 0.022, z + hh / 2 - 0.035), bevel=0.004)
    drawer = bk.empty("Drawer", c, (0, 0, 0.565), r)
    B("Face", (w - 0.03, 0.02, 0.13), c, "M_RmWalnut", drawer, (0, -d / 2 - 0.005, 0), bevel=0.005)
    B("Pull", (0.12, 0.018, 0.016), c, "M_RmBrass", drawer, (0, -d / 2 - 0.022, 0.025), bevel=0.004)
    B("LabelCard", (0.06, 0.004, 0.025), c, "M_RmLabel", drawer, (0.13, -d / 2 - 0.016, 0.03))
    B("Tray", (w - 0.06, d - 0.06, 0.09), c, "M_RmCream", drawer, (0, 0.0, -0.01))
    B("Papers", (0.22, 0.3, 0.025), c, "M_Paper", drawer, (-0.05, 0.02, 0.045)).rotation_euler = (0, 0, R(6))
    B("Folder", (0.24, 0.32, 0.012), c, "M_Manila", drawer, (-0.04, 0.03, 0.062)).rotation_euler = (0, 0, R(-4))
    B("Stapler", (0.045, 0.16, 0.035), c, "M_PlasticRed", drawer, (0.12, -0.05, 0.055), bevel=0.01)
    for k, m in enumerate(("M_PlasticRed", "M_DuckYellow", "M_PlasticBlue")):
        SP("Candy", 0.016, c, m, drawer, (0.13 + 0.02 * k, 0.12 - 0.015 * k, 0.05), scale=(1.4, 1, 0.8), segments=8, rings=5)
    return r


@room_prop
def rm_shelf(c):
    """Wall shelf above the desk (pivot: back centre at the lower board). Binders, books, a trophy, a box."""
    r = root("RmShelf", c)
    w, d = 1.0, 0.24
    rnd = random.Random(5)
    for z in (0.0, 0.38):
        slab("Board", rrect_pts(w, d, 0.01, 2), 0.03, c, "M_RmWalnut", r, (0, -d / 2, z), bevel=0.004)
    for sx in (-0.38, 0.38):
        for z in (0.0, 0.38):
            B("Bracket", (0.02, 0.17, 0.02), c, "M_RmBrass", r, (sx, -0.09, z - 0.012), bevel=0.004)
            B("BracketV", (0.02, 0.02, 0.06), c, "M_RmBrass", r, (sx, -0.012, z - 0.04), bevel=0.004)
    # binders on the lower board
    mats = ["M_RmBinderRed", "M_RmBinderBlue", "M_RmBinderGreen", "M_RmBinderMustard", "M_RmBinderGrey", "M_RmBinderBlue", "M_RmBinderRed"]
    x = -w / 2 + 0.06
    for i, m in enumerate(mats):
        bw = 0.065
        tilt = R(8) if i == len(mats) - 1 else 0
        b = B("Binder", (bw, 0.21, 0.3), c, m, r, (x + bw / 2, -0.12, 0.03 + 0.15), bevel=0.004)
        b.rotation_euler = (0, tilt, 0)
        B("Label", (0.004, 0.06, 0.12), c, "M_RmLabel", r, (x + bw / 2, -0.226, 0.03 + 0.17)).rotation_euler = (0, tilt, R(90))
        CY("Ring", 0.012, 0.004, c, "M_Metal", r, (x + bw / 2, -0.228, 0.08), rotation=(R(90), 0, 0), segments=10)
        x += bw + 0.004
    # magazine file box
    B("FileBox", (0.12, 0.2, 0.24), c, "M_Manila", r, (x + 0.1, -0.12, 0.03 + 0.12), bevel=0.004)
    # upper board: books, a trophy, a small box
    x = -w / 2 + 0.05
    for i in range(9):
        bw = rnd.uniform(0.025, 0.045)
        bh = rnd.uniform(0.17, 0.24)
        m = rnd.choice(["M_RmBinderRed", "M_RmBinderGreen", "M_RmCream", "M_RmBinderBlue", "M_RmBinderMustard", "M_RmWalnutDark"])
        B("Book", (bw, 0.16, bh), c, m, r, (x + bw / 2, -0.11, 0.41 + bh / 2), bevel=0.003)
        x += bw + 0.003
    lean = B("BookLean", (0.035, 0.16, 0.22), c, "M_RmBinderGrey", r, (x + 0.06, -0.11, 0.41 + 0.105), bevel=0.003)
    lean.rotation_euler = (0, R(-24), 0)
    # trophy
    tx = 0.18
    B("TrophyBase", (0.07, 0.07, 0.04), c, "M_RmWalnutDark", r, (tx, -0.12, 0.43), bevel=0.004)
    bk.lathe("Cup", [(0, 0), (0.012, 0), (0.012, 0.05), (0.04, 0.09), (0.045, 0.13), (0.04, 0.13), (0.0, 0.1)], c, "M_RmBrass", r, (tx, -0.12, 0.45), segments=16)
    for sx in (-1, 1):
        bk.torus("Handle", 0.022, 0.005, c, "M_RmBrass", r, (tx + sx * 0.045, -0.12, 0.555), rotation=(R(90), 0, 0), major_seg=10, minor_seg=5)
    # small box + globe-ish ball
    B("Box", (0.14, 0.12, 0.08), c, "M_RmBinderMustard", r, (0.36, -0.11, 0.45), bevel=0.006)
    SP("Ball", 0.035, c, "M_RmBinderBlue", r, (0.36, -0.11, 0.525), segments=12, rings=8)
    return r


@room_prop
def rm_cork_board(c):
    """Cork board (pivot: back centre at its centre). CorkSurface carries the pinned-notes decal (corkboard_notes); in a
    v5 match the suç tahtası is drawn into it at runtime. No 3D pins: the art has its own and they would cover names."""
    r = root("RmCorkBoard", c)
    w, h = 0.8, 0.6
    vslab("Frame", w + 0.05, h + 0.05, 0.025, 0.012, c, "M_RmWalnut", r, (0, 0.0, -(h + 0.05) / 2), bevel=0.004)
    uv_plane("CorkSurface", w, h, c, "M_RmCork", r, (0, -0.0262, 0))
    return r


@room_prop
def rm_clock(c):
    """Wall clock (pivot: back centre). HourHand / MinuteHand rotate about the clock's forward axis."""
    r = root("RmClock", c)
    rad = 0.16
    CY("Body", rad + 0.012, 0.045, c, "M_RmWalnutDark", r, (0, -0.0225, 0), rotation=(R(90), 0, 0), segments=32, cap_bevel=0.006)
    CY("Face", rad - 0.008, 0.004, c, "M_RmClockFace", r, (0, -0.046, 0), rotation=(R(90), 0, 0), segments=32)
    bk.torus("Rim", rad, 0.01, c, "M_RmBrass", r, (0, -0.047, 0), rotation=(R(90), 0, 0), major_seg=32, minor_seg=6)
    for i in range(12):
        a = 2 * math.pi * i / 12
        big = i % 3 == 0
        m = B("Mark", (0.008 if big else 0.005, 0.003, 0.028 if big else 0.016), c, "M_RmBlack", r,
              (math.sin(a) * (rad - 0.03), -0.0495, math.cos(a) * (rad - 0.03)))
        m.rotation_euler = (0, a, 0)
    hh = bk.empty("HourHand", c, (0, -0.052, 0), r)
    B("Blade", (0.012, 0.003, 0.075), c, "M_RmBlack", hh, (0, 0, 0.03), bevel=0.002)
    mh = bk.empty("MinuteHand", c, (0, -0.056, 0), r)
    B("Blade", (0.008, 0.003, 0.115), c, "M_RmBlack", mh, (0, 0, 0.045), bevel=0.002)
    CY("Cap", 0.01, 0.006, c, "M_RmBrass", r, (0, -0.06, 0), rotation=(R(90), 0, 0), segments=12)
    return r


@room_prop
def rm_plant(c):
    """Monstera in a terracotta pot with saucer."""
    r = root("RmPlant", c)
    bk.lathe("Saucer", [(0, 0), (0.2, 0), (0.21, 0.025), (0.19, 0.025), (0.18, 0.012), (0, 0.012)], c, "M_RmTerracotta", r, segments=24)
    bk.lathe("Pot", [(0, 0.02), (0.14, 0.02), (0.18, 0.36), (0.19, 0.4), (0.17, 0.4), (0.16, 0.37), (0, 0.37)], c, "M_RmTerracotta", r, segments=24)
    CY("Soil", 0.165, 0.02, c, "M_Soil", r, (0, 0, 0.37), segments=20)
    rnd = random.Random(17)
    for i in range(13):
        a = 2 * math.pi * i / 13 + rnd.uniform(-0.25, 0.25)
        tilt = rnd.uniform(15, 50)
        ln = rnd.uniform(0.32, 0.62)
        stem_top = Vector((math.cos(a) * ln * math.sin(R(tilt)), math.sin(a) * ln * math.sin(R(tilt)), 0.38 + ln * math.cos(R(tilt))))
        tube("Stem", [(0, 0, 0.36), tuple(stem_top * 0.5 + Vector((0, 0, 0.19))), tuple(stem_top)], 0.007, c, "M_RmLeafDeep", r, segments=5)
        size = rnd.uniform(0.3, 0.42)
        lf = leaf("Leaf", size * 1.15, size, c, "M_RmLeafDeep" if i % 3 else "M_RmLeafBright", r, tuple(stem_top),
                  rotation=(R(rnd.uniform(55, 95)), 0, a + math.pi / 2), bend=0.3, seg=7)
    return r


@room_prop
def rm_cactus(c):
    r = root("RmCactus", c)
    bk.lathe("Pot", [(0, 0), (0.045, 0), (0.055, 0.075), (0.058, 0.08), (0.05, 0.08), (0.0, 0.07)], c, "M_RmPotCream", r, segments=16)
    SP("Body", 0.04, c, "M_RmLeafDeep", r, (0, 0, 0.12), scale=(1, 1, 1.5), segments=12, rings=10)
    SP("Arm", 0.02, c, "M_RmLeafDeep", r, (0.035, 0, 0.13), scale=(1, 1, 1.5), segments=8, rings=6)
    SP("Flower", 0.014, c, "M_StickyPink", r, (0, 0, 0.185), segments=8, rings=5)
    return r


@room_prop
def rm_small_plant(c):
    """Leafy pot for the top of the drawer unit / credenzas."""
    r = root("RmSmallPlant", c)
    bk.lathe("Pot", [(0, 0), (0.07, 0), (0.085, 0.13), (0.09, 0.14), (0.08, 0.14), (0.0, 0.125)], c, "M_RmPotCream", r, segments=18)
    rnd = random.Random(8)
    for i in range(9):
        a = 2 * math.pi * i / 9 + rnd.uniform(-0.2, 0.2)
        leaf("Leaf", rnd.uniform(0.14, 0.22), 0.07, c, "M_RmLeafBright" if i % 2 else "M_RmLeafDeep", r, (0, 0, 0.13),
             rotation=(R(rnd.uniform(20, 55)), 0, a + math.pi / 2), bend=0.3, seg=5)
    return r


@room_prop
def rm_tall_plant(c):
    """Snake plant in a tall cream pot (meeting room corners)."""
    r = root("RmTallPlant", c)
    bk.lathe("Pot", [(0, 0), (0.15, 0), (0.17, 0.42), (0.18, 0.45), (0.16, 0.45), (0.0, 0.42)], c, "M_RmPotCream", r, segments=20)
    rnd = random.Random(23)
    for i in range(14):
        a = 2 * math.pi * i / 14 + rnd.uniform(-0.3, 0.3)
        rr = rnd.uniform(0.02, 0.1)
        leaf("Blade", rnd.uniform(0.55, 0.95), rnd.uniform(0.06, 0.09), c, "M_RmLeafDeep" if i % 3 else "M_RmLeafBright", r,
             (math.cos(a) * rr, math.sin(a) * rr, 0.43), rotation=(R(rnd.uniform(4, 18)), 0, a + math.pi / 2), bend=0.08, seg=5)
    return r


@room_prop
def rm_bin(c):
    r = root("RmBin", c)
    bk.lathe("Bin", [(0, 0), (0.12, 0), (0.15, 0.32), (0.155, 0.325), (0.145, 0.325), (0.115, 0.012), (0, 0.012)], c, "M_RmGraphite", r, segments=22)
    bk.torus("Lip", 0.15, 0.007, c, "M_RmBrass", r, (0, 0, 0.322), major_seg=22, minor_seg=5)
    for k in range(3):
        bk.ico("Paper", 0.045, c, "M_Paper", r, (0.03 * (k - 1), 0.02 * ((k * 2) % 3 - 1), 0.28 + 0.02 * k), subdiv=1, jitter=0.3, seed=k + 2)
    return r


@room_prop
def rm_rug(c):
    r = root("RmRug", c)
    CY("Rug", 0.95, 0.012, c, "M_RmRugTeal", r, (0, 0, 0.006), segments=48)
    bk.torus("Border", 0.88, 0.012, c, "M_RmRugMustard", r, (0, 0, 0.011), major_seg=48, minor_seg=4).scale = (1, 1, 0.3)
    return r


@room_prop
def rm_coat_rack(c):
    r = root("RmCoatRack", c)
    CY("Base", 0.2, 0.03, c, "M_RmWalnutDark", r, (0, 0, 0.015), segments=20, cap_bevel=0.008)
    CY("Pole", 0.022, 1.75, c, "M_RmWalnutDark", r, (0, 0, 0.9), segments=12)
    SP("Knob", 0.035, c, "M_RmBrass", r, (0, 0, 1.79), segments=12, rings=8)
    for i in range(4):
        a = 2 * math.pi * i / 4 + 0.4
        tube("Hook", [(0, 0, 1.6), (math.cos(a) * 0.12, math.sin(a) * 0.12, 1.66), (math.cos(a) * 0.17, math.sin(a) * 0.17, 1.72)], 0.01, c, "M_RmBrass", r, segments=6)
    # trench coat draped from the front hook
    SP("CoatShoulders", 0.16, c, "M_RmCoat", r, (0, -0.12, 1.5), scale=(1.3, 0.6, 0.5), segments=14, rings=8)
    bk.lathe("Coat", [(0, 0.55), (0.24, 0.6), (0.2, 1.0), (0.17, 1.42), (0.12, 1.5), (0, 1.52)], c, "M_RmCoat", r, (0, -0.12, 0), segments=16, scale=(1.0, 0.55, 1.0))
    B("Belt", (0.36, 0.2, 0.05), c, "M_RmWalnutDark", r, (0, -0.12, 1.05), bevel=0.01)
    # hat on top
    CY("HatBrim", 0.17, 0.012, c, "M_RmCoatDark", r, (0.05, 0.0, 1.83), segments=20).rotation_euler = (R(8), R(-6), 0)
    CY("HatCrown", 0.1, 0.1, c, "M_RmCoatDark", r, (0.05, 0.005, 1.89), segments=18, radius_top=0.085).rotation_euler = (R(8), R(-6), 0)
    CY("HatBand", 0.102, 0.022, c, "M_RmBinderRed", r, (0.05, 0.003, 1.855), segments=18).rotation_euler = (R(8), R(-6), 0)
    return r


@room_prop
def rm_file_cabinet(c):
    """Two-drawer graphite filing cabinet with a retro desk fan and a box file on top (office right wall)."""
    r = root("RmFileCabinet", c)
    w, d, h = 0.46, 0.55, 0.72
    B("Body", (w, d, h), c, "M_RmGraphite", r, (0, 0, h / 2), bevel=0.01)
    for i in range(2):
        z = 0.2 + i * 0.33
        B("Front", (w - 0.04, 0.02, 0.3), c, "M_RmGraphite", r, (0, -d / 2 - 0.006, z), bevel=0.006)
        B("Handle", (0.14, 0.025, 0.02), c, "M_RmBrass", r, (0, -d / 2 - 0.022, z + 0.08), bevel=0.004)
        B("Label", (0.08, 0.006, 0.04), c, "M_RmLabel", r, (0, -d / 2 - 0.018, z + 0.03))
    # desk fan
    fx, fy = -0.08, -0.02
    bk.lathe("FanBase", [(0, 0), (0.09, 0), (0.085, 0.025), (0.03, 0.04), (0, 0.04)], c, "M_RmCream", r, (fx, fy, h), segments=20)
    CY("FanNeck", 0.012, 0.2, c, "M_RmCream", r, (fx, fy, h + 0.13), segments=10)
    head = (fx, fy - 0.02, h + 0.27)
    CY("Motor", 0.045, 0.08, c, "M_RmCream", r, (head[0], head[1] + 0.05, head[2]), rotation=(R(90), 0, 0), segments=16, cap_bevel=0.01)
    bk.torus("Guard", 0.12, 0.006, c, "M_Metal", r, (head[0], head[1] - 0.02, head[2]), rotation=(R(90), 0, 0), major_seg=28, minor_seg=5)
    for k in range(6):
        a = R(k * 30)
        B("Wire", (0.24, 0.003, 0.003), c, "M_Metal", r, (head[0], head[1] - 0.045, head[2])).rotation_euler = (0, a, 0)
    for k in range(3):
        bl = B("Blade", (0.05, 0.006, 0.1), c, "M_RmTerracotta", r, (head[0], head[1] - 0.025, head[2]))
        bl.rotation_euler = (R(10), R(120 * k), 0)
    CY("Hub", 0.02, 0.02, c, "M_RmBrass", r, (head[0], head[1] - 0.05, head[2]), rotation=(R(90), 0, 0), segments=12)
    B("BoxFile", (0.14, 0.3, 0.26), c, "M_RmBinderGreen", r, (0.14, 0.04, h + 0.13), bevel=0.006)
    return r


@room_prop
def rm_low_bookcase(c):
    """Back-wall bookcase (behind the player): books, a radio, a box file."""
    r = root("RmLowBookcase", c)
    w, d, h = 1.1, 0.34, 0.9
    for sx in (-1, 1):
        B("Side", (0.03, d, h), c, "M_RmWalnut", r, (sx * (w / 2 - 0.015), 0, h / 2), bevel=0.004)
    B("Back", (w, 0.015, h), c, "M_RmWalnutDark", r, (0, d / 2 - 0.0075, h / 2))
    for z in (0.02, 0.44, h - 0.02):
        B("Shelf", (w, d, 0.03), c, "M_RmWalnut", r, (0, 0, z), bevel=0.004)
    rnd = random.Random(31)
    for row, z0 in enumerate((0.035, 0.455)):
        x = -w / 2 + 0.05
        while x < w / 2 - 0.12:
            bw = rnd.uniform(0.028, 0.05)
            bh = rnd.uniform(0.24, 0.34)
            if rnd.random() < 0.1:
                x += 0.12
                continue
            m = rnd.choice(["M_RmBinderRed", "M_RmBinderGreen", "M_RmCream", "M_RmBinderBlue", "M_RmBinderMustard", "M_RmWalnutDark", "M_RmBinderGrey"])
            B("Book", (bw, 0.22, bh), c, m, r, (x + bw / 2, -0.03, z0 + bh / 2), bevel=0.003)
            x += bw + 0.003
    # radio on top
    B("Radio", (0.3, 0.14, 0.17), c, "M_RmTerracotta", r, (-0.25, 0, h + 0.085), bevel=0.02)
    CY("Grill", 0.05, 0.006, c, "M_RmCream", r, (-0.31, -0.072, h + 0.09), rotation=(R(90), 0, 0), segments=16)
    CY("Dial", 0.022, 0.012, c, "M_RmBrass", r, (-0.17, -0.074, h + 0.09), rotation=(R(90), 0, 0), segments=12)
    B("BoxFile", (0.26, 0.2, 0.12), c, "M_Manila", r, (0.22, 0, h + 0.06), bevel=0.004)
    return r


# ================================================================================================ office: desk items

@room_prop
def rm_monitor(c):
    """Monitor with a separate Screen surface (0.56 x 0.315, 16:9) facing -Y; screen centre at z = 0.33."""
    r = root("RmMonitor", c)
    sw, sh = 0.56, 0.315
    bw, bh = sw + 0.04, sh + 0.06
    cz = 0.33
    vslab("Bezel", bw, bh, 0.028, 0.018, c, "M_RmBezel", r, (0, 0.014, cz - bh / 2 + 0.012), bevel=0.006)
    uv_plane("Screen", sw, sh, c, "M_RmScreenOff", r, (0, -0.0152, cz + 0.008))
    vslab("Housing", bw - 0.16, bh - 0.12, 0.05, 0.05, c, "M_RmBezel", r, (0, 0.062, cz - (bh - 0.12) / 2 + 0.01), bevel=0.02)
    B("ChinLogo", (0.05, 0.003, 0.008), c, "M_RmBrass", r, (0, -0.0155, cz - sh / 2 - 0.016))
    SP("PcLed", 0.004, c, "M_LEDBlue", r, (bw / 2 - 0.03, -0.0155, cz - sh / 2 - 0.017), segments=8, rings=5)
    slab("Neck", rrect_pts(0.07, 0.03, 0.012, 3), 0.2, c, "M_RmGraphite", r, (0, 0.05, 0.02), bevel=0.006)
    slab("Foot", ellipse_pts(0.13, 0.09, 28), 0.016, c, "M_RmGraphite", r, (0, 0.03, 0.0), bevel=0.006)
    # sticky notes on the bezel
    for (x, z, m, rot) in ((bw / 2 - 0.02, cz + 0.12, "M_Sticky", 7), (bw / 2 - 0.035, cz + 0.06, "M_StickyPink", -5), (-bw / 2 + 0.015, cz - 0.1, "M_Sticky", -8)):
        n = B("Note", (0.055, 0.002, 0.055), c, m, r, (x, -0.0157, z))
        n.rotation_euler = (0, R(rot), 0)
    return r


@room_prop
def rm_keyboard(c):
    r = root("RmKeyboard", c)
    w, d = 0.42, 0.14
    prism_x("Base", [(-d / 2, 0), (d / 2, 0), (d / 2, 0.026), (-d / 2, 0.014)], w, c, "M_RmCream", r, bevel=0.005)
    boxes = []
    rows = 5
    for row in range(rows):
        y = -d / 2 + 0.018 + row * 0.024
        z = 0.016 + (y + d / 2) / d * 0.012
        n = 14 if row < 4 else 9
        kw = (w - 0.03) / 14
        x0 = -w / 2 + 0.016
        for k in range(n):
            ww = kw - 0.004
            x = x0 + k * kw
            if row == 4:
                ww = kw * (5.4 if k == 3 else 1.0) - 0.004
                x = x0 + (k * kw if k <= 3 else (k + 4.4) * kw)
            accent = (row == 4 and k == 3) or (row == 0 and k == 0) or (row == 2 and k == n - 1)
            boxes.append(((x + ww / 2, y, z + 0.006), (ww, 0.019, 0.01), "M_RmBinderMustard" if accent else "M_RmKeys"))
    multi_box_obj("Keys", boxes, c, r)
    return r


@room_prop
def rm_mouse(c):
    r = root("RmMouse", c)
    slab("Pad", rrect_pts(0.24, 0.2, 0.02, 4), 0.003, c, "M_RmWainscotDark", r, (0, 0, 0))
    SP("Mouse", 0.034, c, "M_RmCream", r, (0.0, 0.0, 0.004), scale=(0.8, 1.25, 0.55), segments=16, rings=10)
    B("Wheel", (0.006, 0.014, 0.008), c, "M_RmBlack", r, (0, -0.02, 0.022), bevel=0.002)
    cable("Cord", [(0, 0.04, 0.008), (0.0, 0.09, 0.004), (-0.03, 0.15, 0.002), (-0.08, 0.2, 0.002)], 0.0025, c, "M_RmBlack", r)
    return r


@room_prop
def rm_phone(c):
    """Desk phone: cream wedge body, keypad, LCD (PhoneDisplay), RingLight, Handset (driven) resting on the left cradle,
    CordBase / Handset/CordEnd anchors for the coiled cord (drawn at runtime), Plug = pulled-out cable (dead line)."""
    r = root("RmPhone", c)
    w, d = 0.24, 0.21
    prof = [(-d / 2, 0.0), (d / 2, 0.0), (d / 2, 0.07), (d / 2 - 0.03, 0.082), (-d / 2 + 0.015, 0.035), (-d / 2, 0.022)]
    prism_x("Body", prof, w, c, "M_RmPhoneBody", r, bevel=0.012)
    slope = math.atan2(0.082 - 0.035, (d / 2 - 0.03) - (-d / 2 + 0.015))

    def on_slope(x, yy, lift=0.0):
        # point on the sloped top at depth yy (relative to the body front)
        z = 0.035 + (yy - (-d / 2 + 0.015)) * math.tan(slope) + lift
        return (x, yy, z)
    # keypad (right half)
    for row in range(4):
        for col in range(3):
            x = 0.025 + col * 0.03
            yy = -0.07 + row * 0.028
            k = B("Key", (0.022, 0.02, 0.01), c, "M_RmPhoneDark" if row < 3 else "M_RmBinderMustard", r, on_slope(x, yy, 0.004), bevel=0.003)
            k.rotation_euler = (slope, 0, 0)
    # LCD + ring light at the back right
    disp = B("PhoneDisplay", (0.09, 0.035, 0.004), c, "M_RmPhoneLcd", r, on_slope(0.055, 0.045, 0.002))
    disp.rotation_euler = (slope, 0, 0)
    lamp = SP("RingLight", 0.011, c, "M_RmRingOff", r, on_slope(0.1, 0.06, 0.006), scale=(1, 1, 0.6), segments=12, rings=8)
    # cradle on the left half
    for yy in (-0.065, 0.06):
        B("Cradle", (0.07, 0.03, 0.02), c, "M_RmPhoneDark", r, on_slope(-0.06, yy, 0.008), bevel=0.006).rotation_euler = (slope, 0, 0)
    B("Hook", (0.012, 0.012, 0.01), c, "M_RmBrass", r, on_slope(-0.06, 0.0, 0.004), bevel=0.003)
    # handset: grip along Y, ear (back, +Y) and mouth (front, -Y) cups hanging down onto the cradle
    hz = 0.035 + (0.0 - (-d / 2 + 0.015)) * math.tan(slope) + 0.04
    hs = bk.empty("Handset", c, (-0.06, 0.0, hz), r)
    hs.rotation_euler = (slope, 0, 0)
    tube("Grip", [(0, -0.085, -0.004), (0, -0.04, 0.008), (0, 0.04, 0.008), (0, 0.085, -0.004)], 0.017, c, "M_RmPhoneBody", hs, segments=10)
    for sy in (-1, 1):
        CY("Cup", 0.03, 0.03, c, "M_RmPhoneBody", hs, (0, sy * 0.095, -0.016), segments=16, cap_bevel=0.006)
        CY("CupFace", 0.022, 0.004, c, "M_RmPhoneDark", hs, (0, sy * 0.095, -0.032), segments=14)
    bk.empty("CordEnd", c, (0, -0.1, -0.02), hs)
    bk.empty("CordBase", c, (-w / 2 - 0.002, -0.06, 0.02), r)
    # cable to the desk edge + the pulled plug (dead line)
    cable("WallCord", [(0.0, d / 2, 0.02), (0.02, d / 2 + 0.05, 0.004), (0.06, d / 2 + 0.2, 0.003)], 0.0035, c, "M_RmBlack", r)
    plug = bk.empty("Plug", c, (0, 0, 0), r)
    cable("PlugCord", [(0.09, d / 2 + 0.01, 0.004), (0.17, d / 2 - 0.02, 0.004), (0.2, -0.02, 0.004), (0.19, -0.08, 0.004)], 0.0035, c, "M_RmBlack", plug)
    pb = B("PlugBody", (0.022, 0.035, 0.016), c, "M_RmPhoneBody", plug, (0.19, -0.1, 0.009), bevel=0.004)
    pb.rotation_euler = (0, 0, R(12))
    B("PlugTab", (0.012, 0.014, 0.004), c, "M_RmBrass", plug, (0.192, -0.122, 0.009))
    return r


@room_prop
def rm_recorder(c):
    """v5 desk cassette recorder (records the connected call). Graphite body with a cream deck: speaker grille,
    REC lamp (RecLight), a row of piano keys whose red one is RecButton (pressed at runtime), the cassette well with
    TapeIn (the recorded tape, shown once a call was recorded) and its two reels ReelL / ReelR (turn while
    recording). A walnut rack on the right holds the blank tapes Cassette0..2 (one per recorder use left).
    Pivot: bottom centre of the body; the rack sticks out to +X."""
    r = root("RmRecorder", c)
    w, d, h = 0.26, 0.16, 0.052
    top = h + 0.001
    slab("Body", rrect_pts(w, d, 0.022, 5), h - 0.006, c, "M_RmGraphite", r, (0, 0, 0.004), bevel=0.006)
    slab("Deck", rrect_pts(w - 0.016, d - 0.016, 0.016, 5), 0.004, c, "M_RmCream", r, (0, 0, h - 0.003), bevel=0.0015)
    for sx in (-1, 1):
        for sy in (-1, 1):
            CY("Foot", 0.009, 0.005, c, "M_RmBlack", r, (sx * (w / 2 - 0.03), sy * (d / 2 - 0.025), 0.0025), segments=10)
    # speaker grille (left) + volume knob
    B("Grille", (0.07, 0.092, 0.002), c, "M_RmBlack", r, (-0.08, 0.012, top), bevel=0.0008)
    for k in range(7):
        B("Slot", (0.058, 0.0045, 0.0022), c, "M_RmGraphite", r, (-0.08, -0.024 + k * 0.012, top + 0.0004))
    CY("Knob", 0.011, 0.01, c, "M_RmBrass", r, (-0.1, -0.056, top + 0.004), segments=14, cap_bevel=0.002)
    B("KnobMark", (0.002, 0.008, 0.0015), c, "M_RmBlack", r, (-0.1, -0.06, top + 0.0095))
    # REC lamp in a brass bezel
    bk.torus("LampRim", 0.0085, 0.0018, c, "M_RmBrass", r, (-0.058, -0.056, top + 0.001), major_seg=14, minor_seg=4)
    SP("RecLight", 0.0068, c, "M_RmRingOff", r, (-0.058, -0.056, top + 0.001), scale=(1, 1, 0.55), segments=12, rings=8)
    # cassette well (back right) with a chrome rim
    wx, wy, ww, wd = 0.045, 0.022, 0.15, 0.088
    B("Well", (ww, wd, 0.0016), c, "M_RmBlack", r, (wx, wy, top - 0.0002))
    for (cx, cy, sx_, sy_) in ((wx, wy - wd / 2, ww + 0.006, 0.003), (wx, wy + wd / 2, ww + 0.006, 0.003),
                               (wx - ww / 2, wy, 0.003, wd), (wx + ww / 2, wy, 0.003, wd)):
        B("WellRim", (sx_, sy_, 0.003), c, "M_Metal", r, (cx, cy, top + 0.0008))
    # the recorded tape in the well, with its reels (driven)
    tape = bk.empty("TapeIn", c, (wx, wy + 0.004, top + 0.001), r)
    B("Shell", (0.112, 0.07, 0.008), c, "M_RmPhoneDark", tape, (0, 0, 0.004), bevel=0.0015)
    B("Label", (0.096, 0.026, 0.001), c, "M_RmLabel", tape, (0, -0.02, 0.0085))
    B("Band", (0.096, 0.006, 0.0012), c, "M_PlasticRed", tape, (0, -0.028, 0.009))
    B("TapeWin", (0.064, 0.024, 0.0012), c, "M_Coffee", tape, (0, 0.012, 0.0085))
    for name, sx in (("ReelL", -1), ("ReelR", 1)):
        reel = bk.empty(name, c, (wx + sx * 0.022, wy + 0.004 + 0.012, top + 0.0105), r)
        CY("Hub", 0.0095, 0.0024, c, "M_RmCream", reel, (0, 0, 0), segments=16)
        for k in range(3):
            tooth = B("Tooth", (0.0034, 0.008, 0.0028), c, "M_RmBlack", reel, (0, 0, 0.0003))
            tooth.rotation_euler = (0, 0, R(60 * k))
    # piano keys along the front edge: the red one records
    for k in range(5):
        x = -0.012 + k * 0.027
        if k == 0:
            btn = bk.empty("RecButton", c, (x, -0.054, top + 0.004), r)
            B("Key", (0.024, 0.036, 0.012), c, "M_PlasticRed", btn, (0, 0, 0), bevel=0.003).rotation_euler = (R(-6), 0, 0)
            SP("RecDot", 0.0035, c, "M_RmCream", btn, (0, -0.008, 0.0068), scale=(1, 1, 0.4), segments=8, rings=4)
            continue
        key = B("Key", (0.024, 0.036, 0.012), c, "M_RmBlack" if k == 4 else "M_RmKeys", r, (x, -0.054, top + 0.004), bevel=0.003)
        key.rotation_euler = (R(-6), 0, 0)
    # front face: brass badge + microphone
    B("Badge", (0.05, 0.002, 0.011), c, "M_RmBrass", r, (-0.075, -d / 2 - 0.0005, 0.03), bevel=0.0006)
    CY("Mic", 0.007, 0.003, c, "M_RmBlack", r, (0.085, -d / 2 - 0.001, 0.028), rotation=(R(90), 0, 0), segments=12)
    # carry handle folded flat along the back
    cable("Handle", [(-0.1, d / 2 - 0.004, h - 0.012), (-0.1, d / 2 + 0.014, h - 0.004), (0.1, d / 2 + 0.014, h - 0.004),
                     (0.1, d / 2 - 0.004, h - 0.012)], 0.0055, c, "M_RmBlack", r, segments=8)
    for sx in (-1, 1):
        CY("Hinge", 0.008, 0.012, c, "M_RmBrass", r, (sx * 0.1, d / 2 - 0.002, h - 0.012), rotation=(0, R(90), 0), segments=10)
    # tape rack on the right: three slots, one blank cassette per recorder use left
    rx = w / 2 + 0.048
    slab("RackBase", rrect_pts(0.076, 0.124, 0.01, 3), 0.02, c, "M_RmWalnut", r, (rx, 0, 0), bevel=0.003)
    for k in range(4):
        B("Fin", (0.003, 0.11, 0.014), c, "M_RmWalnutDark", r, (rx - 0.033 + k * 0.022, 0, 0.027), bevel=0.001)
    stripes = ("M_PlasticRed", "M_PlasticBlue", "M_RmBinderMustard")
    for k in range(3):
        cas = bk.empty("Cassette%d" % k, c, (rx - 0.022 + k * 0.022, 0, 0.02), r)
        B("Shell", (0.016, 0.104, 0.068), c, "M_RmPhoneDark", cas, (0, 0, 0.034), bevel=0.0018)
        for sx in (-1, 1):
            B("Label", (0.0012, 0.086, 0.03), c, "M_RmLabel", cas, (sx * 0.0084, -0.002, 0.044))
            B("TapeBand", (0.0014, 0.086, 0.007), c, stripes[k], cas, (sx * 0.0086, -0.002, 0.033))
        B("Spine", (0.0172, 0.07, 0.0012), c, "M_RmLabel", cas, (0, 0, 0.0685))
    return r


@room_prop
def rm_mug(c):
    """Ceramic mug with a colour band (Stripe, the player's colour) and Coffee."""
    r = root("RmMug", c)
    s = 1.25
    prof = [(0, 0), (0.042, 0), (0.044, 0.006), (0.047, 0.1), (0.043, 0.1), (0.04, 0.012), (0, 0.012)]
    bk.lathe("Cup", [(a * s, b * s) for a, b in prof], c, "M_Ceramic", r, segments=24)
    h = bk.torus("Handle", 0.028 * s, 0.0075 * s, c, "M_Ceramic", r, (0.05 * s, 0, 0.055 * s), arc=math.pi * 1.15, major_seg=14, minor_seg=7)
    h.rotation_euler = (R(90), R(103), 0)
    bk.lathe("Stripe", [(0.0463 * s, 0.05 * s), (0.0472 * s, 0.05 * s), (0.0476 * s, 0.072 * s), (0.0467 * s, 0.072 * s)], c, "M_PlasticBlue", r, segments=24, smooth=True)
    CY("Coffee", 0.04 * s, 0.004, c, "M_Coffee", r, (0, 0, 0.088 * s), segments=24)
    return r


@room_prop
def rm_spill(c):
    """Coffee spill around the keyboard (shown in the Coffee monitor state)."""
    r = root("RmSpill", c)
    sp = bk.empty("Spill", c, (0, 0, 0), r)
    bk.flat_blob("Puddle", 0.17, 0.09, 0.003, c, "M_Coffee", sp, (0.0, 0.0, 0.0), points=18, seed=5, wobble=0.3)
    bk.flat_blob("Drop", 0.03, 0.025, 0.003, c, "M_Coffee", sp, (0.2, -0.06, 0.0), points=10, seed=6)
    bk.flat_blob("Drop2", 0.02, 0.018, 0.003, c, "M_Coffee", sp, (-0.21, 0.05, 0.0), points=10, seed=7)
    return r


@room_prop
def rm_lamp(c):
    """Banker's lamp: brass base and stem, green glass shade; Glow = the lit underside (on/off)."""
    r = root("RmLamp", c)
    bk.lathe("Base", [(0, 0), (0.09, 0), (0.095, 0.008), (0.08, 0.025), (0.03, 0.035), (0, 0.036)], c, "M_RmBrass", r, segments=24, scale=(1.3, 0.85, 1))
    CY("Stem", 0.011, 0.3, c, "M_RmBrass", r, (0, 0, 0.18), segments=12)
    B("Yoke", (0.2, 0.014, 0.014), c, "M_RmBrass", r, (0, 0, 0.33), bevel=0.004)
    for sx in (-1, 1):
        B("YokeArm", (0.012, 0.012, 0.05), c, "M_RmBrass", r, (sx * 0.095, 0, 0.35), bevel=0.003)
    sh = half_tube_x("Shade", 0.075, 0.27, 0.006, c, "M_RmLampGreen", r, (0, -0.01, 0.335), seg=14)
    sh.rotation_euler = (R(-12), 0, 0)
    for sx in (-1, 1):
        CY("Cap", 0.074, 0.008, c, "M_RmBrass", r, (sx * 0.136, -0.01, 0.335), rotation=(0, R(90), 0), segments=16).rotation_euler = (R(-12), R(90), 0)
    g = B("Glow", (0.24, 0.12, 0.004), c, "M_RmLampGlow", r, (0, -0.012, 0.345))
    g.rotation_euler = (R(-12), 0, 0)
    tube("Chain", [(0.06, -0.06, 0.34), (0.062, -0.065, 0.28), (0.061, -0.063, 0.24)], 0.0025, c, "M_RmBrass", r, segments=5)
    SP("Bead", 0.008, c, "M_RmBrass", r, (0.061, -0.063, 0.235), segments=8, rings=5)
    return r


@room_prop
def rm_nameplate(c):
    """Walnut block with brass plates front and back (TMP names in Unity) and a colour Stripe."""
    r = root("RmNameplate", c)
    slab("Block", rrect_pts(0.28, 0.07, 0.01, 3), 0.035, c, "M_RmWalnutDark", r, (0, 0, 0), bevel=0.005)
    for sy in (-1, 1):
        p = B("Plate", (0.25, 0.004, 0.05), c, "M_RmBrass", r, (0, sy * 0.022, 0.06), bevel=0.002)
        p.rotation_euler = (R(18 * sy), 0, 0)
        # support behind the plate (must stay behind it: it used to poke through the middle of the name)
        B("PlateBack", (0.012, 0.016, 0.046), c, "M_RmWalnutDark", r, (0, sy * 0.009, 0.055)).rotation_euler = (R(18 * sy), 0, 0)
    B("Stripe", (0.26, 0.072, 0.006), c, "M_PlasticBlue", r, (0, 0, 0.036))
    return r


@room_prop
def rm_photo_frame(c):
    r = root("RmPhotoFrame", c)
    fr = bk.empty("FrameTilt", c, (0, 0, 0.0), r)
    fr.rotation_euler = (R(-14), 0, 0)
    vslab("Frame", 0.15, 0.19, 0.016, 0.006, c, "M_RmWalnut", fr, (0, 0.008, 0.0), bevel=0.003)
    uv_plane("Photo", 0.115, 0.155, c, "M_Paper", fr, (0, -0.0085, 0.095))
    B("Stand", (0.03, 0.006, 0.15), c, "M_RmWalnutDark", r, (0, 0.045, 0.07)).rotation_euler = (R(22), 0, 0)
    return r


@room_prop
def rm_in_tray(c):
    r = root("RmInTray", c)
    for z in (0.0, 0.07):
        slab("Tray", rrect_pts(0.26, 0.33, 0.01, 2), 0.006, c, "M_RmWalnut", r, (0, 0, z), bevel=0.002)
        for sx in (-1, 1):
            B("Side", (0.006, 0.33, 0.04), c, "M_RmWalnut", r, (sx * 0.127, 0, z + 0.02))
        B("Back", (0.26, 0.006, 0.04), c, "M_RmWalnut", r, (0, 0.162, z + 0.02))
    for k in range(2):
        B("Post", (0.012, 0.012, 0.07), c, "M_RmBrass", r, ((k * 2 - 1) * 0.12, -0.15, 0.035))
    B("Papers", (0.21, 0.29, 0.02), c, "M_Paper", r, (0.004, 0.01, 0.017)).rotation_euler = (0, 0, R(3))
    B("Memo", (0.21, 0.29, 0.012), c, "M_Manila", r, (-0.006, 0.0, 0.083)).rotation_euler = (0, 0, R(-4))
    B("MemoTop", (0.21, 0.29, 0.004), c, "M_Paper", r, (0.004, 0.006, 0.092)).rotation_euler = (0, 0, R(5))
    return r


@room_prop
def rm_pen_cup(c):
    r = root("RmPenCup", c)
    bk.lathe("Cup", [(0, 0), (0.035, 0), (0.036, 0.095), (0.032, 0.095), (0.031, 0.006), (0, 0.006)], c, "M_RmTerracotta", r, segments=16)
    for i, (m, tilt) in enumerate((("M_DuckYellow", 8), ("M_PlasticBlue", -6), ("M_RmBlack", 3), ("M_PlasticRed", -10))):
        p = CY("Pen", 0.0045, 0.15, c, m, r, (0.01 * (i - 1.5), 0.006 * ((i * 3) % 3 - 1), 0.11), segments=6)
        p.rotation_euler = (R(tilt), R(-tilt * 0.7), 0)
    return r


@room_prop
def rm_notepad(c):
    """Notepad + pen (meeting seats, desk)."""
    r = root("RmNotepad", c)
    B("Pad", (0.15, 0.21, 0.012), c, "M_Paper", r, (0, 0, 0.006), bevel=0.002)
    B("Binding", (0.15, 0.018, 0.016), c, "M_RmBlack", r, (0, 0.1, 0.008))
    for k in range(5):
        B("Line", (0.11, 0.003, 0.001), c, "M_RmBinderBlue", r, (-0.005, 0.06 - k * 0.025, 0.0125))
    pen = CY("Pen", 0.005, 0.14, c, "M_RmBlack", r, (0.1, -0.01, 0.006), rotation=(R(90), 0, R(12)), segments=8)
    return r


@room_prop
def rm_ceiling_lamp(c):
    """Flush round ceiling lamp; pivot at the ceiling (top), hangs down; Glow is the diffuser."""
    r = root("RmCeilingLamp", c)
    CY("Rose", 0.2, 0.03, c, "M_RmBrass", r, (0, 0, -0.015), segments=28)
    bk.lathe("Glow", [(0, -0.12), (0.12, -0.11), (0.2, -0.07), (0.23, -0.03), (0, -0.03)], c, "M_RmCeilingGlow", r, segments=28)
    return r


@room_prop
def rm_pendant_bar(c):
    """Linear pendant over the meeting table (pivot at the ceiling, hangs 0.6 m)."""
    r = root("RmPendantBar", c)
    for sy in (-1, 1):
        CY("Wire", 0.003, 0.6, c, "M_RmBlack", r, (0, sy * 0.7, -0.3), segments=5)
        CY("Canopy", 0.04, 0.02, c, "M_RmBrass", r, (0, sy * 0.7, -0.01), segments=12)
    slab("Bar", rrect_pts(0.12, 1.7, 0.04, 4), 0.06, c, "M_RmGraphite", r, (0, 0, -0.66), bevel=0.01)
    B("Glow", (0.09, 1.62, 0.01), c, "M_RmCeilingGlow", r, (0, 0, -0.665))
    return r


# ================================================================================================ meeting room

@room_prop
def rm_meeting_table(c):
    """Racetrack walnut table, 3.3 x 1.3, top at 0.75, leather inlay, two pedestals. Long along Y."""
    r = root("RmMeetingTable", c)
    L, W, top = 3.3, 1.3, 0.75
    slab("Top", stadium_pts(L, W, 16), 0.05, c, "M_RmWalnut", r, (0, 0, top - 0.05), bevel=0.012, bevel_seg=3)
    slab("Inlay", stadium_pts(L - 0.9, 0.42, 12), 0.003, c, "M_RmWainscotDark", r, (0, 0, top), bevel=0.0)
    slab("Under", stadium_pts(L - 0.3, W - 0.3, 12), 0.04, c, "M_RmWalnutDark", r, (0, 0, top - 0.09))
    for sy in (-1, 1):
        slab("Pedestal", rrect_pts(0.5, 0.36, 0.08, 4), top - 0.11, c, "M_RmWalnutDark", r, (0, sy * 0.85, 0.03), bevel=0.01)
        slab("Base", rrect_pts(0.8, 0.5, 0.1, 4), 0.03, c, "M_RmBrass", r, (0, sy * 0.85, 0.0), bevel=0.006)
    return r


@room_prop
def rm_meeting_chair(c):
    """Club-style meeting chair, seat top at 0.48; Fabric takes the sitter's colour. Backrest at -Y."""
    r = root("RmMeetingChair", c)
    fab = bk.empty("Fabric", c, (0, 0, 0), r)
    slab("Seat", rrect_pts(0.52, 0.5, 0.12, 5), 0.09, c, "M_RmLeather", fab, (0, 0.02, 0.39), bevel=0.035, bevel_seg=3)
    back = vslab("Back", 0.52, 0.48, 0.09, 0.16, c, "M_RmLeather", fab, (0, -0.2, 0.47), bevel=0.035)
    back.rotation_euler = (R(90 + 10), 0, 0)
    for sx in (-1, 1):
        slab("Arm", rrect_pts(0.07, 0.42, 0.03, 3), 0.05, c, "M_RmLeather", fab, (sx * 0.29, -0.02, 0.6), bevel=0.02)
        B("ArmPost", (0.03, 0.03, 0.14), c, "M_RmBrass", r, (sx * 0.29, 0.0, 0.53))
    CY("Column", 0.03, 0.3, c, "M_RmBrass", r, (0, 0.0, 0.24), segments=14)
    for i in range(4):
        a = 2 * math.pi * i / 4 + R(45)
        leg = B("Leg", (0.3, 0.04, 0.03), c, "M_RmBrass", r, (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.07), bevel=0.008)
        leg.rotation_euler = (0, R(-8), a)
        SP("Glide", 0.02, c, "M_RmBlack", r, (math.cos(a) * 0.29, math.sin(a) * 0.29, 0.02), segments=8, rings=5)
    return r


@room_prop
def rm_projector_screen(c):
    """Wall-mounted screen (pivot: back centre at the canvas bottom). Canvas 2.2 x 1.2375 (16:9) faces -Y."""
    r = root("RmProjectorScreen", c)
    w, h = 2.2, 2.2 * 9 / 16
    B("Housing", (w + 0.22, 0.12, 0.12), c, "M_RmCream", r, (0, -0.07, h + 0.16), bevel=0.03)
    for sx in (-1, 1):
        B("EndCap", (0.04, 0.13, 0.13), c, "M_RmGraphite", r, (sx * (w / 2 + 0.12), -0.07, h + 0.16), bevel=0.01)
    vslab("Backing", w + 0.12, h + 0.12, 0.012, 0.01, c, "M_RmBlack", r, (0, -0.018, -0.06))
    uv_plane("Canvas", w, h, c, "M_RmCanvas", r, (0, -0.0312, h / 2))
    B("BottomBar", (w + 0.1, 0.03, 0.03), c, "M_RmGraphite", r, (0, -0.04, -0.075), bevel=0.008)
    CY("Pull", 0.006, 0.16, c, "M_RmBlack", r, (0, -0.045, -0.17), segments=6)
    SP("PullKnob", 0.016, c, "M_RmBrass", r, (0, -0.045, -0.255), segments=10, rings=6)
    return r


@room_prop
def rm_projector(c):
    """Ceiling projector (pivot: the ceiling mount). Lens faces -Y."""
    r = root("RmProjector", c)
    CY("Plate", 0.07, 0.015, c, "M_RmGraphite", r, (0, 0, -0.0075), segments=14)
    CY("Pole", 0.016, 0.34, c, "M_RmGraphite", r, (0, 0, -0.17), segments=10)
    slab("Body", rrect_pts(0.32, 0.28, 0.04, 4), 0.11, c, "M_RmCream", r, (0, 0, -0.45), bevel=0.02)
    CY("Lens", 0.042, 0.05, c, "M_RmBlack", r, (0.07, -0.15, -0.395), rotation=(R(90), 0, 0), segments=18)
    CY("LensGlass", 0.032, 0.006, c, "M_LEDBlue", r, (0.07, -0.177, -0.395), rotation=(R(90), 0, 0), segments=16)
    for k in range(4):
        B("Vent", (0.006, 0.12, 0.004), c, "M_RmBlack", r, (-0.1 + k * 0.02, 0.02, -0.338))
    return r


@room_prop
def rm_whiteboard(c):
    """Whiteboard (pivot: back centre at the board centre). BoardSurface carries the doodles decal."""
    r = root("RmWhiteboard", c)
    w, h = 1.6, 0.8
    vslab("Frame", w + 0.05, h + 0.05, 0.025, 0.01, c, "M_Metal", r, (0, 0.0, -(h + 0.05) / 2), bevel=0.004)
    uv_plane("BoardSurface", w, h, c, "M_RmWhiteboard", r, (0, -0.0262, 0))
    B("Tray", (0.8, 0.07, 0.02), c, "M_Metal", r, (0, -0.06, -h / 2 - 0.035), bevel=0.004)
    for i, m in enumerate(("M_PlasticRed", "M_PlasticBlue", "M_RmBlack")):
        CY("Marker", 0.009, 0.12, c, m, r, (-0.25 + i * 0.09, -0.065, -h / 2 - 0.015), rotation=(0, R(90), R(4 * i)), segments=8)
    B("Eraser", (0.12, 0.05, 0.035), c, "M_RmGraphite", r, (0.2, -0.065, -h / 2 - 0.008), bevel=0.008)
    return r


@room_prop
def rm_sideboard(c):
    """Low walnut credenza (meeting room foot wall)."""
    r = root("RmSideboard", c)
    w, d, h = 1.8, 0.46, 0.76
    slab("Top", rrect_pts(w, d, 0.02, 3), 0.03, c, "M_RmWalnut", r, (0, 0, h - 0.03), bevel=0.006)
    B("Body", (w - 0.04, d - 0.03, h - 0.15), c, "M_RmWalnutDark", r, (0, 0.01, 0.12 + (h - 0.15) / 2), bevel=0.006)
    for i in range(3):
        x = -0.58 + i * 0.58
        B("Door", (0.55, 0.018, h - 0.19), c, "M_RmWalnut", r, (x, -d / 2 + 0.01, 0.12 + (h - 0.15) / 2), bevel=0.004)
        B("Pull", (0.016, 0.02, 0.14), c, "M_RmBrass", r, (x + 0.22, -d / 2 - 0.004, 0.45), bevel=0.003)
    for sx in (-1, 1):
        for sy in (-1, 1):
            CY("Leg", 0.018, 0.12, c, "M_RmBrass", r, (sx * (w / 2 - 0.1), sy * (d / 2 - 0.08), 0.06), segments=10, radius_top=0.012)
    return r


@room_prop
def rm_jug(c):
    """Glass water jug with water."""
    r = root("RmJug", c)
    bk.lathe("Jug", [(0, 0), (0.06, 0), (0.065, 0.02), (0.07, 0.12), (0.05, 0.2), (0.048, 0.24), (0.054, 0.25),
                     (0.044, 0.25), (0.042, 0.2), (0.062, 0.12), (0.057, 0.02), (0.0, 0.01)], c, "M_RmGlassCup", r, segments=22)
    bk.lathe("Water", [(0, 0.012), (0.058, 0.014), (0.063, 0.12), (0.052, 0.17), (0.0, 0.17)], c, "M_RmWater", r, segments=20)
    tube("Handle", [(0.055, 0, 0.2), (0.11, 0, 0.18), (0.11, 0, 0.08), (0.065, 0, 0.06)], 0.009, c, "M_RmGlassCup", r, segments=6)
    return r


@room_prop
def rm_glass(c):
    r = root("RmGlass", c)
    bk.lathe("Glass", [(0, 0), (0.03, 0), (0.036, 0.1), (0.033, 0.1), (0.027, 0.006), (0, 0.006)], c, "M_RmGlassCup", r, segments=18)
    bk.lathe("Water", [(0, 0.007), (0.027, 0.008), (0.032, 0.065), (0.0, 0.065)], c, "M_RmWater", r, segments=16)
    return r


@room_prop
def rm_tray_cups(c):
    """Tray with four glasses (sideboard)."""
    r = root("RmTrayCups", c)
    slab("Tray", rrect_pts(0.42, 0.28, 0.04, 4), 0.012, c, "M_RmBrass", r, (0, 0, 0), bevel=0.004)
    for k in range(4):
        x, y = -0.13 + (k % 2) * 0.12, -0.05 + (k // 2) * 0.11
        bk.lathe("Glass", [(0, 0.012), (0.03, 0.012), (0.036, 0.11), (0.033, 0.11), (0.027, 0.018), (0, 0.018)], c, "M_RmGlassCup", r, (x, y, 0), segments=16)
    bk.lathe("Thermos", [(0, 0.012), (0.055, 0.012), (0.058, 0.22), (0.035, 0.26), (0.03, 0.29), (0.0, 0.29)], c, "M_RmGraphite", r, (0.12, 0.02, 0), segments=18)
    return r


@room_prop
def rm_speakerphone(c):
    r = root("RmSpeakerphone", c)
    CY("Body", 0.13, 0.035, c, "M_RmGraphite", r, (0, 0, 0.0175), segments=3, cap_bevel=0.012).rotation_euler = (0, 0, R(90))
    CY("Grill", 0.05, 0.004, c, "M_RmBlack", r, (0, 0, 0.036), segments=18)
    for k in range(3):
        a = R(90 + 120 * k)
        SP("Led", 0.006, c, "M_LEDGreen", r, (math.cos(a) * 0.075, math.sin(a) * 0.075, 0.035), segments=6, rings=4)
    return r


@room_prop
def rm_box(c):
    """Cardboard box of belongings next to the fired player's chair."""
    r = root("RmBox", c)
    w, d, h, t = 0.4, 0.3, 0.24, 0.012
    B("Bottom", (w, d, t), c, "M_Cardboard", r, (0, 0, t / 2))
    for sy in (-1, 1):
        B("WallY", (w, t, h), c, "M_Cardboard", r, (0, sy * (d - t) / 2, h / 2))
        fl = B("Flap", (w, t, 0.13), c, "M_Cardboard", r, (0, sy * (d / 2 + 0.045), h + 0.04))
        fl.rotation_euler = (R(-50 * sy), 0, 0)
    for sx in (-1, 1):
        B("WallX", (t, d, h), c, "M_Cardboard", r, (sx * (w - t) / 2, 0, h / 2))
    bk.lathe("Pot", [(0, 0), (0.05, 0), (0.06, 0.09), (0.0, 0.09)], c, "M_RmTerracotta", r, (-0.1, 0.03, h - 0.04), segments=12)
    for k in range(5):
        leaf("Leaf", 0.12, 0.05, c, "M_RmLeafBright", r, (-0.1, 0.03, h + 0.05), rotation=(R(30), 0, k * 1.25), bend=0.3, seg=4)
    bk.lathe("Mug", [(0, 0), (0.042, 0), (0.045, 0.1), (0.04, 0.1), (0.0, 0.01)], c, "M_Ceramic", r, (0.1, -0.04, h - 0.05), segments=14)
    B("Frame", (0.12, 0.02, 0.15), c, "M_RmWalnut", r, (0.05, 0.08, h + 0.02)).rotation_euler = (R(-20), 0, R(10))
    return r


# ================================================================================================ first-person hands

def _outline_hull(obj, coll, push=0.006):
    """Inverted hull (navy outline like the characters)."""
    me = obj.data.copy()
    hull = bpy.data.objects.new(obj.name + "Outline", me)
    coll.objects.link(hull)
    hull.parent = obj.parent
    hull.matrix_basis = obj.matrix_basis.copy()
    bm = bmesh.new()
    bm.from_mesh(me)
    bm.normal_update()
    for v in bm.verts:
        v.co += v.normal * push
    for f in bm.faces:
        f.normal_flip()
    bm.to_mesh(me)
    bm.free()
    me.materials.clear()
    me.materials.append(bk.mat("M_Outline"))
    return hull


def _hand(c, side):
    sx = -1 if side == "L" else 1
    r = root("RmHand" + side, c)
    m = bk.empty("Mitten", c, (0, 0, 0), r)
    palm = SP("Palm", 0.052, c, "M_BeanBlue", m, (0, 0, 0), scale=(1.0, 1.25, 0.62), segments=18, rings=12)
    thumb = SP("Thumb", 0.022, c, "M_BeanBlue", m, (-sx * 0.045, -0.02, 0.006), scale=(1.0, 1.5, 0.9), segments=12, rings=8)
    thumb.rotation_euler = (0, 0, R(-sx * 30))
    arm = CY("Arm", 0.034, 0.32, c, "M_BeanBlue", m, (0, 0.17, -0.02), rotation=(R(90 - 6), 0, 0), segments=16, radius_top=0.04)
    for o in (palm, thumb, arm):
        _outline_hull(o, c, 0.005)
    return r


@room_prop
def rm_hand_l(c):
    return _hand(c, "L")


@room_prop
def rm_hand_r(c):
    return _hand(c, "R")
