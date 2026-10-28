"""v2 props: kitchen, copy room, storage, restroom, server room, lounge.

Conventions as in map_props_office.py (front = -Y, pivot at the floor centre; wall-mounted: back centre,
vertically centred).
"""
import math
import random

import bk_common as bk
from map_kit import prop, root, uv_plane, cable, R

B = bk.box
CY = bk.cylinder
SP = bk.sphere


# ============================================================================================ kitchen

def _base_cabinet(c, r, w, d=0.62, h=0.92, doors=2, top="M_DarkWood"):
    B("Cabinet", (w, d - 0.04, h - 0.1), c, "M_PlasticWhite", r, (0, 0.02, 0.1 + (h - 0.1) / 2 - 0.02), bevel=0.006)
    B("Plinth", (w - 0.02, d - 0.1, 0.1), c, "M_Baseboard", r, (0, 0.05, 0.05))
    B("Top", (w + 0.005, d, 0.04), c, top, r, (0, 0, h - 0.02), bevel=0.006)
    dw = w / doors
    for i in range(doors):
        x = -w / 2 + dw * (i + 0.5)
        B("Door", (dw - 0.012, 0.018, h - 0.18), c, "M_PaintMintWhite", r, (x, -d / 2 + 0.03, 0.1 + (h - 0.18) / 2), bevel=0.004)
        B("Pull", (0.012, 0.02, 0.12), c, "M_Steel", r, (x + (dw / 2 - 0.06) * (1 if i % 2 else -1), -d / 2 + 0.015, h - 0.22))


@prop
def counter_module(c):
    r = root("CounterModule", c)
    _base_cabinet(c, r, 1.0)
    B("Backsplash", (1.0, 0.02, 0.35), c, "M_KitchenTileW", r, (0, 0.3, 1.1))
    return r


@prop
def sink_module(c):
    r = root("SinkModule", c)
    w, d, h = 1.1, 0.62, 0.92
    B("Cabinet", (w, d - 0.04, h - 0.1), c, "M_PlasticWhite", r, (0, 0.02, 0.42), bevel=0.006)
    B("Plinth", (w - 0.02, d - 0.1, 0.1), c, "M_Baseboard", r, (0, 0.05, 0.05))
    for sx in (-1, 1):
        B("TopSide", (0.25, d, 0.04), c, "M_DarkWood", r, (sx * (w / 2 - 0.125), 0, h - 0.02))
    B("TopFront", (0.6, 0.1, 0.04), c, "M_DarkWood", r, (0, -d / 2 + 0.05, h - 0.02))
    B("TopBack", (0.6, 0.12, 0.04), c, "M_DarkWood", r, (0, d / 2 - 0.06, h - 0.02))
    B("Basin", (0.58, 0.4, 0.02), c, "M_Steel", r, (0, -0.01, h - 0.16))
    for sx in (-1, 1):
        B("BasinSide", (0.02, 0.4, 0.16), c, "M_Steel", r, (sx * 0.29, -0.01, h - 0.08))
    for sy in (-1, 1):
        B("BasinEnd", (0.58, 0.02, 0.16), c, "M_Steel", r, (0, -0.01 + sy * 0.2, h - 0.08))
    CY("Tap", 0.018, 0.3, c, "M_Steel", r, (0, 0.24, h + 0.15), segments=10)
    t = CY("Spout", 0.014, 0.22, c, "M_Steel", r, (0, 0.14, h + 0.29), rotation=(R(90), 0, 0), segments=10)
    for sx in (-1, 1):
        B("Door", (0.53, 0.018, 0.74), c, "M_PaintMintWhite", r, (sx * 0.275, -d / 2 + 0.03, 0.46), bevel=0.004)
    B("Backsplash", (w, 0.02, 0.35), c, "M_KitchenTileW", r, (0, 0.3, 1.1))
    B("SoapBottle", (0.06, 0.06, 0.16), c, "M_PlasticGreen", r, (0.4, 0.15, h + 0.08), bevel=0.015)
    B("Sponge", (0.1, 0.06, 0.03), c, "M_Sticky", r, (-0.42, 0.1, h + 0.015))
    return r


@prop
def fridge(c):
    r = root("Fridge", c)
    w, d, h = 0.84, 0.7, 1.9
    B("Body", (w, d, h), c, "M_FridgeWhite", r, (0, 0, h / 2), bevel=0.03)
    B("Gap", (w - 0.02, 0.006, 0.012), c, "M_PlasticDark", r, (0, -d / 2 - 0.002, 1.2))
    for z0, z1 in ((0.08, 1.18), (1.22, 1.85)):
        B("DoorPanel", (w - 0.03, 0.02, z1 - z0), c, "M_FridgeWhite", r, (0, -d / 2 - 0.01, (z0 + z1) / 2), bevel=0.012)
        B("Handle", (0.03, 0.04, 0.35), c, "M_Steel", r, (w / 2 - 0.08, -d / 2 - 0.045, z0 + 0.25 if z1 < 1.5 else z0 + 0.15), bevel=0.01)
    rnd = random.Random(23)
    for i in range(5):
        m = rnd.choice(["M_Sticky", "M_StickyPink", "M_Paper", "M_PlasticRed", "M_PlasticBlue"])
        n = B("Magnet", (rnd.uniform(0.05, 0.14), 0.006, rnd.uniform(0.05, 0.16)), c, m, r, (rnd.uniform(-0.3, 0.2), -d / 2 - 0.022, rnd.uniform(0.9, 1.7)))
        n.rotation_euler = (0, R(rnd.uniform(-10, 10)), 0)
    B("Box", (0.3, 0.25, 0.2), c, "M_Cardboard", r, (0.1, 0.05, h + 0.1)).rotation_euler = (0, 0, R(8))
    return r


@prop
def upper_cabinet(c):
    """Wall cabinet 1.0 x 0.35 x 0.7 (pivot: back centre, vertical centre)."""
    r = root("UpperCabinet", c)
    B("Box", (1.0, 0.33, 0.7), c, "M_PlasticWhite", r, (0, -0.165, 0), bevel=0.006)
    for sx in (-1, 1):
        B("Door", (0.49, 0.018, 0.66), c, "M_PaintMintWhite", r, (sx * 0.25, -0.34, 0), bevel=0.004)
        B("Pull", (0.012, 0.02, 0.1), c, "M_Steel", r, (sx * 0.06, -0.355, -0.24))
    return r


@prop
def coffee_machine(c):
    """Espresso machine (st.coffee). Steam particles are added by Unity."""
    r = root("CoffeeMachine", c)
    B("Body", (0.42, 0.4, 0.46), c, "M_Steel", r, (0, 0.02, 0.23), bevel=0.025)
    B("Side", (0.43, 0.3, 0.3), c, "M_PlasticRed", r, (0, 0.06, 0.28), bevel=0.02)
    B("Hopper", (0.22, 0.2, 0.14), c, "M_Water", r, (0.08, 0.08, 0.53), bevel=0.01)
    B("Beans", (0.18, 0.16, 0.07), c, "M_Coffee", r, (0.08, 0.08, 0.5))
    B("Panel", (0.34, 0.02, 0.1), c, "M_PlasticDark", r, (0, -0.185, 0.39), bevel=0.006)
    B("Display", (0.1, 0.006, 0.05), c, "M_ScreenDesktop", r, (-0.08, -0.197, 0.39))
    for i, m in enumerate(("M_LEDGreen", "M_PlasticWhite", "M_PlasticWhite")):
        CY("Button", 0.016, 0.02, c, m, r, (0.03 + i * 0.045, -0.2, 0.39), rotation=(R(90), 0, 0), segments=10)
    B("Head", (0.2, 0.1, 0.06), c, "M_PlasticDark", r, (0, -0.2, 0.28), bevel=0.012)
    CY("Portafilter", 0.04, 0.03, c, "M_Steel", r, (0, -0.22, 0.235), segments=12)
    B("Handle", (0.03, 0.15, 0.025), c, "M_PlasticDark", r, (0.0, -0.32, 0.235))
    B("DripTray", (0.3, 0.16, 0.025), c, "M_Steel", r, (0, -0.2, 0.0125), bevel=0.004)
    B("Grill", (0.26, 0.12, 0.004), c, "M_DarkMetal", r, (0, -0.2, 0.027))
    B("Sign", (0.2, 0.004, 0.06), c, "M_Paper", r, (0.0, -0.197, 0.06)).rotation_euler = (0, R(-4), 0)
    return r


