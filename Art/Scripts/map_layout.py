"""Office v2 layout: the single source of truth for architecture + set dressing.

Gameplay positions (desks, stations, slots, rooms, cameras, manager route, meeting seats) come from the C# contract
`DefaultOffice.Create()` (Game/Assets/Game/Core/Contracts/OfficeLayout.cs), parsed here so the dressing follows the
rules layout. This script writes `Art/Layout/office_v2.json`, which is consumed by:
  - build_architecture.py  -> the OfficeShell FBX (floors, walls, glass, doors, windows, rugs)
  - map_preview.py         -> whole-map Blender renders (no Unity needed)
  - ProjectBuilder.Scene.cs -> props, lights, decal spots and signs in the Unity scene

Run standalone (plain Python 3):  python -I Art/Scripts/map_layout.py
Re-run it whenever DefaultOffice positions change (the Unity builder warns when the JSON is stale).

Coordinates are Unity metres: x right, y up, z forward (north). Yaw in degrees, clockwise seen from above.
Props: the usable front faces local -Z, so a prop whose front must face north (+Z) has yaw 180, south 0, east 270,
west 90 (see FACE). Wall-mounted props have their pivot at the back centre at the given height.
"""
import json
import math
import os
import re
import sys

sys.dont_write_bytecode = True
HERE = os.path.dirname(os.path.abspath(__file__))
ROOT = os.path.abspath(os.path.join(HERE, "..", ".."))
CONTRACTS = os.path.join(ROOT, "Game", "Assets", "Game", "Core", "Contracts")
OUT = os.path.join(ROOT, "Art", "Layout", "office_v2.json")

FACE = {"S": 0.0, "N": 180.0, "E": 270.0, "W": 90.0}
WALL_H = 2.8
T_IN = 0.14
T_OUT = 0.3
DOOR_H = 2.3
PARTITION_H = 1.15


# ================================================================================================ contract parser

def _num(s):
    return float(s.strip().rstrip("f"))


def _vec(s):
    return [_num(x) for x in s.split(",")]


def parse_office():
    """Parse DefaultOffice.Create() into plain data. Robust to formatting, strict about what it needs."""
    src = open(os.path.join(CONTRACTS, "OfficeLayout.cs"), encoding="utf-8-sig").read()
    ids_src = open(os.path.join(CONTRACTS, "Ids.cs"), encoding="utf-8-sig").read()
    consts = dict(re.findall(r'public const string (\w+) = "([^"]+)"', ids_src))
    o = {}
    m = re.search(r"DeskPositions\s*=\s*\{(.*?)\};", src, re.S)
    o["desks"] = [_vec(v) for v in re.findall(r"new Vec3\(([^)]*)\)", m.group(1))]
    o["offsets"] = {k: _vec(v) for k, v in re.findall(r"public static readonly Vec3 (\w+)Offset = new Vec3\(([^)]*)\)", src)}
    o["rooms"] = {n: [_num(a), _num(b), _num(c), _num(d)] for n, a, b, c, d in
                  re.findall(r'Room\(l, "(\w+)", ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), ([-\d.f]+)\)', src)}
    o["stations"] = {}
    for name, kind, pos, yaw in re.findall(r"Stations\.Add\(new StationDef \{ Id = EntityIds\.(\w+), Kind = StationKind\.(\w+), Position = new Vec3\(([^)]*)\), Yaw = ([-\d.f]+)", src):
        o["stations"][consts[name]] = {"kind": kind, "p": _vec(pos), "yaw": _num(yaw)}
    o["slots"] = {}
    for name, kind, pos, yaw in re.findall(r"Slots\.Add\(new SlotDef \{ Id = EntityIds\.(\w+), Kind = SlotKind\.(\w+), Position = new Vec3\(([^)]*)\), Yaw = ([-\d.f]+)", src):
        o["slots"][consts[name]] = {"kind": kind, "p": _vec(pos), "yaw": _num(yaw)}
    o["managerHome"] = _vec(re.search(r"ManagerHome = new Vec3\(([^)]*)\)", src).group(1))
    o["managerHomeYaw"] = _num(re.search(r"ManagerHomeYaw = ([-\d.f]+)", src).group(1))
    o["route"] = []
    for pos, yaw, desk in re.findall(r"ManagerRoute\.Add\(new ManagerStop \{ Position = new Vec3\(([^)]*)\), Yaw = ([-\d.f]+)(?:, DeskIndex = (\d+))?", src):
        o["route"].append({"p": _vec(pos), "yaw": _num(yaw), "desk": int(desk) if desk else -1})
    seat_x = [_num(v) for v in re.search(r"float\[\] seatX = \{([^}]*)\}", src).group(1).split(",")]
    o["seats"] = []
    for z, yaw in re.findall(r"Position = new Vec3\(seatX\[i\], 0f, ([-\d.f]+)\), Yaw = ([-\d.f]+)", src):
        for x in seat_x:
            o["seats"].append({"p": [x, 0.0, _num(z)], "yaw": _num(yaw)})
    o["meetingSpot"] = _vec(re.search(r"ManagerMeetingSpot = new Vec3\(([^)]*)\)", src).group(1))
    o["cameras"] = []
    for name, pos, yaw, x0, z0, x1, z1, room in re.findall(r'Cam\(l, "(\w+)", new Vec3\(([^)]*)\), ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), ([-\d.f]+), "(\w+)"\)', src):
        o["cameras"].append({"id": "cam." + name, "p": _vec(pos), "yaw": _num(yaw), "zone": [_num(x0), _num(z0), _num(x1), _num(z1)], "room": room})
    assert len(o["desks"]) == 6 and len(o["stations"]) >= 7 and len(o["slots"]) >= 10 and o["route"] and o["cameras"], "DefaultOffice parse failed"
    return o


# ================================================================================================ layout builder

