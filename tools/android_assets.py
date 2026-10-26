"""Brand icons from the Karnama logo mark (outlined eight-point Shiraz star framing a "play"), for the website and the Android app:
web favicons/PWA icons (src/karnama-web/public/brand), Android launcher + adaptive + notification icons and splash screens.

Usage: python tools/android_assets.py [dark] [accent] [star]    e.g.  python tools/android_assets.py "#0b1b34" "#3b6cff" "#d4bc8b"
(star outline in the gold second accent, the "play" in the brand colour).
White-label: run again with the agency's colours (or replace the PNGs with exported artwork) before publishing.
The same shapes live in src/karnama-web/src/components/app/logo-mark.tsx.
"""
import os, sys
from PIL import Image, ImageDraw

ROOT = os.path.join(os.path.dirname(__file__), "..")
RES = os.path.join(ROOT, "mobile", "android", "app", "src", "main", "res")
WEB = os.path.join(ROOT, "src", "karnama-web", "public", "brand")
bg = sys.argv[1] if len(sys.argv) > 1 else "#0b1b34"
fg = sys.argv[2] if len(sys.argv) > 2 else "#3b6cff"
gold = sys.argv[3] if len(sys.argv) > 3 else "#d4bc8b"

STAR = [(50, 16.1), (59.94, 26), (73.97, 26.03), (74, 40.06), (83.9, 50), (74, 59.94), (73.97, 73.97), (59.94, 74),
        (50, 83.9), (40.06, 74), (26.03, 73.97), (26, 59.94), (16.1, 50), (26, 40.06), (26.03, 26.03), (40.06, 26)]
PLAY = [(44, 38), (44, 62), (64, 50)]
grow = lambda pts, f: [(50 + (x - 50) * f, 50 + (y - 50) * f) for x, y in pts]
SS = 4  # supersampling for smooth edges


def mark(size, box=None, plate=True, star_color=gold, cut=True, radius=24, play_color=fg):
    """The mark at `size` px; `box` = drawn width of the 100-unit artboard (for safe zones)."""
    box = box or size
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    off = (size - box) / 2 * SS
    k = box * SS / 100
    P = lambda pts: [(off + x * k, off + y * k) for x, y in pts]
    if plate:
        d.rounded_rectangle((off, off, off + 100 * k, off + 100 * k), radius=radius * k, fill=bg)
    d.polygon(P(grow(STAR, 1.12)), fill=star_color)
    hole = Image.new("L", (big, big), 0)
    ImageDraw.Draw(hole).polygon(P(grow(STAR, 0.86)), fill=255)
    if plate:
        d.polygon(P(grow(STAR, 0.86)), fill=bg)
    else:   # transparent inside the outline
        img.putalpha(Image.composite(Image.new("L", (big, big), 0), img.getchannel("A"), hole))
        d = ImageDraw.Draw(img)
    d.polygon(P(PLAY), fill=play_color)
    return img.resize((size, size), Image.LANCZOS)


def notification(size):
    """White star with a transparent play cut (Android tints it)."""
    big = size * SS
    img = Image.new("RGBA", (big, big), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    k = big / 100
    P = lambda pts: [(x * k, y * k) for x, y in pts]
    d.polygon(P(grow(STAR, 1.12)), fill="#ffffff")
    d.polygon(P(grow(STAR, 0.86)), fill=(0, 0, 0, 0))
    d.polygon(P(PLAY), fill="#ffffff")
    return img.resize((size, size), Image.LANCZOS)


def save(img, path):
    os.makedirs(os.path.dirname(path), exist_ok=True)
    img.save(path, optimize=True)


# Website / PWA
save(mark(48), os.path.join(WEB, "favicon-48.png"))
save(mark(192), os.path.join(WEB, "icon-192.png"))
save(mark(512), os.path.join(WEB, "icon-512.png"))
full = Image.new("RGBA", (180, 180), bg); full.alpha_composite(mark(180, 180, plate=False)); save(full, os.path.join(WEB, "apple-touch-icon.png"))
mask512 = Image.new("RGBA", (512, 512), bg); mask512.alpha_composite(mark(512, 340, plate=False)); save(mask512, os.path.join(WEB, "icon-maskable-512.png"))

# Android
if os.path.isdir(RES):
    for folder, px in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
        save(mark(px), os.path.join(RES, f"mipmap-{folder}", "ic_launcher.png"))
        circle = Image.new("RGBA", (px, px), (0, 0, 0, 0))
        cmask = Image.new("L", (px * SS, px * SS), 0)
        ImageDraw.Draw(cmask).ellipse((0, 0, px * SS - 1, px * SS - 1), fill=255)
        sq = Image.new("RGBA", (px, px), bg); sq.alpha_composite(mark(px, int(px * 0.92), plate=False))
        circle.paste(sq, (0, 0), cmask.resize((px, px), Image.LANCZOS))
        save(circle, os.path.join(RES, f"mipmap-{folder}", "ic_launcher_round.png"))
        fpx = px * 108 // 48   # adaptive foreground: 108dp canvas, mark inside the 66dp safe zone
        save(mark(fpx, fpx * 66 // 108, plate=False), os.path.join(RES, f"mipmap-{folder}", "ic_launcher_foreground.png"))
        save(notification(px // 2), os.path.join(RES, f"drawable-{folder}", "ic_stat_karnama.png"))
    with open(os.path.join(RES, "values", "ic_launcher_background.xml"), "w") as f:
        f.write(f'<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">{bg}</color>\n</resources>\n')
    for folder in os.listdir(RES):
        path = os.path.join(RES, folder, "splash.png")
        if os.path.exists(path):
            w, h = Image.open(path).size
            img = Image.new("RGBA", (w, h), bg)
            m = mark(min(w, h) // 3, plate=False)
            img.alpha_composite(m, ((w - m.width) // 2, (h - m.height) // 2))
            img.convert("RGB").save(path, optimize=True)
print("brand icons written")
