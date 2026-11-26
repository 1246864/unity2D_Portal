# PlayerController2D 架构结构与调用关系

## 1. 架构目标与阅读范围

本文按当前源码说明玩家控制的结构、固定物理步顺序、双动作系统契约、地墙交接、高速 X 动量缓存、墙滑淡出及传送门调用关系。参数含义与完整模块职责见 [player-controller-modules.md](./player-controller-modules.md)。

核对文件：

- `Assets/Scripts/PlayerController2D.cs`
- `Assets/Scripts/PortalPair.cs`
- `Assets/Editor/PlayerController2DEditor.cs`
- `Assets/Prefabs/Player.prefab`
- `Assets/Tests/PlayMode/PlayerControllerPlayTests.cs`
- `Assets/Tests/PlayMode/PortalTeleportTests.cs`

本文基于重构后的源码进行静态核对；相关平底碰撞体改动已执行 Unity PlayMode 专项验证。

## 2. 总体结构

```text
Unity / PlayerControlManager / 自动化测试
                  |
                  | 输入、跳跃、生命周期或测试 API
                  v
+-------------------------------------------------------+
| PlayerController2D（外层 MonoBehaviour，唯一协调者）    |
|                                                       |
| 配置 / 输入 / 缓冲与方向锁 / Rigidbody2D / PolygonCollider2D |
| 地墙 BoxCast / 接触计时 / 高速 X 缓存 / 墙滑淡出          |
| 跳跃仲裁 / SwitchActionSystem / 传送与重生 / 显示回正     |
|                                                       |
|                ActionSystem（私有抽象基类）              |
|                   Enter / Exit                         |
|                   OnTeleport / Tick(dt)                |
|                      /             \                   |
|                     /               \                  |
|       PlayerActionSystem       PhysicsActionSystem      |
|       地面、斜坡、普通墙滑       空中、跳跃、缓存、淡出     |
|                     \               /                  |
|                      \             /                   |
|                同一时刻仅一个 _activeActions             |
+-------------------------------------------------------+
                    ^                         ^
                    |                         |
       PortalPair -> 门位置/旋转/速度 API       共享状态查询
       Editor ----> 只绘制序列化字段，不参与运行逻辑
```

### 同文件双系统契约

`PlayerActionSystem` 和 `PhysicsActionSystem` 是 `PlayerController2D.cs` 中的私有嵌套实现，不是可单独挂到 GameObject 上的组件。二者通过基类的 `Owner` 和 `Body` 访问外层状态与刚体。

契约如下：

1. 外层统一拥有配置、输入、接触、缓存、计时与交接；动作系统不自行探测地墙或决定系统切换。
2. `SwitchActionSystem(next)` 固定执行 `旧 Exit()` → 替换 `_activeActions` → `新 Enter()`。
3. 常规 FixedUpdate 中只执行一次 `_activeActions.Tick(dt)`，两个系统不能并行写运动。
4. 起跳由外层仲裁；切到 Physics 后可额外调用 `StartJump` 设置初速度，随后仍只执行当前 Physics 的一次 Tick。
5. `Enter/Exit/OnTeleport` 整理内部状态，不负责另一套运动求解。Player 的这些入口重置墙滑；Physics 的 `Enter/ResetMotion` 重置空中与跳跃状态，`OnTeleport` 只取消墙监测暂停。
6. 传送、重生等外层公开入口可在常规 Tick 之外写位置/速度，但它们不是第三条并行运动系统。

## 3. 状态与数据所有权

