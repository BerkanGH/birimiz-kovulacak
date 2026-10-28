"""v2 props: open office cubicles + desk clutter, manager office, meeting room, reception, safety/global props.

Conventions (see bk_common): metres, Blender axes, front = -Y, pivot at the floor centre. Wall-mounted props: pivot
at the back centre, vertically centred, unless the docstring says otherwise. Named sub-objects in map_kit.DRIVEN
are driven by Unity scripts.
"""
import math
import random

import bk_common as bk
from map_kit import prop, root, uv_plane, cable, multi_box_obj, R

B = bk.box
CY = bk.cylinder
SP = bk.sphere


# ============================================================================================ cubicles & desk kit

def _panel(c, r, length, h=1.15, notes=0, seed=1):
    t = 0.05
    B("Fabric", (length - 0.08, t, h - 0.1), c, "M_PanelFabric", r, (0, 0, 0.05 + (h - 0.1) / 2))
    B("Rail", (length, 0.1, 0.035), c, "M_PanelFrame", r, (0, 0, h - 0.0175), bevel=0.008)
    for sx in (-1, 1):
        B("Post", (0.04, 0.07, h), c, "M_PanelFrame", r, (sx * (length / 2 - 0.02), 0, h / 2), bevel=0.006)
    B("Kick", (length - 0.08, 0.06, 0.06), c, "M_PanelFrame", r, (0, 0, 0.03))
    for sx in (-1, 1):
        B("Foot", (0.06, 0.34, 0.03), c, "M_PanelFrame", r, (sx * (length / 2 - 0.12), 0, 0.015), bevel=0.006)
    rnd = random.Random(seed)
    for i in range(notes):
        m = rnd.choice(["M_Paper", "M_Sticky", "M_StickyPink", "M_Manila"])
        n = B("Pinned", (rnd.uniform(0.12, 0.22), 0.004, rnd.uniform(0.12, 0.26)), c, m, r,
              (rnd.uniform(-length / 2 + 0.25, length / 2 - 0.25), -0.028, rnd.uniform(0.85, 1.0)))
        n.rotation_euler = (0, R(rnd.uniform(-8, 8)), 0)


@prop
def cubicle_back(c):
    r = root("CubicleBack", c)
    _panel(c, r, 2.06, notes=3, seed=3)
    return r


@prop
def cubicle_side(c):
    r = root("CubicleSide", c)
    _panel(c, r, 1.68, notes=1, seed=8)
    return r


@prop
def nameplate_rail(c):
    """Bracket on top of the cubicle back panel that holds the swappable nameplate (pivot = panel top)."""
    r = root("NameplateRail", c)
    B("Base", (0.4, 0.13, 0.012), c, "M_DarkMetal", r, (0, 0, 0.006), bevel=0.003)
    for sy in (-1, 1):
        B("Lip", (0.4, 0.008, 0.025), c, "M_DarkMetal", r, (0, sy * 0.061, 0.0125))
    return r


@prop
def wastebasket(c):
    r = root("Wastebasket", c)
    bk.lathe("Bin", [(0, 0), (0.12, 0), (0.15, 0.32), (0.14, 0.32), (0.11, 0.015), (0, 0.015)], c, "M_PlasticDark", r, segments=16)
    bk.ico("Paper", 0.05, c, "M_Paper", r, (0.02, 0.01, 0.27), subdiv=1, jitter=0.3, seed=2)
    return r


@prop
def pc_tower(c):
    r = root("PcTower", c)
    B("Case", (0.2, 0.42, 0.42), c, "M_PlasticDark", r, (0, 0, 0.23), bevel=0.012)
    B("Front", (0.18, 0.01, 0.38), c, "M_DarkMetal", r, (0, -0.215, 0.23))
    SP("PcLed", 0.008, c, "M_LEDBlue", r, (0.05, -0.222, 0.4), segments=8, rings=4)
    for sx in (-1, 1):
        B("Foot", (0.04, 0.38, 0.02), c, "M_Rubber", r, (sx * 0.07, 0, 0.01))
    return r


@prop
def unplugged_cable(c):
    """Power cable lying on the floor next to a desk (shown while the PC is unplugged)."""
    r = root("UnpluggedCable", c)
    pts = [(-0.05, 0.55, 0.012), (0.0, 0.35, 0.012), (0.12, 0.2, 0.012), (0.05, 0.02, 0.012), (-0.12, -0.08, 0.012),
           (-0.2, -0.25, 0.012), (-0.05, -0.38, 0.012), (0.18, -0.42, 0.012)]
    cable("Cord", pts, 0.009, c, "M_Cable", r)
    plug = B("Plug", (0.05, 0.08, 0.035), c, "M_PlasticDark", r, (0.22, -0.43, 0.018), bevel=0.006)
    plug.rotation_euler = (0, 0, R(70))
    for sx in (-1, 1):
        CY("Pin", 0.004, 0.03, c, "M_Steel", r, (0.26 + 0.012 * sx, -0.43 + 0.012, 0.018), rotation=(0, R(90), R(-20)), segments=6)
    return r


