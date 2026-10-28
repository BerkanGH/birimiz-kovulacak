"""Builds every office prop and interactive item as a separate FBX.

Run:  blender -b --factory-startup -P Art/Scripts/build_props.py [-- --previews] [--each]
Outputs: Art/Blender/props.blend, Game/Assets/Game/Art/Models/Props/<Name>.fbx, Art/Previews/props_*.png

Props live in three modules that share one registry (map_kit.PROPS): this file (desk kit, v1 furniture, items),
map_props_office.py (cubicles, manager office, meeting room, reception, safety) and map_props_rooms.py (kitchen,
copy room, storage, restroom, server room, lounge).

Pivot: bottom centre (wall-mounted props: back centre). Front/usable side faces Blender -Y (Unity -Z).
Before export every prop is merged to one body mesh (one submesh per material) plus the named sub-objects that the
Unity side drives (map_kit.DRIVEN: Drawer, Screen, Coffee, Stripe, Handset, RingLight, StatusLight, Leds, ...).
"""
import math
import os
import random
import sys

sys.dont_write_bytecode = True
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bpy  # noqa: E402
import bk_common as bk  # noqa: E402
import map_kit  # noqa: E402
from map_kit import prop, root, uv_plane, cable  # noqa: E402

R = math.radians
PROPS = map_kit.PROPS


# --------------------------------------------------------------------------------------------- desk kit

@prop
def desk(c):
    r = root("Desk", c)
    bk.box("Top", (1.8, 0.9, 0.04), c, "M_DeskWood", r, (0, 0, 0.74), bevel=0.012)
    bk.box("Edge", (1.8, 0.02, 0.03), c, "M_DarkWood", r, (0, -0.45, 0.735))
    bk.box("LegPanel", (0.05, 0.84, 0.72), c, "M_DeskFrame", r, (-0.86, 0, 0.36), bevel=0.01)
    bk.box("Pedestal", (0.44, 0.8, 0.70), c, "M_DeskFrame", r, (0.6, 0, 0.37), bevel=0.01)
    bk.box("Modesty", (1.2, 0.02, 0.36), c, "M_DeskFrame", r, (-0.22, 0.42, 0.5))
    bk.box("CableTray", (0.9, 0.12, 0.05), c, "M_DarkMetal", r, (-0.3, 0.33, 0.66))
    bk.cylinder("Grommet", 0.03, 0.005, c, "M_PlasticDark", r, (-0.25, 0.36, 0.761), segments=12)
    bk.box("LowerDrawer", (0.40, 0.03, 0.30), c, "M_DeskWood", r, (0.6, -0.405, 0.22), bevel=0.006)
    bk.box("LowerHandle", (0.14, 0.025, 0.02), c, "M_Metal", r, (0.6, -0.43, 0.3))
    drawer = bk.empty("Drawer", c, (0.6, 0, 0.55), r)
    bk.box("DrawerFront", (0.40, 0.03, 0.22), c, "M_DeskWood", drawer, (0, -0.415, 0), bevel=0.006)
    bk.box("DrawerBody", (0.36, 0.6, 0.16), c, "M_DeskFrame", drawer, (0, -0.11, -0.01))
    bk.box("DrawerHandle", (0.14, 0.025, 0.02), c, "M_Metal", drawer, (0, -0.44, 0.02))
    bk.box("DrawerPapers", (0.26, 0.34, 0.03), c, "M_Manila", drawer, (0, -0.1, 0.06))
    return r


@prop
def computer(c):
    """Desk PC. Screen (UV plane) swaps material: desktop / lock screen / password-locked / off / no signal."""
    r = root("Computer", c)
    bk.box("Monitor", (0.6, 0.04, 0.37), c, "M_PlasticDark", r, (0, 0.035, 0.345), bevel=0.012)
    bk.box("MonitorBack", (0.36, 0.06, 0.22), c, "M_PlasticDark", r, (0, 0.075, 0.34), bevel=0.02)
    uv_plane("Screen", 0.55, 0.31, c, "M_ScreenDesktop", r, (0, 0.013, 0.35))
    bk.box("Chin", (0.12, 0.004, 0.012), c, "M_Metal", r, (0, 0.013, 0.177))
    bk.sphere("PcLed", 0.006, c, "M_LEDBlue", r, (0.27, 0.012, 0.172), segments=6, rings=4)
    bk.box("Neck", (0.06, 0.035, 0.2), c, "M_DarkMetal", r, (0, 0.09, 0.11), bevel=0.008)
    bk.box("Foot", (0.24, 0.17, 0.016), c, "M_DarkMetal", r, (0, 0.07, 0.008), bevel=0.006)
    bk.box("Keyboard", (0.44, 0.15, 0.022), c, "M_PlasticDark", r, (0, -0.28, 0.011), bevel=0.006)
    bk.box("Keys", (0.41, 0.12, 0.006), c, "M_PlasticWhite", r, (0, -0.28, 0.024))
    bk.box("Pad", (0.24, 0.2, 0.003), c, "M_InkNavy", r, (0.34, -0.27, 0.0015))
    bk.sphere("Mouse", 0.035, c, "M_PlasticDark", r, (0.34, -0.27, 0.012), scale=(0.75, 1.15, 0.45), segments=10, rings=6)
    cable("Cord", [(0, 0.1, 0.05), (0.05, 0.25, 0.0), (0.2, 0.3, -0.0)], 0.006, c, "M_Cable", r)
    return r


