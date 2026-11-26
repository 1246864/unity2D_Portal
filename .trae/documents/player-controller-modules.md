# PlayerController2D 模块说明

## 1. 文档范围

本文描述当前玩家控制、双动作系统、地墙接触、高速动量、墙滑、传送门衔接、编辑器显示和 Player Prefab 配置。对应的架构与逐物理步调用顺序见 [player-controller-architecture.md](./player-controller-architecture.md)。

核对来源：

- `Assets/Scripts/PlayerController2D.cs`
- `Assets/Scripts/PortalPair.cs`
- `Assets/Editor/PlayerController2DEditor.cs`
- `Assets/Prefabs/Player.prefab`
- `Assets/Tests/PlayMode/PlayerControllerPlayTests.cs`
- `Assets/Tests/PlayMode/PortalTeleportTests.cs`

本文是源码静态说明；相关平底碰撞体改动已执行 Unity PlayMode 专项验证，具体通过范围以实际测试结果为准。

## 2. 模块总览

| 模块 | 位置 | 核心职责 |
|---|---|---|
| `PlayerController2D` 外层协调器 | `Assets/Scripts/PlayerController2D.cs` | 配置与输入、共享计时、地墙探测、身体空间查询、跳跃仲裁、动作系统交接、高速接触缓存、传送/重生、显示旋转和公开状态 |
| `PlayerActionSystem` | 同文件内的私有嵌套类 | 地面直接移动、低台阶安全爬升、斜坡速度、玩家墙面横向控制和普通墙滑 |
| `PhysicsActionSystem` | 同文件内的私有嵌套类 | 空中惯性与可配置 X 衰减、跳跃弧线、上升倒角安全纠偏、空中权限、高速撞墙 X 缓存期运动及墙滑松键 Y 淡出 |
| `PortalPair` | `Assets/Scripts/PortalPair.cs` | 门碰撞/扫掠、出口位置和速度映射、出口安全间距、相机协同、冷却与画面刷新 |
| `PlayerController2DEditor` | `Assets/Editor/PlayerController2DEditor.cs` | 用中文字段名和中文 Tooltip 展示序列化参数 |
| `Player.prefab` | `Assets/Prefabs/Player.prefab` | 提供刚体、身体碰撞体、精灵及控制器默认序列化值 |

**双系统不是两个独立组件，也不是两个脚本文件。** 它们在 `PlayerController2D.cs` 内继承同一个私有 `ActionSystem` 基类，通过外层 `Owner` 读取配置和共享状态。正常运行时只有一个 `_activeActions`；每个物理步只调用一次当前系统的 `Tick(dt)`，不再并列执行旧的横向、纵向运动入口。

## 3. 重构前后的行为边界

| 项目 | 旧逻辑 | 当前逻辑 |
|---|---|---|
| 运动组织 | 玩家直接控制与物理运动混杂，容易在同一流程重复改写速度 | 同文件 `PlayerActionSystem` / `PhysicsActionSystem` 双系统，外层仲裁后独占当前物理步 |
| 输入限速 | 以完整速度向量做综合限速，Y 可能参与输入限制 | `LimitHorizontalInputAcceleration` 只比较候选 **X**；Y、重力、跳跃和门速度不进入该限制 |
| 高速阈值 | 使用 `moveSpeed * 1.1` 等派生阈值 | 统一使用显式 `inputSpeedLimit = 24`；高速落地与新高速撞墙均看世界 X |
| 推墙交接 | 需要持续推墙计时，或按纵向低速自动交接 | 删除持续推墙计时和低速自动交接；无撞墙缓存时，Physics 检测到朝墙输入即可切 Player |
| `wallSlideStartSpeed` | 兼作系统交接的低速阈值 | 只用于普通墙滑初始 Y 上限和高速 X 缓存期推墙制动启动条件，**不参与系统交接** |
| 高速落地 | 落地立即让玩家地面控制消耗动量，或采用其他地面宽限写法 | 仅当 Physics 在新地面接触时 `|X| > 24`，保持 Physics 约 `0.1s`；期间按 `physicsHorizontalDeceleration` 只衰减 X，不改 Y、不施力 |
| 高速撞墙 | 主要靠当前碰撞后速度或纵向速度判断 | 新墙接触用 `VelocityBeforePhysics.x` 锁存撞墙前世界 X，缓存 `0.1s`，避免碰撞解算清零 X 后丢失动量 |
| 松键墙滑 | 退出玩家墙滑后直接恢复物理下落 | 同墙、中性松键、仍在下落时，冻结退出时的墙滑 cap，Y 制动权重按 `0.3s` 淡出；X 不插值 |
| 自由下落 | 旧的终端速度/超速恢复会限制下落 | 下落由 Unity 原生重力加速；达到配置的 `maxFallSpeed` 后由 Physics 限制最大下落速度，该值同时作为普通玩家墙滑最终 cap |

