"""v3 rooms: the player's private office and the meeting room (architecture shells, props, skyline, first-person hands)
plus the shared layout JSON that the Unity builder (Editor/ProjectBuilder.Rooms.cs) assembles the scene from.

Run:  blender -b --factory-startup -P Art/Scripts/build_rooms.py [-- --previews] [--fast]
Outputs:
  Game/Assets/Game/Art/Models/Rooms/<Name>.fbx     shells (RmOfficeShell, RmMeetingShell), props (Rm*), RmSkyline
  Game/Assets/Game/Art/Textures/Rooms/*.png        detail maps + monitor states (room_textures.py)
  Art/Layout/rooms_v3.json                         every placement / anchor / light in Unity coordinates
  Art/Blender/rooms.blend, Art/Previews/rooms/*.png (EEVEE, with --previews)

Units: metres. Layout positions are Unity (x, y up, z forward); yaw in degrees (clockwise seen from above).
The player sits facing +Z in the office; the meeting table runs along Z with the manager and the projector at +Z.
UVs: every mesh except the decal/screen surfaces gets box-projected UVs in metres, so the detail textures tile by
world size (1 UV = 1 m); material tiling is set per material in Art/palette_rooms.json.
"""
import json
import math
import os
import random
import shutil
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
for _m in ("room_props", "room_textures"):
    sys.modules.pop(_m, None)
import bmesh  # noqa: E402
import bpy  # noqa: E402
from mathutils import Matrix, Vector  # noqa: E402

import bk_common as bk  # noqa: E402
import map_kit  # noqa: E402
import room_props as rp  # noqa: E402
import room_textures  # noqa: E402

R = math.radians
ARGS = sys.argv[sys.argv.index("--") + 1:] if "--" in sys.argv else []
OUT_MODELS = os.path.join(bk.UNITY_MODELS, "Rooms")
LAYOUT_PATH = os.path.join(bk.ROOT, "Art", "Layout", "rooms_v3.json")
PREV_DIR = os.path.join(bk.PREVIEW_DIR, "rooms")

# ================================================================================================ layout (Unity coords)

OFFICE = {
    # v5: 4.6 x 4.0 m so the player can take a few steps (v3: 3.6 x 3.2). The desk wall (z1), the desk kit, the window
    # and the door keep their places relative to the seat; the room grows sideways and behind the chair.
    "x0": -2.3, "x1": 2.3, "z0": -2.4, "z1": 1.6, "h": 2.8, "t": 0.14,
    "window": {"wall": "x0", "s0": -0.9, "s1": 1.2, "sill": 0.86, "top": 2.32},
    "door": {"wall": "x1", "s0": -1.3, "s1": -0.4, "h": 2.1, "glass": [1.08, 1.92]},
}
MEET_ORIGIN = (0.0, 0.0, 30.0)
MEETING = {
    "x0": -2.3, "x1": 2.3, "z0": -3.0, "z1": 3.0, "h": 2.9, "t": 0.14,
    "windows": [{"wall": "x0", "s0": -2.35, "s1": -0.55, "sill": 0.8, "top": 2.4}, {"wall": "x0", "s0": 0.15, "s1": 1.95, "sill": 0.8, "top": 2.4}],
    "door": {"wall": "z0", "s0": 0.85, "s1": 1.75, "h": 2.15, "glass": [1.1, 1.95]},
}

# id, model, position, yaw, extra
OFFICE_PROPS = [
    ("desk", "RmDesk", (0.0, 0.0, 1.18), 0),
    ("chair", "RmChair", (0.0, 0.0, 0.36), 0),
    ("monitor", "RmMonitor", (0.0, 0.75, 1.37), 0),
    ("keyboard", "RmKeyboard", (-0.02, 0.75, 1.02), 0),
    ("mouse", "RmMouse", (0.34, 0.75, 1.04), -4),
    ("phone", "RmPhone", (0.62, 0.75, 1.1), 26),
    ("recorder", "RmRecorder", (0.47, 0.75, 1.385), -10),
    ("mug", "RmMug", (-0.35, 0.75, 0.92), 205),
    ("spill", "RmSpill", (-0.16, 0.751, 0.93), 0),
    ("notepad", "RmNotepad", (-0.71, 0.75, 0.94), -12),
    # v5 task objects: the Petty Cash box (left of the keyboard, turned toward the seat; shown only during Petty
    # Cash) and the Weekend Shift roster (clipboard on the desk wall right of the cork board, reachable from the chair)
    ("cashbox", "RmCashBox", (-0.58, 0.75, 1.19), -24),
    ("roster", "RmRoster", (1.52, 1.42, 1.6), 0),
    ("lamp", "RmLamp", (-0.6, 0.75, 1.4), 14),
    ("photo", "RmPhotoFrame", (-0.38, 0.75, 1.47), 10),
    ("nameplate", "RmNameplate", (0.2, 0.75, 1.255), -6),
    ("pencup", "RmPenCup", (0.2, 0.75, 1.47), 0),
    ("drawerunit", "RmDrawerUnit", (1.08, 0.0, 1.24), 0),
    ("intray", "RmInTray", (1.06, 0.66, 1.12), 4),
    ("smallplant", "RmSmallPlant", (1.13, 0.66, 1.42), 0),
    ("bin", "RmBin", (-0.98, 0.0, 0.98), 0),
    ("plant", "RmPlant", (-1.78, 0.0, 1.18), 30),
    ("rug", "RmRug", (0.0, 0.0, 0.3), 0),
    ("coatrack", "RmCoatRack", (1.97, 0.0, -1.78), 200),
    ("filecabinet", "RmFileCabinet", (2.02, 0.0, 0.42), 90),
    ("bookcase", "RmLowBookcase", (-1.2, 0.0, -2.23), 180),
    ("sideboard", "RmSideboard", (0.62, 0.0, -2.15), 180),
    ("traycups", "RmTrayCups", (1.1, 0.76, -2.17), 180),
    ("sideplant", "RmSmallPlant", (0.05, 0.76, -2.18), 0),
    ("tallplant", "RmTallPlant", (-2.0, 0.0, -1.95), 20),
    ("armchair", "RmMeetingChair", (-1.8, 0.0, -1.1), 72),
    ("shelf", "RmShelf", (-0.42, 1.48, 1.6), 0),
    ("cork", "RmCorkBoard", (0.8, 1.45, 1.6), 0),
    ("clock", "RmClock", (0.8, 2.22, 1.6), 0),
    ("ceilinglamp", "RmCeilingLamp", (0.0, 2.8, 0.35), 0),
    ("ceilinglamp2", "RmCeilingLamp", (0.0, 2.8, -1.45), 0),
    ("cactus", "RmCactus", (-2.255, 0.87, 0.15), 0),
]
OFFICE_ANCHORS = {
    "eye": (0.0, 1.24, 0.42), "eyeYaw": 0.0, "eyePitch": 15.0,
    "yawLimit": 115.0, "pitchUp": 40.0, "pitchDown": 65.0,
    "corridor": {"x": 2.75, "z0": -4.0, "z1": 3.0},
    "handL": (-0.21, 0.775, 0.97), "handR": (0.32, 0.785, 1.0),
    # v5: where the Petty Cash notes lie on the desk once you take them out of the box (right of the box)
    "cashPile": (-0.33, 0.75, 1.2), "cashPileYaw": -8.0,
}
# Walking (v5): where the player stands after getting up, the chair pushed back, eye height, body radius, and the
# furniture footprints the runtime collides with (Unity x/z, half extents, yaw). Generated from OFFICE_PROPS below.
WALK_RECTS = {"RmDesk": (1.62, 0.78), "RmDrawerUnit": (0.46, 0.6), "RmFileCabinet": (0.48, 0.58),
              "RmLowBookcase": (1.12, 0.36), "RmSideboard": (1.82, 0.48)}
