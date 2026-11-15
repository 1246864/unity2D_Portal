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
  - **不递归**：传送门相机排除输出精灵所在的 **Snapshot** 层，主相机照常显示门内画面
- 传送已实现：门内有极薄碰撞箱（高度 = 门洞高度），玩家中心的碰撞点撞到即传送到另一扇门 `facing` 外侧一点点；传送当帧强制双门重拍；玩家在门背面时该门画面隐藏（详见 `docs/UPDATE_2026-11-14.md`）

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
│  ├─ Editor/ProjectBuilder.cs   一键构建关卡/预制体/传送门（约 800 行）
│  ├─ Palettes/TerrainPalette.prefab
│  ├─ Prefabs/Player.prefab
│  ├─ Scenes/Level01.unity       主场景
│  ├─ Scripts/
│  │  ├─ PlayerController2D.cs
│  │  ├─ CameraFollow2D.cs
│  │  ├─ CaptureDisplayGroup.cs  传送门端（A/B）
│  │  ├─ PortalPair.cs           传送门对总控
│  │  ├─ RegionSnapshotCamera.cs 相机捕获 → 贴图 → 精灵
│  │  └─ TilemapOutline.cs       瓦片规则描边层
│  ├─ Settings/                  URP、Renderer2D、物理材质
│  ├─ Shaders/PortalHintBeam.shader  门位置光带（渐变/流光/两端高亮）
│  ├─ Tests/PlayMode/            18 个自动化测试
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
├─ Portal A                 local (0.83, -1.46)  → world (3.83, 1.54)
│  ├─ Portal Camera         (正交，z = -10，viewScale 1.2)
│  ├─ Portal Capture        (RegionSnapshotCamera，驱动相机→贴图)
│  ├─ Portal View Center     (视觉中心，local z = -10；View 的父节点)
│  │  └─ Portal View         (SpriteRenderer，order 20，VisibleOutsideMask；local z = +10)
│  └─ Portal Mask           (遮罩容器)
│     ├─ View Mask Top      (scale (1, -1))
│     │  └─ Mask            SpriteMask，local (0, 50)，scale (200, 100)
│     └─ View Mask Bottom   (scale (1, -1))
│        └─ Mask            SpriteMask，local (0, -50)，scale (200, 100)
└─ Portal B                 local (-14.89, -1.48) → world (-11.89, 1.52)
   ├─ Portal Camera
   ├─ Portal Capture
   ├─ Portal View Center     (视觉中心，local z = 0)
   │  └─ Portal View         (SpriteRenderer，order 20，VisibleOutsideMask)
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
| `PortalPair` | `Portal Pair` | **传送业务总控**：玩家引用/眼高、薄碰撞箱、碰撞触发、位移/速度/相机、冷却、传送后双门成像刷新 |
| `CaptureDisplayGroup` | `Portal A` / `Portal B` | 每端的显示逻辑：配对、交叉连线、遮罩驱动、画面裁剪、背面隐藏，并把门碰撞转发给 `PortalPair` |
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

`centerGapPixels`（默认 1）让两半在中线各让出一点，避免半屏拼接把共用像素列放大成亮线。

### 6.4 遮罩

每端两组 `View Mask Top/Bottom`，由 `UpdateMasks()` 每帧驱动：

```csharp
maskTop.localPosition    = new Vector3(0f, pair.maskTopOffset, 0f);     // 默认 +1.5
maskBottom.localPosition = new Vector3(0f, pair.maskBottomOffset, 0f);  // 默认 -1.5
// 对准玩家眼睛后再旋转 90°
float angle = Mathf.Atan2(dir.y, dir.x) * Mathf.Rad2Deg;
mask.rotation = Quaternion.Euler(0f, 0f, angle);
```