当前还明确删除或不再使用：综合向量限速、`moveSpeed * 1.1` 阈值、持续推墙计时、低速自动交接，以及把普通墙滑速度重新升高当作自动切回 Physics 的条件。

## 4. 外层 PlayerController2D

### 4.1 初始化与组件契约

`PlayerController2D` 要求同节点存在 `Rigidbody2D` 和 `PolygonCollider2D`。`Awake` 依次完成：

1. 缓存刚体、身体碰撞体和出生点。
2. 创建两个动作系统，默认激活 Player 并调用其 `Enter()`。
3. 确保 `visualRoot` 和显示用 `SpriteRenderer` 存在；若根节点原有精灵，则复制显示设置到子节点并禁用根节点精灵，避免双重绘制。
4. 强制刚体使用 `CollisionDetectionMode2D.Continuous`。
5. 在玩家中心创建非 Trigger 的 `CircleCollider2D`，名为 `Center Point`，半径取 `centerPointRadius`。

显示旋转只作用于 `visualRoot`；物理根节点、刚体和身体碰撞体不随传送门旋转。`Center Point` 专用于薄门碰撞，身体碰撞体的门层过滤由 `PortalPair` 管理。

唯一身体 Polygon 保持 `0.72 × 1.5` Bounds：底边局部 Y 为 `-0.75`，两侧竖直，头顶使用半径 `0.36`、圆心局部 Y 为 `0.39` 的 16 段半圆弧。`ConfigureDefaultBody` 统一供 Prefab 构建入口和测试夹具生成路径；Awake 不重建路径或覆盖材质。

安全空间查询复用同一组几何常量，将身体拆成下身 OverlapBox 与头顶 OverlapCircle 的并集，排除门层和 Trigger。它们只是查询，不会增加第二个物理碰撞体。

### 4.2 输入与共享状态

- `SetMoveInput(float)`：进入程序接管；即使传 0 也保持接管。输入经可选反转后 Clamp 到 `[-1, 1]`。
- `ClearManualInput()`：清除程序接管并停下，设备输入随后可再次生效。
- `SubmitDeviceInput(...)` / `ClearDeviceInput()`：供同程序集总控使用；程序接管期间不会覆盖程序命令。
- `JumpPressed()`：写入跳跃缓冲并记录按住状态。
- `JumpReleased()`：只登记松开事件；在下一物理步结束长按推进，不立即截断速度。
- `SetHorizontalInputInverted(bool)`：只反转操作输入，不反转墙法线或墙跳方向锁。
- `CurrentHorizontalInput`：墙跳方向锁有效时使用 `_forcedMoveDirection`，否则使用 `_moveInput`。

外层还持有土狼时间、跳跃缓冲、墙跳方向锁、地墙法线与接触时间、高速 X 缓存、墙滑淡出，以及当前动作系统引用。

### 4.3 接触探测

所有地墙 BoxCast 使用：

```text
contactMask = groundMask & ~portalContactMask
```

因此即使误把门层加入 `groundMask`，只要 `portalContactMask` 覆盖该门层，门碰撞也不会被识别为地面或可控墙。

