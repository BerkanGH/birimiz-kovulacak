"""Face expression atlas for the bean characters (players and managers share it).

One 512x512 RGBA page, 4x4 cells of 128 px. Rows 0-1 (top of the image) hold 8 EYE cells, rows 2-3 hold 8 MOUTH
cells. Each cell maps 1:1 onto a small face patch mesh on the head (eyes patch 0.40 x 0.26 m, mouth patch
0.22 x 0.13 m), so shapes are drawn here in patch metres. Transparent pixels show the skin underneath (alpha clip),
which is how eyelids work without per-skin textures.

Unity swaps a cell by changing the material's _BaseMap offset (one material per cell, see ProjectBuilder.Characters):
  eyes cell k  -> offset ((k % 4) / 4, -(k // 4) / 4) relative to cell 0
  mouth cell m -> offset ((m % 4) / 4, -(m // 4) / 4) relative to cell 8
Patch UVs in the FBX point at eye cell 0 and mouth cell 8 (the neutral faces).

Pure numpy (runs inside Blender or any Python with numpy). Writes PNG with zlib, no PIL needed.
"""
import math
import struct
import zlib

import numpy as np

CELL = 128
COLS = 4
SIZE = CELL * COLS
EYE_W, EYE_H = 0.40, 0.28
MOUTH_W, MOUTH_H = 0.22, 0.13
EYES = ["neutral", "happy", "sneaky", "shocked", "angry", "sad", "blink", "laugh"]
MOUTHS = ["neutral", "happy", "smirk", "shocked", "angry", "sad", "talk_a", "talk_e"]
# Inset (in UV units) applied by the mesh generator so mip filtering never samples a neighbour cell.
UV_INSET = 2.0 / SIZE
# The 3D patches are this much larger than the drawing space (bigger eyes like the 2D portraits).
EYE_SCALE = 1.12
MOUTH_SCALE = 1.1


def eye_pt(x, y):
    """Drawing-space eye coordinates -> patch metres on the 3D head."""
    return x * EYE_SCALE, y * EYE_SCALE


def srgb(h):
    h = h.lstrip("#")
    return np.array([int(h[i:i + 2], 16) / 255.0 for i in (0, 2, 4)], dtype=np.float32)


INK = srgb("#1A2240")      # navy ink, matches the 2D portraits
WHITE = srgb("#FBF4E6")
PUPIL = srgb("#121A30")
BROW = srgb("#1A2240")
MOUTH = srgb("#6E1F33")
TONGUE = srgb("#E8697C")
TEETH = srgb("#FFFFFF")
TEAR = srgb("#7CC4FF")