@prop
def empty_desk_card(c):
    """Manila tent card on an unassigned desk ("BOŞ MASA")."""
    r = root("EmptyDeskCard", c)
    bk.prism_nameplate("Card", 0.22, 0.09, 0.09, c, "M_Manila", r)
    B("Stamp", (0.12, 0.002, 0.03), c, "M_StampRed", r, (0, -0.024, 0.045)).rotation_euler = (R(-26), 0, R(-6))
    B("StampB", (0.12, 0.002, 0.03), c, "M_StampRed", r, (0, 0.024, 0.045)).rotation_euler = (R(26), 0, R(6))
    return r


@prop
def sticky_notes(c):
    """Sticky notes stuck around a monitor (placed at the computer offset)."""
    r = root("StickyNotes", c)
    for i, (x, z, m, rot) in enumerate(((0.3, 0.42, "M_Sticky", 8), (-0.3, 0.2, "M_StickyPink", -6), (0.31, 0.25, "M_Sticky", -4))):
        n = B("Note", (0.065, 0.003, 0.065), c, m, r, (x, 0.008, z))
        n.rotation_euler = (0, R(rot), R(18 if x > 0 else -18))
    return r


@prop
def folder_stack(c):
    r = root("FolderStack", c)
    z = 0.0
    for i, m in enumerate(("M_Manila", "M_PlasticBlue", "M_Manila", "M_PlasticRed", "M_Manila")):
        f = B("Folder", (0.24, 0.32, 0.018), c, m, r, (0.01 * ((i * 7) % 3 - 1), 0.012 * ((i * 5) % 3 - 1), z + 0.009))
        f.rotation_euler = (0, 0, R((i * 13) % 11 - 5))
        z += 0.019
    B("Sheets", (0.21, 0.29, 0.01), c, "M_Paper", r, (0.02, 0, z + 0.005)).rotation_euler = (0, 0, R(12))
    return r


@prop
def figurine(c):
    r = root("Figurine", c)
    B("Base", (0.06, 0.06, 0.012), c, "M_PlasticDark", r, (0, 0, 0.006))
    B("Body", (0.045, 0.035, 0.05), c, "M_PlasticOrange", r, (0, 0, 0.037), bevel=0.006)
    B("Head", (0.05, 0.04, 0.04), c, "M_PlasticWhite", r, (0, 0, 0.083), bevel=0.008)
    for sx in (-1, 1):
        SP("Eye", 0.006, c, "M_LEDBlue", r, (0.012 * sx, -0.021, 0.086), segments=6, rings=4)
    CY("Antenna", 0.002, 0.03, c, "M_Metal", r, (0, 0, 0.115), segments=4)
    SP("Tip", 0.006, c, "M_PlasticRed", r, (0, 0, 0.132), segments=6, rings=4)
    return r


@prop
def headphones(c):
    r = root("Headphones", c)
    bk.torus("Band", 0.08, 0.008, c, "M_PlasticDark", r, (0, 0, 0.02), rotation=(R(90), 0, 0), arc=math.pi, major_seg=12, minor_seg=5)
    for sx in (-1, 1):
        CY("Cup", 0.04, 0.03, c, "M_PlasticRed", r, (0.08 * sx, 0, 0.02), rotation=(0, R(90), 0), segments=12)
    return r


@prop
def mini_trophy(c):
    r = root("MiniTrophy", c)
    B("Base", (0.07, 0.07, 0.03), c, "M_DarkWood", r, (0, 0, 0.015))
    CY("Stem", 0.008, 0.04, c, "M_Gold", r, (0, 0, 0.05), segments=8)
    bk.lathe("Cup", [(0, 0), (0.012, 0), (0.035, 0.05), (0.032, 0.05), (0.01, 0.006), (0, 0.006)], c, "M_Gold", r, (0, 0, 0.07), segments=12)
    return r


@prop
def coat_rack(c):
    r = root("CoatRack", c)
    CY("Pole", 0.02, 1.75, c, "M_DarkWood", r, (0, 0, 0.875), segments=10)
    for i in range(3):
        a = 2 * math.pi * i / 3
        leg = B("Leg", (0.35, 0.03, 0.03), c, "M_DarkWood", r, (math.cos(a) * 0.15, math.sin(a) * 0.15, 0.05))
        leg.rotation_euler = (0, R(-15), a)
    for i in range(4):
        a = 2 * math.pi * i / 4 + 0.4
        hook = CY("Hook", 0.012, 0.16, c, "M_DarkWood", r, (math.cos(a) * 0.07, math.sin(a) * 0.07, 1.68), rotation=(R(55) * math.sin(a), R(-55) * math.cos(a), 0), segments=6)
    coat = bk.ico("Coat", 0.18, c, "M_PlasticBlue", r, (0.08, 0.05, 1.3), scale=(0.9, 0.55, 2.0), subdiv=2, jitter=0.06, seed=4)
    bk.ico("Scarf", 0.07, c, "M_PlasticRed", r, (-0.08, -0.03, 1.55), scale=(0.9, 0.7, 1.9), subdiv=1, jitter=0.1, seed=6)
    return r