@prop
def office_chair(c):
    """Task chair; the M_ChairFabric parts are recoloured with the desk owner's colour in Unity."""
    r = root("OfficeChair", c)
    bk.box("Seat", (0.5, 0.48, 0.09), c, "M_ChairFabric", r, (0, 0.02, 0.48), bevel=0.035)
    back = bk.box("Back", (0.46, 0.07, 0.56), c, "M_ChairFabric", r, (0, -0.26, 0.84), bevel=0.035)
    back.rotation_euler = (R(-8), 0, 0)
    bk.box("BackShell", (0.42, 0.03, 0.5), c, "M_PlasticDark", r, (0, -0.3, 0.84), bevel=0.012).rotation_euler = (R(-8), 0, 0)
    bk.box("BackBar", (0.05, 0.05, 0.3), c, "M_PlasticDark", r, (0, -0.27, 0.52))
    for sx in (-1, 1):
        bk.box("ArmRest", (0.06, 0.3, 0.04), c, "M_PlasticDark", r, (0.28 * sx, 0.0, 0.67), bevel=0.012)
        bk.box("ArmPost", (0.04, 0.04, 0.17), c, "M_PlasticDark", r, (0.28 * sx, -0.06, 0.58))
    bk.cylinder("Gas", 0.03, 0.34, c, "M_Metal", r, (0, 0, 0.27), segments=12)
    for i in range(5):
        a = 2 * math.pi * i / 5
        leg = bk.box("Leg", (0.3, 0.05, 0.04), c, "M_PlasticDark", r, (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.08))
        leg.rotation_euler = (0, 0, a)
        bk.sphere("Wheel", 0.03, c, "M_PlasticDark", r, (math.cos(a) * 0.29, math.sin(a) * 0.29, 0.03), segments=8, rings=5)
    return r


# --------------------------------------------------------------------------------------------- furniture & items (v1, kept)

@prop
def padlock(c):
    r = root("Padlock", c)
    bk.box("Body", (0.13, 0.05, 0.1), c, "M_Gold", r, (0, 0, 0.05), bevel=0.012)
    bk.torus("Shackle", 0.042, 0.011, c, "M_Metal", r, (0, 0, 0.1), rotation=(R(90), 0, 0), arc=math.pi, major_seg=12, minor_seg=6)
    bk.cylinder("Keyhole", 0.012, 0.01, c, "M_PlasticDark", r, (0, -0.026, 0.05), rotation=(R(90), 0, 0), segments=10)
    return r


@prop
def nameplate(c):
    r = root("Nameplate", c)
    bk.prism_nameplate("Body", 0.34, 0.1, 0.1, c, "M_NameplateDark", r, (0, 0, 0.012))
    bk.box("Stripe", (0.35, 0.11, 0.014), c, "M_NameplateStripe", r, (0, 0, 0.007), bevel=0.003)
    return r


@prop
def manager_desk(c):
    r = root("ManagerDesk", c)
    bk.box("Top", (2.0, 0.95, 0.05), c, "M_DarkWood", r, (0, 0, 0.765), bevel=0.015)
    bk.box("FrontPanel", (1.96, 0.04, 0.72), c, "M_DarkWood", r, (0, 0.44, 0.38), bevel=0.01)
    for sx in (-1, 1):
        bk.box("Side", (0.05, 0.9, 0.74), c, "M_DarkWood", r, (0.96 * sx, 0, 0.37), bevel=0.01)
    bk.box("Monitor", (0.6, 0.035, 0.36), c, "M_PlasticDark", r, (-0.35, 0.25, 1.0), bevel=0.01)
    bk.box("Screen", (0.55, 0.006, 0.31), c, "M_Screen", r, (-0.35, 0.231, 1.005))
    bk.box("Neck", (0.05, 0.03, 0.17), c, "M_PlasticDark", r, (-0.35, 0.28, 0.87))
    bk.box("PaperStack", (0.22, 0.3, 0.06), c, "M_Paper", r, (0.45, 0.0, 0.82))
    bk.box("Sign", (0.3, 0.06, 0.08), c, "M_Gold", r, (0.0, -0.38, 0.83), bevel=0.01)
    lamp_base = bk.cylinder("LampBase", 0.07, 0.02, c, "M_DarkMetal", r, (0.75, 0.25, 0.8))
    bk.cylinder("LampArm", 0.012, 0.35, c, "M_DarkMetal", r, (0.75, 0.25, 0.97), segments=8)
    bk.cylinder("LampShade", 0.09, 0.1, c, "M_PlasticGreen", r, (0.75, 0.18, 1.15), radius_top=0.05, rotation=(R(-30), 0, 0))
    return r


@prop
def executive_chair(c):
    r = root("ExecutiveChair", c)
    bk.box("Seat", (0.6, 0.55, 0.12), c, "M_PlasticDark", r, (0, 0, 0.5), bevel=0.04)
    back = bk.box("Back", (0.58, 0.12, 0.85), c, "M_PlasticDark", r, (0, -0.3, 1.0), bevel=0.05)
    back.rotation_euler = (R(-8), 0, 0)
    for sx in (-1, 1):
        bk.box("Arm", (0.08, 0.45, 0.08), c, "M_PlasticDark", r, (0.32 * sx, -0.02, 0.68), bevel=0.02)
    bk.cylinder("Gas", 0.035, 0.36, c, "M_Metal", r, (0, 0, 0.28), segments=12)
    for i in range(5):
        a = 2 * math.pi * i / 5
        leg = bk.box("Leg", (0.34, 0.06, 0.05), c, "M_Gold", r, (math.cos(a) * 0.17, math.sin(a) * 0.17, 0.08))
        leg.rotation_euler = (0, 0, a)
    return r


