# 2D 传送门（unity2D_Portal）— 项目总览

> 本文档记录这个项目的完整设计、当前状态、数据结构与实现细节，供后续开发（尤其是传送逻辑）参考。
> 项目由 `unity2Dcsm` 全量迁移而来，git 历史、资源 GUID、场景层级均保持原样。

---

## 1. 这是什么

一个 **2D 横版平台跳跃 + 传送门机制** 的 Unity 模板/原型。

- 关卡由 1×1 瓦片（Tilemap）搭建，含 45° 斜坡
- 角色可以跑、跳（跳跃高度精确 6 格）、上下坡
- 场景里有一对**传送门 A / B**：
  - 每一端的画面由一台**独立相机**拍摄，实时显示成精灵
  - 两端的画面**交叉输出**（A 的画面画在 B 上，B 的画在 A 上）
  - 门面用 `SpriteMask` 裁切，遮罩始终**朝向玩家眼睛**
  - 支持**镜中镜**（递归）：相机能看到另一扇门的画面，形成无限嵌套
- 传送（玩家穿过门）的逻辑**尚未实现**，计划由使用者自行编写，所需属性已经准备好

---

## 2. 技术栈

| 项目 | 版本 / 值 |
|---|---|
| Unity | **2022.3.61f1c1** |
| 渲染 | URP **14.0.12**，2D Renderer |
| 色彩空间 | Linear |
| 输入 | 旧版 Input Manager（`Horizontal` / `Jump`） |
| 测试 | Unity Test Framework 1.1.33（PlayMode） |
| 物理 | Physics2D，瓦片使用 CompositeCollider2D |

---

## 3. 目录结构

```
unity2D_Portal/
├─ Assets/
│  ├─ Art/Placeholders/     占位贴图（tile1x1、4 个斜坡、玩家胶囊、mask_white）
│  ├─ Editor/ProjectBuilder.cs   一键构建关卡/预制体/描边/传送门（约 900 行）
│  ├─ Materials/ScreenOutline.mat
│  ├─ Palettes/TerrainPalette.prefab
│  ├─ Prefabs/Player.prefab
│  ├─ Scenes/Level01.unity       主场景
│  ├─ Scripts/
│  │  ├─ PlayerController2D.cs
│  │  ├─ CameraFollow2D.cs
│  │  ├─ CaptureDisplayGroup.cs  传送门端（A/B）
│  │  ├─ PortalPair.cs           传送门对总控
│  │  ├─ RegionSnapshotCamera.cs 相机捕获 → 贴图 → 精灵
│  │  └─ Rendering/ScreenOutlineFeature.cs
│  ├─ Settings/                  URP、Renderer2D、描边材质、物理材质
│  ├─ Shaders/ScreenOutline.shader
│  ├─ Tests/PlayMode/            9 个自动化测试
│  └─ Tiles/                     2 个基础瓦片 + 8 个斜坡瓦片
├─ docs/DEVELOPMENT.md          分阶段开发记录
├─ Tools/generate_placeholders.py
├─ Preview.png / Preview_Gameplay.png
└─ PROJECT.md                   ← 本文档
```

---

## 4. 场景树（Level01，当前实际数据）

```
Main Camera                 (CameraFollow2D，正交 5.625)
Global Light 2D             (Light2D, Global)
Grid
└─ Terrain                  (Tilemap + TilemapCollider2D + CompositeCollider2D)
Player                      (预制体实例，Player 排序层，order 100)

Portal Pair                 (3, 3)  ← PortalPair (player 已指向 Player)
├─ Portal A                 local (18.89, 4)  → world (21.89, 7)
│  ├─ Portal Camera         (正交，z = -10，viewScale 1.2)
│  ├─ Portal Capture        (RegionSnapshotCamera，驱动相机→贴图)
│  ├─ Portal View           (SpriteRenderer，order 20，VisibleOutsideMask)
│  └─ Portal Mask           (遮罩容器)
│     ├─ View Mask Top      (scale (1, -1))
│     │  └─ Mask            SpriteMask，local (0, 50)，scale (200, 100)
│     └─ View Mask Bottom   (scale (1, -1))
│        └─ Mask            SpriteMask，local (0, -50)，scale (200, 100)
└─ Portal B                 local (-10.38, 1.94) → world (-7.38, 4.94)
   ├─ Portal Camera
   ├─ Portal Capture
   ├─ Portal View           (SpriteRenderer，order 20，VisibleOutsideMask)
   └─ Portal Mask
      ├─ View Mask Top      (scale (1, 1))
      │  └─ Mask            SpriteMask，local (0, 50)，scale (200, 100)
      └─ View Mask Bottom   (scale (1, 1))
         └─ Mask            SpriteMask，local (0, -50)，scale (200, 100)
```

