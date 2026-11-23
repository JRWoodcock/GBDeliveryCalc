"""Generates the GB DeliveryCalc icon, MSIX logos and Microsoft Store listing images.

Run from the repository root:  python packaging/art/generate_art.py
Needs Pillow and the Segoe UI fonts that ship with Windows.
"""
import os
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
ASSETS = os.path.join(ROOT, "packaging", "Assets")
STORE = os.path.join(ROOT, "packaging", "StoreListing")
APP_ASSETS = os.path.join(ROOT, "src", "GBDeliveryCalc.App", "Assets")

NAVY = (30, 58, 138)
BLUE = (37, 99, 235)
WHITE = (255, 255, 255)
AMBER = (245, 158, 11)
FONT_BOLD = r"C:\Windows\Fonts\segoeuib.ttf"
FONT_SEMI = r"C:\Windows\Fonts\seguisb.ttf"
FONT_REG = r"C:\Windows\Fonts\segoeui.ttf"


def gradient(size, top, bottom):
    w, h = size
    img = Image.new("RGB", size, top)
    px = img.load()
    for y in range(h):
        t = y / max(1, h - 1)
        c = tuple(int(top[i] + (bottom[i] - top[i]) * t) for i in range(3))
        for x in range(w):
            px[x, y] = c
    return img


def glyph(size, color=WHITE, accent=AMBER, text_color=NAVY):
    """The truck-and-calculator mark on a transparent square canvas of the given size."""
    s = size / 1000.0
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)

    def r(x0, y0, x1, y1, rad, fill):
        d.rounded_rectangle([x0 * s, y0 * s, x1 * s, y1 * s], radius=rad * s, fill=fill)

    # Cargo box and cab
    r(120, 300, 640, 690, 40, color)
    r(660, 420, 880, 690, 40, color)
    d.polygon([(660 * s, 420 * s), (790 * s, 420 * s), (880 * s, 540 * s), (660 * s, 540 * s)], fill=color)
    # Cab window
    d.polygon([(700 * s, 460 * s), (775 * s, 460 * s), (835 * s, 535 * s), (700 * s, 535 * s)], fill=NAVY)
    # Wheels
    for cx in (270, 760):
        d.ellipse([(cx - 95) * s, (630 * s), (cx + 95) * s, (820 * s)], fill=color)
        d.ellipse([(cx - 48) * s, (677 * s), (cx + 48) * s, (773 * s)], fill=NAVY)
    # "GB" on the box
    font = ImageFont.truetype(FONT_BOLD, int(210 * s))
    tw = d.textlength("GB", font=font)
    d.text(((380 - tw / s / 2) * s, 365 * s), "GB", font=font, fill=text_color)
    # Calculator badge (top right)
    r(600, 130, 880, 410, 50, accent)
    for row in range(2):
        for col in range(2):
            x0 = 640 + col * 120
            y0 = 175 + row * 120
            r(x0, y0, x0 + 80, y0 + 80, 14, NAVY)
    # plus and equals signs on the keys
    d.rectangle([660 * s, 211 * s, 700 * s, 219 * s], fill=accent)
    d.rectangle([676 * s, 195 * s, 684 * s, 235 * s], fill=accent)
    d.rectangle([780 * s, 203 * s, 820 * s, 211 * s], fill=accent)
    d.rectangle([780 * s, 223 * s, 820 * s, 231 * s], fill=accent)
    return img


def tile(size, rounded=True, glyph_scale=0.82):
    """Glyph on the navy gradient, optionally with rounded corners (used for icons)."""
    bg = gradient((size, size), BLUE, NAVY).convert("RGBA")
    if rounded:
        mask = Image.new("L", (size, size), 0)
        ImageDraw.Draw(mask).rounded_rectangle([0, 0, size - 1, size - 1], radius=int(size * 0.18), fill=255)
        bg.putalpha(mask)
    g = glyph(int(size * glyph_scale))
    off = (size - g.size[0]) // 2
    bg.alpha_composite(g, (off, off))
    return bg


def save(img, *parts):
    path = os.path.join(*parts)
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path)
    print("wrote", os.path.relpath(path, ROOT), img.size)


def hi(size):
    """Render large and downscale for clean edges at small sizes."""
    return lambda make: make(max(size * 4, 256)).resize((size, size), Image.LANCZOS)