@prop
def bookshelf(c):
    r = root("Bookshelf", c)
    rnd = random.Random(7)
    w, d, h = 1.2, 0.35, 1.9
    for sx in (-1, 1):
        bk.box("Side", (0.04, d, h), c, "M_DarkWood", r, (sx * (w / 2 - 0.02), 0, h / 2))
    bk.box("Back", (w, 0.02, h), c, "M_DarkWood", r, (0, d / 2 - 0.01, h / 2))
    colors = ["M_PlasticRed", "M_PlasticBlue", "M_PlasticGreen", "M_Purple", "M_Envelope", "M_PlasticOrange", "M_PlasticWhite"]
    for i in range(5):
        z = 0.02 + i * 0.46
        bk.box("Shelf", (w - 0.06, d - 0.02, 0.03), c, "M_DarkWood", r, (0, 0, z))
        if i == 4:
            continue
        x = -w / 2 + 0.08
        while x < w / 2 - 0.12:
            bw = rnd.uniform(0.035, 0.07)
            bh = rnd.uniform(0.22, 0.34)
            book = bk.box("Book", (bw, 0.24, bh), c, rnd.choice(colors), r, (x + bw / 2, -0.02, z + 0.015 + bh / 2))
            if rnd.random() < 0.12:
                book.rotation_euler = (0, R(12), 0)
            x += bw + 0.006
            if rnd.random() < 0.08:
                x += 0.12
    return r


@prop
def meeting_table(c):
    r = root("MeetingTable", c)
    bk.box("Top", (4.2, 1.5, 0.05), c, "M_WoodMidA", r, (0, 0, 0.745), bevel=0.02)
    bk.box("Inlay", (3.6, 0.7, 0.004), c, "M_InkNavy", r, (0, 0, 0.772))
    for sx in (-1, 1):
        bk.box("Leg", (0.12, 1.0, 0.7), c, "M_DarkMetal", r, (1.5 * sx, 0, 0.36), bevel=0.01)
        bk.box("Foot", (0.16, 1.2, 0.03), c, "M_DarkMetal", r, (1.5 * sx, 0, 0.015))
    bk.box("Beam", (3.0, 0.1, 0.08), c, "M_DarkMetal", r, (0, 0, 0.6))
    bk.cylinder("Speaker", 0.12, 0.03, c, "M_PlasticDark", r, (0.3, 0, 0.785), segments=3)
    for i, x in enumerate((-1.4, 0.0, 1.4)):
        bk.box("Notepad", (0.18, 0.24, 0.01), c, "M_Sticky" if i % 2 else "M_Paper", r, (x + 0.1, -0.5, 0.775)).rotation_euler = (0, 0, R(8 - i * 6))
        bk.box("NotepadB", (0.18, 0.24, 0.01), c, "M_Paper", r, (x - 0.1, 0.5, 0.775)).rotation_euler = (0, 0, R(-6 + i * 5))
    bk.box("Laptop", (0.34, 0.24, 0.015), c, "M_Metal", r, (-1.6, -0.3, 0.78), bevel=0.004)
    lid = bk.box("LaptopLid", (0.34, 0.01, 0.23), c, "M_Metal", r, (-1.6, -0.18, 0.885))
    lid.rotation_euler = (R(-15), 0, 0)
    return r


@prop
def meeting_chair(c):
    r = root("MeetingChair", c)
    bk.box("Seat", (0.46, 0.45, 0.07), c, "M_ChairFabric", r, (0, 0, 0.46), bevel=0.025)
    back = bk.box("Back", (0.44, 0.05, 0.42), c, "M_ChairFabric", r, (0, -0.23, 0.74), bevel=0.02)
    back.rotation_euler = (R(-6), 0, 0)
    for sx in (-1, 1):
        for sy in (-1, 1):
            bk.cylinder("Leg", 0.015, 0.44, c, "M_Metal", r, (0.19 * sx, 0.18 * sy, 0.22), segments=8)
    return r


@prop
def whiteboard(c):
    r = root("Whiteboard", c)
    bk.box("Board", (1.8, 0.03, 1.0), c, "M_Whiteboard", r, (0, 0, 1.45))
    bk.box("FrameTop", (1.86, 0.04, 0.04), c, "M_Metal", r, (0, -0.005, 1.97))
    bk.box("FrameBottom", (1.86, 0.04, 0.04), c, "M_Metal", r, (0, -0.005, 0.93))
    for sx in (-1, 1):
        bk.box("FrameSide", (0.04, 0.04, 1.08), c, "M_Metal", r, (0.93 * sx, -0.005, 1.45))
    bk.box("Tray", (0.8, 0.08, 0.02), c, "M_Metal", r, (0, -0.05, 0.92))
    for i, m in enumerate(("M_PlasticRed", "M_PlasticBlue", "M_PlasticGreen")):
        bk.cylinder("Marker", 0.01, 0.12, c, m, r, (-0.2 + i * 0.1, -0.05, 0.94), rotation=(0, R(90), 0), segments=8)
    return r


