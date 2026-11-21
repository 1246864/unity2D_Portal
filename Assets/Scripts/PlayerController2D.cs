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
        [Header("水平移动")]
        [Tooltip("最高跑速（单位/秒）")]
        public float moveSpeed = 9f;
        [Tooltip("地面上按住方向时的加速度")]
        public float groundAcceleration = 100f;
        [Tooltip("地面上松开方向时的减速度")]
        public float groundDeceleration = 100f;
        [Tooltip("空中按住方向时的加速度")]
        public float airAcceleration = 65f;
        [Tooltip("空中松开方向时的减速度")]
        public float airDeceleration = 65f;
        [Tooltip("超过最高跑速且仍同向输入时，在地面使用的衰减；空中为它的 65%")]
        public float overSpeedDeceleration = 40f;

        [Header("输入方向")]
        [Tooltip("反转水平输入；开启后按右会向左走。")]
        public bool invertHorizontalInput;

        public bool IsHorizontalInputInverted => invertHorizontalInput;

        [Header("跳跃")]
        [Tooltip("按住跳跃时的最大跳跃高度（世界单位，等于格）")]
        public float jumpHeight = 6.2f;
        [Tooltip("角色高度（格），用于地面探测和场景辅助线")]
        public float playerHeight = 1.5f;
        [Tooltip("离开地面后仍可起跳的宽容时间")]
        public float coyoteTime = 0.1f;
        [Tooltip("落地前提前按跳的缓冲时间")]
        public float jumpBufferTime = 0.12f;
        [Tooltip("起跳后按住跳跃可维持上升的时间（秒）；提前松开会结束维持，已产生的短跳不会消失")]
        [Min(0f)] public float variableJumpTime = 0.2f;
        [Tooltip("上升阶段的基础重力倍率；调低会减缓上升速度变化并降低所需起跳初速度，满跳高度仍由跳跃高度约束")]
        [Range(0.1f, 2f)] public float riseGravityMultiplier = 0.85f;
        [Tooltip("起跳时沿当前输入方向增加的水平速度（单位/秒）；墙跳也使用它计算离墙速度")]
        [Min(0f)] public float jumpHorizontalBoost = 4f;
        [Tooltip("垂直速度绝对值低于此值时进入顶点低重力区（单位/秒）；需按住跳跃才生效")]
        [Min(0f)] public float apexVelocityThreshold = 8.5f;
        [Tooltip("顶点阶段直接乘在基础重力上的倍率，优先于上升/下落倍率；越小顶点越柔和")]
        [Range(0.1f, 1f)] public float apexGravityMultiplier = 0.5f;
        [Tooltip("下落阶段的基础重力倍率；调低会减缓下落加速，不改变上升和满跳高度")]
        [Range(0.1f, 2f)] public float fallGravityMultiplier = 0.75f;
        [Tooltip("世界 Y 轴最大下落速度（单位/秒）；贴墙下落的上限也会随此值恢复")]
        [Min(0f)] public float maxFallSpeed = 24f;

        [Header("墙面")]
        [Tooltip("向两侧探测墙面的距离")]
        [Min(0.01f)] public float wallCheckDistance = 0.12f;
        [Tooltip("墙跳持续按住时的目标高度（格）；低于普通满跳，使返回同一面墙时高度自然下降")]
        [Min(0.1f)] public float wallJumpHeight = 4.8f;
        [Tooltip("刚开始贴墙下落时的下落速度上限")]
        [Min(0f)] public float wallSlideStartSpeed = 4f;
        [Tooltip("持续贴墙下落时，速度上限恢复到最大下落速度所需的时间")]
        [Min(0f)] public float wallSlideTime = 1.2f;
        [Tooltip("墙跳后保持背离墙面输入的时间")]
        [Min(0f)] public float wallJumpForceTime = 0.24f;

        [Header("地面检测")]
        [Tooltip("算作可站立地面和墙面的层")]
        public LayerMask groundMask = 1 << 6;
        [Tooltip("地面探测向下延伸的距离")]
        public float groundCheckDistance = 0.15f;

        [Header("重生")]
        [Tooltip("低于该 Y 坐标时重生")]
        public float fallLimitY = -12f;

        [Header("传送门碰撞点")]
        [Tooltip("玩家中心碰撞点的半径（世界单位），用于和传送门的薄碰撞箱做连续碰撞")]
        [Min(0.01f)] public float centerPointRadius = 0.08f;

        [Header("显示")]
        [Tooltip("玩家精灵的父节点；留空时运行时自动创建，物理根节点只保留碰撞")]
        public Transform visualRoot;

        [Header("传送门旋转（玩家管理）")]
        [Tooltip("回正角速度（度/秒）。越大回正越快")]
        [Min(0.01f)] public float portalRealignSpeed = 80f;

        [Tooltip("回正曲线：横轴 0~1 为回正进度，纵轴 0~1 为角度插值权重")]
        public AnimationCurve portalRealignCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("传送门回正动态加速")]
        [Tooltip("下坠速度达到该值时，下坠加速达到最大值（世界单位/秒）")]
        [Min(0.01f)] public float portalRealignFallSpeedForMaxBoost = 12f;

        [Tooltip("下坠速度达到最大值时，回正速度倍率")]
        [Min(1f)] public float portalRealignMaxFallSpeedMultiplier = 3f;

        [Tooltip("玩家触地时的回正速度倍率")]
        [Min(1f)] public float portalRealignGroundedSpeedMultiplier = 6f;

        Rigidbody2D _rb;
        CapsuleCollider2D _col;
        SpriteRenderer _sr;
        CircleCollider2D _centerPoint;

        float _moveInput;
        bool _manualInput;
        bool _jumpHeld;
        bool _jumpReleasePending;
        bool _isJumping;
        float _coyoteCounter;
        float _jumpBufferCounter;
        float _varJumpTimer;
        float _varJumpSpeed;
        Vector2 _varJumpDirection = Vector2.up;
        int _wallDirection;
        float _wallSlideTimer;
        int _forcedMoveDirection;
        float _forcedMoveTimer;
        Vector3 _spawnPoint;
        Vector2 _groundNormal = Vector2.up;

        bool _isRealigning;
        float _realignElapsed;
        float _realignStartAngle;

        /// <summary>角色站在（或紧贴）可站立地面时为 true。</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>正在按向墙面并贴墙下落时为 true；不包含抓墙或攀爬。</summary>
        public bool IsWallSliding { get; private set; }

        /// <summary>当前刚体速度。</summary>
        public Vector2 Velocity => _rb != null ? _rb.velocity : Vector2.zero;

        /// <summary>剩余长按跳跃的推进方向；传送门按同一法线/切线规则变换它。</summary>
        public Vector2 VariableJumpDirection => _varJumpDirection;

        /// <summary>当前应用的水平输入（-1 到 1）。</summary>
        public float MoveInput => _moveInput;

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

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<CapsuleCollider2D>();
            _spawnPoint = transform.position;
            _wallSlideTimer = wallSlideTime;

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

        void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            TickTimers(dt);
            ProbeContacts();
            ApplyHorizontalMovement(dt);
            TryConsumeBufferedJump();
            ApplyVerticalMotion(dt);
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
            if (_varJumpTimer > 0f) _varJumpTimer -= dt;

            if (_forcedMoveTimer > 0f)
            {
                _forcedMoveTimer -= dt;
                if (_forcedMoveTimer <= 0f) _forcedMoveDirection = 0;
            }
        }

        /// <summary>只更新接触信息，不在探测阶段修改刚体速度。</summary>
        void ProbeContacts()
        {
            if (_isJumping && _rb.velocity.y <= 0f)
                _isJumping = false;

            Bounds b = _col.bounds;
            Vector2 groundProbeCenter = new Vector2(b.center.x, b.min.y + 0.05f);
            Vector2 groundProbeSize = new Vector2(b.size.x * 0.85f, 0.06f);
            RaycastHit2D groundHit = Physics2D.BoxCast(
                groundProbeCenter, groundProbeSize, 0f, Vector2.down,
                groundCheckDistance, groundMask);

            // 跳跃进行中时忽略仍接触的地面；爬坡产生的向上速度不受影响。
            IsGrounded = groundHit.collider != null && !_isJumping;
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
            if (ProbeWall(b, Vector2.right)) _wallDirection = 1;
            else if (ProbeWall(b, Vector2.left)) _wallDirection = -1;
        }

        bool ProbeWall(Bounds bounds, Vector2 direction)
        {
            Vector2 probeSize = new Vector2(bounds.size.x * 0.7f, bounds.size.y * 0.7f);
            RaycastHit2D hit = Physics2D.BoxCast(
                bounds.center, probeSize, 0f, direction,
                wallCheckDistance, groundMask);

            // 初始重叠时 Unity 可能不给法线；有明确法线时只接受足够竖直的面。
            return hit.collider != null &&
                   (hit.normal.sqrMagnitude < 0.01f || Mathf.Abs(hit.normal.x) > 0.5f);
        }

        void ApplyHorizontalMovement(float dt)
        {
            float input = CurrentHorizontalInput;
            float target = input * moveSpeed;
            bool hasInput = Mathf.Abs(input) > 0.01f;
            float currentX = _rb.velocity.x;

            bool reducingOverSpeed = hasInput &&
                                     Mathf.Abs(currentX) > moveSpeed &&
                                     Mathf.Sign(currentX) == Mathf.Sign(input);
            float accel;
            if (reducingOverSpeed)
            {
                // Celeste 的 RunReduce：同向超速时慢慢回落，而不是立即压到跑速。
                accel = overSpeedDeceleration * (IsGrounded ? 1f : 0.65f);
            }
            else if (IsGrounded)
            {
                accel = hasInput ? groundAcceleration : groundDeceleration;
            }
            else
            {
                accel = hasInput ? airAcceleration : airDeceleration;
            }

            float vx = Mathf.MoveTowards(currentX, target, accel * dt);
            float vy = _rb.velocity.y;

            // 在斜坡上把水平速度换算成沿坡的垂直速度，不依赖材质摩擦。
            if (IsGrounded && _groundNormal.y > 0.3f && Mathf.Abs(_groundNormal.x) > 0.02f)
            {
                float slopeRise = -_groundNormal.x / Mathf.Max(0.0001f, _groundNormal.y);
                vy = vx * slopeRise;
            }
            else if (IsGrounded)
            {
                vy = 0f;
            }

            _rb.velocity = new Vector2(vx, vy);
        }

        void TryConsumeBufferedJump()
        {
            if (_jumpBufferCounter <= 0f) return;

            // 地面/土狼跳优先；墙跳与普通跳共用同一次预输入。
            if (IsGrounded || _coyoteCounter > 0f)
            {
                float launchX = _rb.velocity.x + jumpHorizontalBoost * CurrentHorizontalInput;
                StartJump(launchX, jumpHeight);
                return;
            }

            if (_wallDirection == 0) return;

            int jumpDirection = -_wallDirection;
            StartJump(jumpDirection * (moveSpeed + jumpHorizontalBoost), wallJumpHeight);
            _forcedMoveDirection = jumpDirection;
            _forcedMoveTimer = wallJumpForceTime;
        }

        void StartJump(float horizontalVelocity, float height)
        {
            _varJumpSpeed = CalculateJumpLaunchSpeed(height);
            _varJumpTimer = _jumpHeld && !_jumpReleasePending ? variableJumpTime : 0f;
            _varJumpDirection = Vector2.up;
            _rb.velocity = new Vector2(horizontalVelocity, _varJumpSpeed);

            _jumpBufferCounter = 0f;
            _coyoteCounter = 0f;
            _wallSlideTimer = wallSlideTime;
            _wallDirection = 0;
            IsWallSliding = false;
            _isJumping = true;
            IsGrounded = false;
        }

        float CalculateJumpLaunchSpeed(float height)
        {
            float baseGravity = EffectiveGravity;
            if (baseGravity < 0.01f) baseGravity = 9.81f;

            float riseGravity = baseGravity * Mathf.Max(0.01f, riseGravityMultiplier);
            float apexGravity = baseGravity * Mathf.Max(0.01f, apexGravityMultiplier);

            // 满跳高度 = 维持上升阶段 + 上升重力阶段 + 顶点低重力阶段。
            // 普通跳与墙跳共用弧线规则，但分别使用各自的目标高度。
            float holdGravity = riseGravity * Mathf.Max(0f, variableJumpTime);
            float apexBonus = apexVelocityThreshold * apexVelocityThreshold *
                              (riseGravity / apexGravity - 1f);
            float discriminant = holdGravity * holdGravity +
                                 2f * riseGravity * height - apexBonus;
            float launch = Mathf.Sqrt(Mathf.Max(0f, discriminant)) - holdGravity;

            return launch > 0.01f
                ? launch
                : Mathf.Sqrt(2f * riseGravity * height);
        }

        void ApplyVerticalMotion(float dt)
        {
            if (_jumpReleasePending)
            {
                // 松开只结束维持上升和顶点半重力，不直接截断当前速度。
                _varJumpTimer = 0f;
                _jumpReleasePending = false;
            }

            if (IsGrounded)
            {
                IsWallSliding = false;
                _wallSlideTimer = wallSlideTime;
                return;
            }

            float gravity = EffectiveGravity;
            bool maintainingJump = false;
            if (_varJumpTimer > 0f)
            {
                if (_jumpHeld)
                {
                    maintainingJump = true;
                    // 沿传送后的跳跃方向维持速度，不改变与其垂直的出口动量。
                    Vector2 direction = _varJumpDirection.sqrMagnitude > 0.0001f
                        ? _varJumpDirection.normalized
                        : Vector2.up;
                    // 只补偿 Unity 世界重力在推进方向上的投影，斜向/水平跳跃不会额外加速。
                    float maintainedSpeed = _varJumpSpeed + gravity * dt * direction.y;
                    float directionalSpeed = Vector2.Dot(_rb.velocity, direction);
                    if (directionalSpeed < maintainedSpeed)
                        _rb.velocity += direction * (maintainedSpeed - directionalSpeed);
                }
                else
                {
                    _varJumpTimer = 0f;
                }
            }

            UpdateWallSlide(dt, gravity);

            if (!maintainingJump && !IsWallSliding)
            {
                // 顶点倍率直接乘基础重力；否则按上升/下落阶段选择独立倍率。
                float gravityMultiplier;
                if (_jumpHeld && apexGravityMultiplier < 1f &&
                    Mathf.Abs(_rb.velocity.y) < apexVelocityThreshold)
                {
                    gravityMultiplier = apexGravityMultiplier;
                }
                else
                {
                    gravityMultiplier = _rb.velocity.y > 0f
                        ? riseGravityMultiplier
                        : fallGravityMultiplier;
                }

                // 抵消 Unity 随后自动施加的重力差值，得到本物理步的目标重力。
                float correction = gravity * (1f - gravityMultiplier) * dt;
                _rb.velocity += new Vector2(0f, correction);
            }

            ClampFallSpeed(gravity, dt);
        }

        void UpdateWallSlide(float dt, float gravity)
        {
            float input = CurrentHorizontalInput;
            IsWallSliding = _wallDirection != 0 && _rb.velocity.y <= 0f &&
                            input * _wallDirection > 0.01f;
            if (!IsWallSliding) return;

            float duration = Mathf.Max(0f, wallSlideTime);
            float remaining = duration > 0f
                ? Mathf.Clamp01(_wallSlideTimer / duration)
                : 0f;
            float cap = Mathf.Lerp(maxFallSpeed, wallSlideStartSpeed, remaining);

            // 以当前重力接近墙面限速，并抵消随后自动施加的一帧基础重力。
            float desiredY = Mathf.MoveTowards(_rb.velocity.y, -cap, gravity * dt);
            _rb.velocity = new Vector2(_rb.velocity.x, desiredY + gravity * dt);
            if (duration > 0f) _wallSlideTimer = Mathf.Max(0f, _wallSlideTimer - dt);
        }

        void ClampFallSpeed(float gravity, float dt)
        {
            // 限制经过本帧 Unity 自动重力后的速度，避免每帧都越过上限一小段。
            float minimumBeforePhysics = -maxFallSpeed + gravity * dt;
            if (_rb.velocity.y < minimumBeforePhysics)
                _rb.velocity = new Vector2(_rb.velocity.x, minimumBeforePhysics);
        }

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
            _isJumping = false;
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

        /// <summary>把刚体瞬移到指定位置并停止，同时恢复竖直。</summary>
        public void Teleport(Vector2 position)
        {
            TeleportCore(position);
            _isRealigning = false;
            ApplyRotation(0f);
        }

        /// <summary>
        /// 传送门专用：位置瞬移后，把玩家旋转到指定的绝对角度；
        /// 之后按 portalRealignSpeed 和回正曲线平滑回正。
        /// 返回刚出传送门时的初始旋转角，供相机同步。
        /// </summary>
        public float TeleportWithPortalRotation(Vector2 position, float exitAngle)
        {
            return TeleportWithPortalRotation(
                position, exitAngle, _varJumpDirection, true);
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
            float remainingVarJump = preserveJumpProgress ? _varJumpTimer : 0f;
            TeleportCore(position);
            _varJumpTimer = remainingVarJump;
            if (preserveJumpProgress && variableJumpDirection.sqrMagnitude > 0.0001f)
                _varJumpDirection = variableJumpDirection.normalized;

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
            _rb.position = position;
            transform.position = position;

            // 旧位置的接触和墙面输入锁不能跨传送保留；出口速度仍由 PortalPair 写入。
            _varJumpTimer = 0f;
            _varJumpDirection = Vector2.up;
            _forcedMoveTimer = 0f;
            _forcedMoveDirection = 0;
            _wallDirection = 0;
            _wallSlideTimer = wallSlideTime;
            _groundNormal = Vector2.up;
            IsGrounded = false;
            IsWallSliding = false;
            Physics2D.SyncTransforms();
        }

        /// <summary>重设出生点（进入新区域后很有用）。</summary>
        public void SetSpawnPoint(Vector3 worldPosition)
        {
            _spawnPoint = worldPosition;
        }

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
    }
}