class Layout:
    def __init__(self, office):
        self.o = office
        self.props, self.lights, self.decals, self.labels = [], [], [], []
        self.walls, self.floors, self.rugs = [], [], []

    # ---- helpers
    def st(self, sid):
        return self.o["stations"][sid]

    def sl(self, sid):
        return self.o["slots"][sid]

    def prop(self, m, p, r=0.0, c=True, s=True, layer=None, id=None, anchors=None, role=None, room=None, scale=None, tint=None):
        e = {"m": m, "p": [round(float(v), 4) for v in p], "r": round(float(r) % 360.0, 3)}
        if not c:
            e["c"] = False
        if not s:
            e["s"] = False
        if layer:
            e["layer"] = layer
        if id:
            e["id"] = id
        if anchors:
            e["anchors"] = anchors
        if role:
            e["role"] = role
        if room:
            e["room"] = room
        if scale:
            e["scale"] = scale
        if tint:
            e["tint"] = tint
        self.props.append(e)
        return e

    def light(self, p, color, intensity, rng, room, group="room", kind="point", angle=None, yaw=0.0, pitch=90.0):
        e = {"t": kind, "p": list(p), "color": color, "i": intensity, "range": rng, "room": room, "group": group}
        if kind == "spot":
            e["angle"] = angle or 100.0
            e["yaw"] = yaw
            e["pitch"] = pitch
        self.lights.append(e)

    def decal(self, did, p, r, size, room):
        self.decals.append({"id": did, "p": list(p), "r": r, "size": list(size), "room": room})

    def label(self, text, p, r, size, color="#2E2257", font="title", plate=None):
        e = {"text": text, "p": list(p), "r": r, "size": list(size), "color": color, "font": font}
        if plate:
            e["plate"] = plate
        self.labels.append(e)

    def wall(self, a, b, kind="wall", openings=None, h=WALL_H, t=T_IN):
        self.walls.append({"a": list(a), "b": list(b), "kind": kind, "h": h, "t": t, "open": openings or []})

    def to_json(self):
        return {
            "version": 2,
            "generator": "Art/Scripts/map_layout.py",
            "wallHeight": WALL_H,
            "partitionHeight": PARTITION_H,
            "source": {
                "stations": {k: v["p"] for k, v in self.o["stations"].items()},
                "slots": {k: v["p"] for k, v in self.o["slots"].items()},
                "desks": self.o["desks"],
                "nameplateOffset": self.o["offsets"].get("Nameplate"),
            },
            "office": self.o,
            "rooms": ROOM_STYLE,
            "floors": self.floors,
            "rugs": self.rugs,
            "walls": self.walls,
            "desk": DESK_KIT(self.o),
            "props": self.props,
            "lights": self.lights,
            "decals": self.decals,
            "labels": self.labels,
        }


# Paint and floor per room. lowerH = wainscot height (0 = one colour), rail = chair rail material.
ROOM_STYLE = {
    "room.office":    {"upper": "M_PaintPaper", "lower": "M_PaintOfficeGreen", "rail": "M_RailGreen", "lowerH": 1.0, "base": "M_RailGreen",
                       "floor": {"pattern": "checker", "mats": ["M_CarpetA", "M_CarpetB"], "tile": 1.0}},
    "room.reception": {"upper": "M_PaintPaper", "lower": "M_RailGreen", "rail": "M_DeskWood", "lowerH": 1.0, "base": "M_Baseboard",
                       "floor": {"pattern": "checker", "mats": ["M_StoneA", "M_StoneB"], "tile": 1.0}},
    "room.manager":   {"upper": "M_PaintNavy", "lower": "M_DarkWood", "rail": "M_Gold", "lowerH": 1.05, "base": "M_DarkWood",
                       "floor": {"pattern": "planks", "mats": ["M_WoodDarkA", "M_WoodDarkB"], "tile": 0.2}},
    "room.meeting":   {"upper": "M_PaintPaleGreen", "lower": "M_PaintSlate", "rail": "M_DeskWood", "lowerH": 1.0, "base": "M_PaintSlate",
                       "floor": {"pattern": "planks", "mats": ["M_WoodMidA", "M_WoodMidB"], "tile": 0.22}},
    "room.kitchen":   {"upper": "M_PaintMintWhite", "lower": "M_TileMint", "rail": "M_PlasticWhite", "lowerH": 1.3, "base": "M_PlasticWhite",
                       "floor": {"pattern": "checker", "mats": ["M_KitchenTileW", "M_KitchenTileG"], "tile": 0.5}},
    "room.copy":      {"upper": "M_PaintManila", "lower": "M_PaintMustard", "rail": "M_DarkWood", "lowerH": 0.9, "base": "M_DarkWood",
                       "floor": {"pattern": "checker", "mats": ["M_Vinyl", "M_StoneB"], "tile": 0.5}},
    "room.storage":   {"upper": "M_PaintGrey", "lower": "M_PaintGreyDark", "rail": None, "lowerH": 1.0, "base": "M_Baseboard",
                       "floor": {"pattern": "checker", "mats": ["M_Concrete", "M_Concrete"], "tile": 2.0}},
    "room.restroom":  {"upper": "M_PaintBathWhite", "lower": "M_TileBathBlue", "rail": "M_PlasticWhite", "lowerH": 1.4, "base": "M_TileBathBlue",
                       "floor": {"pattern": "checker", "mats": ["M_BathTileA", "M_BathTileB"], "tile": 0.4}},
    "room.server":    {"upper": "M_PaintServer", "lower": "M_PaintServerDark", "rail": None, "lowerH": 1.0, "base": "M_Baseboard",
                       "floor": {"pattern": "grid", "mats": ["M_ServerFloor", "M_ServerGrid"], "tile": 0.6}},
    "room.lounge":    {"upper": "M_PaintPeach", "lower": "M_PaintOrange", "rail": "M_DeskWood", "lowerH": 0.95, "base": "M_DarkWood",
                       "floor": {"pattern": "planks", "mats": ["M_WoodLightA", "M_WoodLightB"], "tile": 0.22}},
    "outside":        {"upper": "M_Exterior", "lower": "M_Exterior", "rail": None, "lowerH": 0.0, "base": None},
    "stall":          {"upper": "M_PaintSlate", "lower": "M_PaintSlate", "rail": None, "lowerH": 0.0, "base": None},
}