@prop
def corkboard(c):
    r = root("Corkboard", c)
    bk.box("Board", (1.2, 0.03, 0.8), c, "M_Cork", r, (0, 0, 1.5))
    for (sx, sz, w, h) in ((0, 0.42, 1.26, 0.04), (0, -0.42, 1.26, 0.04)):
        bk.box("Frame", (w, 0.04, h), c, "M_DarkWood", r, (sx, -0.005, 1.5 + sz))
    for sx in (-1, 1):
        bk.box("Frame", (0.04, 0.04, 0.88), c, "M_DarkWood", r, (0.62 * sx, -0.005, 1.5))
    rnd = random.Random(4)
    for i in range(6):
        m = rnd.choice(["M_Paper", "M_Envelope", "M_PlasticWhite", "M_DuckYellow"])
        note = bk.box("Note", (0.18, 0.005, 0.14), c, m, r, (rnd.uniform(-0.45, 0.45), -0.02, 1.5 + rnd.uniform(-0.25, 0.25)))
        note.rotation_euler = (0, R(rnd.uniform(-10, 10)), 0)
    return r



@prop
def water_cooler(c):
    r = root("WaterCooler", c)
    bk.box("Base", (0.36, 0.36, 1.0), c, "M_PlasticWhite", r, (0, 0, 0.5), bevel=0.02)
    bk.cylinder("Bottle", 0.14, 0.45, c, "M_Water", r, (0, 0, 1.25), segments=20)
    bk.box("TapBlue", (0.04, 0.05, 0.05), c, "M_PlasticBlue", r, (-0.07, -0.19, 0.8))
    bk.box("TapRed", (0.04, 0.05, 0.05), c, "M_PlasticRed", r, (0.07, -0.19, 0.8))
    return r


@prop
def big_plant(c):
    r = root("BigPlant", c)
    bk.lathe("Pot", [(0, 0), (0.22, 0), (0.28, 0.5), (0.25, 0.5), (0.22, 0.05), (0, 0.05)], c, "M_Pot", r, segments=20, smooth=False)
    bk.cylinder("Soil", 0.255, 0.02, c, "M_Soil", r, (0, 0, 0.46), segments=20)
    rnd = random.Random(11)
    for i in range(12):
        a = 2 * math.pi * i / 12 + rnd.uniform(-0.2, 0.2)
        tilt = rnd.uniform(20, 45)
        length = rnd.uniform(0.45, 0.75)
        leaf = bk.sphere("Leaf", 0.1, c, "M_Leaf" if i % 2 else "M_LeafLight", r, (0, 0, 0.5), scale=(0.9, 0.3, length / 0.1 / 2), segments=10, rings=8)
        leaf.data.transform(__import__("mathutils").Matrix.Translation((0, 0, 0.1 * length / 0.1 / 2)))
        leaf.rotation_euler = (R(tilt), 0, a)
    return r


@prop
def small_plant(c):
    r = root("SmallPlant", c)
    bk.cylinder("Pot", 0.07, 0.11, c, "M_Pot", r, (0, 0, 0.055), radius_top=0.08, segments=14)
    for i in range(5):
        a = 2 * math.pi * i / 5
        leaf = bk.sphere("Leaf", 0.05, c, "M_LeafLight", r, (math.cos(a) * 0.04, math.sin(a) * 0.04, 0.16), scale=(0.6, 0.35, 1.3), segments=8, rings=6)
        leaf.rotation_euler = (R(25) * math.cos(a), R(25) * math.sin(a), a)
    return r


@prop
def cactus(c):
    r = root("Cactus", c)
    bk.cylinder("Pot", 0.06, 0.09, c, "M_PlasticWhite", r, (0, 0, 0.045), radius_top=0.07, segments=14)
    bk.sphere("Body", 0.05, c, "M_Leaf", r, (0, 0, 0.16), scale=(1, 1, 1.7), segments=12, rings=10)
    bk.sphere("ArmA", 0.025, c, "M_Leaf", r, (0.05, 0, 0.18), scale=(1, 1, 1.6), segments=10, rings=8)
    bk.sphere("Flower", 0.018, c, "M_Bow", r, (0, 0, 0.245), segments=8, rings=6)
    return r


@prop
def photo_frame(c):
    r = root("PhotoFrame", c)
    f = bk.box("Frame", (0.16, 0.02, 0.13), c, "M_DarkWood", r, (0, 0, 0.07), bevel=0.004)
    f.rotation_euler = (R(-12), 0, 0)
    p = bk.box("Photo", (0.13, 0.005, 0.1), c, "M_LeafLight", r, (0, -0.012, 0.07))
    p.rotation_euler = (R(-12), 0, 0)
    return r


@prop
def desk_lamp(c):
    r = root("DeskLamp", c)
    bk.cylinder("Base", 0.07, 0.02, c, "M_PlasticWhite", r, (0, 0, 0.01), segments=16)
    arm = bk.cylinder("Arm", 0.01, 0.3, c, "M_PlasticWhite", r, (0, 0.04, 0.16), rotation=(R(-15), 0, 0), segments=8)
    bk.cylinder("Shade", 0.08, 0.09, c, "M_Bow", r, (0, -0.02, 0.33), radius_top=0.04, rotation=(R(160), 0, 0), segments=16)
    return r


@prop
def pencil_cup(c):
    r = root("PencilCup", c)
    bk.cylinder("Cup", 0.035, 0.09, c, "M_DarkMetal", r, (0, 0, 0.045), segments=12)
    for i, m in enumerate(("M_DuckYellow", "M_PlasticRed", "M_PlasticBlue")):
        p = bk.cylinder("Pencil", 0.006, 0.16, c, m, r, (0.012 * (i - 1), 0.005 * i, 0.12), segments=6)
        p.rotation_euler = (R(8 * (i - 1)), R(6 * (1 - i)), 0)
    return r


