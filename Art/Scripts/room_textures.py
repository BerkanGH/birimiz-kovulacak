"""v3 room textures (pure Python, no dependencies): surface detail maps and monitor states for the private office and
the meeting room.

Writes Game/Assets/Game/Art/Textures/Rooms/*.png. The Unity builder (ProjectBuilder.Rooms.cs) assigns them:
  Wood.png, Planks.png, Carpet.png, Plaster.png   greyscale detail (base map; the palette colour tints it, mean ~0.9)
  Facade.png, FacadeLit.png                       skyline buildings: windows by day / lit windows (emission) at dusk
  Silhouette.png                                  soft passer-by shape seen through the frosted door glass (RGBA)
  ScreenDesktop/Alert/Offline/Coffee.png          the office monitor (base + emission map of the screen material)
UVs on the room meshes are box-projected in metres (build_rooms.py), so 1 texture tile = 1 m unless the material
says otherwise.

Run:  python -I Art/Scripts/room_textures.py   (build_rooms.py also calls it)
"""
import math
import os
import random
import struct
import sys
import zlib

sys.dont_write_bytecode = True
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Game", "Assets", "Game", "Art", "Textures", "Rooms")


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


class Img:
    def __init__(self, w, h, bg=(0, 0, 0), alpha=False):
        self.w, self.h, self.alpha = w, h, alpha
        self.c = 4 if alpha else 3
        px = list(bg) + ([255] if alpha and len(bg) == 3 else [])
        self.buf = bytearray(bytes(px[:self.c]) * (w * h))

    def set(self, x, y, col):
        if 0 <= x < self.w and 0 <= y < self.h:
            i = (y * self.w + x) * self.c
            self.buf[i:i + self.c] = bytes(col[:self.c]) if len(col) >= self.c else bytes(list(col) + [255])

    def get(self, x, y):
        i = (y * self.w + x) * self.c
        return tuple(self.buf[i:i + self.c])

    def blend(self, x, y, col, a):
        if not (0 <= x < self.w and 0 <= y < self.h) or a <= 0:
            return
        i = (y * self.w + x) * self.c
        for k in range(3):
            self.buf[i + k] = int(self.buf[i + k] * (1 - a) + col[k] * a)

    def rect(self, x0, y0, x1, y1, col, a=1.0):
        for y in range(max(0, int(y0)), min(self.h, int(y1))):
            for x in range(max(0, int(x0)), min(self.w, int(x1))):
                if a >= 1.0:
                    self.set(x, y, col)
                else:
                    self.blend(x, y, col, a)

    def rrect(self, x0, y0, x1, y1, r, col, a=1.0):
        """Rounded rectangle with 1 px anti-aliasing."""
        for y in range(max(0, int(y0)), min(self.h, int(math.ceil(y1)))):
            for x in range(max(0, int(x0)), min(self.w, int(math.ceil(x1)))):
                cx = min(max(x + 0.5, x0 + r), x1 - r)
                cy = min(max(y + 0.5, y0 + r), y1 - r)
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy) - r
                cov = min(1.0, max(0.0, 0.5 - d))
                if cov > 0:
                    self.blend(x, y, col, cov * a)

    def circle(self, cx, cy, r, col, a=1.0, ring=0.0):
        for y in range(max(0, int(cy - r - 2)), min(self.h, int(cy + r + 2))):
            for x in range(max(0, int(cx - r - 2)), min(self.w, int(cx + r + 2))):
                d = math.hypot(x + 0.5 - cx, y + 0.5 - cy)
                cov = min(1.0, max(0.0, r + 0.5 - d))
                if ring > 0:
                    cov = min(cov, min(1.0, max(0.0, d - (r - ring) + 0.5)))
                if cov > 0:
                    self.blend(x, y, col, cov * a)

    def line(self, x0, y0, x1, y1, w, col, a=1.0):
        minx, maxx = int(min(x0, x1) - w - 1), int(max(x0, x1) + w + 2)
        miny, maxy = int(min(y0, y1) - w - 1), int(max(y0, y1) + w + 2)
        dx, dy = x1 - x0, y1 - y0
        ll = max(1e-6, dx * dx + dy * dy)
        for y in range(max(0, miny), min(self.h, maxy)):
            for x in range(max(0, minx), min(self.w, maxx)):
                px, py = x + 0.5, y + 0.5
                t = max(0.0, min(1.0, ((px - x0) * dx + (py - y0) * dy) / ll))
                d = math.hypot(px - (x0 + dx * t), py - (y0 + dy * t))
                cov = min(1.0, max(0.0, w / 2 + 0.5 - d))
                if cov > 0:
                    self.blend(x, y, col, cov * a)

    def vgrad(self, c0, c1, y0=0, y1=None):
        y1 = self.h if y1 is None else y1
        for y in range(y0, y1):
            t = (y - y0) / max(1, (y1 - y0 - 1))
            col = tuple(int(c0[k] + (c1[k] - c0[k]) * t) for k in range(3))
            self.rect(0, y, self.w, y + 1, col)

    def save(self, path):
        stride = self.w * self.c
        raw = b"".join(b"\x00" + bytes(self.buf[y * stride:(y + 1) * stride]) for y in range(self.h))

        def chunk(tag, data):
            return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        ctype = 6 if self.alpha else 2
        png = (b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", self.w, self.h, 8, ctype, 0, 0, 0))
               + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b""))
        with open(path, "wb") as f:
            f.write(png)


