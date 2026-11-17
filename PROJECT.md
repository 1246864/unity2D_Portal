# 2D 传送门（unity2D_Portal）— 项目总览

> 当前开发版：捕获范围静态/动态双模式（基于 `83e95e7`）
> 当前测试：**23/23 PlayMode 通过**（`Logs/TestResults_scene.xml`，26.21 秒）
> 本文档描述当前实际状态；历史更新报告已合并到本文与 `docs/DEVELOPMENT.md`。

---

## 1. 这是什么

一个 **2D 横版平台跳跃 + 双门传送门** 的 Unity 原型：

- 1×1 Tilemap 关卡，含 45° 斜坡、深灰地形、台阶和悬浮平台；
- 胶囊角色，6 格跳高、土狼时间、跳跃缓冲、可变跳高、坠落重生；
- 一对可**任意角度旋转**的传送门：
  - 每端一台独立正交相机，实时拍摄画面并显示为 Sprite；
  - A/B 交叉输出；
  - `SpriteMask` 裁出门洞，遮罩始终朝向玩家眼睛；
  - 传送门相机排除 Portal View 所在层，避免递归；
- 传送：
  - 门内极薄 `BoxCollider2D` + 玩家中心 `CircleCollider2D`；
  - 出口按出口门的法线/切线坐标系计算，支持旋转门；
  - 速度按入口/出口门坐标系变换；
  - 传送后两门强制重拍，发起门重播开门动画；
- 玩家显示与物理分离：
  - 物理根节点保留 Rigidbody2D / CapsuleCollider2D / Center Point；
  - 运行时创建 `Player Visual` 子节点，只旋转显示，胶囊保持竖直；
- 动态地形开洞：
  - 门激活时按门洞矩形对 Tilemap 碰撞做非整格布尔切割；
  - 视觉 Tile 不变，剩余碰撞用运行时 `PolygonCollider2D` 补回；
- 门位置光带 + 粒子，已静态化到场景。

项目从 `unity2Dcsm` 迁移而来，git 历史和 `.meta` GUID 保留。

---

## 2. 技术栈

| 项目 | 值 |
|---|---|
| Unity | **2022.3.61f1c1** |
| 渲染 | URP **14.0.12**，2D Renderer |
| 色彩空间 | Linear |
| 输入 | 旧版 Input Manager（`Horizontal` / `Jump`） |
| 测试 | Unity Test Framework 1.1.33（PlayMode） |
| 物理 | Physics2D，重力 `(0, -40)`，Tilemap + CompositeCollider2D |
| 单位 | 1 格 = 1 世界单位；PPU = 32 |

---

## 3. 目录结构

```text
unity2D_Portal/
├─ Assets/
│  ├─ Art/Placeholders/      占位贴图（tile1x1、4 个斜坡、玩家胶囊、mask_white）
│  ├─ Editor/
│  │  ├─ ProjectBuilder.cs       旧版一键构建（未跟进当前场景，勿运行）
│  │  └─ PlayerController2DEditor.cs  中文玩家 Inspector
│  ├─ Palettes/TerrainPalette.prefab
│  ├─ Prefabs/Player.prefab
│  ├─ Scenes/Level01.unity       主场景
│  ├─ Scripts/
│  │  ├─ PlayerController2D.cs
│  │  ├─ CameraFollow2D.cs
│  │  ├─ CaptureDisplayGroup.cs
│  │  ├─ PortalPair.cs
│  │  ├─ PortalTerrainCarver.cs
│  │  ├─ RegionSnapshotCamera.cs
│  │  └─ TilemapOutline.cs
│  ├─ Settings/                 URP、Renderer2D、物理材质
│  ├─ Shaders/PortalHintBeam.shader
│  ├─ Tests/PlayMode/           4 个测试文件，23 个用例
│  └─ Tiles/                    10 个 Tile 资产
├─ docs/DEVELOPMENT.md
├─ Tools/generate_placeholders.py
├─ Preview.png / Preview_Gameplay.png
├─ PROJECT.md
└─ README.md
```