def DESK_KIT(o):
    """Per-desk furniture relative to the desk root (desk yaw applies). Roles are wired by the Unity builder."""
    pc = o["offsets"]["Computer"]
    seat = o["offsets"]["Seat"]
    npl = o["offsets"]["Nameplate"]
    hp = PARTITION_H
    kit = {
        "model": "Desk",
        "parts": [
            {"m": "Computer", "p": pc, "r": 0, "role": "pc"},
            {"m": "OfficeChair", "p": seat, "r": 0, "role": "chair"},
            {"m": "CubicleBack", "p": [0, 0, 0.5], "r": 0, "role": "partition"},
            {"m": "CubicleSide", "p": [-1.0, 0, -0.31], "r": 90, "role": "partition"},
            {"m": "CubicleSide", "p": [1.0, 0, -0.31], "r": 90, "role": "partition"},
            {"m": "Wastebasket", "p": [-0.62, 0, -0.05], "r": 0, "role": "decor"},
            {"m": "PcTower", "p": [-0.55, 0, 0.18], "r": 0, "role": "decor"},
            {"m": "UnpluggedCable", "p": [-0.45, 0, -0.55], "r": 20, "role": "cable"},
            {"m": "DeskDirt", "p": [0.0, 0.762, -0.05], "r": 0, "role": "dirt"},
            {"m": "CoffeeSpill", "p": [-0.1, 0.765, 0.05], "r": 0, "role": "spill"},
            {"m": "EmptyDeskCard", "p": [0.1, 0.76, -0.2], "r": 0, "role": "empty"},
        ],
        # Personal clutter per desk index (shown only while the desk is assigned).
        "personal": [
            [{"m": "Cactus", "p": [0.78, 0.76, 0.3], "r": 0}, {"m": "PhotoFrame", "p": [0.55, 0.76, 0.33], "r": 200},
             {"m": "StickyNotes", "p": [-0.25, 0.76, 0.1], "r": 0}, {"m": "PencilCup", "p": [-0.78, 0.76, 0.28], "r": 0}],
            [{"m": "DeskLamp", "p": [0.75, 0.76, 0.28], "r": 200}, {"m": "FolderStack", "p": [-0.75, 0.76, -0.2], "r": 10},
             {"m": "SmallPlant", "p": [0.5, 0.76, 0.32], "r": 0}],
            [{"m": "SmallPlant", "p": [0.78, 0.76, 0.3], "r": 0}, {"m": "Figurine", "p": [0.55, 0.76, 0.32], "r": 160},
             {"m": "StickyNotes", "p": [-0.25, 0.76, 0.1], "r": 0}, {"m": "Headphones", "p": [-0.78, 0.76, -0.22], "r": 30}],
            [{"m": "PhotoFrame", "p": [0.78, 0.76, 0.3], "r": 190}, {"m": "PencilCup", "p": [0.55, 0.76, 0.33], "r": 0},
             {"m": "FolderStack", "p": [-0.75, 0.76, -0.2], "r": -8}],
            [{"m": "Cactus", "p": [0.55, 0.76, 0.32], "r": 0}, {"m": "DeskLamp", "p": [0.78, 0.76, 0.28], "r": 200},
             {"m": "Headphones", "p": [-0.78, 0.76, -0.22], "r": -20}, {"m": "StickyNotes", "p": [-0.25, 0.76, 0.1], "r": 0}],
            [{"m": "MiniTrophy", "p": [0.78, 0.76, 0.3], "r": 180}, {"m": "SmallPlant", "p": [0.55, 0.76, 0.33], "r": 0},
             {"m": "FolderStack", "p": [-0.75, 0.76, -0.2], "r": 4}],
        ],
        "nameplateHolder": {"m": "NameplateRail", "p": [npl[0], hp, npl[2]], "r": 0},
        "chairPushedIn": [0, 0, -0.55],
    }
    return kit