# ------------------------------------------------------------------------------------------------ noise

class Noise:
    """Tileable value noise (period = grid cells)."""

    def __init__(self, period, seed):
        rnd = random.Random(seed)
        self.p = period
        self.v = [[rnd.random() for _ in range(period)] for _ in range(period)]

    def at(self, u, v):
        """u, v in cells (wraps)."""
        x0, y0 = int(math.floor(u)), int(math.floor(v))
        fx, fy = u - x0, v - y0
        fx = fx * fx * (3 - 2 * fx)
        fy = fy * fy * (3 - 2 * fy)
        p = self.p
        a = self.v[y0 % p][x0 % p]
        b = self.v[y0 % p][(x0 + 1) % p]
        c = self.v[(y0 + 1) % p][x0 % p]
        d = self.v[(y0 + 1) % p][(x0 + 1) % p]
        return (a * (1 - fx) + b * fx) * (1 - fy) + (c * (1 - fx) + d * fx) * fy


def fbm(noises, u, v):
    s, amp, tot = 0.0, 1.0, 0.0
    for n in noises:
        s += n.at(u * n.p, v * n.p) * amp
        tot += amp
        amp *= 0.5
    return s / tot


def grey(v):
    g = max(0, min(255, int(v * 255)))
    return (g, g, g)


# ------------------------------------------------------------------------------------------------ surfaces

def wood(size=512):
    """Long grain along U (x). Values ~0.78..1.0 so the palette colour stays the mean."""
    im = Img(size, size)
    warp = [Noise(4, 11), Noise(8, 12)]
    fine = Noise(64, 13)
    knots = Noise(6, 14)
    for y in range(size):
        v = y / size
        for x in range(size):
            u = x / size
            w = fbm(warp, u, v)
            ring = math.sin((v * 22.0 + w * 3.2) * math.pi * 2) * 0.5 + 0.5
            ring = ring ** 3
            streak = fine.at(u * 4 % 64, v * 64)
            k = knots.at(u * 6, v * 6)
            val = 0.93 - 0.11 * ring - 0.05 * streak + 0.04 * (w - 0.5) - (0.06 if k > 0.86 else 0.0)
            im.set(x, y, grey(val))
    return im


