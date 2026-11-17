# 2D 平台跳跃 + 传送门（Unity 2022.3.61f1c1 / URP 2D）

一个可运行的 2D 横版平台跳跃 + 双门传送门原型。关卡由 1×1 Tilemap 搭建，角色支持 6 格跳高、45° 斜坡、土狼时间和跳跃缓冲；场景中的 `Portal A` / `Portal B` 支持任意角度旋转、玩家显示/物理分离、地形动态开洞和相机相对映射。

> 当前验证：**23/23 PlayMode 通过**（最近一次：`Logs/TestResults_cleanup.xml`，25.93 秒）。
> 详细架构、场景数据与已知问题见 **[PROJECT.md](PROJECT.md)**，实现细节见 **[docs/DEVELOPMENT.md](docs/DEVELOPMENT.md)**。

## 快速开始

1. Unity Hub → Open → 选择本目录。
2. 使用 **Unity 2022.3.61f1c1** 打开。
3. 打开场景 `Assets/Scenes/Level01.unity`（或菜单 `Platformer > Open Level 01`）。
4. 点 Play。

> ⚠️ 当前场景包含大量手动/运行时功能（旋转门、静态光带、地形开洞参数），**不要执行** `Platformer > Rebuild Level + Prefabs`：构建脚本仍停留在旧版关卡，会覆盖当前场景。

## 操作

| 操作 | 按键 |
|---|---|
| 左右移动 | `A` / `D` 或 `←` / `→` |
| 跳跃 | `空格`（按住更高，松开截断上升） |

## 当前内容

- **地形**：233 个 1×1 瓦片，bounds `x=-16..23`、`y=-4..11`；普通 `TerrainTile`、深灰 `DarkTerrainTile`、右上/左上斜坡瓦片。
- **角色**：胶囊 0.72×1.5，最大跳跃高度 **6 格**（实测 5.999），支持斜坡、土狼时间、跳跃缓冲、坠落重生。
- **相机**：主相机正交 **8**；位置/角度分别平滑跟随，传送时用相对矩阵保持画面连续。
- **传送门**：
  - A 位于 world `(5.78, 2.04)`，未旋转，朝左；
  - B 位于 world `(10.68, 3.68)`，**旋转 45°**，朝右；
  - 门洞高度 4（`maskTopOffset=2` / `maskBottomOffset=-2`），开门动画 0.6 秒；
  - 门内薄碰撞箱、中心碰撞点、冷却、速度坐标变换；
  - 玩家出传送门后只旋转显示节点，胶囊保持竖直，并按参数回正。
- **地形开洞**：门激活时沿门背面切掉一块地形碰撞（`carveWidth=0.8`、`carveFrontMargin=0.1`），视觉瓦片不变；剩余碰撞用运行时 `PolygonCollider2D` 补回。
- **门位置提示**：场景静态 `Portal Hint` 光带 + `Portal Hint Particles`，使用 `Portal light band` 排序层；运行时只赋材质/参数。
- **描边**：`TilemapOutline` 在运行时按四邻域生成独立 `Outline` Tilemap，不写入场景。

## 传送门一览

```text
Main Camera
Portal Pair
├─ Portal A                  CaptureDisplayGroup(A)，facing Left
│  ├─ Portal Camera          正交，运行时 orthoSize = 主相机 × 1.2
│  ├─ Portal Capture         RegionSnapshotCamera
│  ├─ Portal Mask            SpriteMask 上下两块
│  ├─ Portal View Center
│  │  └─ Portal View         SpriteRenderer，A 层/order 20
│  ├─ Portal Hint            静态光带
│  └─ Portal Hint Particles  静态粒子
└─ Portal B                  CaptureDisplayGroup(B)，facing Right，旋转 45°
   └─ （同上结构）
```

- A 的相机画面画到 B 的 `Portal View`，B 的相机画面画到 A 的 `Portal View`；
- Portal View 的 Snapshot(8) 层从传送门相机 `cullingMask` 排除，避免递归；
- A 保留右半、B 保留左半，中线留 1px；
- 遮罩节点按开门进度上下移动，并转向玩家眼睛。

## 地形与描边

- `Grid` 下单个 `Terrain` Tilemap；`TilemapCollider2D`（Used By Composite）+ `CompositeCollider2D` 合并碰撞。
- 普通色 `#E0E0E0`，深灰 `#454C5C`；10 个 Tile 资产（2 个方块：普通/深灰 + 8 个斜坡：4 方向 × 2 色）。
- `TilemapOutline`：实心格按四邻域画 1px 内描边；深灰与普通地形交界只在深灰侧画；斜坡铺设斜线；描边层 Default/order 1。
- 瓦片贴图自带 1px、亮度 0.929 的内描边。

## 测试

命令行：

```powershell
Unity.exe -batchmode -projectPath <项目> `
  -runTests -testPlatform PlayMode `
  -testResults Logs\TestResults.xml `
  -logFile Logs\playtests.log
```

当前 **23 个 PlayMode 用例**：

- 角色物理：站立、6 格跳、水平移动、45° 斜坡、坠落重生；
- 相机：跟随、出传送门旋转/回正；
- Tilemap：合并碰撞站立、描边层、斜坡爬升、快照结构；
- 传送门：门洞高度、开门动画、重新启用后传送、出口位置/旋转、速度、另一门淡出、光带、背面隐藏；
- 地形开洞：切割/恢复、开口方向实时旋转、门移动后跟随。

## 目录

```text
Assets/
  Editor/
    ProjectBuilder.cs            旧版重建工具（已落后，勿运行）
    PlayerController2DEditor.cs  中文玩家 Inspector
  Prefabs/Player.prefab
  Scenes/Level01.unity
  Scripts/
    PlayerController2D.cs
    CameraFollow2D.cs
    CaptureDisplayGroup.cs
    PortalPair.cs
    PortalTerrainCarver.cs
    RegionSnapshotCamera.cs
    TilemapOutline.cs
  Shaders/PortalHintBeam.shader
  Tests/PlayMode/
  Tiles/  Palettes/  Settings/  Art/Placeholders/
docs/DEVELOPMENT.md
PROJECT.md
```

## 重要限制

1. `ProjectBuilder` 未跟进当前场景：不要运行 `Rebuild Level + Prefabs`。
2. 光带/粒子是场景静态对象；运行时改 `PortalHeight` 不会改变它们的大小，`ProjectBuilder` 也不会创建它们。
3. `Portal View` 的 SpriteMask 裁剪在运行时截图中曾出现“整块画面叠加”的异常；测试只校验遮罩节点/排序，不校验像素。
4. `Portal view` 排序层已加入 TagManager，但当前场景未使用。
5. 地形开洞依赖运行时 `PolygonCollider2D`，不是烘焙碰撞；后续若改为静态碰撞需要同步改造。