| 数据/职责 | 唯一所有者 | 使用方 |
|---|---|---|
| 刚体与身体碰撞体引用 | 外层 `_rb`、`_col` | 两系统通过 `Body` 访问刚体；外层用身体碰撞体 Bounds 探测 |
| 原始/程序输入 | 外层 `_moveInput`、`_manualInput`、`_jumpHeld` | 外层跳跃仲裁；两系统读取 `CurrentHorizontalInput` |
| 跳跃缓冲、土狼时间、方向锁 | 外层 | 跳跃仲裁、接触与输入计算 |
| 地面、墙方向、地法线 | 外层 `ProbeContacts` | 跳跃、交接、Player 斜坡/墙滑、Physics 缓存 |
| 高速落地锁存、撞墙 X 缓存、待恢复 X | 外层 | `ResolveActionSystem` 与 Physics Tick |
| 普通墙滑状态与 cap 计时 | PlayerActionSystem | 外层查询并在淡出启动前捕获 cap |
| 松键淡出剩余时间、冻结 cap、墙方向 | 外层 | Physics `ApplyWallSlideFade` |
| 跳跃相位、空中时间、可变推进方向 | PhysicsActionSystem | 外层探测、门传送、公开状态查询 |
| 当前动作系统 | 外层 `_activeActions` | 所有运动写入仲裁 |
| 门碰撞、扫掠、速度坐标映射 | PortalPair | 调用玩家门传送 API，不直接选择动作系统 |

这套所有权避免把接触状态、空中权限或动量缓存复制到两个系统各自维护。

## 4. Unity 生命周期入口

### 4.1 Awake

```text
Awake
├─ 获取 Rigidbody2D / PolygonCollider2D，记录出生点
├─ new PlayerActionSystem(this)
├─ new PhysicsActionSystem(this)
├─ _activeActions = Player；Player.Enter()
├─ EnsureVisualRoot()，建立显示子节点与 SpriteRenderer
├─ 刚体设为 Continuous
└─ CreateCenterPoint()，建立中心圆形碰撞点
```

初始系统是 Player。显示节点和 Center Point 都是运行时对象，不是 Prefab 中预先存在的子节点。

### 4.2 Update

`Update` 与固定运动解算分离：

1. `AdvancePortalRealign()` 推进显示节点回正；只改 `visualRoot.localRotation`。
2. 有水平输入时更新显示精灵 `flipX`。

回正时长由起始角度和 `portalRealignSpeed` 决定，曲线为 `portalRealignCurve`；下落速度和接地状态通过 `GetRealignSpeedMultiplier()` 加快显示回正，不改物理速度。

### 4.3 FixedUpdate 的精确顺序

当前 `PlayerController2D.FixedUpdate()` 顺序不可颠倒：

```text
FixedUpdate
│
├─ 1. TickTimers(dt)
│    ├─ 递减土狼时间、跳跃缓冲
│    └─ 递减墙跳方向锁；到期清 _forcedMoveDirection
│
├─ 2. ProbeContacts()
│    ├─ Physics 激活时先 RefreshJumpPhase()
│    ├─ contactMask = groundMask & ~portalContactMask
│    ├─ 地面 BoxCast -> IsGrounded / coyote / _groundNormal
│    └─ 若未暂停墙监测：右侧再左侧 BoxCast -> _wallDirection
│
├─ 3. TickContactTimers(dt)
│    ├─ 清上一物理步的待恢复标记
│    ├─ 接地：锁存高速落地、累计地面时间、清墙缓存与淡出
│    └─ 离地：清地面状态，推进撞墙缓存和松键淡出
│
├─ 4. 跳跃优先仲裁
│    ├─ TryConsumeBufferedJump() 成功：起跳，跳过本步普通交接
│    └─ 否则 ResolveActionSystem()
│
├─ 5. _activeActions.Tick(dt)       // 全步仅一次
│    ├─ Player：地面/墙面前先做 0.2 低台阶及净空查询
│    └─ Physics：末尾预测上升倒角并选择最小安全横向纠偏
│
├─ 6. RespawnIfFallen()
│
└─ 7. VelocityBeforePhysics = _rb.velocity
```

关键约束：

- **跳跃优先于接触交接**，避免本步先切系统再处理起跳。
- 地面交接优先于墙缓存；地面状态会清掉墙缓存和淡出。
- `VelocityBeforePhysics` 在 Tick 和重生检查后记录，供随后的碰撞/扫掠传送以及下一固定步的撞墙前 X 锁存使用。
- 高速落地保留期间，Physics Tick 的接地分支按 `physicsHorizontalDeceleration` 只衰减 X，保留 Y，不施力并直接返回。

