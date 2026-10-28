"""Exo Cinema's pictures for Discord, from the Ticket of tools/make-logo.py and in the set Exo Browser's and Exo
Launcher's are (their .github/discord): the bot's avatar, the icon, and the releases channel's banner.

The avatar is a disc of black glass lit from the top with a thin rim, as Exo Browser's: Discord crops it to a circle,
so it fills its square and the rim sits on that circle. The ticket is Exo Cinema's own white glass, larger than on the
app icon since the disc has no squircle to leave room for. The banner is the releases card of Exo's server: the app
icon on black, its name and one line.

    python tools/make-discord.py

Writes .github/discord/exo-cinema-avatar.png (1024), exo-cinema-icon.png (512), banner-cinema-releases-v3.png (1200x400),
and tools/discord-check.png: the avatar as Discord shows it (circles at 128, 80, 40 and 32 px) to check by eye.
"""
from __future__ import annotations

import importlib.util
from pathlib import Path

from PIL import Image, ImageChops, ImageDraw, ImageFilter, ImageFont

REPO = Path(__file__).resolve().parent.parent
OUT = REPO / ".github" / "discord"
_spec = importlib.util.spec_from_file_location("make_logo", REPO / "tools" / "make-logo.py")
logo = importlib.util.module_from_spec(_spec)
_spec.loader.exec_module(logo)

# Inter, as Exo draws it; Exo Launcher keeps the variable TTF (its fonts ship as woff2 here, which Pillow can't read).
INTER = REPO.parent / "ExoLauncher" / "ExoLauncher" / "Assets" / "Fonts" / "Inter-Variable.ttf"
GLYPH_SCALE = 0.94  # the ticket on the disc against its 64 box: 65% of the width (a ticket is wide and short)


