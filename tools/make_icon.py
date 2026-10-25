"""Draws packaging/icon.png (the Text X package icon) - the translate symbol on a deep-navy tile.

    python tools/make_icon.py

Same drawing as UI/Theming/Icons.cs (24 x 24 grid), rendered with 4x supersampling.
"""
import os
from PIL import Image, ImageDraw

NAVY = (0x13, 0x2D, 0x52, 255)
INK = (0x9E, 0xC0, 0xF0, 255)
EDGE = (0x9E, 0xC0, 0xF0, 140)


def bezier(p0, p1, p2, p3, n=40):
    pts = []
    for i in range(n + 1):
        t = i / n
        a = (1 - t) ** 3
        b = 3 * (1 - t) ** 2 * t
        c = 3 * (1 - t) * t ** 2
        d = t ** 3
        pts.append((a * p0[0] + b * p1[0] + c * p2[0] + d * p3[0], a * p0[1] + b * p1[1] + c * p2[1] + d * p3[1]))
    return pts


def glyph():
    return [
        [(4, 5), (12, 5)],
        [(8, 3), (8, 5)],
        bezier((10, 5), (9.5, 8), (7.5, 11), (4, 13)),
        bezier((6, 9), (7, 11), (9, 12.5), (11, 13)),
        [(13, 21), (17, 11), (21, 21)],
        [(14.5, 17), (19.5, 17)],
    ]


def render(size, out):
    ss = 4
    S = size * ss
    img = Image.new("RGBA", (S, S), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = int(S * 0.22)
    d.rounded_rectangle([0, 0, S - 1, S - 1], radius=r, fill=NAVY, outline=EDGE, width=max(1, S // 128))
    inset = S * 0.18
    scale = (S - 2 * inset) / 24.0
    w = max(2, int(2.2 * scale))
    rad = w / 2.0
    for poly in glyph():
        pts = [(inset + x * scale, inset + y * scale) for x, y in poly]
        # stamp a round brush along the path: smooth strokes with round caps and joins
        for (x0, y0), (x1, y1) in zip(pts, pts[1:]):
            steps = max(1, int(((x1 - x0) ** 2 + (y1 - y0) ** 2) ** 0.5 / 1.5))
            for i in range(steps + 1):
                x = x0 + (x1 - x0) * i / steps
                y = y0 + (y1 - y0) * i / steps
                d.ellipse([x - rad, y - rad, x + rad, y + rad], fill=INK)
    img = img.resize((size, size), Image.LANCZOS)
    img.save(out)
    print("wrote", out)


if __name__ == "__main__":
    root = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))
    render(256, os.path.join(root, "packaging", "icon.png"))