@prop
def mug_shelf(c):
    """Counter-top mug board, 0.8 wide; mugs stand on top at 0.05 m (slot.mugshelf)."""
    r = root("MugShelf", c)
    B("Board", (0.82, 0.24, 0.035), c, "M_DeskWood", r, (0, 0, 0.0325), bevel=0.006)
    for sx in (-1, 1):
        B("Foot", (0.06, 0.2, 0.015), c, "M_Rubber", r, (sx * 0.35, 0, 0.0075))
    B("Back", (0.82, 0.02, 0.32), c, "M_DeskWood", r, (0, 0.13, 0.21), bevel=0.004)
    for i in range(6):
        CY("Peg", 0.008, 0.06, c, "M_Steel", r, (-0.3 + i * 0.12, 0.1, 0.32), rotation=(R(90), 0, 0), segments=6)
    B("Sign", (0.24, 0.005, 0.06), c, "M_Manila", r, (0, 0.118, 0.42))
    return r


@prop
def salt_shaker(c):
    r = root("SaltShaker", c)
    bk.lathe("Glass", [(0, 0), (0.025, 0), (0.028, 0.06), (0.022, 0.085), (0, 0.085)], c, "M_VendingGlass", r, segments=12)
    bk.lathe("Salt", [(0, 0.004), (0.022, 0.004), (0.024, 0.05), (0, 0.05)], c, "M_PlasticWhite", r, segments=12)
    CY("Cap", 0.022, 0.02, c, "M_Steel", r, (0, 0, 0.095), segments=12)
    bk.lathe("Pepper", [(0, 0), (0.025, 0), (0.028, 0.06), (0.022, 0.085), (0, 0.085)], c, "M_DarkMetal", r, (0.07, 0.02, 0), segments=12)
    return r


@prop
def sugar_jar(c):
    r = root("SugarJar", c)
    CY("Jar", 0.05, 0.13, c, "M_VendingGlass", r, (0, 0, 0.065), segments=14)
    CY("Sugar", 0.046, 0.09, c, "M_PlasticWhite", r, (0, 0, 0.047), segments=14)
    CY("Lid", 0.052, 0.02, c, "M_DeskWood", r, (0, 0, 0.14), segments=14)
    B("Stirrers", (0.06, 0.05, 0.1), c, "M_DeskWood", r, (0.1, 0.0, 0.05))
    return r


@prop
def microwave(c):
    r = root("Microwave", c)
    B("Body", (0.5, 0.38, 0.3), c, "M_PlasticWhite", r, (0, 0, 0.15), bevel=0.015)
    B("Window", (0.3, 0.006, 0.2), c, "M_Tv", r, (-0.06, -0.192, 0.15))
    B("Panel", (0.1, 0.006, 0.24), c, "M_PlasticDark", r, (0.18, -0.192, 0.15))
    B("Clock", (0.06, 0.004, 0.025), c, "M_LEDGreen", r, (0.18, -0.196, 0.23))
    B("Handle", (0.02, 0.03, 0.18), c, "M_Steel", r, (0.11, -0.205, 0.15))
    return r


@prop
def kettle(c):
    r = root("Kettle", c)
    CY("Base", 0.09, 0.025, c, "M_PlasticDark", r, (0, 0, 0.0125), segments=14)
    bk.lathe("Body", [(0, 0.025), (0.08, 0.025), (0.085, 0.12), (0.06, 0.22), (0, 0.22)], c, "M_Steel", r, segments=16)
    bk.torus("Handle", 0.07, 0.012, c, "M_PlasticDark", r, (0.07, 0, 0.14), rotation=(R(90), 0, 0), arc=math.pi, major_seg=10, minor_seg=5).rotation_euler = (R(90), R(-90), 0)
    CY("Spout", 0.015, 0.08, c, "M_Steel", r, (-0.09, 0, 0.17), rotation=(0, R(-50), 0), segments=8)
    return r


@prop
def fruit_bowl(c):
    r = root("FruitBowl", c)
    bk.lathe("Bowl", [(0, 0), (0.06, 0), (0.13, 0.08), (0.12, 0.085), (0.055, 0.012), (0, 0.012)], c, "M_Ceramic", r, segments=16)
    for i, (m, s) in enumerate((("M_PlasticOrange", 0.045), ("M_PlasticRed", 0.042), ("M_LeafLight", 0.04), ("M_DuckYellow", 0.04))):
        a = i * 1.6
        SP("Fruit", s, c, m, r, (math.cos(a) * 0.05, math.sin(a) * 0.05, 0.07 + (0.03 if i == 3 else 0)), segments=10, rings=6)
    return r


@prop
def kitchen_table(c):
    r = root("KitchenTable", c)
    CY("Top", 0.55, 0.04, c, "M_DeskWood", r, (0, 0, 0.74), segments=28, cap_bevel=0.008)
    CY("Pole", 0.05, 0.7, c, "M_DarkMetal", r, (0, 0, 0.37), segments=12)
    CY("Base", 0.3, 0.03, c, "M_DarkMetal", r, (0, 0, 0.015), segments=20)
    CY("Napkins", 0.05, 0.08, c, "M_Paper", r, (0.1, 0.05, 0.8), segments=10)
    return r


@prop
def stool(c):
    r = root("Stool", c)
    CY("Seat", 0.19, 0.06, c, "M_KitchenTileG", r, (0, 0, 0.62), segments=18, cap_bevel=0.02)
    for i in range(4):
        a = math.pi / 4 + i * math.pi / 2
        leg = CY("Leg", 0.015, 0.62, c, "M_Steel", r, (math.cos(a) * 0.12, math.sin(a) * 0.12, 0.31), segments=6)
        leg.rotation_euler = (R(6) * -math.sin(a), R(6) * math.cos(a), 0)
    bk.torus("Ring", 0.15, 0.01, c, "M_Steel", r, (0, 0, 0.25), major_seg=16, minor_seg=4)
    return r


@prop
def trash_bins(c):
    r = root("TrashBins", c)
    for i, (m, label) in enumerate((("M_PlasticBlue", "M_Paper"), ("M_DuckYellow", "M_PlasticWhite"), ("M_PlasticGreen", "M_PlasticWhite"))):
        x = -0.3 + i * 0.3
        B("Bin", (0.28, 0.38, 0.62), c, m, r, (x, 0, 0.31), bevel=0.02)
        B("Lid", (0.29, 0.39, 0.04), c, m, r, (x, 0, 0.64), bevel=0.012)
        B("Label", (0.14, 0.004, 0.1), c, label, r, (x, -0.192, 0.45))
    return r


# ============================================================================================ copy room

