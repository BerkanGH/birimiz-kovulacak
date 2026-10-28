"""Monitor screen textures for the v2 workstation states (pure Python, no dependencies).

Writes Game/Assets/Game/Art/Textures/Screens/{Desktop,LockScreen,Locked,NoSignal}.png (160 x 90). The Unity builder
puts them on M_ScreenDesktop / M_ScreenLockScreen / M_ScreenLocked / M_ScreenNoSignal (base + emission map), so the
state of every PC reads at a glance: e-mail open (FakeEmail possible), lock screen, password-locked, internet down.

Run:  python -I Art/Scripts/map_textures.py   (build_props.py also calls it)
"""
import os
import struct
import sys
import zlib

sys.dont_write_bytecode = True
ROOT = os.path.abspath(os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", ".."))
OUT = os.path.join(ROOT, "Game", "Assets", "Game", "Art", "Textures", "Screens")
W, H = 160, 90


def hexc(h):
    h = h.lstrip("#")
    return tuple(int(h[i:i + 2], 16) for i in (0, 2, 4))


class Img:
    def __init__(self, bg):
        self.px = [[bg for _ in range(W)] for _ in range(H)]

    def rect(self, x0, y0, x1, y1, c):
        for y in range(max(0, y0), min(H, y1)):
            row = self.px[y]
            for x in range(max(0, x0), min(W, x1)):
                row[x] = c

    def grad(self, c0, c1):
        for y in range(H):
            t = y / (H - 1)
            c = tuple(int(c0[k] + (c1[k] - c0[k]) * t) for k in range(3))
            for x in range(W):
                self.px[y][x] = c

    def circle(self, cx, cy, r, c, ring=0):
        for y in range(max(0, cy - r - 1), min(H, cy + r + 2)):
            for x in range(max(0, cx - r - 1), min(W, cx + r + 2)):
                d = ((x - cx) ** 2 + (y - cy) ** 2) ** 0.5
                if d <= r and (ring == 0 or d >= r - ring):
                    self.px[y][x] = c

    def padlock(self, cx, cy, s, c, bg):
        self.circle(cx, cy - s, s, c, ring=max(2, s // 3))
        self.rect(cx - s - 1, cy - s, cx + s + 2, cy, bg)  # cut lower half of the shackle ring
        for x in (cx - s, cx + s - 1):
            self.rect(x, cy - s, x + max(2, s // 3), cy, c)
        self.rect(cx - int(s * 1.5), cy, cx + int(s * 1.5) + 1, cy + int(s * 1.8), c)
        self.rect(cx - 1, cy + s // 2, cx + 2, cy + s, bg)

    def save(self, path):
        raw = b"".join(b"\x00" + bytes(v for p in row for v in p) for row in self.px)
        def chunk(tag, data):
            return struct.pack(">I", len(data)) + tag + data + struct.pack(">I", zlib.crc32(tag + data) & 0xFFFFFFFF)
        png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", W, H, 8, 2, 0, 0, 0)) + chunk(b"IDAT", zlib.compress(raw, 9)) + chunk(b"IEND", b"")
        with open(path, "wb") as f:
            f.write(png)


def desktop():
    im = Img((0, 0, 0))
    im.grad(hexc("#2F6FDE"), hexc("#5BC0EB"))
    for i in range(4):
        im.rect(6, 6 + i * 16, 16, 16 + i * 16, hexc("#F1E9D8") if i % 2 else hexc("#F5C842"))
    # e-mail window
    im.rect(26, 8, 150, 76, hexc("#FFFFFF"))
    im.rect(26, 8, 150, 17, hexc("#24315E"))
    im.rect(140, 10, 147, 15, hexc("#E2453C"))
    im.rect(26, 17, 60, 76, hexc("#E9EEF6"))
    for i in range(6):
        im.rect(29, 21 + i * 8, 57, 25 + i * 8, hexc("#9AA8BF") if i else hexc("#2F6FDE"))
    for i in range(7):
        im.rect(64, 22 + i * 7, 64 + (80 if i % 3 else 55), 25 + i * 7, hexc("#B9C2CF"))
    im.rect(64, 22, 120, 26, hexc("#24315E"))
    im.rect(124, 66, 146, 73, hexc("#3BB273"))  # send
    im.rect(0, 82, W, H, hexc("#1E2A44"))
    im.rect(3, 84, 11, 89, hexc("#5BC0EB"))
    im.rect(140, 84, 157, 88, hexc("#F1E9D8"))
    return im


def lockscreen():
    im = Img((0, 0, 0))
    im.grad(hexc("#1B2650"), hexc("#3B4F8F"))
    for i, w in enumerate((10, 4, 10, 10)):  # "09:41"-ish digits
        x = 52 + i * 16 + (6 if i > 1 else 0)
        im.rect(x, 14, x + w, 34, hexc("#FFFFFF"))
    im.rect(78, 18, 81, 21, hexc("#FFFFFF"))
    im.rect(78, 27, 81, 30, hexc("#FFFFFF"))
    im.circle(80, 50, 8, hexc("#F1E9D8"))
    im.circle(80, 47, 3, hexc("#3B4F8F"))
    im.rect(56, 64, 104, 72, hexc("#FFFFFF"))
    for i in range(5):
        im.circle(63 + i * 7, 68, 1, hexc("#24315E"))
    return im


def locked():
    im = Img(hexc("#B3261E"))
    im.rect(0, 0, W, 10, hexc("#7E1A15"))
    im.padlock(80, 40, 10, hexc("#FFFFFF"), hexc("#B3261E"))
    im.rect(48, 70, 112, 78, hexc("#FFFFFF"))
    for i in range(6):
        im.rect(54 + i * 9, 73, 58 + i * 9, 76, hexc("#B3261E"))
    return im


def nosignal():
    im = Img(hexc("#2A2B2F"))
    bars = ["#C0C0C0", "#C0C000", "#00C0C0", "#00C000", "#C000C0", "#C00000", "#0000C0"]
    bw = W // len(bars) + 1
    for i, b in enumerate(bars):
        im.rect(i * bw, 0, (i + 1) * bw, 58, hexc(b))
    im.rect(0, 58, W, 66, hexc("#1A1A1A"))
    im.rect(40, 30, 120, 58, hexc("#1A1A1A"))
    # unplugged globe + red cross
    im.circle(80, 44, 10, hexc("#9AA8BF"), ring=2)
    im.rect(70, 43, 91, 45, hexc("#9AA8BF"))
    for k in range(-7, 8):
        im.rect(80 + k, 44 + k, 82 + k, 46 + k, hexc("#FF3B2F"))
        im.rect(80 + k, 44 - k, 82 + k, 46 - k, hexc("#FF3B2F"))
    for x in range(0, W, 3):
        im.rect(x, 70 + (x * 7) % 15, x + 2, 72 + (x * 7) % 15, hexc("#55565C"))
    return im


def main():
    os.makedirs(OUT, exist_ok=True)
    for name, fn in (("Desktop", desktop), ("LockScreen", lockscreen), ("Locked", locked), ("NoSignal", nosignal)):
        fn().save(os.path.join(OUT, name + ".png"))
    print("SCREENS_DONE", OUT)


if __name__ == "__main__":
    main()
