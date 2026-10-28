"""Generate Exo Cinema's app icon and web marks from one geometry: Ticket.

The mark is a cinema ticket: one slab of white glass, notched on both sides, with its tear line, sheared 11° about
the vertical centre like Exo Launcher's E. The icon is built exactly as Exo Launcher's (its tools/make-logo.py,
whose plate, light and glass this copies): a continuous-curvature squircle of black glass, the glyph in white
glass lit from above with a shadow under it and a faint bloom, and detail that follows size. At 32 px and under
the plate is a plain gradient and the glyph flat white. At every size the ticket keeps its 11° slant, as Exo Launcher's
E keeps its lean at 16 px: its top and bottom sit on whole pixels so they stay crisp, and its notches and tear line
never get thinner than a pixel, so they read at 16 px instead of washing out.

    python tools/make-logo.py

Writes src/ExoCinema/Assets/ExoCinema.ico (the app and its window), src/ExoCinema/wwwroot/logo.svg (the intro),
favicon.svg, mark.svg (the glyph alone, for the Bar), logo.png, and tools/logo-sizes.png to check by eye.
"""
from __future__ import annotations

import math
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter

BOX = 64.0
SHEAR_DEG = 11.0
SQUIRCLE_N = 4.4

# Ticket, in the 64 box before the shear.
T_X0, T_X1, T_Y0, T_Y1 = 10.0, 54.0, 19.0, 45.0
T_CORNER = 2.6
NOTCH_R = 4.6          # half-circles cut from the middle of each short side
TEAR_X, TEAR_W = 41.0, 1.7
TEAR_DASH, TEAR_GAP = 2.6, 2.1
TEAR_Y0, TEAR_Y1 = 21.0, 43.0

PLATE = ((0.0, (34, 34, 38)), (0.45, (12, 12, 14)), (1.0, (4, 4, 5)))
PLATE_SMALL = ((0.0, (26, 26, 29)), (1.0, (6, 6, 7)))
GLASS_TOP, GLASS_MID, GLASS_FOOT = (255, 255, 255), (247, 247, 250), (216, 216, 225)
ICO_SIZES = (16, 20, 24, 32, 40, 48, 64, 128, 256)

REPO = Path(__file__).resolve().parent.parent
APP = REPO / "src" / "ExoCinema"
TAN = math.tan(math.radians(SHEAR_DEG))


def squircle(size: float, inset: float = 0.0, steps: int = 720) -> list[tuple[float, float]]:
    a, c = size / 2 - inset, size / 2
    pts = []
    for i in range(steps):
        t = 2 * math.pi * i / steps
        ct, st = math.cos(t), math.sin(t)
        pts.append((c + a * math.copysign(abs(ct) ** (2 / SQUIRCLE_N), ct), c + a * math.copysign(abs(st) ** (2 / SQUIRCLE_N), st)))
    return pts


def rounded(pts: list[tuple[float, float]], r: float, steps: int = 12) -> list[tuple[float, float]]:
    """A closed polygon whose corners turn through a curve that leaves each edge `r` from the corner."""
    out = []
    for i, v in enumerate(pts):
        p0, p1 = pts[i - 1], pts[(i + 1) % len(pts)]
        la, lb = math.dist(p0, v), math.dist(v, p1)
        k = min(r, la / 2, lb / 2)
        a = (v[0] + (p0[0] - v[0]) * k / la, v[1] + (p0[1] - v[1]) * k / la)
        b = (v[0] + (p1[0] - v[0]) * k / lb, v[1] + (p1[1] - v[1]) * k / lb)
        for j in range(steps + 1):
            t = j / steps
            out.append(((1 - t) ** 2 * a[0] + 2 * (1 - t) * t * v[0] + t * t * b[0], (1 - t) ** 2 * a[1] + 2 * (1 - t) * t * v[1] + t * t * b[1]))
    return out


def _shear(size: float):
    return lambda x, y: (x + (size / 2 - y) * TAN, y)


def body(size: float, snap: bool, icon: int | None = None) -> list[tuple[float, float]]:
    """The ticket's outline in pixels for a `size` box, sheared about the centre; `snap` puts its top and bottom on
    whole pixels of an `icon`-sized image (the box may be supersampled) so small sizes stay crisp."""
    k, sh = size / BOX, _shear(size)
    y0, y1 = T_Y0 * k, T_Y1 * k
    if snap:
        step = size / (icon or size)
        y0, y1 = round(y0 / step) * step, round(y1 / step) * step
    return [sh(T_X0 * k, y0), sh(T_X1 * k, y0), sh(T_X1 * k, y1), sh(T_X0 * k, y1)]