def wide(w, h):
    """Wide tile: glyph on the left, the product name on two lines sized to fit the right side."""
    img = gradient((w, h), BLUE, NAVY).convert("RGBA")
    g = glyph(int(h * 0.9))
    img.alpha_composite(g, (int(w * 0.03), int(h * 0.05)))
    d = ImageDraw.Draw(img)
    x = int(w * 0.03) + int(h * 0.9) + int(w * 0.01)
    room = w - x - int(w * 0.05)
    size = int(h * 0.30)
    while size > 8 and d.textlength("DeliveryCalc", font=ImageFont.truetype(FONT_SEMI, size)) > room:
        size -= 1
    f2 = ImageFont.truetype(FONT_SEMI, size)
    f1 = ImageFont.truetype(FONT_BOLD, int(size * 0.75))
    d.text((x, int(h * 0.50)), "GB", font=f1, fill=WHITE, anchor="ls")
    d.text((x, int(h * 0.52)), "DeliveryCalc", font=f2, fill=WHITE, anchor="lt")
    return img


def main():
    # MSIX logos: scale-100 and scale-200
    for scale in (1, 2):
        suffix = ".png" if scale == 1 else ".scale-200.png"
        save(hi(44 * scale)(lambda n: tile(n, rounded=False, glyph_scale=0.92)), ASSETS, "Square44x44Logo" + suffix)
        save(hi(150 * scale)(lambda n: tile(n, rounded=False, glyph_scale=0.70)), ASSETS, "Square150x150Logo" + suffix)
        save(hi(50 * scale)(lambda n: tile(n, rounded=False, glyph_scale=0.90)), ASSETS, "StoreLogo" + suffix)
        save(wide(310 * scale, 150 * scale), ASSETS, "Wide310x150Logo" + suffix)
    # Taskbar / Start list icons without a plate (transparent background, navy glyph)
    for t in (16, 24, 32, 48, 256):
        g = glyph(max(t * 4, 256), color=NAVY, text_color=WHITE).resize((t, t), Image.LANCZOS)
        save(g, ASSETS, f"Square44x44Logo.targetsize-{t}_altform-unplated.png")
        save(hi(t)(lambda n: tile(n, rounded=False, glyph_scale=0.92)), ASSETS, f"Square44x44Logo.targetsize-{t}.png")

    # App window icon and .ico for the executable
    save(tile(256), APP_ASSETS, "AppIcon.png")
    big = tile(256)
    ico_path = os.path.join(APP_ASSETS, "AppIcon.ico")
    big.save(ico_path, sizes=[(16, 16), (24, 24), (32, 32), (48, 48), (64, 64), (128, 128), (256, 256)])
    print("wrote", os.path.relpath(ico_path, ROOT))

    # Microsoft Store listing art
    save(tile(300, rounded=False, glyph_scale=0.78), STORE, "StoreLogo-300x300.png")
    for n in (1080, 2160):
        save(tile(n, rounded=False, glyph_scale=0.70), STORE, f"BoxArt-{n}x{n}.png")
    poster = gradient((720, 1080), BLUE, NAVY).convert("RGBA")
    pg = glyph(560)
    poster.alpha_composite(pg, (80, 200))
    d = ImageDraw.Draw(poster)
    d.text((360, 790), "GB DeliveryCalc", font=ImageFont.truetype(FONT_BOLD, 64), fill=WHITE, anchor="mm")
    d.text((360, 870), "Know the real cost of every delivery", font=ImageFont.truetype(FONT_REG, 32), fill=(219, 234, 254), anchor="mm")
    save(poster, STORE, "Poster-720x1080.png")
    hero = gradient((1920, 1080), BLUE, NAVY).convert("RGBA")
    hero.alpha_composite(glyph(720), (110, 180))
    d = ImageDraw.Draw(hero)
    d.text((880, 440), "GB DeliveryCalc", font=ImageFont.truetype(FONT_BOLD, 112), fill=WHITE)
    d.text((884, 590), "Delivery and installation costs,", font=ImageFont.truetype(FONT_REG, 54), fill=(219, 234, 254))
    d.text((884, 660), "worked out before you quote.", font=ImageFont.truetype(FONT_REG, 54), fill=(219, 234, 254))
    save(hero, STORE, "HeroArt-1920x1080.png")


if __name__ == "__main__":
    main()