def build(o):
    L = Layout(o)
    rooms = o["rooms"]

    # -------------------------------------------------------------------------------- floors & rugs
    for name, rect in rooms.items():
        st = ROOM_STYLE["room." + name]["floor"]
        L.floors.append({"room": "room." + name, "rect": rect, **st})
    L.rugs += [
        {"rect": [-15.0, 4.9, -11.0, 7.6], "mat": "M_RugRed", "border": "M_Gold"},           # manager
        {"rect": [4.6, -8.7, 8.0, -6.2], "mat": "M_RugMustard", "border": "M_RugCream"},    # lounge
        {"rect": [13.2, -9.2, 15.6, -6.3], "mat": "M_RugTeal", "border": "M_RugCream"},     # lounge games corner
        {"rect": [14.9, -3.2, 15.95, -1.8], "mat": "M_RailGreen", "border": None},           # entrance mat
        {"rect": [-5.6, 5.9, -0.4, 8.5], "mat": "M_RugTeal", "border": "M_RugCream"},       # meeting
    ]

    # -------------------------------------------------------------------------------- walls
    X0, X1, Z0, Z1 = -16.0, 16.0, -10.0, 10.0
    h = T_OUT / 2
    win = lambda c, w, sill=0.9, top=2.35: {"c": c, "w": w, "t": "window", "sill": sill, "top": top}
    door = lambda c, w=1.6: {"c": c, "w": w, "t": "door"}
    # Outer walls (inner face on the room boundary)
    L.wall((X0 - T_OUT, Z1 + h), (X1 + T_OUT, Z1 + h), "outer", [win(-14.0, 2.0), win(-10.6, 2.0), win(-4.9, 2.2), win(-1.1, 2.2),
                                                             win(8.15, 1.3, 1.1), win(11.5, 1.4)], t=T_OUT)
    L.wall((X0 - T_OUT, Z0 - h), (X1 + T_OUT, Z0 - h), "outer", [win(-5.85, 0.8, 1.6), win(6.0, 2.4), win(10.0, 2.0), win(14.2, 2.0)], t=T_OUT)
    L.wall((X0 - h, Z0), (X0 - h, Z1), "outer", [win(9.0, 1.4), win(4.8, 1.2), win(2.4, 2.0), win(-1.0, 2.4), win(-3.9, 1.4)], t=T_OUT)
    L.wall((X1 + h, Z0), (X1 + h, Z1), "outer", [win(3.0, 1.4), {"c": -2.5, "w": 2.0, "t": "entrance"}, win(-4.3, 0.9), win(-7.5, 2.4)], t=T_OUT)
    ti = T_IN / 2
    # North row / open office boundary (z = 4)
    L.wall((X0, 4.0), (-8.0, 4.0), "glass", [{"c": -10.0, "w": 1.6, "t": "glassdoor"}])
    L.wall((-8.0, 4.0), (2.0, 4.0), "wall", [door(-4.5)])
    L.wall((2.0, 4.0), (10.0, 4.0), "wall", [door(4.0, 1.8)])
    L.wall((10.0, 4.0), (X1, 4.0), "wall", [door(12.0)])
    # North row dividers
    L.wall((-8.0, 4.0 + ti), (-8.0, Z1), "glass", [])
    L.wall((2.0, 4.0 + ti), (2.0, Z1), "wall", [])
    L.wall((10.0, 4.0 + ti), (10.0, Z1), "wall", [])
    # Office | reception: low counter wall with two openings
    L.wall((6.0, Z0 + 5.0 + ti), (6.0, 4.0 - ti), "low", [{"c": -0.6, "w": 2.0, "t": "gap"}, {"c": 3.1, "w": 1.8, "t": "gap"}], h=1.1, t=0.18)
    # South row boundary (z = -5)
    L.wall((X0, -5.0), (-10.0, -5.0), "wall", [door(-13.0)])
    L.wall((-10.0, -5.0), (-5.0, -5.0), "wall", [door(-6.2)])
    L.wall((-5.0, -5.0), (3.0, -5.0), "wall", [door(1.6)])
    L.wall((3.0, -5.0), (6.0, -5.0), "wall", [{"c": 4.5, "w": 2.4, "t": "opening"}])
    L.wall((6.0, -5.0), (X1, -5.0), "wall", [door(9.0, 1.8)])
    # South row dividers
    for x in (-10.0, -5.0, 3.0):
        L.wall((x, Z0), (x, -5.0 - ti), "wall", [])
    # Restroom stalls (opaque, 2 m)
    L.wall((-8.3, Z0), (-8.3, -8.4), "stall", [], h=2.0, t=0.05)
    L.wall((-6.7, Z0), (-6.7, -8.4), "stall", [], h=2.0, t=0.05)
    L.wall((-10.0 + ti, -8.4), (-6.7, -8.4), "stall", [{"c": -9.15, "w": 0.95, "t": "gap"}, {"c": -7.5, "w": 0.95, "t": "gap"}], h=2.0, t=0.05)

    # -------------------------------------------------------------------------------- rooms
    office_room(L)
    manager_room(L)
    meeting_room(L)
    kitchen_room(L)
    copy_room(L)
    reception_room(L)
    storage_room(L)
    restroom_room(L)
    server_room(L)
    lounge_room(L)
    cameras_and_safety(L)
    return L


# ================================================================================================ rooms

def office_room(L):
    o = L.o
    # Water cooler, notice board, clock, plants, filing, paper clutter (manager route lines stay clear)
    L.prop("WaterCooler", (1.9, 0, 3.6), FACE["S"], room="room.office")
    L.prop("Corkboard", (-1.3, 0, 3.93), FACE["S"], c=False, room="room.office", role="noticeboard")
    L.prop("WallClock", (-7.0, 2.25, 3.93), FACE["S"], c=False, room="room.office")
    L.prop("BigPlant", (5.45, 0, 3.45), 20, room="room.office")
    L.prop("BigPlant", (-15.45, 0, 3.5), 70, room="room.office")
    L.prop("BigPlant", (-15.45, 0, -4.45), 140, room="room.office")
    L.prop("FileCabinet", (-15.65, 0, -2.6), FACE["E"], room="room.office")
    L.prop("FileCabinet", (-15.65, 0, -2.0), FACE["E"], room="room.office")
    L.prop("PaperBoxStack", (5.45, 0, -4.4), 15, room="room.office")
    L.prop("PaperBoxStack", (2.9, 0, -4.55), -10, room="room.office")
    L.prop("CoatRack", (5.6, 0, -3.3), 0, room="room.office")
    # huddle corner in the east hub (manager route at x = 0.8 stays clear)
    L.prop("KitchenTable", (3.6, 0, -2.4), 0, room="room.office", tint={"M_DeskWood": "M_WoodLightA"})
    for dx, dz, yaw in ((-0.75, 0, 90), (0.75, 0, 270), (0, -0.75, 0)):
        L.prop("MeetingChair", (3.6 + dx, 0, -2.4 + dz), yaw, c=False, room="room.office")
    L.prop("MobileWhiteboard", (4.9, 0, 1.0), 300, room="room.office")
    L.prop("BigPlant", (-1.85, 0, -4.45), 200, room="room.office")
    L.prop("Sofa", (-0.4, 0, -4.48), FACE["N"], room="room.office", tint={"M_Sofa": "M_RailGreen"})
    L.prop("StandingTable", (3.4, 0, 0.4), 0, room="room.office")
    L.prop("SmallPlant", (3.4, 1.08, 0.4), 0, c=False, room="room.office")
    L.prop("Bookshelf", (-10.0, 0, -4.8), FACE["N"], room="room.office")
    L.prop("PrinterSmall", (-4.5, 0, -4.7), FACE["N"], room="room.office")
    # Ceiling-free light sources: floor lamps in the aisles + room lights
    for x in (-12.0, -7.0, -2.0):
        L.light((x, 2.5, -0.4), "#FFE9C8", 1.6, 8.5, "room.office")
    L.light((3.0, 2.5, -0.5), "#FFE9C8", 1.5, 8.0, "room.office")
    L.light((-14.5, 2.4, 3.2), "#FFE4BE", 0.9, 5.5, "room.office")
    L.decal("poster_teamwork", (-6.6, 1.65, 3.92), FACE["S"], (0.6, 0.85), "room.office")
    L.decal("poster_motivation", (-10.6, 1.65, -4.92), FACE["N"], (0.6, 0.85), "room.office")
    L.decal("poster_safety", (-2.6, 1.65, -4.92), FACE["N"], (0.6, 0.85), "room.office")
    L.decal("memo_board", (-1.3, 1.5, 3.9), FACE["S"], (1.05, 0.65), "room.office")
    L.decal("poster_quota", (-15.92, 1.7, 0.6), FACE["E"], (0.7, 0.5), "room.office")