@prop
def standing_table(c):
    r = root("StandingTable", c)
    CY("Top", 0.38, 0.035, c, "M_DeskWood", r, (0, 0, 1.06), segments=24, cap_bevel=0.006)
    CY("Pole", 0.035, 1.04, c, "M_DarkMetal", r, (0, 0, 0.53), segments=12)
    CY("Base", 0.24, 0.025, c, "M_DarkMetal", r, (0, 0, 0.0125), segments=20)
    return r


@prop
def printer_small(c):
    r = root("PrinterSmall", c)
    B("Cabinet", (0.6, 0.45, 0.6), c, "M_Cabinet", r, (0, 0, 0.3), bevel=0.01)
    B("DoorLine", (0.004, 0.005, 0.52), c, "M_DarkMetal", r, (0, -0.226, 0.3))
    B("Body", (0.46, 0.38, 0.22), c, "M_PlasticWhite", r, (0, 0.02, 0.71), bevel=0.02)
    B("Tray", (0.3, 0.16, 0.012), c, "M_PlasticDark", r, (0, -0.22, 0.72))
    B("Sheet", (0.21, 0.14, 0.004), c, "M_Paper", r, (0, -0.22, 0.728))
    B("Panel", (0.12, 0.01, 0.05), c, "M_PlasticDark", r, (0.15, -0.17, 0.79))
    return r


@prop
def shredder(c):
    r = root("Shredder", c)
    B("Bin", (0.4, 0.3, 0.55), c, "M_PlasticDark", r, (0, 0, 0.275), bevel=0.012)
    B("Head", (0.42, 0.32, 0.1), c, "M_DarkMetal", r, (0, 0, 0.6), bevel=0.012)
    B("Slot", (0.3, 0.012, 0.006), c, "M_Rubber", r, (0, 0, 0.652))
    B("Window", (0.25, 0.006, 0.2), c, "M_Water", r, (0, -0.152, 0.3))
    for i in range(5):
        B("Strip", (0.008, 0.2, 0.002), c, "M_Paper", r, (-0.06 + 0.03 * i, 0.04, 0.66)).rotation_euler = (R(80), 0, 0)
    return r


# ============================================================================================ manager office

@prop
def trophy_shelf(c):
    r = root("TrophyShelf", c)
    B("Cabinet", (1.6, 0.4, 0.86), c, "M_DarkWood", r, (0, 0, 0.43), bevel=0.012)
    for sx in (-1, 0, 1):
        B("Door", (0.5, 0.012, 0.74), c, "M_WoodDarkA", r, (sx * 0.52, -0.205, 0.43), bevel=0.004)
        B("Knob", (0.015, 0.015, 0.06), c, "M_Gold", r, (sx * 0.52 + 0.2, -0.215, 0.5))
    B("Top", (1.64, 0.44, 0.03), c, "M_WoodDarkB", r, (0, 0, 0.875), bevel=0.006)
    for i, (x, s) in enumerate(((-0.55, 1.0), (-0.15, 1.35), (0.3, 0.9))):
        B("Plinth", (0.12 * s, 0.12 * s, 0.05 * s), c, "M_DarkWood", r, (x, 0.02, 0.89 + 0.025 * s))
        bk.lathe("Cup", [(0, 0), (0.02 * s, 0), (0.02 * s, 0.05 * s), (0.07 * s, 0.14 * s), (0.065 * s, 0.14 * s), (0.015 * s, 0.06 * s), (0, 0.06 * s)],
                 c, "M_Gold", r, (x, 0.02, 0.89 + 0.05 * s), segments=16)
        for sx in (-1, 1):
            bk.torus("Handle", 0.025 * s, 0.005 * s, c, "M_Gold", r, (x + sx * 0.07 * s, 0.02, 0.89 + 0.14 * s), rotation=(R(90), 0, 0), major_seg=10, minor_seg=4)
    f = B("Plaque", (0.3, 0.02, 0.24), c, "M_DarkWood", r, (0.62, 0.12, 1.0), bevel=0.006)
    f.rotation_euler = (R(-10), 0, 0)
    B("PlaqueGold", (0.22, 0.004, 0.14), c, "M_Gold", r, (0.62, 0.105, 1.0)).rotation_euler = (R(-10), 0, 0)
    return r


