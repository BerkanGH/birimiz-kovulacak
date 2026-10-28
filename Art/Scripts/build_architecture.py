"""Office architecture: the v2 shell (floors, painted walls, windows, doors, glass walls, low walls, stalls, rugs)
generated from Art/Layout/office_v2.json, plus the skyline backdrop.

Run:  blender -b --factory-startup -P Art/Scripts/build_architecture.py [-- --previews]
      (regenerates the layout JSON from DefaultOffice first, via map_layout.py)
Outputs: Art/Blender/architecture.blend, Game/Assets/Game/Art/Models/Architecture/{OfficeShell,Skyline}.fbx

OfficeShell child naming = how the Unity builder treats each object:
  Wall_* / Header_* / Stall_*  opaque, layer Walls, BoxCollider, OccluderWall (camera fade), static
  Glass_*                      layer Glass (blocks movement, not sight), BoxCollider, static
  Low_*                        layer Partitions (hides crouched players), BoxCollider, OccluderWall, static
  DoorFrame_* / GlassDoor_*    no collider, static
  Floor_* / Rug_* / Ground     no collider (one big floor collider is added in Unity), static
Every wall object is at most ~3.2 m long so the camera can fade only the pieces in front of the player.
"""
import json
import math
import os
import random
import shutil
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bmesh  # noqa: E402
import bpy  # noqa: E402

import bk_common as bk  # noqa: E402
import map_kit  # noqa: E402
import map_layout  # noqa: E402

MAX_CHUNK = 3.2
DOOR_H = map_layout.DOOR_H
FRAME = "M_WindowFrame"


# ================================================================================================ mesh builder

class MB:
    """Accumulates boxes/quads with materials into one mesh object."""

    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def box(self, x0, x1, y0, y1, z0, z1, m):
        if x1 - x0 < 1e-4 or y1 - y0 < 1e-4 or z1 - z0 < 1e-4:
            return
        map_kit.add_box_to_bm(self.bm, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), self.mi(m))

    def quad(self, x0, y0, x1, y1, z, m):
        vs = [self.bm.verts.new(p) for p in ((x0, y0, z), (x1, y0, z), (x1, y1, z), (x0, y1, z))]
        f = self.bm.faces.new(vs)
        f.material_index = self.mi(m)

    def obj(self, name, coll, parent):
        if not self.bm.faces:
            self.bm.free()
            return None
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        o = bk.mesh_obj(name, self.bm, coll, self.mats, parent)
        return o


# ================================================================================================ helpers

def room_at(rooms, x, y):
    for name, (x0, z0, x1, z1) in rooms.items():
        if x0 <= x <= x1 and z0 <= y <= z1:
            return "room." + name
    return "outside"


def oriented(axis, s0, s1, f0, f1):
    """Box extents in world XY from run (s) and perpendicular (f) ranges."""
    if axis == "x":
        return s0, s1, f0, f1
    return f0, f1, s0, s1


def wall_slab(mb, axis, s0, s1, f, t, z0, z1, st_a, st_b, skins=True):
    """Core (cap colour) + paint skins (wainscot, chair rail, upper paint, baseboard) on both faces."""
    x0, x1, y0, y1 = oriented(axis, s0, s1, f - t / 2, f + t / 2)
    mb.box(x0, x1, y0, y1, z0, z1, "M_WallCap")
    if not skins:
        return
    for sign, st in ((-1, st_a), (1, st_b)):
        if st is None:
            continue
        face = f + sign * t / 2

        def skin(za, zb, mat, depth=0.01, out=0.0):
            if zb - za < 1e-4:
                return
            fa, fb = sorted((face + sign * out, face + sign * (out + depth)))
            bx0, bx1, by0, by1 = oriented(axis, s0, s1, fa, fb)
            mb.box(bx0, bx1, by0, by1, za, zb, mat)

        lh = st.get("lowerH", 0.0) or 0.0
        if lh > 0:
            skin(max(z0, 0.0), min(z1, lh), st["lower"])
            skin(max(z0, lh), z1, st["upper"])
            if st.get("rail") and z0 < lh < z1:
                skin(lh - 0.02, lh + 0.03, st["rail"], depth=0.025)
        else:
            skin(z0, z1, st["upper"])
        if st.get("base") and z0 <= 0.001:
            skin(0.0, 0.1, st["base"], depth=0.018)