> ⚠️ 手动调整场景后，**不要**再执行 `Platformer > Rebuild Level + Prefabs`——该命令会用脚本从零重建 `Level01`，覆盖所有手动修改。

---

## 5. 排序层与物理层

**排序层（TagManager）**

| 排序层 | ID | 用途 |
|---|---|---|
| Default | 0 | 地形、瓦片 |
| Portal A | 916715397 | Portal A 的画面 + 它的遮罩 |
| Portal B | 513430870 | Portal B 的画面 + 它的遮罩 |
| **Player** | 3198412009 | 玩家，最上层 |

**关键点**：Unity 的 `SpriteMask` 是**全局按排序层生效**的。A 的遮罩之所以只影响 A 的画面，是因为 A 的 `Portal View` 与两块 `SpriteMask` 都在 **Portal A** 层，且遮罩的 `front/backSortingLayerID` 都设成了该层；B 同理。**这是遮罩隔离的核心，不要随意改动。**

**物理层**

| 层 | 索引 | 用途 |
|---|---|---|
| Ground | 6 | 地形、瓦片碰撞 |
| Player | 7 | 玩家 |
| Snapshot | 8 | 传送门相关对象 |

---

## 6. 传送门系统

### 6.1 组件

| 组件 | 挂载位置 | 职责 |
|---|---|---|
| `PortalPair` | `Portal Pair` | 持有 `player` 引用与 `playerEyeOffset`（默认 0.4），提供 `PlayerEye` |
| `CaptureDisplayGroup` | `Portal A` / `Portal B` | 每端的逻辑：配对、交叉连线、遮罩驱动、画面裁剪 |
| `RegionSnapshotCamera` | `Portal Capture` | 相机 → RenderTexture → Texture2D → Sprite |

### 6.2 交叉输出

```
Portal A 的 Portal Capture  →  渲染 A 的相机  →  画到 B 的 Portal View
Portal B 的 Portal Capture  →  渲染 B 的相机  →  画到 A 的 Portal View
```

`CaptureDisplayGroup.WireUp()` 在 `Awake` 中完成：`capture.output = partner.output`。
配对是**自动**的：同父节点下找 `side` 不同的那一组。

### 6.3 画面裁剪（中线）

`ApplyHalfCrop()` 每帧检查画面精灵，把捕获纹理裁成一半：

| 门 | 保留 | 位移 |
|---|---|---|
| A | 右半 | 右移 `W/4` |
| B | 左半 | 左移 `W/4` |

`centerGapPixels`（默认 1）让两半在中线各让出一点，避免递归把共用像素列放大成亮线。

### 6.4 遮罩

每端两组 `View Mask Top/Bottom`，由 `UpdateMasks()` 每帧驱动：

```csharp
maskTop.localPosition    = new Vector3(0f, maskTopOffset, 0f);     // 默认 +1.5
maskBottom.localPosition = new Vector3(0f, maskBottomOffset, 0f); // 默认 -1.5
// 对准玩家眼睛后再旋转 90°
float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
mask.rotation = Quaternion.Euler(0f, 0f, angle);
```

- 遮罩只在**本门排序层**内生效（A 不影响 B）
- `PortalHeight` 是只读属性：`|maskTopOffset - maskBottomOffset|`

### 6.5 递归（镜中镜）

捕获相机不排除任何层，因此当 `Portal View` 落在相机视野内时，会被再次拍进画面，形成嵌套。因为画面是 1:1 且与相机同位置，实际视觉效果取决于遮罩与摆放。

### 6.6 朝向

`CaptureDisplayGroup.facing`（`PortalFacing.Left / Right`）：
- A 默认 **Right**，B 默认 **Left**
- 辅助：`FacesLeft`、`FacingSign`（-1 / +1）

传送逻辑需要用它判断：两门同向 vs 相背时，画面/速度是否要水平翻转。

---

## 7. 角色（PlayerController2D）