def manager_room(L):
    o = L.o
    hx, _, hz = o["managerHome"]
    # The manager stands in front of his desk glaring out through the glass; the desk is behind him.
    L.prop("ManagerDesk", (hx, 0, hz + 0.85), 180, room="room.manager")
    L.prop("ExecutiveChair", (hx, 0, hz + 1.6), 180, c=False, room="room.manager")
    L.prop("Bookshelf", (-15.82, 0, 6.2), FACE["E"], room="room.manager")
    L.prop("Bookshelf", (-15.82, 0, 7.45), FACE["E"], room="room.manager")
    L.prop("TrophyShelf", (-11.0, 0, 9.78), FACE["S"], room="room.manager")
    L.prop("Globe", (-14.9, 0, 9.3), 0, room="room.manager")
    L.prop("BigPlant", (-15.4, 0, 4.6), 10, room="room.manager")
    L.prop("BigPlant", (-8.7, 0, 9.4), 60, room="room.manager")
    L.prop("LeatherSofa", (-12.0, 0, 4.55), FACE["N"], room="room.manager")
    L.prop("PhotoFrame", (hx + 0.55, 0.79, hz + 0.75), 180, c=False, room="room.manager")
    L.prop("DeskTrophy", (hx - 0.6, 0.79, hz + 0.65), 160, c=False, room="room.manager")
    # Delivery (manager's inbox + coffee tray) against the east glass wall
    d = L.st("st.delivery")
    t = L.sl("slot.delivery")
    L.prop("DeliveryCabinet", (t["p"][0], 0, t["p"][2]), d["yaw"], id="st.delivery", anchors=["slot.delivery"], role="delivery", room="room.manager")
    L.light((-12.0, 2.45, 7.0), "#FFE0B0", 1.7, 7.0, "room.manager")
    L.decal("portrait_manager", (-13.0, 1.75, 9.99), FACE["S"], (0.7, 0.9), "room.manager")
    L.decal("diploma", (-15.99, 1.75, 8.95), FACE["E"], (0.45, 0.34), "room.manager")
    L.label("MÜDÜR", (-10.0, 2.52, 3.86), FACE["S"], (1.3, 0.26), "#D62F3B", plate="M_InkNavy")


def meeting_room(L):
    o = L.o
    L.prop("MeetingTable", (-3.0, 0, 7.2), 0, room="room.meeting")
    for i, s in enumerate(o["seats"]):
        L.prop("MeetingChair", s["p"], s["yaw"], c=False, room="room.meeting", id="seat." + str(i))
    L.prop("ProjectorScreen", (1.93, 0, 7.2), FACE["W"], c=False, room="room.meeting")
    L.prop("Projector", (-1.4, 0.77, 7.2), 270 - 180, c=False, room="room.meeting")
    hc = L.sl("spot.cabinet")
    L.prop("HideCabinet", (hc["p"][0], 0, 9.65), FACE["S"], anchors=["spot.cabinet"], role="hiding", room="room.meeting")
    L.prop("Credenza", (-3.0, 0, 4.35), FACE["N"], room="room.meeting")
    L.prop("WaterBottles", (-3.4, 0.82, 4.35), 0, c=False, room="room.meeting")
    L.prop("BigPlant", (1.45, 0, 9.45), 0, room="room.meeting")
    L.prop("BigPlant", (-7.45, 0, 4.55), 0, room="room.meeting")
    L.prop("Whiteboard", (1.93, 0, 5.0), FACE["W"], c=False, room="room.meeting")
    L.prop("PendantBar", (-3.0, 2.25, 7.2), 0, c=False, room="room.meeting")
    L.light((-3.0, 2.1, 7.2), "#FFE6C4", 1.8, 7.0, "room.meeting")
    L.decal("poster_kpi", (-6.4, 1.65, 4.08), FACE["N"], (0.8, 0.6), "room.meeting")
    L.decal("poster_mission", (-2.95, 1.6, 9.99), FACE["S"], (0.9, 0.65), "room.meeting")
    L.label("TOPLANTI ODASI", (-4.5, 2.52, 3.92), FACE["S"], (1.7, 0.24), "#F1E9D8", plate="M_RailGreen")