---

## 4. 场景树（Level01，当前实际数据）

```text
Main Camera                 Camera(正交 8) + CameraFollow2D + URP CameraData + AudioListener
Global Light 2D             Light2D(Global)
Level
└─ Grid                     Grid(1×1)
   └─ Terrain               Tilemap(233 格) + TilemapRenderer(order 0)
                            + TilemapOutline + Rigidbody2D(Static)
                            + CompositeCollider2D + TilemapCollider2D
                            layer = Ground(6)
Player                      Player 预制体实例，位置 (-9, 0.75)
                            SpriteRenderer(Player 层/order 100，根节点运行时禁用)
                            + Rigidbody2D(Dynamic, Continuous, Interpolate)
                            + CapsuleCollider2D(0.72×1.5)
                            + PlayerController2D
  ├─ Center Point           运行时，CircleCollider2D(r=0.08)
  └─ Player Visual          运行时，显示精灵 + 传送门旋转

Portal Pair                 PortalPair，world (3,3)
                            画面尺寸配置：useDynamicSize=true、captureWidth=50、captureHeight=35、
                            cameraSizeMultiplier=(1.2, 2.2)（统一下发给 A/B）
├─ Portal A                 CaptureDisplayGroup(A)，facing Left
│  ├─ Portal Camera         local z=-10；静态时 orthoSize=captureHeight/2（17.5），动态时=主相机 ortho×2.2
│  ├─ Portal Capture        RegionSnapshotCamera
│  ├─ Portal Mask
│  │  ├─ View Mask Top      scale(1,-1)
│  │  │  └─ Mask            SpriteMask，local(0,50)，scale(200,100)
│  │  └─ View Mask Bottom   scale(1,-1)
│  │     └─ Mask            SpriteMask，local(0,-50)，scale(200,100)
│  ├─ Portal View Center    local z=-10
│  │  └─ Portal View        SpriteRenderer，layer 8，Portal A/order 20，
│  │                        VisibleOutsideMask，local z=+10
│  ├─ Portal Hint           静态光带，Portal light band/order 0，scale(0.45,4.3)
│  └─ Portal Hint Particles 静态粒子，Portal light band/order 1
└─ Portal B                 CaptureDisplayGroup(B)，facing Right，旋转 45°
   ├─ Portal Camera
   ├─ Portal Capture
   ├─ Portal Mask（同上）
   ├─ Portal View Center    local z=0
   │  └─ Portal View        layer 8，Portal B/order 20
   ├─ Portal Hint
   └─ Portal Hint Particles
```

运行时在 `Grid` 下生成 `Outline` Tilemap（Default/order 1），并在 `Portal Pair` 上补 `PortalTerrainCarver`。

### 当前关键数值

| 项目 | 值 |
|---|---|
| 地形格子 | **233**（bounds x `-16..23`、y `-4..11`，尺寸 40×16） |
| 主相机 | 正交 **8**，位置 `(-9, 1.95, -10)` |
| Portal A | world `(5.78, 2.04)`，旋转 0°，facing Left |
| Portal B | world `(10.68, 3.68)`，**旋转 45°**，facing Right |
| PortalHeight | **4**（`maskTopOffset=2` / `maskBottomOffset=-2`） |
| 开门动画 | `openDuration=0.6` |
| 捕获范围 | 静态备用 **50 × 35**；当前动态宽 **×1.2** / 高 **×2.2** |
| 地形开洞 | `carveTerrain=true`，`carveWidth=0.8`，`carveFrontMargin=0.1` |
| 光带 | `hintExtraHeight=0.3` → scene scale y=4.3；长度 0.45 |
| 玩家 | 高度 1.5，跳跃 6，速度 9，中心碰撞点 0.08 |

---