WALK_CIRCLES = {"RmPlant": 0.3, "RmTallPlant": 0.22, "RmCoatRack": 0.25, "RmBin": 0.17, "RmMeetingChair": 0.36}
OFFICE_WALK = {"eye": 1.56, "radius": 0.24, "stand": (0.0, 0.54), "chairBack": (0.06, -0.06), "chairBackYaw": 14.0,
               "chairRadius": 0.3, "doorLamp": (2.3, 2.24, -0.85)}
OFFICE_DECALS = [
    {"id": "poster", "p": (-1.27, 1.6, 1.6), "r": 0, "size": (0.5, 0.75),
     "variants": ["poster_kahve", "poster_sadakat", "poster_takim_ruhu", "poster_verimlilik", "poster_motivation", "poster_teamwork"]},
    {"id": "poster2", "p": (2.3, 1.68, 0.42), "r": 90, "size": (0.46, 0.65),
     "variants": ["poster_teamwork", "poster_safety", "poster_motivation", "poster_kahve", "poster_takim_ruhu", "poster_sadakat"]},
    {"id": "diploma", "p": (-1.2, 1.55, -2.4), "r": 180, "size": (0.45, 0.34), "variants": ["diploma"]},
    {"id": "poster3", "p": (0.62, 1.62, -2.4), "r": 180, "size": (0.62, 0.46), "variants": ["poster_kpi"]},
    {"id": "poster4", "p": (-2.3, 1.62, -1.55), "r": -90, "size": (0.42, 0.56), "variants": ["poster_mission"]},
]
OFFICE_LIGHTS = [
    {"name": "Ceiling", "t": "point", "p": (0.0, 2.5, 0.35), "color": "#FFE2B5", "i": 1.4, "range": 6.0},
    {"name": "Ceiling2", "t": "point", "p": (0.0, 2.5, -1.45), "color": "#FFE2B5", "i": 1.15, "range": 5.5},
    {"name": "DeskLamp", "t": "spot", "p": (-0.6, 1.06, 1.38), "pitch": 90, "yaw": 0, "angle": 110, "color": "#FFD08A", "i": 2.2, "range": 1.8},
    {"name": "Monitor", "t": "point", "p": (0.0, 1.06, 1.22), "color": "#A9DCEB", "i": 0.45, "range": 1.4},
    {"name": "WindowFill", "t": "point", "p": (-1.7, 1.7, 0.15), "color": "#D3E3F7", "i": 1.0, "range": 5.5},
    {"name": "Corridor", "t": "point", "p": (3.05, 2.4, -0.85), "color": "#FFF0D8", "i": 2.2, "range": 4.0},
]


def walk_layout():
    """Collision footprints for the runtime walker (Unity coordinates)."""
    rects, circles = [], []
    for pid, model, p, yaw in OFFICE_PROPS:
        if model in WALK_RECTS:
            w, d = WALK_RECTS[model]
            rects.append({"id": pid, "c": [p[0], p[2]], "h": [w / 2, d / 2], "r": yaw})
        elif model in WALK_CIRCLES:
            circles.append({"id": pid, "c": [p[0], p[2]], "r": WALK_CIRCLES[model]})
    out = dict(OFFICE_WALK)
    out["rects"] = rects
    out["circles"] = circles
    return out


SEAT_Z = (0.95, -0.15, -1.25)
SEATS = []  # seat order: alternating sides, nearest to the manager first
for k, z in enumerate(SEAT_Z):
    SEATS.append({"p": (-1.02, 0.0, z), "yaw": 90.0})
    SEATS.append({"p": (1.02, 0.0, z), "yaw": -90.0})

