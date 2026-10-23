"""Tile PNG previews into one labelled contact sheet: python contact_sheet.py out.png cols thumb img1 img2 ..."""
import os
import sys
from PIL import Image, ImageDraw

out, cols, thumb = sys.argv[1], int(sys.argv[2]), int(sys.argv[3])
paths = sys.argv[4:]
rows = (len(paths) + cols - 1) // cols
sheet = Image.new("RGB", (cols * thumb, rows * (thumb + 18)), (40, 40, 48))
draw = ImageDraw.Draw(sheet)
for i, p in enumerate(paths):
    im = Image.open(p).convert("RGB")
    im.thumbnail((thumb, thumb))
    x, y = (i % cols) * thumb, (i // cols) * (thumb + 18)
    sheet.paste(im, (x, y + 18))
    draw.text((x + 4, y + 3), os.path.splitext(os.path.basename(p))[0], fill=(230, 230, 230))
sheet.save(out)
print("saved", out, sheet.size)