## 5. 接触探测与跳跃墙监测

### 5.1 地墙探测调用关系

```text
ProbeContacts
├─ Physics.RefreshJumpPhase()        // 仅 Physics 激活
├─ Physics2D.BoxCast(ground probe)
├─ ProbeWall(bounds, right, mask)
│  └─ 未命中才 ProbeWall(bounds, left, mask)
└─ 输出 IsGrounded / _groundNormal / _wallDirection
```

- 地面与墙面使用同一 `contactMask`；`portalContactMask` 默认排除第 8 层 Snapshot 门碰撞盒。
- 地面探针使用身体碰撞体的完整宽度，只接受 `normal.y > 0.3` 的向上表面；少量脚底边缘仍有支撑时保持接地，侧墙不会算作地面。
- 墙面探测使用身体碰撞体 Bounds 的 70% 宽高和 `wallCheckDistance`；法线明确时要求 `abs(normal.x) > 0.5`。
- 只有真实地形墙能参与交接、墙滑和高速 X 缓存；门碰撞本身不能触发这些逻辑。
- Physics 正在跳跃时，地面命中不会直接视为有效接地，避免起跳后仍被旧地面接触拉住。

### 5.2 跳跃上升暂停墙监测

```text
Physics.StartJump
└─ _isJumping = true
   _wallMonitorSuppressionActive = true
   _wallMonitorSuppressRemaining = EstimateJumpApexTime()

下一固定步 ProbeContacts
├─ RefreshJumpPhase()
│  ├─ velocity.y <= 0：到顶点，结束跳跃与暂停
│  └─ 仍上升且估算计时耗尽：对仍武装的暂停续算
└─ IsWallMonitoringSuppressed == true 时不做左右墙探测
```

实际顶点由 Y 速度决定，估算计时不是提前恢复墙监测的依据。暂停只屏蔽墙面探测，不屏蔽地面 BoxCast。

### 5.3 传送立即取消暂停

`TeleportCore()` 调用 `_activeActions.OnTeleport()`。若当前是 Physics，其实现清除 `_wallMonitorSuppressionActive` 和剩余计时，但不重置 `_airborneTime`、跳跃推进时间或当前动作系统。

因此门传送后无需等待旧跳跃到达顶点，下一次 `ProbeContacts` 就能重新探测墙；后续 `RefreshJumpPhase` 也不会重新武装已取消的旧暂停。

### 5.4 平底圆头与安全接触纠正

根节点只有一个 `PolygonCollider2D`：底边和两侧竖直，头顶为 16 段半圆弧。身体宽 `0.72`、高 `1.5`，圆头半径 `0.36`、圆心局部 Y 为 `0.39`，底边局部 Y 为 `-0.75`。Center Point 仍是独立运行时子节点，不是第二个根身体碰撞体。

候选空间使用同一几何常量，通过“下身 OverlapBox + 头顶 OverlapCircle”检查，所有查询排除 `portalContactMask` 且不命中 Trigger。查询不创建额外物理碰撞体，也不读取 `visualRoot` 的角度；物理根节点始终保持世界轴对齐。

两类位置修正都由当前动作系统在自己的 Tick 内独占执行：

```text
PlayerActionSystem.Tick
└─ 接地、近平地且有水平速度
   └─ 侧面障碍 -> 局部顶面 -> 头顶路径 -> 目标完整身体
      └─ 全部安全：爬升不超过 maxStepUpHeight=0.2

PhysicsActionSystem.Tick
└─ 最终 velocity.y > 0
   └─ 圆头向上 CircleCast（覆盖本步预计位移）
      └─ 仅斜向倒角：按 0.02 搜索最小安全 X 偏移
         ├─ 检查当前及本步上升后的完整身体空间
         ├─ 排除其他阻挡，并确认已绕开原向下表面
         └─ 最大 headCorrectionMaxDistance=0.24，保持本步 Y 速度
```

