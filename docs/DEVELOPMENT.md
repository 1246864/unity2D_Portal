# 开发文档 — 2D 平台跳跃模板

> 记录从零到当前状态的完整开发过程、关键决策与验证数据。
> 最后更新：2026-11-11

---

## 1. 项目概述

目标：制作一个**可直接运行、便于二次修改**的 2D 平台跳跃基础模板，后续在此基础上迭代具体游戏内容。

当前状态：

- 可运行关卡（1×1 瓦片地形、45° 斜坡、悬浮平台）
- 胶囊角色控制器（6 格跳高、土狼时间、跳跃预输入、可变跳高、斜坡行走、坠落重生）
- 平滑跟随相机（带方向前瞻）
- 全屏合并描边（URP 2D Renderer Feature）
- 6 个 PlayMode 自动化测试
- Git 版本管理

---

## 2. 环境与依赖

| 项目 | 值 |
|---|---|
| 操作系统 | Windows 11 |
| Unity | **2022.3.61f1c1**（Unity 中国版，`china_unity` 分支） |
| 渲染管线 | URP 14.0.12（2D Renderer） |
| 工程来源 | Unity 官方 `com.unity.template.universal-2d-2.1.3` 模板抽取 |
| 输入 | 旧版 Input Manager（`Horizontal` / `Jump`） |
| 色彩空间 | Linear |
| 脚本运行时 | .NET Standard 2.1 |

主要包：`com.unity.feature.2d`、`com.unity.render-pipelines.universal`、`com.unity.test-framework`、`com.unity.ugui`、`com.unity.textmeshpro`。

### 许可证说明

- Unity Personal（`AlwaysOnline`），通过 Unity Hub 在线激活。
- 命令行批处理运行时，编辑器会写 `%LOCALAPPDATA%\Unity` 与 `%PROGRAMDATA%\Unity` 下的授权缓存；若这些目录不可写，会直接退出并返回 **140063（授权失败）**。
- 本机残留的 `Unity_lic.ulf` 中 `StopDate` 为短期令牌，联网启动会自动续期。

---

## 3. 开发历程

### 阶段 0 — 环境勘察

- 发现本机装有两个编辑器：`2022.3.49f1c1`（无 PlaybackEngines）与 `2022.3.61f1c1`（含 Windows/Android/iOS/WebGL/Linux/Mac 模块）。选定后者。
- 初次命令行启动返回 140063：许可证 `StopDate` 已过期。
- 用户在 Hub 重新激活后，日志出现 `Successfully updated license`、序列号分配与 entitlement 解析，授权恢复。
- 结论：批量构建必须允许编辑器写入授权缓存目录。

### 阶段 1 — 工程骨架

- 从官方 URP 2D 模板解包 `ProjectData~`，作为工程基础（URP 资源、Renderer2D、场景模板）。
- 用 Python + Pillow 生成占位精灵（纯色胶囊、方块、斜坡），统一 **PPU = 32**（32px = 1 格）。
- 实现 `PlayerController2D`：水平加减速、跳跃、地面检测、重生。
- 实现 `CameraFollow2D`：SmoothDamp 跟随 + 移动方向前瞻。
- 实现 `ProjectBuilder`（编辑器脚本）：一键导入贴图设置、生成预制体、搭建场景、写 Build Settings；支持菜单与 `-executeMethod` 批处理。
- 用 asmdef 划分三层程序集：`Platformer.Runtime` / `Platformer.Editor` / `Platformer.PlayTests`。
- 首次 PlayMode 测试：0 个用例被发现 → 原因是测试程序集被标记为 Editor-only；改为标准 PlayMode 测试程序集后正常。

### 阶段 2 — 手感与物理调优

关键指标：**跳跃高度恰好 6 格**、角色高 1.7 格、45° 斜坡可走。

