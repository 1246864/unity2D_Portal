# 开发文档 — 2D 平台跳跃 / 传送门

> 当前 HEAD：`83e95e7`（2026-11-17）
> 最近验证：23/23 PlayMode 通过（`Logs/TestResults_cleanup.xml`，25.93 秒）
> 项目总览见 **[../PROJECT.md](../PROJECT.md)**，快速开始见 **[../README.md](../README.md)**。

---

## 1. 环境与程序集

| 项目 | 值 |
|---|---|
| Unity | 2022.3.61f1c1 |
| 渲染 | URP 14.0.12（2D Renderer） |
| 输入 | 旧版 Input Manager |
| 色彩空间 | Linear |
| 物理 | Physics2D，重力 `(0, -40)` |
| 测试 | Unity Test Framework 1.1.33，PlayMode |

三个程序集：

| asmdef | 内容 |
|---|---|
| `Platformer.Runtime` | `Assets/Scripts` 下的全部运行时脚本 |
| `Platformer.Editor` | `ProjectBuilder.cs`、`PlayerController2DEditor.cs` |
| `Platformer.PlayTests` | `Assets/Tests/PlayMode`，引用 Runtime |

---

## 2. 当前架构

```text
Input
  └─ PlayerController2D（物理根节点 + Player Visual 显示节点）
       ├─ Rigidbody2D / CapsuleCollider2D / Center Point
       └─ Player Visual（只负责旋转和显示）

PortalPair（总控）
  ├─ BoxCollider2D × 2（门内薄碰撞箱，Snapshot 层）
  ├─ PortalTerrainCarver（运行时补，地形开洞）
  ├─ CaptureDisplayGroup A/B（画面、遮罩、静态光带、碰撞转发）
  └─ CameraFollow2D（主相机）

CaptureDisplayGroup
  ├─ RegionSnapshotCamera → RenderTexture → Texture2D → Sprite
  ├─ Portal View（SpriteRenderer，Snapshot 层）
  ├─ SpriteMask 上下两块
  └─ Portal Hint / Portal Hint Particles（场景静态对象）

TilemapOutline
  └─ 读取 Terrain，运行时生成 Outline Tilemap
```

---

## 3. 关键实现

### 3.1 地形

- 1 个 `Grid` + 1 个 `Terrain` Tilemap，全部 1×1 瓦片。
- `TilemapCollider2D`（Used By Composite）+ `CompositeCollider2D` 合并成一个碰撞体。
- 斜坡瓦片使用精确三角物理形状（由导入脚本写入 Sprite 物理轮廓）。
- 物理材质：Terrain / Player 分开；Terrain friction 0.6。

### 3.2 描边

`TilemapOutline` 在 `OnEnable/Start` 运行时生成独立 `Outline` Tilemap：

- 实心格：四邻域为空或异色 → 该侧 1px 内描边；
- 深灰格邻接普通地形：只在深灰侧画交界线；
- 斜坡格：铺对应方向的斜线；
- 空格不画外圈；
- 生成的 Tile/Sprite/Texture 都带 `HideFlags.DontSave`；
- 退出 Play 时销毁，不写入场景。

### 3.3 玩家显示/物理分离

`PlayerController2D.Awake`：

1. 缓存 Rigidbody2D、CapsuleCollider2D、SpriteRenderer；
2. 创建/解析 `Player Visual` 子节点；
3. 把根节点 SpriteRenderer 的 sprite/color/material/sortingLayer/sortingOrder/flip/maskInteraction 复制到显示节点；
4. 禁用根节点渲染；
5. 创建 `Center Point`（CircleCollider2D，0.08）；
6. 设置 Continuous 碰撞检测。

传送只改变 `PlayerVisual.localRotation`，不改变物理根节点和胶囊。

### 3.4 开口方向

```csharp
Vector3 local = Quaternion.Euler(0f, 0f, FacingSign * -90f) * Vector3.up;
Vector3 world = transform.TransformDirection(local);
OpeningDirection = world.normalized;
BackDirection = -OpeningDirection;
```

用途：

- 背面隐藏；
- 地形开洞；
- 出口位置；
- 速度变换；
- Gizmo。

### 3.5 出口位置/速度

