"""Tiny dependency-free PNG reader/writer (numpy + zlib) used for atlas generation and contact sheets."""
import struct
import zlib

import numpy as np


def read_png(path):
    """Returns float32 (h, w, 4) RGBA in 0..1, row 0 = top. Supports 8-bit gray/RGB/RGBA/gray+alpha, non-interlaced."""
    with open(path, "rb") as f:
        data = f.read()
    assert data[:8] == b"\x89PNG\r\n\x1a\n", path
    pos = 8
    idat = b""
    w = h = depth = ctype = None
    palette = None
    while pos < len(data):
        ln = struct.unpack(">I", data[pos:pos + 4])[0]
        tag = data[pos + 4:pos + 8]
        body = data[pos + 8:pos + 8 + ln]
        pos += 12 + ln
        if tag == b"IHDR":
            w, h, depth, ctype, _, _, interlace = struct.unpack(">IIBBBBB", body)
            assert depth == 8 and interlace == 0, "unsupported PNG"
        elif tag == b"PLTE":
            palette = np.frombuffer(body, np.uint8).reshape(-1, 3)
        elif tag == b"IDAT":
            idat += body
        elif tag == b"IEND":
            break
    ch = {0: 1, 2: 3, 3: 1, 4: 2, 6: 4}[ctype]
    raw = zlib.decompress(idat)
    stride = w * ch
    out = np.zeros((h, stride), np.int32)
    prev = np.zeros(stride, np.int32)
    p = 0
    for y in range(h):
        ft = raw[p]
        line = np.frombuffer(raw[p + 1:p + 1 + stride], np.uint8).astype(np.int32)
        p += 1 + stride
        if ft == 0:
            cur = line
        elif ft == 2:
            cur = (line + prev) & 255
        else:
            cur = np.zeros(stride, np.int32)
            for i in range(stride):
                a = cur[i - ch] if i >= ch else 0
                b = prev[i]
                c = prev[i - ch] if i >= ch else 0
                if ft == 1:
                    pr = a
                elif ft == 3:
                    pr = (a + b) >> 1
                else:
                    pa, pb, pc = abs(b - c), abs(a - c), abs(a + b - 2 * c)
                    pr = a if (pa <= pb and pa <= pc) else (b if pb <= pc else c)
                cur[i] = (line[i] + pr) & 255
        out[y] = cur
        prev = cur
    img = out.reshape(h, w, ch).astype(np.float32) / 255.0
    if ctype == 3:
        idx = out.reshape(h, w)
        rgb = palette[idx].astype(np.float32) / 255.0
        return np.concatenate([rgb, np.ones((h, w, 1), np.float32)], 2)
    if ch == 1:
        img = np.repeat(img, 3, 2)
    elif ch == 2:
        img = np.concatenate([np.repeat(img[..., :1], 3, 2), img[..., 1:2]], 2)
    if img.shape[2] == 3:
        img = np.concatenate([img, np.ones((h, w, 1), np.float32)], 2)
    return img


def write_png(path, rgba):
    """rgba: (h, w, 3|4) float 0..1, row 0 = top."""
    if rgba.shape[2] == 3:
        rgba = np.concatenate([rgba, np.ones(rgba.shape[:2] + (1,), np.float32)], 2)
    h, w = rgba.shape[:2]
    data = (np.clip(rgba, 0, 1) * 255 + 0.5).astype(np.uint8)
    raw = b"".join(b"\x00" + data[y].tobytes() for y in range(h))

    def chunk(tag, payload):
        c = struct.pack(">I", len(payload)) + tag + payload
        return c + struct.pack(">I", zlib.crc32(tag + payload) & 0xFFFFFFFF)
    png = b"\x89PNG\r\n\x1a\n" + chunk(b"IHDR", struct.pack(">IIBBBBB", w, h, 8, 6, 0, 0, 0))
    png += chunk(b"IDAT", zlib.compress(raw, 6)) + chunk(b"IEND", b"")
    with open(path, "wb") as f:
        f.write(png)