@prop
def printer(c):
    """Shared office copier (st.printer). Output tray protrudes at the front at ~0.98 m (slot.printer_tray).
    StatusLight blinks while printing."""
    r = root("Printer", c)
    w, d = 0.95, 0.66
    B("Body", (w, d, 0.92), c, "M_PlasticWhite", r, (0, 0, 0.46), bevel=0.025)
    B("Plinth", (w - 0.04, d - 0.04, 0.06), c, "M_PlasticDark", r, (0, 0, 0.03))
    for i, z in enumerate((0.2, 0.42, 0.64)):
        B("PaperDrawer", (w - 0.08, 0.02, 0.19), c, "M_PlasticWhite", r, (0, -d / 2 - 0.005, z), bevel=0.006)
        B("DrawerHandle", (0.26, 0.025, 0.03), c, "M_PlasticDark", r, (0, -d / 2 - 0.02, z + 0.06))
        B("Gauge", (0.04, 0.006, 0.02), c, "M_LEDGreen", r, (0.36, -d / 2 - 0.016, z + 0.06))
    B("Scanner", (w - 0.04, d - 0.2, 0.12), c, "M_PlasticDark", r, (0, 0.1, 0.98), bevel=0.015)
    B("Lid", (w - 0.06, d - 0.24, 0.03), c, "M_PlasticWhite", r, (0, 0.1, 1.055), bevel=0.008)
    B("OutputTray", (0.42, 0.3, 0.02), c, "M_PlasticDark", r, (0, -d / 2 - 0.12, 0.965))
    B("TrayLip", (0.42, 0.02, 0.05), c, "M_PlasticDark", r, (0, -d / 2 - 0.27, 0.985))
    for sx in (-1, 1):
        B("TraySide", (0.02, 0.3, 0.04), c, "M_PlasticDark", r, (sx * 0.21, -d / 2 - 0.12, 0.98))
    panel = B("Panel", (0.26, 0.14, 0.05), c, "M_DarkMetal", r, (0.3, -d / 2 + 0.02, 1.0), bevel=0.01)
    panel.rotation_euler = (R(25), 0, 0)
    disp = B("Display", (0.16, 0.008, 0.07), c, "M_ScreenDesktop", r, (0.3, -d / 2 - 0.02, 1.015))
    disp.rotation_euler = (R(-65), 0, 0)
    SP("StatusLight", 0.016, c, "M_PlasticGreen", r, (0.42, -d / 2 - 0.05, 0.99), segments=10, rings=6)
    B("Brand", (0.2, 0.004, 0.04), c, "M_InkNavy", r, (-0.3, -d / 2 - 0.002, 0.86))
    return r


@prop
def printer_broken(c):
    """Out-of-order copier with a red warning tape and a paper jam."""
    r = root("PrinterBroken", c)
    B("Body", (0.75, 0.6, 0.85), c, "M_PaintGrey", r, (0, 0, 0.425), bevel=0.02)
    B("Scanner", (0.72, 0.42, 0.1), c, "M_PlasticDark", r, (0, 0.08, 0.9), bevel=0.012)
    B("Tape", (0.78, 0.62, 0.06), c, "M_StampRed", r, (0, 0, 0.6))
    rnd = random.Random(3)
    for i in range(5):
        s = B("Jam", (0.21, 0.297, 0.003), c, "M_Paper", r, (rnd.uniform(-0.25, 0.25), -0.25 + rnd.uniform(-0.1, 0.05), 0.96 + i * 0.01))
        s.rotation_euler = (R(rnd.uniform(-25, 25)), R(rnd.uniform(-20, 20)), R(rnd.uniform(-40, 40)))
    for i in range(4):
        s = B("Floor", (0.21, 0.297, 0.003), c, "M_Paper", r, (rnd.uniform(-0.5, 0.5), -0.55 + rnd.uniform(-0.2, 0.1), 0.002))
        s.rotation_euler = (0, 0, R(rnd.uniform(-60, 60)))
    return r


@prop
def paper_box_stack(c):
    """Copy-paper cartons. The top of the front two (0.3 m) is the 'behind the printer' hiding spot."""
    r = root("PaperBoxStack", c)
    for i, sx in enumerate((-0.17, 0.17)):
        B("Carton", (0.33, 0.46, 0.3), c, "M_Cardboard", r, (sx, 0, 0.15), bevel=0.006)
        B("Band", (0.335, 0.465, 0.05), c, "M_PlasticWhite", r, (sx, 0, 0.2))
        B("Logo", (0.12, 0.004, 0.06), c, "M_InkNavy", r, (sx, -0.233, 0.09))
    B("CartonTop", (0.34, 0.46, 0.3), c, "M_Cardboard", r, (0.2, 0.35, 0.15), bevel=0.006)
    t = B("CartonTop2", (0.34, 0.46, 0.3), c, "M_Cardboard", r, (0.18, 0.33, 0.45), bevel=0.006)
    t.rotation_euler = (0, 0, R(9))
    return r


@prop
def paper_shelf(c):
    r = root("PaperShelf", c)
    w, d, h = 1.2, 0.45, 2.0
    for sx in (-1, 1):
        B("Upright", (0.04, d, h), c, "M_DarkMetal", r, (sx * (w / 2 - 0.02), 0, h / 2))
    rnd = random.Random(31)
    for i in range(5):
        z = 0.08 + i * 0.45
        B("Shelf", (w - 0.04, d, 0.025), c, "M_Steel", r, (0, 0, z))
        if i == 4:
            continue
        x = -w / 2 + 0.1
        while x < w / 2 - 0.15:
            m = rnd.choice(["M_Paper", "M_Paper", "M_Sticky", "M_StickyPink", "M_PlasticBlue", "M_Manila"])
            hh = rnd.choice([0.06, 0.12, 0.18, 0.24])
            B("Ream", (0.22, 0.31, hh), c, m, r, (x + 0.11, -0.03, z + 0.0125 + hh / 2))
            x += 0.24 + rnd.random() * 0.05
    return r


@prop
def worktable(c):
    r = root("Worktable", c)
    B("Top", (1.4, 0.7, 0.04), c, "M_DeskWood", r, (0, 0, 0.88), bevel=0.008)
    for sx in (-1, 1):
        for sy in (-1, 1):
            B("Leg", (0.05, 0.05, 0.86), c, "M_DarkMetal", r, (sx * 0.65, sy * 0.3, 0.43))
    B("Shelf", (1.3, 0.6, 0.02), c, "M_DarkMetal", r, (0, 0, 0.2))
    B("Boxes", (0.5, 0.35, 0.18), c, "M_Cardboard", r, (-0.3, 0, 0.3))
    return r


@prop
def paper_cutter(c):
    r = root("PaperCutter", c)
    B("Base", (0.4, 0.32, 0.02), c, "M_PlasticWhite", r, (0, 0, 0.01), bevel=0.004)
    B("Grid", (0.36, 0.28, 0.002), c, "M_InkNavy", r, (0, 0, 0.021))
    arm = B("Blade", (0.44, 0.03, 0.015), c, "M_Steel", r, (0.0, 0.15, 0.08))
    arm.rotation_euler = (0, R(-25), 0)
    B("Grip", (0.05, 0.04, 0.04), c, "M_PlasticRed", r, (-0.18, 0.15, 0.17))
    return r


@prop
def recycle_bin(c):
    r = root("RecycleBin", c)
    B("Bin", (0.45, 0.4, 0.7), c, "M_PlasticBlue", r, (0, 0, 0.35), bevel=0.02)
    B("Lid", (0.46, 0.41, 0.04), c, "M_PlasticBlue", r, (0, 0, 0.72), bevel=0.012)
    B("Slot", (0.3, 0.06, 0.006), c, "M_PlasticDark", r, (0, 0, 0.742))
    B("Label", (0.2, 0.004, 0.2), c, "M_PlasticWhite", r, (0, -0.202, 0.42))
    for i in range(3):
        B("Sheet", (0.21, 0.297, 0.003), c, "M_Paper", r, (0.04 * i - 0.04, 0.0, 0.76 + 0.004 * i)).rotation_euler = (0, 0, R(20 * i - 15))
    return r


# ============================================================================================ storage