def kitchen_room(L):
    cm = L.st("st.coffee")
    mug = L.sl("slot.mugshelf")
    zc = 9.69  # counter centre (0.62 deep against the north wall)
    # Counter run x 2.6..8.6 (1 m modules), sink module under the window, fridge in the NE corner.
    for x in (3.1, 4.1, 5.1, 6.1, 7.1):
        L.prop("CounterModule", (x, 0, zc), FACE["S"], room="room.kitchen")
    L.prop("SinkModule", (8.15, 0, zc), FACE["S"], room="room.kitchen")
    L.prop("Fridge", (9.45, 0, 9.6), FACE["S"], room="room.kitchen")
    L.prop("CoffeeMachine", (cm["p"][0], 0.92, 9.72), cm["yaw"], c=False, id="st.coffee", role="coffee", room="room.kitchen")
    L.prop("MugShelf", (mug["p"][0] + 0.12, 0.92, mug["p"][2]), mug["yaw"], c=False, anchors=["slot.mugshelf"], role="mugshelf", room="room.kitchen")
    L.prop("SaltShaker", (cm["p"][0] - 0.42, 0.92, 9.62), 0, c=False, room="room.kitchen")
    L.prop("SugarJar", (cm["p"][0] - 0.55, 0.92, 9.78), 0, c=False, room="room.kitchen")
    L.prop("Microwave", (3.3, 0.92, 9.72), FACE["S"], c=False, room="room.kitchen")
    L.prop("Kettle", (4.25, 0.92, 9.7), 20, c=False, room="room.kitchen")
    L.prop("FruitBowl", (6.3, 0.92, 9.62), 0, c=False, room="room.kitchen")
    for x in (3.1, 4.1, 5.6, 6.6):
        L.prop("UpperCabinet", (x, 1.55, 9.99), FACE["S"], c=False, room="room.kitchen")
    L.prop("KitchenTable", (7.0, 0, 6.4), 0, room="room.kitchen")
    for i, (dx, dz, yaw) in enumerate(((-0.75, 0, 90), (0.75, 0, 270), (0, 0.75, 180), (0, -0.75, 0))):
        L.prop("Stool", (7.0 + dx, 0, 6.4 + dz), yaw, c=False, room="room.kitchen")
    L.prop("TrashBins", (2.5, 0, 4.5), FACE["E"], room="room.kitchen")
    L.prop("BigPlant", (9.45, 0, 4.55), 0, room="room.kitchen")
    L.prop("WallClock", (6.0, 2.3, 4.08), FACE["N"], c=False, room="room.kitchen")
    L.light((5.5, 2.45, 7.2), "#FFF1D6", 1.7, 7.5, "room.kitchen")
    L.light((5.5, 2.0, 9.3), "#FFF1D6", 0.8, 3.5, "room.kitchen")
    L.decal("poster_coffee", (2.08, 1.7, 7.2), FACE["E"], (0.6, 0.85), "room.kitchen")
    L.decal("sign_salt", (cm["p"][0] - 0.45, 1.25, 9.99), FACE["S"], (0.32, 0.22), "room.kitchen")
    L.decal("poster_fridge_rules", (9.45, 1.35, 9.21), FACE["S"], (0.32, 0.42), "room.kitchen")
    L.label("MUTFAK", (4.0, 2.52, 3.92), FACE["S"], (1.1, 0.24), "#F1E9D8", plate="M_RailGreen")
    L.label("KAHVE", (cm["p"][0], 2.05, 9.98), FACE["S"], (0.8, 0.22), "#5B3A29")


def copy_room(L):
    pr = L.st("st.printer")
    hp = L.sl("spot.printer_back")
    L.prop("Printer", (pr["p"][0], 0, 9.6), pr["yaw"], id="st.printer", anchors=["slot.printer_tray"], role="printer", room="room.copy")
    L.prop("PaperBoxStack", (hp["p"][0], 0, hp["p"][2]), 8, anchors=["spot.printer_back"], role="hiding", room="room.copy")
    L.prop("PaperShelf", (15.75, 0, 8.6), FACE["W"], room="room.copy")
    L.prop("PaperShelf", (15.75, 0, 7.3), FACE["W"], room="room.copy")
    L.prop("PrinterBroken", (11.0, 0, 9.6), FACE["S"], room="room.copy")
    L.prop("Worktable", (10.45, 0, 6.6), FACE["E"], room="room.copy")
    L.prop("PaperCutter", (10.45, 0.9, 6.3), 270, c=False, room="room.copy")
    L.prop("FolderStack", (10.45, 0.9, 7.0), 30, c=False, room="room.copy")
    L.prop("RecycleBin", (15.6, 0, 4.5), FACE["W"], room="room.copy")
    L.prop("Shredder", (14.5, 0, 4.45), FACE["N"], room="room.copy")
    L.prop("SupplyCabinet", (15.72, 0, 5.6), FACE["W"], room="room.copy")
    L.prop("FileCabinet", (10.4, 0, 8.6), FACE["E"], room="room.copy")
    L.prop("FileCabinet", (10.4, 0, 9.15), FACE["E"], room="room.copy")
    L.light((13.0, 2.45, 7.2), "#F4F7FF", 1.6, 7.0, "room.copy")
    L.decal("poster_printer", (10.08, 1.7, 8.6), FACE["E"], (0.6, 0.8), "room.copy")
    L.decal("sign_out_of_order", (11.0, 1.15, 9.27), FACE["S"], (0.36, 0.22), "room.copy")
    L.label("FOTOKOPİ", (12.0, 2.52, 3.92), FACE["S"], (1.2, 0.24), "#F1E9D8", plate="M_RailGreen")


def reception_room(L):
    ph = L.st("st.phone")
    mail = L.sl("slot.mailroom")
    px, _, pz = ph["p"]
    # Reception desk: front counter faces the open office (west); the office phone sits on the counter.
    L.prop("ReceptionDesk", (px + 0.95, 0, pz), FACE["W"], room="room.reception")
    L.prop("DeskPhone", (px + 0.62, 1.07, pz), ph["yaw"], c=False, id="st.phone", role="phone", room="room.reception")
    L.prop("OfficeChair", (px + 1.75, 0, pz + 0.3), 270, c=False, room="room.reception", tint={"M_ChairFabric": "M_RailGreen"})
    L.prop("MailStation", (15.6, 0, mail["p"][2]), mail["yaw"], anchors=["slot.mailroom"], role="mail", room="room.reception")
    L.prop("WaitingBench", (12.6, 0, -4.6), FACE["N"], room="room.reception")
    L.prop("CoffeeTableSmall", (12.6, 0, -3.6), 0, room="room.reception")
    L.prop("Magazines", (12.6, 0.42, -3.6), 15, c=False, room="room.reception")
    L.prop("BigPlant", (15.4, 0, -4.4), 0, room="room.reception")
    L.prop("BigPlant", (6.6, 0, -4.45), 0, room="room.reception")
    L.prop("BigPlant", (15.4, 0, 3.45), 0, room="room.reception")
    L.prop("UmbrellaStand", (15.6, 0, -0.95), 0, room="room.reception")
    L.prop("FishTank", (8.2, 0, 3.6), FACE["S"], room="room.reception")
    L.light((10.5, 2.45, -0.5), "#FFEBD0", 1.7, 7.5, "room.reception")
    L.light((14.0, 2.45, 1.6), "#FFEBD0", 1.2, 6.0, "room.reception")
    L.light((13.0, 2.45, -3.4), "#FFEBD0", 1.0, 5.5, "room.reception")
    L.decal("logo_company", (15.99, 1.75, -0.3), FACE["W"], (1.3, 0.65), "room.reception")
    L.decal("poster_welcome", (9.6, 1.65, 3.92), FACE["S"], (0.7, 0.5), "room.reception")
    L.label("RESEPSİYON", (px + 0.6, 1.25, pz), FACE["W"], (1.2, 0.2), "#F1E9D8")
    L.label("POSTA", (15.98, 2.15, mail["p"][2]), FACE["W"], (0.8, 0.2), "#F1E9D8", plate="M_RailGreen")