- 跳跃初速度按 `v₀ = √(2·g·h) + ½·g·Δt` 计算（后半项补偿离散积分），实测顶点 **5.999 格**。
- 改重力不会改变跳高（每次起跳实时计算）。
- 斜坡：地面探测返回法线，接地时把水平速度换算为沿坡速度；平地时把垂直速度归零，避免离开斜坡时被“弹飞”。
- 加入土狼时间（0.1s）、跳跃预输入（0.12s）、下落额外重力（1.6×）、跳跃截断（0.45）。
- 修复测试中的两个问题：
  - 批处理模式帧率极高，`yield return null` 的“120 帧”只相当于极短时间 → 相机测试改用 `WaitForSeconds`。
  - 直接改 `transform.position` 会被插值的刚体覆盖 → 增加 `Teleport()` 接口。

### 阶段 3 — 合并描边 v1（已废弃）

- 需求：相邻同材质方块合并成一条外轮廓，而不是每格一个边框。
- 方案：自定义精灵 Shader + `MergedOutlineBlock` 组件，逐块检测上下左右是否有**同色同组**邻居，有邻居的边不画描边。
- 结果：同色满边相邻时合并正常（37 块、72 条内部边），但 **2×1 / 1×2 与 2×2 混排时会出现部分接触，导致轮廓断线**。
- 结论：逐块方案无法优雅处理任意尺寸拼接，决定升级。

### 阶段 4 — 2 格地形重构 + 全屏描边 v2

需求变化：地图以 **2 格为模块**（2×2、2×1、1×2），1 格作为精细单位；地板/墙不再区分颜色与类型。

实施：

- 新增精灵：`block2x2`（64×64）、`block2x1`（64×32）、`block1x2`（32×64）、`slope2x2`（64×64 三角）。
- 全部地形统一颜色 `#66758F`，贴图为纯白，由 `SpriteRenderer.color` 上色。
- 关卡改为 2 格网格：2 格厚地面、4 格宽坑、每级 2 格的台阶、2×2 斜坡。
- 描边改为 **全屏边缘检测**：
  - `ScreenOutlineFeature`（Renderer Feature）+ `Hidden/Platformer/ScreenOutline` Shader。
  - 在 `AfterRenderingTransparents` 阶段把相机颜色拷贝到临时 RT，检测四邻颜色差，叠加描边色后写回。
  - 同色地形内部无颜色差 → 自动合并成一条轮廓；任意形状/尺寸/拼接均成立。
- 踩坑：`ScriptableRendererData.SetDirty()` 只刷新内存，不序列化特性列表；改用 `SerializedObject` 修改 `m_RendererFeatures` 并 `AssetDatabase.SaveAssets()`，重启后仍在。

### 阶段 5 — 描边细化与工程化

- 描边宽度 2px → **1px**，边缘过渡带宽 0.08 → 0.03，观感更干净。
- 初始化 Git 仓库，补充开发文档。

### 阶段 6 — 角色调整与第二地形类型

- 角色高度 1.7 → **1.5 格**：胶囊贴图 24×48 px、碰撞体 0.72×1.5，出生点 y 调整为 0.75，相机偏移 1.2 → 1.0。
- 移动速度 7 → **9**，地面/空中加减速同步提高（115/150、70/45），相机前瞻速度基准同步改为 9。
- 新增**深灰地形** `Block2x2Dark`（`#454C5C`）：由于描边基于颜色差，深灰之间同色自动合并，深灰与普通地形之间必然画线，**无需额外逻辑**。
- 关卡高台顶部放置 2 个深灰 2×2 方块用于演示。
- 测试全部通过（跳高仍为 5.999，斜坡峰值 2.76）。

### 阶段 7 — 物理材质统一

- 新增 `Assets/Settings/TerrainPhysicsMaterial.physicsMaterial2D`（friction 0.6 / bounciness 0）。
- 5 个地形预制体（Block2x2 / Block2x2Dark / Block2x1 / Block1x2 / Slope2x2）的碰撞体全部引用同一个材质资产。
- `EnsurePhysicsMaterial` 改为「已存在则复用」：重复重建关卡不会覆盖在 Inspector 里手调的摩擦/弹性。
- 验证脚本增加统计：场景中所有 Ground 层的碰撞体都必须挂载该材质，否则报错。

### 阶段 8 — Tilemap 重构

地形建造方式从「逐块实例化预制体」改为 **Tilemap**：

