// 文档导航（相对于项目根目录）：
// 模块说明：.trae/documents/player-controller-modules.md
// 架构说明：.trae/documents/player-controller-architecture.md
using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 精简、可调的 2D 平台跳跃角色控制器。
    ///
    /// 跳跃高度以世界单位表示（1 单位 = 1 格），并且与重力无关：
    /// 每次起跳都会根据当前重力重新计算初速度，因此修改
    /// Physics2D.gravity 后，跳跃高度仍等于 <see cref="jumpHeight"/>。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class PlayerController2D : MonoBehaviour
    {
        #region 可配置参数

        [Header("水平移动")]
        [Tooltip("方向输入的横向目标速度（单位/秒），范围 0~任意正值；0 表示输入目标为静止，已有超速不会被硬截断")]
        [Min(0f)] public float moveSpeed = 9f;
        [Tooltip("地面或玩家墙面模式按住方向时的横向加速度（单位/秒²），范围 0~任意正值；0 表示方向输入不再加速")]
        [Min(0f)] public float groundAcceleration = 100f;
        [Tooltip("地面松开方向时的横向减速度（单位/秒²），范围 0~任意正值；0 表示不主动减速，贴墙松键由物理模块接管")]
        [Min(0f)] public float groundDeceleration = 100f;
        [Tooltip("空中方向输入调整 X 速度的变化率（单位/秒²），范围 0~任意正值；会乘以剩余空中权限，松键不主动刹车")]
        [Min(0f)] public float airAcceleration = 65f;
        [Tooltip("AD 允许继续增加 |X| 的上限及高速地墙接触阈值（单位/秒），范围 0~任意正值；Y 不参与，已有超速仍可减速和反向")]
        [Min(0f)] public float inputSpeedLimit = 24f;

        [Header("空中控制权限")]
        [Tooltip("离地后水平方向控制保持完整权限的时间（秒），范围 0~任意正值；0 表示离地后立即进入权限衰减")]
        [Min(0f)] public float airControlFullTime = 0.3f;
        [Tooltip("跳跃离地后水平控制权限完全归零的时间（秒），范围 0~任意正值；小于完整控制时间时按完整控制时间处理")]
        [Min(0f)] public float jumpAirControlEndTime = 0.8f;

        [Header("输入方向")]
        [Tooltip("是否反转 AD/水平输入；开启后按右会向左走，只反转操作输入，不改变墙跳方向锁或接触法线")]
        public bool invertHorizontalInput;

        public bool IsHorizontalInputInverted => invertHorizontalInput;

        [Header("跳跃")]
        [Tooltip("持续按住跳跃的普通满跳高度（世界单位/格），范围 0~任意正值；0 表示没有有效起跳高度")]
        [Min(0f)] public float jumpHeight = 6.2f;
        [Tooltip("Scene 跳跃辅助线使用的角色高度（格），范围 0.01~任意正值；不修改碰撞体，实际接触探测使用胶囊 Bounds")]
        [Min(0.01f)] public float playerHeight = 1.5f;
        [Tooltip("离地后仍可普通起跳的宽限时间（秒），范围 0~任意正值；0 表示关闭土狼时间，不影响墙跳")]
        [Min(0f)] public float coyoteTime = 0.1f;
        [Tooltip("落地或下降贴墙前跳跃预输入的保留时间（秒），范围 0~任意正值；0 表示关闭，上升贴墙按跳不留墙跳缓冲")]
        [Min(0f)] public float jumpBufferTime = 0.12f;
        [Tooltip("起跳后按住跳跃可维持推进的时间（秒），范围 0~任意正值；0 表示关闭，跳跃上升期间暂停墙面监测，顶点或传送后恢复")]
        [Min(0f)] public float variableJumpTime = 0.2f;
        [Tooltip("非维持上升阶段的基础重力倍率，范围 0.1~2；调低会减小起跳初速度，满跳高度仍按配置反算，自由下落不使用此倍率")]
        [Range(0.1f, 2f)] public float riseGravityMultiplier = 0.85f;
        [Tooltip("普通跳在当前 X 上代数叠加、墙跳计算固定离墙 X 使用的水平助推（单位/秒），范围 0~任意正值；0 表示不额外助推")]
        [Min(0f)] public float jumpHorizontalBoost = 4f;
        [Tooltip("进入顶点低重力区的上升 Y 速度阈值（单位/秒），范围 0~任意正值；0 表示关闭顶点阶段，且需按住跳跃才生效")]
        [Min(0f)] public float apexVelocityThreshold = 8.5f;
        [Tooltip("上升顶点阶段直接乘在基础重力上的倍率，范围 0.1~1；仅在顶点阈值内且按住跳跃时生效，自由下落始终使用原生重力")]
        [Range(0.1f, 1f)] public float apexGravityMultiplier = 0.5f;
        [Tooltip("普通玩家墙滑的最终下落速度上限（单位/秒），范围 0~任意正值；0 表示最终可停住下落，不限制自由下落或传送出口速度")]
        [Min(0f)] public float maxFallSpeed = 24f;

        [Header("墙面")]
        [Tooltip("胶囊向两侧 BoxCast 的额外墙面探测距离（世界单位），范围 0.01~任意正值；只接受足够竖直的真实地形墙")]
        [Min(0.01f)] public float wallCheckDistance = 0.12f;
        [Tooltip("墙跳持续按住时的目标高度（格），范围 0.1~任意正值；通常低于普通满跳，使返回同墙时高度自然下降，不限制墙跳次数")]
        [Min(0.1f)] public float wallJumpHeight = 4.8f;
        [Tooltip("玩家墙滑初始下落上限及高速撞墙缓存期推墙制动的启动阈值（单位/秒），范围 0~任意正值；不参与动作系统交接判定")]
        [Min(0f)] public float wallSlideStartSpeed = 4f;
        [Tooltip("持续玩家墙滑从初始下落上限恢复到最终上限所需时间（秒），范围 0~任意正值；0 表示立即使用最终上限")]
        [Min(0f)] public float wallSlideTime = 1.2f;
        [Tooltip("高速撞墙缓存期内，下落且朝墙输入时物理模块施加的向上加速度（单位/秒²），范围 0~任意正值；0 表示关闭制动")]
        [Min(0f)] public float highSpeedWallBrakeAcceleration = 380f;
        [Tooltip("墙跳后强制保持离墙输入的时间（秒），范围 0~任意正值；0 表示关闭方向锁，不影响后续再次墙跳")]
        [Min(0f)] public float wallJumpForceTime = 0.24f;

        [Header("地面检测")]
        [Tooltip("参与地面和墙面 BoxCast 的地形 LayerMask，可多选；应只配置可站立地形，不要包含传送门层")]
        public LayerMask groundMask = 1 << 6;
        [Tooltip("从地墙控制接触中排除的传送门 LayerMask，可多选；需覆盖门碰撞层，默认排除 Snapshot 门碰撞盒")]
        public LayerMask portalContactMask = 1 << 8;
        [Tooltip("胶囊底部向下 BoxCast 的延伸距离（世界单位），范围 0~任意正值；过大可能提前接地，过小可能在斜坡丢失接触")]
        [Min(0f)] public float groundCheckDistance = 0.15f;

        [Header("重生")]
        [Tooltip("触发重生的世界 Y 阈值，可配置任意正负值；玩家根节点低于此值立即回到出生点")]
        public float fallLimitY = -12f;

        [Header("传送门碰撞点")]
        [Tooltip("玩家中心圆形碰撞点的半径（世界单位），范围 0.01~任意正值；用于薄门连续碰撞，也会计入出口安全距离")]
        [Min(0.01f)] public float centerPointRadius = 0.08f;

        [Header("显示")]
        [Tooltip("承载 SpriteRenderer 和传送门旋转的显示子节点；可留空并由 Awake 自动创建，物理根节点与胶囊始终不旋转")]
        public Transform visualRoot;

        [Header("传送门旋转（玩家管理）")]
        [Tooltip("显示节点回正的基础角速度（度/秒），范围 0.01~任意正值；越大回正越快，出口角度为 0 时无需回正")]
        [Min(0.01f)] public float portalRealignSpeed = 80f;

        [Tooltip("回正插值曲线，横轴与纵轴建议均为 0~1；留空时退化为线性插值，只影响显示角度，不影响物理")]
        public AnimationCurve portalRealignCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("传送门回正动态加速")]
        [Tooltip("下坠回正加速达到最大倍率所需的下落速度（单位/秒），范围 0.01~任意正值；只加速显示回正，不改变物理速度")]
        [Min(0.01f)] public float portalRealignFallSpeedForMaxBoost = 12f;

        [Tooltip("达到下坠加速阈值时使用的最大回正速度倍率，范围 1~任意正值；1 表示关闭下坠加速")]
        [Min(1f)] public float portalRealignMaxFallSpeedMultiplier = 3f;

        [Tooltip("玩家触地时至少使用的回正速度倍率，范围 1~任意正值；与当前下坠倍率取较大值")]
        [Min(1f)] public float portalRealignGroundedSpeedMultiplier = 6f;

        [Header("高速接触与墙滑淡出")]
        [Tooltip("高速落地或撞墙后的动量保留时长（秒），范围 0~任意正值；0 表示不延迟，撞墙缓存会被落地、跳跃或传送提前清除")]
        [Min(0f)] public float highSpeedContactRetentionTime = 0.1f;
        [Tooltip("同墙中性松键后墙滑 Y 制动的淡出时长（秒），范围 0~任意正值；0 表示立即恢复原生重力，X 不做额外插值")]
        [Min(0f)] public float wallSlideReleaseFadeTime = 0.3f;

        #endregion

        #region 运行时状态

        Rigidbody2D _rb;
        CapsuleCollider2D _col;
        SpriteRenderer _sr;
        CircleCollider2D _centerPoint;

        float _moveInput;
        bool _manualInput;
        bool _jumpHeld;
        bool _jumpReleasePending;
        float _coyoteCounter;
        float _jumpBufferCounter;
        int _wallDirection;
        int _forcedMoveDirection;
        float _forcedMoveTimer;
        Vector3 _spawnPoint;
        Vector2 _groundNormal = Vector2.up;
        float _groundContactTime;
        bool _delayLandingHandoff;

        int _previousWallDirection;
        bool _hasWallMomentumCache;
        float _wallMomentumCacheX;
        int _wallMomentumCacheDirection;
        float _wallMomentumCacheTime;
        bool _hasPendingWallMomentum;
        float _pendingWallMomentumX;

        float _wallSlideFadeRemaining;
        float _wallSlideFadeCap;
        int _wallSlideFadeDirection;

        PlayerActionSystem _playerActions;
        PhysicsActionSystem _physicsActions;
        ActionSystem _activeActions;

        bool _isRealigning;
        float _realignElapsed;
        float _realignStartAngle;

        #endregion

        #region 状态查询

        /// <summary>角色站在（或紧贴）可站立地面时为 true。</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>玩家动作系统正在执行普通贴墙下落时为 true。</summary>
        public bool IsWallSliding => _playerActions != null && _playerActions.IsWallSliding;

        /// <summary>当前是否探测到可控制的真实地形墙。</summary>
        public bool HasWallContact => _wallDirection != 0;

        /// <summary>当前是否激活物理动作系统；高速接地保留期间仍由碰撞和原生重力维持接触。</summary>
        public bool IsPhysicsActionActive => _physicsActions != null && _activeActions == _physicsActions;

        /// <summary>当前刚体速度。</summary>
        public Vector2 Velocity => _rb != null ? _rb.velocity : Vector2.zero;

        /// <summary>剩余长按跳跃的推进方向；传送门按同一法线/切线规则变换它。</summary>
        public Vector2 VariableJumpDirection => _physicsActions != null
            ? _physicsActions.VariableJumpDirection
            : Vector2.up;

        /// <summary>当前应用的水平输入（-1 到 1）。</summary>
        public float MoveInput => _moveInput;

        /// <summary>玩家系统为 1；物理系统按空中时间衰减方向速度调整系数。</summary>
        public float AirControlAuthority => IsPhysicsActionActive
            ? _physicsActions.AirControlAuthority
            : 1f;

        /// <summary>玩家中心的碰撞点，传送门用它来检测是否撞到门。</summary>
        public Collider2D CenterPoint => _centerPoint;

        /// <summary>物理步开始前的速度，传送碰撞回调里用它恢复进门瞬间的速度。</summary>
        public Vector2 VelocityBeforePhysics { get; private set; }

        /// <summary>显式位置跳变的版本号；传送门用它把重生/瞬移与连续物理位移区分开。</summary>
        internal int PositionRevision { get; private set; }

        /// <summary>当前玩家显示部分的 Z 旋转角（-180~180），相机用它对齐。</summary>
        public float CurrentRotationAngle => visualRoot != null
            ? Mathf.DeltaAngle(0f, visualRoot.eulerAngles.z)
            : 0f;

        /// <summary>玩家显示部分的根节点；物理根节点不旋转。</summary>
        public Transform VisualRoot => visualRoot;

        /// <summary>是否正处于“出传送门后回正”的动画中。</summary>
        public bool IsRealigning => _isRealigning;

        #endregion

        #region 初始化与显示

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<CapsuleCollider2D>();
            _spawnPoint = transform.position;
            _playerActions = new PlayerActionSystem(this);
            _physicsActions = new PhysicsActionSystem(this);
            _activeActions = _playerActions;
            _playerActions.Enter();

            EnsureVisualRoot();
            _sr = visualRoot.GetComponent<SpriteRenderer>();

            // 传送门的碰撞箱很薄，用连续碰撞避免高速时直接穿过去。
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            CreateCenterPoint();
        }

        /// <summary>
        /// 把原玩家根节点上的 SpriteRenderer 拆到一个子节点。
        /// 物理根节点只负责 Rigidbody2D/碰撞，子节点只负责显示和传送门旋转。
        /// </summary>
        void EnsureVisualRoot()
        {
            SpriteRenderer source = GetComponent<SpriteRenderer>();

            if (visualRoot == null)
            {
                var visualGo = new GameObject("Player Visual");
                visualGo.layer = gameObject.layer;
                visualGo.transform.SetParent(transform, false);
                visualGo.transform.localPosition = Vector3.zero;
                visualGo.transform.localRotation = Quaternion.identity;
                visualRoot = visualGo.transform;
            }

            SpriteRenderer visualSr = visualRoot.GetComponent<SpriteRenderer>();
            if (visualSr == null) visualSr = visualRoot.gameObject.AddComponent<SpriteRenderer>();

            if (source != null)
            {
                visualSr.sprite = source.sprite;
                visualSr.color = source.color;
                visualSr.sharedMaterial = source.sharedMaterial;
                visualSr.sortingLayerID = source.sortingLayerID;
                visualSr.sortingOrder = source.sortingOrder;
                visualSr.flipX = source.flipX;
                visualSr.flipY = source.flipY;
                visualSr.maskInteraction = source.maskInteraction;
                visualSr.enabled = true;

                // 根节点不再绘制精灵，避免显示两份。
                source.enabled = false;
            }
        }

        /// <summary>在玩家中心创建一个很小的碰撞点，专门用来撞传送门的薄碰撞箱。</summary>
        void CreateCenterPoint()
        {
            var point = new GameObject("Center Point");
            point.layer = gameObject.layer;
            point.transform.SetParent(transform, false);
            point.transform.localPosition = Vector3.zero;

            _centerPoint = point.AddComponent<CircleCollider2D>();
            _centerPoint.radius = centerPointRadius;
            _centerPoint.isTrigger = false;
        }

        void Update()
        {
            AdvancePortalRealign();

            if (_sr != null && Mathf.Abs(_moveInput) > 0.01f)
                _sr.flipX = _moveInput < 0f;
        }

        /// <summary>
        /// 出传送门后的回正：从出口角按曲线平滑转回 0°（竖直）。
        /// 只旋转显示子节点，物理根节点和胶囊碰撞体始终不动。
        /// </summary>
        void AdvancePortalRealign()
        {
            if (!_isRealigning) return;

            // 速度决定基础时长；下坠越快、触地时，时间推进倍率越高。
            float duration = Mathf.Abs(_realignStartAngle) /
                             Mathf.Max(0.01f, portalRealignSpeed);
            float speedMultiplier = GetRealignSpeedMultiplier();
            _realignElapsed += Time.deltaTime * speedMultiplier;
            float t = duration > 0.0001f
                ? Mathf.Clamp01(_realignElapsed / duration)
                : 1f;
            float eased = portalRealignCurve != null ? portalRealignCurve.Evaluate(t) : t;
            float angle = Mathf.LerpAngle(_realignStartAngle, 0f, eased);
            ApplyRotation(angle);

            if (t >= 1f)
            {
                _isRealigning = false;
                ApplyRotation(0f);
            }
        }

        /// <summary>
        /// 下坠速度越大，回正越快；触地时再乘一个更大的倍率。
        /// 返回值最小为 1，不会比配置的基础速度更慢。
        /// </summary>
        float GetRealignSpeedMultiplier()
        {
            float fallSpeed = _rb != null ? Mathf.Max(0f, -_rb.velocity.y) : 0f;
            float fall01 = Mathf.Clamp01(
                fallSpeed / Mathf.Max(0.01f, portalRealignFallSpeedForMaxBoost));
            float fallMultiplier = Mathf.Lerp(
                1f, Mathf.Max(1f, portalRealignMaxFallSpeedMultiplier), fall01);

            if (IsGrounded)
                return Mathf.Max(fallMultiplier, portalRealignGroundedSpeedMultiplier);

            return fallMultiplier;
        }

        /// <summary>只旋转玩家显示子节点；物理 Rigidbody2D 和碰撞体保持不变。</summary>
        void ApplyRotation(float angle)
        {
            if (visualRoot == null) return;
            visualRoot.localRotation = Quaternion.Euler(0f, 0f, angle);
        }

        #endregion

        #region 输入与固定步主循环

        /// <summary>外部程序接管移动输入（传 0 表示停下并保持接管）。</summary>
        public bool IsManualInput => _manualInput;

        public void SetMoveInput(float horizontal)
        {
            _manualInput = true;
            ApplyMoveInput(horizontal);
        }

        /// <summary>总控提交本帧设备输入；程序接管期间不覆盖外部命令。</summary>
        internal void SubmitDeviceInput(float horizontal, bool jumpDown, bool jumpUp)
        {
            if (_manualInput) return;

            ApplyMoveInput(horizontal);
            if (jumpDown) JumpPressed();
            if (jumpUp) JumpReleased();
        }

        /// <summary>总控停用时清理设备状态，不影响外部程序接管。</summary>
        internal void ClearDeviceInput()
        {
            if (_manualInput) return;

            ApplyMoveInput(0f);
            _jumpBufferCounter = 0f;
            JumpReleased();
        }

        void ApplyMoveInput(float horizontal)
        {
            if (invertHorizontalInput)
                horizontal = -horizontal;
            _moveInput = Mathf.Clamp(horizontal, -1f, 1f);
        }

        /// <summary>设置是否反转水平输入。</summary>
        public void SetHorizontalInputInverted(bool inverted)
        {
            if (invertHorizontalInput == inverted) return;

            invertHorizontalInput = inverted;

            // 程序输入立即翻转；设备输入由总控下一次提交时应用新方向。
            if (_manualInput)
                _moveInput = -_moveInput;
        }

        /// <summary>清除程序接管并停下，下一帧恢复总控设备输入。</summary>
        public void ClearManualInput()
        {
            _manualInput = false;
            ApplyMoveInput(0f);
        }

        /// <summary>缓存一次跳跃输入，并记录跳跃键仍处于按住状态。</summary>
        public void JumpPressed()
        {
            _jumpBufferCounter = jumpBufferTime;
            _jumpHeld = true;
            _jumpReleasePending = false;
        }

        /// <summary>记录松开事件；在下一物理步结束维持上升，不立即改写速度。</summary>
        public void JumpReleased()
        {
            _jumpHeld = false;
            _jumpReleasePending = true;
        }

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            TickTimers(dt);
            ProbeContacts();
            TickContactTimers(dt);

            // 起跳优先于接触交接，且每个物理步只有一个动作系统写运动。
            if (!TryConsumeBufferedJump())
                ResolveActionSystem();

            _activeActions.Tick(dt);
            RespawnIfFallen();

            // 记录本物理步的速度，碰撞回调里用它恢复进门瞬间的速度。
            VelocityBeforePhysics = _rb.velocity;
        }

        float EffectiveGravity => Mathf.Abs(Physics2D.gravity.y * _rb.gravityScale);

        float CurrentHorizontalInput => _forcedMoveTimer > 0f
            ? _forcedMoveDirection
            : _moveInput;

        void TickTimers(float dt)
        {
            if (_coyoteCounter > 0f) _coyoteCounter -= dt;
            if (_jumpBufferCounter > 0f) _jumpBufferCounter -= dt;

            if (_forcedMoveTimer > 0f)
            {
                _forcedMoveTimer -= dt;
                if (_forcedMoveTimer <= 0f) _forcedMoveDirection = 0;
            }
        }

        #endregion

        #region 地面、墙面与动量缓存状态

        void TickContactTimers(float dt)
        {
            // 恢复标记只存在一个物理步；跳跃可在 Physics 消费它之前将其取消。
            _hasPendingWallMomentum = false;

            if (IsGrounded)
            {
                if (_groundContactTime <= 0f)
                {
                    // 只按落地瞬间的世界 X 锁存；Y 和后续速度变化不改变本次交接方式。
                    _delayLandingHandoff = IsPhysicsActionActive &&
                        Mathf.Abs(_rb.velocity.x) > inputSpeedLimit;
                }
                _groundContactTime += dt;

                // 地面交接优先，墙面缓存和墙滑淡出都不能跨落地继续生效。
                ClearWallMomentumCache();
                ResetWallSlideFade();
            }
            else
            {
                _groundContactTime = 0f;
                _delayLandingHandoff = false;
                TickWallMomentumCache(dt);
                TickWallSlideFade(dt);
            }

            _previousWallDirection = _wallDirection;
        }

        void TickWallMomentumCache(float dt)
        {
            if (_hasWallMomentumCache)
            {
                if (_wallDirection == 0)
                {
                    // 缓存期内真正离墙才恢复；使用赋值而不是叠加，Y 保持当前值。
                    _pendingWallMomentumX = _wallMomentumCacheX;
                    _hasPendingWallMomentum = true;
                    ClearWallMomentumCache();
                }
                else if (_wallDirection != _wallMomentumCacheDirection)
                {
                    ClearWallMomentumCache();
                }
                else
                {
                    _wallMomentumCacheTime += dt;
                    if (_wallMomentumCacheTime >= highSpeedContactRetentionTime)
                        ClearWallMomentumCache();
                }
            }

            if (_hasWallMomentumCache || _hasPendingWallMomentum ||
                _wallDirection == 0 || _wallDirection == _previousWallDirection)
                return;

            // BoxCast 发现墙时，碰撞解算可能已清零当前 X，因此使用撞墙前的速度。
            if (highSpeedContactRetentionTime <= 0f) return;

            float approachX = VelocityBeforePhysics.x;
            if (approachX * _wallDirection <= inputSpeedLimit) return;

            _hasWallMomentumCache = true;
            _wallMomentumCacheX = approachX;
            _wallMomentumCacheDirection = _wallDirection;
            _wallMomentumCacheTime = 0f;
        }

        void ClearWallMomentumCache()
        {
            _hasWallMomentumCache = false;
            _wallMomentumCacheX = 0f;
            _wallMomentumCacheDirection = 0;
            _wallMomentumCacheTime = 0f;
        }

        bool IsWallMomentumCacheActive => _hasWallMomentumCache &&
            _wallMomentumCacheDirection == _wallDirection;

        bool TryConsumePendingWallMomentum(out float restoredX)
        {
            restoredX = _pendingWallMomentumX;
            if (!_hasPendingWallMomentum) return false;

            _hasPendingWallMomentum = false;
            _pendingWallMomentumX = 0f;
            return true;
        }

        void TickWallSlideFade(float dt)
        {
            if (_wallSlideFadeRemaining <= 0f) return;

            bool invalid = _wallDirection == 0 ||
                           _wallDirection != _wallSlideFadeDirection ||
                           Mathf.Abs(CurrentHorizontalInput) > 0.01f ||
                           _rb.velocity.y > 0f;
            if (invalid)
            {
                ResetWallSlideFade();
                return;
            }

            _wallSlideFadeRemaining = Mathf.Max(0f, _wallSlideFadeRemaining - dt);
        }

        void ResetWallSlideFade()
        {
            _wallSlideFadeRemaining = 0f;
            _wallSlideFadeCap = 0f;
            _wallSlideFadeDirection = 0;
        }

        void ResetContactTimers()
        {
            _groundContactTime = 0f;
            _delayLandingHandoff = false;
            _previousWallDirection = 0;
            ClearWallMomentumCache();
            _hasPendingWallMomentum = false;
            _pendingWallMomentumX = 0f;
            ResetWallSlideFade();
        }

        #endregion

        #region 横向速度与墙滑纵向计算

        /// <summary>玩家墙滑与 Physics 松键淡出共用的 Y 修正，避免两套公式漂移。</summary>
        float CalculateWallSlideVerticalVelocity(float currentY, float cap, float dt)
        {
            float gravityStep = EffectiveGravity * dt;
            float desiredY = Mathf.MoveTowards(currentY, -cap, gravityStep);
            return desiredY + gravityStep;
        }

        /// <summary>玩家模块的地面/墙面直接横向输入计算，不接触 Y。</summary>
        float CalculatePlayerHorizontalSpeed(float currentX, float input, float dt)
        {
            float target = input * moveSpeed;
            float acceleration = Mathf.Abs(input) > 0.01f
                ? groundAcceleration
                : groundDeceleration;
            float candidateX = Mathf.MoveTowards(currentX, target, acceleration * dt);
            return LimitHorizontalInputAcceleration(currentX, candidateX);
        }

        /// <summary>物理模块的空中横向输入计算；同向超速时保留动量，不主动刹回跑速。</summary>
        float CalculateAirHorizontalSpeed(float currentX, float input, float authority, float dt)
        {
            if (Mathf.Abs(input) <= 0.01f || authority <= 0f) return currentX;

            float target = input * moveSpeed;
            bool sameDirectionOverSpeed = currentX * input > 0f &&
                Mathf.Abs(currentX) > Mathf.Abs(target);
            if (sameDirectionOverSpeed) return currentX;

            float candidateX = Mathf.MoveTowards(
                currentX, target, airAcceleration * authority * dt);
            return LimitHorizontalInputAcceleration(currentX, candidateX);
        }

        /// <summary>只拒绝 AD 在 24 之上继续增加 |X|；Y 不进入计算，减速和反向始终允许。</summary>
        float LimitHorizontalInputAcceleration(float currentX, float candidateX)
        {
            bool increasesSpeedAboveLimit =
                Mathf.Abs(candidateX) > inputSpeedLimit &&
                Mathf.Abs(candidateX) > Mathf.Abs(currentX);
            return increasesSpeedAboveLimit ? currentX : candidateX;
        }

        #endregion

        #region 地墙接触探测

        /// <summary>只更新共享接触信息；门层永远不能触发动作系统切换。</summary>
        void ProbeContacts()
        {
            if (IsPhysicsActionActive)
                _physicsActions.RefreshJumpPhase();

            int contactMask = groundMask & ~portalContactMask;
            Bounds b = _col.bounds;
            Vector2 groundProbeCenter = new Vector2(b.center.x, b.min.y + 0.05f);
            Vector2 groundProbeSize = new Vector2(b.size.x * 0.85f, 0.06f);
            RaycastHit2D groundHit = Physics2D.BoxCast(
                groundProbeCenter, groundProbeSize, 0f, Vector2.down,
                groundCheckDistance, contactMask);

            // 跳跃进行中时忽略仍接触的地面；爬坡产生的向上速度不受影响。
            IsGrounded = groundHit.collider != null &&
                         !(IsPhysicsActionActive && _physicsActions.IsJumping);
            if (IsGrounded)
            {
                _coyoteCounter = coyoteTime;
                _groundNormal = groundHit.normal;
            }
            else
            {
                _groundNormal = Vector2.up;
            }

            _wallDirection = 0;
            bool suppressWallMonitoring = IsPhysicsActionActive &&
                _physicsActions.IsWallMonitoringSuppressed;
            if (!suppressWallMonitoring)
            {
                if (ProbeWall(b, Vector2.right, contactMask)) _wallDirection = 1;
                else if (ProbeWall(b, Vector2.left, contactMask)) _wallDirection = -1;
            }
        }

        bool ProbeWall(Bounds bounds, Vector2 direction, int contactMask)
        {
            Vector2 probeSize = new Vector2(bounds.size.x * 0.7f, bounds.size.y * 0.7f);
            RaycastHit2D hit = Physics2D.BoxCast(
                bounds.center, probeSize, 0f, direction,
                wallCheckDistance, contactMask);

            // 初始重叠时 Unity 可能不给法线；有明确法线时只接受足够竖直的面。
            return hit.collider != null &&
                   (hit.normal.sqrMagnitude < 0.01f || Mathf.Abs(hit.normal.x) > 0.5f);
        }

        #endregion

        #region 动作系统交接与跳跃仲裁

        void SwitchActionSystem(ActionSystem next)
        {
            if (_activeActions == next) return;
            _activeActions.Exit();
            _activeActions = next;
            _activeActions.Enter();
        }

        bool IsPushingWall(int direction)
        {
            return direction != 0 && CurrentHorizontalInput * direction > 0.01f;
        }

        void StartWallSlideFadeIfNeeded()
        {
            // 只有同墙中性松键退出有效墙滑才淡出；反向、离墙和上升不启动。
            if (_wallDirection == 0 || Mathf.Abs(CurrentHorizontalInput) > 0.01f ||
                !_playerActions.IsWallSliding ||
                _playerActions.WallSlideDirection != _wallDirection || _rb.velocity.y > 0f)
                return;

            _wallSlideFadeRemaining = wallSlideReleaseFadeTime;
            _wallSlideFadeCap = _playerActions.CurrentWallSlideCap;
            _wallSlideFadeDirection = _wallDirection;
        }

        void ResolveActionSystem()
        {
            if (IsPhysicsActionActive)
            {
                if (IsGrounded)
                {
                    bool retainLandingMomentum = _delayLandingHandoff &&
                        _groundContactTime < highSpeedContactRetentionTime;
                    if (!retainLandingMomentum)
                        SwitchActionSystem(_playerActions);
                    return;
                }

                // 高速撞墙缓存优先于主观推墙；缓存到期后同一步即可交接。
                if (_hasWallMomentumCache) return;

                // 上升和下降都监听朝墙方向；不再要求持续推墙计时。
                if (IsPushingWall(_wallDirection))
                {
                    ResetWallSlideFade();
                    SwitchActionSystem(_playerActions);
                }
                return;
            }

            if (IsGrounded) return;

            // 即使传送后保留的是 Player，新高速撞墙也必须先进入 Physics 缓存。
            if (_hasWallMomentumCache)
            {
                ResetWallSlideFade();
                SwitchActionSystem(_physicsActions);
                return;
            }

            if (IsPushingWall(_wallDirection)) return;

            // Player 退出前捕获墙滑状态，随后 Exit 仍可正常清理自己的内部状态。
            StartWallSlideFadeIfNeeded();
            SwitchActionSystem(_physicsActions);
        }

        bool TryConsumeBufferedJump()
        {
            if (_jumpBufferCounter <= 0f) return false;

            bool hasWall = _wallDirection != 0;
            bool wallJumpAllowed = hasWall && _rb.velocity.y <= 0f;

            // 明确朝离墙方向跳时优先执行墙跳，不让土狼跳保留撞墙前的水平动量。
            bool jumpingAwayFromWall = wallJumpAllowed &&
                CurrentHorizontalInput * _wallDirection < -0.01f;

            // 其余情况仍保持地面/土狼跳优先；墙跳与普通跳共用同一次预输入。
            if (!jumpingAwayFromWall && (IsGrounded || _coyoteCounter > 0f))
            {
                float launchX = _rb.velocity.x + jumpHorizontalBoost * CurrentHorizontalInput;
                SwitchActionSystem(_physicsActions);
                _physicsActions.StartJump(launchX, jumpHeight);
                CompleteJumpRequest();
                return true;
            }

            if (!hasWall) return false;

            if (!wallJumpAllowed)
            {
                // 上升贴墙只监听方向键，本次按跳不留到下降后自动墙跳。
                _jumpBufferCounter = 0f;
                _jumpReleasePending = false;
                return false;
            }

            int jumpDirection = -_wallDirection;
            SwitchActionSystem(_physicsActions);
            // StartJump 会同时覆盖 X/Y 速度，墙跳不继承碰墙前的动量或缓存。
            _physicsActions.StartJump(
                jumpDirection * (moveSpeed + jumpHorizontalBoost), wallJumpHeight);
            CompleteJumpRequest();
            _forcedMoveDirection = jumpDirection;
            _forcedMoveTimer = wallJumpForceTime;
            return true;
        }

        void CompleteJumpRequest()
        {
            _jumpBufferCounter = 0f;
            _coyoteCounter = 0f;
            _wallDirection = 0;
            _playerActions.ResetWallSlide();
            IsGrounded = false;
            ResetContactTimers();
        }

        void ResetToPlayerActions()
        {
            _activeActions.Exit();
            _physicsActions.ResetMotion();
            _activeActions = _playerActions;
            _playerActions.Enter();
        }

        #endregion

        #region 双动作系统

        /// <summary>动作系统只在外层协调下独占一个物理步的运动写入。</summary>
        abstract class ActionSystem
        {
            protected readonly PlayerController2D Owner;
            protected Rigidbody2D Body => Owner._rb;

            protected ActionSystem(PlayerController2D owner)
            {
                Owner = owner;
            }

            public virtual void Enter() { }
            public virtual void Exit() { }
            public virtual void OnTeleport() { }
            public abstract void Tick(float dt);
        }

        /// <summary>玩家模块：地面移动，以及主观推墙后的直接水平控制和墙滑。</summary>
        sealed class PlayerActionSystem : ActionSystem
        {
            float _wallSlideTimer;

            public bool IsWallSliding { get; private set; }
            public bool IsContinuousWallSlide { get; private set; }
            public int WallSlideDirection { get; private set; }

            public float CurrentWallSlideCap
            {
                get
                {
                    float duration = Mathf.Max(0f, Owner.wallSlideTime);
                    float remaining = duration > 0f
                        ? Mathf.Clamp01(_wallSlideTimer / duration)
                        : 0f;
                    return Mathf.Lerp(
                        Owner.maxFallSpeed, Owner.wallSlideStartSpeed, remaining);
                }
            }

            public PlayerActionSystem(PlayerController2D owner) : base(owner)
            {
                ResetWallSlide();
            }

            public override void Enter()
            {
                ResetWallSlide();
            }

            public override void Exit()
            {
                ResetWallSlide();
            }

            public override void OnTeleport()
            {
                ResetWallSlide();
            }

            public void ResetWallSlide()
            {
                _wallSlideTimer = Owner.wallSlideTime;
                IsContinuousWallSlide = false;
                IsWallSliding = false;
                WallSlideDirection = 0;
            }

            public override void Tick(float dt)
            {
                float input = Owner.CurrentHorizontalInput;
                Vector2 currentVelocity = Body.velocity;
                float vx = Owner.CalculatePlayerHorizontalSpeed(currentVelocity.x, input, dt);

                float vy = currentVelocity.y;
                if (Owner.IsGrounded && Owner._groundNormal.y > 0.3f &&
                    Mathf.Abs(Owner._groundNormal.x) > 0.02f)
                {
                    float slopeRise = -Owner._groundNormal.x /
                                      Mathf.Max(0.0001f, Owner._groundNormal.y);
                    vy = vx * slopeRise;
                }
                else if (Owner.IsGrounded)
                {
                    vy = 0f;
                }

                Body.velocity = new Vector2(vx, vy);
                UpdateWallSlide(dt, input);
            }

            void UpdateWallSlide(float dt, float input)
            {
                bool pushing = !Owner.IsGrounded && Owner._wallDirection != 0 &&
                               Body.velocity.y <= 0f &&
                               input * Owner._wallDirection > 0.01f;
                if (!pushing)
                {
                    ResetWallSlide();
                    return;
                }

                if (!IsContinuousWallSlide)
                {
                    IsContinuousWallSlide = true;
                    _wallSlideTimer = Owner.wallSlideTime;
                }

                IsWallSliding = true;
                WallSlideDirection = Owner._wallDirection;
                float nextY = Owner.CalculateWallSlideVerticalVelocity(
                    Body.velocity.y, CurrentWallSlideCap, dt);
                Body.velocity = new Vector2(Body.velocity.x, nextY);
                if (Owner.wallSlideTime > 0f)
                    _wallSlideTimer = Mathf.Max(0f, _wallSlideTimer - dt);
            }
        }

        /// <summary>物理模块：空中运动、高速接触动量缓存，以及松键后的墙滑纵向淡出。</summary>
        sealed class PhysicsActionSystem : ActionSystem
        {
            float _airborneTime;
            bool _jumpAirControl;
            bool _isJumping;
            float _varJumpTimer;
            float _varJumpSpeed;
            Vector2 _varJumpDirection = Vector2.up;
            bool _wallMonitorSuppressionActive;
            float _wallMonitorSuppressRemaining;

            public bool IsJumping => _isJumping;
            public bool IsWallMonitoringSuppressed => _wallMonitorSuppressionActive &&
                _wallMonitorSuppressRemaining > 0f;
            public float RemainingVariableJumpTime => _varJumpTimer;
            public Vector2 VariableJumpDirection => _varJumpDirection;

            public float AirControlAuthority
            {
                get
                {
                    if (_airborneTime <= Owner.airControlFullTime) return 1f;
                    if (!_jumpAirControl) return 0f;

                    float endTime = Mathf.Max(
                        Owner.airControlFullTime, Owner.jumpAirControlEndTime);
                    if (endTime <= Owner.airControlFullTime) return 0f;
                    return 1f - Mathf.InverseLerp(
                        Owner.airControlFullTime, endTime, _airborneTime);
                }
            }

            public PhysicsActionSystem(PlayerController2D owner) : base(owner)
            {
                ResetMotion();
            }

            public override void Enter()
            {
                ResetMotion();
            }

            public override void Exit()
            {
                _isJumping = false;
                _varJumpTimer = 0f;
                _varJumpDirection = Vector2.up;
                _wallMonitorSuppressionActive = false;
                _wallMonitorSuppressRemaining = 0f;
            }

            public override void OnTeleport()
            {
                // 取消本次暂停的武装状态，后续 Refresh 不会在上升出口重新启动它。
                _wallMonitorSuppressionActive = false;
                _wallMonitorSuppressRemaining = 0f;
            }

            public void ResetMotion()
            {
                _airborneTime = 0f;
                _jumpAirControl = false;
                _isJumping = false;
                _varJumpTimer = 0f;
                _varJumpSpeed = 0f;
                _varJumpDirection = Vector2.up;
                _wallMonitorSuppressionActive = false;
                _wallMonitorSuppressRemaining = 0f;
            }

            public void RefreshJumpPhase()
            {
                if (!_isJumping)
                {
                    _wallMonitorSuppressionActive = false;
                    _wallMonitorSuppressRemaining = 0f;
                    return;
                }

                if (Body.velocity.y <= 0f)
                {
                    // 实际顶点优先于估算计时，确保下降前不会提前执行蹭墙逻辑。
                    _isJumping = false;
                    _wallMonitorSuppressionActive = false;
                    _wallMonitorSuppressRemaining = 0f;
                }
                else if (_wallMonitorSuppressionActive && _wallMonitorSuppressRemaining <= 0f)
                {
                    // 外部速度变化导致原计时不足时，仅对仍武装的暂停续算。
                    _wallMonitorSuppressRemaining = EstimateJumpApexTime();
                }
            }

            public void StartJump(float horizontalVelocity, float height)
            {
                _varJumpSpeed = CalculateJumpLaunchSpeed(height);
                _varJumpTimer = Owner._jumpHeld && !Owner._jumpReleasePending
                    ? Owner.variableJumpTime
                    : 0f;
                _varJumpDirection = Vector2.up;
                Body.velocity = new Vector2(horizontalVelocity, _varJumpSpeed);
                _isJumping = true;
                _wallMonitorSuppressionActive = true;
                _wallMonitorSuppressRemaining = EstimateJumpApexTime();
                _airborneTime = 0f;
                _jumpAirControl = true;
            }

            public void RestoreJumpDrive(float remainingTime, Vector2 direction)
            {
                _varJumpTimer = Mathf.Max(0f, remainingTime);
                if (direction.sqrMagnitude > 0.0001f)
                    _varJumpDirection = direction.normalized;
            }

            public override void Tick(float dt)
            {
                ConsumeJumpRelease();

                if (Owner.IsGrounded)
                {
                    // 只有高速落地延迟会让 Physics 在接地后运行；本段不写速度或施力。
                    _isJumping = false;
                    _varJumpTimer = 0f;
                    _varJumpDirection = Vector2.up;
                    return;
                }

                _wallMonitorSuppressRemaining = Mathf.Max(
                    0f, _wallMonitorSuppressRemaining - dt);
                RefreshJumpPhase();
                _airborneTime += dt;

                bool restoredWallMomentum = Owner.TryConsumePendingWallMomentum(
                    out float restoredWallMomentumX);
                if (restoredWallMomentum)
                {
                    // 单次赋值恢复撞墙前 X，保留离墙时的 Y，不允许重复恢复。
                    Body.velocity = new Vector2(restoredWallMomentumX, Body.velocity.y);
                }

                bool cachingWallMomentum = Owner.IsWallMomentumCacheActive;
                if (cachingWallMomentum)
                    Body.velocity = new Vector2(0f, Body.velocity.y);

                if (!restoredWallMomentum && !cachingWallMomentum)
                {
                    // 横向输入由外层纯计算方法求解，这里只写回 X。
                    float nextX = Owner.CalculateAirHorizontalSpeed(
                        Body.velocity.x, Owner.CurrentHorizontalInput, AirControlAuthority, dt);
                    Body.velocity = new Vector2(nextX, Body.velocity.y);
                }

                float gravity = Owner.EffectiveGravity;
                bool maintainingJump = false;
                if (_varJumpTimer > 0f)
                {
                    _varJumpTimer = Mathf.Max(0f, _varJumpTimer - dt);
                    if (Owner._jumpHeld)
                    {
                        maintainingJump = true;
                        Vector2 direction = _varJumpDirection.sqrMagnitude > 0.0001f
                            ? _varJumpDirection.normalized
                            : Vector2.up;
                        float maintainedSpeed = _varJumpSpeed + gravity * dt * direction.y;
                        float directionalSpeed = Vector2.Dot(Body.velocity, direction);
                        if (directionalSpeed < maintainedSpeed)
                            Body.velocity += direction * (maintainedSpeed - directionalSpeed);
                    }
                    else
                    {
                        _varJumpTimer = 0f;
                    }
                }

                // 普通推墙已在交接前切走；这里只保留高速缓存期内的物理纵向制动。
                if (cachingWallMomentum && Owner.IsPushingWall(Owner._wallDirection) &&
                    Body.velocity.y < -Owner.wallSlideStartSpeed)
                {
                    Body.AddForce(Vector2.up *
                                  (Owner.highSpeedWallBrakeAcceleration * Body.mass),
                                  ForceMode2D.Force);
                }

                // 只调整上升弧线；下落完全由 Unity 原生重力积分。
                if (!maintainingJump && Body.velocity.y > 0f)
                {
                    float gravityMultiplier = Owner._jumpHeld &&
                                              Owner.apexGravityMultiplier < 1f &&
                                              Body.velocity.y < Owner.apexVelocityThreshold
                        ? Owner.apexGravityMultiplier
                        : Owner.riseGravityMultiplier;
                    float correction = gravity * (1f - gravityMultiplier) * dt;
                    Body.velocity += new Vector2(0f, correction);
                }

                // 斜向跳跃驱动可能含 X 分量；缓存和恢复步的最终 X 只由接触状态决定。
                if (cachingWallMomentum)
                    Body.velocity = new Vector2(0f, Body.velocity.y);
                else if (restoredWallMomentum)
                    Body.velocity = new Vector2(restoredWallMomentumX, Body.velocity.y);

                ApplyWallSlideFade(dt);
            }

            void ConsumeJumpRelease()
            {
                if (!Owner._jumpReleasePending) return;

                // 松开只结束维持上升，不截断当前速度。
                _varJumpTimer = 0f;
                Owner._jumpReleasePending = false;
            }

            void ApplyWallSlideFade(float dt)
            {
                if (Owner._wallSlideFadeRemaining <= 0f) return;
                if (Owner.wallSlideReleaseFadeTime <= 0f)
                {
                    Owner.ResetWallSlideFade();
                    return;
                }

                float weight = Mathf.Clamp01(
                    Owner._wallSlideFadeRemaining / Owner.wallSlideReleaseFadeTime);
                float currentY = Body.velocity.y;
                float brakedY = Owner.CalculateWallSlideVerticalVelocity(
                    currentY, Owner._wallSlideFadeCap, dt);
                float nextY = Mathf.Lerp(currentY, brakedY, weight);
                Body.velocity = new Vector2(Body.velocity.x, nextY);
            }

            float EstimateJumpApexTime()
            {
                // 使用可能出现的最小上升重力估算时间上界，实际顶点仍由 Y 速度判定。
                float gravityMultiplier = Owner._jumpHeld
                    ? Mathf.Min(Owner.riseGravityMultiplier, Owner.apexGravityMultiplier)
                    : Owner.riseGravityMultiplier;
                float gravity = Mathf.Max(0.01f, Owner.EffectiveGravity * gravityMultiplier);
                float holdTime = Owner._jumpHeld ? _varJumpTimer : 0f;
                return holdTime + Mathf.Max(0f, Body.velocity.y) / gravity;
            }

            float CalculateJumpLaunchSpeed(float height)
            {
                float baseGravity = Owner.EffectiveGravity;
                if (baseGravity < 0.01f) baseGravity = 9.81f;

                float riseGravity = baseGravity * Mathf.Max(0.01f, Owner.riseGravityMultiplier);
                float apexGravity = baseGravity * Mathf.Max(0.01f, Owner.apexGravityMultiplier);
                float holdGravity = riseGravity * Mathf.Max(0f, Owner.variableJumpTime);
                float apexBonus = Owner.apexVelocityThreshold * Owner.apexVelocityThreshold *
                                  (riseGravity / apexGravity - 1f);
                float discriminant = holdGravity * holdGravity +
                                     2f * riseGravity * height - apexBonus;
                float launch = Mathf.Sqrt(Mathf.Max(0f, discriminant)) - holdGravity;

                return launch > 0.01f
                    ? launch
                    : Mathf.Sqrt(2f * riseGravity * height);
            }
        }

        #endregion

        #region 传送、重生与公开接口

        void RespawnIfFallen()
        {
            if (transform.position.y < fallLimitY)
                Respawn();
        }

        // ------------------------------------------------------------------
        // 公开 API（自动化 PlayMode 测试也会用到）
        // ------------------------------------------------------------------

        /// <summary>回到出生点并停止移动。</summary>
        public void Respawn()
        {
            TeleportCore(_spawnPoint);
            ResetToPlayerActions();
            _jumpBufferCounter = 0f;
            _jumpReleasePending = false;
            _coyoteCounter = 0f;
            _groundNormal = Vector2.up;
            IsGrounded = false;
            VelocityBeforePhysics = Vector2.zero;
            _isRealigning = false;
            _realignElapsed = 0f;
            ApplyRotation(0f);
            // 持续水平输入属于当前操作状态，重生时保留；跳跃缓冲和土狼时间不跨生命周期。
        }

        /// <summary>生命周期传送：停止运动、恢复竖直并重置为玩家动作系统。</summary>
        public void Teleport(Vector2 position)
        {
            TeleportCore(position);
            ResetToPlayerActions();
            _isRealigning = false;
            ApplyRotation(0f);
        }

        /// <summary>仅接收门映射后的出口速度，不改变动作系统或空中权限进度。</summary>
        public void SetPortalExitVelocity(Vector2 velocity)
        {
            if (_rb == null || !float.IsFinite(velocity.x) || !float.IsFinite(velocity.y)) return;

            _rb.velocity = velocity;
            VelocityBeforePhysics = velocity;
        }

        /// <summary>
        /// 传送门专用：位置瞬移后，把玩家旋转到指定的绝对角度；
        /// 之后按 portalRealignSpeed 和回正曲线平滑回正。
        /// 返回刚出传送门时的初始旋转角，供相机同步。
        /// </summary>
        public float TeleportWithPortalRotation(Vector2 position, float exitAngle)
        {
            return TeleportWithPortalRotation(
                position, exitAngle, VariableJumpDirection, true);
        }

        /// <summary>
        /// 传送门完整入口：保留剩余长按跳跃，并使用 PortalPair 变换后的推进方向。
        /// preserveJumpProgress 与 PortalPair.preserveVelocity 一致，关闭速度保留时也会取消旧跳跃。
        /// </summary>
        public float TeleportWithPortalRotation(
            Vector2 position,
            float exitAngle,
            Vector2 variableJumpDirection,
            bool preserveJumpProgress)
        {
            float remainingVarJump = preserveJumpProgress
                ? _physicsActions.RemainingVariableJumpTime
                : 0f;
            TeleportCore(position);
            _physicsActions.RestoreJumpDrive(
                remainingVarJump,
                preserveJumpProgress ? variableJumpDirection : Vector2.up);

            _realignStartAngle = Mathf.DeltaAngle(0f, exitAngle);
            _realignElapsed = 0f;
            _isRealigning = portalRealignSpeed > 0.0001f &&
                            Mathf.Abs(_realignStartAngle) > 0.01f;

            float appliedAngle = _isRealigning ? _realignStartAngle : 0f;
            ApplyRotation(appliedAngle);
            return appliedAngle;
        }

        /// <summary>只在传送瞬间水平翻转精灵，不改变玩家的旋转、缩放或物理空间。</summary>
        public void MirrorSpriteHorizontally()
        {
            if (_sr != null) _sr.flipX = !_sr.flipX;
        }

        void TeleportCore(Vector2 position)
        {
            unchecked { PositionRevision++; }
            _rb.velocity = Vector2.zero;
            VelocityBeforePhysics = Vector2.zero;
            _rb.position = position;
            transform.position = position;

            // 只清除旧位置状态；当前动作系统和物理空中进度不因门传送重置。
            _forcedMoveTimer = 0f;
            _forcedMoveDirection = 0;
            _wallDirection = 0;
            _groundNormal = Vector2.up;
            IsGrounded = false;
            ResetContactTimers();
            _playerActions.ResetWallSlide();
            _activeActions.OnTeleport();
            Physics2D.SyncTransforms();
        }

        /// <summary>重设出生点（进入新区域后很有用）。</summary>
        public void SetSpawnPoint(Vector3 worldPosition)
        {
            _spawnPoint = worldPosition;
        }

        #endregion

        #region 场景辅助线

        void OnDrawGizmosSelected()
        {
            float half = (playerHeight > 0f ? playerHeight : 1.5f) * 0.5f;
            Vector3 feet = transform.position + Vector3.down * half;
            Vector3 apex = transform.position + Vector3.up * (jumpHeight - half);

            Gizmos.color = new Color(0.3f, 0.9f, 0.4f, 0.9f);
            Gizmos.DrawLine(feet, apex);
            Gizmos.DrawWireSphere(apex, 0.12f);
            Gizmos.DrawLine(feet, feet + Vector3.down * groundCheckDistance);

            // 玩家中心的传送门碰撞点。
            Gizmos.color = new Color(1f, 0.5f, 0.2f, 0.9f);
            Gizmos.DrawWireSphere(transform.position, centerPointRadius);
        }

        #endregion
    }
}