MEETING_PROPS = [
    ("table", "RmMeetingTable", (0.0, 0.0, -0.15), 0),
    ("jug", "RmJug", (0.0, 0.75, 0.3), 0),
    ("speakerphone", "RmSpeakerphone", (0.0, 0.75, -0.75), 0),
    ("screen", "RmProjectorScreen", (-0.25, 1.32, 3.0), 0),
    ("projector", "RmProjector", (-0.25, 2.9, 0.45), 180),
    ("whiteboard", "RmWhiteboard", (2.3, 1.62, -0.35), 90),
    ("clock", "RmClock", (2.3, 2.3, 1.55), 90),
    # v5 step 3a: a mobile whiteboard in front of the window wall between the windows, facing the right-hand seats,
    # and the ballot box on the table centre (the seat padfolios + pencils are placed per seat by the Unity builder)
    ("whiteboard2", "RmWhiteboardStand", (-1.98, 0.0, -0.2), -90),
    # v5 step 3b: turned across the table, its front label faces the left row and the back label the right row
    ("ballotbox", "RmBallotBox", (0.0, 0.75, -0.2), 90),
    ("sideboard", "RmSideboard", (-0.9, 0.0, -2.74), 180),
    ("traycups", "RmTrayCups", (-1.35, 0.76, -2.72), 180),
    ("sideplant", "RmSmallPlant", (-0.35, 0.76, -2.76), 0),
    ("plant1", "RmTallPlant", (-1.9, 0.0, 2.55), 0),
    ("plant2", "RmTallPlant", (1.9, 0.0, 2.55), 40),
    ("plant3", "RmTallPlant", (1.95, 0.0, -2.6), 80),
    ("pendant1", "RmPendantBar", (0.0, 2.9, 0.65), 0),
    ("pendant2", "RmPendantBar", (0.0, 2.9, -1.05), 0),
]
MEETING_ANCHORS = {
    "manager": (0.95, 0.0, 2.15), "managerYaw": 205.0,
    "eyeUp": 1.24, "eyeForward": 0.06,
    # v5 step 3a: seated free look of about 110 degrees in total (docs/V5_INTERACTION.md §3), pitch limited
    "yawLimit": 55.0, "pitchUp": 30.0, "pitchDown": 50.0,
    "seatItemInset": 0.6,
}
MEETING_DECALS = [
    {"id": "portrait", "p": (-0.9, 1.7, -3.0), "r": 180, "size": (0.52, 0.68), "variants": ["portrait_manager"]},
    # v5: moved past the second window (the whiteboard stand stands in front of the pier between the windows)
    {"id": "mission", "p": (-2.3, 1.7, 2.45), "r": -90, "size": (0.4, 0.56), "variants": ["poster_mission"]},
    {"id": "kpi", "p": (2.3, 1.6, 1.85), "r": 90, "size": (0.6, 0.45), "variants": ["poster_kpi"]},
]
MEETING_LIGHTS = [
    {"name": "Pendant1", "t": "point", "p": (0.0, 2.15, 0.65), "color": "#FFE0B0", "i": 1.3, "range": 4.5},
    {"name": "Pendant2", "t": "point", "p": (0.0, 2.15, -1.05), "color": "#FFE0B0", "i": 1.3, "range": 4.5},
    {"name": "WindowFill", "t": "point", "p": (-1.6, 1.8, -0.2), "color": "#D3E3F7", "i": 1.1, "range": 5.5},
    {"name": "ScreenSpill", "t": "point", "p": (-0.25, 1.9, 2.55), "color": "#F2F4FF", "i": 0.5, "range": 2.6},
    {"name": "Corridor", "t": "point", "p": (1.3, 2.4, -3.9), "color": "#FFF0D8", "i": 2.0, "range": 4.0},
]


def u2b(p):
    return (p[0], p[2], p[1])


# ================================================================================================ mesh builder

class MB:
    def __init__(self):
        self.bm = bmesh.new()
        self.mats = []

    def mi(self, m):
        if m not in self.mats:
            self.mats.append(m)
        return self.mats.index(m)

    def box(self, x0, x1, y0, y1, z0, z1, m):
        """Blender-space axis-aligned box."""
        if x1 - x0 < 1e-5 or y1 - y0 < 1e-5 or z1 - z0 < 1e-5:
            return
        map_kit.add_box_to_bm(self.bm, ((x0 + x1) / 2, (y0 + y1) / 2, (z0 + z1) / 2), (x1 - x0, y1 - y0, z1 - z0), self.mi(m))

    def rbox(self, center, size, rot_x, m):
        """Box rotated about X (blind slats)."""
        faces = map_kit.add_box_to_bm(self.bm, (0, 0, 0), size, self.mi(m))
        verts = {v for f in faces for v in f.verts}
        c, s = math.cos(rot_x), math.sin(rot_x)
        for v in verts:
            y, z = v.co.y, v.co.z
            v.co = Vector((v.co.x + center[0], y * c - z * s + center[1], y * s + z * c + center[2]))

    def obj(self, name, coll, parent):
        if not self.bm.faces:
            self.bm.free()
            return None
        bmesh.ops.recalc_face_normals(self.bm, faces=self.bm.faces)
        return bk.mesh_obj(name, self.bm, coll, self.mats, parent)


def wall_run(mb, axis, s0, s1, f, t, h, inside, openings, bands):
    """One straight wall. axis 'x' = runs along Blender X at y=f; 'y' = runs along Blender Y at x=f.
    inside = +1/-1: side of the room. openings = [(a, b, zb, zt)] along the run. bands = [(z0, z1, mat, extra_depth)]
    painted bands on the inside face (extra_depth > 0 sticks out: baseboard, chair rail, crown)."""
    def put(a, b, z0, z1, d0, d1, m):
        if axis == "x":
            mb.box(a, b, min(d0, d1), max(d0, d1), z0, z1, m)
        else:
            mb.box(min(d0, d1), max(d0, d1), a, b, z0, z1, m)
    # solid pieces: list of (a, b, z0, z1)
    pieces = []
    cuts = sorted(openings)
    cur = s0
    for (a, b, zb, zt) in cuts:
        if a > cur:
            pieces.append((cur, a, 0.0, h))
        if zb > 0:
            pieces.append((a, b, 0.0, zb))
        if zt < h:
            pieces.append((a, b, zt, h))
        cur = b
    if cur < s1:
        pieces.append((cur, s1, 0.0, h))
    inner = f + inside * t / 2
    outer = f - inside * t / 2
    for (a, b, z0, z1) in pieces:
        # core (exterior material on the outside half, paint on the inside half)
        put(a, b, z0, z1, outer, f, "M_RmExterior")
        for (bz0, bz1, m, extra) in bands:
            za, zb2 = max(z0, bz0), min(z1, bz1)
            if zb2 - za < 1e-4:
                continue
            put(a, b, za, zb2, f, inner + inside * extra, m)