@prop
def cleaning_closet(c):
    """Cleaning closet (st.cleaning). Its middle shelf at 0.88 m holds the spray rack (slot.sprayrack items at
    local x -0.15, 0.15, 0.45 from the anchor, so the closet is 0.9 wide and centred 0.15 m to the right)."""
    r = root("CleaningCloset", c)
    w, d, h = 0.9, 0.5, 1.9
    B("Back", (w, 0.03, h), c, "M_Cabinet", r, (0, d / 2 - 0.015, h / 2))
    for sx in (-1, 1):
        B("Side", (0.03, d, h), c, "M_Cabinet", r, (sx * (w / 2 - 0.015), 0, h / 2))
    B("Roof", (w, d, 0.03), c, "M_Cabinet", r, (0, 0, h - 0.015))
    B("Floor", (w, d, 0.04), c, "M_Cabinet", r, (0, 0, 0.02))
    B("Shelf", (w - 0.06, d - 0.06, 0.025), c, "M_Cabinet", r, (0, 0, 0.865))
    B("ShelfTop", (w - 0.06, d - 0.06, 0.025), c, "M_Cabinet", r, (0, 0, 1.45))
    door = bk.empty("Door", c, (-w / 2, -d / 2, 0), r)
    B("DoorPanel", (w - 0.02, 0.025, h - 0.02), c, "M_Cabinet", door, ((w - 0.02) / 2, 0, h / 2), bevel=0.006)
    B("Vent", (0.4, 0.03, 0.12), c, "M_DarkMetal", door, (w / 2, -0.005, h - 0.3))
    door.rotation_euler = (0, 0, R(-110))
    CY("Bucket", 0.14, 0.26, c, "M_PlasticOrange", r, (0.2, 0.02, 0.17), radius_top=0.16, segments=16)
    CY("MopStick", 0.015, 1.4, c, "M_DeskWood", r, (-0.25, 0.08, 0.75), rotation=(R(-6), R(5), 0), segments=8)
    bk.ico("MopHead", 0.11, c, "M_Paper", r, (-0.26, 0.05, 0.08), scale=(1, 1, 0.6), subdiv=2, jitter=0.12, seed=5)
    for i, m in enumerate(("M_Paper", "M_PlasticBlue", "M_Sticky")):
        CY("Roll", 0.05, 0.1, c, m, r, (-0.25 + i * 0.12, 0.08, 1.52), segments=10)
    B("Label", (0.3, 0.01, 0.08), c, "M_Paper", r, (0, -d / 2 + 0.03, h - 0.12))
    return r


@prop
def fuse_box(c):
    """Wall electrical panel (st.fusebox), 0.5 x 0.6, pivot back centre / vertical centre.
    FuseLever flips down and FuseLight turns red during a power cut."""
    r = root("FuseBox", c)
    B("Box", (0.5, 0.14, 0.6), c, "M_PaintGreyDark", r, (0, -0.07, 0), bevel=0.012)
    B("Door", (0.46, 0.02, 0.56), c, "M_Cabinet", r, (-0.1, -0.2, 0), bevel=0.006).rotation_euler = (0, 0, R(-70))
    for i in range(3):
        for j in range(5):
            B("Breaker", (0.05, 0.03, 0.08), c, "M_PlasticWhite" if (i + j) % 4 else "M_PlasticDark", r, (-0.14 + j * 0.07, -0.15, 0.15 - i * 0.12))
    lever = bk.empty("FuseLever", c, (0.17, -0.15, -0.18), r)
    B("LeverBase", (0.06, 0.03, 0.08), c, "M_DarkMetal", lever, (0, 0, 0))
    B("LeverArm", (0.025, 0.12, 0.025), c, "M_StampRed", lever, (0, -0.06, 0.0))
    SP("FuseLight", 0.014, c, "M_LEDGreen", r, (0.17, -0.145, -0.05), segments=8, rings=4)
    B("Warning", (0.14, 0.005, 0.12), c, "M_Sticky", r, (0.12, -0.143, 0.22))
    B("Conduit", (0.06, 0.05, 0.9), c, "M_PaintGreyDark", r, (-0.18, -0.03, 0.75))
    return r


@prop
def low_pallet(c):
    r = root("LowPallet", c)
    B("Top", (0.8, 0.6, 0.03), c, "M_DeskWood", r, (0, 0, 0.315))
    for sx in (-1, 0, 1):
        B("Block", (0.08, 0.6, 0.27), c, "M_WoodMidB", r, (sx * 0.34, 0, 0.165))
    B("Bottom", (0.8, 0.6, 0.03), c, "M_DeskWood", r, (0, 0, 0.015))
    return r


@prop
def storage_shelf(c):
    r = root("StorageShelf", c)
    w, d, h = 1.2, 0.45, 2.0
    for sx in (-1, 1):
        for sy in (-1, 1):
            B("Post", (0.035, 0.035, h), c, "M_PanelFrame", r, (sx * (w / 2 - 0.02), sy * (d / 2 - 0.02), h / 2))
    rnd = random.Random(41)
    for i in range(4):
        z = 0.1 + i * 0.6
        B("Shelf", (w, d, 0.025), c, "M_Steel", r, (0, 0, z))
        if i == 3:
            continue
        x = -w / 2 + 0.05
        while x < w / 2 - 0.2:
            bw = rnd.uniform(0.25, 0.42)
            bh = rnd.uniform(0.2, 0.42)
            m = rnd.choice(["M_Cardboard", "M_Cardboard", "M_Manila", "M_PlasticBlue", "M_PaintGreyDark"])
            bx = B("Box", (bw, d - 0.08, bh), c, m, r, (x + bw / 2, 0, z + 0.0125 + bh / 2))
            bx.rotation_euler = (0, 0, R(rnd.uniform(-4, 4)))
            x += bw + rnd.uniform(0.02, 0.08)
    return r


@prop
def cardboard_stack(c):
    r = root("CardboardStack", c)
    rnd = random.Random(7)
    z = 0
    for i in range(3):
        s = (0.55 - i * 0.07, 0.45 - i * 0.05, 0.3 - i * 0.03)
        b = B("Box", s, c, "M_Cardboard", r, (rnd.uniform(-0.04, 0.04), rnd.uniform(-0.04, 0.04), z + s[2] / 2), bevel=0.006)
        b.rotation_euler = (0, 0, R(rnd.uniform(-12, 12)))
        B("Tape", (s[0] + 0.004, 0.06, 0.004), c, "M_Cardboard" if i else "M_Sticky", r, (b.location.x, b.location.y, z + s[2] + 0.002)).rotation_euler = b.rotation_euler
        z += s[2]
    return r


@prop
def mop_bucket(c):
    r = root("MopBucket", c)
    B("Bucket", (0.4, 0.3, 0.3), c, "M_DuckYellow", r, (0, 0, 0.17), bevel=0.03)
    B("Wringer", (0.2, 0.25, 0.12), c, "M_DarkMetal", r, (0.08, 0, 0.36))
    for sx in (-1, 1):
        for sy in (-1, 1):
            SP("Wheel", 0.025, c, "M_Rubber", r, (sx * 0.16, sy * 0.11, 0.025), segments=8, rings=5)
    CY("Stick", 0.014, 1.2, c, "M_DeskWood", r, (-0.05, 0.02, 0.85), rotation=(R(12), R(-8), 0), segments=8)
    B("Water", (0.34, 0.24, 0.01), c, "M_Water", r, (-0.03, 0, 0.27))
    return r


@prop
def ladder(c):
    """Step ladder leaning against a wall (front -Y)."""
    r = root("Ladder", c)
    lean = R(12)
    for sx in (-1, 1):
        rail = B("Rail", (0.04, 0.025, 1.9), c, "M_PanelFrame", r, (sx * 0.2, -0.2, 0.93))
        rail.rotation_euler = (-lean, 0, 0)
    for i in range(6):
        z = 0.25 + i * 0.3
        st = B("Step", (0.4, 0.06, 0.02), c, "M_PanelFrame", r, (0, -0.2 + math.sin(lean) * (z - 0.93) * -1 + 0.0, z))
    return r