@prop
def globe(c):
    r = root("Globe", c)
    for i in range(3):
        a = 2 * math.pi * i / 3
        leg = B("Leg", (0.03, 0.03, 0.7), c, "M_DarkWood", r, (math.cos(a) * 0.17, math.sin(a) * 0.17, 0.35))
        leg.rotation_euler = (R(-12) * math.sin(a), R(12) * math.cos(a), 0)
    bk.torus("Ring", 0.25, 0.012, c, "M_Gold", r, (0, 0, 0.72), major_seg=24, minor_seg=5)
    SP("Sphere", 0.22, c, "M_PlasticBlue", r, (0, 0, 0.92), segments=20, rings=12)
    rnd = random.Random(12)
    for i in range(5):
        a, b = rnd.uniform(0, 6.28), rnd.uniform(-0.8, 0.8)
        SP("Land", 0.09, c, "M_LeafLight", r, (math.cos(a) * math.cos(b) * 0.19, math.sin(a) * math.cos(b) * 0.19, 0.92 + math.sin(b) * 0.19),
           scale=(1.0, 1.0, 0.6), segments=8, rings=5)
    bk.torus("Meridian", 0.235, 0.008, c, "M_Gold", r, (0, 0, 0.92), rotation=(R(90), R(23), 0), major_seg=24, minor_seg=4)
    return r


@prop
def leather_sofa(c):
    r = root("LeatherSofa", c)
    B("Base", (1.7, 0.8, 0.38), c, "M_Leather", r, (0, 0, 0.24), bevel=0.05)
    B("Back", (1.7, 0.22, 0.48), c, "M_Leather", r, (0, 0.29, 0.62), bevel=0.06)
    for sx in (-1, 1):
        B("Arm", (0.2, 0.8, 0.5), c, "M_Leather", r, (sx * 0.8, 0, 0.45), bevel=0.06)
        CY("ArmRoll", 0.1, 0.8, c, "M_Leather", r, (sx * 0.8, 0, 0.7), rotation=(R(90), 0, 0), segments=12)
        for sy in (-1, 1):
            CY("Foot", 0.03, 0.06, c, "M_Gold", r, (sx * 0.78, sy * 0.33, 0.03), segments=8)
    for sx in (-1, 1):
        B("Cushion", (0.68, 0.56, 0.12), c, "M_Leather", r, (sx * 0.35, -0.06, 0.48), bevel=0.05)
    for i in range(6):
        SP("Button", 0.012, c, "M_WoodDarkB", r, (-0.6 + i * 0.24, 0.17, 0.72), segments=6, rings=4)
    return r


@prop
def desk_trophy(c):
    r = root("DeskTrophy", c)
    B("Base", (0.1, 0.1, 0.04), c, "M_DarkWood", r, (0, 0, 0.02), bevel=0.005)
    bk.lathe("Cup", [(0, 0), (0.018, 0), (0.018, 0.05), (0.06, 0.14), (0.055, 0.14), (0.012, 0.06), (0, 0.06)], c, "M_Gold", r, (0, 0, 0.04), segments=16)
    B("Label", (0.07, 0.004, 0.02), c, "M_Gold", r, (0, -0.051, 0.02))
    return r


# ============================================================================================ meeting room

@prop
def projector_screen(c):
    """Wall-mounted pull-down screen, pivot at the wall foot (back centre at floor level)."""
    r = root("ProjectorScreen", c)
    B("Housing", (2.6, 0.12, 0.12), c, "M_PlasticWhite", r, (0, -0.08, 2.52), bevel=0.02)
    B("Canvas", (2.4, 0.01, 1.45), c, "M_ProjectorScreen", r, (0, -0.1, 1.77))
    B("Border", (2.42, 0.008, 0.04), c, "M_PlasticDark", r, (0, -0.104, 1.04))
    B("BottomBar", (2.44, 0.03, 0.03), c, "M_DarkMetal", r, (0, -0.1, 1.03))
    CY("Pull", 0.006, 0.18, c, "M_PlasticDark", r, (0, -0.1, 0.92), segments=6)
    return r


@prop
def projector(c):
    r = root("Projector", c)
    B("Body", (0.3, 0.24, 0.1), c, "M_PlasticWhite", r, (0, 0, 0.055), bevel=0.02)
    CY("Lens", 0.04, 0.05, c, "M_PlasticDark", r, (0.07, -0.125, 0.06), rotation=(R(90), 0, 0), segments=14)
    CY("LensGlass", 0.03, 0.006, c, "M_LEDBlue", r, (0.07, -0.152, 0.06), rotation=(R(90), 0, 0), segments=12)
    for sx in (-1, 1):
        CY("Foot", 0.012, 0.012, c, "M_Rubber", r, (sx * 0.1, 0, 0.006), segments=6)
    cable("Cord", [(-0.1, 0.12, 0.04), (-0.12, 0.3, 0.0), (-0.3, 0.6, 0.0)], 0.006, c, "M_Cable", r)
    return r