def window_unit(mb, glass, blinds, axis, s0, s1, f, t, inside, sill, top, blind_drop=0.55):
    fr = 0.055
    d0, d1 = f - t / 2 - 0.005, f + t / 2 + 0.005
    inner = f + inside * t / 2

    def put(a, b, z0, z1, e0, e1, m, target=mb):
        if axis == "x":
            target.box(a, b, min(e0, e1), max(e0, e1), z0, z1, m)
        else:
            target.box(min(e0, e1), max(e0, e1), a, b, z0, z1, m)
    # reveal lining (paint) and frame
    for a, b in ((s0, s0 + fr), (s1 - fr, s1)):
        put(a, b, sill, top, f - 0.03, f + 0.03, "M_RmFrame")
    put(s0, s1, sill, sill + fr, f - 0.03, f + 0.03, "M_RmFrame")
    put(s0, s1, top - fr, top, f - 0.03, f + 0.03, "M_RmFrame")
    n = max(1, int(round((s1 - s0) / 0.95)))
    for k in range(1, n):
        sm = s0 + (s1 - s0) * k / n
        put(sm - 0.025, sm + 0.025, sill, top, f - 0.025, f + 0.025, "M_RmFrame")
    put(s0 + 0.2 * 0, s1, sill + (top - sill) * 0.62 - 0.02, sill + (top - sill) * 0.62 + 0.02, f - 0.025, f + 0.025, "M_RmFrame")
    # inner reveal (inside half of the wall thickness)
    for a, b in ((s0 - 0.0, s0 + 0.0), ):
        pass
    put(s0, s0 + 0.01, sill, top, f, inner, "M_RmWallCream")
    put(s1 - 0.01, s1, sill, top, f, inner, "M_RmWallCream")
    put(s0, s1, top - 0.01, top, f, inner, "M_RmWallCream")
    # interior sill board + exterior stone ledge
    put(s0 - 0.06, s1 + 0.06, sill - 0.035, sill + 0.01, f, inner + inside * 0.1, "M_RmTrim")
    put(s0 - 0.04, s1 + 0.04, sill - 0.06, sill, f - inside * (t / 2 + 0.08), f, "M_RmExterior")
    # glass
    put(s0 + fr, s1 - fr, sill + fr, top - fr, f - 0.004, f + 0.004, "M_RmWindowGlass", glass)
    # venetian blinds on the inside: head rail, tilted slats down to blind_drop of the height, bottom rail, cord
    bz_top = top - 0.02
    bz_bot = top - (top - sill) * blind_drop
    rail_e0, rail_e1 = inner + inside * 0.02, inner + inside * 0.08
    put(s0 + 0.02, s1 - 0.02, bz_top - 0.05, bz_top, rail_e0, rail_e1, "M_RmBlind", blinds)
    zz = bz_top - 0.07
    mid = (rail_e0 + rail_e1) / 2
    while zz > bz_bot:
        if axis == "x":
            blinds.rbox(((s0 + s1) / 2, mid, zz), (s1 - s0 - 0.06, 0.05, 0.003), R(38) * inside, "M_RmBlind")
        else:
            # run along Y: slat long along Y, tilt about Y -> rotate in the XZ plane (swap via rbox on a temp)
            faces = map_kit.add_box_to_bm(blinds.bm, (0, 0, 0), (0.05, s1 - s0 - 0.06, 0.003), blinds.mi("M_RmBlind"))
            verts = {v for fc in faces for v in fc.verts}
            ang = R(38) * inside
            cc, ss = math.cos(ang), math.sin(ang)
            for v in verts:
                x, z = v.co.x, v.co.z
                v.co = Vector((x * cc - z * ss + mid, v.co.y + (s0 + s1) / 2, x * ss + z * cc + zz))
        zz -= 0.042
    put(s0 + 0.03, s1 - 0.03, bz_bot - 0.03, bz_bot, rail_e0, rail_e1, "M_RmBlind", blinds)
    for k, sx in enumerate((s0 + 0.12, s1 - 0.12)):
        put(sx - 0.003, sx + 0.003, bz_bot - 0.45 - 0.1 * k, bz_top - 0.05, mid - 0.003, mid + 0.003, "M_RmBlind", blinds)
        put(sx - 0.01, sx + 0.01, bz_bot - 0.5 - 0.1 * k, bz_bot - 0.45 - 0.1 * k, mid - 0.01, mid + 0.01, "M_RmBrass", blinds)


