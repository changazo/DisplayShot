"""Generates DisplayShot's artwork: the logo, the app icon and the toolbar glyphs.

Everything is drawn from simple shapes (and Palatino Linotype Bold for the monogram and the
text-tool "T"), supersampled 8x and downsampled, so the PNGs in assets/ can be rebuilt at any time:

    python tools/make_assets.py

Requires Pillow. Palette and fonts follow https://github.com/changazo/page.
"""
import os
from PIL import Image, ImageDraw, ImageFont

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(HERE, "..", "assets")               # embedded in the exe
DOC_IMAGES = os.path.join(HERE, "..", "docs", "images")   # README / branding only
FONTS = os.path.join(os.environ.get("WINDIR", r"C:\Windows"), "Fonts")
SERIF_BOLD = os.path.join(FONTS, "palab.ttf")        # Palatino Linotype Bold (the page's serif on Windows)
SANS_SEMIBOLD = os.path.join(FONTS, "seguisb.ttf")   # Segoe UI Semibold (the page's sans on Windows)

# Palette (changazo/page, dark theme first)
LAVENDER = (0xED, 0xE5, 0xFF)
GRAPE = (0x6B, 0x4E, 0x71)
CHARCOAL = (0x3A, 0x44, 0x54)
SLATE = (0x53, 0x68, 0x7E)
BLUSH = (0xF5, 0xDD, 0xDD)
DARK_BG = (0x2A, 0x31, 0x3F)

SS = 8   # supersampling factor
OUT = 4  # glyph PNGs are written at 4x their design size; the app scales them to the monitor's DPI


def canvas(w, h, bg=(0, 0, 0, 0)):
    im = Image.new("RGBA", (w * SS, h * SS), bg)
    return im, ImageDraw.Draw(im)


def finish(im, w, h):
    return im.resize((w * OUT, h * OUT), Image.LANCZOS)


def s(v):
    """Scale a coordinate (or tuple of coordinates) from glyph units to the supersampled canvas."""
    if isinstance(v, (tuple, list)):
        return [x * SS for x in v]
    return v * SS


def rounded_line(d, pts, width, color):
    d.line([tuple(s(p)) for p in pts], fill=color, width=int(s(width)), joint="curve")
    r = s(width) / 2
    for p in (pts[0], pts[-1]):
        x, y = s(p)
        d.ellipse([x - r, y - r, x + r, y + r], fill=color)


# --------------------------------------------------------------------------------------------
# Monogram (logo and app icon), same geometry as the JCCh favicon: Palatino Linotype Bold at
# 204/512 of the square, second line indented to make a staircase.

def monogram(size, bg, fg, top="Di", bottom="Sh"):
    big = 512 * 4
    im = Image.new("RGBA", (big, big), bg + (255,))
    d = ImageDraw.Draw(im)
    f = ImageFont.truetype(SERIF_BOLD, 204 * 4)
    # favicon: line 1 baseline-left at (88, 238), line 2 at (164, 410); re-centre the block horizontally
    x1, y1, x2, y2 = 88 * 4, 238 * 4, 164 * 4, 410 * 4
    b1 = d.textbbox((x1, y1), top, font=f, anchor="ls")
    b2 = d.textbbox((x2, y2), bottom, font=f, anchor="ls")
    left, right = min(b1[0], b2[0]), max(b1[2], b2[2])
    dx = (big - (right - left)) // 2 - left
    d.text((x1 + dx, y1), top, font=f, fill=fg + (255,), anchor="ls")
    d.text((x2 + dx, y2), bottom, font=f, fill=fg + (255,), anchor="ls")
    return im.resize((size, size), Image.LANCZOS)


def camera_logo(size, body, fg, top="Di", bottom="Sh"):
    """App logo: a soft camera silhouette (rounded body, trapezoid viewfinder blended into it with
    smooth joints) carrying the Di/Sh monogram, laid out like the favicon's staircase.
    Transparent around the camera."""
    from PIL import ImageFilter
    k4 = 4                                   # draw on a 2048 canvas (512 units x 4)
    big = 512 * k4
    mask = Image.new("L", (big, big), 0)
    md = ImageDraw.Draw(mask)
    md.rounded_rectangle([v * k4 for v in (24, 150, 488, 456)], radius=92 * k4, fill=255)          # body
    md.polygon([(x * k4, y * k4) for x, y in [(150, 214), (190, 100), (322, 100), (362, 214)]], fill=255)  # viewfinder
    # blend: blur + threshold rounds the viewfinder's corners and fills the joints with smooth curves
    mask = mask.filter(ImageFilter.GaussianBlur(14 * k4)).point(lambda v: 255 if v >= 128 else 0)
    mask = mask.filter(ImageFilter.GaussianBlur(1.2 * k4))   # anti-aliased edge
    im = Image.new("RGBA", (big, big), body + (0,))
    im.putalpha(mask)
    d = ImageDraw.Draw(im)
    # monogram scaled into the body: favicon geometry x 0.74, block centred in the body
    k = 0.74
    f = ImageFont.truetype(SERIF_BOLD, int(204 * k * k4))
    oy = (150 + (456 - 150 - 320 * k) / 2) - 92 * k
    x1, y1 = 88 * k, 238 * k + oy
    x2, y2 = 164 * k, 410 * k + oy
    b1 = d.textbbox((x1 * k4, y1 * k4), top, font=f, anchor="ls")
    b2 = d.textbbox((x2 * k4, y2 * k4), bottom, font=f, anchor="ls")
    dx = (24 + 488) / 2 * k4 - (min(b1[0], b2[0]) + max(b1[2], b2[2])) / 2
    d.text((x1 * k4 + dx, y1 * k4), top, font=f, fill=fg + (255,), anchor="ls")
    d.text((x2 * k4 + dx, y2 * k4), bottom, font=f, fill=fg + (255,), anchor="ls")
    return im.resize((size, size), Image.LANCZOS)


