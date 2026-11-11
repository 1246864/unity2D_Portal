"""Generate placeholder sprites for the platformer template.

PPU is 32, so 32 px == 1 grid cell (1 world unit). Terrain comes in 2-cell
modules: 2x2 (64x64), 2x1 (64x32) and 1x2 (32x64).

Terrain sprites are flat white (tinted by SpriteRenderer.color). Outlines are
produced by the full-screen ScreenOutline renderer feature, which merges any
adjacent flat-coloured shapes into one silhouette, so nothing is baked into the
terrain textures.
"""
import os
from PIL import Image, ImageDraw

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Art", "Placeholders")
OUT_DIR = os.path.abspath(OUT_DIR)
os.makedirs(OUT_DIR, exist_ok=True)

SS = 4  # supersampling factor for smooth edges


def save(img, name):
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def downscale(img, w, h):
    return img.resize((w * SS, h * SS), Image.LANCZOS).resize((w, h), Image.LANCZOS)


def make_flat(w, h):
    """Flat white rectangle; tinted at runtime."""
    return Image.new("RGBA", (w * SS, h * SS), (255, 255, 255, 255)).resize((w, h), Image.LANCZOS)


def make_slope(size=64):
    """2x2 cell 45-degree ramp (right triangle), flat white for tinting."""
    img = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    pts = [(0, size * SS - 1), (size * SS - 1, size * SS - 1), (size * SS - 1, 0)]
    d.polygon(pts, fill=(255, 255, 255, 255))
    return downscale(img, size, size)


def make_capsule(w=26, h=55):
    """Player placeholder: capsule 0.81 x 1.72 units, 1.7 cells tall."""
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = (w * SS) // 2
    d.rounded_rectangle(
        [0, 0, w * SS - 1, h * SS - 1],
        radius=r,
        fill=(79, 194, 247, 255),
    )
    # small "face" marker so facing direction is readable
    d.ellipse(
        [w * SS * 0.55, h * SS * 0.28, w * SS * 0.85, h * SS * 0.40],
        fill=(25, 34, 48, 255),
    )
    return downscale(img, w, h)


if __name__ == "__main__":
    save(make_flat(64, 64), "block2x2.png")
    save(make_flat(64, 32), "block2x1.png")
    save(make_flat(32, 64), "block1x2.png")
    save(make_slope(), "slope2x2.png")
    save(make_capsule(), "player_capsule.png")