@prop
def hide_cabinet(c):
    """Meeting room sideboard with an open compartment (hiding spot shelf top at 0.6 m)."""
    r = root("HideCabinet", c)
    w, d, h = 0.95, 0.7, 0.92
    B("Top", (w, d, 0.04), c, "M_DarkWood", r, (0, 0, h - 0.02), bevel=0.008)
    for sx in (-1, 1):
        B("Side", (0.03, d, h - 0.04), c, "M_DarkWood", r, (sx * (w / 2 - 0.015), 0, (h - 0.04) / 2))
    B("Back", (w, 0.02, h - 0.04), c, "M_DarkWood", r, (0, d / 2 - 0.01, (h - 0.04) / 2))
    B("Bottom", (w - 0.06, d, 0.06), c, "M_DarkWood", r, (0, 0, 0.03))
    B("Shelf", (w - 0.06, d - 0.04, 0.03), c, "M_WoodMidA", r, (0, 0.0, 0.585))
    B("FileBoxes", (0.5, 0.3, 0.22), c, "M_Manila", r, (0.12, 0.12, 0.17))
    B("Binder", (0.06, 0.28, 0.24), c, "M_PlasticRed", r, (-0.3, 0.12, 0.18))
    door = bk.empty("Door", c, (w / 2 - 0.03, -d / 2, 0.06), r)
    B("DoorPanel", (0.45, 0.02, 0.8), c, "M_WoodMidB", door, (-0.225, -0.01, 0.4), bevel=0.004)
    B("DoorKnob", (0.02, 0.025, 0.06), c, "M_Gold", door, (-0.4, -0.03, 0.45))
    door.rotation_euler = (0, 0, R(-62))
    return r


@prop
def credenza(c):
    r = root("Credenza", c)
    B("Body", (1.8, 0.45, 0.78), c, "M_DarkWood", r, (0, 0, 0.41), bevel=0.01)
    for i in range(3):
        x = -0.6 + i * 0.6
        B("Door", (0.57, 0.015, 0.66), c, "M_WoodMidA", r, (x, -0.227, 0.41), bevel=0.004)
        B("Pull", (0.12, 0.02, 0.015), c, "M_DarkMetal", r, (x, -0.24, 0.7))
    for sx in (-1, 1):
        B("Leg", (0.04, 0.04, 0.04), c, "M_DarkMetal", r, (sx * 0.85, 0, 0.02))
    return r


@prop
def water_bottles(c):
    r = root("WaterBottles", c)
    B("Tray", (0.42, 0.26, 0.015), c, "M_Steel", r, (0, 0, 0.0075), bevel=0.004)
    for i in range(3):
        CY("Bottle", 0.035, 0.2, c, "M_Water", r, (-0.12 + i * 0.08, 0.05, 0.115), segments=12)
        CY("Cap", 0.02, 0.025, c, "M_PlasticBlue", r, (-0.12 + i * 0.08, 0.05, 0.228), segments=10)
    for i in range(3):
        CY("Glass", 0.03, 0.09, c, "M_VendingGlass", r, (-0.12 + i * 0.1, -0.07, 0.06), segments=12, radius_top=0.034)
    return r


@prop
def pendant_bar(c):
    """Linear pendant over the meeting table (pivot = lamp centre)."""
    r = root("PendantBar", c)
    B("Bar", (1.8, 0.12, 0.06), c, "M_DarkMetal", r, (0, 0, 0.0), bevel=0.012)
    B("Glow", (1.72, 0.09, 0.01), c, "M_LampWarm", r, (0, 0, -0.032))
    for sx in (-1, 1):
        CY("Wire", 0.003, 0.55, c, "M_DarkMetal", r, (sx * 0.7, 0, 0.3), segments=4)
    return r


# ============================================================================================ reception