def window_unit(mb, axis, s0, s1, f, t, sill, top, inside_sign):
    w = s1 - s0
    fr = 0.06
    d0, d1 = f - t / 2 - 0.01, f + t / 2 + 0.01
    for sa, sb in ((s0, s0 + fr), (s1 - fr, s1)):
        x0, x1, y0, y1 = oriented(axis, sa, sb, d0, d1)
        mb.box(x0, x1, y0, y1, sill, top, FRAME)
    for za, zb in ((sill, sill + fr), (top - fr, top)):
        x0, x1, y0, y1 = oriented(axis, s0, s1, d0, d1)
        mb.box(x0, x1, y0, y1, za, zb, FRAME)
    n = max(1, int(round(w / 1.2)))
    for k in range(1, n):
        sm = s0 + w * k / n
        x0, x1, y0, y1 = oriented(axis, sm - 0.025, sm + 0.025, f - 0.04, f + 0.04)
        mb.box(x0, x1, y0, y1, sill, top, FRAME)
    x0, x1, y0, y1 = oriented(axis, s0 + fr, s1 - fr, f - 0.006, f + 0.006)
    mb.box(x0, x1, y0, y1, sill + fr, top - fr, "M_Glass")
    # interior sill board
    fa, fb = sorted((f + inside_sign * (t / 2 - 0.02), f + inside_sign * (t / 2 + 0.12)))
    x0, x1, y0, y1 = oriented(axis, s0 - 0.05, s1 + 0.05, fa, fb)
    mb.box(x0, x1, y0, y1, sill - 0.035, sill, "M_PlasticWhite")


def door_frame(mb, axis, s0, s1, f, t, h=DOOR_H, mat=FRAME, threshold=True):
    d0, d1 = f - t / 2 - 0.02, f + t / 2 + 0.02
    for sa, sb in ((s0 - 0.06, s0), (s1, s1 + 0.06)):
        x0, x1, y0, y1 = oriented(axis, sa, sb, d0, d1)
        mb.box(x0, x1, y0, y1, 0.0, h + 0.06, mat)
    x0, x1, y0, y1 = oriented(axis, s0 - 0.06, s1 + 0.06, d0, d1)
    mb.box(x0, x1, y0, y1, h, h + 0.06, mat)
    if threshold:
        x0, x1, y0, y1 = oriented(axis, s0, s1, f - t / 2, f + t / 2)
        mb.box(x0, x1, y0, y1, 0.0, 0.008, "M_Steel")


def glass_run(mb, axis, s0, s1, f, h):
    n = max(1, int(math.ceil((s1 - s0) / 1.25)))
    for k in range(n + 1):
        sm = s0 + (s1 - s0) * k / n
        sa, sb = max(s0, sm - 0.03), min(s1, sm + 0.03)
        x0, x1, y0, y1 = oriented(axis, sa, sb, f - 0.045, f + 0.045)
        mb.box(x0, x1, y0, y1, 0.0, h, FRAME)
    x0, x1, y0, y1 = oriented(axis, s0, s1, f - 0.05, f + 0.05)
    mb.box(x0, x1, y0, y1, 0.0, 0.1, FRAME)
    mb.box(x0, x1, y0, y1, h - 0.08, h, FRAME)
    x0, x1, y0, y1 = oriented(axis, s0, s1, f - 0.007, f + 0.007)
    mb.box(x0, x1, y0, y1, 0.1, h - 0.08, "M_Glass")
    x0, x1, y0, y1 = oriented(axis, s0, s1, f - 0.009, f + 0.009)
    mb.box(x0, x1, y0, y1, 1.05, 1.22, "M_PaintPaper")


def split_chunks(s0, s1):
    n = max(1, int(math.ceil((s1 - s0) / MAX_CHUNK - 1e-6)))
    return [(s0 + (s1 - s0) * k / n, s0 + (s1 - s0) * (k + 1) / n) for k in range(n)]


# ================================================================================================ shell

