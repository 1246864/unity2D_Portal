# Commit 报告 — 2026-11-15

## 1. 提交信息

```text
feat(portal): 支持旋转传送门、玩家显示/物理分离与动态地形开洞

- 实时计算传送门开口方向，支持任意角度摆门
- 玩家拆分为物理根节点与 Player Visual 显示节点，旋转只作用于显示
- 出口位置改用出口门法线/切线坐标系，避免旋转门出口过近
- 速度/受力向量按入口/出口门坐标系变换
- 玩家回正支持基础速度、曲线、下坠加速和触地加速
- 传送门激活时对 Tilemap 做非整格布尔开洞，停用/移动时恢复或跟随
- 相机改为玩家局部空间跟随，传送时画面保持相对连续
- PlayerController2D 使用中文 Inspector
- 补齐旋转传送、地形开洞和相机相关 PlayMode 测试
```

建议类型：`feat`  
范围：`portal` / `player` / `camera` / `terrain`

---

## 2. 基线

```text
bf8e267 fix(portal): 同步动态属性并修复开门遮罩/颜色状态
```

---

## 3. 变更文件

### 运行时脚本

| 文件 | 变更 |
|---|---|
| `Assets/Scripts/PlayerController2D.cs` | 显示/物理分离；旋转回正；下坠/触地加速；公开显示角度与回正状态 |
| `Assets/Scripts/PortalPair.cs` | 地形开洞接入；切线出口定位；相对角度旋转；速度坐标系变换 |
| `Assets/Scripts/CaptureDisplayGroup.cs` | 实时 `OpeningDirection` / `BackDirection`；开洞状态通知；方向 Gizmo |
| `Assets/Scripts/CameraFollow2D.cs` | 相机独立，但使用玩家显示节点的局部空间跟随，保证传送映射连续 |
| `Assets/Scripts/PortalTerrainCarver.cs` | 新增：Tilemap 非整格碰撞布尔开洞、恢复、移动跟随 |

### 编辑器

| 文件 | 变更 |
|---|---|
| `Assets/Editor/PlayerController2DEditor.cs` | 新增：中文玩家 Inspector，所有字段中文标签/说明 |

### 测试

| 文件 | 变更 |
|---|---|
| `Assets/Tests/PlayMode/PlayerControllerPlayTests.cs` | 新增出传送门后旋转、相机、回正测试 |
| `Assets/Tests/PlayMode/PortalTeleportTests.cs` | 适配旋转门、切线出口、相对角度、速度方向 |
| `Assets/Tests/PlayMode/PortalTerrainCarverTests.cs` | 新增开洞/恢复/方向实时/移动跟随测试 |

### 场景

| 文件 | 变更 |
|---|---|
| `Assets/Scenes/Level01.unity` | 新地形；门位置/旋转；门洞高度；开洞参数；相机回到根节点 |

---

## 4. 核心行为变化

1. **任意角度传送门**
   - `OpeningDirection` 由门 Transform 的 Y+ 实时计算；
   - 开门、背面判断、开洞、提示光带和速度变换共用同一个方向。

2. **玩家显示与碰撞分离**
   - 物理根节点保留 Rigidbody2D/CapsuleCollider2D/Center Point；
   - 精灵复制到 `Player Visual`；
   - 传送门只旋转显示节点，碰撞胶囊始终竖直。

3. **出口定位**
   - 出口 = 出口门位置 + 出口门法线 × `front` + 出口门切线 × 入口切线偏移；
   - 不再使用世界 Y，旋转门不会把玩家放进碰撞箱。

4. **出口旋转**
   - `exitAngle = 出口门角度 - 入口门角度`；
   - 玩家显示节点先转到该角度，再按玩家配置回正。

5. **速度/受力**
   - 法线分量反向，切线分量保留；
   - 两门同向/相背/任意相对旋转都能正确映射。

6. **回正**
   - 由玩家管理基础速度、曲线；
   - 下坠速度越大，回正越快；
   - 触地时使用更高的倍率；
   - 只旋转显示节点，不影响物理。

7. **相机**
   - 相机独立于玩家；
   - 跟随状态是玩家显示空间里的 `_localOffset`；
   - 传送时不需要额外世界快照，局部偏移保持不变，画面相对玩家连续；
   - 多次传送不会累计相机偏移。

8. **地形开洞**
   - 运行时由 `PortalPair` 自动补 `PortalTerrainCarver`；
   - 门洞矩形与瓦片物理形状做布尔差；
   - 支持半格切割、门移动/旋转跟随、门停用恢复；
   - 不调用 `Tilemap.RefreshAllTiles()`，避免覆盖 per-cell collider。

9. **中文 Inspector**
   - 自定义编辑器显示中文标签/说明；
   - 内部字段名保持不变，兼容序列化与测试。

---

## 5. 验证状态

### 已执行

- Roslyn 编译：
  - `Platformer.Runtime` exit 0
  - `Platformer.PlayTests` exit 0
  - `Platformer.Editor` exit 0

### 测试记录

- 最近一次完整 PlayMode：**23/23 passed**（`Logs/TestResults_refactor3.xml`）；
- 该记录早于最后的相机局部空间跟随、中文 Inspector、回正动态加速调整；
- 当前工作区最后这些改动未重新跑完整 PlayMode。

### 提交前建议

关闭 Unity 编辑器后执行：

```powershell
Unity.exe -batchmode -projectPath <project> `
  -runTests -testPlatform PlayMode `
  -testResults Logs/TestResults_final.xml `
  -logFile Logs/playtests_final.log
```

---

## 6. 风险与注意事项

- 地形开洞目前依赖运行时 Tile/Sprite 克隆；如果后续改成静态烘焙碰撞，需要同步调整。
- `Player Visual` 在运行时创建；若改为 Prefab 内固定子节点，需要保留排序层/材质配置。
- 相机跟随现在完全依赖玩家显示节点局部空间；`CameraFollow2D.target` 必须指向玩家根节点。
- 回正参数是玩家手感参数，建议按关卡节奏在 Inspector 中调整。
- `Assets/InitTestScene639303515354314258.unity` 是测试运行残留，不应提交。
