"""为平台跳跃模板生成占位精灵。

PPU 为 32，即 32 px 等于 1 格（1 世界单位）。地形在 1×1 的 Tilemap 网格上绘制。

地形精灵是白色的，带一圈亮度 0.929 的 1px 内边。Tile 颜色会与贴图相乘，
于是每个瓦片都得到比自身颜色暗一档的独立内边（#E0E0E0 -> #D0D0D0）。
整体外轮廓由全屏的 ScreenOutline 渲染器特性单独生成。

斜坡瓦片是 1×1 的直角三角形，斜边为 45 度，共四种朝向。
它们的物理形状由导入脚本显式写入。
"""
import os
from PIL import Image, ImageChops, ImageDraw, ImageFilter

OUT_DIR = os.path.join(os.path.dirname(os.path.abspath(__file__)), "..", "Assets", "Art", "Placeholders")
OUT_DIR = os.path.abspath(OUT_DIR)
os.makedirs(OUT_DIR, exist_ok=True)

SS = 4  # 超采样倍数，用于得到平滑边缘

# 满亮度的 0.929 倍。运行时会与 Tile 颜色相乘，
# 于是 #E0E0E0 变成 #D0D0D0，#454C5C 变成 #404750，以此类推：
# 每个瓦片都得到比自身颜色暗一档的独立内边。
RIM = 237


def save(img, name):
    path = os.path.join(OUT_DIR, name)
    img.save(path)
    print(f"wrote {path} ({img.width}x{img.height})")


def downscale(img, w, h):
    return img.resize((w * SS, h * SS), Image.LANCZOS).resize((w, h), Image.LANCZOS)


def make_tile(size=32):
    """1×1 方块，烘焙了一圈暗 1 档的 1px 内边（运行时着色）。"""
    img = Image.new("RGBA", (size, size), (255, 255, 255, 255))
    d = ImageDraw.Draw(img)
    d.rectangle([0, 0, size - 1, size - 1], outline=(RIM, RIM, RIM, 255), width=1)
    return img


def make_triangle(size, points):
    """1×1 的 45 度三角形，每条边都有 1px 的暗色内边。"""
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
    """玩家占位精灵：0.75 × 1.5 单位的胶囊，高 1.5 格。"""
    img = Image.new("RGBA", (w * SS, h * SS), (0, 0, 0, 0))
    d = ImageDraw.Draw(img)
    r = (w * SS) // 2
    d.rounded_rectangle(
        [0, 0, w * SS - 1, h * SS - 1],
        radius=r,
        fill=(79, 194, 247, 255),
    )
    # 一个小小的"眼睛"标记，方便看出朝向
    d.ellipse(
        [w * SS * 0.55, h * SS * 0.28, w * SS * 0.85, h * SS * 0.40],
        fill=(25, 34, 48, 255),
    )
    return downscale(img, w, h)


# 四种 45 度三角形朝向在图空间（y 轴向下）中的顶点。
SLOPES = {
    # 斜边从左下到右上，实体在下方：向右上升的可走坡道。
    "slope_up_right": [(0, 32), (32, 32), (32, 0)],
    # 斜边从右下到左上，实体在下方：向左上升的坡道。
    "slope_up_left": [(0, 32), (32, 32), (0, 0)],
    # 斜边从左上到右下，实体在上方：向右下降的坡道。
    "slope_down_right": [(0, 0), (32, 0), (32, 32)],
    # 斜边从右上到左下，实体在上方：向左下降的坡道。
    "slope_down_left": [(32, 0), (0, 0), (0, 32)],
}


if __name__ == "__main__":
    save(make_tile(32), "tile1x1.png")
    for slope_name, corners in SLOPES.items():
        save(make_triangle(32, corners), slope_name + ".png")
    save(make_capsule(), "player_capsule.png")