def planks(size=512, count=6):
    """Floor boards: `count` boards across U per tile, staggered end joints, grain along V, thin dark gaps."""
    im = Img(size, size)
    rnd = random.Random(21)
    warp = [Noise(4, 31), Noise(8, 32)]
    fine = Noise(64, 33)
    bw = size / count
    joints = [rnd.uniform(0.0, 1.0) for _ in range(count)]
    tone = [[rnd.uniform(-0.06, 0.05) for _ in range(3)] for _ in range(count)]
    for y in range(size):
        v = y / size
        for x in range(size):
            b = int(x / bw)
            fx = (x - b * bw) / bw
            u = x / size
            seg = int(((v + joints[b]) % 1.0) * 2.0)  # two boards per tile length
            w = fbm(warp, u * 0.5 + b * 0.13, v)
            ring = (math.sin((u * 30.0 + w * 2.6 + b * 1.7) * math.pi * 2) * 0.5 + 0.5) ** 3
            streak = fine.at(u * 64, v * 4 % 64)
            val = 0.9 + tone[b][seg] - 0.09 * ring - 0.04 * streak
            edge = min(fx, 1 - fx) * bw
            if edge < 1.2:
                val *= 0.62
            jv = ((v + joints[b]) % 0.5) * size
            if jv < 1.2 or jv > size * 0.5 - 0.6:
                val *= 0.66
            im.set(x, y, grey(val))
    return im


