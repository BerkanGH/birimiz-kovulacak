"""Modular office architecture kit (assembled by the Unity ProjectBuilder from the floor plan in code).

Run:  blender -b --factory-startup -P Art/Scripts/build_architecture.py [-- --previews]
Outputs: Art/Blender/architecture.blend, Game/Assets/Game/Art/Models/Architecture/<Piece>.fbx

All wall pieces: length along X centred on the origin, thickness along Y, bottom at z = 0, height 2.6 m.
Floor tiles: 2 x 2 m, top surface at z = 0.
"""
import bpy
import math
import os
import random
import shutil
import sys

sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import bk_common as bk  # noqa: E402

H = 2.6
T = 0.16
PIECES = []


def piece(fn):
    PIECES.append(fn)
    return fn


def baseboards(c, r, length):
    for sy in (-1, 1):
        bk.box("Baseboard", (length, 0.02, 0.1), c, "M_Baseboard", r, (0, sy * (T / 2 + 0.01), 0.05))


@piece
def wall_1m(c):
    r = bk.empty("Wall_1m", c)
    bk.box("Wall", (1.0, T, H), c, "M_Wall", r, (0, 0, H / 2))
    baseboards(c, r, 1.0)
    return r


@piece
def wall_accent_1m(c):
    r = bk.empty("WallAccent_1m", c)
    bk.box("Wall", (1.0, T, H), c, "M_WallAccent", r, (0, 0, H / 2))
    baseboards(c, r, 1.0)
    return r


@piece
def wall_window_2m(c):
    r = bk.empty("WallWindow_2m", c)
    sill, top = 0.85, 2.25
    bk.box("Lower", (2.0, T, sill), c, "M_Wall", r, (0, 0, sill / 2))
    bk.box("Upper", (2.0, T, H - top), c, "M_Wall", r, (0, 0, (H + top) / 2))
    f = 0.06
    bk.box("FrameBottom", (2.0, T + 0.04, f), c, "M_WindowFrame", r, (0, 0, sill + f / 2))
    bk.box("FrameTop", (2.0, T + 0.02, f), c, "M_WindowFrame", r, (0, 0, top - f / 2))
    for sx in (-1, 0, 1):
        bk.box("Mullion", (f, T + 0.02, top - sill), c, "M_WindowFrame", r, (sx * (1.0 - f / 2) if sx else 0, 0, (sill + top) / 2))
    bk.box("Glass", (2.0, 0.02, top - sill), c, "M_Glass", r, (0, 0, (sill + top) / 2))
    bk.box("Sill", (2.0, 0.22, 0.03), c, "M_PlasticWhite", r, (0, -0.06, sill + 0.075))
    baseboards(c, r, 2.0)
    return r


@piece
def glass_1m(c):
    r = bk.empty("Glass_1m", c)
    for sx in (-1, 1):
        bk.box("Post", (0.05, 0.08, H), c, "M_WindowFrame", r, (sx * 0.475, 0, H / 2))
    bk.box("TopRail", (1.0, 0.08, 0.06), c, "M_WindowFrame", r, (0, 0, H - 0.03))
    bk.box("BottomRail", (1.0, 0.08, 0.08), c, "M_WindowFrame", r, (0, 0, 0.04))
    bk.box("Glass", (0.9, 0.015, H - 0.14), c, "M_Glass", r, (0, 0, H / 2))
    bk.box("Band", (0.9, 0.018, 0.12), c, "M_PlasticWhite", r, (0, 0, 1.15))
    return r


@piece
def door_frame(c):
    r = bk.empty("DoorFrame_1m6", c)
    for sx in (-1, 1):
        bk.box("Jamb", (0.08, 0.12, 2.25), c, "M_WindowFrame", r, (sx * 0.84, 0, 1.125))
    bk.box("Header", (1.76, 0.12, 0.08), c, "M_WindowFrame", r, (0, 0, 2.29))
    bk.box("Transom", (1.76, 0.015, H - 2.33), c, "M_Glass", r, (0, 0, (H + 2.33) / 2))
    bk.box("TopRail", (1.76, 0.08, 0.06), c, "M_WindowFrame", r, (0, 0, H - 0.03))
    return r