def avatar(size: int = 1024) -> Image.Image:
    ss = 2
    px = size * ss
    disc = Image.new("L", (px, px), 0)
    ImageDraw.Draw(disc).ellipse([0, 0, px - 1, px - 1], fill=255)
    img = Image.new("RGBA", (px, px), (43, 43, 47, 255))
    img.paste(logo._vgrad(px, logo.PLATE), (0, 0), disc)

    # Light from the top, inside the disc.
    sheen = Image.new("L", (px, px), 0)
    ImageDraw.Draw(sheen).ellipse([-0.2 * px, -0.65 * px, 1.2 * px, 0.4 * px], fill=26)
    img = logo._over(img, (255, 255, 255), ImageChops.multiply(sheen.filter(ImageFilter.GaussianBlur(px * 0.08)), disc))

    # The ticket, scaled about the centre of its 64 box.
    g = int(px * GLYPH_SCALE)
    gmask = Image.new("L", (px, px), 0)
    gmask.paste(logo.glyph_mask(g, 256, True), ((px - g) // 2, (px - g) // 2))
    bloom = gmask.filter(ImageFilter.GaussianBlur(px * 0.05)).point(lambda v: v * 0.18)
    img = logo._over(img, (255, 255, 255), ImageChops.multiply(bloom, disc))
    shadow = Image.new("L", (px, px), 0)
    shadow.paste(gmask, (0, round(px * 0.02)))
    shadow = shadow.filter(ImageFilter.GaussianBlur(px * 0.018)).point(lambda v: v * 0.75)
    img = logo._over(img, (0, 0, 0), ImageChops.multiply(shadow, disc))
    top = (px - g) / 2 / px + logo.T_Y0 / logo.BOX * GLYPH_SCALE
    foot = (px - g) / 2 / px + logo.T_Y1 / logo.BOX * GLYPH_SCALE
    glass = logo._vgrad(px, [(0.0, logo.GLASS_TOP), (top, logo.GLASS_TOP), (top + 0.55 * (foot - top), logo.GLASS_MID), (foot, logo.GLASS_FOOT), (1.0, logo.GLASS_FOOT)])
    img.paste(glass, (0, 0), gmask)

    # The rim: a thin ring on the circle Discord crops to, bright at the top and fading down.
    width = px * 0.011
    ring = Image.new("L", (px, px), 0)
    d = ImageDraw.Draw(ring)
    d.ellipse([0, 0, px - 1, px - 1], fill=255)
    d.ellipse([width, width, px - 1 - width, px - 1 - width], fill=0)
    light = logo._vgrad(px, [(0.0, (255, 255, 255, 150)), (0.4, (255, 255, 255, 40)), (0.75, (255, 255, 255, 22)), (1.0, (255, 255, 255, 60))])
    img = logo._over(img, (255, 255, 255), ImageChops.multiply(light.getchannel("A"), ring))
    return img.resize((size, size), Image.LANCZOS)


def _font(size: int, weight: int, opsz: int) -> ImageFont.FreeTypeFont:
    """Inter at `size` px, its weight and optical size set (14 is the text cut, 32 the Display cut)."""
    f = ImageFont.truetype(str(INTER), size)
    f.set_variation_by_axes([opsz, weight])
    return f


def banner() -> Image.Image:
    """The releases channel's banner, as Exo Launcher's banner-browser-releases-v3: the icon, the name and one line."""
    # Measured from that banner: the icon 212 px at (78, 94); the name in Inter Display 67 px on a baseline at 195 and
    # the line in Inter's text cut 40 px, gray 157, on one at 253, both starting at x 338 (drawn from 334, Inter's side bearing); black with a light of about 17 along
    # the top.
    w, h = 1200, 400
    img = Image.new("RGBA", (w, h), (2, 2, 2, 255))
    glow = Image.new("L", (w, h), 0)
    d = ImageDraw.Draw(glow)
    d.ellipse([-300, -420, 1500, 230], fill=17)
    d.ellipse([60, -160, 700, 200], fill=8)
    img = logo._over(img, (255, 255, 255), glow.filter(ImageFilter.GaussianBlur(110)))
    icon = logo.render(256).resize((212, 212), Image.LANCZOS)
    shade = Image.new("L", (w, h), 0)
    shade.paste(icon.getchannel("A"), (78, 104))
    img = logo._over(img, (0, 0, 0), shade.filter(ImageFilter.GaussianBlur(18)).point(lambda v: v * 0.8))
    img.alpha_composite(icon, (78, 94))
    t = ImageDraw.Draw(img)
    t.text((334, 195), "Exo Cinema releases", font=_font(67, 650, 32), fill=(255, 255, 255, 255), anchor="ls")
    t.text((334, 253), "New builds, delivered automatically", font=_font(40, 400, 14), fill=(157, 157, 160, 255), anchor="ls")
    return img.convert("RGB")


def main() -> None:
    OUT.mkdir(parents=True, exist_ok=True)
    av = avatar()
    av.save(OUT / "exo-cinema-avatar.png", optimize=True)
    logo.render(512).save(OUT / "exo-cinema-icon.png", optimize=True)
    banner().save(OUT / "banner-cinema-releases-v3.png", optimize=True)

    # As Discord shows the avatar: cropped to circles on its dark theme, next to Exo Browser's for comparison.
    sheet = Image.new("RGBA", (760, 300), (49, 51, 56, 255))
    browser = REPO.parent / "ExoBrowser" / ".github" / "discord" / "exo-browser-avatar.png"
    rows = [av] + ([Image.open(browser).convert("RGBA")] if browser.exists() else [])
    for r, src in enumerate(rows):
        x = 20
        for s in (128, 80, 40, 32):
            a = src.resize((s, s), Image.LANCZOS)
            m = Image.new("L", (s * 4, s * 4), 0)
            ImageDraw.Draw(m).ellipse([0, 0, s * 4 - 1, s * 4 - 1], fill=255)
            a.putalpha(ImageChops.multiply(a.getchannel("A"), m.resize((s, s), Image.LANCZOS)))
            sheet.alpha_composite(a, (x, 10 + r * 145 + (128 - s) // 2))
            x += s + 40
    sheet.save(REPO / "tools" / "discord-check.png")
    print("ok")


if __name__ == "__main__":
    main()