平天花板、竖墙、初始重叠零法线或任何目标空间不足都不纠偏。台阶落点额外留出 `0.03`，大于两侧默认接触偏移之和，防止 Overlap 把合法顶面接触误判为穿透。小幅位置修正不调用 `TeleportCore`、不增加 `PositionRevision`，也不创建第三条运动系统。

## 6. 动作系统交接状态机

### 6.1 Player 激活时

| 条件（按优先级） | 结果 |
|---|---|
| 本步成功普通跳/墙跳 | 外层切 Physics，`StartJump` 设置速度 |
| `IsGrounded` | 保持 Player |
| 存在新建立的高速撞墙 X 缓存 | 清淡出并切 Physics |
| 空中且 `IsPushingWall(_wallDirection)` | 保持 Player，执行玩家墙滑/横向控制 |
| 空中且没有有效朝墙输入（松键、反向或无墙） | 必要时先捕获淡出，再切 Physics |

Player 不再因为“墙滑 Y 又超过某阈值”而自动跳回 Physics，也没有低速碰墙自动交接条件。

### 6.2 Physics 激活时

| 条件（按优先级） | 结果 |
|---|---|
| 本步成功跳跃请求 | 保持/进入 Physics 并重新开始跳跃 |
| 接地且高速落地 retention 尚未结束 | 保持 Physics；本步 Tick 只对 X 应用水平减速度，不改 Y |
| 接地且无需保留，或 retention 已结束 | 切 Player |
| 离地且 `_hasWallMomentumCache` | 保持 Physics；缓存优先于推墙交接 |
| 离地、无缓存且朝墙输入 | 清淡出并切 Player；上升和下降都使用同一方向判断 |
| 其他空中状态 | 保持 Physics |

这里已没有“持续推墙满一定时间才交接”，也没有“下降到 `wallSlideStartSpeed` 才交接”。上升跳跃期间的墙监测暂停会使 `_wallDirection=0`，从输入端避免普通交接打断跳跃；暂停取消后则按同一规则处理。

### 6.3 不经过普通状态机的入口

- `Respawn()` 和公开 `Teleport(Vector2)`：显式 `ResetToPlayerActions()`。
- `PortalPair` 门传送：不调用 `ResetToPlayerActions()`，保留当前动作系统和 Physics 空中权限。
- `SetPortalExitVelocity()`：只写速度，不触发任何状态迁移。

## 7. 高速接触与动量缓存流程

共用参数：`highSpeedContactRetentionTime = 0.1s`，高速阈值：`inputSpeedLimit = 24`。

### 7.1 高速落地序列

```text
ProbeContacts 得到新 IsGrounded
        |
TickContactTimers：此前为 Physics 且当前 |rb.velocity.x| > 24
        |
_delayLandingHandoff = true，累计 _groundContactTime
        |
ResolveActionSystem
├─ _groundContactTime < 0.1：保持 Physics
│  └─ Physics.Tick 按 physicsHorizontalDeceleration 衰减 X，保留 Y 后返回
└─ 到期或锁存为 false：SwitchActionSystem(Player)
   └─ 恢复 groundAcceleration / groundDeceleration 等直接控制
```

落地判断使用新地面接触时的**当前世界 X**；撞墙缓存则使用**物理步开始前记录的世界 X**，二者不要互换。Y 均不参与高速判定。

### 7.2 新高速撞墙序列

```text
上一 FixedUpdate 末尾：VelocityBeforePhysics = rb.velocity
                 |
Unity 碰撞解算可能把当前 X 清零
                 |
本步 ProbeContacts 发现新真实地形墙
                 |
TickWallMomentumCache
├─ approachX = VelocityBeforePhysics.x
├─ 要求 retention > 0
└─ 要求 approachX * wallDirection > 24
                 |
锁存 _wallMomentumCacheX / Direction / Time = 0
                 |
ResolveActionSystem
├─ 原系统为 Player：先切 Physics
└─ 原系统为 Physics：缓存阻止推墙交接
                 |
Physics.Tick
├─ 缓存期跳过普通空中 X 输入，并在前后把 X 固定为 0
├─ 缓存不套用普通墙滑 Y cap；下降用原生重力，上升仍走跳跃修正
└─ 推墙且 Y < -wallSlideStartSpeed：
   AddForce(up * highSpeedWallBrakeAcceleration * mass)
```