# 5x7 bitmap font for sheet labels (A-Z, 0-9, a few symbols); lowercase is drawn as uppercase.
_FONT = {
    "A": "01110100011000111111100011000110001", "B": "11110100011000111110100011000111110", "C": "01110100011000010000100001000101110",
    "D": "11110100011000110001100011000111110", "E": "11111100001000011110100001000011111", "F": "11111100001000011110100001000010000",
    "G": "01110100011000010111100011000101111", "H": "10001100011000111111100011000110001", "I": "01110001000010000100001000010001110",
    "J": "00111000100001000010000101001001100", "K": "10001100101010011000101001001010001", "L": "10000100001000010000100001000011111",
    "M": "10001110111010110101100011000110001", "N": "10001100011100110101100111000110001", "O": "01110100011000110001100011000101110",
    "P": "11110100011000111110100001000010000", "Q": "01110100011000110001101011001001101", "R": "11110100011000111110101001001010001",
    "S": "01111100001000001110000010000111110", "T": "11111001000010000100001000010000100", "U": "10001100011000110001100011000101110",
    "V": "10001100011000110001100010101000100", "W": "10001100011000110101101011010101010", "X": "10001100010101000100010101000110001",
    "Y": "10001100010101000100001000010000100", "Z": "11111000010001000100010001000011111", "0": "01110100011001110101110011000101110",
    "1": "00100011000010000100001000010001110", "2": "01110100010000100010001000100011111", "3": "11111000100010000010000011000101110",
    "4": "00010001100101010010111110001000010", "5": "11111100001111000001000011000101110", "6": "00110010001000011110100011000101110",
    "7": "11111000010001000100010000100001000", "8": "01110100011000101110100011000101110", "9": "01110100011000101111000010001001100",
    "_": "00000000000000000000000000000011111", "-": "00000000000000011111000000000000000", ".": "00000000000000000000000000110001100",
    "/": "00001000010001000100010001000010000", " ": "00000000000000000000000000000000000", ":": "00000011000110000000011000110000000",
}


def draw_text(img, x, y, text, color=(1, 1, 1), scale=2):
    for ch in text.upper():
        bits = _FONT.get(ch, _FONT[" "])
        for i, b in enumerate(bits):
            if b == "1":
                cx, cy = x + (i % 5) * scale, y + (i // 5) * scale
                img[cy:cy + scale, cx:cx + scale, :3] = color
        x += 6 * scale
    return x


def contact_sheet(paths_labels, cols, cell=None, bg=(0.13, 0.14, 0.17), label_h=20):
    imgs = [(read_png(p), lbl) for p, lbl in paths_labels]
    ch, cw = cell or imgs[0][0].shape[:2]
    rows = (len(imgs) + cols - 1) // cols
    sheet = np.zeros((rows * (ch + label_h), cols * cw, 4), np.float32)
    sheet[..., :3] = bg
    sheet[..., 3] = 1
    for i, (im, lbl) in enumerate(imgs):
        r, c = i // cols, i % cols
        y0, x0 = r * (ch + label_h), c * cw
        h, w = min(ch, im.shape[0]), min(cw, im.shape[1])
        a = im[:h, :w, 3:4]
        sheet[y0 + label_h:y0 + label_h + h, x0:x0 + w, :3] = im[:h, :w, :3] * a + sheet[y0 + label_h:y0 + label_h + h, x0:x0 + w, :3] * (1 - a)
        draw_text(sheet, x0 + 4, y0 + 3, lbl, (0.95, 0.95, 0.95), 2)
    return sheet


def resize(img, scale):
    """Box-filter downscale (scale < 1) for contact sheets."""
    h, w = img.shape[:2]
    nh, nw = max(1, int(h * scale)), max(1, int(w * scale))
    ys = (np.arange(nh + 1) * h / nh).astype(int)
    xs = (np.arange(nw + 1) * w / nw).astype(int)
    c = np.cumsum(np.cumsum(np.pad(img, ((1, 0), (1, 0), (0, 0))), 0), 1)
    tot = c[ys[1:]][:, xs[1:]] - c[ys[:-1]][:, xs[1:]] - c[ys[1:]][:, xs[:-1]] + c[ys[:-1]][:, xs[:-1]]
    area = ((ys[1:] - ys[:-1])[:, None] * (xs[1:] - xs[:-1])[None, :])[..., None]
    return (tot / area).astype(np.float32)