def build_shell(data, coll, parent):
    rooms = data["office"]["rooms"]
    styles = data["rooms"]
    H = data["wallHeight"]
    counters = {}
    made = []

    def name(prefix):
        counters[prefix] = counters.get(prefix, 0) + 1
        return "%s_%03d" % (prefix, counters[prefix])

    def emit(mb, prefix):
        o = mb.obj(name(prefix), coll, parent)
        if o is not None:
            made.append(o)
        return o

    # ---------------------------------------------------------------- floors
    rnd = random.Random(7)
    for fl in data["floors"]:
        x0, z0, x1, z1 = fl["rect"]
        mb = MB()
        mats = fl["mats"]
        tile = fl["tile"]
        mb.box(x0, x1, z0, z1, -0.12, -0.004, mats[0])
        if fl["pattern"] == "checker":
            nx, nz = int(round((x1 - x0) / tile)), int(round((z1 - z0) / tile))
            for i in range(nx):
                for j in range(nz):
                    mb.quad(x0 + i * tile, z0 + j * tile, x0 + (i + 1) * tile, z0 + (j + 1) * tile, 0.0, mats[(i + j) % 2])
        elif fl["pattern"] == "grid":
            mb.quad(x0, z0, x1, z1, -0.002, mats[1])
            nx, nz = int(round((x1 - x0) / tile)), int(round((z1 - z0) / tile))
            g = 0.012
            for i in range(nx):
                for j in range(nz):
                    mb.quad(x0 + i * tile + g, z0 + j * tile + g, x0 + (i + 1) * tile - g, z0 + (j + 1) * tile - g, 0.0, mats[0])
        else:  # planks along X
            mb.quad(x0, z0, x1, z1, -0.002, "M_FloorGap")
            rows = int(round((z1 - z0) / tile))
            g = 0.004
            for j in range(rows):
                x = x0 - rnd.uniform(0, 1.0)
                while x < x1:
                    ln = rnd.uniform(0.9, 1.7)
                    a, b = max(x, x0), min(x + ln, x1)
                    if b - a > 0.05:
                        mb.quad(a + g, z0 + j * tile + g, b - g, z0 + (j + 1) * tile - g, 0.0, mats[rnd.randint(0, 1)])
                    x += ln
        made.append(mb.obj("Floor_" + fl["room"].split(".")[1], coll, parent))

    # ---------------------------------------------------------------- rugs
    for rg in data["rugs"]:
        x0, z0, x1, z1 = rg["rect"]
        mb = MB()
        mb.box(x0, x1, z0, z1, 0.0, 0.012, rg["mat"])
        if rg.get("border"):
            b = 0.09
            i = 0.12
            for (a0, a1, c0, c1) in ((x0 + i, x1 - i, z0 + i, z0 + i + b), (x0 + i, x1 - i, z1 - i - b, z1 - i),
                                     (x0 + i, x0 + i + b, z0 + i, z1 - i), (x1 - i - b, x1 - i, z0 + i, z1 - i)):
                mb.box(a0, a1, c0, c1, 0.012, 0.014, rg["border"])
        emit(mb, "Rug")

    # ---------------------------------------------------------------- ground outside
    mb = MB()
    mb.box(-70, 70, -55, 55, -0.2, -0.05, "M_Leaf")
    mb.box(-19.5, 19.5, -13.5, 13.5, -0.05, -0.02, "M_Concrete")
    for k in range(-9, 10):
        mb.box(k * 2.0 - 0.02, k * 2.0 + 0.02, -13.5, 13.5, -0.02, -0.018, "M_PaintGreyDark")
    made.append(mb.obj("Ground", coll, parent))

    # ---------------------------------------------------------------- walls
    for w in data["walls"]:
        (ax, az), (bx, bz) = w["a"], w["b"]
        axis = "x" if abs(az - bz) < 1e-6 else "y"
        f = az if axis == "x" else ax
        s0, s1 = (min(ax, bx), max(ax, bx)) if axis == "x" else (min(az, bz), max(az, bz))
        t, h, kind = w["t"], w["h"], w["kind"]

        def side_styles(sm):
            if kind == "stall":
                return styles["stall"], styles["stall"]
            pa = (sm, f - t / 2 - 0.25) if axis == "x" else (f - t / 2 - 0.25, sm)
            pb = (sm, f + t / 2 + 0.25) if axis == "x" else (f + t / 2 + 0.25, sm)
            ra, rb = room_at(rooms, *pa), room_at(rooms, *pb)
            return styles[ra], styles[rb]

        def inside_sign(sm):
            pa = (sm, f - t / 2 - 0.25) if axis == "x" else (f - t / 2 - 0.25, sm)
            return -1 if room_at(rooms, *pa) != "outside" else 1

        # split into solid runs and openings
        ops = sorted(w["open"], key=lambda o: o["c"])
        pieces = []
        cur = s0
        for o in ops:
            a, b = o["c"] - o["w"] / 2, o["c"] + o["w"] / 2
            if a > cur:
                pieces.append(("solid", cur, a, None))
            pieces.append((o["t"], a, b, o))
            cur = b
        if cur < s1:
            pieces.append(("solid", cur, s1, None))

        if kind == "glass":
            for typ, a, b, o in pieces:
                if typ == "solid":
                    for ca, cb in split_chunks(a, b):
                        mb = MB()
                        glass_run(mb, axis, ca, cb, f, h)
                        emit(mb, "Glass")
                else:  # glass door: frame + transom
                    mb = MB()
                    door_frame(mb, axis, a, b, f, 0.1, mat=FRAME)
                    x0, x1, y0, y1 = oriented(axis, a, b, f - 0.007, f + 0.007)
                    mb.box(x0, x1, y0, y1, DOOR_H + 0.06, h - 0.08, "M_Glass")
                    x0, x1, y0, y1 = oriented(axis, a, b, f - 0.05, f + 0.05)
                    mb.box(x0, x1, y0, y1, h - 0.08, h, FRAME)
                    emit(mb, "GlassDoor")
            continue

        for typ, a, b, o in pieces:
            sm = (a + b) / 2
            st_a, st_b = side_styles(sm)
            if typ == "solid":
                for ca, cb in split_chunks(a, b):
                    st_a, st_b = side_styles((ca + cb) / 2)
                    mb = MB()
                    if kind == "low":
                        wall_slab(mb, axis, ca, cb, f, t, 0.0, h - 0.04, dict(st_a, lowerH=0, upper=st_a["lower"], rail=None),
                                  dict(st_b, lowerH=0, upper=st_b["lower"], rail=None))
                        x0, x1, y0, y1 = oriented(axis, ca, cb, f - t / 2 - 0.05, f + t / 2 + 0.05)
                        mb.box(x0, x1, y0, y1, h - 0.04, h, "M_DeskWood")
                        emit(mb, "Low")
                    elif kind == "stall":
                        wall_slab(mb, axis, ca, cb, f, t, 0.12, h, st_a, st_b)
                        for pa in (ca, cb):
                            x0, x1, y0, y1 = oriented(axis, pa - 0.03 if pa > ca else pa, pa + 0.03 if pa < cb else pa, f - 0.03, f + 0.03)
                            mb.box(x0, x1, y0, y1, 0.0, 0.12, "M_Steel")
                        emit(mb, "Stall")
                    else:
                        wall_slab(mb, axis, ca, cb, f, t, 0.0, h, st_a, st_b)
                        emit(mb, "Wall")
            elif typ == "window":
                mb = MB()
                wall_slab(mb, axis, a, b, f, t, 0.0, o["sill"], st_a, st_b)
                wall_slab(mb, axis, a, b, f, t, o["top"], h, st_a, st_b)
                window_unit(mb, axis, a, b, f, t, o["sill"], o["top"], inside_sign(sm))
                emit(mb, "Wall")
            elif typ in ("door", "opening"):
                mb = MB()
                wall_slab(mb, axis, a, b, f, t, DOOR_H + (0.06 if typ == "door" else 0.0), h, st_a, st_b)
                emit(mb, "Header")
                fr = MB()
                if typ == "door":
                    door_frame(fr, axis, a, b, f, t)
                else:
                    x0, x1, y0, y1 = oriented(axis, a, b, f - t / 2, f + t / 2)
                    fr.box(x0, x1, y0, y1, 0.0, 0.008, "M_Steel")
                emit(fr, "DoorFrame")
            elif typ == "entrance":
                mb = MB()
                wall_slab(mb, axis, a, b, f, t, DOOR_H + 0.06, h, st_a, st_b)
                door_frame(mb, axis, a, b, f, t, threshold=False)
                mid = (a + b) / 2
                for la, lb in ((a + 0.02, mid - 0.01), (mid + 0.01, b - 0.02)):
                    x0, x1, y0, y1 = oriented(axis, la, lb, f - 0.03, f + 0.03)
                    mb.box(x0, x1, y0, y1, 0.0, DOOR_H, FRAME)
                    x0, x1, y0, y1 = oriented(axis, la + 0.06, lb - 0.06, f - 0.035, f + 0.035)
                    mb.box(x0, x1, y0, y1, 0.08, DOOR_H - 0.06, "M_Glass")
                    hx = mid - 0.12 if lb < mid + 0.05 else mid + 0.12
                    x0, x1, y0, y1 = oriented(axis, hx - 0.015, hx + 0.015, f - 0.09, f + 0.09)
                    mb.box(x0, x1, y0, y1, 0.8, 1.5, "M_Steel")
                emit(mb, "Wall")
            # "gap": nothing (low wall / stall openings)
    return made