@prop
def wet_floor_sign(c):
    r = root("WetFloorSign", c)
    for sy in (-1, 1):
        p = B("Panel", (0.3, 0.015, 0.6), c, "M_DuckYellow", r, (0, 0.09 * sy, 0.3))
        p.rotation_euler = (R(-12 * sy), 0, 0)
        ic = B("Icon", (0.12, 0.004, 0.16), c, "M_PlasticDark", r, (0, 0.09 * sy - 0.012 * sy, 0.32))
        ic.rotation_euler = (R(-12 * sy), 0, 0)
    return r


# ============================================================================================ restroom

@prop
def toilet(c):
    """Toilet with a wall-hung cistern behind (lid off: hiding spot at 0.7 m, centred on the cistern)."""
    r = root("Toilet", c)
    bk.lathe("Pedestal", [(0, 0), (0.12, 0), (0.1, 0.25), (0.16, 0.38), (0, 0.38)], c, "M_Porcelain", r, (0, -0.25, 0), segments=18, scale=(1, 1.35, 1))
    CY("Seat", 0.18, 0.04, c, "M_Porcelain", r, (0, -0.28, 0.4), segments=18).scale = (1, 1.3, 1)
    lid = CY("Lid", 0.18, 0.025, c, "M_PlasticWhite", r, (0, -0.06, 0.6), segments=18)
    lid.scale = (1, 1.3, 1)
    lid.rotation_euler = (R(-80), 0, 0)
    # cistern (open top)
    w, d, h0, h1 = 0.5, 0.24, 0.42, 0.8
    B("TankFront", (w, 0.025, h1 - h0), c, "M_Porcelain", r, (0, -d / 2 + 0.0125, (h0 + h1) / 2), bevel=0.008)
    B("TankBack", (w, 0.025, h1 - h0), c, "M_Porcelain", r, (0, d / 2 - 0.0125, (h0 + h1) / 2), bevel=0.008)
    for sx in (-1, 1):
        B("TankSide", (0.025, d, h1 - h0), c, "M_Porcelain", r, (sx * (w / 2 - 0.0125), 0, (h0 + h1) / 2), bevel=0.008)
    B("TankBottom", (w, d, 0.03), c, "M_Porcelain", r, (0, 0, h0 + 0.2))
    B("Water", (w - 0.05, d - 0.05, 0.01), c, "M_Water", r, (0, 0, h0 + 0.22))
    tl = B("TankLid", (w + 0.02, d + 0.02, 0.03), c, "M_Porcelain", r, (0.0, d / 2 + 0.03, h1 + 0.12), bevel=0.008)
    tl.rotation_euler = (R(-78), 0, 0)
    B("Flush", (0.06, 0.02, 0.03), c, "M_Steel", r, (0.15, -d / 2 - 0.01, h1 - 0.05))
    CY("Pipe", 0.03, 0.42, c, "M_Steel", r, (0, -0.05, 0.21), segments=10)
    CY("RollHolder", 0.05, 0.1, c, "M_Paper", r, (0.36, -0.05, 0.6), rotation=(0, R(90), 0), segments=12)
    return r


@prop
def stall_door(c):
    """Open stall door leaf (pivot = hinge), swung into the stall along -Y."""
    r = root("StallDoor", c)
    B("Leaf", (0.03, 0.8, 1.75), c, "M_PaintSlate", r, (0, -0.42, 1.0), bevel=0.006)
    B("Latch", (0.02, 0.06, 0.04), c, "M_Steel", r, (0.02, -0.78, 1.0))
    B("Sign", (0.004, 0.1, 0.1), c, "M_LEDGreen", r, (0.018, -0.75, 1.15))
    return r


@prop
def bathroom_sinks(c):
    r = root("BathroomSinks", c)
    w, d = 1.6, 0.55
    B("Vanity", (w, d, 0.06), c, "M_StoneA", r, (0, 0, 0.85), bevel=0.01)
    B("Apron", (w, 0.03, 0.2), c, "M_PaintSlate", r, (0, -d / 2 + 0.015, 0.75))
    for sx in (-1, 1):
        bk.lathe("Basin", [(0, 0), (0.16, 0.0), (0.2, 0.12), (0.18, 0.12), (0, 0.02)], c, "M_Porcelain", r, (sx * 0.42, -0.02, 0.86), segments=18, scale=(1.1, 0.8, 1))
        CY("Tap", 0.015, 0.18, c, "M_Steel", r, (sx * 0.42, 0.18, 0.97), segments=8)
        CY("Spout", 0.012, 0.12, c, "M_Steel", r, (sx * 0.42, 0.13, 1.05), rotation=(R(90), 0, 0), segments=8)
    B("Soap", (0.07, 0.07, 0.15), c, "M_PlasticGreen", r, (0, 0.15, 0.95), bevel=0.015)
    B("Towels", (0.25, 0.15, 0.08), c, "M_PlasticWhite", r, (0.0, -0.05, 0.92), bevel=0.02)
    for sx in (-1, 1):
        B("Bracket", (0.04, 0.4, 0.04), c, "M_Steel", r, (sx * 0.7, 0.05, 0.8))
    return r


@prop
def mirror(c):
    r = root("Mirror", c)
    B("Frame", (1.5, 0.03, 0.8), c, "M_PanelFrame", r, (0, -0.015, 0), bevel=0.008)
    B("Glass", (1.44, 0.005, 0.74), c, "M_Mirror", r, (0, -0.033, 0))
    return r


@prop
def hand_dryer(c):
    r = root("HandDryer", c)
    B("Body", (0.3, 0.2, 0.32), c, "M_Steel", r, (0, -0.1, 0), bevel=0.04)
    B("Nozzle", (0.08, 0.06, 0.06), c, "M_DarkMetal", r, (0, -0.16, -0.17))
    SP("Sensor", 0.012, c, "M_LEDBlue", r, (0, -0.2, -0.08), segments=6, rings=4)
    return r


# ============================================================================================ server room

def _leds(c, r, name, w, z0, rows, mat, y, seed):
    rnd = random.Random(seed)
    from map_kit import multi_box_obj
    boxes = []
    for i in range(rows):
        z = z0 + i * 0.09
        for k in range(rnd.randint(2, 5)):
            boxes.append(((-w / 2 + 0.06 + rnd.random() * (w - 0.12), y, z), (0.018, 0.006, 0.01), mat))
    return multi_box_obj(name, boxes, c, r)


@prop
def server_rack(c):
    """Server rack 0.6 x 0.9 x 2.0. Leds / LedsB are emissive strips that Unity blinks."""
    r = root("ServerRack", c)
    w, d, h = 0.6, 0.9, 2.0
    B("Frame", (w, d, h), c, "M_PlasticDark", r, (0, 0, h / 2), bevel=0.012)
    B("Front", (w - 0.06, 0.02, h - 0.12), c, "M_DarkMetal", r, (0, -d / 2 - 0.005, h / 2))
    rnd = random.Random(13)
    z = 0.12
    while z < h - 0.2:
        uh = rnd.choice([0.045, 0.09, 0.09, 0.135])
        B("Unit", (w - 0.1, 0.012, uh - 0.008), c, rnd.choice(["M_PlasticDark", "M_Cabinet", "M_Rubber"]), r, (0, -d / 2 - 0.017, z + uh / 2))
        z += uh
    _leds(c, r, "Leds", w, 0.2, 18, "M_LEDGreen", -d / 2 - 0.025, 1)
    _leds(c, r, "LedsB", w, 0.25, 16, "M_LEDAmber", -d / 2 - 0.025, 2)
    B("Top", (w + 0.01, d + 0.01, 0.03), c, "M_DarkMetal", r, (0, 0, h + 0.015))
    B("Vent", (0.4, 0.3, 0.01), c, "M_Rubber", r, (0, 0, h + 0.031))
    return r