## 5. 排序层与物理层

**排序层（TagManager）**

| 排序层 | uniqueID | 用途 |
|---|---:|---|
| Default | 0 | 地形、描边、默认对象 |
| **Portal light band** | 3700759211 | `Portal Hint` + `Portal Hint Particles` |
| Portal view | 4127090001 | 预留，当前未使用 |
| Portal A | 916715397 | A 的 Portal View + 遮罩自定义范围 |
| Portal B | 513430870 | B 的 Portal View + 遮罩自定义范围 |
| Player | 3198412009 | 玩家显示节点 |

**物理层**

| 层 | 索引 | 用途 |
|---|---|---|
| Ground | 6 | 地形、残余开洞碰撞 |
| Player | 7 | 玩家 |
| Snapshot | 8 | 门薄碰撞箱、Portal View |

传送门薄碰撞箱放在 Snapshot(8)；玩家身体胶囊 `excludeLayers` 排除 8，只让中心点参与门碰撞。

---

## 6. 传送门系统

### 6.1 组件

| 组件 | 挂载位置 | 职责 |
|---|---|---|
| `PortalPair` | `Portal Pair` | 玩家引用、门薄碰撞箱、碰撞转发、传送、冷却、相机相对映射、地形开洞接入、画面尺寸配置与下发 |
| `CaptureDisplayGroup` | `Portal A/B` | 配对/交叉连线、自动查找 View 结构、遮罩、半屏裁剪、颜色/可见性、静态光带驱动、碰撞转发 |
| `RegionSnapshotCamera` | `Portal Capture` | 相机 → RenderTexture → Texture2D → Sprite |
| `PortalTerrainCarver` | `Portal Pair`（运行时补） | 按门洞矩形切割 Tilemap 碰撞并恢复 |

### 6.2 开口方向

```csharp
public Vector2 OpeningDirection
{
    get
    {
        Vector3 local = Quaternion.Euler(0f, 0f, FacingSign * -90f) * Vector3.up;
        Vector3 world = transform.TransformDirection(local);
        return world.normalized;
    }
}

public Vector2 BackDirection => -OpeningDirection;
```

- A 朝左未旋转：`(-1, 0)`；
- B 朝右旋转 45°：`(0.707, 0.707)`；
- 门整体旋转时，开口方向、背面判断、开洞方向、出口和速度全部实时跟随。

### 6.3 本组 View 结构自动解析

`CaptureDisplayGroup.Awake()` 和 `Start()` 都会调用 `ResolveViewRig()`，从本组子物体中按名字查找：

```text
Portal View Center
  └─ Portal View（SpriteRenderer）
Portal Mask
  ├─ View Mask Top
  └─ View Mask Bottom
```

查找结果写入 `viewCenter`、`output`、`viewObject`、`maskTop`、`maskBottom`，因此场景引用留空也能工作。Inspector 右键菜单“重新查找 Portal View 结构”可手动刷新。`ResolveMaskRig()` 保留为兼容入口，内部也走 `ResolveViewRig()`。

### 6.4 成像与交叉输出

```text
A 的 Portal Capture → 渲染 A 的相机 → 画到 B 的 Portal View
B 的 Portal Capture → 渲染 B 的相机 → 画到 A 的 Portal View
```

`CaptureDisplayGroup.Awake` 中：

```csharp
capture.output = partner.output;
partner.capture.output = output;
```

捕获参数：

- 捕获尺寸有两种模式，**统一在 `PortalPair` 上配置**，由 `PushViewSettingsToGroups()` 下发给 A/B 两个 `RegionSnapshotCamera`；`RegionSnapshotCamera` 的尺寸字段在 Inspector 中隐藏：
  - 静态（默认，`false`）：`captureWidth × captureHeight`，默认 **50 × 50**（当前场景静态备用 **50 × 35**），不随窗口/主相机变化；
  - 动态（`true`）：宽度 = 主相机宽度 × `cameraSizeMultiplier.x`，高度 = 主相机高度 × `cameraSizeMultiplier.y`；默认 **(1.2, 2)**，当前场景 **useDynamicSize=true、(1.2, 2.2)**；