- 地面：从身体碰撞体底部附近向下 BoxCast，探针覆盖完整脚底宽度，距离为 `groundCheckDistance`；只接受 `normal.y > 0.3` 的向上表面，接地时更新土狼时间和 `_groundNormal`。
- 墙面：从身体碰撞体 Bounds 向右、再向左 BoxCast，额外距离为 `wallCheckDistance`；只接受法线足够竖直的真实地形面，初始重叠导致法线不明确时也允许命中。
- 探测尺寸来自身体碰撞体实时 Bounds；`playerHeight` 只用于 Scene 辅助线，不参与实际碰撞探测。
- Physics 跳跃上升暂停期间，本步 `_wallDirection` 保持 0；地面探测不被该暂停关闭。

### 4.4 跳跃仲裁

`TryConsumeBufferedJump()` 先于普通动作系统交接执行：

- **普通跳/土狼跳**：默认地面或土狼时间优先；`launchX = 当前 X + jumpHorizontalBoost * 当前输入`，再调用 `PhysicsActionSystem.StartJump(launchX, jumpHeight)`。
- **明确离墙输入的墙跳**：在可墙跳时优先于普通/土狼跳，避免继承撞墙前 X。
- **墙跳**：要求有墙且 `velocity.y <= 0`；固定使用 `-wallDirection * (moveSpeed + jumpHorizontalBoost)` 作为 X，高度为 `wallJumpHeight`。`StartJump` 同时覆盖 X/Y，不继承碰墙动量或缓存。
- **上升贴墙按跳**：不允许墙跳，并清除本次跳跃缓冲；不会留到下降后自动墙跳。
- 墙跳后设置离墙方向锁 `wallJumpForceTime`。

普通跳和墙跳都切到 Physics；完成请求后清理土狼时间、旧接触、缓存、淡出和墙滑状态。离散跳跃助推不经过 `inputSpeedLimit` 硬截断。

## 5. PlayerActionSystem：玩家直接控制

### 5.1 核心职责

- 地面和玩家墙面模式的横向目标速度求解。
- 近平地水平移动时，对不超过 `maxStepUpHeight=0.2` 的局部台阶执行安全爬升。
- 地面松键减速、平地 Y 归零和斜坡切向速度计算。
- 非接地、下降且朝墙输入时执行普通墙滑。
- 提供 `IsWallSliding`、`IsContinuousWallSlide`、`WallSlideDirection` 和 `CurrentWallSlideCap`。

`Enter()`、`Exit()`、`OnTeleport()` 都重置墙滑内部状态；它们不直接写刚体速度。`IsContinuousWallSlide` 用于标记一段连续墙滑及其计时，不再作为高速自动交接条件。

### 5.2 每个 Tick 的运动

1. 用 `CalculatePlayerHorizontalSpeed` 只计算下一 X。
2. 接地且地面接近水平时，按本步 X 位移探测侧壁，再用局部向下射线求台阶顶面；顶面必须与侧壁属于同一 Collider，高度不得超过 0.2。
3. 台阶候选先检查圆头爬升路径，再检查目标完整身体。通过后由 Player 自己应用本步 X 位移和爬升高度，写入 `(vx,0)` 并结束本步；高墙、第二堵墙、窄槽或头顶不足都放弃爬升。
4. 未爬阶时，接地斜坡按 `vy = vx * (-normal.x / normal.y)` 计算，其他接地情形把 Y 写为 0，再写回 `(vx, vy)`。
5. 若当前非接地、有真实墙、朝墙输入且 Y 不在上升，则进入/维持墙滑，并用共享公式修正 Y。

台阶落点保留 `0.03` 安全间隙，大于两侧默认接触偏移之和；随后由正常接地和重力贴合顶面。爬阶不调用跳跃或传送，不增加动作系统。

普通墙滑不是永久低速悬挂：墙滑 cap 随 `wallSlideTime` 从 `wallSlideStartSpeed` 插值到 `maxFallSpeed`。`wallSlideTime = 0` 时立即采用最终 cap。

## 6. PhysicsActionSystem：空中与物理动量

