"""Whole-office preview without Unity: builds the shell + every prop placement from Art/Layout/office_v2.json and
renders EEVEE shots to Art/Previews/map/.

Run:  blender -b --factory-startup -P Art/Scripts/map_preview.py [-- --only top,office] [--fast] [--power-cut]
Shots: top (orthographic plan), overview, office, manager_meeting, kitchen_copy, reception, lounge, south_wing.
Walls between a shot's camera and its target are hidden (like the in-game camera fade).
"""
import json
import math
import os
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
from mathutils import Vector  # noqa: E402

import bk_common as bk  # noqa: E402
import map_kit  # noqa: E402
import map_layout  # noqa: E402
import build_props  # noqa: E402
import build_architecture  # noqa: E402

OUT_DIR = os.path.join(bk.PREVIEW_DIR, "map")
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
PLAYER_COLORS = ["#4A90E2", "#F5C842", "#F28AB2", "#6CC56A", "#FF8A3D", "#9D6BDB"]
PRESENT = [0, 1, 2, 3]  # preview a 4-player match: desks 4 and 5 are unassigned


def arg_value(name, default=None):
    if name in ARGS:
        i = ARGS.index(name)
        if i + 1 < len(ARGS):
            return ARGS[i + 1]
    return default


# ================================================================================================ library

class Library:
    def __init__(self):
        self.lib = bk.collection("Library")
        roots = build_props.build_all()
        self.coll = {}
        for r in roots:
            c = r.users_collection[0]
            self.coll[r.name] = c
        bpy.context.view_layer.layer_collection.children["Props"].exclude = True
        self.variants = {}

    def get(self, model, swap=None):
        if not swap:
            return self.coll.get(model)
        key = model + str(sorted(swap.items()))
        if key in self.variants:
            return self.variants[key]
        src = self.coll[model]
        dst = bpy.data.collections.new(model + "_var%d" % len(self.variants))
        bpy.data.collections["Props"].children.link(dst)
        mapping = {}
        for o in src.objects:
            n = o.copy()
            if o.type == "MESH" and any(m and m.name in swap for m in o.data.materials):
                n.data = o.data.copy()
                for i, m in enumerate(n.data.materials):
                    if m and m.name in swap:
                        n.data.materials[i] = material(swap[m.name])
            dst.objects.link(n)
            mapping[o] = n
        for o, n in mapping.items():
            if o.parent in mapping:
                n.parent = mapping[o.parent]
        self.variants[key] = dst
        return dst


def material(name):
    if name.startswith("#"):
        m = bpy.data.materials.get("Col" + name)
        if m is None:
            m = bpy.data.materials.new("Col" + name)
            m.use_nodes = True
            r, g, b = bk.hex_to_linear(name)
            bsdf = m.node_tree.nodes.get("Principled BSDF")
            bsdf.inputs["Base Color"].default_value = (r, g, b, 1)
            bsdf.inputs["Roughness"].default_value = 0.6
        return m
    return bk.mat(name)


COLLIDERS = []  # (instance object, model) placed with a collider in Unity


def place(lib, model, p, yaw, name=None, swap=None, scale=None, parent_coll=None, collider=False):
    c = lib.get(model, swap)
    if c is None:
        print("PREVIEW missing model", model)
        return None
    o = bpy.data.objects.new(name or ("I_" + model), None)
    o.instance_type = "COLLECTION"
    o.instance_collection = c
    o.location = map_kit.u2b(p)
    o.rotation_euler = map_kit.yaw2rot(yaw)
    if scale:
        o.scale = (scale[0], scale[2], scale[1])
    (parent_coll or bpy.context.scene.collection).objects.link(o)
    if collider:
        COLLIDERS.append((o, model))
    return o


def rot_y(v, yaw):
    a = math.radians(yaw)
    return (v[0] * math.cos(a) + v[2] * math.sin(a), v[1], -v[0] * math.sin(a) + v[2] * math.cos(a))