```text
front = gateThickness/2 + centerPointRadius + exitMargin

tangentOffset = dot(playerPos - sourcePos, source.up)

target = partnerPos
       + partner.OpeningDirection * front
       + partner.up * tangentOffset
```

```text
normalSpeed  = dot(velocity, source.OpeningDirection)
tangentSpeed = dot(velocity, source.up)

outVelocity  = partner.OpeningDirection * (-normalSpeed)
             + partner.up * tangentSpeed
```

- 不再使用世界 Y 来保留高度；
- 速度先分解到入口门坐标系，再映射到出口门坐标系；
- 速度来源是 `PlayerController2D.VelocityBeforePhysics`。

### 3.6 开门动画

`CaptureDisplayGroup`：

- `PlayOpenAnimation()`：`_openProgress=0`，遮罩立即回到关闭位置；
- `AdvanceOpenAnimation()`：按 `openDuration` 和 `openCurve` 推进到 1；
- `StopOpenAnimation()`：立即结束动画并保持完全打开；
- `PortalPair.OnGateHit` 先停止所有门动画，再让发起门重播。

### 3.7 相机

当前实现（`83e95e7` 之后）：

- 位置：`target.position + rotation(offset + lookAhead)`，SmoothDamp；
- 角度：跟随 `PlayerVisual.eulerAngles.z`，SmoothDampAngle；
- 普通状态 `rotationSmoothTime`，回正期间 `realignRotationSmoothTime`；
- 传送时记录：

```text
cameraRelative = playerVisual.worldToLocalMatrix * camera.localToWorldMatrix
```

- LateUpdate：

```text
world = newPlayerVisual.localToWorldMatrix * cameraRelative
```

- `Decompose2D` 提取位置/Z 旋转/缩放；
- 连续传送不会累计相机偏移；
- 回正第一帧不做硬对齐（`_wasRealigning` 防止重复对齐）。

> 旧版“只在玩家显示空间维护 `_localOffset`”的方案已废弃。

### 3.8 地形开洞

`PortalTerrainCarver` 由 `PortalPair` 在运行时自动补上：

1. 收集所有激活门的矩形：
   - 起点 = 门中心；
   - 方向 = `BackDirection`；
   - 长度 = `carveWidth`；
   - 高度 = `PortalHeight`；
   - 向门面方向扩展 `carveFrontMargin`。
2. 对每个被覆盖的 Tilemap 格子：
   - 读取 Tile Sprite 的物理轮廓；
   - 与所有矩形做布尔差（左侧/右侧/上条/下条四块裁剪）；
   - 如果面积有变化：`Tile.ColliderType = None`；
   - 剩余碎片生成运行时 `PolygonCollider2D` 子物体（Ground 层）。
3. `ProcessTilemapChanges()` → `CompositeCollider2D.GenerateGeometry()` → `Physics2D.SyncTransforms()`。
4. 门停用/移动/场景卸载时：
   - 销毁残余碰撞；
   - 恢复原 ColliderType；
   - 重建碰撞体。

不调用 `Sprite.OverridePhysicsShape`，不调用 `Tilemap.RefreshAllTiles()`。

### 3.9 光带/粒子

- `Portal Hint` / `Portal Hint Particles` 是场景静态对象，运行时不再创建/移动/缩放；
- `CaptureDisplayGroup` 只负责：
  - 赋 Sprite（若为空）；
  - 创建 `PortalHintBeam` 材质并设置参数；
  - 设置粒子颜色、发射率、生命周期、模拟空间、方向；
- 排序层统一 `Portal light band`；
- `hintRenderer` / `hintParticles` 在 Inspector 中必须赋值，否则运行时会警告。

### 3.10 中文 Inspector

`PlayerController2DEditor` 只影响 Inspector 显示：

- 中文标签/说明；
- 分组：水平移动、跳跃、地面检测、重生、传送门碰撞点、显示、传送门旋转、回正动态加速；
- 不改变字段名，兼容序列化和测试。

---

## 4. 生命周期/帧顺序