def cuts(size: float, icon: int = 256) -> list[list[tuple[float, float]]]:
    """What is cut out of the ticket: the two notches and the tear line's dashes, sheared with it. At a small `icon`
    size none of them gets thinner than a pixel there (one pixel is BOX / icon in the 64 box)."""
    k, sh = size / BOX, _shear(size)
    px = BOX / icon
    notch = max(NOTCH_R, 1.6 * px)
    tear_w, dash, gap = max(TEAR_W, px), max(TEAR_DASH, px), max(TEAR_GAP, px)
    mid = (T_Y0 + T_Y1) / 2
    out = []
    for cx in (T_X0, T_X1):
        out.append([sh((cx + notch * math.cos(a)) * k, (mid + notch * math.sin(a)) * k) for a in (2 * math.pi * i / 64 for i in range(64))])
    y = TEAR_Y0
    while y < TEAR_Y1:
        y2 = min(y + dash, TEAR_Y1)
        x0, x1 = (TEAR_X - tear_w / 2) * k, (TEAR_X + tear_w / 2) * k
        out.append([sh(x0, y * k), sh(x1, y * k), sh(x1, y2 * k), sh(x0, y2 * k)])
        y += dash + gap
    return out


def _mask(px: int, polys) -> Image.Image:
    m = Image.new("L", (px, px), 0)
    d = ImageDraw.Draw(m)
    for p in polys:
        d.polygon(p, fill=255)
    return m


def glyph_mask(px: int, size: int, rich: bool) -> Image.Image:
    """The ticket as a mask: its outline (corners softened) less its notches and tear line."""
    outline = rounded(body(px, snap=size <= 48, icon=size), (T_CORNER if rich else T_CORNER * 0.6) * px / BOX)
    return ImageChops.subtract(_mask(px, [outline]), _mask(px, cuts(px, icon=size)))



def _vgrad(px: int, stops) -> Image.Image:
    col = Image.new("RGBA", (1, px))
    for y in range(px):
        t = y / max(1, px - 1)
        for (t0, c0), (t1, c1) in zip(stops, stops[1:]):
            if t0 <= t <= t1:
                f = (t - t0) / (t1 - t0) if t1 > t0 else 0.0
                c = tuple(round(a + (b - a) * f) for a, b in zip(c0, c1))
                col.putpixel((0, y), c if len(c) == 4 else (*c, 255))
                break
    return col.resize((px, px), Image.NEAREST)


def _over(img: Image.Image, rgb: tuple, alpha: Image.Image) -> Image.Image:
    return Image.composite(Image.new("RGBA", img.size, (*rgb, 255)), img, alpha)


def render(size: int) -> Image.Image:
    """The icon at `size` px, supersampled so curves and shears stay clean."""
    ss = 4 if size >= 128 else 8
    px = size * ss
    rich = size >= 48
    plate = _mask(px, [squircle(px)])
    img = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    img.paste(_vgrad(px, PLATE if size >= 24 else PLATE_SMALL), (0, 0), plate)
    if rich:
        sheen = Image.new("L", (px, px), 0)
        ImageDraw.Draw(sheen).ellipse([-0.25 * px, -0.62 * px, 1.25 * px, 0.42 * px], fill=30)
        img = _over(img, (255, 255, 255), ImageChops.multiply(sheen.filter(ImageFilter.GaussianBlur(px * 0.08)), plate))

    gmask = glyph_mask(px, size, rich)
    if rich:
        bloom = gmask.filter(ImageFilter.GaussianBlur(px * 0.05)).point(lambda v: v * 0.16)
        img = _over(img, (255, 255, 255), ImageChops.multiply(bloom, plate))
        shadow = Image.new("L", (px, px), 0)
        shadow.paste(gmask, (0, round(px * 0.018)))
        shadow = shadow.filter(ImageFilter.GaussianBlur(px * 0.016)).point(lambda v: v * 0.7)
        img = _over(img, (0, 0, 0), ImageChops.multiply(shadow, plate))
        y0, y1 = T_Y0 / BOX, T_Y1 / BOX
        glass = _vgrad(px, [(0.0, GLASS_TOP), (y0, GLASS_TOP), (y0 + 0.55 * (y1 - y0), GLASS_MID), (y1, GLASS_FOOT), (1.0, GLASS_FOOT)])
        img.paste(glass, (0, 0), gmask)
    else:
        img.paste((*GLASS_TOP, 255), (0, 0), gmask)

    if size >= 24:
        width = max(px * 0.012, ss * (1.0 if size < 64 else 1.2))
        ring = ImageChops.subtract(plate, _mask(px, [squircle(px, inset=width)]))
        light = _vgrad(px, [(0.0, (255, 255, 255, 120 if rich else 70)), (0.35, (255, 255, 255, 18)), (0.7, (255, 255, 255, 10)), (1.0, (255, 255, 255, 40 if rich else 20))])
        img = _over(img, (255, 255, 255), ImageChops.multiply(light.getchannel("A"), ring))

    img.putalpha(plate)
    return img.resize((size, size), Image.LANCZOS)