`highSpeedWallBrakeAcceleration` 默认 380；`wallSlideStartSpeed` 默认 4 只是制动启动条件。缓存期不推墙时，Y 不使用普通墙滑公式，也不施加这项制动力。

### 7.3 缓存退出序列

| 事件 | 外层处理 | Physics/交接结果 |
|---|---|---|
| 仍同墙，计时未到 0.1s | 继续缓存 | X 保持 0；按输入决定是否制动 Y |
| 同墙到期，仍推墙 | 清缓存 | 同一步可切 Player，立即执行普通玩家墙滑 |
| 同墙到期，不推墙 | 清缓存 | 保持 Physics，不做 X 恢复 |
| 真正离墙 | 锁存 X 转为一次性 pending，再清缓存 | 当前 Physics Tick 单次赋值恢复 X，保留当前 Y |
| 换到另一方向墙 | 清缓存 | 不恢复旧 X |
| 落地 | 清缓存及 pending/淡出 | 走地面交接规则 |
| 起跳 | `CompleteJumpRequest` 清缓存 | 使用新跳跃 X/Y，不继承缓存 |
| 门传送/生命周期瞬移 | `TeleportCore` 清缓存与 pending | 不跨位置保留旧接触动量缓存 |

离墙恢复是赋值 `_rb.velocity = new Vector2(restoredX, currentY)`，不是把缓存 X 叠加到当前 X；恢复标记只存在一个物理步。

## 8. 运动计算的调用关系

### 8.1 X 计算

```text
PlayerActionSystem.Tick
└─ CalculatePlayerHorizontalSpeed(currentX, input, dt)
   ├─ MoveTowards(currentX, input * moveSpeed,
   │              (groundAcceleration 或 groundDeceleration) * dt)
   └─ LimitHorizontalInputAcceleration(currentX, candidateX)

PhysicsActionSystem.Tick（无缓存、无离墙恢复时）
├─ CalculateAirHorizontalSpeed(currentX, input, AirControlAuthority, dt)
│  ├─ 松键/权限 0/同向超速：输入求解保留 currentX
│  ├─ MoveTowards(..., airAcceleration * authority * dt)
│  └─ LimitHorizontalInputAcceleration(currentX, candidateX)
└─ ApplyPhysicsHorizontalDeceleration(nextX, dt)
   └─ MoveTowards(nextX, 0, physicsHorizontalDeceleration * dt)
```

这两个输入求解函数都是**横向纯计算**：输入、候选值、比较和返回值都只有 X，不读取或限制 Y。正常空中分支随后再独立衰减 X；撞墙缓存和离墙恢复保持原路径。`inputSpeedLimit=24` 只拒绝 AD 在 24 以上继续增大 |X|，不是综合向量限速，也不硬截断已有超速。

默认参数下：跑速目标 9，普通跳在当前 X 上代数加助推 4，墙跳固定使用 `离墙方向 * (9 + 4)`；这些离散助推不经过普通 AD 增速限制。

### 8.2 普通墙滑与松键淡出

```text
PlayerActionSystem.UpdateWallSlide
└─ CalculateWallSlideVerticalVelocity(currentY, CurrentWallSlideCap, dt)

Player 退出有效墙滑前
└─ StartWallSlideFadeIfNeeded()
   ├─ 仅同墙 + 中性松键 + 下降 + Player.IsWallSliding
   └─ 保存 CurrentWallSlideCap，设置 remaining = 0.3
        |
        v
PhysicsActionSystem.ApplyWallSlideFade
├─ CalculateWallSlideVerticalVelocity(currentY, savedCap, dt)
└─ Lerp(currentY, brakedY, remaining / 0.3)
```

共享 Y 计算为：