@prop
def wall_clock(c):
    r = root("WallClock", c)
    bk.cylinder("Face", 0.18, 0.04, c, "M_PlasticWhite", r, (0, -0.02, 0), rotation=(R(90), 0, 0), segments=24)
    bk.torus("Rim", 0.18, 0.018, c, "M_PlasticDark", r, (0, -0.02, 0), rotation=(R(90), 0, 0), major_seg=24, minor_seg=6)
    h = bk.box("Hour", (0.015, 0.005, 0.09), c, "M_PlasticDark", r, (0.02, -0.045, 0.03))
    h.rotation_euler = (0, R(-40), 0)
    m = bk.box("Minute", (0.01, 0.005, 0.14), c, "M_PlasticRed", r, (-0.03, -0.047, 0.05))
    m.rotation_euler = (0, R(30), 0)
    return r


@prop
def dartboard(c):
    r = root("Dartboard", c)
    bk.cylinder("Board", 0.23, 0.04, c, "M_PlasticDark", r, (0, -0.02, 0), rotation=(R(90), 0, 0), segments=24)
    for rad, m in ((0.17, "M_PlasticRed"), (0.1, "M_PlasticGreen"), (0.03, "M_PlasticRed")):
        bk.torus("Ring", rad, 0.012, c, m, r, (0, -0.042, 0), rotation=(R(90), 0, 0), major_seg=24, minor_seg=5)
    d = bk.cylinder("Dart", 0.006, 0.12, c, "M_DuckYellow", r, (0.05, -0.09, 0.04), rotation=(R(80), 0, 0), segments=6)
    return r


@prop
def alarm_light(c):
    r = root("AlarmLight", c)
    bk.cylinder("Base", 0.09, 0.05, c, "M_DarkMetal", r, (0, 0, 0.025), segments=16)
    bk.sphere("Dome", 0.075, c, "M_AlarmRed", r, (0, 0, 0.05), scale=(1, 1, 1.1), segments=16, rings=8)
    return r


# ------------------------------------------------------------------------------------------- stations






@prop
def delivery_cabinet(c):
    """Manager's inbox (reports, left) + coffee tray (right); slot.delivery anchor sits on the top centre (0.95 m)."""
    r = root("DeliveryCabinet", c)
    bk.box("Cabinet", (1.0, 0.5, 0.94), c, "M_DarkWood", r, (0, 0, 0.47), bevel=0.012)
    for sx in (-1, 1):
        bk.box("Door", (0.47, 0.015, 0.8), c, "M_DeskWood", r, (0.245 * sx, -0.255, 0.47), bevel=0.004)
        bk.box("Knob", (0.02, 0.02, 0.1), c, "M_Gold", r, (0.04 * sx, -0.27, 0.6))
    bk.box("InboxBase", (0.36, 0.42, 0.015), c, "M_DarkMetal", r, (-0.24, 0, 0.9475))
    for sx in (-1, 1):
        bk.box("InboxSide", (0.015, 0.42, 0.07), c, "M_DarkMetal", r, (-0.24 + 0.175 * sx, 0, 0.975))
    bk.box("InboxBack", (0.36, 0.015, 0.07), c, "M_DarkMetal", r, (-0.24, 0.205, 0.975))
    bk.cylinder("CoffeeTray", 0.2, 0.02, c, "M_Metal", r, (0.25, 0, 0.95), segments=24, cap_bevel=0.005)
    bk.box("SignPost", (0.03, 0.03, 0.4), c, "M_DarkMetal", r, (0, 0.2, 1.14))
    bk.box("SignBoard", (0.56, 0.03, 0.18), c, "M_PlasticBlue", r, (0, 0.19, 1.4), bevel=0.01)
    return r


@prop
def lost_found_box(c):
    r = root("LostFoundBox", c)
    w, d, h, t = 0.62, 0.42, 0.3, 0.012
    bk.box("Bottom", (w, d, t), c, "M_Cardboard", r, (0, 0, t / 2))
    for sy in (-1, 1):
        bk.box("WallY", (w, t, h), c, "M_Cardboard", r, (0, sy * (d - t) / 2, h / 2))
        flap = bk.box("FlapY", (w, t, 0.16), c, "M_Cardboard", r, (0, sy * (d / 2 + 0.06), h + 0.05))
        flap.rotation_euler = (R(-50 * sy), 0, 0)
    for sx in (-1, 1):
        bk.box("WallX", (t, d, h), c, "M_Cardboard", r, (sx * (w - t) / 2, 0, h / 2))
    bk.box("Label", (0.36, 0.006, 0.1), c, "M_Paper", r, (0, -d / 2 - 0.004, 0.17))
    return r


@prop
def junk_bin(c):
    r = root("JunkBin", c)
    w, d, h, t = 0.78, 0.52, 0.55, 0.025
    bk.box("Bottom", (w, d, t), c, "M_BinGreen", r, (0, 0, t / 2), bevel=0.006)
    for sy in (-1, 1):
        bk.box("WallY", (w, t, h), c, "M_BinGreen", r, (0, sy * (d - t) / 2, h / 2), bevel=0.006)
    for sx in (-1, 1):
        bk.box("WallX", (t, d, h), c, "M_BinGreen", r, (sx * (w - t) / 2, 0, h / 2), bevel=0.006)
        bk.box("Grip", (0.03, 0.18, 0.05), c, "M_BinGreen", r, (sx * (w / 2 + 0.01), 0, h - 0.08))
    bk.box("Label", (0.5, 0.006, 0.16), c, "M_Paper", r, (0, -d / 2 - 0.004, 0.3))
    return r