```text
Awake
  PlayerController2D → EnsureVisualRoot / Center Point
  PortalPair → CacheGroups / EnsureGates / EnsureTerrainCarver / PushPlayer
  CaptureDisplayGroup → ResolvePartner / WireUp / 遮罩 / EnsureHint

Start
  RegionSnapshotCamera → 分配 RT / Sprite / 启用 Portal Camera / Capture
  CaptureDisplayGroup → 记录 Portal View 初始位置 / 排除 Snapshot 层

Update
  PlayerController2D：输入 / 回正
  CaptureDisplayGroup：颜色 / 可见性
  PortalPair：玩家切换 / 冷却 / carve 参数同步

LateUpdate
  RegionSnapshotCamera：Capture
  CaptureDisplayGroup：动画 → 半屏裁剪 → 遮罩 → 光带参数
  PortalPair：门洞高度 / 强制刷新
  CameraFollow2D：位置/角度跟随
  PortalTerrainCarver：门移动/旋转时重新开洞
```

物理回调：

```text
Center Point 撞门
  → CaptureDisplayGroup.OnCollisionEnter/Stay
  → PortalPair.OnGateHit
  → StopAllOpenAnimations
  → source.PlayOpenAnimation
  → Teleport
```

---

## 5. 测试

命令行：

```powershell
Unity.exe -batchmode -projectPath <项目> `
  -runTests -testPlatform PlayMode `
  -testResults Logs\TestResults.xml `
  -logFile Logs\playtests.log
```

当前 23 个用例：

| 文件 | 用例 |
|---|---|
| `PlayerControllerPlayTests` | 7 |
| `LevelTilemapTests` | 5 |
| `PortalTeleportTests` | 8 |
| `PortalTerrainCarverTests` | 3 |

最近结果：23/23，0 failed，25.93 秒。

测试注意事项：

- PlayMode 测试以 additive 方式加载 `Level01`；
- 地形开洞会影响 Ground 碰撞，部分测试会临时关闭 carver；
- 传送/相机测试会临时改 `portalRealignSpeed`、Rigidbody 类型等，测试结束要还原；
- `Level01` 场景当前 A 未旋转、B 旋转 45°，断言使用门的实际 `OpeningDirection`/角度，不假设水平。

---

## 6. 已知问题

1. **ProjectBuilder 落后**
   仍生成旧版场景（5.625 相机、旧门位置、PortalHeight 3、无静态光带/粒子、无 carve 参数、无新排序层）。不要运行 Rebuild。

2. **遮罩裁剪异常**
   运行时截图中 Portal View 曾以整块着色画面叠加，未被 SpriteMask 裁成门洞；测试只校验遮罩节点/排序，不校验像素。

3. **静态光带不跟随运行时尺寸**
   `PortalHeight` 改变不会重设静态光带 scale 或粒子 shape；`hintBackOffset` 只影响 `HintLinePosition` 属性。

4. **`Portal view` 层未使用**
   TagManager 已加入，但 Portal View 仍使用 Portal A/B。

5. **开洞是运行时碰撞**
   残余 PolygonCollider2D 未使用 TerrainPhysicsMaterial，且不是烘焙资产。

6. **每帧捕获开销**
   两个 Portal Camera 每帧 Render + ReadPixels；`captureCamera.enabled=true` 可能让 URP 再自动渲染一次。

7. **Shader 构建剥离风险**
   `PortalHintBeam` 只通过 `Shader.Find` 使用，没有材质资产引用；Player 构建可能丢失。

8. **`carveTerrain` 开关**
   `false → true` 时，如果 carver 已存在且宽度未变，不会自动重新启用。

---

## 7. 维护约定

- 不要运行 `Platformer > Rebuild Level + Prefabs`；
- 不要随意移动场景中的 `Portal Hint` / `Portal Hint Particles`，它们的位置/缩放是最终视觉参数；
- 改门洞高度时同步检查：
  - 静态光带 scale.y = `PortalHeight + hintExtraHeight`；
  - 粒子 shape；
  - 测试期望；
- 改传送/开洞/相机后运行完整 PlayMode；
- 改排序层时同时检查：
  - Portal View 的排序层；
  - SpriteMask 的 front/back 排序层；
  - 静态光带的 `Portal light band` 层；
- 新增场景/资源时保留 `.meta`，避免 GUID 漂移。