```text
gravityStep = EffectiveGravity * dt
desiredY = MoveTowards(currentY, -cap, gravityStep)
结果 = desiredY + gravityStep
```

这样 Player 墙滑和 Physics 淡出使用同一份公式。淡出只混和 Y；X 由 Physics 当前规则处理，不做额外淡出。落地、离墙、换墙、方向输入或上升都会使淡出失效。

## 9. 跳跃请求调用链

```text
JumpPressed / SubmitDeviceInput(jumpDown)
└─ 外层设置 _jumpBufferCounter / _jumpHeld
        |
FixedUpdate：TryConsumeBufferedJump
├─ 普通跳或土狼跳
│  ├─ launchX = 当前 X + jumpHorizontalBoost * 当前输入
│  ├─ SwitchActionSystem(Physics)
│  ├─ Physics.StartJump(launchX, jumpHeight=6.2)
│  └─ CompleteJumpRequest()
│
└─ 可下降墙跳；明确离墙输入时优先于普通/土狼跳
   ├─ direction = -wallDirection
   ├─ SwitchActionSystem(Physics)
   ├─ Physics.StartJump(direction * (moveSpeed + 4), wallJumpHeight=4.8)
   ├─ CompleteJumpRequest()
   └─ 设置 wallJumpForceTime 方向锁

StartJump 内部
├─ CalculateJumpLaunchSpeed(height)
├─ 直接覆盖 Body.velocity 的 X/Y
├─ 设置可变推进时间与 Vector2.up 方向
├─ 设置跳跃相位与墙监测暂停
└─ 重置本段空中时间和跳跃权限标记

CompleteJumpRequest 内部
└─ 清缓冲、土狼、旧地墙接触、缓存、淡出和 Player 墙滑状态

随后：当前 Physics.Tick（仍只调用一次）
```

有墙但正在上升时按跳会清掉本次缓冲，不生成延迟墙跳。松开跳跃由 Physics `ConsumeJumpRelease()` 结束长按推进，不立即改写当前速度。

## 10. PortalPair 调用关系

### 10.1 命中入口

`PortalPair` 使用 `[DefaultExecutionOrder(-100)]`，其 FixedUpdate 先于默认顺序的玩家 FixedUpdate 执行门运动采样和扫掠。传送有两条真实入口：

```text
入口 A：CaptureDisplayGroup 碰撞转发
       -> PortalPair.OnGateHit(source, collision)
       -> 只接受 collision.collider == controller.CenterPoint
       -> Teleport(source, partner)

入口 B：PortalPair.FixedUpdate
       -> SampleMotion / SyncGateBody
       -> TrySweepingPortals(previousPlayer, currentPlayer)
       -> 选择最早命中门
       -> TeleportAt(..., controller.VelocityBeforePhysics)
```

`PositionRevision` 在显式位置跳变时递增。`PortalPair` 观察到版本变化后只重建基线，不把瞬移前后位置连成扫掠走廊。

### 10.2 TeleportAt 顺序

```text
PortalPair.TeleportAt
│
├─ 1. 按入口/出口门的 Right、Tangent 映射玩家位置
│
├─ 2. 沿出口 Opening 应用最小安全间距
│    safeExitOffset = -gateContactOffset + gateThickness/2
│                    + centerPointRadius + exitMargin
│
├─ 3. 计算出口显示角、相机相对位姿
│
├─ 4. incoming = controller.VelocityBeforePhysics
│    relative = incoming - sourceState.PointVelocity(入口接触点)
│    mapped = TransformPortalVector(relative, sourceState, partnerState)
│    // 法线反向、切线保留
│    exitVelocity = mapped + partnerState.PointVelocity(出口目标点)
│
├─ 5. 以同一坐标规则变换 VariableJumpDirection
│
├─ 6. controller.TeleportWithPortalRotation(
│         target, exitAngle, mappedJumpDirection, preserveVelocity)
│    ├─ 先读取保留时的 RemainingVariableJumpTime
│    ├─ TeleportCore
│    │  ├─ PositionRevision++
│    │  ├─ 速度及 VelocityBeforePhysics 清零
│    │  ├─ 写 Rigidbody2D.position / transform.position
│    │  ├─ 清旧接触、方向锁、缓存、淡出、墙滑
│    │  ├─ 当前动作系统 OnTeleport() // Physics 取消墙监测暂停
│    │  └─ Physics2D.SyncTransforms()
│    ├─ RestoreJumpDrive(remaining, mappedDirection)
│    └─ 设置 visualRoot 初始出口角，启动显示回正
│
├─ 7. 仅 preserveVelocity=true 时
│    controller.SetPortalExitVelocity(exitVelocity)
│    ├─ _rb.velocity = exitVelocity
│    └─ VelocityBeforePhysics = exitVelocity
│
├─ 8. 总控传送相机；设置共享冷却和玩家扫掠基线
├─ 9. 标记当帧刷新双门捕获，同向门执行一次性精灵/投影协同
└─ 10. 立即重算双门透明度
```