### 6.1 核心职责

- 空中 X 输入求解后，按 `physicsHorizontalDeceleration` 将 X 向 0 衰减；同向超速不会被输入求解主动刹回跑速。
- 普通跳/墙跳初速度、长按推进、上升重力和顶点低重力。
- 上升倒角的预测横向纠偏，以及纠偏目标和后续上升路径的空间验证。
- 跳跃上升墙面监测暂停，以及传送后的暂停取消。
- 高速撞墙 X 缓存、离墙单次恢复和缓存期推墙 Y 制动。
- 同墙中性松键后的墙滑 Y 淡出。
- 自由下落由 Unity 原生重力加速，达到 `maxFallSpeed` 后限制最大下落速度。

### 6.2 空中权限

`AirControlAuthority` 只缩放方向输入对 X 的调整率，不缩放已有速度：

- 离地至 `airControlFullTime = 0.3s`：权限为 1。
- 非跳跃离地超过该时间：权限为 0。
- 跳跃离地：从 0.3s 到 `jumpAirControlEndTime = 0.8s` 线性降至 0。
- 权限为 0 后，方向输入不再调整已有 X，但 `physicsHorizontalDeceleration` 仍会继续衰减 X。

系统 `Enter()`、显式 `ResetMotion()` 会重置这套进度，`StartJump()` 也会把新一次跳跃的空中时间归零；门传送不调用这些重置入口，所以不会借传送重新获得空中控制权。

### 6.3 跳跃上升与墙监测

`StartJump` 设置跳跃相位并武装墙监测暂停。`RefreshJumpPhase()` 以实际 `velocity.y <= 0` 判断顶点；在仍上升时暂停不会因估算计时耗尽而提前失效。暂停期间外层不探测墙，避免上升擦墙打断跳跃弧线。

门传送调用当前系统的 `OnTeleport()`：Physics 的实现立即取消本次暂停及其武装状态，但保留跳跃推进和空中权限。也就是说，**传送后不必等待原跳跃顶点，下一固定步即可重新探测墙面**。传送也不会自动重新武装旧暂停。

### 6.4 接地保留期间

Physics 只有在高速落地延迟交接时才会带着 `IsGrounded == true` 执行 Tick。该分支结束跳跃状态，按 `physicsHorizontalDeceleration` 将 X 向 0 衰减并保留当前 Y，不施加额外力，随后直接返回；碰撞和 Unity 原生物理继续工作，直到外层交还 Player。

### 6.5 上升倒角安全纠偏

在现有速度计算全部完成且最终 Y 仍向上时，圆头按本步预计上升距离做 CircleCast，因此纠偏发生在碰撞把 Y 清零之前。只有同时含向下和横向分量的倒角法线会触发；平天花板、竖墙和零法线初始重叠保持原碰撞结果。

横向逃逸方向取倒角法线的 X 符号，从 `0.02` 开始选择最小候选，最多移动 `headCorrectionMaxDistance=0.24`。候选必须同时满足：当前高度完整身体可放置、本步上升后的完整身体可放置、继续向上不会撞上其他障碍且已绕开原向下表面。只剩原障碍的竖直侧面时允许贴墙继续上升。

成功后只修正根节点 X，保留本步最终 Y 速度；失败则不纠偏，不增加二段跳、持续吸附或额外向上速度。该位置修正不经过 `TeleportCore`，也不改变 `PositionRevision`、跳跃进度或动作系统。

## 7. 共享横向纯计算与纵向墙滑公式

### 7.1 横向只处理 X

`CalculatePlayerHorizontalSpeed`：

- 目标为 `input * moveSpeed`。
- 有输入使用 `groundAcceleration`，松键使用 `groundDeceleration`。
- 以 `MoveTowards` 得到候选 X，再交给输入增速限制。

`CalculateAirHorizontalSpeed`：

- 松键或权限为 0：保留当前 X。
- 与输入同向且已超过该方向目标速度：保留当前 X，不主动刹回跑速。
- 其他情况按 `airAcceleration * authority` 接近目标 X，再交给输入增速限制。