def door_unit(mb, glass_mb, axis, s0, s1, f, t, inside, h, glass_z, leaf_mat="M_RmWalnut"):
    def put(a, b, z0, z1, e0, e1, m, target=mb):
        if axis == "x":
            target.box(a, b, min(e0, e1), max(e0, e1), z0, z1, m)
        else:
            target.box(min(e0, e1), max(e0, e1), a, b, z0, z1, m)
    inner = f + inside * t / 2
    # casing on the inside face
    cw = 0.07
    for a, b in ((s0 - cw, s0), (s1, s1 + cw)):
        put(a, b, 0.0, h + cw, inner - inside * 0.01, inner + inside * 0.02, "M_RmTrim")
    put(s0 - cw, s1 + cw, h, h + cw, inner - inside * 0.01, inner + inside * 0.02, "M_RmTrim")
    # jamb lining
    put(s0, s0 + 0.02, 0.0, h, f - t / 2, f + t / 2, "M_RmTrim")
    put(s1 - 0.02, s1, 0.0, h, f - t / 2, f + t / 2, "M_RmTrim")
    put(s0, s1, h - 0.02, h, f - t / 2, f + t / 2, "M_RmTrim")
    # leaf (closed), 4 cm thick, set slightly inside; glass pane in the upper half
    l0, l1 = f + inside * 0.0, f + inside * 0.04
    a, b = s0 + 0.02, s1 - 0.02
    gz0, gz1 = glass_z
    st = 0.11
    put(a, b, 0.0, gz0, l0, l1, leaf_mat)
    put(a, b, gz1, h - 0.02, l0, l1, leaf_mat)
    put(a, a + st, gz0, gz1, l0, l1, leaf_mat)
    put(b - st, b, gz0, gz1, l0, l1, leaf_mat)
    # raised panel + glazing beads + kick plate
    put(a + 0.1, b - 0.1, 0.25, gz0 - 0.12, l1, l1 + inside * 0.008, leaf_mat)
    for zz in (gz0 - 0.015, gz1):
        put(a + st - 0.015, b - st + 0.015, zz, zz + 0.015, l1, l1 + inside * 0.012, "M_RmBrass")
    put(a + 0.03, b - 0.03, 0.02, 0.2, l1, l1 + inside * 0.004, "M_RmBrass")
    put(a + st, b - st, gz0, gz1, f + inside * 0.016, f + inside * 0.024, "M_RmFrosted", glass_mb)
    # lever handle on the inside (hinge side = s0)
    hx = b - 0.08
    put(hx - 0.025, hx + 0.025, 0.95, 1.1, l1, l1 + inside * 0.01, "M_RmBrass")
    put(hx - 0.11, hx + 0.012, 1.015, 1.035, l1 + inside * 0.04, l1 + inside * 0.06, "M_RmBrass")
    put(hx - 0.012, hx + 0.012, 1.015, 1.035, l1, l1 + inside * 0.06, "M_RmBrass")


def floor_ceiling(mb, x0, x1, y0, y1, h, floor_mat, ceil_mat):
    mb.box(x0 - 0.2, x1 + 0.2, y0 - 0.2, y1 + 0.2, -0.06, 0.0, floor_mat)
    mb.box(x0 - 0.2, x1 + 0.2, y0 - 0.2, y1 + 0.2, h, h + 0.06, ceil_mat)


def shell(name, coll, room, style, details_fn=None):
    """Builds one room: Walls (painted bands), Floor, Ceiling, WindowFrame (+Glass, Blinds), Door (+DoorGlass),
    Corridor (outside the door), Details. Returns the root."""
    r = bk.empty(name, coll)
    x0, x1, y0, y1, h, t = room["x0"], room["x1"], room["z0"], room["z1"], room["h"], room["t"]
    bands = style["bands"](h)
    walls, frames, glass, blinds, door, dglass, corridor, floor, ceil = MB(), MB(), MB(), MB(), MB(), MB(), MB(), MB(), MB()
    wins = room.get("windows") or [room["window"]]
    dr = room["door"]

    def openings(wall):
        out = []
        for w in wins:
            if w["wall"] == wall:
                out.append((w["s0"], w["s1"], w["sill"], w["top"]))
        if dr["wall"] == wall:
            out.append((dr["s0"], dr["s1"], 0.0, dr["h"]))
        return out
    # walls: south y=y0 (inside +1), north y=y1 (inside -1) run along X; west x=x0 (+1), east x=x1 (-1) run along Y
    wall_run(walls, "x", x0 - t, x1 + t, y0 - t / 2, t, h, +1, openings("z0"), bands)
    wall_run(walls, "x", x0 - t, x1 + t, y1 + t / 2, t, h, -1, openings("z1"), bands)
    wall_run(walls, "y", y0, y1, x0 - t / 2, t, h, +1, openings("x0"), bands)
    wall_run(walls, "y", y0, y1, x1 + t / 2, t, h, -1, openings("x1"), bands)
    for w in wins:
        if w["wall"] == "x0":
            window_unit(frames, glass, blinds, "y", w["s0"], w["s1"], x0 - t / 2, t, +1, w["sill"], w["top"], style.get("blind", 0.5))
        elif w["wall"] == "x1":
            window_unit(frames, glass, blinds, "y", w["s0"], w["s1"], x1 + t / 2, t, -1, w["sill"], w["top"], style.get("blind", 0.5))
    if dr["wall"] == "x1":
        door_unit(door, dglass, "y", dr["s0"], dr["s1"], x1 + t / 2, t, -1, dr["h"], dr["glass"])
        cx0, cx1 = x1 + t, x1 + t + 1.9
        corridor.box(cx0, cx1, y0 - 3.0, y1 + 3.0, -0.06, 0.0, "M_RmCorridorFloor")
        corridor.box(cx0, cx1, y0 - 3.0, y1 + 3.0, h, h + 0.05, "M_RmCeiling")
        corridor.box(cx1, cx1 + 0.1, y0 - 3.0, y1 + 3.0, 0.0, h, "M_RmCorridor")
        corridor.box(cx1 - 0.02, cx1, y0 - 3.0, y1 + 3.0, 0.0, 0.1, "M_RmBlack")
        corridor.box(cx0, cx1, y0 - 3.1, y0 - 3.0, 0.0, h, "M_RmCorridor")
        corridor.box(cx0, cx1, y1 + 3.0, y1 + 3.1, 0.0, h, "M_RmCorridor")
    elif dr["wall"] == "z0":
        door_unit(door, dglass, "x", dr["s0"], dr["s1"], y0 - t / 2, t, +1, dr["h"], dr["glass"])
        cy1, cy0 = y0 - t, y0 - t - 1.9
        corridor.box(x0 - 3.0, x1 + 3.0, cy0, cy1, -0.06, 0.0, "M_RmCorridorFloor")
        corridor.box(x0 - 3.0, x1 + 3.0, cy0, cy1, h, h + 0.05, "M_RmCeiling")
        corridor.box(x0 - 3.0, x1 + 3.0, cy0 - 0.1, cy0, 0.0, h, "M_RmCorridor")
        corridor.box(x0 - 3.1, x0 - 3.0, cy0, cy1, 0.0, h, "M_RmCorridor")
        corridor.box(x1 + 3.0, x1 + 3.1, cy0, cy1, 0.0, h, "M_RmCorridor")
    floor_ceiling(floor, x0, x1, y0, y1, h, style["floor"], "M_RmCeiling")
    # crown + cove on the ceiling edge handled by bands; ceiling details
    det = MB()
    if details_fn:
        details_fn(det)
    out = {}
    for nm, m in (("Walls", walls), ("WindowFrame", frames), ("WindowGlass", glass), ("Blinds", blinds), ("Door", door),
                  ("DoorGlass", dglass), ("Corridor", corridor), ("Floor", floor), ("Details", det)):
        o = m.obj(nm, coll, r)
        if o is not None:
            out[nm] = o
    return r