# ================================================================================================ skyline

def skyline(c):
    """Backdrop seen through the windows: low-poly city blocks with lit window bands (60 m wide)."""
    r = bk.empty("Skyline", c)
    rnd = random.Random(5)
    mb = MB()
    x = -30.0
    mats = ["M_Cabinet", "M_PaintGrey", "M_PaintManila", "M_PaintSlate", "M_StoneB", "M_PaintPeach"]
    while x < 30.0:
        w = rnd.uniform(3.0, 6.5)
        h = rnd.uniform(6.0, 22.0)
        d = rnd.uniform(3.0, 6.0)
        y = rnd.uniform(2.0, 9.0)
        m = rnd.choice(mats)
        mb.box(x, x + w, y, y + d, -3.0, h, m)
        z = 1.0
        while z < h - 1.0:
            mb.box(x + 0.4, x + w - 0.4, y - 0.02, y, z, z + 0.9, rnd.choice(["M_Glass", "M_Sky", "M_PaintSlate"]))
            z += 2.0
        mb.box(x - 0.1, x + w + 0.1, y - 0.1, y + d + 0.1, h, h + 0.3, "M_WallCap")
        x += w + rnd.uniform(0.5, 2.0)
    for k in range(14):
        tx = -28 + k * 4.3 + rnd.uniform(-0.5, 0.5)
        mb.box(tx - 0.1, tx + 0.1, -1.1, -0.9, -0.05, 1.6, "M_DarkWood")
    o = mb.obj("SkylineBlocks", c, r)
    rnd2 = random.Random(9)
    for k in range(14):
        tx = -28 + k * 4.3
        bk.ico("Tree", 1.1, c, "M_Leaf" if k % 2 else "M_LeafLight", r, (tx, -1.0, 2.3), scale=(1, 1, 1.15), subdiv=1, jitter=0.15, seed=k)
    return r


def main():
    data = map_layout.main()
    bk.reset_scene()
    coll = bk.collection("Architecture")
    shell_c = bk.collection("OfficeShell", coll)
    shell = bk.empty("OfficeShell", shell_c)
    objs = build_shell(data, shell_c, shell)
    sky_c = bk.collection("Skyline", coll)
    sky = skyline(sky_c)
    out_dir = os.path.join(bk.UNITY_MODELS, "Architecture")
    exp_dir = os.path.join(bk.EXPORT_DIR, "Architecture")
    os.makedirs(out_dir, exist_ok=True)
    os.makedirs(exp_dir, exist_ok=True)
    tris = 0
    for r in (shell, sky):
        path = os.path.join(out_dir, r.name + ".fbx")
        bk.export_fbx(r, path)
        shutil.copy2(path, os.path.join(exp_dir, r.name + ".fbx"))
        tris += map_kit.tri_count(r)
    sky.location = (0, 40, 0)
    bk.save_blend("architecture.blend")
    kinds = {}
    for o in objs:
        k = o.name.split("_")[0]
        kinds[k] = kinds.get(k, 0) + 1
    print("ARCH_DONE objects", len(objs), kinds, "tris", tris)


if __name__ == "__main__":
    main()