def _path(pts) -> str:
    return "M" + " L".join(f"{x:.2f} {y:.2f}" for x, y in pts) + " Z"


def _glyph_mask_svg() -> str:
    """The ticket for SVG: a mask of its outline less its cuts."""
    outline = _path(rounded(body(BOX, snap=False), T_CORNER))
    holes = " ".join(_path(p) for p in cuts(BOX))
    return (f'<mask id="t" maskUnits="userSpaceOnUse" x="0" y="0" width="64" height="64">'
            f'<path d="{outline}" fill="#fff"/><path d="{holes}" fill="#000"/></mask>')


def svg(small: bool) -> str:
    """The icon as SVG in the 64 box; `small` keeps only what reads at 16 px."""
    plate = _path(squircle(BOX, steps=240))
    stops = PLATE_SMALL if small else PLATE
    defs = ['<linearGradient id="p" x1="0" y1="0" x2="0" y2="1">' + "".join(f'<stop offset="{t}" stop-color="#{r:02x}{g:02x}{b:02x}"/>' for t, (r, g, b) in stops) + "</linearGradient>", _glyph_mask_svg()]
    out = [f'<path d="{plate}" fill="url(#p)"/>']
    if small:
        out.append('<rect width="64" height="64" fill="#fff" mask="url(#t)"/>')
    else:
        defs += [
            '<radialGradient id="s" cx="0.5" cy="-0.1" r="0.75"><stop offset="0" stop-color="#fff" stop-opacity="0.12"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></radialGradient>',
            '<linearGradient id="r" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity="0.47"/><stop offset="0.35" stop-color="#fff" stop-opacity="0.07"/><stop offset="0.7" stop-color="#fff" stop-opacity="0.04"/><stop offset="1" stop-color="#fff" stop-opacity="0.16"/></linearGradient>',
            '<filter id="b" x="-20%" y="-20%" width="140%" height="140%"><feGaussianBlur stdDeviation="3.2"/></filter>',
            '<filter id="d" x="-20%" y="-20%" width="140%" height="160%"><feGaussianBlur stdDeviation="1"/></filter>',
            f'<clipPath id="c"><path d="{plate}"/></clipPath>',
            f'<linearGradient id="g" gradientUnits="userSpaceOnUse" x1="0" y1="{T_Y0}" x2="0" y2="{T_Y1}"><stop offset="0" stop-color="#fff"/><stop offset="0.55" stop-color="#f7f7fa"/><stop offset="1" stop-color="#d8d8e1"/></linearGradient>',
        ]
        out += [
            f'<path d="{plate}" fill="url(#s)"/>',
            '<g clip-path="url(#c)">'
            '<g filter="url(#b)" opacity="0.16"><rect width="64" height="64" fill="#fff" mask="url(#t)"/></g>'
            '<g filter="url(#d)" opacity="0.7" transform="translate(0 1.15)"><rect width="64" height="64" fill="#000" mask="url(#t)"/></g>'
            '</g>',
            '<rect width="64" height="64" fill="url(#g)" mask="url(#t)"/>',
            f'<path d="{_path(squircle(BOX, inset=0.38, steps=240))}" fill="none" stroke="url(#r)" stroke-width="0.77"/>',
        ]
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 64 64"><defs>{"".join(defs)}</defs>{"".join(out)}</svg>\n'