def storage_room(L):
    cl = L.sl("slot.sprayrack")
    fb = L.st("st.fusebox")
    lf = L.sl("slot.lostfound")
    # Cleaning closet in the SW corner, diagonal (front faces the user, yaw from the contract).
    cx, _, cz = cl["p"]
    yaw = math.radians(cl["yaw"])
    # The spray rack items sit at local x -0.15/0.15/0.45 of the anchor (= the station point), on the closet's
    # 0.88 m shelf: centre the 0.9 m closet 0.15 m to the anchor's right and 0.1 m behind it.
    fwd = (math.sin(yaw), math.cos(yaw))
    right = (math.cos(yaw), -math.sin(yaw))
    L.prop("CleaningCloset", (cx + fwd[0] * 0.1 + right[0] * 0.15, 0, cz + fwd[1] * 0.1 + right[1] * 0.15), cl["yaw"], id="st.cleaning",
           anchors=["slot.sprayrack"], role="closet", room="room.storage")
    L.prop("FuseBox", (fb["p"][0], 1.25, -9.99), fb["yaw"], c=False, id="st.fusebox", role="fusebox", room="room.storage")
    L.prop("LowPallet", (lf["p"][0], 0, lf["p"][2]), lf["yaw"], room="room.storage")
    L.prop("LostFoundBox", (lf["p"][0], 0.33, lf["p"][2]), lf["yaw"], c=False, anchors=["slot.lostfound"], role="lostfound", room="room.storage")
    L.prop("StorageShelf", (-15.7, 0, -6.4), FACE["E"], room="room.storage")
    L.prop("StorageShelf", (-15.7, 0, -7.75), FACE["E"], room="room.storage")
    L.prop("StorageShelf", (-10.32, 0, -6.6), FACE["W"], room="room.storage")
    L.prop("CardboardStack", (-11.4, 0, -5.6), 12, room="room.storage")
    L.prop("MopBucket", (-10.55, 0, -8.85), 30, room="room.storage")
    L.prop("Ladder", (-10.3, 0, -8.2), FACE["W"], c=False, room="room.storage")
    L.prop("WetFloorSign", (-12.4, 0, -6.3), 200, c=False, room="room.storage")
    L.prop("CardboardStack", (-14.6, 0, -5.55), -20, room="room.storage")
    L.prop("ArchiveBoxes", (-11.0, 0, -7.9), FACE["W"], room="room.storage")
    L.prop("OldChairs", (-14.4, 0, -7.1), 35, room="room.storage")
    L.prop("WaterCooler", (-11.4, 0, -9.55), 0, room="room.storage", tint={"M_Water": "M_PaintGrey"})
    L.light((-13.0, 2.45, -7.4), "#E8F0FF", 1.3, 6.0, "room.storage")
    L.decal("sign_fusebox", (fb["p"][0] + 0.75, 1.55, -9.99), FACE["N"], (0.3, 0.3), "room.storage")
    L.label("DEPO", (-13.0, 2.52, -4.92), FACE["N"], (0.9, 0.24), "#F1E9D8", plate="M_RailGreen")


def restroom_room(L):
    ht = L.sl("spot.toilet")
    tx = ht["p"][0]
    L.prop("Toilet", (tx, 0, -9.75), FACE["N"], anchors=["spot.toilet"], role="hiding", room="room.restroom")
    L.prop("Toilet", (-9.15, 0, -9.75), FACE["N"], room="room.restroom")
    L.prop("StallDoor", (-9.15 + 0.48, 0, -8.4), 0, c=False, room="room.restroom", tint=None)
    L.prop("StallDoor", (tx + 0.48, 0, -8.4), 0, c=False, room="room.restroom")
    L.prop("BathroomSinks", (-9.65, 0, -6.7), FACE["E"], room="room.restroom")
    L.prop("Mirror", (-9.93, 1.55, -6.7), FACE["E"], c=False, room="room.restroom")
    L.prop("HandDryer", (-5.07, 1.2, -7.3), FACE["W"], c=False, room="room.restroom")
    L.prop("Wastebasket", (-5.35, 0, -8.0), 0, room="room.restroom")
    L.prop("SmallPlant", (-9.65, 0.88, -7.55), 0, c=False, room="room.restroom")
    L.light((-7.5, 2.45, -6.8), "#EEF6FF", 1.3, 5.5, "room.restroom")
    L.light((-8.3, 2.3, -9.2), "#EEF6FF", 0.7, 3.0, "room.restroom")
    L.decal("sign_wash_hands", (-5.08, 1.6, -6.3), FACE["W"], (0.3, 0.4), "room.restroom")
    L.label("WC", (-6.2, 2.52, -4.92), FACE["N"], (0.6, 0.26), "#F1E9D8", plate="M_PaintSlate")


def server_room(L):
    ro = L.st("st.router")
    rx = ro["p"][0]
    L.prop("NetworkRack", (rx, 0, -9.62), ro["yaw"], id="st.router", role="router", room="room.server")
    for i, z in enumerate((-9.35, -8.7, -8.05, -7.4, -6.75, -6.1)):
        L.prop("ServerRack", (-4.5, 0, z), FACE["E"], room="room.server")
    for i, z in enumerate((-8.7, -8.05, -7.4, -6.75)):
        L.prop("ServerRack", (-2.6, 0, z), FACE["W"], room="room.server")
    L.prop("CableTray", (-3.55, 2.3, -7.6), 90, c=False, room="room.server")
    L.prop("CableTray", (-1.8, 2.3, -9.4), 0, c=False, room="room.server")
    L.prop("CableMess", (-1.6, 0, -8.6), 30, c=False, room="room.server")
    L.prop("AcUnit", (2.93, 1.9, -7.6), FACE["W"], c=False, room="room.server")
    L.prop("SysadminDesk", (1.9, 0, -9.45), FACE["N"], room="room.server")
    L.prop("OfficeChair", (1.9, 0, -8.75), 180, c=False, room="room.server")
    L.prop("CardboardStack", (2.5, 0, -5.6), 0, room="room.server")
    L.light((-1.0, 2.4, -7.5), "#BFD8FF", 1.0, 6.5, "room.server")
    L.light((rx, 1.6, -9.0), "#7FB8FF", 0.6, 2.5, "room.server")
    L.decal("sign_server_danger", (2.45, 1.6, -4.92), FACE["N"], (0.35, 0.35), "room.office")
    L.label("SUNUCU ODASI", (1.6, 2.52, -4.92), FACE["N"], (1.5, 0.24), "#F1E9D8", plate="M_PaintServerDark")