- 捕获相机 orthographicSize = 捕获世界高度 / 2；aspect = 捕获世界宽度 / 高度；
- 输出 PPU 40；静态 50×50 对应 2000×2000 纹理；尺寸超过 `maxTextureSize` 时会警告并限制纹理分辨率；
- `captureInterval=0`（每帧捕获）；
- Portal Camera 的 `cullingMask` 排除 Portal View 所在 Snapshot(8) 层，避免递归；
- 主相机仍渲染 Snapshot 层。

### 6.5 半屏裁剪

`ApplyHalfCrop()`：

- 默认 A 保留右半，B 保留左半；
- 中线留 `centerGapPixels=1`；
- 用完整纹理的一半重建 Sprite，X 偏移 `±(W/4 + gap/2)`；
- 在 `Portal View` 初始 localPosition 上叠加，只改 X，不覆盖 `Portal View Center` 的 Z 偏移。

可以通过 `CaptureDisplayGroup` 直接改写某扇门输出使用的半屏：

```csharp
// 设置自己（A 的 Portal View 使用 B 相机的哪半）
groupA.OutputHalf = PortalViewHalf.Right;
groupA.SetOutputHalf(PortalViewHalf.Left);

// 控制另一扇门（A 的 partner 是 B）
// 语义：让 B 的 Portal View 使用 A 相机的右半，A 自己的输出不变
groupA.SetPartnerCameraHalf(PortalViewHalf.Right);
groupA.SetPartnerOutputHalf(PortalViewHalf.Right); // 同义
groupA.PartnerOutputHalf = PortalViewHalf.Right;   // 属性写法
```

- `PortalViewHalf.Left / Right`
- `OutputHalf`：本门 Portal View 使用 partner 相机的哪半张纹理；
- `PartnerOutputHalf` / `SetPartnerOutputHalf` / `SetPartnerCameraHalf`：从当前门控制 partner 的输出，也就是“我的相机哪半张纹理给对方显示”；不会改自己的 `OutputHalf`；
- 本门 `OutputHalf` / `SetOutputHalf` setter 会立即重建半屏 Sprite，并**按新半屏自动对齐 Portal View 的 localPosition.x**；
- `PartnerOutputHalf` / `SetPartnerOutputHalf` / `SetPartnerCameraHalf` 只替换 partner 的纹理；partner 还没有半屏时仍会自动对齐一次，已经处于半屏时才**不修改对方的 Portal View localPosition.x**；
- 首次裁切/尺寸变化/正常刷新仍会重新计算半屏 X 偏移；
- `_fullSprite` 会缓存完整捕获纹理，所以可以在左半/右半之间反复切换，不会以半屏为源二次裁剪；
- 如果调用时 `partner` 尚未解析，控制值会缓存，等 `ResolvePartner()` 后补发。

因此 A 的 `ChangeFacing()` 应在完成自己的显示变化后调用：

```csharp
SetPartnerCameraHalf(PortalViewHalf.Right); // B 使用 A 相机的右半
```

而不是 `SetOutputHalf(...)`，后者只会改 A 自己的输出。

### 6.6 遮罩

每端两块 `View Mask Top/Bottom`：

- 随开门进度，Y 偏移从 0 到 `PortalPair` 配置值；
- 每帧转向玩家眼睛；
- SpriteMask 的自定义范围指向本门的 Portal A/B 层；
- Portal View 的 `maskInteraction = VisibleOutsideMask`。

> 已知问题：运行时主相机截图中曾出现 Portal View 以整块着色画面叠加、未被裁成门洞的现象。当前测试只校验遮罩节点、排序和高度，不校验像素；视觉验收时需要单独确认。