- 新建 `TerrainTile` / `DarkTerrainTile` 两个 Tile 资产（1×1，纯白贴图 + 颜色）。
- 场景结构变为 `Level/Grid/Terrain`：`Tilemap` + `TilemapRenderer` + `TilemapCollider2D`（Used By Composite）+ `CompositeCollider2D` + 静态 `Rigidbody2D`。
- 整张地形合并为**一个多边形碰撞体**，避免瓦片接缝卡脚。
- 关卡布局仍按 2 格模块编写，`PaintModule` 在 1 格网格上刷 2×2 / 2×1 / 1×2 区域；视觉与之前完全一致（同一预览图哈希）。
- 45° 斜坡保留为预制体（三角形特殊形状，瓦片无法直接表达）。
- 踩坑：脚本刷完瓦片后，`TilemapCollider2D` 没有立即生成形状，保存的场景里合并碰撞体是空的（`m_CompositePaths: []`），运行时玩家直接穿地。修复：绘制后依次调用
  `tilemap.RefreshAllTiles()` → `tilemapCollider.ProcessTilemapChanges()` → `composite.GenerateGeometry()`。
- 新增集成测试 `Tilemap_Level_Has_Grounded_Player`：加载 Level01，校验瓦片数（132）与玩家站立位置（y=0.75）。

### 阶段 9 — 统一 1×1 瓦片与资产清理

- Tilemap 本身已按 1×1 存储，2×2 / 2×1 / 1×2 的「方块尺寸变体」概念彻底移除：构建脚本改为 `PaintCell / PaintRow / PaintColumn` 逐格绘制，`PaintModule` 删除。
- 删除官方模板遗留的未使用资产：`Assets/Settings/Lit2DSceneTemplate.scenetemplate`、`Assets/Settings/Scenes/URP2DSceneTemplate.unity`（GUID 引用检查确认无任何使用方）。
- 在独立副本中重建验证：编译无错误、132 格、预览图 SHA-256 与重构前完全一致。

### 阶段 10 — 1×1 斜坡瓦片（4 方向 × 2 色）

- 新增 4 个方向的 1×1 45° 三角形精灵（右上 / 左上 / 右下 / 左下），普通色与深灰各一套，共 **8 个 Tile 资产**。
- 关卡里的 2×2 斜坡预制体由 3 个瓦片拼成（2 个斜坡瓦片 + 1 个实心瓦片），`Slope2x2.prefab` 与 `slope2x2.png` 删除。
- 难点 1：三角形的直角边在超采样缩小时留下半透明像素，相邻瓦片接缝被全屏描边检测到，出现内部线条。修复：把三角顶点延伸到画布边缘（0/32 而不是 0/31），直角边变为完全不透明。
- 难点 2：Unity 自动生成的 fallback 物理形状沿抗锯齿边缘内缩约 0.06 格，玩家会陷进斜坡。修复：通过 `SpriteDataProviderFactories` + `ISpritePhysicsOutlineDataProvider.SetOutlines` 写入**精确三角形物理形状**（注意：坐标是**以轴心为原点的像素**，不是 0..1 归一化）。
- 合并碰撞体检出精确斜边 (10,0)→(12,2)；新增 `Player_Climbs_Tilemap_Ramp` 集成测试验证玩家能走上斜坡。

### 阶段 11 — 修复 45° 斜边描边

- 问题：深灰 45° 斜坡的斜边几乎没有描边；补对角采样后又出现黑白棋盘格。
- 原因：描边用「中心与四邻的颜色差」判定，而 45° 斜边是抗锯齿渐变，轴向采样会沿边滑动，色差时有时无。
- 方案：轮廓边改为**与相机背景色做二值内外判断**，在形状内侧 1px + 外侧 1px 画线；类型边界（普通/深灰）仍用色差判定，且只在两侧都属于形状内部时生效，避免与轮廓边重复。
- 结果：斜边变为连续的单像素线，整体描边更细更干净。该方案依赖纯色相机背景。

### 阶段 12 — 普通地形改为超浅灰