@piece
def pillar(c):
    r = bk.empty("Pillar", c)
    bk.box("Pillar", (0.36, 0.36, H), c, "M_Wall", r, (0, 0, H / 2), bevel=0.02)
    bk.box("Base", (0.4, 0.4, 0.1), c, "M_Baseboard", r, (0, 0, 0.05))
    return r


def floor_tile(c, name, material):
    r = bk.empty(name, c)
    bk.box("Tile", (2.0, 2.0, 0.06), c, material, r, (0, 0, -0.03), bevel=0.006, segments=1)
    return r


@piece
def floor_carpet(c):
    return floor_tile(c, "Floor_Carpet", "M_Floor")


@piece
def floor_tile_kitchen(c):
    return floor_tile(c, "Floor_Tile", "M_FloorTile")


@piece
def floor_wood(c):
    return floor_tile(c, "Floor_Wood", "M_FloorMeeting")


@piece
def ceiling_lamp(c):
    r = bk.empty("PendantLamp", c)
    bk.cylinder("Cable", 0.006, 0.6, c, "M_PlasticDark", r, (0, 0, 0.3), segments=6)
    bk.cylinder("Shade", 0.35, 0.12, c, "M_PlasticDark", r, (0, 0, 0.0), radius_top=0.16, segments=24)
    bk.cylinder("Glow", 0.3, 0.01, c, "M_LampWarm", r, (0, 0, -0.06), segments=24)
    return r


@piece
def skyline(c):
    """Backdrop seen through the windows: sky panel + low-poly city blocks (length 40 m along X)."""
    r = bk.empty("Skyline", c)
    bk.box("Sky", (60.0, 0.1, 30.0), c, "M_Sky", r, (0, 8.0, 6.0))
    rnd = random.Random(5)
    x = -28.0
    mats = ["M_Cabinet", "M_PlasticWhite", "M_WallAccent", "M_Baseboard", "M_DeskFrame"]
    while x < 28.0:
        w = rnd.uniform(2.0, 5.0)
        h = rnd.uniform(4.0, 16.0)
        d = rnd.uniform(2.0, 4.0)
        bk.box("Building", (w, d, h), c, rnd.choice(mats), r, (x + w / 2, rnd.uniform(1.0, 5.0), h / 2 - 3.0))
        x += w + rnd.uniform(0.3, 1.5)
    bk.box("Ground", (60.0, 12.0, 0.1), c, "M_LeafLight", r, (0, 2.0, -3.0))
    return r


def main():
    bk.reset_scene()
    coll = bk.collection("Architecture")
    roots = [fn(bk.collection(fn.__name__, coll)) for fn in PIECES]
    out_dir = os.path.join(bk.UNITY_MODELS, "Architecture")
    os.makedirs(os.path.join(bk.EXPORT_DIR, "Architecture"), exist_ok=True)
    for r in roots:
        path = os.path.join(out_dir, r.name + ".fbx")
        bk.export_fbx(r, path)
        shutil.copy2(path, os.path.join(bk.EXPORT_DIR, "Architecture", r.name + ".fbx"))
    for i, r in enumerate(roots[:-1]):
        r.location = (i * 3.0, 0, 0)
    roots[-1].location = (0, 40, 0)
    bk.save_blend("architecture.blend")
    if "--previews" in sys.argv:
        bk.setup_preview_render((1400, 600))
        bk.render_preview(os.path.join(bk.PREVIEW_DIR, "architecture.png"), target=(15, 0, 1.2), distance=24, elevation_deg=18, azimuth_deg=-10, lens=60)
    print("ARCH_DONE", len(roots))


main()