def carpet(size=256):
    im = Img(size, size)
    n = [Noise(16, 41), Noise(32, 42), Noise(128, 43)]
    rnd = random.Random(44)
    for y in range(size):
        for x in range(size):
            u, v = x / size, y / size
            val = 0.84 + 0.12 * fbm(n, u, v) + rnd.uniform(-0.05, 0.05)
            # faint woven diagonal
            if ((x + y) // 3) % 4 == 0:
                val -= 0.03
            im.set(x, y, grey(val))
    return im


def plaster(size=256):
    im = Img(size, size)
    n = [Noise(8, 51), Noise(16, 52), Noise(64, 53)]
    rnd = random.Random(54)
    for y in range(size):
        for x in range(size):
            u, v = x / size, y / size
            val = 0.93 + 0.06 * fbm(n, u, v) + rnd.uniform(-0.005, 0.005)
            im.set(x, y, grey(val))
    return im


def facade(lit=False, size=256):
    """One tile = 6 m x 6 m: 2 storeys x 3 bays. Day: concrete + glass. Lit: black with warm windows (emission)."""
    rnd = random.Random(61)
    im = Img(size, size, (0, 0, 0) if lit else hexc("#D7D0C4"))
    n = Noise(16, 62)
    if not lit:
        for y in range(size):
            for x in range(size):
                g = 0.84 + 0.1 * n.at(x / size * 16, y / size * 16)
                im.set(x, y, grey(g))
    cell = size / 3
    floor = size / 2
    for fy in range(2):
        for bx in range(3):
            x0 = bx * cell + cell * 0.16
            x1 = (bx + 1) * cell - cell * 0.16
            y0 = fy * floor + floor * 0.22
            y1 = (fy + 1) * floor - floor * 0.18
            if lit:
                on = rnd.random() < 0.55
                if on:
                    warm = rnd.choice([hexc("#FFD58A"), hexc("#FFE6B5"), hexc("#FFC46B"), hexc("#CFE3FF")])
                    im.rect(x0, y0, x1, y1, warm)
                    im.rect(x0, y0, x1, y0 + (y1 - y0) * 0.25, tuple(int(c * 0.75) for c in warm))
            else:
                im.rect(x0, y0, x1, y1, hexc("#5E7387"))
                im.rect(x0, y0, x1, y0 + (y1 - y0) * 0.45, hexc("#7F97AC"))
                im.rect((x0 + x1) / 2 - 1, y0, (x0 + x1) / 2 + 1, y1, hexc("#3D4A57"))
                im.rect(x0 - 2, y1, x1 + 2, y1 + 3, hexc("#EDE8DF"))
    return im


def silhouette(w=128, h=256):
    """Blurred bean passer-by (head + shoulders + body), dark, soft alpha: reads as a shape behind frosted glass."""
    im = Img(w, h, (22, 26, 38, 0), alpha=True)
    for y in range(h):
        for x in range(w):
            px, py = (x + 0.5) / w, (y + 0.5) / h   # py 0 = top
            # bean body: capsule from y 0.12..0.98, radius 0.38
            cx = 0.5
            top, bot, r = 0.2, 0.98, 0.36
            yy = min(max(py, top), bot)
            d = math.hypot(px - cx, (py - yy) * 0.9) - r
            # soft edge
            a = 1.0 - min(1.0, max(0.0, (d + 0.09) / 0.18))
            a = a * a * (3 - 2 * a)
            if a > 0:
                i = (y * w + x) * 4
                im.buf[i + 3] = int(a * 235)
    return im


# ------------------------------------------------------------------------------------------------ screens

SW, SH = 384, 216
TEAL0, TEAL1 = hexc("#1F5A6B"), hexc("#3E9A8E")
NAVY = hexc("#1C2742")
CREAM = hexc("#F4EBDD")


def _icon(im, x, y, kind):
    s = 26
    im.rrect(x, y, x + s, y + s, 6, (255, 255, 255), 0.14)
    cx, cy = x + s / 2, y + s / 2
    if kind == "task":      # clipboard (orange)
        im.rrect(cx - 8, cy - 10, cx + 8, cy + 11, 2, hexc("#E9923B"))
        im.rect(cx - 6, cy - 7, cx + 6, cy + 9, CREAM)
        im.rect(cx - 4, cy - 12, cx + 4, cy - 8, hexc("#5B3A29"))
        for k in range(3):
            im.rect(cx - 4, cy - 4 + k * 4, cx + 4, cy - 2 + k * 4, hexc("#9AA3B1"))
    elif kind == "mail":    # envelope (blue)
        im.rrect(cx - 10, cy - 7, cx + 10, cy + 8, 2, hexc("#5AA9E6"))
        im.line(cx - 10, cy - 7, cx, cy + 1, 2, hexc("#2B6FB3"))
        im.line(cx + 10, cy - 7, cx, cy + 1, 2, hexc("#2B6FB3"))
    elif kind == "record":  # folder (manila)
        im.rrect(cx - 10, cy - 8, cx - 1, cy - 4, 2, hexc("#D9B45B"))
        im.rrect(cx - 10, cy - 6, cx + 10, cy + 9, 2, hexc("#F1CF6E"))
    elif kind == "trick":   # black mask (purple)
        im.rrect(cx - 10, cy - 7, cx + 10, cy + 8, 6, hexc("#7C4DB0"))
        im.circle(cx - 4.5, cy, 3, (255, 255, 255))
        im.circle(cx + 4.5, cy, 3, (255, 255, 255))
    elif kind == "agenda":  # calendar (red)
        im.rrect(cx - 9, cy - 8, cx + 9, cy + 10, 2, CREAM)
        im.rect(cx - 9, cy - 8, cx + 9, cy - 3, hexc("#D9483B"))
        for k in range(3):
            for j in range(3):
                im.rect(cx - 6 + j * 5, cy + k * 4 - 1, cx - 3 + j * 5, cy + k * 4 + 1, hexc("#9AA3B1"))


def _bucket(im, cx, cy, s, col, a=1.0):
    """KOVA bucket mark (matches Art/UI/computer/os_wallpaper.png)."""
    for k in range(int(s * 1.0)):
        t = k / max(1, s * 1.0)
        w = s * (0.52 - 0.12 * t)
        im.rect(cx - w, cy - s * 0.35 + k, cx + w, cy - s * 0.35 + k + 1, col, a)
    for ang in range(0, 181, 6):
        r = math.radians(ang)
        im.circle(cx + math.cos(r) * s * 0.5, cy - s * 0.38 - math.sin(r) * s * 0.42, max(1.0, s * 0.06), col, a)


def _desktop_base():
    im = Img(SW, SH, hexc("#F2EEE2"))
    for row in range(5):
        for col in range(9):
            x = 22 + col * 46 + (23 if row % 2 else 0)
            y = 22 + row * 44
            _bucket(im, x, y, 14, hexc("#E4DFD1"))
    # company mark: green bucket
    _bucket(im, 192, 96, 40, hexc("#3E8A5E"))
    im.rect(160, 94, 224, 100, hexc("#2F6B4A"))
    # app icons (left column)
    for i, k in enumerate(("task", "mail", "record", "trick", "agenda")):
        im.rrect(9, 7 + i * 34, 41, 39 + i * 34, 7, hexc("#2F6B5E"), 0.9)
        _icon(im, 12, 10 + i * 34, k)
    # sticky note
    im.rrect(318, 112, 370, 160, 2, hexc("#F7E36B"))
    for k in range(4):
        im.rect(324, 122 + k * 8, 362 - (k % 2) * 10, 125 + k * 8, hexc("#8A7A3A"))
    # taskbar
    im.rect(0, SH - 18, SW, SH, NAVY)
    im.rrect(5, SH - 15, 21, SH - 3, 3, hexc("#3E8A5E"))
    for i in range(4):
        im.rrect(30 + i * 22, SH - 15, 46 + i * 22, SH - 3, 3, (255, 255, 255), 0.18)
    for i in range(4):
        im.rect(SW - 46 + i * 9, SH - 13, SW - 40 + i * 9, SH - 5, CREAM if i != 1 else NAVY)
    return im


def screen_desktop():
    im = _desktop_base()
    # a task window, half open
    im.rrect(66, 24, 292, 136, 5, (0, 0, 0), 0.18)
    im.rrect(64, 22, 290, 134, 5, (255, 255, 255))
    im.rect(64, 22, 290, 36, hexc("#2F6B5E"))
    im.circle(279, 29, 4, hexc("#E2453C"))
    for k in range(6):
        im.rect(76, 46 + k * 13, 76 + (190 if k % 3 else 120), 51 + k * 13, hexc("#B9C0C9"))
    im.rect(76, 46, 196, 52, NAVY)
    im.rrect(214, 112, 280, 128, 4, hexc("#3BB273"))
    return im


def screen_alert():
    im = screen_desktop()
    # big red notification card + badge on the mail icon
    im.rrect(110, 54, 280, 140, 8, (0, 0, 0), 0.35)
    im.rrect(104, 48, 274, 134, 8, hexc("#FFF4E4"))
    im.rect(104, 48, 274, 70, hexc("#D9362B"))
    im.circle(128, 59, 8, (255, 255, 255))
    im.rect(127, 53, 130, 61, hexc("#D9362B"))
    im.rect(127, 63, 130, 66, hexc("#D9362B"))
    for k in range(3):
        im.rect(118, 82 + k * 12, 118 + (140 if k < 2 else 90), 88 + k * 12, hexc("#8C96A3"))
    im.rrect(200, 116, 266, 130, 4, hexc("#D9362B"))
    im.circle(36, 46, 7, hexc("#E2453C"))
    im.rect(35, 41, 38, 48, (255, 255, 255))
    im.rect(35, 49, 38, 51, (255, 255, 255))
    return im


def screen_offline():
    im = Img(SW, SH)
    im.vgrad(hexc("#2A2E38"), hexc("#3A3F4C"))
    cx, cy = SW / 2, 96
    for r in (46, 32, 18):
        for a in range(-50, 51):
            t = math.radians(a - 90)
            im.circle(cx + math.cos(t) * r, cy + 20 + math.sin(t) * r, 3.2, hexc("#8C96A6"))
    im.circle(cx, cy + 20, 5, hexc("#8C96A6"))
    im.line(cx - 46, cy - 34, cx + 46, cy + 40, 9, hexc("#2A2E38"))
    im.line(cx - 46, cy - 34, cx + 46, cy + 40, 6, hexc("#E2453C"))
    im.rrect(cx - 92, 150, cx + 92, 172, 6, hexc("#E2453C"))
    for k in range(9):
        im.rect(cx - 80 + k * 18, 158, cx - 70 + k * 18, 164, (255, 255, 255))
    im.rect(0, SH - 18, SW, SH, hexc("#1A1D24"))
    return im


def screen_coffee():
    im = _desktop_base()
    rnd = random.Random(71)
    # glitch bands
    for k in range(18):
        y = rnd.randint(0, SH - 6)
        hgt = rnd.randint(2, 7)
        off = rnd.randint(-30, 30)
        col = rnd.choice([hexc("#FF3B8A"), hexc("#3BFFE0"), hexc("#FFFFFF"), hexc("#000000")])
        im.rect(max(0, off), y, min(SW, SW + off), y + hgt, col, 0.35)
    # coffee drips from the top
    brown, dark = hexc("#6B4226"), hexc("#3F2616")
    for k in range(22):
        x = rnd.uniform(0, SW)
        ln = rnd.uniform(30, 190)
        w = rnd.uniform(6, 18)
        im.rect(x - w / 2, 0, x + w / 2, ln, brown, 0.92)
        im.circle(x, ln, w / 2 + 1.5, brown, 0.92)
        im.rect(x - w / 2, 0, x - w / 2 + 2, ln, dark, 0.6)
    im.rect(0, 0, SW, 26, brown)
    return im


# ------------------------------------------------------------------------------------------------ v5 banknote

# 5x7 bitmap glyphs for the few letters printed on the note ("1 PRİM", "KOVA"); '#' = ink
GLYPHS = {
    "1": ["..#..", ".##..", "#.#..", "..#..", "..#..", "..#..", "#####"],
    "P": ["####.", "#...#", "#...#", "####.", "#....", "#....", "#...."],
    "R": ["####.", "#...#", "#...#", "####.", "#.#..", "#..#.", "#...#"],
    "I": ["#####", "..#..", "..#..", "..#..", "..#..", "..#..", "#####"],
    "M": ["#...#", "##.##", "#.#.#", "#.#.#", "#...#", "#...#", "#...#"],
    "K": ["#...#", "#..#.", "#.#..", "##...", "#.#..", "#..#.", "#...#"],
    "O": [".###.", "#...#", "#...#", "#...#", "#...#", "#...#", ".###."],
    "V": ["#...#", "#...#", "#...#", "#...#", ".#.#.", ".#.#.", "..#.."],
    "A": [".###.", "#...#", "#...#", "#####", "#...#", "#...#", "#...#"],
}


def _text(im, s, x, y, px, col, dot_i=None, a=1.0):
    """Blocky letters, px = size of one glyph cell; dot_i = index of an 'I' that gets the Turkish dot (İ)."""
    for k, ch in enumerate(s):
        g = GLYPHS.get(ch)
        gx = x + k * px * 6
        if g is None:
            continue
        for row, line in enumerate(g):
            for col_, bit in enumerate(line):
                if bit == "#":
                    im.rect(gx + col_ * px, y + row * px, gx + (col_ + 1) * px, y + (row + 1) * px, col, a)
        if dot_i is not None and k == dot_i:
            im.rect(gx + 2 * px, y - 2 * px, gx + 3 * px, y - px, col, a)


def bill(w=512, h=256):
    """One prim banknote (v5 cash box and drawer): mint paper, guilloche, a bean in the medallion, '1 PRİM'."""
    paper, paper2 = hexc("#D3EBCB"), hexc("#B9DDB1")
    ink, ink2, light = hexc("#2F6B4F"), hexc("#5E9E77"), hexc("#EAF5E4")
    im = Img(w, h, paper)
    n = [Noise(8, 91), Noise(16, 92)]
    for y in range(h):
        for x in range(w):
            t = 0.5 + 0.5 * math.cos((x / w - 0.3) * math.pi * 1.6) * math.cos((y / h - 0.5) * math.pi)
            v = fbm(n, x / w, y / h)
            col = tuple(int(paper2[k] + (paper[k] - paper2[k]) * t + (v - 0.5) * 14) for k in range(3))
            im.set(x, y, col)
    # guilloche bands top and bottom
    for k in range(7):
        ph = k * 0.9
        pts = [(x, 30 + 10 * math.sin(x / 19.0 + ph) + k * 1.6) for x in range(0, w + 8, 6)]
        pts2 = [(x, h - 30 - 10 * math.sin(x / 17.0 + ph) - k * 1.6) for x in range(0, w + 8, 6)]
        for seq in (pts, pts2):
            for (x0, y0), (x1, y1) in zip(seq, seq[1:]):
                im.line(x0, y0, x1, y1, 1.2, ink2, 0.55)
    # frame
    for (x0, y0, x1, y1) in ((0, 0, w, 9), (0, h - 9, w, h), (0, 0, 9, h), (w - 9, 0, w, h)):
        im.rect(x0, y0, x1, y1, ink)
    for (x0, y0, x1, y1) in ((16, 16, w - 16, 18), (16, h - 18, w - 16, h - 16), (16, 16, 18, h - 16), (w - 18, 16, w - 16, h - 16)):
        im.rect(x0, y0, x1, y1, ink, 0.8)
    # medallion with a bean (the game's people) on the left
    cx, cy = 132, h / 2
    for r_, a in ((86, 0.35), (80, 1.0)):
        im.circle(cx, cy, r_, light, a)
    for k in range(18):
        ang = k / 18.0 * math.pi * 2
        im.line(cx + math.cos(ang) * 60, cy + math.sin(ang) * 72, cx + math.cos(ang) * 78, cy + math.sin(ang) * 82, 1.5, ink2, 0.5)
    im.circle(cx, cy, 80, ink, 1.0, ring=3.5)
    im.rrect(cx - 26, cy - 48, cx + 26, cy + 58, 26, ink)              # bean body
    im.rrect(cx - 34, cy + 30, cx + 34, cy + 66, 16, ink)              # shoulders
    im.circle(cx - 10, cy - 14, 8, light); im.circle(cx + 12, cy - 14, 8, light)
    im.circle(cx - 8, cy - 13, 3.5, ink); im.circle(cx + 14, cy - 13, 3.5, ink)
    im.rect(cx - 4, cy + 26, cx + 4, cy + 52, light, 0.9)             # tie
    # value: big "1", "PRİM", the KOVA mark, corner numerals
    _text(im, "1", 300, 50, 13, ink)
    _text(im, "PRIM", 260, 160, 8, ink, dot_i=2)
    _text(im, "KOVA", 404, 40, 4, ink2)
    _bucket(im, 446, 96, 22, ink2)
    _text(im, "1", 30, 30, 4, ink)
    _text(im, "1", w - 52, h - 58, 4, ink)
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    jobs = [("Wood", wood), ("Planks", planks), ("Carpet", carpet), ("Plaster", plaster),
            ("Facade", lambda: facade(False)), ("FacadeLit", lambda: facade(True)), ("Silhouette", silhouette),
            ("ScreenDesktop", screen_desktop), ("ScreenAlert", screen_alert), ("ScreenOffline", screen_offline),
            ("ScreenCoffee", screen_coffee), ("Bill", bill)]
    only = [a for a in sys.argv[1:] if not a.startswith("-")]
    for name, fn in jobs:
        if only and name not in only:
            continue
        fn().save(os.path.join(OUT, name + ".png"))
    print("ROOM_TEXTURES_DONE", OUT)


if __name__ == "__main__":
    main()