def mark_svg() -> str:
    """The glyph alone in white (the Void ink), cropped to its own box, for the Bar and the sign-in sheet."""
    pts = rounded(body(BOX, snap=False), T_CORNER)
    xs, ys = [x for x, _ in pts], [y for _, y in pts]
    x0, y0, x1, y1 = min(xs), min(ys), max(xs), max(ys)
    side = max(x1 - x0, y1 - y0)
    cx, cy = (x0 + x1) / 2, (y0 + y1) / 2
    vb = f"{cx - side / 2:.2f} {cy - side / 2:.2f} {side:.2f} {side:.2f}"
    return f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="{vb}"><defs>{_glyph_mask_svg()}</defs><rect width="64" height="64" fill="#fff" mask="url(#t)"/></svg>\n'


def bar_mark_svg() -> str:
    """The mark for the Bar's Search capsule, as Exo Launcher's (its glyph in the ink, slanted, drawn for its size).
    At 20 px a sheared cut lands between pixels and goes gray, so the cuts are placed by hand on whole pixels, as
    Exo Launcher hints its E at 16 to 24 px: the body keeps its 11° slant with its top and bottom on whole pixels, the
    notches are 2 px deep, and the tear line is three black dots that step left with the slant."""
    size = 20
    k = size / BOX
    y0, y1 = round(T_Y0 * k), round(T_Y1 * k)

    def x_at(x: float, y: float) -> float:
        return x * k + (size / 2 - y) * TAN

    body = [(x_at(T_X0, y0), y0), (x_at(T_X1, y0), y0), (x_at(T_X1, y1), y1), (x_at(T_X0, y1), y1)]
    mid = (y0 + y1) // 2
    left, right = round(x_at(T_X0, mid)), round(x_at(T_X1, mid))
    holes = [(left - 1, mid - 1, 3, 2), (right - 2, mid - 1, 3, 2)]
    for y in range(y0 + 1, y1 - 1, 2):
        holes.append((math.floor(x_at(TEAR_X, y + 0.5)), y, 1, 1))
    rects = "".join(f'<rect x="{x}" y="{y}" width="{w}" height="{h}" fill="#000" shape-rendering="crispEdges"/>' for x, y, w, h in holes)
    return (f'<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 {size} {size}" width="{size}" height="{size}">'
            f'<defs><mask id="m" maskUnits="userSpaceOnUse" x="0" y="0" width="{size}" height="{size}">'
            f'<path d="{_path(body)}" fill="#fff"/>{rects}</mask></defs>'
            f'<rect width="{size}" height="{size}" fill="#fff" mask="url(#m)"/></svg>\n')


def main() -> None:
    frames = [render(s) for s in ICO_SIZES]
    assets = APP / "Assets"
    assets.mkdir(parents=True, exist_ok=True)
    frames[-1].save(assets / "ExoCinema.ico", format="ICO", sizes=[(s, s) for s in ICO_SIZES], append_images=frames[:-1])
    web = APP / "wwwroot"
    frames[-1].save(web / "logo.png", optimize=True)
    (web / "logo.svg").write_text(svg(False), encoding="utf-8")
    (web / "favicon.svg").write_text(svg(True), encoding="utf-8")
    (web / "mark.svg").write_text(bar_mark_svg(), encoding="utf-8")

    # Every size side by side on dark, then 16 to 48 px enlarged 4x, then a light strip, to check by eye.
    sheet = Image.new("RGBA", (1200, 420), (12, 12, 13, 255))
    x = 16
    for s, f in zip(ICO_SIZES, frames):
        sheet.alpha_composite(f, (x, 16))
        if s <= 48:
            sheet.alpha_composite(f.resize((s * 4, s * 4), Image.NEAREST), (x, 290 - s * 2))
        x += (s * 4 if s <= 48 else s) + 14
    light = Image.new("RGBA", (300, 60), (243, 243, 243, 255))
    lx = 10
    for s in (16, 20, 24, 32, 48):
        light.alpha_composite(frames[ICO_SIZES.index(s)], (lx, (60 - s) // 2))
        lx += s + 14
    sheet.alpha_composite(light, (16, 350))
    sheet.save(REPO / "tools" / "logo-sizes.png")
    print("ok")


if __name__ == "__main__":
    main()