正常空中分支在输入求解后调用 `ApplyPhysicsHorizontalDeceleration`，以 `physicsHorizontalDeceleration * dt` 为步长将结果 X 向 0 移动；高速落地保留期也使用同一函数。默认 `12` 单位/秒²，设为 `0` 可关闭。撞墙 X 缓存和离墙单次恢复不经过这项衰减。

`LimitHorizontalInputAcceleration(currentX, candidateX)` 仅在以下两个条件同时成立时拒绝候选值：

```text
|candidateX| > inputSpeedLimit
且 |candidateX| > |currentX|
```

因此 24 是“AD 继续增加 |X| 的上限”，不是刚体总速度硬上限。已有超速仍可减速、反向；Y、跳跃助推、传送速度和外部物理动量均不被它 Clamp。

### 7.2 共享墙滑 Y 公式

Player 普通墙滑和 Physics 松键淡出都调用 `CalculateWallSlideVerticalVelocity(currentY, cap, dt)`：

```text
gravityStep = |Physics2D.gravity.y * Rigidbody2D.gravityScale| * dt
desiredY    = MoveTowards(currentY, -cap, gravityStep)
return      = desiredY + gravityStep
```

末尾补回 `gravityStep` 是为了配合随后 Unity 原生重力积分。两套墙滑路径不得各自复制另一套公式。

### 7.3 同墙松键淡出

只有从有效 Player 墙滑退出，且满足“同一面墙、输入回到中性、仍在下降”时，外层才在 Player `Exit()` 前保存当前 cap 并启动 `wallSlideReleaseFadeTime = 0.3s`。

Physics 每步计算：

```text
weight  = Clamp01(remaining / wallSlideReleaseFadeTime)
nextY   = Lerp(currentY, CalculateWallSlideVerticalVelocity(currentY, savedCap, dt), weight)
```

落地、离墙、换墙、任何方向输入或转为上升都会清除淡出。反向输入和上升退出不会新建淡出；X 始终不做额外插值。

## 8. 高速落地、撞墙缓存与交接

共用配置 `highSpeedContactRetentionTime = 0.1s`，但地面与墙面使用不同状态。

### 8.1 高速落地

- 仅在新地面接触且当前为 Physics 时锁存 `_delayLandingHandoff`。
- 条件为落地瞬间世界 `|velocity.x| > inputSpeedLimit`，严格大于 24；Y 不参与。
- 条件成立且地面接触时间仍小于 0.1s 时保持 Physics；否则切 Player。
- 保留期每个物理步按 `physicsHorizontalDeceleration` 衰减 X，保留 Y，不改交接时机。
- 普通速度落地立即恢复 Player 地面控制。
- 接地优先清除墙面 X 缓存和墙滑淡出，不能跨地面继续生效。

### 8.2 新高速撞墙

- 只在新探测到墙、没有现存缓存/待恢复动量且 retention 大于 0 时尝试建立。
- 使用物理步开始记录的 `VelocityBeforePhysics.x`，因为碰撞解算可能已把当前 X 清零。
- 条件为 `approachX * wallDirection > inputSpeedLimit`：既要求朝墙，也要求撞墙前世界 X 超过 24。纯纵向高速不算高速撞墙缓存。
- 缓存保存完整的有符号 `approachX`、墙方向和计时。即使撞墙前是 Player，新高速撞墙也先切 Physics 执行缓存。

缓存有效时：

- Physics 强制当前 X 为 0；普通空中方向输入不再改写 X。
- 缓存本身不套用普通墙滑 Y cap；下降默认仍由原生重力积分，上升则继续走既有跳跃弧线修正。
- 只有“仍朝该墙输入且 `velocity.y < -wallSlideStartSpeed`”时，才通过向上力施加 `highSpeedWallBrakeAcceleration`；不推墙不会制动。
- 缓存存在期间，主观推墙不能提前抢回 Player。

缓存结束：

