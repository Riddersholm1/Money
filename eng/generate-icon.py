"""Draws the NuGet package icon: a coin bearing the generic currency sign, on a dark disc.

Run from the repository root:  python3 eng/generate-icon.py

The icon is geometry rather than a font glyph, so regenerating it needs nothing but a Python
install — no image library, no font, and no binary asset that cannot be rebuilt from source.
"""
import struct
import zlib

SIZE = 128

BACKGROUND = (0x14, 0x1B, 0x2D)   # deep navy, reads on both light and dark NuGet themes
DISC = (0xF2, 0xC1, 0x4E)         # coin gold
DISC_EDGE = (0xC9, 0x97, 0x27)
GLYPH = (0x14, 0x1B, 0x2D)

CENTRE = (SIZE - 1) / 2.0
RADIUS = 52.0
EDGE = 46.0

# The generic currency sign U+00A4: a circle with four rays, drawn as geometry so the icon needs no
# font and no image library.
GLYPH_RADIUS = 17.0
GLYPH_STROKE = 4.0
RAY_INNER = 15.0
RAY_OUTER = 27.0


def coverage(x, y, inside):
    """4x4 supersampled coverage of `inside` over the pixel at (x, y)."""
    hits = 0
    for sy in range(4):
        for sx in range(4):
            if inside(x + (sx + 0.5) / 4.0, y + (sy + 0.5) / 4.0):
                hits += 1
    return hits / 16.0


def blend(under, over, alpha):
    return tuple(round(u + (o - u) * alpha) for u, o in zip(under, over))


def distance(px, py):
    return ((px - CENTRE - 0.5) ** 2 + (py - CENTRE - 0.5) ** 2) ** 0.5


def in_disc(px, py):
    return distance(px, py) <= RADIUS


def in_face(px, py):
    return distance(px, py) <= EDGE


def in_glyph(px, py):
    dx = px - CENTRE - 0.5
    dy = py - CENTRE - 0.5
    ring = (dx * dx + dy * dy) ** 0.5

    if abs(ring - GLYPH_RADIUS) <= GLYPH_STROKE / 2.0:
        return True

    # Four diagonal rays, drawn as thick segments from the ring outwards.
    for ux, uy in ((0.7071, 0.7071), (0.7071, -0.7071), (-0.7071, 0.7071), (-0.7071, -0.7071)):
        along = dx * ux + dy * uy
        across = abs(dx * -uy + dy * ux)
        if RAY_INNER <= along <= RAY_OUTER and across <= GLYPH_STROKE / 2.0:
            return True

    return False


rows = []
for y in range(SIZE):
    row = bytearray()
    for x in range(SIZE):
        colour = BACKGROUND
        colour = blend(colour, DISC_EDGE, coverage(x, y, in_disc))
        colour = blend(colour, DISC, coverage(x, y, in_face))
        colour = blend(colour, GLYPH, coverage(x, y, in_glyph))
        row.extend(colour)
    rows.append(bytes(row))

raw = b"".join(b"\x00" + row for row in rows)


def chunk(kind, payload):
    body = kind + payload
    return struct.pack(">I", len(payload)) + body + struct.pack(">I", zlib.crc32(body))


png = (
    b"\x89PNG\r\n\x1a\n"
    + chunk(b"IHDR", struct.pack(">IIBBBBB", SIZE, SIZE, 8, 2, 0, 0, 0))
    + chunk(b"IDAT", zlib.compress(raw, 9))
    + chunk(b"IEND", b"")
)

with open("assets/icon.png", "wb") as handle:
    handle.write(png)

print(f"assets/icon.png: {len(png)} bytes, {SIZE}x{SIZE}")