@prop
def file_cabinet(c):
    r = root("FileCabinet", c)
    bk.box("Body", (0.5, 0.6, 1.1), c, "M_Cabinet", r, (0, 0, 0.55), bevel=0.012)
    for i in range(3):
        z = 0.2 + i * 0.35
        bk.box("DrawerFront", (0.46, 0.02, 0.32), c, "M_Cabinet", r, (0, -0.305, z), bevel=0.005)
        bk.box("Handle", (0.16, 0.03, 0.025), c, "M_Metal", r, (0, -0.325, z + 0.06))
        bk.box("LabelHolder", (0.1, 0.01, 0.05), c, "M_Paper", r, (0, -0.318, z + 0.12))
    return r


# --------------------------------------------------------------------------------------------- items

@prop
def mug(c):
    r = root("Mug", c)
    s = 1.3
    prof = [(0, 0), (0.045, 0), (0.047, 0.008), (0.047, 0.1), (0.041, 0.1), (0.039, 0.012), (0, 0.012)]
    bk.lathe("Cup", [(a * s, b * s) for a, b in prof], c, "M_Ceramic", r, segments=20)
    h = bk.torus("Handle", 0.03 * s, 0.008 * s, c, "M_Ceramic", r, (0.05 * s, 0, 0.055 * s), arc=math.pi * 1.15, major_seg=12, minor_seg=6)
    h.rotation_euler = (R(90), R(103), 0)
    bk.cylinder("Coffee", 0.038 * s, 0.006, c, "M_Coffee", r, (0, 0, 0.086 * s), segments=20)
    return r


@prop
def boss_mug(c):
    r = root("BossMug", c)
    prof = [(0, 0), (0.06, 0), (0.062, 0.01), (0.062, 0.13), (0.055, 0.13), (0.053, 0.014), (0, 0.014)]
    bk.lathe("Cup", prof, c, "M_BossMug", r, segments=20)
    bk.torus("Rim", 0.0585, 0.005, c, "M_Gold", r, (0, 0, 0.13), major_seg=20, minor_seg=6)
    h = bk.torus("Handle", 0.04, 0.01, c, "M_Gold", r, (0.068, 0, 0.068), arc=math.pi * 1.15, major_seg=12, minor_seg=6)
    h.rotation_euler = (R(90), R(103), 0)
    bk.box("Star", (0.04, 0.004, 0.04), c, "M_Gold", r, (0, -0.062, 0.075)).rotation_euler = (0, R(45), 0)
    return r


@prop
def report(c):
    r = root("Report", c)
    bk.box("Pages", (0.235, 0.315, 0.012), c, "M_Paper", r, (0.008, 0, 0.008))
    bk.box("Cover", (0.25, 0.33, 0.006), c, "M_PlasticBlue", r, (0, 0, 0.017), bevel=0.002)
    bk.box("Label", (0.15, 0.06, 0.002), c, "M_Paper", r, (0, 0.06, 0.021))
    bk.box("Spine", (0.012, 0.33, 0.02), c, "M_PlasticBlue", r, (-0.125, 0, 0.01))
    return r


@prop
def rubber_duck(c):
    r = root("RubberDuck", c)
    bk.sphere("Body", 0.095, c, "M_DuckYellow", r, (0, 0.01, 0.07), scale=(1.0, 1.3, 0.78), segments=18, rings=12)
    bk.sphere("Head", 0.065, c, "M_DuckYellow", r, (0, -0.07, 0.16), segments=16, rings=10)
    bk.sphere("Beak", 0.035, c, "M_DuckBeak", r, (0, -0.13, 0.15), scale=(1.25, 1.2, 0.45), segments=12, rings=8)
    for sx in (-1, 1):
        bk.sphere("Eye", 0.013, c, "M_Pupil", r, (0.035 * sx, -0.115, 0.185), segments=8, rings=6)
    tail = bk.sphere("Tail", 0.04, c, "M_DuckYellow", r, (0, 0.13, 0.11), scale=(0.8, 1.2, 0.7), segments=10, rings=8)
    tail.rotation_euler = (R(-30), 0, 0)
    for sx in (-1, 1):
        bk.sphere("Wing", 0.05, c, "M_DuckYellow", r, (0.085 * sx, 0.02, 0.08), scale=(0.35, 1.0, 0.6), segments=10, rings=8)
    return r


@prop
def stapler(c):
    r = root("Stapler", c)
    bk.box("Base", (0.075, 0.24, 0.025), c, "M_DarkMetal", r, (0, 0, 0.0125), bevel=0.008)
    top = bk.box("Top", (0.07, 0.23, 0.04), c, "M_PlasticRed", r, (0, -0.005, 0.048), bevel=0.012)
    top.rotation_euler = (R(-4), 0, 0)
    bk.cylinder("Hinge", 0.018, 0.08, c, "M_Metal", r, (0, 0.105, 0.035), rotation=(0, R(90), 0), segments=10)
    return r


@prop
def rival_cv(c):
    r = root("RivalCv", c)
    bk.box("Pages", (0.23, 0.31, 0.01), c, "M_Paper", r, (0.008, 0, 0.007))
    bk.box("Cover", (0.25, 0.33, 0.006), c, "M_Purple", r, (0, 0, 0.015), bevel=0.002)
    bk.cylinder("Logo", 0.045, 0.003, c, "M_Gold", r, (0, 0.05, 0.0195), segments=16)
    bk.box("Stamp", (0.12, 0.04, 0.002), c, "M_PlasticRed", r, (0, -0.08, 0.019))
    return r