- **真正离开同一面墙**：探测到离墙的本物理步生成一次性待恢复标记，Physics 将锁存的撞墙前 X 单次赋值回刚体，保留离墙时 Y；不会叠加或重复恢复。
- **换墙**：清除缓存，不恢复原方向 X。
- **同墙到期**：清除缓存；若仍朝墙输入，同一物理步即可切 Player，否则保持 Physics。
- **落地、跳跃或传送**：按各自清理规则提前清除，不跨这些事件保留。

## 9. PortalPair 与玩家控制器的接口

### 9.1 门入口与出口职责

`PortalPair` 通过中心点碰撞回调或 FixedUpdate 扫掠确认命中，再以 `controller.VelocityBeforePhysics` 作为进门速度。速度映射遵循：开口法线分量取反、门切线分量保留；移动/旋转门还会先扣除入口门接触点速度，再叠加出口门点速度。

位置映射后，必须保证玩家中心碰撞点处于出口薄碰撞箱之外。沿出口 `Opening` 的最小法向间距为：

```text
safeExitOffset = -gateContactOffset
               + gateThickness * 0.5
               + controller.centerPointRadius
               + exitMargin
```

若映射结果的法向偏移小于该值，`PortalPair` 沿出口开口方向补足距离。这里包含门碰撞箱后移量、半厚度、中心点半径和额外安全边距；目的是避免冷却期间中心点继续撞门并被碰撞解算清零整份速度。`exitMargin` 脚本默认 0.03，过大会让出口明显外移。

### 9.2 传送调用顺序与速度写回

`PortalPair.TeleportAt` 先调用 `TeleportWithPortalRotation(...)`，再在 `preserveVelocity == true` 时调用：

```csharp
controller.SetPortalExitVelocity(velocity);
```

`SetPortalExitVelocity(Vector2)` 只做两件事：

1. 写 `_rb.velocity = velocity`。
2. 同步 `VelocityBeforePhysics = velocity`。

它不移动位置、不切换动作系统、不重置空中权限，也不建立额外动量保护标记。`preserveVelocity == false` 时不调用它，TeleportCore 留下的零速度就是出口速度，同时旧长按跳跃进度也不会保留。

门传送与生命周期传送必须区分：

- `TeleportWithPortalRotation`：清旧位置接触状态、递增位置版本、映射显示角度和剩余跳跃方向；保留当前动作系统及 Physics 空中计时。
- `Teleport(Vector2)`：停止运动、恢复竖直，并重置为 Player；不是门业务保留动量的入口。
- `Respawn()`：回出生点、重置为 Player，并清跳跃缓冲/土狼时间等生命周期状态；持续水平输入仍保留。

## 10. 关键公开接口与状态

以下均为 `PlayerController2D` 上真实存在的接口；嵌套动作系统方法不属于外部公共 API。

| 接口 | 用途/注意事项 |
|---|---|
| `SetMoveInput(float horizontal)` | 程序接管水平输入；传 0 不会解除接管 |
| `ClearManualInput()` | 解除接管并清零当前水平输入 |
| `SetHorizontalInputInverted(bool inverted)` | 设置输入反转，不改墙跳方向锁 |
| `JumpPressed()` / `JumpReleased()` | 提交跳跃按下/松开事件 |
| `Respawn()` | 回出生点并执行生命周期重置 |
| `Teleport(Vector2 position)` | 生命周期瞬移并回到 Player |
| `SetPortalExitVelocity(Vector2 velocity)` | 仅供门出口写速度及物理步前速度，不做系统重置 |
| `TeleportWithPortalRotation(Vector2 position, float exitAngle)` | 门传送简入口，默认使用当前跳跃方向并保留其进度 |
| `TeleportWithPortalRotation(Vector2, float, Vector2, bool)` | 完整门入口，可传映射后的跳跃方向及是否保留进度 |
| `MirrorSpriteHorizontally()` | 只翻转显示精灵，不镜像物理空间或缩放 |
| `SetSpawnPoint(Vector3 worldPosition)` | 更新后续重生位置 |

常用只读状态：