- 普通地形（`TerrainTile` + 4 个普通斜坡瓦片）颜色由 `#66758F` 改为 `#E0E0E0`。
- 直接修改 Tile 资产的 `m_Color`，场景无需重建即可生效；`ProjectBuilder.TerrainColor` 同步更新，保证后续重建一致。
- 深灰变体（`DarkTerrainTile` + 4 个深灰斜坡）保持不变。

### 阶段 13 — 每个瓦片的内描边

- 需求：除了整体外轮廓，每个 1×1 瓦片也要有独立的边，颜色是自身亮度降一档（`#E0E0E0 → #D0D0D0`）。
- 方案：在生成贴图时给每个瓦片烘焙一圈 1px、亮度 0.929 倍的内描边。Tile 颜色与贴图相乘，所以任意颜色都会得到各自对应的暗一档描边，无需为每种颜色单独做图。
- 屏幕空间描边只负责整体轮廓和类型分界；把 `_Threshold` 从 0.05 提到 **0.28**，避免把瓦片内描边误判成新的边界而重复描线。
- 三角形瓦片的边通过「alpha 掩码腐蚀」生成（`ImageFilter.MinFilter`），沿斜边也是 1px。
- 结果：浅灰地形呈白色块体 + 细网格 + 浅蓝外轮廓；深灰块同样带网格。

### 阶段 14 — 区域捕获复制

- 新增 `Assets/Scripts/CaptureRegion.cs`：挂在空物体上的区域标记，橙色 Gizmo 框，`size` 可在 Inspector 调整。
- 新增 `Assets/Editor/RegionCapture.cs`：按区域边界取整到格子，把范围内的 Tilemap 瓦片复制到新的 Grid/Tilemap/合并碰撞体（沿用 TerrainPhysicsMaterial），并把与区域相交的 SpriteRenderer 对象整体 `Instantiate`。
- 复制体统一挂在 `Captured Region` 下，放在原区域上方 2 格，避免与原关卡重叠。
- 关卡总览预览改为自动取景（遍历 SpriteRenderer / TilemapRenderer 的 bounds），复制区域出现后也会被框进画面。
- 测试适配：`Tilemap_Level_Has_Grounded_Player` 改为选择格子最多的 Tilemap，避免被捕获区域的小 Tilemap 干扰；8/8 通过。

### 阶段 15 — 运行时镜像、对象池与脚本基类

- 新增 `PlatformerBehaviour` 基类：`PlayerController2D`、`CameraFollow2D`、`CaptureRegion`、`RegionMirror` 全部继承；`MarkAsCopy()` 禁用逻辑、冻结刚体、关闭碰撞体，副本只负责显示。
- 新增 `SimpleObjectPool`：池化 GameObject，记录创建数，支持预热与批量回收。
- 新增 `RegionMirror`：每帧从 `CaptureRegion` 读取范围内瓦片（Tile + 每格颜色）与相交精灵，用池中的纯显示精灵输出到 `destination`；窗口移动/缩放下一帧自动跟随。
- 编辑器捕获工具复制出的对象自动 `MarkAsCopy()`，静态副本也不再响应输入。
- 场景新增 `Region Mirror` + `Mirror Output`；预览渲染前强制 `RebuildNow()`，渲染后隐藏池对象。
- 新增测试 `Mirror_Follows_Capture_Window`：缩小/放大窗口验证镜像每帧更新；9/9 通过。

---

## 4. 工程结构

```
Assets/
  Art/Placeholders/          占位精灵（纯白，运行时上色）
    tile1x1.png  slope_up_right.png  slope_up_left.png
    slope_down_right.png  slope_down_left.png  player_capsule.png
  Editor/
    Platformer.Editor.asmdef
    ProjectBuilder.cs        一键重建：贴图设置/预制体/场景/BuildSettings/描边特性
  Materials/
    ScreenOutline.mat        描边材质（颜色/宽度/阈值）
  Prefabs/
    Player.prefab
  Tiles/
    TerrainTile.asset  DarkTerrainTile.asset
    SlopeUpRight/UpLeft/DownRight/DownLeft（+ 各 Dark 版本）
  Scenes/
    Level01.unity            主场景
  Scripts/
    Platformer.Runtime.asmdef
    PlayerController2D.cs    角色控制器
    CameraFollow2D.cs        相机跟随
    Rendering/ScreenOutlineFeature.cs   全屏描边 Renderer Feature
  Shaders/
    ScreenOutline.shader
  Settings/
    UniversalRP.asset  Renderer2D.asset  ScreenOutlineFeature.asset
    PlayerPhysicsMaterial.physicsMaterial2D
  Tests/PlayMode/
    Platformer.PlayTests.asmdef
    PlayerControllerPlayTests.cs
Tools/
  generate_placeholders.py   占位图生成脚本
docs/
  DEVELOPMENT.md             本文档
```

