"""Generate placeholder sprites for the platformer template.

PPU is 32, so 32 px == 1 grid cell (1 world unit). Terrain is painted on a
1x1 Tilemap grid.

Terrain sprites are white with a 1 px rim at 0.929 brightness. The Tile colour
multiplies both, so each tile gets an individual rim one step darker than its own
colour (#E0E0E0 -> #D0D0D0). The merged outer outline is produced separately by
the full-screen ScreenOutline renderer feature.

Slope tiles are 1x1 right triangles with a 45-degree hypotenuse in one of four
orientations. Their physics shape is written explicitly by the import script.
"""
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Art", "Placeholders")
OUT_DIR = os.path.abspath(OUT_DIR)
os.makedirs(OUT_DIR, exist_ok=True)

SS = 4  # supersampling factor for smooth edges

# 0.929 of full brightness. The tile colour is multiplied by this at runtime, so
# #E0E0E0 becomes #D0D0D0, #454C5C becomes #404750, and so on: every tile gets an
# individual rim that is one step darker than its own colour.
RIM = 237


def save(img, name):
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def downscale(img, w, h):
    return img.resize((w * SS, h * SS), Image.LANCZOS).resize((w, h), Image.LANCZOS)


def make_tile(size=32):
    """1x1 block with a 1 px darker rim baked in (tinted at runtime)."""
    img = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], outline=(RIM, RIM, RIM, 255), width=1)
    return img


def make_triangle(size, points):
    """1x1 cell 45-degree triangle with a 1 px darker rim along every edge."""
    mask_hi = Image.new("L", (size * SS, size * SS), 0)
    ImageDraw.Draw(mask_hi).polygon([(x * SS, y * SS) for (x, y) in points], fill=255)
    mask = mask_hi.resize((size, size), Image.LANCZOS)

    eroded = mask.filter(ImageFilter.MinFilter(3))
    rim = ImageChops.subtract(mask, eroded)

    img = Image.new("RGBA", (size, size), (0, 0, 0, 0))
    img.paste(Image.new("RGBA", (size, size), (255, 255, 255, 255)), (0, 0), mask)
    img.paste(Image.new("RGBA", (size, size), (RIM, RIM, RIM, 255)), (0, 0), rim)
    return img


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
    save(make_tile(32), "tile1x1.png")
    for slope_name, corners in SLOPES.items():
        save(make_triangle(32, corners), slope_name + ".png")
    save(make_capsule(), "player_capsule.png")