- `IsGrounded`、`HasWallContact`、`IsWallSliding`。
- `IsPhysicsActionActive`、`AirControlAuthority`。
- `Velocity`、`VelocityBeforePhysics`。
- `MoveInput`、`IsManualInput`、`IsHorizontalInputInverted`。
- `CenterPoint`、`VisualRoot`、`CurrentRotationAngle`、`IsRealigning`。
- `VariableJumpDirection`：门按与速度相同的法线/切线规则变换该方向。

`PositionRevision` 是 `internal`，供 `PortalPair` 区分显式瞬移与连续位移，避免把重生/传送前后的点扫成一条假走廊。

## 11. Editor 与 Prefab

### 11.1 中文 Inspector

`PlayerController2DEditor` 是 `PlayerController2D` 的 `CustomEditor`。它通过 `serializedObject.Update()` / `ApplyModifiedProperties()` 和 `EditorGUILayout.PropertyField` 绘制真实序列化字段，使用中文分组、中文标签与中文 `GUIContent` Tooltip；缺少某字段时 `Field` 会跳过，不伪造替代属性。

当前说明特别强调：

- `inputSpeedLimit` 只限制 AD 增加 X 绝对速度，也是高速落地/撞墙阈值。
- `wallSlideStartSpeed` 不是交接阈值。
- 跳跃上升暂停墙面监测，顶点或传送后恢复。
- 高速接触保留和同墙松键淡出分别是 0.1s、0.3s 配置。
- 自由下落使用原生重力加速，并受 `maxFallSpeed` 限制；该值同时作为玩家墙滑最终上限。
- “最大低台阶高度”和“头顶最大纠偏距离”分别对应 `maxStepUpHeight=0.2`、`headCorrectionMaxDistance=0.24`；两者都受完整身体空间与净空检查约束。

参数字段上的中文 `[Tooltip]` 是 Inspector 悬停说明的主来源；`Field` 会优先使用 `_property.tooltip`，仅在缺失时使用调用处的备用文案。新增或调整说明应优先修改字段声明，并同步分组标签、需要固化的 Prefab 默认值和文档。

### 11.2 Player.prefab 当前序列化值

| 分组 | 字段与值 |
|---|---|
| 水平 | `moveSpeed=9`，`groundAcceleration=100`，`groundDeceleration=100`，`airAcceleration=65`，`physicsHorizontalDeceleration=12`，`inputSpeedLimit=24` |
| 空中权限 | `airControlFullTime=0.3`，`jumpAirControlEndTime=0.8` |
| 跳跃 | `jumpHeight=6.2`，`playerHeight=1.5`，`coyoteTime=0.1`，`jumpBufferTime=0.12`，`variableJumpTime=0.2` |
| 跳跃弧线/助推 | `riseGravityMultiplier=0.85`，`jumpHorizontalBoost=4`，`apexVelocityThreshold=8.5`，`apexGravityMultiplier=0.5` |
| 墙滑 | `maxFallSpeed=100`，`wallSlideStartSpeed=4`，`wallSlideTime=1.2`，`wallSlideReleaseFadeTime=0.3` |
| 墙面 | `wallCheckDistance=0.12`，`wallJumpHeight=4.8`，`highSpeedWallBrakeAcceleration=380`，`wallJumpForceTime=0.24` |
| 接触 | `groundMask=64`（第 6 层），`portalContactMask=256`（第 8 层），`groundCheckDistance=0.15`，`maxStepUpHeight=0.2`，`headCorrectionMaxDistance=0.24` |
| 传送门回正 | `portalRealignGroundedSpeedMultiplier=1` |
| 高速接触/重生 | `highSpeedContactRetentionTime=0.1`，`fallLimitY=-12` |

Prefab 未单独序列化的字段使用脚本默认值，包括：`invertHorizontalInput=false`、`centerPointRadius=0.08`、`visualRoot=null`、`portalRealignSpeed=80`、EaseInOut 回正曲线、下坠回正阈值 12、最大下坠倍率 3。