---

## 5. 关键设计

### 5.1 单位与网格

- 1 格 = 1 世界单位，PPU = 32。
- 地形模块以 2 格为标准尺寸；1 格仍是构建与对齐的基础单位。
- 所有地形默认使用同一颜色，视觉上靠轮廓区分体块。
- 深灰变体 `Block2x2Dark` 作为第二种颜色：同色合并、异色分离由描边算法自动完成。

### 5.2 跳跃

```
v₀ = √(2 · g · h) + ½ · g · Δt
```

- `g = |Physics2D.gravity.y| × gravityScale = 40`
- `h = jumpHeight = 6`
- 半帧补偿抵消半隐式欧拉积分的顶点损失，实测 5.999 格。
- 空中下落时额外施加 `(fallGravityMultiplier - 1)` 倍重力，手感更干脆；不影响顶点高度。
- 松开跳跃键时保留 45% 上升速度（可变跳高）。

### 5.3 地面检测与斜坡

- 胶囊底部做 `BoxCast`（宽 85%、高 0.06、向下 0.15），命中 Ground 层即接地。
- 接地时记录命中法线：
  - 法线接近竖直 → 平地，垂直速度归零；
  - 法线有水平分量（坡度 < ~66°）→ 水平速度换算为沿坡速度，实现上下坡。
- `_isJumping` 标记避免起跳瞬间被重新判定为接地（否则 6 格跳会被截断）。

### 5.4 相机

- `SmoothDamp` 跟随目标 + 固定偏移（0, 1.0）。
- 根据刚体水平速度计算前瞻偏移（最大 1.5 格），并单独平滑。
- 正交尺寸 5.625（16:9 下约 20×11.25 格可视范围）。

### 5.5 全屏描边

- 注入点：`AfterRenderingTransparents`。
- 流程：`Blit(source → temp, outlineMaterial)` → `Blit(temp → source)`。
- Shader 采样中心与上下左右四个邻域颜色，取最大色差，超过阈值即输出描边色。
- 优点：与形状/尺寸无关，天然合并同色区域；缺点：依赖颜色差，背景或地形颜色过于接近时需要调阈值。

---

### 5.6 地图构建（Tilemap）

- `Level/Grid/Terrain` 为 1×1 网格的 Tilemap；关卡代码用 `PaintCell / PaintRow / PaintColumn` 逐格绘制。
- `TilemapCollider2D`（Used By Composite）+ `CompositeCollider2D`：整张地形合并为一个多边形碰撞体，避免瓦片接缝卡脚，也减少碰撞体数量。
- 刷瓦片后必须依次调用 `RefreshAllTiles()` → `ProcessTilemapChanges()` → `GenerateGeometry()`，否则保存的场景里碰撞体为空（实测会穿地）。
- 45° 斜坡是三角形，瓦片无法表达，保留为独立预制体并共用同一物理材质。

## 6. 测试与验证

PlayMode 测试（`Assets/Tests/PlayMode`），命令行执行：

```
Unity.exe -batchmode -nographics -projectPath <项目> ^
  -runTests -testPlatform PlayMode -testResults Logs/TestResults.xml -logFile Logs/playtests.log
```

| 用例 | 结果 |
|---|---|
| `Player_Stands_On_Ground` | 通过 |
| `Jump_Reaches_Six_Cell_Height` | 通过，实测 **5.999 格** |
| `Horizontal_Movement_Works` | 通过（1 秒内 > 4 格） |
| `Camera_Follows_Player` | 通过，误差 **0.000** |
| `Falls_Below_Limit_Respawn` | 通过 |
| `Walks_Up_45_Degree_Slope` | 通过，峰值 y = **2.76** |
| `Tilemap_Level_Has_Grounded_Player` | 通过，135 格瓦片；玩家 y=0.75 站在合并碰撞体上 |
| `Player_Climbs_Tilemap_Ramp` | 通过，沿 1×1 斜坡瓦片走上 2 格高台 |