def office_bands(h):
    return [(0.0, 0.1, "M_RmWalnutDark", 0.016), (0.1, 0.9, "M_RmWainscot", 0.012), (0.9, 0.95, "M_RmTrim", 0.026),
            (0.95, h - 0.09, "M_RmWallCream", 0.0), (h - 0.09, h, "M_RmTrim", 0.03)]


def meeting_bands(h):
    return [(0.0, 0.1, "M_RmWalnutDark", 0.016), (0.1, 1.05, "M_RmPanelWood", 0.014), (1.05, 1.1, "M_RmTrim", 0.03),
            (1.1, h - 0.09, "M_RmWallWarm", 0.0), (h - 0.09, h, "M_RmTrim", 0.03)]


def office_details(mb):
    o = OFFICE
    x0, x1, y0, y1, h = o["x0"], o["x1"], o["z0"], o["z1"], o["h"]
    w = o["window"]
    # radiator under the window (Blender: x = Unity x, y = Unity z)
    rx = x0 + 0.06
    ya, yb = w["s0"] + 0.25, w["s1"] - 0.25
    n = int((yb - ya) / 0.06)
    for k in range(n):
        yy = ya + k * (yb - ya) / n
        mb.box(rx, rx + 0.09, yy, yy + 0.045, 0.16, 0.7, "M_RmRadiator")
    mb.box(rx + 0.02, rx + 0.07, ya, yb, 0.12, 0.16, "M_RmRadiator")
    mb.box(rx + 0.02, rx + 0.07, ya, yb, 0.7, 0.73, "M_RmRadiator")
    mb.box(rx + 0.03, rx + 0.06, yb + 0.02, yb + 0.07, 0.1, 0.2, "M_RmBrass")
    # light switch + outlet + thermostat
    mb.box(x1 - 0.012, x1, -0.3 + 0.05, -0.3 + 0.13, 1.18, 1.3, "M_RmCream")
    mb.box(x1 - 0.018, x1 - 0.012, -0.3 + 0.08, -0.3 + 0.1, 1.22, 1.26, "M_RmBrass")
    mb.box(0.55, 0.63, y1 - 0.012, y1, 0.28, 0.36, "M_RmCream")
    mb.box(-1.0, -0.9, y1 - 0.015, y1, 1.42, 1.52, "M_RmCream")
    # smoke detector + vent on the ceiling
    mb.box(0.7, 0.82, -0.6, -0.48, h - 0.03, h, "M_RmCream")
    mb.box(-0.9, -0.5, -1.0, -0.8, h - 0.012, h, "M_RmFrame")
    for k in range(5):
        mb.box(-0.88 + k * 0.08, -0.85 + k * 0.08, -0.98, -0.82, h - 0.016, h - 0.012, "M_RmGraphite")


def meeting_details(mb):
    m = MEETING
    x0, x1, y0, y1, h = m["x0"], m["x1"], m["z0"], m["z1"], m["h"]
    for w in m["windows"]:
        rx = x0 + 0.06
        ya, yb = w["s0"] + 0.2, w["s1"] - 0.2
        n = int((yb - ya) / 0.06)
        for k in range(n):
            yy = ya + k * (yb - ya) / n
            mb.box(rx, rx + 0.09, yy, yy + 0.045, 0.14, 0.62, "M_RmRadiator")
        mb.box(rx + 0.02, rx + 0.07, ya, yb, 0.1, 0.14, "M_RmRadiator")
    mb.box(1.95, 2.05, y0, y0 + 0.012, 1.18, 1.3, "M_RmCream")
    mb.box(-0.6, 0.6, -0.1, 0.1, h - 0.012, h, "M_RmFrame")
    mb.box(1.5, 1.62, 1.8, 1.92, h - 0.03, h, "M_RmCream")


# ================================================================================================ skyline

