"""Generate placeholder sprites for the platformer template.

PPU is 32, so 32 px == 1 grid cell (1 world unit). Terrain is painted on a
1x1 Tilemap grid.

Terrain tiles are flat white (tinted by the Tile asset colour). Outlines come
from the full-screen ScreenOutline renderer feature, which merges any adjacent
flat-coloured shapes into one silhouette, so nothing is baked into the textures.

Slope tiles are 1x1 right triangles with a 45-degree hypotenuse in one of four
orientations. Unity generates their physics shape from the alpha outline, so the
tile collider is actually triangular.
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


def make_triangle(size, points):
    """1x1 cell 45-degree triangle. Points are image-space corners (y down)."""
    img = Image.new("RGBA", (size * SS, size * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    d.polygon([(x * SS, y * SS) for (x, y) in points], fill=(255, 255, 255, 255))
    return downscale(img, size, size)


def make_capsule(w=24, h=48):
    """Player placeholder: capsule 0.75 x 1.5 units, 1.5 cells tall."""
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


# Image-space (y down) corners of the four 45-degree triangle orientations.
SLOPES = {
    # Hypotenuse bottom-left -> top-right, solid below it: walkable ramp rising right.
    "slope_up_right": [(0, 32), (32, 32), (32, 0)],
    # Hypotenuse bottom-right -> top-left, solid below it: ramp rising left.
    "slope_up_left": [(0, 32), (32, 32), (0, 0)],
    # Hypotenuse top-left -> bottom-right, solid above it: ramp falling right.
    "slope_down_right": [(0, 0), (32, 0), (32, 32)],
    # Hypotenuse top-right -> bottom-left, solid above it: ramp falling left.
    "slope_down_left": [(32, 0), (0, 0), (0, 32)],
}


if __name__ == "__main__":
    save(make_flat(32, 32), "tile1x1.png")
    for slope_name, corners in SLOPES.items():
        save(make_triangle(32, corners), slope_name + ".png")
    save(make_capsule(), "player_capsule.png")