def wordmark():
    """README banner: monogram square + 'DisplayShot' in the serif, on the dark background."""
    h, pad = 200, 36
    mark = camera_logo(h - 2 * pad + 12, GRAPE, (255, 255, 255))
    f = ImageFont.truetype(SERIF_BOLD, 92 * 2)
    tmp = ImageDraw.Draw(Image.new("RGBA", (1, 1)))
    tw = tmp.textbbox((0, 0), "DisplayShot", font=f)[2] // 2
    w = pad + mark.width + 40 + tw + pad
    im = Image.new("RGBA", (w * 2, h * 2), DARK_BG + (255,))
    im.alpha_composite(mark.resize((mark.width * 2, mark.height * 2), Image.LANCZOS), ((pad - 6) * 2, (pad - 8) * 2))
    d = ImageDraw.Draw(im)
    d.text(((pad + mark.width + 40) * 2, h), "DisplayShot", font=f, fill=LAVENDER + (255,), anchor="lm")
    return im.resize((w, h), Image.LANCZOS)


# --------------------------------------------------------------------------------------------
# Toolbar glyphs: 32x32, Lavender on transparent (button states are drawn by the app).

def glyph_rect():
    im, d = canvas(32, 32)
    d.rounded_rectangle(s((7, 9, 25, 23)), radius=s(2), outline=LAVENDER, width=s(2))
    return finish(im, 32, 32)


def glyph_oval():
    im, d = canvas(32, 32)
    d.ellipse(s((6, 8, 26, 24)), outline=LAVENDER, width=s(2))
    return finish(im, 32, 32)


def glyph_brush():
    im, d = canvas(32, 32)
    rounded_line(d, [(25, 7), (16.5, 15.5)], 2.6, LAVENDER)          # handle
    rounded_line(d, [(16, 16), (13.5, 18.5)], 4.2, LAVENDER)         # ferrule
    d.polygon([tuple(s(p)) for p in [(14.8, 17.2), (12.8, 15.2), (9.5, 18.6), (7.0, 24.6), (13.2, 21.4)]],
              fill=LAVENDER)                                         # bristles, swept to a point
    return finish(im, 32, 32)


def glyph_text():
    im, d = canvas(32, 32)
    f = ImageFont.truetype(SERIF_BOLD, s(25))
    d.text((s(16), s(16.5)), "T", font=f, fill=LAVENDER, anchor="mm")
    return finish(im, 32, 32)


def glyph_undo():
    im, d = canvas(32, 32)
    w = 2.4
    d.polygon([tuple(s(p)) for p in [(5.0, 12.5), (12.0, 7.2), (12.0, 17.8)]], fill=LAVENDER)   # arrowhead
    rounded_line(d, [(11, 12.5), (18.5, 12.5)], w, LAVENDER)                                 # shaft
    d.arc(s((12.5, 12.5 - w / 2 + 0.0, 25.5, 25.5)), start=270, end=90, fill=LAVENDER, width=int(s(w)))   # hook
    rounded_line(d, [(19, 25.5 - w / 2 + 0.05), (12.5, 25.5 - w / 2 + 0.05)], w, LAVENDER)   # tail
    return finish(im, 32, 32)


def glyph_line():
    im, d = canvas(32, 32)
    rounded_line(d, [(8, 24), (24, 8)], 2.4, LAVENDER)
    return finish(im, 32, 32)


def glyph_folder():
    """Save as: a folder (the Explorer dialog)."""
    im, d = canvas(32, 32)
    outline = [(6, 9), (12.5, 9), (14.5, 11.5), (26, 11.5), (26, 24), (6, 24)]
    d.line([tuple(s(p)) for p in outline + [outline[0]]], fill=LAVENDER, width=int(s(2)), joint="curve")
    d.line([tuple(s(p)) for p in [(6, 15), (26, 15)]], fill=LAVENDER, width=int(s(2)))
    return finish(im, 32, 32)