场景验证（`Platformer > Validate Level`）：

- 相机正交、尺寸 5.625、跟随 Player
- Player 位置 (-9, 0.85)、`jumpHeight = 6`、层 Player
- 斜坡位于 (11, 1)
- 描边特性存在
- 场景精灵数 34

---

## 7. 已知限制

1. 全屏描边基于颜色差：若地形与背景颜色接近，需下调 `_Threshold`；若地形使用多种颜色，不同颜色交界也会被描边。
2. 描边宽度以像素计（屏幕空间），分辨率变化时相对粗细会变。
3. 斜坡处理假设坡度小于约 66°；更陡的斜面会被当作墙。
4. 关卡由 `ProjectBuilder` 生成；直接在场景里手改后再次执行 Rebuild 会被覆盖。
5. 未包含：动画、音效、UI、敌人、存档、打包流程。

---

## 8. 后续可扩展

- 角色动画（Animator + 状态机）、音效
- 二段跳 / 冲刺 / 墙跳 / 抓边
- 敌人与伤害、血量、检查点
- 场景切换、菜单、暂停
- 用 Tilemap 或关卡编辑工具替代脚本摆放
- 打包 Windows / WebGL / Android

---

## 9. 常用命令

| 目的 | 命令 |
|---|---|
| 重建关卡 | 菜单 `Platformer > Rebuild Level + Prefabs` |
| 重建并出预览图 | 菜单 `Platformer > Rebuild + Render Preview` |
| 批处理重建 | `Unity.exe -batchmode -projectPath <项目> -executeMethod Platformer.EditorTools.ProjectBuilder.BuildAll -quit` |
| 校验场景 | `-executeMethod Platformer.EditorTools.ProjectBuilder.ValidateLevel` |
| 跑测试 | `-runTests -testPlatform PlayMode` |

---

## 10. 变更记录

| 日期 | 内容 |
|---|---|
| 2026-11-11 | 工程初始化：URP 2D 模板、占位精灵、角色控制器、相机、构建脚本 |
| 2026-11-11 | 物理调优：6 格跳高（5.999）、斜坡贴地、土狼时间、跳跃预输入 |
| 2026-11-11 | 合并描边 v1（逐块邻接判断），发现混合尺寸断线问题 |
| 2026-11-11 | 地形改为 2 格模块 + 统一配色；描边升级为全屏边缘检测 |
| 2026-11-11 | 描边细化至 1px；初始化 Git；补充开发文档 |
| 2026-11-11 | 角色缩小到 1.5 格、移速 7→9；新增深灰方块类型（同色合并、异色分离） |
| 2026-11-12 | 所有障碍物统一使用 TerrainPhysicsMaterial，可单独调摩擦/弹性 |
| 2026-11-12 | 地图建造重构为 Tilemap（Grid + 合并碰撞体），新增关卡集成测试 |
| 2026-11-12 | 移除 2 格模块概念，统一 1×1 瓦片；删除未使用的模板资产 |
| 2026-11-12 | 新增 1×1 斜坡瓦片（4 方向 × 普通/深灰），精确三角物理形状；移除 2×2 斜坡预制体 |
| 2026-11-12 | 修复 45° 斜边描边（背景掩码轮廓 + 色差类型边界）；新增 TerrainPalette 调色板 |
| 2026-11-12 | 普通地形颜色改为超浅灰 `#E0E0E0`（深灰变体不变） |
| 2026-11-12 | 每个瓦片增加内描边（贴图烘焙 0.929 亮度边），外轮廓阈值提到 0.28 |
| 2026-11-12 | 新增区域捕获复制工具（CaptureRegion + RegionCapture），关卡预览改为自动取景 |
| 2026-11-12 | 运行时镜像 + 对象池 + PlatformerBehaviour 基类；副本只显示 |