@prop
def salary_list(c):
    r = root("SalaryList", c)
    bk.box("Envelope", (0.28, 0.18, 0.012), c, "M_Envelope", r, (0, 0, 0.006), bevel=0.002)
    flap = bk.prism_nameplate("Flap", 0.28, 0.18, 0.004, c, "M_Envelope", r, (0, 0, 0.012))
    bk.cylinder("Seal", 0.026, 0.008, c, "M_WaxRed", r, (0, 0, 0.018), segments=14)
    bk.box("Stamp", (0.1, 0.035, 0.002), c, "M_WaxRed", r, (0.08, -0.05, 0.0135))
    return r


@prop
def clutter_papers(c):
    r = root("ClutterPapers", c)
    rnd = random.Random(21)
    z = 0.002
    for i in range(7):
        sheet = bk.box("Sheet", (0.21, 0.297, 0.004), c, "M_Paper", r, (rnd.uniform(-0.05, 0.05), rnd.uniform(-0.04, 0.04), z))
        sheet.rotation_euler = (R(rnd.uniform(-3, 3)), R(rnd.uniform(-3, 3)), R(rnd.uniform(-35, 35)))
        z += 0.012
    for i in range(3):
        bk.ico("Crumple", rnd.uniform(0.035, 0.05), c, "M_Paper", r, (rnd.uniform(-0.12, 0.12), rnd.uniform(-0.12, 0.12), z + 0.02), subdiv=1, jitter=0.25, seed=i + 3)
    cup = bk.cylinder("OldCup", 0.035, 0.09, c, "M_PlasticWhite", r, (0.1, 0.1, 0.07), radius_top=0.042, segments=12)
    cup.rotation_euler = (R(80), 0, R(30))
    return r


@prop
def clutter_box(c):
    r = root("ClutterBox", c)
    w, d, h, t = 0.36, 0.28, 0.2, 0.01
    bk.box("Bottom", (w, d, t), c, "M_Cardboard", r, (0, 0, t / 2))
    for sy in (-1, 1):
        bk.box("WallY", (w, t, h), c, "M_Cardboard", r, (0, sy * (d - t) / 2, h / 2))
        flap = bk.box("Flap", (w, t, 0.12), c, "M_Cardboard", r, (0, sy * (d / 2 + 0.045), h + 0.035))
        flap.rotation_euler = (R(-55 * sy), 0, 0)
    for sx in (-1, 1):
        bk.box("WallX", (t, d, h), c, "M_Cardboard", r, (sx * (w - t) / 2, 0, h / 2))
    rnd = random.Random(9)
    for i in range(4):
        p = bk.cylinder("Junk", 0.008, 0.25, c, rnd.choice(["M_DuckYellow", "M_PlasticRed", "M_PlasticBlue"]), r, (rnd.uniform(-0.1, 0.1), rnd.uniform(-0.06, 0.06), 0.2), segments=6)
        p.rotation_euler = (R(rnd.uniform(-25, 25)), R(rnd.uniform(-25, 25)), 0)
    bk.torus("Cable", 0.07, 0.01, c, "M_PlasticDark", r, (0.05, 0.02, 0.2), rotation=(R(70), 0, R(20)), major_seg=16, minor_seg=5)
    bk.box("Papers", (0.25, 0.2, 0.06), c, "M_Paper", r, (-0.02, 0, 0.17)).rotation_euler = (R(10), 0, R(8))
    return r


@prop
def spray(c):
    r = root("Spray", c)
    bk.cylinder("Bottle", 0.042, 0.17, c, "M_PlasticGreen", r, (0, 0, 0.085), segments=16, cap_bevel=0.008)
    bk.cylinder("Shoulder", 0.042, 0.04, c, "M_PlasticGreen", r, (0, 0, 0.19), radius_top=0.02, segments=16)
    bk.cylinder("Neck", 0.02, 0.03, c, "M_PlasticWhite", r, (0, 0, 0.225), segments=12)
    bk.box("Head", (0.045, 0.085, 0.04), c, "M_PlasticWhite", r, (0, -0.02, 0.255), bevel=0.01)
    bk.box("Trigger", (0.018, 0.02, 0.05), c, "M_PlasticWhite", r, (0, -0.045, 0.215))
    bk.cylinder("Label", 0.0435, 0.06, c, "M_PlasticWhite", r, (0, 0, 0.09), segments=16)
    return r


@prop
def desk_dirt(c):
    r = root("DeskDirt", c)
    rnd = random.Random(31)
    for i in range(3):
        bk.ico("PaperBall", rnd.uniform(0.03, 0.045), c, "M_Paper", r, (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.15, 0.15), 0.03), subdiv=1, jitter=0.3, seed=40 + i)
    for i in range(2):
        bk.torus("Ring", 0.04, 0.006, c, "M_Stain", r, (rnd.uniform(-0.3, 0.3), rnd.uniform(-0.15, 0.15), 0.002), major_seg=16, minor_seg=4).scale = (1, 1, 0.2)
    for i in range(14):
        bk.ico("Crumb", rnd.uniform(0.006, 0.012), c, "M_Dirt", r, (rnd.uniform(-0.35, 0.35), rnd.uniform(-0.2, 0.2), 0.006), subdiv=1, jitter=0.3, seed=60 + i)
    peel = bk.sphere("BananaPeel", 0.05, c, "M_DuckYellow", r, (0.25, 0.1, 0.012), scale=(1.6, 0.6, 0.18), segments=10, rings=6)
    peel.rotation_euler = (0, 0, R(30))
    return r