@prop
def network_rack(c):
    """Router station (st.router): open network cabinet with the office router on top.
    RouterLight / RouterLight2 go red (blinking) during an internet outage."""
    r = root("NetworkRack", c)
    w, d, h = 0.7, 0.5, 1.25
    B("Cabinet", (w, d, h), c, "M_PlasticDark", r, (0, 0, h / 2), bevel=0.012)
    B("Opening", (w - 0.08, 0.02, h - 0.15), c, "M_Rubber", r, (0, -d / 2 - 0.002, h / 2))
    for i in range(4):
        z = 0.25 + i * 0.22
        B("Patch", (w - 0.14, 0.03, 0.08), c, "M_DarkMetal", r, (0, -d / 2 - 0.015, z))
        for k in range(8):
            m = ("M_CableBlue", "M_CableYellow", "M_PlasticRed", "M_PlasticGreen")[(k + i) % 4]
            cable("Patchcord", [(-0.24 + k * 0.068, -d / 2 - 0.03, z), (-0.24 + k * 0.068, -d / 2 - 0.07, z - 0.05),
                                (-0.2 + k * 0.05, -d / 2 - 0.06, z - 0.15)], 0.005, c, m, r, segments=4)
    door = B("GlassDoor", (w - 0.04, 0.012, h - 0.06), c, "M_VendingGlass", r, (-0.12, -d / 2 - 0.3, h / 2))
    door.rotation_euler = (0, 0, R(-60))
    # router on top
    B("Router", (0.42, 0.26, 0.06), c, "M_PlasticWhite", r, (0, 0.0, h + 0.03), bevel=0.015)
    for sx in (-1, 1):
        a = CY("Antenna", 0.01, 0.26, c, "M_PlasticDark", r, (sx * 0.17, 0.11, h + 0.18), segments=6)
        a.rotation_euler = (0, R(12 * sx), 0)
    SP("RouterLight", 0.014, c, "M_LEDGreen", r, (-0.1, -0.132, h + 0.035), segments=8, rings=4)
    SP("RouterLight2", 0.014, c, "M_LEDGreen", r, (-0.05, -0.132, h + 0.035), segments=8, rings=4)
    for k in range(3):
        SP("RouterLed", 0.008, c, "M_LEDBlue", r, (0.02 + k * 0.04, -0.132, h + 0.035), segments=6, rings=4)
    B("Label", (0.16, 0.004, 0.05), c, "M_Sticky", r, (0.15, -0.132, h + 0.03))
    B("Sign", (0.3, 0.005, 0.12), c, "M_PlasticWhite", r, (0.0, -d / 2 - 0.004, h - 0.08))
    return r


@prop
def cable_tray(c):
    """Overhead ladder tray, 3 m long (pivot = tray centre)."""
    r = root("CableTray", c)
    L = 3.0
    for sy in (-1, 1):
        B("Rail", (L, 0.02, 0.06), c, "M_PanelFrame", r, (0, sy * 0.15, 0))
    for i in range(10):
        B("Rung", (0.02, 0.3, 0.015), c, "M_PanelFrame", r, (-L / 2 + 0.15 + i * 0.3, 0, -0.02))
    for k, m in enumerate(("M_CableBlue", "M_CableYellow", "M_Cable", "M_PlasticRed")):
        cable("Bundle", [(-L / 2, -0.08 + k * 0.05, 0.0), (0, -0.07 + k * 0.05, 0.01), (L / 2, -0.08 + k * 0.05, 0.0)], 0.012, c, m, r, segments=5)
    for sx in (-1, 1):
        CY("Hanger", 0.006, 0.5, c, "M_DarkMetal", r, (sx * 1.2, 0, 0.25), segments=4)
    return r


@prop
def cable_mess(c):
    r = root("CableMess", c)
    rnd = random.Random(19)
    for k, m in enumerate(("M_CableBlue", "M_CableYellow", "M_Cable", "M_Cable", "M_PlasticRed")):
        pts = []
        x, y = rnd.uniform(-0.4, 0.4), rnd.uniform(-0.4, 0.4)
        for i in range(8):
            pts.append((x, y, 0.012 + 0.005 * (i % 2)))
            x += rnd.uniform(-0.25, 0.25)
            y += rnd.uniform(-0.25, 0.25)
        cable("Loop", pts, 0.009, c, m, r, segments=5)
    B("PowerStrip", (0.35, 0.07, 0.04), c, "M_PlasticWhite", r, (0.2, -0.3, 0.02), bevel=0.01)
    SP("StripLed", 0.008, c, "M_LEDRed", r, (0.35, -0.3, 0.042), segments=6, rings=4)
    return r


@prop
def ac_unit(c):
    r = root("AcUnit", c)
    B("Body", (0.95, 0.24, 0.3), c, "M_PlasticWhite", r, (0, -0.12, 0), bevel=0.04)
    B("Vent", (0.8, 0.02, 0.05), c, "M_PaintGreyDark", r, (0, -0.24, -0.11))
    SP("Led", 0.008, c, "M_LEDBlue", r, (0.38, -0.245, -0.05), segments=6, rings=4)
    return r