@prop
def reception_desk(c):
    """Front counter faces -Y (the visitors); work surface behind. 3 m wide."""
    r = root("ReceptionDesk", c)
    w = 3.0
    B("Front", (w, 0.08, 1.04), c, "M_RailGreen", r, (0, -0.36, 0.52), bevel=0.012)
    B("FrontBand", (w + 0.01, 0.085, 0.08), c, "M_DeskWood", r, (0, -0.36, 0.86))
    B("Ledge", (w + 0.06, 0.32, 0.04), c, "M_DeskWood", r, (0, -0.3, 1.05), bevel=0.01)
    B("Work", (w - 0.1, 0.6, 0.04), c, "M_DeskFrame", r, (0, 0.05, 0.74), bevel=0.008)
    for sx in (-1, 1):
        B("Side", (0.06, 0.8, 1.04), c, "M_RailGreen", r, (sx * (w / 2 - 0.03), 0, 0.52), bevel=0.01)
    B("Pedestal", (0.45, 0.55, 0.7), c, "M_DeskFrame", r, (1.0, 0.08, 0.36), bevel=0.008)
    for i in range(3):
        B("Drawer", (0.42, 0.01, 0.2), c, "M_DeskWood", r, (1.0, 0.36, 0.15 + i * 0.22))
    B("Kick", (w, 0.06, 0.06), c, "M_Baseboard", r, (0, -0.4, 0.03))
    # work surface clutter
    B("Monitor", (0.5, 0.03, 0.3), c, "M_PlasticDark", r, (-0.4, 0.2, 1.04), bevel=0.008).rotation_euler = (0, 0, R(180))
    B("MonitorNeck", (0.05, 0.04, 0.16), c, "M_PlasticDark", r, (-0.4, 0.22, 0.84))
    B("Keyboard", (0.4, 0.13, 0.02), c, "M_PlasticDark", r, (-0.4, 0.0, 0.77), bevel=0.005)
    B("Bell", (0.07, 0.07, 0.015), c, "M_Gold", r, (0.9, -0.3, 1.078))
    SP("BellDome", 0.035, c, "M_Gold", r, (0.9, -0.3, 1.085), scale=(1, 1, 0.7), segments=12, rings=6)
    B("Brochures", (0.2, 0.14, 0.03), c, "M_Manila", r, (0.4, -0.3, 1.085)).rotation_euler = (0, 0, R(8))
    B("PenHolder", (0.06, 0.06, 0.08), c, "M_DarkMetal", r, (-1.1, -0.28, 1.11))
    return r


@prop
def desk_phone(c):
    """The office phone (st.phone): Handset shakes and RingLight blinks while it rings."""
    r = root("DeskPhone", c)
    B("PhoneBase", (0.22, 0.2, 0.06), c, "M_PlasticDark", r, (0, 0, 0.03), bevel=0.014)
    pad = B("Keypad", (0.09, 0.09, 0.006), c, "M_PlasticWhite", r, (0.045, -0.03, 0.063))
    pad.rotation_euler = (R(-8), 0, 0)
    B("Display", (0.08, 0.035, 0.004), c, "M_ScreenDesktop", r, (0.045, 0.05, 0.062))
    SP("RingLight", 0.014, c, "M_PlasticRed", r, (-0.075, -0.08, 0.064), segments=8, rings=6)
    hs = bk.empty("Handset", c, (-0.05, 0.0, 0.085), r)
    B("HandsetGrip", (0.05, 0.2, 0.03), c, "M_PlasticDark", hs, (0, 0, 0), bevel=0.01)
    B("Ear", (0.06, 0.06, 0.04), c, "M_PlasticDark", hs, (0, 0.08, -0.008), bevel=0.012)
    B("Mouth", (0.06, 0.06, 0.04), c, "M_PlasticDark", hs, (0, -0.08, -0.008), bevel=0.012)
    cable("Cord", [(-0.1, -0.06, 0.04), (-0.13, -0.09, 0.02), (-0.12, -0.13, 0.0), (-0.08, -0.12, 0.0)], 0.005, c, "M_Cable", r)
    return r


@prop
def mail_station(c):
    """Mail room counter (slot.mailroom items lie on the front half of the top at 0.95 m) + pigeonholes."""
    r = root("MailStation", c)
    w, d = 1.4, 0.85
    B("Counter", (w, d, 0.92), c, "M_RailGreen", r, (0, 0, 0.46), bevel=0.01)
    B("Top", (w + 0.04, d + 0.03, 0.03), c, "M_DeskWood", r, (0, -0.01, 0.935), bevel=0.006)
    for i in range(2):
        B("Door", (0.66, 0.012, 0.76), c, "M_PaintOfficeGreen", r, (-0.34 + i * 0.68, -0.43, 0.46), bevel=0.004)
        B("Pull", (0.12, 0.02, 0.015), c, "M_Steel", r, (-0.34 + i * 0.68, -0.445, 0.78))
    # pigeonholes on the back half
    bd = 0.35
    y0 = d / 2 - bd / 2
    B("CubbyBack", (w, 0.02, 1.0), c, "M_DeskWood", r, (0, d / 2 - 0.01, 1.45))
    for i in range(5):
        x = -w / 2 + i * (w / 4)
        B("CubbyV", (0.02, bd, 1.0), c, "M_DeskWood", r, (max(min(x, w / 2 - 0.01), -w / 2 + 0.01), y0, 1.45))
    for j in range(4):
        B("CubbyH", (w, bd, 0.02), c, "M_DeskWood", r, (0, y0, 0.96 + j * 0.325))
    rnd = random.Random(17)
    for i in range(4):
        for j in range(3):
            if rnd.random() < 0.6:
                x = -w / 2 + (i + 0.5) * (w / 4)
                m = rnd.choice(["M_Envelope", "M_Paper", "M_Manila"])
                env = B("Letter", (0.22, 0.2, 0.05 + rnd.random() * 0.05), c, m, r, (x + rnd.uniform(-0.04, 0.04), y0 + 0.02, 0.97 + j * 0.325 + 0.04))
                env.rotation_euler = (0, R(rnd.uniform(-8, 8)), 0)
            B("Tag", (0.08, 0.004, 0.025), c, "M_Paper", r, (-w / 2 + (i + 0.5) * (w / 4), y0 - bd / 2 - 0.002, 0.995 + j * 0.325))
    B("SortTray", (0.4, 0.3, 0.05), c, "M_DarkMetal", r, (0.42, -0.18, 0.975))
    return r