@prop
def coffee_spill(c):
    r = root("CoffeeSpill", c)
    bk.flat_blob("DeskPuddle", 0.24, 0.16, 0.004, c, "M_Coffee", r, (0, 0, 0.0), points=16, seed=7, wobble=0.3)
    bk.flat_blob("Splash", 0.05, 0.04, 0.004, c, "M_Coffee", r, (0.3, -0.05, 0.0), points=10, seed=8)
    bk.flat_blob("FloorPuddle", 0.3, 0.2, 0.004, c, "M_Coffee", r, (0.1, -0.75, -0.74), points=16, seed=9, wobble=0.3)
    return r


# ------------------------------------------------------------------------------------------ main

def build_all():
    """Build every registered prop into its own collection; returns the list of root empties."""
    import map_props_office  # noqa: F401  (registers props)
    import map_props_rooms  # noqa: F401
    coll = bk.collection("Props")
    roots = []
    seen = set()
    for fn in PROPS:
        sub = bk.collection(fn.__name__, coll)
        r = fn(sub)
        if "." in r.name:
            # a sub-object of an earlier prop took this name: rename that one, keep the prop root exact
            want = r.name.split(".")[0]
            other = bpy.data.objects.get(want)
            if other is not None and other not in roots:
                other.name = want + "Part"
            r.name = want
        if r.name in seen or "." in r.name:
            raise RuntimeError("Duplicate prop name " + r.name)
        seen.add(r.name)
        roots.append(r)
    return roots


def check_driven(roots):
    """Driven sub-object names must be exact in the prop that uses them (Blender suffixes duplicates)."""
    need = {"Desk": ["Drawer"], "Computer": ["Screen", "PcLed"], "Mug": ["Coffee"], "Nameplate": ["Stripe"],
            "MailEnvelope": ["Stripe"], "DeskPhone": ["Handset", "RingLight"], "Printer": ["StatusLight"],
            "NetworkRack": ["RouterLight", "RouterLight2"], "FuseBox": ["FuseLever", "FuseLight"],
            "SecurityCamera": ["RecLight"], "EmergencyLight": ["EmLamp"], "ServerRack": ["Leds", "LedsB"],
            "PcTower": ["PcLed"], "WallTv": ["TvScreen"]}
    by_name = {r.name: r for r in roots}
    bad = []
    for prop_name, names in need.items():
        r = by_name.get(prop_name)
        if r is None:
            bad.append(prop_name + " missing")
            continue
        found = set()
        stack = [r]
        while stack:
            o = stack.pop()
            stack.extend(o.children)
            found.add(map_kit.base_name(o))
        for n in names:
            if n not in found:
                bad.append(prop_name + "/" + n)
    if bad:
        print("DRIVEN NAME PROBLEMS:", bad)
    return bad


def main():
    import map_textures
    map_textures.main()
    bk.reset_scene()
    roots = build_all()
    check_driven(roots)
    out_dir = os.path.join(bk.UNITY_MODELS, "Props")
    import shutil
    os.makedirs(os.path.join(bk.EXPORT_DIR, "Props"), exist_ok=True)
    os.makedirs(out_dir, exist_ok=True)
    stats = []
    for r in roots:
        map_kit.merge_prop(r)
        stats.append((r.name, map_kit.tri_count(r)))
    for r in roots:
        path = os.path.join(out_dir, r.name + ".fbx")
        map_kit.export_prop(r, path)
        shutil.copy2(path, os.path.join(bk.EXPORT_DIR, "Props", r.name + ".fbx"))
    with open(os.path.join(bk.PREVIEW_DIR, "props_tris.txt"), "w", encoding="utf-8") as f:
        for n, t in sorted(stats, key=lambda x: -x[1]):
            f.write("%-22s %6d\n" % (n, t))
    # Lay out a browsing grid in the saved .blend (and for the preview render).
    cols = 12
    for i, r in enumerate(roots):
        r.location = ((i % cols) * 2.6, -(i // cols) * 2.6, 0)
    bk.save_blend("props.blend")
    if "--previews" in sys.argv:
        bk.setup_preview_render((2200, 1700))
        rows = (len(roots) + cols - 1) // cols
        cx, cy = (cols - 1) * 2.6 / 2, -(rows - 1) * 2.6 / 2
        bk.render_preview(os.path.join(bk.PREVIEW_DIR, "props_overview.png"), target=(cx, cy, 0.3), distance=60, elevation_deg=50, azimuth_deg=-20, lens=50, ortho_scale=33)
    if "--each" in sys.argv:
        from mathutils import Vector
        bk.setup_preview_render((420, 420))
        for i, r in enumerate(roots):
            for o in roots:
                o.location = (0, 0, -500) if o is not r else (0, 0, 0)
            bpy.context.view_layer.update()
            pts = []
            stack = [r]
            while stack:
                o = stack.pop()
                stack.extend(o.children)
                if o.type == "MESH":
                    pts.extend(o.matrix_world @ Vector(cn) for cn in o.bound_box)
            lo = Vector((min(p.x for p in pts), min(p.y for p in pts), min(p.z for p in pts)))
            hi = Vector((max(p.x for p in pts), max(p.y for p in pts), max(p.z for p in pts)))
            size = max((hi - lo).length, 0.1)
            bk.render_preview(os.path.join(bk.PREVIEW_DIR, "props", r.name + ".png"), target=tuple((lo + hi) / 2), distance=size * 1.6, elevation_deg=28, azimuth_deg=-30, lens=50)
    total = sum(t for _, t in stats)
    print("PROPS_DONE", len(roots), "tris", total)


if __name__ == "__main__":
    main()