def add(a, b):
    return (a[0] + b[0], a[1] + b[1], a[2] + b[2])


# ================================================================================================ scene

def slot_local(kind, i):
    """Mirror of OfficeWorld.SlotPose (Unity local offsets per slot kind)."""
    if kind == "MugShelf":
        return (-0.18 + 0.12 * i, 0.02, 0.0), 1.0
    if kind == "LostAndFound":
        return (-0.18 + 0.18 * (i % 3), 0.02, -0.08 if i < 3 else 0.08), 0.85
    if kind == "JunkBin":
        return (-0.22 + 0.15 * (i % 4), 0.08 + 0.04 * (i // 4), -0.08 if i % 2 == 0 else 0.08), 1.0
    if kind == "SprayRack":
        return (-0.15 + 0.3 * i, 0.0, 0.0), 1.0
    if kind == "Mailroom":
        return (-0.45 + 0.3 * i, 0.0, 0.1), 1.0
    if kind == "HidingSpot":
        return (-0.13 + 0.26 * i, 0.0, 0.0), 0.8
    return (0, 0, 0), 1.0


def build_scene(data, power_cut=False):
    lib = Library()
    world_c = bk.collection("World")
    shell_root = bk.empty("OfficeShell", world_c)
    build_architecture.build_shell(data, world_c, shell_root)
    o = data["office"]

    # props
    for e in data["props"]:
        swap = e.get("tint")
        place(lib, e["m"], e["p"], e["r"], swap=swap, scale=e.get("scale"), collider=e.get("c", True))

    # desks
    kit = data["desk"]
    for i, dp in enumerate(o["desks"]):
        assigned = i in PRESENT
        place(lib, kit["model"], dp, 0, collider=True)
        for part in kit["parts"]:
            role = part.get("role")
            p = part["p"]
            if role in ("dirt", "spill", "cable"):
                continue
            if role == "empty" and assigned:
                continue
            if role == "chair":
                p = kit["chairPushedIn"] if not assigned else p
                swap = {"M_ChairFabric": PLAYER_COLORS[i]} if assigned else {"M_ChairFabric": "M_PaintGreyDark"}
                place(lib, part["m"], add(dp, rot_y(p, 0)), part["r"], swap=swap)
                continue
            if role == "pc" and not assigned:
                place(lib, part["m"], add(dp, p), part["r"], swap={"M_ScreenDesktop": "M_ScreenOff"})
                continue
            if role == "pc" and i == 1:
                place(lib, part["m"], add(dp, p), part["r"], swap={"M_ScreenDesktop": "M_ScreenLockScreen"})
                continue
            place(lib, part["m"], add(dp, p), part["r"], collider=(role == "partition"))
        if assigned:
            for d in kit["personal"][i]:
                place(lib, d["m"], add(dp, d["p"]), d["r"])
        nh = kit["nameplateHolder"]
        place(lib, nh["m"], add(dp, nh["p"]), nh["r"])
        if assigned:
            npo = o["offsets"]["Nameplate"]
            place(lib, "Nameplate", add(dp, npo), 0, swap={"M_NameplateStripe": PLAYER_COLORS[i]})

    # items in their initial slots + a few match-time items
    sl = o["slots"]

    def slot_items(slot_id, models):
        s = sl[slot_id]
        for i, m in enumerate(models):
            loc, sc = slot_local(s["kind"], i)
            place(lib, m, add(s["p"], rot_y(loc, s["yaw"])), s["yaw"] + (0 if s["kind"] in ("MugShelf", "SprayRack") else (i * 37) % 30 - 15), scale=(sc, sc, sc))

    slot_items("slot.mugshelf", ["Mug"] * 6)
    slot_items("slot.sprayrack", ["Spray"] * 3)
    slot_items("slot.lostfound", ["RubberDuck", "Stapler", "RivalCv", "BossMug", "SalaryList"])
    slot_items("slot.junk", ["ClutterPapers", "ClutterBox", "ClutterPapers", "ClutterBox"])
    slot_items("slot.mailroom", ["MailEnvelope", "MailEnvelope"])
    t = sl["slot.printer_tray"]
    place(lib, "Report", t["p"], t["yaw"])

    # characters (placeholders): employees at a few spots + the manager at home
    chars = [((o["desks"][0][0], 0, o["desks"][0][2] - 0.9), 0, 0), ((5.2, 0, 8.4), 0, 1), ((o["desks"][4][0] + 0.55, 0, o["desks"][4][2] - 1.0), 0, 2),
             ((10.0, 0, -0.5), 90, 3), ((-1.0, 0, -8.7), 180, 4)]
    for p, yaw, ci in chars:
        bean(p, PLAYER_COLORS[ci], 1.0)
    bean(o["managerHome"], "#B07444", 1.2, tie=True)

    # lights
    for L in data["lights"]:
        group = L.get("group", "room")
        if group == "emergency" and not power_cut:
            continue
        if group == "room" and power_cut:
            continue
        ld = bpy.data.lights.new("L", "POINT")
        r, g, b = bk.hex_to_linear(L["color"])
        ld.color = (r, g, b)
        ld.energy = L["i"] * (130.0 if group == "room" else 60.0)
        ld.shadow_soft_size = 0.25
        ld.use_shadow = False
        lo = bpy.data.objects.new("Light", ld)
        lo.location = map_kit.u2b(L["p"])
        bpy.context.scene.collection.objects.link(lo)
    if power_cut:
        for i, dp in enumerate(o["desks"]):
            if i in PRESENT:
                ld = bpy.data.lights.new("Glow", "POINT")
                ld.color = (0.4, 0.75, 1.0)
                ld.energy = 25
                ld.use_shadow = False
                lo = bpy.data.objects.new("Glow", ld)
                lo.location = map_kit.u2b(add(dp, (-0.25, 1.15, -0.2)))
                bpy.context.scene.collection.objects.link(lo)
    # skyline backdrop (same placement as the Unity builder)
    sky_c = bk.collection("SkylineLib")
    sky = build_architecture.skyline(sky_c)
    bpy.context.view_layer.layer_collection.children["SkylineLib"].exclude = True
    for p, yaw in (((0, 0, -27), 180), ((-36, 0, 0), 270), ((0, 0, 27), 0), ((36, 0, 0), 90)):
        o2 = bpy.data.objects.new("Sky", None)
        o2.instance_type = "COLLECTION"
        o2.instance_collection = sky_c
        o2.location = map_kit.u2b(p)
        o2.rotation_euler = map_kit.yaw2rot(yaw)
        bpy.context.scene.collection.objects.link(o2)
    # transparent materials: blended (Unity uses alpha blending; dithered looks noisy at low samples)
    for m in bpy.data.materials:
        spec = bk.palette()["materials"].get(m.name)
        if spec and spec.get("alpha", 1.0) < 1.0 and hasattr(m, "surface_render_method"):
            m.surface_render_method = "BLENDED"
    sun = bpy.data.lights.new("Sun", "SUN")
    sun.energy = 3.2 if not power_cut else 0.5
    sun.color = (1.0, 0.95, 0.87)
    sun.angle = math.radians(3)
    so = bpy.data.objects.new("Sun", sun)
    d_u = Vector((math.sin(math.radians(-32)) * math.cos(math.radians(52)), -math.sin(math.radians(52)), math.cos(math.radians(-32)) * math.cos(math.radians(52))))
    d_b = Vector((d_u.x, d_u.z, d_u.y))
    so.rotation_euler = d_b.to_track_quat("-Z", "Y").to_euler()
    bpy.context.scene.collection.objects.link(so)
    return lib


def bean(p, color, s, tie=False):
    m = material(color)
    b = bk.sphere("Bean", 0.33 * s, bpy.context.scene.collection.children["World"], m.name if False else None, None,
                  map_kit.u2b((p[0], 0.62 * s, p[2])), scale=(1, 1, 1.75), segments=20, rings=12)
    b.data.materials.append(m)
    for sx in (-1, 1):
        e = bk.sphere("Eye", 0.06 * s, bpy.context.scene.collection.children["World"], None, b, (0.11 * sx, -0.3 * s, 0.2 * s), segments=10, rings=6)
        e.data.materials.append(bk.mat("M_Eye"))


# ================================================================================================ render

def setup_render(res, fast):
    sc = bpy.context.scene
    sc.render.engine = "BLENDER_EEVEE"
    sc.render.resolution_x, sc.render.resolution_y = res
    sc.render.resolution_percentage = 100
    ee = sc.eevee
    for attr, val in (("taa_render_samples", 16 if fast else 48), ("use_shadows", True), ("use_raytracing", False),
                      ("shadow_ray_count", 1), ("shadow_step_count", 4), ("fast_gi_resolution", "2")):
        if hasattr(ee, attr):
            try:
                setattr(ee, attr, val)
            except Exception:
                pass
    if hasattr(ee, "use_fast_gi"):
        ee.use_fast_gi = True
    vs = sc.view_settings
    for vt in ("Khronos PBR Neutral", "AgX", "Standard"):
        try:
            vs.view_transform = vt
            break
        except TypeError:
            continue
    vs.exposure = 0.15
    world = bpy.data.worlds.new("W")
    world.use_nodes = True
    bg = world.node_tree.nodes.get("Background")
    bg.inputs["Color"].default_value = (0.62, 0.72, 0.85, 1)
    bg.inputs["Strength"].default_value = 0.55
    sc.world = world


def wall_objects():
    out = []
    for o in bpy.data.objects:
        if o.type == "MESH" and o.name.split("_")[0] in ("Wall", "Header", "Stall", "Glass", "GlassDoor", "DoorFrame", "Low"):
            out.append(o)
    return out


def seg_hits_box(a, b, lo, hi):
    tmin, tmax = 0.0, 1.0
    for k in range(2):
        d = b[k] - a[k]
        if abs(d) < 1e-9:
            if a[k] < lo[k] or a[k] > hi[k]:
                return False
        else:
            t1, t2 = (lo[k] - a[k]) / d, (hi[k] - a[k]) / d
            t1, t2 = min(t1, t2), max(t1, t2)
            tmin, tmax = max(tmin, t1), min(tmax, t2)
            if tmin > tmax:
                return False
    return True


def shot(name, target_u, dist, pitch, azim, lens=28, res=(1600, 1000), ortho=None, fast=False, hide_walls=True):
    setup_render(res, fast)
    walls = wall_objects()
    tb = Vector(map_kit.u2b(target_u))
    cam_d = bpy.data.cameras.new("Cam")
    cam_d.lens = lens
    cam_d.clip_end = 400
    if ortho:
        cam_d.type = "ORTHO"
        cam_d.ortho_scale = ortho
    cam = bpy.data.objects.new("Cam", cam_d)
    bpy.context.scene.collection.objects.link(cam)
    el, az = math.radians(pitch), math.radians(azim)
    # azim: direction the camera looks (Unity yaw); camera sits behind the target
    fwd = Vector((math.sin(az) * math.cos(el), math.cos(az) * math.cos(el), -math.sin(el)))
    cam.location = tb - fwd * dist
    cam.rotation_euler = fwd.to_track_quat("-Z", "Y").to_euler()
    if ortho and pitch >= 89.9:
        cam.rotation_euler = (0, 0, 0)
        cam.location = (tb.x, tb.y, 60)
    bpy.context.scene.camera = cam
    hidden = []
    if hide_walls and not ortho:
        a2 = (cam.location.x, cam.location.y)
        b2 = (tb.x, tb.y)
        for o in walls:
            ws = [o.matrix_world @ Vector(c) for c in o.bound_box]
            lo = (min(v.x for v in ws) - 0.05, min(v.y for v in ws) - 0.05)
            hi = (max(v.x for v in ws) + 0.05, max(v.y for v in ws) + 0.05)
            if seg_hits_box(a2, b2, lo, hi):
                o.hide_render = True
                hidden.append(o)
    os.makedirs(OUT_DIR, exist_ok=True)
    path = os.path.join(OUT_DIR, name + ".png")
    bpy.context.scene.render.filepath = path
    bpy.ops.render.render(write_still=True)
    for o in hidden:
        o.hide_render = False
    bpy.data.objects.remove(cam)
    print("SHOT", path)


SHOTS = {
    "top": dict(target_u=(0, 0, 0), dist=60, pitch=90, azim=0, res=(2400, 1560), ortho=33.5),
    "overview": dict(target_u=(0, 0, 0.5), dist=38, pitch=52, azim=-20, lens=30, res=(2000, 1250)),
    "office": dict(target_u=(-7.0, 0.6, 0.2), dist=11.5, pitch=40, azim=20, lens=26),
    "office_close": dict(target_u=(-7.0, 1.0, -0.4), dist=5.5, pitch=34, azim=-15, lens=30),
    "manager_meeting": dict(target_u=(-7.5, 0.6, 7.0), dist=11, pitch=44, azim=10, lens=26),
    "kitchen_copy": dict(target_u=(8.0, 0.6, 7.4), dist=10, pitch=44, azim=-5, lens=26),
    "reception": dict(target_u=(11.5, 0.6, -0.5), dist=9, pitch=42, azim=65, lens=26),
    "lounge": dict(target_u=(9.5, 0.6, -7.6), dist=9.5, pitch=46, azim=170, lens=26),
    "south_wing": dict(target_u=(-7.0, 0.6, -7.6), dist=11, pitch=50, azim=175, lens=24),
}


# ================================================================================================ nav check

FOOTPRINT = {"BigPlant": 0.45, "FloorLamp": 0.5, "CoatRack": 0.45, "StandingTable": 0.6, "KitchenTable": 0.75, "Globe": 0.7,
             "UmbrellaStand": 0.9, "Planter": 0.9, "Ladder": 0.8}


def nav_check(data, lib, agent=0.35, cell=0.05):
    """Approximate the Unity NavMesh: rasterise colliders (props: local bounds box like AddBoundsCollider, walls: mesh
    bounds) inflated by the agent radius, flood-fill from the open office and test every gameplay point. Also checks
    that the manager's straight-line route legs do not cross furniture or walls."""
    import collections
    x0, z0, x1, z1 = -16.6, -10.6, 16.6, 10.6
    nx, nz = int((x1 - x0) / cell), int((z1 - z0) / cell)
    blocked = bytearray(nx * nz)
    raw = []

    def rasterise(poly, inflate):
        cx = sum(p[0] for p in poly) / 4
        cz = sum(p[1] for p in poly) / 4
        ax = ((poly[1][0] - poly[0][0]), (poly[1][1] - poly[0][1]))
        bz = ((poly[3][0] - poly[0][0]), (poly[3][1] - poly[0][1]))
        la, lb = math.hypot(*ax), math.hypot(*bz)
        if la < 1e-6 or lb < 1e-6:
            return
        ua, ub = (ax[0] / la, ax[1] / la), (bz[0] / lb, bz[1] / lb)
        ha, hb = la / 2 + inflate, lb / 2 + inflate
        r = math.hypot(ha, hb)
        i0, i1 = max(0, int((cx - r - x0) / cell)), min(nx - 1, int((cx + r - x0) / cell) + 1)
        j0, j1 = max(0, int((cz - r - z0) / cell)), min(nz - 1, int((cz + r - z0) / cell) + 1)
        for j in range(j0, j1 + 1):
            pz = z0 + (j + 0.5) * cell - cz
            for i in range(i0, i1 + 1):
                px = x0 + (i + 0.5) * cell - cx
                if abs(px * ua[0] + pz * ua[1]) <= ha and abs(px * ub[0] + pz * ub[1]) <= hb:
                    blocked[j * nx + i] = 1

    def world_matrix(ob):
        m = ob.matrix_basis.copy()
        while ob.parent is not None:
            m = ob.parent.matrix_basis @ ob.matrix_parent_inverse @ m
            ob = ob.parent
        return m

    def local_box(coll):
        lo = [1e9] * 3
        hi = [-1e9] * 3
        for ob in coll.all_objects:
            if ob.type != "MESH":
                continue
            for c in ob.bound_box:
                w = world_matrix(ob) @ Vector(c)
                for k in range(3):
                    lo[k] = min(lo[k], w[k])
                    hi[k] = max(hi[k], w[k])
        return lo, hi

    cache = {}
    for inst, model in COLLIDERS:
        coll = inst.instance_collection
        if coll.name not in cache:
            cache[coll.name] = local_box(coll)
        lo, hi = cache[coll.name]
        if lo[0] > hi[0]:
            continue
        f = FOOTPRINT.get(model, 0.98)
        cx, cy = ((lo[0] + hi[0]) / 2, (lo[1] + hi[1]) / 2) if f >= 0.9 else (0.0, 0.0)
        hx, hy = (hi[0] - lo[0]) / 2 * f, (hi[1] - lo[1]) / 2 * f
        m = world_matrix(inst)
        poly = []
        for sx, sy in ((-1, -1), (1, -1), (1, 1), (-1, 1)):
            w = m @ Vector((cx + sx * hx, cy + sy * hy, 0))
            poly.append((w.x, w.y))
        rasterise(poly, agent)
        raw.append((model, poly))
    for ob in bpy.data.objects:
        if ob.type != "MESH" or ob.name.split("_")[0] not in ("Wall", "Stall", "Glass", "Low"):
            continue
        ws = [world_matrix(ob) @ Vector(c) for c in ob.bound_box]
        lx, hx_ = min(v.x for v in ws), max(v.x for v in ws)
        ly, hy_ = min(v.y for v in ws), max(v.y for v in ws)
        poly = [(lx, ly), (hx_, ly), (hx_, hy_), (lx, hy_)]
        rasterise(poly, agent)
        raw.append((ob.name, poly))

    def cell_of(x, z):
        return int((x - x0) / cell), int((z - z0) / cell)

    si, sj = cell_of(-4.5, -0.6)
    seen = bytearray(nx * nz)
    q = collections.deque([(si, sj)])
    seen[sj * nx + si] = 1
    while q:
        i, j = q.popleft()
        for di, dj in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            a, b = i + di, j + dj
            if 0 <= a < nx and 0 <= b < nz:
                k = b * nx + a
                if not seen[k] and not blocked[k]:
                    seen[k] = 1
                    q.append((a, b))

    def nearest_reachable(x, z, radius):
        ci, cj = cell_of(x, z)
        rr = int(radius / cell)
        best = None
        for j in range(max(0, cj - rr), min(nz, cj + rr + 1)):
            for i in range(max(0, ci - rr), min(nx, ci + rr + 1)):
                if seen[j * nx + i]:
                    d = math.hypot((i - ci) * cell, (j - cj) * cell)
                    if d <= radius and (best is None or d < best):
                        best = d
        return best

    o = data["office"]
    problems = []
    checks = []
    for i, d in enumerate(o["desks"]):
        checks.append(("desk.%d.seat" % i, d[0] + o["offsets"]["Seat"][0], d[2] + o["offsets"]["Seat"][2], 1.0, 1.8))
        checks.append(("desk.%d.nameplate(bot side)" % i, d[0] - 0.6, d[2] + 0.95, 1.2, 1.8))
    for sid, st in o["stations"].items():
        checks.append((sid, st["p"][0], st["p"][2], 1.2, 1.8))
    for sid, sl in o["slots"].items():
        checks.append((sid, sl["p"][0], sl["p"][2], 1.6, 1.8))
    for i, s_ in enumerate(o["seats"]):
        checks.append(("meeting.seat%d" % i, s_["p"][0], s_["p"][2], 1.0, 1.8))
    for name, x, z, radius, reach in checks:
        dd = nearest_reachable(x, z, reach)
        if dd is None:
            problems.append("UNREACHABLE %s (%.2f, %.2f): no walkable cell within %.1f m" % (name, x, z, reach))
        elif dd > radius:
            problems.append("FAR %s: nearest walkable %.2f m" % (name, dd))
    pts = [o["managerHome"]] + [r["p"] for r in o["route"]] + [o["managerHome"]]

    def inside(poly, x, z):
        c = 0
        for k in range(4):
            (ax_, az_), (bx_, bz_) = poly[k], poly[(k + 1) % 4]
            if (az_ > z) != (bz_ > z) and x < (bx_ - ax_) * (z - az_) / (bz_ - az_ + 1e-12) + ax_:
                c ^= 1
        return c == 1

    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        n = max(2, int(math.hypot(b[0] - a[0], b[2] - a[2]) / 0.1))
        hit = set()
        for t in range(n + 1):
            x = a[0] + (b[0] - a[0]) * t / n
            z = a[2] + (b[2] - a[2]) * t / n
            for name, poly in raw:
                if inside(poly, x, z):
                    hit.add(name)
        if hit:
            problems.append("MANAGER ROUTE leg %d (%.1f,%.1f)->(%.1f,%.1f) crosses %s" % (k, a[0], a[2], b[0], b[2], sorted(hit)))
    img = bpy.data.images.new("nav", nx, nz)
    px = [0.0] * (nx * nz * 4)
    for k in range(nx * nz):
        if blocked[k]:
            c = (0.25, 0.25, 0.28)
        elif seen[k]:
            c = (0.55, 0.85, 0.6)
        else:
            c = (0.9, 0.5, 0.45)
        px[k * 4:k * 4 + 4] = (c[0], c[1], c[2], 1.0)
    for name, x, z, radius, reach in checks:
        ci, cj = cell_of(x, z)
        for dj in range(-2, 3):
            for di in range(-2, 3):
                a, b = ci + di, cj + dj
                if 0 <= a < nx and 0 <= b < nz:
                    px[(b * nx + a) * 4:(b * nx + a) * 4 + 4] = (0.1, 0.3, 0.95, 1.0)
    for k in range(len(pts) - 1):
        a, b = pts[k], pts[k + 1]
        n = max(2, int(math.hypot(b[0] - a[0], b[2] - a[2]) / cell))
        for t in range(n + 1):
            ci, cj = cell_of(a[0] + (b[0] - a[0]) * t / n, a[2] + (b[2] - a[2]) * t / n)
            if 0 <= ci < nx and 0 <= cj < nz:
                px[(cj * nx + ci) * 4:(cj * nx + ci) * 4 + 4] = (0.9, 0.75, 0.1, 1.0)
    img.pixels = px
    os.makedirs(OUT_DIR, exist_ok=True)
    img.filepath_raw = os.path.join(OUT_DIR, "navcheck.png")
    img.file_format = "PNG"
    img.save()
    for p_ in problems:
        print("NAVCHECK", p_)
    print("NAVCHECK_DONE", len(checks), "points,", len(problems), "problems")
    return problems


def main():
    data = map_layout.main()
    fast = "--fast" in ARGS
    power = "--power-cut" in ARGS
    bk.reset_scene()
    lib = build_scene(data, power_cut=power)
    if "--navcheck" in ARGS:
        nav_check(data, lib)
        if arg_value("--only") is None:
            return
    only = arg_value("--only")
    names = only.split(",") if only else list(SHOTS.keys())
    for n in names:
        kw = dict(SHOTS[n])
        shot(n + ("_powercut" if power else ""), fast=fast, **kw)
    print("PREVIEW_DONE", len(names))


if __name__ == "__main__":
    main()
