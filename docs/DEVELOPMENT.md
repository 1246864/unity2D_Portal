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

---

## 4. 工程结构

```
Assets/
  Art/Placeholders/          占位精灵（纯白，运行时上色）
    tile1x1.png   slope2x2.png   player_capsule.png
  Editor/
    Platformer.Editor.asmdef
    ProjectBuilder.cs        一键重建：贴图设置/预制体/场景/BuildSettings/描边特性
  Materials/
    ScreenOutline.mat        描边材质（颜色/宽度/阈值）
  Prefabs/
    Player.prefab  Slope2x2.prefab
  Tiles/
    TerrainTile.asset  DarkTerrainTile.asset
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
| `Tilemap_Level_Has_Grounded_Player` | 通过，132 格瓦片；玩家 y=0.75 站在合并碰撞体上 |

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