组件要点：玩家在第 7 层；Dynamic `Rigidbody2D` 的质量 1、线性阻力 0、`gravityScale=4`、冻结旋转并启用插值；单个平底圆头 `PolygonCollider2D` 的 Bounds 为 `0.72 × 1.5`，底边无圆角或中间点。`Awake` 会再次确保连续碰撞，并运行时创建显示子节点和 Center Point。

由此得到当前默认动作数值：普通满跳 **6.2 格**、墙跳 **4.8 格**、跑速 **9**、水平助推 **4**、输入 X 增速上限 **24**。普通跑跳从 X=9 起跳可得到 X=13；墙跳固定离墙 X 绝对值也是 13，而不是继承撞墙动量。

## 12. 使用与维护注意事项

1. **不要恢复综合向量限速或全局 X Clamp。** 新横向规则必须只处理 X；外部动量、Y 和自由下落不应被输入上限截断，自由下落仅受 `maxFallSpeed` 限制。
2. **不要让两个系统同时 Tick。** 新运动分支应放入当前系统，或通过外层明确切换；不要恢复并列的横向/纵向运动写入入口。
3. **保持 FixedUpdate 顺序。** 计时 → 接触探测 → 接触计时/缓存 → 跳跃优先仲裁 → 系统交接 → 当前系统 Tick → 重生检查 → 记录 `VelocityBeforePhysics`。
4. **高速撞墙必须使用撞墙前 X。** 不要直接改用可能已被碰撞清零的当前 X，也不要把纵向高速当作 X 撞墙缓存条件。
5. **区分 24、4 和 100 的用途。** `inputSpeedLimit=24` 管 X 输入与高速接触；`wallSlideStartSpeed=4` 管墙滑初值和缓存期制动启动；Prefab 的 `maxFallSpeed=100` 管 Physics 最大下落速度和普通墙滑最终 Y cap。
6. **共享墙滑公式只能有一份。** 修改 Y 修正时同步考虑 Player 墙滑与 Physics 淡出；淡出必须在 Player 状态被 `Exit()` 清理前捕获。
7. **传送恢复墙监测不能回归。** Physics `OnTeleport()` 必须取消旧暂停；同时门传送不能意外重置空中权限或动作系统。
8. **保持门层双重隔离。** 地墙探测使用 `groundMask & ~portalContactMask`；`PortalPair` 让身体碰撞体排除第 8 层，只让 Center Point 撞门。修改层配置时两边必须一致。
9. **注意创建期参数。** `centerPointRadius` 在 `Awake` 创建圆形碰撞点时读取；运行时仅改字段不会自动重建已有碰撞点。
10. **配置改动要同步四处。** 字段及范围特性、Editor 中文说明、需要固化默认值的 Prefab、这两份文档；不要新增与旧语义相同的影子参数。
11. **显示与物理分离。** 传送旋转和回正只改 `visualRoot`，不要旋转带 Rigidbody2D/PolygonCollider2D 的根节点。
12. **纠偏必须先验证空间。** 低台阶与头顶倒角都留在各自动作系统内；目标身体、头顶路径或后续净空不足时必须放弃，不能用 Teleport 或强制速度绕过阻挡。

## 13. 测试源码现状

本轮新增的 12 项低台阶、左右倒角、净空拒绝和旋转契约测试已在临时 Unity 工程全部通过；完整执行范围见架构文档的测试状态。

其中 `High_Speed_Wall_Push_Brakes_Then_Hands_Off_To_Player` 的语义已过时：该测试用 `X=0、Y=-40` 构造“高速撞墙”，随后期待 Physics 先进行纵向制动，再在降到 `wallSlideStartSpeed` 附近后自动交还 Player。当前实现的高速撞墙缓存只由撞墙前世界 X 超过 24 触发，且已删除低速自动交接，因此该测试不能作为当前行为的有效验收标准。

后续若维护测试，应围绕新契约重写该用例：使用朝墙的世界 X 超速建立缓存，验证 0.1s 缓存、推墙制动、缓存优先交接和真正离墙后的单次 X 恢复；不要继续断言“纵向降到 4 后自动交接”。