- 两个偏移配在 `PortalPair` 上，**一对门共用**，所以两扇门的门洞高度始终一致；
- 遮罩只在**本门排序层**内生效（A 不影响 B）；
- `PortalHeight = |maskTopOffset - maskBottomOffset|`：`PortalPair` 与 `CaptureDisplayGroup` 都暴露这个只读属性，后者直接委托给前者；
- 开门动画：`CaptureDisplayGroup.PlayOpenAnimation()`（`OnEnable` 时自动调用，禁用后重新启用也会重播），在 `openDuration` 内把上下偏移从 **0** 增长到 `PortalPair` 配置值，节奏由 `openCurve` 控制；只驱动遮罩视觉，碰撞箱仍按完整门洞高度。传送时**发起传送的那扇门**额外重播一次自己的开门动画（从 A 进 → A 播，从 B 进 → B 播），另一扇门不重播。
- 另一扇门透明度：`CaptureDisplayGroup.otherViewFadeDistance`（默认 **3**，与颜色距离公式同一世界单位）。玩家离某扇门越近，另一扇门的 alpha 越低：距离 0 → 0，达到阈值 → 1；本门自己不变透明。实现里缓存本门/对方门的视觉中心，每帧只做两次局部位移和一次颜色赋值。
- 门位置光带：`CaptureDisplayGroup` 在门的背面（`-facing` 偏移 `hintBackOffset`）生成一块沿 facing 方向铺开的光带（`hintBeamLength` 默认 0.45），靠门一侧是加粗的细线核心；高度 = `PortalPair.PortalHeight + hintExtraHeight`（默认 +0.3，两端从门洞遮罩外露出一点）。`PortalHintBeam` shader：靠门侧粗核心、沿 facing 渐隐到透明、两端高亮、竖向流光叠加噪音失真（`hintNoiseStrength`），噪音掩码在 UV 两轴持续滚动（`hintNoiseSpeed` 0.9）、整体透明度 **0.5**，呼吸频率 **0.5 Hz（2 秒一个周期，`_BreathFrequency`）**、alpha 在 **0.2~1.0** 之间变化；A 蓝 / B 红。光带排序在 Portal View 之下（order-1），主相机里中间被输出盖住，但 Default 层仍开放给传送门相机；粒子从细线沿 facing 发射同色粒子（有效 11.25/s，发射区域比门洞小 0.1，池 256），排序在输出之上。
- 动态同步：`FacesLeft` / `FacingSign` 始终由 `facing` 实时派生；`HintLinePosition` 实时由视觉中心、朝向和偏移计算；`Pair` 在父级变化时重新解析；`_viewAnchor` / `_partnerViewAnchor` 每帧刷新，视觉中心或对方门被换掉时不会继续使用旧缓存。

### 6.5 不递归（排除输出层）

传送门相机在 `CaptureDisplayGroup.Start`（以及 `ProjectBuilder`）里把 `Portal View` 所在的 **Snapshot(8)** 层从 `cullingMask` 排除；主相机仍渲染该层。因此捕获相机不会拍到自己的输出，两门互拍不会形成无限嵌套，门洞里是对方相机的**一次**成像。

### 6.6 朝向

`CaptureDisplayGroup.facing`（`PortalFacing.Left / Right`）：
- 当前场景：A = **Left**，B = **Right**（两门面对面）
- 辅助：`FacesLeft`、`FacingSign`（-1 / +1）
- 开口方向 = `Quaternion.Euler(0, 0, FacingSign * -90) * Vector3.up`，即朝右 `(1,0)`、朝左 `(-1,0)`

传送用它决定：出口落在该门 `facing` 一侧；两门同向时水平速度取反、相背时保持；玩家与开口方向夹角 ≥ 90° 时隐藏该门画面。

### 6.7 传送（PortalPair）

传送业务集中在 `PortalPair`，`CaptureDisplayGroup` 只负责画面并把碰撞转发过来。