### 6.7 静态光带/粒子

- `CaptureDisplayGroup.hintRenderer` / `hintParticles` 是场景引用；
- 运行时 `EnsureHint()` 只赋 Sprite/材质，不创建对象；
- `UpdateHint()` 只更新 shader 参数（方向、宽度、流光、呼吸、噪音）和粒子方向；
- 光带/粒子的位置、缩放由场景 Transform 决定；
- 排序层统一为 `Portal light band`，光带 order 0，粒子 order 1；
- A 蓝、B 红；`Hidden/Platformer/PortalHintBeam` 加法混合。

### 6.8 传送

`PortalPair.Teleport`：

```text
front = gateThickness/2 + centerPointRadius + exitMargin

tangentOffset = dot(playerPos - sourcePos, source.up)

target = partnerPos
       + partner.OpeningDirection * front
       + partner.up * tangentOffset
```

出口旋转：

```text
exitAngle = DeltaAngle(0, partnerAngle - sourceAngle)
```

速度：

```text
normalSpeed  = dot(velocity, source.OpeningDirection)
tangentSpeed = dot(velocity, source.up)
outVelocity  = partner.OpeningDirection * (-normalSpeed)
             + partner.up * tangentSpeed
```

其他行为：

- 冷却 0.25 秒，一对门共享；
- 传送前 `StopAllOpenAnimations()`，再让发起门 `PlayOpenAnimation()`；
- 传送后本帧强制双门 `CaptureNow()` + `ApplyHalfCrop()`；
- 相机相对矩阵由 `PortalPair` 记录，`CameraFollow2D.TeleportRelative()` 延迟到 LateUpdate 应用。

### 6.9 玩家显示与回正

`PlayerController2D`：

- 物理根节点：Rigidbody2D、CapsuleCollider2D、Center Point；
- 显示节点：`PlayerVisual`（运行时创建），复制 SpriteRenderer 属性并禁用根节点渲染；
- `TeleportWithPortalRotation(position, exitAngle)` 只旋转显示节点；
- 回正参数：
  - `portalRealignSpeed=80`
  - `portalRealignCurve=EaseInOut`
  - `portalRealignFallSpeedForMaxBoost=12`
  - `portalRealignMaxFallSpeedMultiplier=3`
  - `portalRealignGroundedSpeedMultiplier=6`
- 回正在 `Update` 中推进；触地/下坠加速；结束后显示角度归 0。

### 6.10 相机

`CameraFollow2D` 当前实现：

- 位置：目标玩家根节点 + 旋转后的 `offset + lookAhead`，SmoothDamp；
- 角度：从 `PlayerVisual` 的 Z 角平滑跟随；
- 普通状态用 `rotationSmoothTime`，回正期间用更迟钝的 `realignRotationSmoothTime`；
- 传送时 `PortalPair` 记录：

```text
cameraRelative = playerVisual.worldToLocalMatrix * camera.localToWorldMatrix
```

- `CameraFollow2D.TeleportRelative()` 在 LateUpdate 用新的 `playerVisual.localToWorldMatrix * cameraRelative` 映射相机；
- `Decompose2D` 提取位置/Z 旋转/缩放；
- 前瞻按 `PlayerVisual.right` 投影；回正期间禁用前瞻。

> 这取代了旧版“只维护玩家显示空间 `_localOffset`”的方案；旧文档中的描述已不再适用。

### 6.11 地形开洞

`PortalTerrainCarver` 由 `PortalPair` 运行时 `AddComponent`：

- 矩形起点：门中心；
- 长度方向：`BackDirection`，长度 `carveWidth`；
- 高度：`PortalHeight`，居中；
- 向门面方向额外扩展 `carveFrontMargin`；
- 对每个被覆盖的 Tilemap 格子：
  - 取 Tile Sprite 的物理形状；
  - 与所有门的矩形做布尔差；
  - 有实际面积变化时：`Tile.ColliderType = None`，从 TilemapCollider2D 移除；
  - 剩余碎片生成子物体 `PolygonCollider2D`（Ground 层）；