def glyph_gear():
    """Toolbar settings: show or hide buttons."""
    import math
    im, d = canvas(32, 32)
    cx, cy, teeth = 16, 16, 8
    r_out, r_in, r_hole = 10.2, 7.6, 3.2
    pts = []
    for i in range(teeth * 4):
        a = 2 * math.pi * i / (teeth * 4) - math.pi / 2
        r = r_out if i % 4 in (1, 2) else r_in
        pts.append((cx + r * math.cos(a), cy + r * math.sin(a)))
    d.polygon([tuple(s(p)) for p in pts], fill=LAVENDER)
    d.ellipse(s((cx - r_hole, cy - r_hole, cx + r_hole, cy + r_hole)), fill=(0, 0, 0, 0))
    return finish(im, 32, 32)


def glyph_arrow():
    im, d = canvas(32, 32)
    rounded_line(d, [(8, 24), (19.6, 12.4)], 2.4, LAVENDER)
    d.polygon([tuple(s(p)) for p in [(25.5, 6.5), (22.6, 16.0), (16.0, 9.4)]], fill=LAVENDER)
    return finish(im, 32, 32)


def glyph_pixelate():
    """Pixelate: a 4x4 mosaic of squares at mixed intensities."""
    im, d = canvas(32, 32)
    pattern = ["#.o#", "o#.o", "#o#.", ".#o#"]
    alpha = {"#": 255, "o": 150, ".": 70}
    cell, gap, x0, y0 = 4.0, 0.9, 7.0, 7.0
    for r, row in enumerate(pattern):
        for c, ch in enumerate(row):
            x, y = x0 + c * (cell + gap), y0 + r * (cell + gap)
            d.rectangle(s((x, y, x + cell, y + cell)), fill=LAVENDER + (alpha[ch],))
    return finish(im, 32, 32)


def glyph_close():
    im, d = canvas(32, 32)
    rounded_line(d, [(10, 10), (22, 22)], 2.6, LAVENDER)
    rounded_line(d, [(22, 10), (10, 22)], 2.6, LAVENDER)
    return finish(im, 32, 32)


def glyph_copy():
    im, d = canvas(32, 32)
    d.rounded_rectangle(s((12, 6, 25, 20)), radius=s(2), outline=LAVENDER, width=s(2))
    # front sheet: clear the back sheet under it, then outline it
    d.rounded_rectangle(s((7, 11.5, 20, 25.5)), radius=s(2), fill=(0, 0, 0, 0))
    d.rounded_rectangle(s((7, 11.5, 20, 25.5)), radius=s(2), outline=LAVENDER, width=s(2))
    return finish(im, 32, 32)


def glyph_save():
    im, d = canvas(32, 32)
    d.rounded_rectangle(s((7, 7, 25, 25)), radius=s(2.5), outline=LAVENDER, width=s(2))
    d.rectangle(s((11.5, 7, 20.5, 12.5)), outline=LAVENDER, width=s(2))   # shutter
    d.rounded_rectangle(s((10.5, 16.5, 21.5, 25)), radius=s(1), fill=LAVENDER)   # label
    return finish(im, 32, 32)


def glyph_pen(diameter):
    im, d = canvas(28, 28)
    r = diameter / 2
    d.ellipse(s((14 - r, 14 - r, 14 + r, 14 + r)), fill=LAVENDER)
    return finish(im, 28, 28)


def main():
    os.makedirs(ASSETS, exist_ok=True)
    os.makedirs(DOC_IMAGES, exist_ok=True)
    out = lambda name: os.path.join(ASSETS, name)
    doc = lambda name: os.path.join(DOC_IMAGES, name)

    camera_logo(96, GRAPE, (255, 255, 255)).save(out("logo.png"))   # small copy used inside the app
    camera_logo(512, GRAPE, (255, 255, 255)).save(doc("logo.png"))
    camera_logo(512, BLUSH, DARK_BG).save(doc("logo-dark.png"))
    wordmark().save(doc("wordmark.png"))
    sizes = [16, 20, 24, 32, 40, 48, 64, 128, 256]
    icon = camera_logo(1024, GRAPE, (255, 255, 255))
    icon.save(out("app.ico"), sizes=[(n, n) for n in sizes])

    glyphs = {
        "tool-rect": glyph_rect(), "tool-oval": glyph_oval(), "tool-line": glyph_line(), "tool-arrow": glyph_arrow(),
        "tool-brush": glyph_brush(), "tool-pixelate": glyph_pixelate(),
        "tool-text": glyph_text(), "action-undo": glyph_undo(), "action-close": glyph_close(),
        "action-copy": glyph_copy(), "action-save": glyph_save(),
        "pen-1": glyph_pen(3), "pen-2": glyph_pen(6.5), "pen-3": glyph_pen(11), "action-saveas": glyph_folder(), "action-gear": glyph_gear(),
    }
    for name, im in glyphs.items():
        im.save(out(name + ".png"))
    print("wrote", len(glyphs) + 2, "files to", os.path.normpath(ASSETS), "and 3 to", os.path.normpath(DOC_IMAGES))


if __name__ == "__main__":
    main()