def skyline(coll):
    """Buildings across the street (west of both rooms), Unity x -12..-150, z -45..85, y -22..60. Facade UVs in metres."""
    r = bk.empty("RmSkyline", coll)
    mb = MB()
    far = MB()
    rnd = random.Random(42)
    facades = ["M_RmFacade", "M_RmFacadeB", "M_RmFacadeC"]
    street = -21.0

    def building(xa, xb, ya, yb, top, m):
        mb.box(xa, xb, ya, yb, street, top, m)
        mb.box(xa - 0.25, xb + 0.25, ya - 0.25, yb + 0.25, top, top + 0.5, "M_RmRoof")
        mb.box(xa + 0.3, xb - 0.3, ya + 0.3, yb - 0.3, top + 0.5, top + 0.55, "M_RmRoof")
        if rnd.random() < 0.6:
            cx, cy = rnd.uniform(xa + 1.5, xb - 1.5), rnd.uniform(ya + 1.5, yb - 1.5)
            mb.box(cx - 1.0, cx + 1.0, cy - 1.0, cy + 1.0, top + 0.5, top + 2.0, "M_RmGraphite")
        if rnd.random() < 0.4:
            cx, cy = rnd.uniform(xa + 1, xb - 1), rnd.uniform(ya + 1, yb - 1)
            mb.box(cx - 0.05, cx + 0.05, cy - 0.05, cy + 0.05, top + 0.5, top + 4.5, "M_RmGraphite")
    # near row across the street: facades face +x (Blender +x = Unity +x)
    y = -45.0
    while y < 85.0:
        w = rnd.uniform(9.0, 16.0)
        top = rnd.uniform(-8.0, 16.0)
        depth = rnd.uniform(10.0, 16.0)
        building(-14.0 - depth, -14.0, y, y + w, top, rnd.choice(facades))
        y += w + rnd.uniform(0.0, 3.0)
    # second row, taller
    y = -50.0
    while y < 90.0:
        w = rnd.uniform(10.0, 20.0)
        top = rnd.uniform(8.0, 34.0)
        building(-55.0, -36.0, y, y + w, top, rnd.choice(facades))
        y += w + rnd.uniform(2.0, 8.0)
    # far silhouettes (no texture)
    y = -80.0
    while y < 120.0:
        w = rnd.uniform(12.0, 26.0)
        top = rnd.uniform(18.0, 60.0)
        far.box(-150.0, -110.0, y, y + w, street, top, "M_RmDistant")
        y += w + rnd.uniform(3.0, 12.0)
    o = mb.obj("Blocks", coll, r)
    f = far.obj("Distant", coll, r)
    return r


# ================================================================================================ UVs + export

def box_uv(obj):
    """Box-projected UVs in metres from world space (props are built at the origin)."""
    me = obj.data
    mw = obj.matrix_world
    rot = mw.to_3x3()
    bm = bmesh.new()
    bm.from_mesh(me)
    uv = bm.loops.layers.uv.verify()
    for f in bm.faces:
        n = rot @ f.normal
        ax = max(range(3), key=lambda i: abs(n[i]))
        for lp in f.loops:
            co = mw @ lp.vert.co
            if ax == 2:
                lp[uv].uv = (co.x, co.y)
            elif ax == 0:
                lp[uv].uv = (co.y, co.z)
            else:
                lp[uv].uv = (co.x, co.z)
    bm.to_mesh(me)
    bm.free()


def uv_all(root):
    bpy.context.view_layer.update()
    stack = [root]
    while stack:
        o = stack.pop()
        stack.extend(o.children)
        if o.type == "MESH" and map_kit.base_name(o) not in rp.OWN_UV:
            box_uv(o)


def flatten(root):
    """Bake rotation/scale of every node below the root into its data (top-down), keeping world positions, so every
    exported node has an identity rotation. The FBX exporter's bake_space_transform mis-converts rotated child nodes
    (a merged body inherits the transform of its first joined part: the hands' arms stood upright, the phone handset
    stood up, the monitor's normals pointed the wrong way)."""
    bpy.context.view_layer.update()
    order, queue = [], [root]
    while queue:
        o = queue.pop(0)
        order.append(o)
        queue.extend(o.children)
    def world_pts(o):
        mw = o.matrix_world
        return [mw @ v.co for v in o.data.vertices] or [mw.to_translation()]
    before = {}
    for o in order:
        if o.type == "MESH":
            before[o.name] = world_pts(o)
    for o in order:
        if o is root:
            continue
        rot_ok = all(abs(a) < 1e-6 for a in o.rotation_euler) and o.rotation_mode in ("XYZ", "XZY", "YXZ", "YZX", "ZXY", "ZYX")
        scale_ok = all(abs(a - 1.0) < 1e-6 for a in o.scale)
        if rot_ok and scale_ok:
            continue
        with bpy.context.temp_override(active_object=o, object=o, selected_objects=[o], selected_editable_objects=[o]):
            bpy.ops.object.transform_apply(location=False, rotation=True, scale=True, properties=False)
        bpy.context.view_layer.update()
    bad = 0
    for o in order:
        if o.type == "MESH" and o.name in before:
            now = world_pts(o)
            lo0 = Vector([min(p[i] for p in before[o.name]) for i in range(3)])
            hi0 = Vector([max(p[i] for p in before[o.name]) for i in range(3)])
            lo1 = Vector([min(p[i] for p in now) for i in range(3)])
            hi1 = Vector([max(p[i] for p in now) for i in range(3)])
            if (lo0 - lo1).length > 0.002 or (hi0 - hi1).length > 0.002:
                bad += 1
                print("FLATTEN MOVED", root.name, o.name)
    return bad


def hoist(root):
    """Keep every exported node a direct child of the prop root. The FBX exporter (bake_space_transform, which the
    Unity side relies on for identity node rotations) mis-converts nodes nested under another node: a mesh under the
    driven empty 'Handset' came out rotated 90 degrees. Driven empties become their own body mesh with the origin at
    the pivot (same name); other nested nodes are re-parented to the root, keeping their world transform."""
    bpy.context.view_layer.update()
    for e in [o for o in hierarchy(root) if o is not root and o.type == "EMPTY" and map_kit.base_name(o) in map_kit.DRIVEN]:
        bodies = [c for c in e.children if c.type == "MESH"]
        if not bodies:
            continue
        body = bodies[0]
        pivot = e.matrix_world.to_translation()
        others = [c for c in e.children if c is not body]
        keep = {c: c.matrix_world.copy() for c in others}
        mw = body.matrix_world.copy()
        body.parent = None
        body.matrix_world = mw
        body.data.transform(Matrix.Translation(mw.to_translation() - pivot) @ mw.to_3x3().to_4x4())
        body.matrix_world = Matrix.Translation(pivot)
        name = e.name
        e.name = name + "__hoisted"
        body.name = name
        body.data.name = name
        for c, m in keep.items():
            c.parent = root
            c.matrix_world = m
        body.parent = root
        body.matrix_world = Matrix.Translation(pivot)
        bpy.data.objects.remove(e)
    bpy.context.view_layer.update()
    for o in hierarchy(root):
        if o is not root and o.parent is not root:
            m = o.matrix_world.copy()
            o.parent = root
            o.matrix_world = m
    bpy.context.view_layer.update()