- `ProcessTilemapChanges()` → `CompositeCollider2D.GenerateGeometry()` → `Physics2D.SyncTransforms()`；
- 门停用、移动、场景卸载时恢复 ColliderType、销毁残余碰撞、重重建；
- 不调用 `Sprite.OverridePhysicsShape` 或 `Tilemap.RefreshAllTiles()`。

---

## 7. 地形与描边

- 1 个 `Grid`、1 个 `Terrain` Tilemap；全部 1×1 瓦片。
- 10 个 Tile 资产：`TerrainTile`、`DarkTerrainTile`、4 方向斜坡 × 2 色。
- 当前场景使用：Terrain 162 格、Dark 66 格、SlopeUpRight 2 格、SlopeUpLeft 3 格。
- `TilemapCollider2D`（Used By Composite）+ `CompositeCollider2D`；物理材质 `TerrainPhysicsMaterial`（friction 0.6）。
- 斜坡使用精确三角物理形状。
- `TilemapOutline` 运行时生成 `Outline` Tilemap：
  - 实心格四邻域：邻空或异色 → 1px 内描边；
  - 深灰邻普通地形：只在深灰侧画交界线；
  - 斜坡格铺斜线；
  - Default/order 1，不写入场景。

---

## 8. 角色与相机参数

### Player

| 参数 | 值 |
|---|---:|
| 移动速度 | 9 |
| 地面加/减速 | 115 / 150 |
| 空中加/减速 | 70 / 45 |
| 跳跃高度 | 6 |
| 角色高度 | 1.5 |
| 土狼时间 / 跳跃缓冲 | 0.1 / 0.12 |
| 下落重力倍率 | 1.6 |
| 最大下落速度 | 30 |
| 松键截断 | 0.45 |
| 中心碰撞点半径 | 0.08 |

### Camera

| 参数 | 值 |
|---|---:|
| 正交尺寸 | 8 |
| 跟随偏移 | (0, 1) |
| 位置平滑 | 0.12 |
| 旋转平滑 | 0.15 |
| 回正旋转平滑 | 0.25 |
| 前瞻距离 / 参考速度 | 1.5 / 9 |

---

## 9. 运行时顺序

```text
Awake
  PlayerController2D：缓存、创建 Center Point、创建/解析 Player Visual
  PortalPair：CacheGroups → EnsureGates → EnsureTerrainCarver →
              PushPlayerToGroups → PushViewSettingsToGroups → 玩家胶囊 excludeLayers
  CaptureDisplayGroup：ResolvePartner → WireUp → 遮罩/玩家/视觉中心 →
                       EnsureHint（静态对象取引用 + 材质）

Start
  RegionSnapshotCamera：分配 RT/Texture/Sprite，启用 Portal Camera，Capture()
  CaptureDisplayGroup：记录 Portal View 初始位置，排除输出层

每帧
  RegionSnapshotCamera.LateUpdate：Capture（每帧）
  CaptureDisplayGroup.Update：颜色/可见性
  CaptureDisplayGroup.LateUpdate：开门动画 → 半屏裁剪 → 遮罩 → 光带参数
  PortalPair.LateUpdate：门洞高度同步；强制刷新
  CameraFollow2D.LateUpdate：位置/角度跟随；传送帧应用相对矩阵
  PortalTerrainCarver.LateUpdate：门移动/旋转时重切开洞
```

物理步中：玩家中心点撞门 → `CaptureDisplayGroup.OnCollisionEnter/Stay` → `PortalPair.OnGateHit` → 停止所有开门动画 → 发起门重播 → `Teleport`。

---

## 10. 编辑器工具