**门传送不选择动作系统、不调用 Enter、不重置空中权限。** `SetPortalExitVelocity` 也不能扩展成隐式交接入口，否则会破坏已经耗尽的空中权限和出口动量连续性。

身体碰撞体由 `PortalPair.IgnorePlayerBodyCollision()` 排除第 8 层；门碰撞只由 Center Point 命中。玩家外层同时又用 `portalContactMask` 排除门层，形成“物理碰撞过滤 + 控制接触过滤”两道边界。

## 11. 接口分层索引

### 11.1 对 gameplay / 总控 / 测试公开

| 类别 | 真实接口 |
|---|---|
| 输入 | `SetMoveInput(float)`、`ClearManualInput()`、`SetHorizontalInputInverted(bool)`、`JumpPressed()`、`JumpReleased()` |
| 生命周期 | `Respawn()`、`Teleport(Vector2)`、`SetSpawnPoint(Vector3)` |
| 门传送 | `TeleportWithPortalRotation(Vector2, float)`、`TeleportWithPortalRotation(Vector2, float, Vector2, bool)`、`SetPortalExitVelocity(Vector2)` |
| 显示协同 | `MirrorSpriteHorizontally()` |
| 状态 | `IsGrounded`、`IsWallSliding`、`HasWallContact`、`IsPhysicsActionActive`、`Velocity`、`VelocityBeforePhysics`、`MoveInput`、`IsManualInput`、`IsHorizontalInputInverted`、`AirControlAuthority`、`CenterPoint`、`VisualRoot`、`CurrentRotationAngle`、`IsRealigning`、`VariableJumpDirection` |

同程序集内部入口为 `SubmitDeviceInput(float, bool, bool)`、`ClearDeviceInput()` 和 `PositionRevision`；它们不是新增的外部动作系统接口。

### 11.2 仅同文件内部使用的动作系统契约

```text
ActionSystem
├─ virtual Enter()
├─ virtual Exit()
├─ virtual OnTeleport()
└─ abstract Tick(float dt)

PlayerActionSystem
├─ ResetWallSlide()
├─ CurrentWallSlideCap
└─ IsWallSliding / IsContinuousWallSlide / WallSlideDirection

PhysicsActionSystem
├─ ResetMotion()
├─ RefreshJumpPhase()
├─ StartJump(float horizontalVelocity, float height)
├─ RestoreJumpDrive(float remainingTime, Vector2 direction)
├─ RemainingVariableJumpTime / VariableJumpDirection
└─ IsJumping / IsWallMonitoringSuppressed / AirControlAuthority
```

这些类型嵌套且私有，不应在文档或调用方中当作可独立注入的公共服务。

## 12. Editor、Prefab 与运行时的关系