@prop
def sysadmin_desk(c):
    r = root("SysadminDesk", c)
    B("Top", (1.2, 0.65, 0.04), c, "M_DeskFrame", r, (0, 0, 0.74), bevel=0.008)
    for sx in (-1, 1):
        B("Leg", (0.04, 0.6, 0.72), c, "M_DarkMetal", r, (sx * 0.57, 0, 0.36))
    B("Crt", (0.42, 0.42, 0.38), c, "M_Manila", r, (-0.15, 0.08, 0.95), bevel=0.04)
    B("CrtScreen", (0.32, 0.01, 0.25), c, "M_LEDGreen", r, (-0.15, -0.13, 0.97))
    B("Keyboard", (0.42, 0.14, 0.025), c, "M_Manila", r, (-0.15, -0.2, 0.775), bevel=0.006)
    for i in range(4):
        CY("Can", 0.033, 0.12, c, ("M_PlasticGreen", "M_PlasticRed", "M_PlasticGreen", "M_LEDBlue")[i], r, (0.3 + 0.07 * (i % 2), -0.1 + 0.08 * (i // 2), 0.82), segments=10)
    B("Manual", (0.25, 0.3, 0.06), c, "M_PlasticBlue", r, (0.4, 0.15, 0.79)).rotation_euler = (0, 0, R(15))
    return r


# ============================================================================================ lounge

@prop
def planter(c):
    """Big lounge planter (spot.plant): soil at 0.3 m between tall leaves."""
    r = root("Planter", c)
    w, d, h = 1.1, 0.5, 0.3
    B("Box", (w, d, h - 0.02), c, "M_DarkWood", r, (0, 0, (h - 0.02) / 2), bevel=0.012)
    B("Rim", (w + 0.03, d + 0.03, 0.03), c, "M_WoodDarkB", r, (0, 0, h - 0.015))
    B("Soil", (w - 0.06, d - 0.06, 0.01), c, "M_Soil", r, (0, 0, h - 0.025))
    rnd = random.Random(29)
    for i in range(16):
        x = rnd.choice([-1, 1]) * rnd.uniform(0.22, 0.5)
        y = rnd.uniform(-0.15, 0.15)
        hh = rnd.uniform(0.6, 1.25)
        leaf = B("Leaf", (0.08, 0.02, hh), c, "M_Leaf" if i % 3 else "M_LeafLight", r, (x, y, h + hh / 2 - 0.02))
        leaf.rotation_euler = (R(rnd.uniform(-14, 14)), R(rnd.uniform(-14, 14)), R(rnd.uniform(0, 180)))
    for i in range(5):
        SP("Bush", 0.12, c, "M_LeafLight", r, (rnd.uniform(-0.45, 0.45), rnd.uniform(-0.1, 0.1), h + 0.05), scale=(1, 0.8, 0.6), segments=8, rings=5)
    return r


@prop
def vending_machine(c):
    r = root("VendingMachine", c)
    w, d, h = 0.95, 0.8, 1.9
    B("Body", (w, d, h), c, "M_VendingBody", r, (0, 0, h / 2), bevel=0.02)
    B("Glass", (0.62, 0.02, 1.25), c, "M_VendingGlass", r, (-0.1, -d / 2 - 0.005, 1.15))
    B("Inside", (0.6, 0.02, 1.22), c, "M_PlasticWhite", r, (-0.1, -d / 2 + 0.12, 1.15))
    rnd = random.Random(37)
    for row in range(5):
        z = 0.62 + row * 0.24
        B("Spiral", (0.6, 0.12, 0.01), c, "M_Steel", r, (-0.1, -d / 2 + 0.06, z - 0.02))
        for k in range(5):
            m = rnd.choice(["M_PlasticRed", "M_DuckYellow", "M_PlasticBlue", "M_PlasticGreen", "M_PlasticOrange", "M_Purple"])
            B("Snack", (0.09, 0.08, 0.14), c, m, r, (-0.34 + k * 0.12, -d / 2 + 0.06, z + 0.06))
    B("Keypad", (0.18, 0.02, 0.34), c, "M_PlasticDark", r, (0.34, -d / 2 - 0.005, 1.2))
    B("PadScreen", (0.12, 0.006, 0.05), c, "M_LEDGreen", r, (0.34, -d / 2 - 0.016, 1.33))
    B("Slot", (0.5, 0.02, 0.14), c, "M_PlasticDark", r, (-0.1, -d / 2 - 0.005, 0.28))
    B("Glow", (0.04, 0.02, 1.3), c, "M_LEDBlue", r, (0.24, -d / 2 - 0.006, 1.15))
    B("Header", (w - 0.08, 0.02, 0.2), c, "M_PlasticWhite", r, (0, -d / 2 - 0.005, 1.86))
    return r


@prop
def sofa(c):
    r = root("Sofa", c)
    B("Base", (2.0, 0.85, 0.4), c, "M_Sofa", r, (0, 0, 0.22), bevel=0.05)
    B("Back", (2.0, 0.22, 0.5), c, "M_Sofa", r, (0, 0.32, 0.62), bevel=0.06)
    for sx in (-1, 1):
        B("Arm", (0.22, 0.85, 0.32), c, "M_Sofa", r, (0.95 * sx, 0, 0.52), bevel=0.06)
        B("Cushion", (0.78, 0.6, 0.14), c, "M_Sofa", r, (0.41 * sx, -0.05, 0.48), bevel=0.05)
        for sy in (-1, 1):
            CY("Foot", 0.025, 0.04, c, "M_DarkWood", r, (sx * 0.9, sy * 0.35, 0.02), segments=8)
    p = B("Pillow", (0.38, 0.14, 0.34), c, "M_CushionMustard", r, (-0.62, 0.15, 0.68), bevel=0.06)
    p.rotation_euler = (R(-12), 0, R(10))
    p = B("Pillow2", (0.36, 0.14, 0.32), c, "M_CushionTeal", r, (0.65, 0.15, 0.67), bevel=0.06)
    p.rotation_euler = (R(-12), 0, R(-8))
    return r


@prop
def coffee_table(c):
    r = root("CoffeeTable", c)
    B("Top", (1.0, 0.55, 0.04), c, "M_WoodLightA", r, (0, 0, 0.4), bevel=0.01)
    for sx in (-1, 1):
        for sy in (-1, 1):
            leg = CY("Leg", 0.02, 0.4, c, "M_DarkWood", r, (sx * 0.42, sy * 0.2, 0.2), segments=8)
            leg.rotation_euler = (R(-6 * sy), R(6 * sx), 0)
    for i, m in enumerate(("M_PlasticRed", "M_Paper", "M_Sticky")):
        B("Mag", (0.21, 0.28, 0.008), c, m, r, (-0.2 + 0.03 * i, 0.0, 0.424 + 0.009 * i)).rotation_euler = (0, 0, R(18 * i - 10))
    CY("Bowl", 0.08, 0.05, c, "M_Ceramic", r, (0.25, 0.05, 0.445), segments=14, radius_top=0.1)
    return r


@prop
def armchair(c):
    r = root("Armchair", c)
    B("Base", (0.8, 0.75, 0.36), c, "M_CushionTeal", r, (0, 0, 0.22), bevel=0.05)
    B("Back", (0.8, 0.18, 0.5), c, "M_CushionTeal", r, (0, 0.29, 0.6), bevel=0.06)
    for sx in (-1, 1):
        B("Arm", (0.15, 0.75, 0.25), c, "M_CushionTeal", r, (sx * 0.34, 0, 0.5), bevel=0.05)
        for sy in (-1, 1):
            CY("Foot", 0.02, 0.05, c, "M_DarkWood", r, (sx * 0.34, sy * 0.3, 0.025), segments=8)
    B("Cushion", (0.5, 0.55, 0.12), c, "M_CushionMustard", r, (0, -0.05, 0.45), bevel=0.05)
    return r


@prop
def floor_lamp(c):
    r = root("FloorLamp", c)
    CY("Base", 0.17, 0.03, c, "M_DarkMetal", r, (0, 0, 0.015), segments=18)
    CY("Pole", 0.015, 1.5, c, "M_DarkMetal", r, (0, 0, 0.77), segments=8)
    CY("Shade", 0.22, 0.3, c, "M_RugCream", r, (0, 0, 1.55), segments=18, radius_top=0.16)
    CY("Glow", 0.15, 0.01, c, "M_LampWarm", r, (0, 0, 1.41), segments=16)
    return r


@prop
def foosball(c):
    r = root("Foosball", c)
    L, W, H = 1.4, 0.75, 0.9
    B("Bottom", (L, W, 0.08), c, "M_DarkWood", r, (0, 0, H - 0.2), bevel=0.012)
    for sy in (-1, 1):
        B("SideY", (L, 0.04, 0.2), c, "M_DarkWood", r, (0, sy * (W / 2 - 0.02), H - 0.1), bevel=0.006)
    for sx in (-1, 1):
        B("SideX", (0.04, W, 0.2), c, "M_DarkWood", r, (sx * (L / 2 - 0.02), 0, H - 0.1), bevel=0.006)
        B("Goal", (0.03, 0.2, 0.06), c, "M_Rubber", r, (sx * (L / 2 - 0.045), 0, H - 0.13))
    B("Field", (L - 0.08, W - 0.08, 0.01), c, "M_FoosGreen", r, (0, 0, H - 0.16))
    B("Line", (0.01, W - 0.08, 0.002), c, "M_PlasticWhite", r, (0, 0, H - 0.154))
    for sx in (-1, 1):
        for sy in (-1, 1):
            B("Leg", (0.08, 0.08, H - 0.2), c, "M_DarkWood", r, (sx * (L / 2 - 0.08), sy * (W / 2 - 0.08), (H - 0.2) / 2))
    for i in range(8):
        x = -0.6 + i * 0.172
        CY("Rod", 0.008, W + 0.3, c, "M_Steel", r, (x, 0, H - 0.08), rotation=(R(90), 0, 0), segments=6)
        m = "M_PlasticRed" if i in (0, 1, 3, 5) else "M_PlasticBlue"
        n = (1, 2, 3, 5, 5, 3, 2, 1)[i]
        for k in range(n):
            y = (k - (n - 1) / 2) * (W - 0.15) / max(n, 1)
            B("Man", (0.03, 0.025, 0.09), c, m, r, (x, y, H - 0.1))
        for s in (-1, 1):
            CY("Grip", 0.018, 0.08, c, "M_Rubber", r, (x, s * (W / 2 + 0.17), H - 0.08), rotation=(R(90), 0, 0), segments=8)
    SP("Ball", 0.018, c, "M_PlasticWhite", r, (0.1, 0.05, H - 0.137), segments=8, rings=6)
    return r


@prop
def bean_bag(c):
    r = root("BeanBag", c)
    bk.ico("Bag", 0.42, c, "M_StampRed", r, (0, 0, 0.26), scale=(1.0, 1.0, 0.6), subdiv=2, jitter=0.05, seed=3, smooth=True)
    bk.ico("Dent", 0.25, c, "M_StampRed", r, (0, 0.18, 0.5), scale=(1.2, 0.6, 0.6), subdiv=2, jitter=0.05, seed=4, smooth=True)
    return r


@prop
def wall_tv(c):
    r = root("WallTv", c)
    B("Bezel", (1.4, 0.05, 0.8), c, "M_Tv", r, (0, -0.04, 0), bevel=0.01)
    B("TvScreen", (1.34, 0.005, 0.74), c, "M_TvGlow", r, (0, -0.068, 0))
    B("Mount", (0.3, 0.03, 0.3), c, "M_DarkMetal", r, (0, -0.015, 0))
    B("Console", (0.3, 0.2, 0.06), c, "M_PlasticWhite", r, (0.5, -0.1, -0.62))
    return r


@prop
def games_shelf(c):
    r = root("GamesShelf", c)
    w, d, h = 1.4, 0.35, 1.0
    B("Body", (w, d, 0.03), c, "M_WoodLightA", r, (0, 0, h - 0.015))
    B("Bottom", (w, d, 0.05), c, "M_WoodLightA", r, (0, 0, 0.025))
    B("Mid", (w - 0.04, d, 0.025), c, "M_WoodLightA", r, (0, 0, 0.5))
    for sx in (-1, 0, 1):
        B("Side", (0.03, d, h), c, "M_WoodLightA", r, (sx * (w / 2 - 0.015), 0, h / 2))
    B("Back", (w, 0.02, h), c, "M_WoodLightB", r, (0, d / 2 - 0.01, h / 2))
    rnd = random.Random(43)
    for shelf_z in (0.05, 0.525):
        x = -w / 2 + 0.06
        while x < w / 2 - 0.15:
            if abs(x) < 0.05:
                x += 0.05
                continue
            bh = rnd.uniform(0.05, 0.08)
            stack = rnd.randint(1, 4)
            m_list = ["M_PlasticRed", "M_PlasticBlue", "M_DuckYellow", "M_PlasticGreen", "M_Purple", "M_PlasticOrange"]
            for k in range(stack):
                B("Game", (0.26, 0.26, bh), c, rnd.choice(m_list), r, (x + 0.13, -0.02, shelf_z + bh * (k + 0.5)))
            x += 0.3
    bk.ico("Plant", 0.12, c, "M_LeafLight", r, (0.5, 0, h + 0.1), scale=(1, 1, 0.9), subdiv=1, jitter=0.15, seed=4)
    CY("Pot", 0.08, 0.1, c, "M_Pot", r, (0.5, 0, h + 0.05), segments=12)
    return r


# ============================================================================================ extra dressing

@prop
def mobile_whiteboard(c):
    r = root("MobileWhiteboard", c)
    B("Board", (1.4, 0.03, 0.9), c, "M_Whiteboard", r, (0, 0, 1.35), bevel=0.006)
    B("Frame", (1.44, 0.04, 0.04), c, "M_PanelFrame", r, (0, 0, 1.82))
    B("FrameB", (1.44, 0.04, 0.04), c, "M_PanelFrame", r, (0, 0, 0.88))
    for sx in (-1, 1):
        B("Post", (0.04, 0.04, 1.8), c, "M_PanelFrame", r, (sx * 0.72, 0, 0.92))
        B("Foot", (0.05, 0.5, 0.04), c, "M_PanelFrame", r, (sx * 0.72, 0, 0.07))
        for sy in (-1, 1):
            SP("Wheel", 0.035, c, "M_Rubber", r, (sx * 0.72, sy * 0.22, 0.035), segments=8, rings=5)
    rnd = random.Random(51)
    for i in range(6):
        B("Scribble", (rnd.uniform(0.2, 0.6), 0.004, 0.025), c, rnd.choice(["M_PlasticBlue", "M_PlasticRed", "M_InkNavy"]), r,
          (rnd.uniform(-0.35, 0.35), -0.018, 1.1 + i * 0.1)).rotation_euler = (0, R(rnd.uniform(-6, 6)), 0)
    for i in range(3):
        B("Note", (0.1, 0.004, 0.1), c, "M_Sticky", r, (0.45 - i * 0.12, -0.018, 1.62)).rotation_euler = (0, R(5 - i * 4), 0)
    return r


@prop
def archive_boxes(c):
    r = root("ArchiveBoxes", c)
    rnd = random.Random(61)
    for i in range(3):
        for j in range(3 - (i // 2)):
            b = B("Archive", (0.32, 0.4, 0.27), c, "M_Manila" if (i + j) % 2 else "M_Cardboard", r, (-0.35 + i * 0.34, rnd.uniform(-0.03, 0.03), 0.135 + j * 0.28), bevel=0.006)
            b.rotation_euler = (0, 0, R(rnd.uniform(-5, 5)))
            B("Label", (0.12, 0.004, 0.08), c, "M_Paper", r, (-0.35 + i * 0.34, -0.205, 0.15 + j * 0.28))
    return r


@prop
def old_chairs(c):
    """Stack of retired office chairs."""
    r = root("OldChairs", c)
    for k in range(2):
        z = k * 0.42
        B("Seat", (0.48, 0.46, 0.08), c, "M_PaintGreyDark", r, (0, 0.03 * k, 0.46 + z), bevel=0.03)
        b = B("Back", (0.44, 0.06, 0.48), c, "M_PaintGreyDark", r, (0, -0.24 + 0.03 * k, 0.78 + z), bevel=0.03)
        b.rotation_euler = (R(-10 - 15 * k), 0, R(8 * k))
    CY("Gas", 0.03, 0.4, c, "M_Metal", r, (0, 0, 0.22), segments=8)
    for i in range(5):
        a = 2 * math.pi * i / 5
        leg = B("Leg", (0.3, 0.05, 0.04), c, "M_PlasticDark", r, (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.06))
        leg.rotation_euler = (0, 0, a)
    B("Tag", (0.08, 0.004, 0.05), c, "M_StampRed", r, (0.1, -0.24, 0.3))
    return r


@prop
def supply_cabinet(c):
    r = root("SupplyCabinet", c)
    w, d, h = 1.0, 0.5, 1.9
    B("Body", (w, d, h), c, "M_Cabinet", r, (0, 0, h / 2), bevel=0.012)
    for sx in (-1, 1):
        B("Door", (0.48, 0.015, h - 0.06), c, "M_Cabinet", r, (sx * 0.25, -d / 2 - 0.008, h / 2), bevel=0.005)
        B("Handle", (0.015, 0.025, 0.2), c, "M_Steel", r, (sx * 0.04, -d / 2 - 0.02, 1.0))
    B("Label", (0.3, 0.004, 0.08), c, "M_Sticky", r, (0.25, -d / 2 - 0.017, 1.6))
    B("TopBoxes", (0.8, 0.4, 0.25), c, "M_Cardboard", r, (0, 0, h + 0.125))
    return r