@prop
def waiting_bench(c):
    r = root("WaitingBench", c)
    B("Beam", (1.9, 0.06, 0.06), c, "M_DarkMetal", r, (0, 0.05, 0.3))
    for sx in (-1, 1):
        B("Leg", (0.06, 0.5, 0.3), c, "M_DarkMetal", r, (sx * 0.85, 0.05, 0.15))
    for i in range(3):
        x = -0.62 + i * 0.62
        B("Seat", (0.56, 0.48, 0.07), c, "M_RailGreen", r, (x, 0, 0.44), bevel=0.025)
        b = B("Back", (0.56, 0.06, 0.45), c, "M_RailGreen", r, (x, 0.24, 0.72), bevel=0.025)
        b.rotation_euler = (R(-8), 0, 0)
    for sx in (-1, 0, 1):
        if sx != 0:
            B("Arm", (0.04, 0.4, 0.04), c, "M_DarkMetal", r, (sx * 0.93, 0, 0.62))
    return r


@prop
def coffee_table_small(c):
    r = root("CoffeeTableSmall", c)
    B("Top", (0.8, 0.5, 0.035), c, "M_DeskWood", r, (0, 0, 0.4), bevel=0.008)
    B("Shelf", (0.72, 0.42, 0.02), c, "M_DarkWood", r, (0, 0, 0.12))
    for sx in (-1, 1):
        for sy in (-1, 1):
            CY("Leg", 0.018, 0.4, c, "M_DarkMetal", r, (sx * 0.36, sy * 0.21, 0.2), segments=8)
    return r


@prop
def magazines(c):
    r = root("Magazines", c)
    for i, m in enumerate(("M_PlasticRed", "M_Paper", "M_PlasticBlue", "M_Sticky")):
        mg = B("Mag", (0.21, 0.28, 0.008), c, m, r, (0.03 * i, 0.01 * i, 0.004 + i * 0.009))
        mg.rotation_euler = (0, 0, R(i * 17 - 20))
    return r


@prop
def umbrella_stand(c):
    r = root("UmbrellaStand", c)
    CY("Can", 0.13, 0.55, c, "M_DarkMetal", r, (0, 0, 0.275), segments=16)
    for i, m in enumerate(("M_PlasticRed", "M_InkNavy", "M_Sticky")):
        u = CY("Umbrella", 0.022, 0.9, c, m, r, (0.03 * (i - 1), 0.02 * (i % 2), 0.5), segments=8, radius_top=0.008)
        u.rotation_euler = (R(6 * (i - 1)), R(5 * (1 - i)), 0)
        bk.torus("Handle", 0.03, 0.007, c, "M_DarkWood", r, (0.03 * (i - 1), 0.02 * (i % 2) + 0.03, 0.97), rotation=(R(90), 0, 0), arc=math.pi, major_seg=8, minor_seg=4)
    return r


@prop
def fish_tank(c):
    r = root("FishTank", c)
    B("Cabinet", (1.0, 0.4, 0.75), c, "M_DarkWood", r, (0, 0, 0.375), bevel=0.01)
    B("Water", (0.94, 0.34, 0.44), c, "M_Water", r, (0, 0, 0.98))
    B("Gravel", (0.92, 0.32, 0.05), c, "M_Cork", r, (0, 0, 0.785))
    B("Lid", (0.98, 0.38, 0.04), c, "M_PlasticDark", r, (0, 0, 1.22))
    B("LidGlow", (0.9, 0.3, 0.005), c, "M_LampWarm", r, (0, 0, 1.198))
    for sx in (-1, 1):
        for sy in (-1, 1):
            B("Edge", (0.02, 0.02, 0.46), c, "M_PlasticDark", r, (sx * 0.48, sy * 0.18, 0.99))
    rnd = random.Random(5)
    for i in range(5):
        SP("Fish", 0.03, c, "M_Fish", r, (rnd.uniform(-0.35, 0.35), rnd.uniform(-0.08, 0.08), rnd.uniform(0.88, 1.1)), scale=(1.6, 0.5, 0.9), segments=8, rings=5)
    for i in range(4):
        w = B("Weed", (0.03, 0.03, rnd.uniform(0.15, 0.3)), c, "M_LeafLight", r, (rnd.uniform(-0.4, 0.4), 0.08, 0.9))
        w.rotation_euler = (0, R(rnd.uniform(-12, 12)), 0)
    return r


