"""Android launcher icons, notification icon and splash screens from the brand (black + yellow, the brand letter).

Usage: python tools/android_assets.py [letter] [bg] [fg]   e.g.  python tools/android_assets.py ک "#0a0a0a" "#feff00"
White-label: run again with the agency's letter/colours (or replace the PNGs with exported artwork) before publishing.
"""
import os, sys
from PIL import Image, ImageDraw, ImageFont

ROOT = os.path.join(os.path.dirname(__file__), "..")
RES = os.path.join(ROOT, "mobile", "android", "app", "src", "main", "res")
FONT = os.path.join(ROOT, "src", "karnama-web", "src", "app", "fonts", "Vazirmatn-Black.ttf")
letter = sys.argv[1] if len(sys.argv) > 1 else "ک"
bg = sys.argv[2] if len(sys.argv) > 2 else "#0a0a0a"
fg = sys.argv[3] if len(sys.argv) > 3 else "#feff00"


def glyph(size, color, box, scale=0.62):
    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    font = ImageFont.truetype(FONT, int(box * scale))
    l, t, r, b = d.textbbox((0, 0), letter, font=font)
    d.text(((size - (r - l)) / 2 - l, (size - (b - t)) / 2 - t), letter, font=font, fill=color)
    return img


def save(img, folder, name):
    os.makedirs(os.path.join(RES, folder), exist_ok=True)
    img.save(os.path.join(RES, folder, name), optimize=True)


for folder, px in {"mdpi": 48, "hdpi": 72, "xhdpi": 96, "xxhdpi": 144, "xxxhdpi": 192}.items():
    square = Image.new("RGBA", (px, px), bg)
    square.alpha_composite(glyph(px, fg, px))
    rounded = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    mask = Image.new("L", (px, px), 0)
    ImageDraw.Draw(mask).rounded_rectangle((0, 0, px - 1, px - 1), radius=px // 5, fill=255)
    rounded.paste(square, (0, 0), mask)
    save(rounded, f"mipmap-{folder}", "ic_launcher.png")
    circle = Image.new("RGBA", (px, px), (0, 0, 0, 0))
    cmask = Image.new("L", (px, px), 0)
    ImageDraw.Draw(cmask).ellipse((0, 0, px - 1, px - 1), fill=255)
    circle.paste(square, (0, 0), cmask)
    save(circle, f"mipmap-{folder}", "ic_launcher_round.png")
    # Adaptive icon foreground: 108dp canvas, the glyph inside the 66dp safe zone.
    fpx = px * 108 // 48
    save(glyph(fpx, fg, fpx * 66 // 108), f"mipmap-{folder}", "ic_launcher_foreground.png")
    # Notification icon: white silhouette on transparent (Android tints it).
    npx = px // 2
    save(glyph(npx, "#ffffff", npx, 0.8), f"drawable-{folder}", "ic_stat_karnama.png")

with open(os.path.join(RES, "values", "ic_launcher_background.xml"), "w") as f:
    f.write(f'<?xml version="1.0" encoding="utf-8"?>\n<resources>\n    <color name="ic_launcher_background">{bg}</color>\n</resources>\n')

# Splash: the letter on the brand background, every density/orientation the template ships.
for folder in os.listdir(RES):
    path = os.path.join(RES, folder, "splash.png")
    if os.path.exists(path):
        w, h = Image.open(path).size
        img = Image.new("RGBA", (w, h), bg)
        g = glyph(min(w, h) // 3, fg, min(w, h) // 3, 0.75)
        img.alpha_composite(g, ((w - g.width) // 2, (h - g.height) // 2))
        img.convert("RGB").save(path, optimize=True)
print("android assets written to", RES)