| 参数 | 值 |
|---|---|
| 移动速度 | 9 |
| 地面加速 / 减速 | 115 / 150 |
| 空中加速 / 减速 | 70 / 45 |
| 跳跃高度 | **6 格** |
| 角色高度 | 1.5 格 |
| Coyote 时间 / 跳跃缓冲 | 0.1 / 0.12 |
| 下落重力倍率 | 1.6 |
| 松键跳跃削减 | 0.45 |
| 地面层 | Ground (6) |

特性：斜坡行走（按地面法线处理）、土狼时间、跳跃缓冲、坠落重生。

## 8. 相机（CameraFollow2D）

正交 5.625、跟随偏移 (0, 1)、平滑 0.12、前瞻 1.5（参考速度 9）。

## 9. 地形与瓦片

- `Grid` + 单个 `Terrain` Tilemap，1×1 瓦片
- 两种颜色：普通 **#E0E0E0**、深灰 **#454C5C**
- 4 方向 45° 斜坡瓦片（普通 + 深灰，共 8 个），碰撞形状为精确三角形
- 每个瓦片贴图自带 1px、亮度 0.929 的内描边（染色后即"暗一档"）
- 地形碰撞用 `CompositeCollider2D` 合并，物理材质 `TerrainPhysicsMaterial`

## 10. 描边系统

- **瓦片内描边**：贴图烘焙，`#E0E0E0 → #D0D0D0`
- **全屏外轮廓**：`ScreenOutlineFeature`（背景掩码 + 色差类型边界），阈值 0.28，宽 1px，颜色 `(0.70, 0.79, 0.92)`
- 背景色 `(0.086, 0.102, 0.153)`，依赖纯色相机背景

## 11. 构建脚本（ProjectBuilder）

菜单（`Platformer`）：

| 菜单 | 作用 |
|---|---|
| `Rebuild Level + Prefabs` | **重建整个场景**（会覆盖手动修改） |
| `Rebuild + Render Preview` | 重建 + 出预览图 |
| `Render Preview Image` | 只渲染预览，不改场景 |
| `Validate Level` | 校验瓦片数、材质、描边 feature |
| `Open Level 01` | 打开主场景 |

命令行：

```
Unity.exe -batchmode -projectPath <项目> -executeMethod Platformer.EditorTools.ProjectBuilder.BuildAll -quit
Unity.exe -batchmode -projectPath <项目> -runTests -testPlatform PlayMode -testResults Logs\TestResults.xml
```

## 12. 测试（9/9 通过）

| 测试 | 校验内容 |
|---|---|
| `Player_Stands_On_Ground` | 站立在地形上，y=0.75 |
| `Jump_Reaches_Six_Cell_Height` | 跳跃高度 ≈ 6 格 |
| `Horizontal_Movement_Works` | 水平移动 |
| `Walks_Up_45_Degree_Slope` | 45° 斜坡行走 |
| `Player_Climbs_Tilemap_Ramp` | 瓦片斜坡爬升 |
| `Falls_Below_Limit_Respawn` | 坠落重生 |
| `Camera_Follows_Player` | 相机跟随收敛 |
| `Tilemap_Level_Has_Grounded_Player` | 关卡瓦片数与玩家站立 |
| `Snapshot_Camera_Captures_Own_View` | 捕获相机独立、1.2 倍尺寸、1:1、画布随视野、A/B 半屏、交叉连线 |

## 13. 已知限制 / 下一步

1. **传送逻辑尚未实现**：玩家穿过门的位置/速度变换需要按 `facing`、`FacingSign`、`PortalHeight` 编写（在 `PortalPair` 或新组件里）。
2. 遮罩尺寸（`Mask` 子物体 `scale (200,100)`、偏移 `±50`）是当前调好的视觉参数，改动会影响门洞大小与朝向。
3. 全屏描边会作用于 `Portal View` 的裁切边缘；若要完全排除，需要把快照放到不参与描边的相机/渲染层（暂未实现）。
4. 场景手动修改后**不要**跑 `Rebuild Level + Prefabs`。

## 14. 迁移记录

- 源目录：`unity2Dcsm` → 目标：`unity2D_Portal`
- 保留：git 历史、所有 `.meta`（GUID 不变）、场景层级、排序层、预制体引用
- 迁移后验证：工程可导入、编译无错、9/9 PlayMode 测试通过