def cell_origin(index):
    """Top-left pixel of a cell in the page (row-major, rows from the top)."""
    return (index % COLS) * CELL, (index // COLS) * CELL


def cell_uv_rect(index):
    """(u0, v0, u1, v1) of a cell in UV space (v up), inset to avoid bleeding."""
    c, r = index % COLS, index // COLS
    u0, u1 = c / COLS, (c + 1) / COLS
    v1, v0 = 1.0 - r / COLS, 1.0 - (r + 1) / COLS
    return u0 + UV_INSET, v0 + UV_INSET, u1 - UV_INSET, v1 - UV_INSET


# ------------------------------------------------------------------------------------------- SDF canvas

class Canvas:
    """A cell in patch metres: x to the viewer's right, y up, origin at the patch centre."""

    def __init__(self, w, h):
        self.w, self.h = w, h
        px = (np.arange(CELL, dtype=np.float32) + 0.5) / CELL
        self.X = (px - 0.5)[None, :].repeat(CELL, 0) * w
        self.Y = (0.5 - px)[:, None].repeat(CELL, 1) * h
        self.aa = max(w, h) / CELL * 0.75
        self.rgb = np.zeros((CELL, CELL, 3), np.float32)  # premultiplied
        self.a = np.zeros((CELL, CELL), np.float32)

    def cover(self, sdf):
        return np.clip(0.5 - sdf / self.aa, 0.0, 1.0)

    def fill(self, sdf, color, opacity=1.0):
        c = self.cover(sdf) * opacity
        self.rgb = self.rgb * (1 - c[..., None]) + color[None, None, :] * c[..., None]
        self.a = self.a * (1 - c) + c

    def erase(self, sdf):
        c = self.cover(sdf)
        self.rgb *= (1 - c[..., None])
        self.a *= (1 - c)

    # shapes ------------------------------------------------------------------------
    def ellipse(self, cx, cy, rx, ry):
        dx, dy = (self.X - cx) / rx, (self.Y - cy) / ry
        k = np.sqrt(dx * dx + dy * dy)
        return (k - 1.0) * min(rx, ry)

    def circle(self, cx, cy, r):
        return np.sqrt((self.X - cx) ** 2 + (self.Y - cy) ** 2) - r

    def halfplane(self, p, n):
        """Positive on the side the normal points to."""
        n = np.array(n, np.float32)
        n = n / np.linalg.norm(n)
        return (self.X - p[0]) * n[0] + (self.Y - p[1]) * n[1]

    def polyline_dist(self, pts):
        d = np.full(self.X.shape, 1e9, np.float32)
        for (ax, ay), (bx, by) in zip(pts, pts[1:]):
            vx, vy = bx - ax, by - ay
            L = vx * vx + vy * vy
            t = np.clip(((self.X - ax) * vx + (self.Y - ay) * vy) / max(L, 1e-12), 0, 1)
            qx, qy = ax + t * vx - self.X, ay + t * vy - self.Y
            d = np.minimum(d, np.sqrt(qx * qx + qy * qy))
        return d

    def stroke(self, pts, width):
        return self.polyline_dist(pts) - width / 2

    def polygon(self, pts):
        """Signed distance to a closed polygon (negative inside), even-odd rule."""
        closed = list(pts) + [pts[0]]
        d = self.polyline_dist(closed)
        inside = np.zeros(self.X.shape, bool)
        for (ax, ay), (bx, by) in zip(closed, closed[1:]):
            cond = (ay > self.Y) != (by > self.Y)
            xint = (bx - ax) * (self.Y - ay) / ((by - ay) if abs(by - ay) > 1e-12 else 1e-12) + ax
            inside ^= cond & (self.X < xint)
        return np.where(inside, -d, d)


def bez(p0, p1, p2, n=18):
    out = []
    for i in range(n + 1):
        t = i / n
        a, b, c = (1 - t) ** 2, 2 * (1 - t) * t, t * t
        out.append((a * p0[0] + b * p1[0] + c * p2[0], a * p0[1] + b * p1[1] + c * p2[1]))
    return out


def mirror(pts):
    return [(-x, y) for x, y in pts]


# ------------------------------------------------------------------------------------------------ eyes

EX, EY = 0.08, -0.008       # eye centre (right eye on screen = +x)
RX, RY = 0.061, 0.077       # sclera radii
OUT = 0.011                 # outline width


def brow(cv, inner, outer, arch, width=0.025, side=1):
    """side=+1 for the screen-right brow; inner/outer given for the screen-right side and mirrored for -1."""
    (ix, iy), (ox, oy) = inner, outer
    mid = ((ix + ox) / 2, (iy + oy) / 2 + arch)
    pts = bez((ix, iy), mid, (ox, oy), 12)
    if side < 0:
        pts = mirror(pts)
    cv.fill(cv.stroke(pts, width), BROW)


def open_eye(cv, side, rx=RX, ry=RY, pupil=0.037, look=(0.0, -0.004), lid=None, hi=True, tear=False):
    """lid: (y_offset_at_inner, y_offset_at_outer) relative to the eye centre; sclera above the line is erased."""
    cx = EX * side
    sclera = cv.ellipse(cx, EY, rx, ry)
    if lid is not None:
        yi, yo = lid
        xi, xo = cx - side * rx, cx + side * rx
        p0, p1 = (xi, EY + yi), (xo, EY + yo)
        n = (-(p1[1] - p0[1]), (p1[0] - p0[0]))
        if n[1] < 0:
            n = (-n[0], -n[1])
        cut = -cv.halfplane(p0, n)   # negative above the lid line
        sclera = np.maximum(sclera, -cut)
    cv.fill(sclera - OUT, INK)
    cv.fill(sclera, WHITE)
    px, py = cx + look[0], EY + look[1]
    pup = np.maximum(cv.circle(px, py, pupil), sclera + 0.002)
    cv.fill(pup, PUPIL)
    if hi:
        cv.fill(np.maximum(cv.circle(px + pupil * 0.38, py + pupil * 0.42, pupil * 0.34), sclera), WHITE)
        cv.fill(np.maximum(cv.circle(px - pupil * 0.35, py - pupil * 0.45, pupil * 0.15), sclera), WHITE)
    if tear:
        tx, ty = cx + side * rx * 0.55, EY - ry * 0.95
        drop = np.minimum(cv.circle(tx, ty - 0.012, 0.012),
                          cv.polygon([(tx - 0.0105, ty - 0.008), (tx + 0.0105, ty - 0.008), (tx, ty + 0.014)]))
        cv.fill(drop - 0.003, INK)
        cv.fill(drop, TEAR)


def arc_eye(cv, side, up=True, width=0.016, span=0.05, height=0.03):
    cx = EX * side
    if up:   # happy closed "^" (n-shape)
        pts = bez((cx - span, EY - height * 0.4), (cx, EY + height * 1.6), (cx + span, EY - height * 0.4))
    else:    # relaxed closed "u"
        pts = bez((cx - span, EY + height * 0.2), (cx, EY - height * 1.2), (cx + span, EY + height * 0.2))
    cv.fill(cv.stroke(pts, width), INK)


def chevron_eye(cv, side, width=0.017):
    cx = EX * side
    s = -side  # points toward the nose
    pts = [(cx - s * 0.035, EY + 0.032), (cx + s * 0.03, EY), (cx - s * 0.035, EY - 0.032)]
    cv.fill(cv.stroke(pts, width), INK)


def sweat(cv, x, y, r=0.016):
    drop = np.minimum(cv.circle(x, y, r), cv.polygon([(x - r * 0.92, y + r * 0.3), (x + r * 0.92, y + r * 0.3), (x, y + r * 2.4)]))
    cv.fill(drop - 0.003, INK)
    cv.fill(drop, TEAR)
    cv.fill(np.maximum(cv.circle(x - r * 0.3, y + r * 0.2, r * 0.28), drop), WHITE)


def draw_eyes(name):
    cv = Canvas(EYE_W, EYE_H)
    B_IN, B_OUT = (0.03, 0.098), (0.122, 0.104)
    for side in (-1, 1):
        if name == "neutral":
            open_eye(cv, side)
            brow(cv, B_IN, B_OUT, 0.012, side=side)
        elif name == "happy":
            arc_eye(cv, side, up=True, span=0.054, height=0.034, width=0.02)
            brow(cv, (0.03, 0.11), (0.122, 0.112), 0.016, side=side)
        elif name == "sneaky":
            # half-lidded side-eye: both pupils look to screen-left, one brow cocked
            open_eye(cv, side, lid=(0.014, 0.026), look=(-0.026, -0.018), pupil=0.033)
            if side > 0:
                brow(cv, (0.03, 0.084), (0.124, 0.12), 0.004, side=side)
            else:
                brow(cv, (0.03, 0.066), (0.124, 0.064), -0.006, side=side)
        elif name == "shocked":
            open_eye(cv, side, rx=RX * 1.1, ry=RY * 1.1, pupil=0.018, look=(0, 0))
            brow(cv, (0.03, 0.122), (0.122, 0.118), 0.016, side=side, width=0.021)
        elif name == "angry":
            open_eye(cv, side, lid=(-0.004, 0.044), look=(0, -0.012), pupil=0.035)
            brow(cv, (0.024, 0.06), (0.126, 0.11), -0.004, side=side, width=0.029)
        elif name == "sad":
            open_eye(cv, side, lid=(0.04, -0.004), look=(0, -0.022), pupil=0.034, tear=(side > 0))
            brow(cv, (0.026, 0.114), (0.124, 0.078), 0.006, side=side, width=0.022)
        elif name == "blink":
            arc_eye(cv, side, up=False, height=0.024, span=0.054, width=0.02)
            brow(cv, B_IN, B_OUT, 0.012, side=side)
        elif name == "laugh":
            chevron_eye(cv, side, width=0.021)
            brow(cv, (0.03, 0.114), (0.122, 0.118), 0.018, side=side)
    if name == "shocked":
        sweat(cv, 0.172, 0.07)
    return cv


# ---------------------------------------------------------------------------------------------- mouths

def mouth_interior(cv, shape, teeth_top=None, tongue=None):
    cv.fill(shape - OUT, INK)
    cv.fill(shape, MOUTH)
    if tongue is not None:
        tx, ty, trx, try_ = tongue
        cv.fill(np.maximum(cv.ellipse(tx, ty, trx, try_), shape), TONGUE)
    if teeth_top is not None:
        band = np.maximum(shape, -cv.halfplane((0, teeth_top), (0, 1)))
        cv.fill(band, TEETH)


def draw_mouth(name):
    cv = Canvas(MOUTH_W, MOUTH_H)
    W = 0.0115
    if name == "neutral":
        cv.fill(cv.stroke(bez((-0.042, 0.008), (0, -0.022), (0.042, 0.008)), W), INK)
    elif name == "happy":
        top = bez((-0.062, 0.016), (0, 0.006), (0.062, 0.016), 10)
        bot = bez((0.062, 0.016), (0.04, -0.07), (-0.062, 0.016), 16)[1:-1]
        shape = cv.polygon(top + bot)
        mouth_interior(cv, shape, teeth_top=0.0, tongue=(0.0, -0.036, 0.03, 0.016))
    elif name == "smirk":
        cv.fill(cv.stroke(bez((-0.05, 0.0), (0.0, -0.022), (0.048, 0.018)), W), INK)
        cv.fill(cv.stroke([(0.04, 0.026), (0.056, 0.012)], W * 0.8), INK)
    elif name == "shocked":
        shape = cv.ellipse(0.0, -0.006, 0.026, 0.036)
        mouth_interior(cv, shape, tongue=(0.0, -0.034, 0.018, 0.012))
    elif name == "angry":
        top = bez((-0.056, 0.0), (0.0, 0.026), (0.056, 0.0), 12)
        bot = bez((0.056, -0.006), (0.0, -0.004), (-0.056, -0.006), 12)
        shape = cv.polygon(top + bot)
        cv.fill(shape - OUT, INK)
        cv.fill(shape, TEETH)
        cv.fill(np.maximum(cv.stroke([(-0.06, 0.004), (0.06, 0.004)], 0.004), shape), INK)
        for x in (-0.03, -0.01, 0.01, 0.03):
            cv.fill(np.maximum(cv.stroke([(x, -0.02), (x, 0.03)], 0.0035), shape), INK)
    elif name == "sad":
        cv.fill(cv.stroke(bez((-0.04, -0.018), (0, 0.022), (0.04, -0.018)), W), INK)
    elif name == "talk_a":
        top = bez((-0.04, 0.012), (0, 0.022), (0.04, 0.012), 10)
        bot = bez((0.04, 0.012), (0.03, -0.06), (-0.04, 0.012), 14)[1:-1]
        shape = cv.polygon(top + bot)
        mouth_interior(cv, shape, teeth_top=0.006, tongue=(0.0, -0.03, 0.024, 0.014))
    elif name == "talk_e":
        shape = cv.ellipse(0.0, 0.0, 0.036, 0.016)
        mouth_interior(cv, shape, teeth_top=0.006)
    return cv


# ------------------------------------------------------------------------------------------------ page

def build_atlas():
    """Returns an (SIZE, SIZE, 4) float32 straight-alpha RGBA page, row 0 = top of the image."""
    page = np.zeros((SIZE, SIZE, 4), np.float32)
    cells = [draw_eyes(n) for n in EYES] + [draw_mouth(n) for n in MOUTHS]
    for i, cv in enumerate(cells):
        x0, y0 = cell_origin(i)
        a = cv.a
        rgb = np.where(a[..., None] > 1e-4, cv.rgb / np.maximum(a[..., None], 1e-4), 0)
        page[y0:y0 + CELL, x0:x0 + CELL, :3] = rgb
        page[y0:y0 + CELL, x0:x0 + CELL, 3] = a
    # Bleed colour into transparent texels (keeps edges dark/correct under bilinear + mips).
    _dilate_rgb(page)
    return page


def _dilate_rgb(page, steps=6):
    rgb, a = page[..., :3], page[..., 3]
    known = a > 0.02
    for _ in range(steps):
        acc = np.zeros_like(rgb)
        cnt = np.zeros(a.shape, np.float32)
        for dy, dx in ((1, 0), (-1, 0), (0, 1), (0, -1)):
            sh_rgb = np.roll(np.roll(rgb, dy, 0), dx, 1)
            sh_k = np.roll(np.roll(known, dy, 0), dx, 1)
            acc += sh_rgb * sh_k[..., None]
            cnt += sh_k
        grow = (~known) & (cnt > 0)
        rgb[grow] = acc[grow] / cnt[grow][..., None]
        known = known | grow
    rgb[~known] = INK


def write_png(path, rgba):
    """rgba: (h, w, 4) float 0..1, row 0 = top."""
    h, w = rgba.shape[:2]
    data = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        return c + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


def preview_sheet(page, skins, scale=2):
    """Every cell composited over each skin colour (rows = skins), for eyeballing."""
    rows = []
    for hexcol in skins:
        bg = srgb(hexcol)
        row = []
        for i in range(16):
            x0, y0 = cell_origin(i)
            c = page[y0:y0 + CELL, x0:x0 + CELL]
            a = (c[..., 3:4] > 0.5).astype(np.float32)  # alpha clip like Unity
            row.append(c[..., :3] * a + bg[None, None, :] * (1 - a))
        rows.append(np.concatenate(row, 1))
    img = np.concatenate(rows, 0)
    img = img.repeat(scale, 0).repeat(scale, 1) if scale > 1 else img
    return np.concatenate([img, np.ones(img.shape[:2] + (1,), np.float32)], 2)


if __name__ == "__main__":
    import os
    import sys
    out = sys.argv[1] if len(sys.argv) > 1 else "FaceAtlas.png"
    pg = build_atlas()
    write_png(out, pg)
    write_png(os.path.splitext(out)[0] + "_preview.png", preview_sheet(pg, ["#4A90E2", "#F5C842", "#9D6BDB"], 1))
    print("wrote", out)