- `PlayerController2DEditor.OnInspectorGUI()` 只读写序列化属性；`Field` 用中文 `GUIContent(label, tooltip)` 绘制，并优先采用字段自身的 `[Tooltip]`，不提供另一套运行逻辑。
- 新增字段若要在 Inspector 出现，需要真实序列化字段及对应 `Field` 调用；参数说明以字段上的中文 `[Tooltip]` 为主，自定义调用处文案只作缺失时的备用。
- `Player.prefab` 固化当前主要参数：普通跳 6.2、墙跳 4.8、跑速 9、助推 4、`inputSpeedLimit=24`、`maxFallSpeed=100`、触地回正倍率 1、高速 retention 0.1、同墙松键 fade 0.3、低台阶上限 0.2、头顶纠偏上限 0.24。
- Prefab 的 `groundMask=64`（层 6）、`portalContactMask=256`（层 8），刚体 `gravityScale=4`；单个平底圆头 `PolygonCollider2D` 的 Bounds 为 `0.72 × 1.5`，底边无圆角或中间点。
- `visualRoot` 默认为空并由 Awake 创建；Center Point 同样运行时创建。`centerPointRadius=0.08` 同时进入出口安全距离计算。

## 13. 维护不变量与测试说明

修改架构时至少守住以下不变量：

1. FixedUpdate 顺序不变，尤其“探测/缓存 → 跳跃优先 → 交接 → 单次 Tick → 记录 VelocityBeforePhysics”。
2. 每个物理步不能有两个动作系统同时写运动；不要把旧并列运动函数重新引入。
3. 横向输入求解只处理 X；禁止恢复综合向量限速或 `moveSpeed * 1.1` 阈值；最大下落速度仅由 `maxFallSpeed` 控制。
4. 高速撞墙从 `VelocityBeforePhysics.x` 锁存；高速落地看新接地时当前 X；二者都严格使用 `inputSpeedLimit`，不混入 Y。
5. 不恢复持续推墙计时和低速自动交接；`wallSlideStartSpeed` 不再是系统切换阈值。
6. 高速缓存优先于推墙交接；离墙恢复只能一次赋值，换墙/到期/落地/跳跃/传送按当前规则清理。
7. 普通墙滑与松键淡出继续共享同一个 Y 公式；淡出只限同墙中性松键且不插值 X。
8. 跳跃上升暂停墙监测，Physics `OnTeleport` 必须立即取消暂停；门传送仍不得重置空中权限。
9. PortalPair 先 `TeleportWithPortalRotation`，后按 `preserveVelocity` 调 `SetPortalExitVelocity`；出口安全间距公式中的四项不能遗漏。
10. 保持 `portalContactMask` 和 Center Point/身体碰撞体层过滤，避免门被当成地墙或出口重复碰撞消掉速度。
11. 低台阶和头顶纠偏只能留在各自动作系统 Tick 内，并先检查头顶路径与完整目标身体；不得改成无空间检测的位置吸附或传送。
12. 根节点只保留一个身体 Polygon；传送门只旋转 `visualRoot`，不能旋转根节点、刚体或碰撞路径。

### 测试状态

本次已在临时 Unity 工程执行相关 PlayMode 测试：新增的 12 项低台阶、倒角、净空拒绝和旋转契约用例全部通过；`PlayerControllerPlayTests` 为 49 项中 46 项通过，`PortalTeleportTests` 22/22，`PortalTerrainCarverTests` 7 项中 6 项通过，`LevelTilemapTests` 5/5。

剩余 4 项为 2 项相机镜像、1 项高速推墙和 1 项门顶支撑条补墙，均属于此前已确认、在旧碰撞体对照中也失败的问题，不作为本次功能通过依据。回填测试中的跨门位置摆放已改用 `Teleport` 更新位置版本，避免把测试摆位误当成真实扫掠穿门。

`PlayerControllerPlayTests.High_Speed_Wall_Push_Brakes_Then_Hands_Off_To_Player` 已语义过时：它以 X=0、Y=-40 构造场景，仍期待“Physics 纵向高速推墙制动 → 降到 wallSlideStartSpeed 附近自动交 Player”。当前高速撞墙链路要求撞墙前世界 X 朝墙且超过 24，缓存只保留 0.1s，并已删除低速自动交接，所以该旧断言序列不再对应当前架构。后续更新测试时应验证当前 X 缓存与 retention 契约，而不是恢复旧实现。