def lounge_room(L):
    jb = L.sl("slot.junk")
    pl = L.sl("spot.plant")
    L.prop("JunkBin", (jb["p"][0], 0, jb["p"][2]), jb["yaw"], anchors=["slot.junk"], role="junk", room="room.lounge")
    L.prop("Planter", (pl["p"][0], 0, pl["p"][2]), pl["yaw"], anchors=["spot.plant"], role="hiding", room="room.lounge")
    L.prop("VendingMachine", (3.5, 0, -9.3), FACE["E"], room="room.lounge")
    L.prop("VendingMachine", (3.5, 0, -8.15), FACE["E"], room="room.lounge", tint={"M_VendingBody": "M_PlasticBlue"})
    L.prop("Sofa", (6.3, 0, -9.45), FACE["N"], room="room.lounge")
    L.prop("CoffeeTable", (6.3, 0, -7.6), 0, room="room.lounge")
    L.prop("Armchair", (4.75, 0, -7.3), 270 - 180 + 180, room="room.lounge")
    L.prop("Armchair", (7.85, 0, -7.3), 270, room="room.lounge")
    L.prop("FloorLamp", (7.75, 0, -9.55), 0, c=False, room="room.lounge")
    L.prop("Foosball", (10.4, 0, -7.4), 0, room="room.lounge")
    L.prop("BeanBag", (14.0, 0, -8.4), 20, room="room.lounge")
    L.prop("BeanBag", (14.9, 0, -7.2), -30, room="room.lounge", tint={"M_StampRed": "M_CushionTeal"})
    L.prop("WallTv", (14.4, 1.55, -5.08), FACE["S"], c=False, room="room.lounge")
    L.prop("GamesShelf", (11.6, 0, -5.3), FACE["S"], room="room.lounge")
    L.prop("BigPlant", (15.5, 0, -9.5), 0, room="room.lounge")
    L.prop("Dartboard", (6.6, 1.6, -5.08), FACE["S"], c=False, room="room.lounge")
    L.light((6.3, 2.45, -7.6), "#FFDDB0", 1.6, 7.0, "room.lounge")
    L.light((12.6, 2.45, -7.5), "#FFDDB0", 1.5, 7.0, "room.lounge")
    L.decal("poster_relax", (9.4, 1.7, -5.08), FACE["S"], (0.7, 0.5), "room.lounge")
    L.decal("poster_vending", (3.08, 1.85, -6.7), FACE["E"], (0.45, 0.6), "room.lounge")
    L.label("DİNLENME", (4.5, 2.52, -4.92), FACE["N"], (1.2, 0.24), "#F1E9D8", plate="M_RailGreen")


def cameras_and_safety(L):
    for cam in L.o["cameras"]:
        # The camera lens points along its yaw; the prop's front (-Z) must face that way.
        L.prop("SecurityCamera", cam["p"], cam["yaw"] + 180.0, c=False, id=cam["id"], role="camera", room="room." + cam["room"])
    # Exit signs over the main doors, fire extinguishers, emergency lights (red, used during power cuts)
    L.prop("ExitSign", (15.98, 2.45, -2.5), FACE["W"], c=False, room="room.reception")
    L.prop("ExitSign", (4.5, 2.55, -5.08), FACE["S"], c=False, room="room.lounge")
    L.prop("FireExtinguisher", (-15.95, 0.0, -0.1), FACE["E"], c=False, room="room.office")
    L.prop("FireExtinguisher", (2.6, 0.0, -4.93), FACE["N"], c=False, room="room.office")
    L.prop("FireExtinguisher", (2.08, 0.0, 5.0), FACE["E"], c=False, room="room.kitchen")
    em = [("room.office", (-15.93, 2.55, 0.8), FACE["E"]), ("room.office", (-2.0, 2.55, 3.93), FACE["S"]),
          ("room.office", (-7.0, 2.55, -4.93), FACE["N"]), ("room.reception", (11.5, 2.55, 3.93), FACE["S"]),
          ("room.kitchen", (6.0, 2.55, 9.99), FACE["S"]), ("room.lounge", (9.0, 2.55, -5.07), FACE["S"]),
          ("room.storage", (-13.0, 2.55, -5.07), FACE["S"]), ("room.server", (-1.0, 2.55, -5.07), FACE["S"]),
          ("room.manager", (-12.0, 2.55, 9.99), FACE["S"]), ("room.meeting", (-3.0, 2.55, 9.99), FACE["S"]),
          ("room.copy", (13.0, 2.55, 4.07), FACE["N"]), ("room.restroom", (-7.5, 2.55, -5.07), FACE["S"])]
    for room, p, yaw in em:
        L.prop("EmergencyLight", p, yaw, c=False, room=room, role="emergency")
        fwd = (math.sin(math.radians(yaw + 180)), math.cos(math.radians(yaw + 180)))
        L.light((p[0] + fwd[0] * 0.5, 2.35, p[2] + fwd[1] * 0.5), "#FF2A1A", 1.6, 7.0, room, group="emergency")


def main():
    office = parse_office()
    L = build(office)
    data = L.to_json()
    os.makedirs(os.path.dirname(OUT), exist_ok=True)
    with open(OUT, "w", encoding="utf-8", newline="\n") as f:
        json.dump(data, f, ensure_ascii=False, indent=1)
    print("LAYOUT_DONE", OUT, "props", len(L.props), "lights", len(L.lights), "walls", len(L.walls), "decals", len(L.decals))
    return data


if __name__ == "__main__":
    main()