- **碰撞箱**：`PortalPair` 在 `Portal A/B` 根节点上运行时创建 `BoxCollider2D`，`size = (gateThickness, PortalHeight)`、非 Trigger，高度随遮罩偏移同步；
- **触发**：玩家中心的 `CircleCollider2D`（`PlayerController2D.centerPointRadius`，默认 0.08）撞到箱子；门箱放在 **Snapshot 层**，身体胶囊用 `excludeLayers` 排除该层（不用 `Physics2D.IgnoreCollision`——它在碰撞箱重新启用后会被重置，导致重新开门时被当成墙）；
- **位移**：`target = partner 中轴 ± (gateThickness/2 + centerPointRadius + exitMargin)`，垂直保留相对高度；
- **速度**：两门同向时水平速度取反，相背时保持；
- **相机**：传送前记录与玩家的相对位置，传送后瞬间搬到"新玩家 + 同一相对位置"；
- **冷却**：一对门共享 `teleportCooldown`；
- **刷新**：传送后同一帧 `LateUpdate` 强制两门捕获相机各拍一次并重新半屏成像。

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
| 中心碰撞点半径 | 0.08（传送门检测用） |

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
- **瓦片规则外描边**：`TilemapOutline`（挂在 `Terrain` 上）在运行时创建独立的 `Outline` Tilemap；按四邻域枚举：地形格邻空画 1px 内描边，深灰格邻接普通地形时在深灰侧画交界线，斜坡格铺斜线；空格不画外圈；颜色 `(0.70, 0.79, 0.92)`，线宽 1px
- 描边层排序 `Default` / order `1`，在地形之上、传送门画面与玩家之下
- 已移除 `ScreenOutlineFeature`、`ScreenOutline.shader`、`ScreenOutline.mat`；玩家和传送门画面不再有外描边

## 11. 构建脚本（ProjectBuilder）

菜单（`Platformer`）：

| 菜单 | 作用 |
|---|---|
| `Rebuild Level + Prefabs` | **重建整个场景**（会覆盖手动修改） |
| `Rebuild + Render Preview` | 重建 + 出预览图 |
| `Render Preview Image` | 只渲染预览，不改场景 |
| `Validate Level` | 校验瓦片数、材质、描边组件 |
| `Open Level 01` | 打开主场景 |

命令行：

```
Unity.exe -batchmode -projectPath <项目> -executeMethod Platformer.EditorTools.ProjectBuilder.BuildAll -quit
Unity.exe -batchmode -projectPath <项目> -runTests -testPlatform PlayMode -testResults Logs\TestResults.xml
```

## 12. 测试（19 个 PlayMode 用例：18 通过 + 1 跳过快照）

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
| `Tilemap_Has_Outline_Layer` | 瓦片规则描边层存在且有瓦片 |
| `Snapshot_Camera_Captures_Own_View` | 捕获相机独立、1.2 倍尺寸、1:1、画布随视野、A/B 半屏、交叉连线 |
| `Portal_Gates_Match_Portal_Height` | 门碰撞箱高度与 PortalPair 门洞高度一致 |
| `Player_Teleports_Through_Portal` | 走进门传送到对侧门正面 |
| `Player_Still_Teleports_After_Portal_Reenable` | 重新启用门后仍能传送（碰撞箱不被当墙） |
| `Portal_Opens_From_Zero_To_Pair_Height` | 开门动画从 0 增长到配置高度 |
| `Portal_View_Hides_When_Player_Is_Behind` | 玩家在门背面时隐藏画面 |
| `Portal_View_Keeps_Offset_Under_View_Center` | 半屏裁剪保留 Portal View 的 Z 偏移 |
| `Other_Portal_Fades_When_Player_Is_Near` | 靠近一扇门时另一扇门按距离变透明 |
| `Portal_Hint_Beam_Matches_Height_And_Sorting` | 光带高度/排序/材质/朝向/呼吸/噪音/粒子 |
| `Portal_Hint_Properties_Follow_Runtime_Changes` | 动态移动视觉中心/改变朝向后属性与光带同步 |

## 13. 已知限制 / 下一步

1. 传送已实现（碰撞箱 + 玩家中心碰撞点 + 连续碰撞），细节见 `docs/UPDATE_2026-11-14.md`；门背面的碰撞箱仍然有效，只是画面隐藏。
2. 遮罩尺寸（`Mask` 子物体 `scale (200,100)`、偏移 `±50`）是当前调好的视觉参数，改动会影响门洞大小与朝向。
3. 瓦片描边只按四邻域枚举：斜坡外侧不额外描边、斜角内凹处按四邻域近似；描边层在 Play 时生成，不写入场景。
4. 场景手动修改后**不要**跑 `Rebuild Level + Prefabs`。

## 14. 迁移记录

- 源目录：`unity2Dcsm` → 目标：`unity2D_Portal`
- 保留：git 历史、所有 `.meta`（GUID 不变）、场景层级、排序层、预制体引用
- 迁移后验证：工程可导入、编译无错、9/9 PlayMode 测试通过