# ============================================================================================ safety / global

@prop
def security_camera(c):
    """Wall/corner camera; pivot = mount point. RecLight is a red LED blinking while recording."""
    r = root("SecurityCamera", c)
    B("Mount", (0.1, 0.04, 0.12), c, "M_PlasticWhite", r, (0, 0.0, 0.0), bevel=0.01)
    arm = CY("Arm", 0.018, 0.16, c, "M_PlasticWhite", r, (0, -0.08, 0.0), rotation=(R(90), 0, 0), segments=8)
    head = bk.empty("Head", c, (0, -0.17, 0.0), r)
    head.rotation_euler = (R(-28), 0, 0)
    B("Body", (0.12, 0.26, 0.11), c, "M_PlasticWhite", head, (0, -0.06, 0), bevel=0.02)
    B("Hood", (0.14, 0.2, 0.02), c, "M_PlasticWhite", head, (0, -0.1, 0.065))
    CY("Lens", 0.035, 0.03, c, "M_PlasticDark", head, (0, -0.2, 0), rotation=(R(90), 0, 0), segments=12)
    CY("Glass", 0.026, 0.006, c, "M_Tv", head, (0, -0.216, 0), rotation=(R(90), 0, 0), segments=10)
    SP("RecLight", 0.009, c, "M_LEDRed", head, (0.045, -0.19, 0.035), segments=8, rings=4)
    return r


@prop
def exit_sign(c):
    r = root("ExitSign", c)
    B("Box", (0.38, 0.06, 0.16), c, "M_PlasticWhite", r, (0, -0.03, 0), bevel=0.008)
    B("Face", (0.34, 0.005, 0.12), c, "M_ExitGreen", r, (0, -0.062, 0))
    B("Man", (0.03, 0.004, 0.08), c, "M_PlasticWhite", r, (-0.1, -0.066, 0)).rotation_euler = (0, R(-15), 0)
    B("Arrow", (0.14, 0.004, 0.025), c, "M_PlasticWhite", r, (0.06, -0.066, 0))
    return r


@prop
def fire_extinguisher(c):
    """Pivot at the wall foot; extinguisher hangs at 0.3-0.85 m with a sign above."""
    r = root("FireExtinguisher", c)
    B("Bracket", (0.12, 0.03, 0.12), c, "M_DarkMetal", r, (0, -0.015, 0.7))
    CY("Tank", 0.075, 0.5, c, "M_StampRed", r, (0, -0.1, 0.55), segments=16, cap_bevel=0.02)
    CY("Valve", 0.03, 0.06, c, "M_DarkMetal", r, (0, -0.1, 0.83), segments=10)
    B("HandleTop", (0.12, 0.03, 0.015), c, "M_DarkMetal", r, (0.02, -0.1, 0.87))
    cable("Hose", [(0.03, -0.1, 0.84), (0.09, -0.12, 0.78), (0.09, -0.16, 0.5), (0.06, -0.17, 0.4)], 0.012, c, "M_Rubber", r)
    B("Label", (0.1, 0.004, 0.12), c, "M_PlasticWhite", r, (0, -0.177, 0.55))
    B("Sign", (0.22, 0.01, 0.22), c, "M_StampRed", r, (0, -0.005, 1.15))
    B("SignIcon", (0.06, 0.004, 0.14), c, "M_PlasticWhite", r, (0, -0.011, 1.15))
    return r


@prop
def emergency_light(c):
    """Wall box with a red lamp that only glows during a power cut (EmLamp)."""
    r = root("EmergencyLight", c)
    B("Box", (0.32, 0.08, 0.12), c, "M_PlasticWhite", r, (0, -0.04, 0), bevel=0.01)
    B("EmLamp", (0.26, 0.02, 0.06), c, "M_LEDOff", r, (0, -0.085, -0.01), bevel=0.008)
    SP("Status", 0.006, c, "M_LEDGreen", r, (0.13, -0.082, 0.04), segments=6, rings=4)
    return r


@prop
def mail_envelope(c):
    """Mail item (variant 'mail'): the Stripe takes the addressee's player colour."""
    r = root("MailEnvelope", c)
    B("Envelope", (0.26, 0.17, 0.012), c, "M_Envelope", r, (0, 0, 0.006), bevel=0.002)
    bk.prism_nameplate("Flap", 0.26, 0.17, 0.004, c, "M_Envelope", r, (0, 0, 0.012))
    B("Stripe", (0.262, 0.04, 0.016), c, "M_NameplateStripe", r, (0, -0.055, 0.008))
    B("StampMark", (0.04, 0.05, 0.002), c, "M_StampRed", r, (0.09, 0.045, 0.017))
    B("Address", (0.1, 0.03, 0.002), c, "M_InkNavy", r, (-0.03, 0.02, 0.017))
    return r