| 菜单 | 作用 | 状态 |
|---|---|---|
| `Platformer/Rebuild Level + Prefabs` | 从零重建场景/预制体 | **已落后，勿运行** |
| `Platformer/Rebuild + Render Preview` | 重建 + 预览 | 同上 |
| `Platformer/Open Level 01` | 打开主场景 | 可用 |
| `Platformer/Validate Level` | 校验相机/玩家/Tilemap/材质 | 可用 |
| `Platformer/Render Preview Image` | 只渲染预览 | 可用，但编辑模式不跑 Awake，光带/粒子不出现 |

`ProjectBuilder` 仍使用旧版：
- 主相机 5.625；
- Portal A/B 旧位置；
- PortalHeight 3、openDuration 0.3；
- 不创建静态光带/粒子；
- 不设置 carve 参数；
- 不创建 `Portal light band` / `Portal view` 排序层。

因此当前场景只能手动维护，不能 Rebuild。

---

## 11. 测试（23/23）

最近结果：`Logs/TestResults_scene.xml`，23 total / 23 passed / 0 failed，26.21 秒。

| 文件 | 用例数 | 覆盖 |
|---|---:|---|
| `PlayerControllerPlayTests.cs` | 7 | 站立、跳高、移动、相机、坠落、斜坡、出传送门旋转/回正 |
| `LevelTilemapTests.cs` | 5 | 关卡站立、描边、Portal View 偏移、斜坡、快照结构 |
| `PortalTeleportTests.cs` | 8 | 门洞/碰撞箱、开门动画、重新启用、传送、淡出、光带、背面隐藏 |
| `PortalTerrainCarverTests.cs` | 3 | 开洞/恢复、开口方向、门移动跟随 |

---

## 12. 已知限制 / 风险

1. **不要运行 `Rebuild Level + Prefabs`**：`ProjectBuilder` 未跟进当前场景，会覆盖旋转门、静态光带、开洞参数和新地形。
2. **遮罩裁剪未完全验证**：运行时截图曾显示 Portal View 以大块着色画面叠加；测试只校验结构，不校验像素。
3. **静态光带/粒子不跟随运行时尺寸变化**：`PortalHeight` 改变不会重设光带 scale/粒子 shape；`hintBackOffset` 只影响旧的 `HintLinePosition` 属性。
4. **`Portal view` 排序层未使用**：当前 Portal View 仍在 Portal A/B 层。
5. **地形开洞是运行时碰撞**：残余 `PolygonCollider2D` 不是烘焙资产；若改用静态碰撞/烘焙需要同步改造。
6. **每帧捕获开销**：两台 Portal Camera 每帧 `Render + ReadPixels`；静态 50×50 默认 2000×2000 纹理，动态模式则随主相机/窗口变化。`captureCamera.enabled=true` 时 URP 还可能自动渲染一次。
7. **`PortalHintBeam` 构建风险**：Shader 只通过运行时 `Shader.Find` 使用，没有材质资产引用；Player 构建可能被剥离。
8. **`carveTerrain` 运行时开关**：从 `false` 改回 `true` 时，如果 `PortalTerrainCarver` 已存在且宽度未变，`PortalPair.Update` 不会重新启用组件。

---

## 13. Git / 维护

- 最近功能提交：`83e95e7 fix(portal): 连续传送时重置相机与开门动画`
- 当前改动：`PortalPair` 统一管理捕获尺寸（`useDynamicSize`、`cameraSizeMultiplier`、`captureWidth/Height`），并下发给两个 `RegionSnapshotCamera`
- `backup/capture-v1`：未合并的旧对象复制实验
- `feature/portal-every-frame` / `test`：旧分支，指向 `156a16b`
- 提交前请确认：
  - 没有修改场景中的静态光带/粒子的 Transform；
  - 没有运行 Rebuild；
  - 传送门 A/B 的 `facing`、旋转、PortalHeight、carve 参数与场景一致；
  - PlayMode 23 个用例通过。