def export_room(r, path):
    """FBX export with exact driven names (like map_kit.export_prop) but WITHOUT bake_space_transform: the exporter
    mis-converts meshes nested under empties (root > Handset > HandsetBody came out rotated 90 degrees). Unity's
    importer converts every node instead (ModelImporter.bakeAxisConversion, set by ProjectBuilder.ConfigureModelImports)."""
    swaps = []
    for o in hierarchy(r):
        b = map_kit.base_name(o)
        if b != o.name and b in map_kit.DRIVEN:
            holder = bpy.data.objects.get(b)
            if holder is not None:
                holder.name = b + "__swap"
                swaps.append((holder, b))
            old = o.name
            o.name = b
            swaps.append((o, old))
    try:
        os.makedirs(os.path.dirname(path), exist_ok=True)
        bk.select_hierarchy(r)
        bpy.ops.export_scene.fbx(
            filepath=path, use_selection=True, object_types={"EMPTY", "MESH"}, use_mesh_modifiers=True,
            mesh_smooth_type="FACE", use_tspace=False, apply_unit_scale=True, apply_scale_options="FBX_SCALE_ALL",
            axis_forward="-Z", axis_up="Y", add_leaf_bones=False, bake_space_transform=False, bake_anim=False,
            path_mode="STRIP")
    finally:
        for obj, name in reversed(swaps):
            obj.name = name


def hierarchy(root):
    out, stack = [], [root]
    while stack:
        o = stack.pop()
        out.append(o)
        stack.extend(o.children)
    return out


def load_palettes():
    pal = bk.palette()["materials"]
    for f in ("palette_characters.json", "palette_rooms.json"):
        with open(os.path.join(bk.ROOT, "Art", f), encoding="utf-8") as fh:
            pal.update(json.load(fh)["materials"])


def write_layout():
    def props(lst):
        out = []
        for pid, model, p, yaw in lst:
            if model is None:
                continue
            out.append({"id": pid, "model": model, "p": list(p), "r": yaw})
        return out
    data = {
        "_comment": "Generated by Art/Scripts/build_rooms.py (Unity coordinates, yaw degrees). Read by Editor/ProjectBuilder.Rooms.cs.",
        "office": {"room": OFFICE, "props": props(OFFICE_PROPS), "anchors": OFFICE_ANCHORS, "walk": walk_layout(), "decals": OFFICE_DECALS,
                   "lights": OFFICE_LIGHTS},
        "meeting": {"origin": list(MEET_ORIGIN), "room": MEETING, "props": props(MEETING_PROPS), "seats": SEATS,
                    "anchors": MEETING_ANCHORS, "decals": MEETING_DECALS, "lights": MEETING_LIGHTS},
    }
    os.makedirs(os.path.dirname(LAYOUT_PATH), exist_ok=True)
    with open(LAYOUT_PATH, "w", encoding="utf-8") as f:
        json.dump(data, f, indent=1)


def build_all():
    bk.reset_scene()
    load_palettes()
    map_kit.DRIVEN.update(rp.DRIVEN_ROOMS)
    coll = bk.collection("Rooms")
    roots = []
    for fn in rp.ROOM_PROPS:
        sub = bk.collection(fn.__name__, coll)
        r = fn(sub)
        if "." in r.name:
            want = r.name.split(".")[0]
            other = bpy.data.objects.get(want)
            if other is not None:
                other.name = want + "Part"
            r.name = want
        roots.append(r)
    sh_c = bk.collection("Shells", coll)
    oc = bk.collection("RmOfficeShell", sh_c)
    roots.append(shell("RmOfficeShell", oc, OFFICE, {"bands": office_bands, "floor": "M_RmFloorOak", "blind": 0.42}, office_details))
    mc = bk.collection("RmMeetingShell", sh_c)
    roots.append(shell("RmMeetingShell", mc, MEETING, {"bands": meeting_bands, "floor": "M_RmCarpetNavy", "blind": 0.35}, meeting_details))
    sc = bk.collection("RmSkyline", sh_c)
    roots.append(skyline(sc))
    return roots


def main():
    room_textures.main()
    roots = build_all()
    os.makedirs(OUT_MODELS, exist_ok=True)
    exp = os.path.join(bk.EXPORT_DIR, "Rooms")
    os.makedirs(exp, exist_ok=True)
    stats = []
    for r in roots:
        if not r.name.endswith("Shell") and r.name != "RmSkyline":
            map_kit.merge_prop(r)
        flatten(r)
        hoist(r)
        uv_all(r)
        stats.append((r.name, map_kit.tri_count(r)))
    for r in roots:
        path = os.path.join(OUT_MODELS, r.name + ".fbx")
        map_kit.export_prop(r, path)
        shutil.copy2(path, os.path.join(exp, r.name + ".fbx"))
    write_layout()
    os.makedirs(PREV_DIR, exist_ok=True)
    with open(os.path.join(PREV_DIR, "tris.txt"), "w", encoding="utf-8") as f:
        for n, t in sorted(stats, key=lambda x: -x[1]):
            f.write("%-22s %7d\n" % (n, t))
    bk.save_blend("rooms.blend")
    if "--previews" in ARGS:
        import room_preview
        room_preview.render_all(roots, fast="--fast" in ARGS)
    total = sum(t for _, t in stats)
    print("ROOMS_DONE models", len(roots), "tris", total)


if __name__ == "__main__":
    main()
