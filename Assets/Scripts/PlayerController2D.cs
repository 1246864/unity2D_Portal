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
        public float groundAcceleration = 115f;
        [Tooltip("地面上松开方向时的减速度")]
        public float groundDeceleration = 150f;
        [Tooltip("空中按住方向时的加速度")]
        public float airAcceleration = 70f;
        [Tooltip("空中松开方向时的减速度")]
        public float airDeceleration = 45f;

        [Header("跳跃")]
        [Tooltip("最大跳跃高度（世界单位，等于格）")]
        public float jumpHeight = 6f;
        [Tooltip("角色高度（格），用于地面探测和 Gizmo")]
        public float playerHeight = 1.5f;
        [Tooltip("离开地面后仍可起跳的宽容时间")]
        public float coyoteTime = 0.1f;
        [Tooltip("落地前提前按跳的缓冲时间")]
        public float jumpBufferTime = 0.12f;
        [Tooltip("下落时额外的重力倍率，让弧线更利落")]
        [Range(1f, 3f)] public float fallGravityMultiplier = 1.6f;
        [Tooltip("最大下落速度（单位/秒）")]
        public float maxFallSpeed = 30f;
        [Tooltip("提前松开跳跃键时保留的垂直速度比例")]
        [Range(0f, 1f)] public float jumpCutMultiplier = 0.45f;

        [Header("地面检测")]
        [Tooltip("算作可站立地面的层")]
        public LayerMask groundMask = 1 << 6;
        [Tooltip("地面探测向下延伸的距离")]
        public float groundCheckDistance = 0.15f;

        [Header("重生")]
        [Tooltip("低于该 Y 坐标时重生")]
        public float fallLimitY = -12f;

        [Header("传送门碰撞点")]
        [Tooltip("玩家中心碰撞点的半径（世界单位），用于和传送门的薄碰撞箱做连续碰撞")]
        [Min(0.01f)] public float centerPointRadius = 0.08f;

        Rigidbody2D _rb;
        CapsuleCollider2D _col;
        SpriteRenderer _sr;
        CircleCollider2D _centerPoint;

        float _moveInput;
        bool _manualInput;
        bool _isJumping;
        float _coyoteCounter;
        float _jumpBufferCounter;
        Vector3 _spawnPoint;
        Vector2 _groundNormal = Vector2.up;

        /// <summary>角色站在（或紧贴）可站立地面时为 true。</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>当前刚体速度。</summary>
        public Vector2 Velocity => _rb != null ? _rb.velocity : Vector2.zero;

        /// <summary>当前应用的水平输入（-1 到 1）。</summary>
        public float MoveInput => _moveInput;

        /// <summary>玩家中心的碰撞点，传送门用它来检测是否撞到门。</summary>
        public Collider2D CenterPoint => _centerPoint;

        /// <summary>物理步开始前的速度，传送碰撞回调里用它恢复进门瞬间的速度。</summary>
        public Vector2 VelocityBeforePhysics { get; private set; }

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<CapsuleCollider2D>();
            _sr = GetComponentInChildren<SpriteRenderer>();
            _spawnPoint = transform.position;

            // 传送门的碰撞箱很薄，用连续碰撞避免高速时直接穿过去。
            _rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            CreateCenterPoint();
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
            if (!_manualInput)
            {
                _moveInput = Input.GetAxisRaw("Horizontal");
                if (Input.GetButtonDown("Jump")) JumpPressed();
                if (Input.GetButtonUp("Jump")) JumpReleased();
            }

            if (_sr != null && Mathf.Abs(_moveInput) > 0.01f)
                _sr.flipX = _moveInput < 0f;
        }

        void FixedUpdate()
        {
            if (_coyoteCounter > 0f) _coyoteCounter -= Time.fixedDeltaTime;
            if (_jumpBufferCounter > 0f) _jumpBufferCounter -= Time.fixedDeltaTime;

            UpdateGrounded();
            ApplyHorizontalMovement();

            if (_jumpBufferCounter > 0f && _coyoteCounter > 0f)
                DoJump();

            ApplyExtraFallGravity();
            ClampFallSpeed();
            RespawnIfFallen();

            // 记录本物理步的速度，碰撞回调里用它恢复进门瞬间的速度。
            VelocityBeforePhysics = _rb.velocity;
        }

        void UpdateGrounded()
        {
            if (_isJumping && _rb.velocity.y <= 0f)
                _isJumping = false;

            Bounds b = _col.bounds;
            Vector2 probeCenter = new Vector2(b.center.x, b.min.y + 0.05f);
            Vector2 probeSize = new Vector2(b.size.x * 0.85f, 0.06f);

            RaycastHit2D hit = Physics2D.BoxCast(
                probeCenter, probeSize, 0f, Vector2.down,
                groundCheckDistance, groundMask);

            // 跳跃进行中时，即使探测仍然接触地面也不判定为接地：
            // 这样爬坡（会产生向上速度）仍算接地，而真正的跳跃不会在最初
            // 几帧被重新判定为接地。
            IsGrounded = hit.collider != null && !_isJumping;

            if (IsGrounded)
            {
                _coyoteCounter = coyoteTime;
                _groundNormal = hit.normal;
            }
            else
            {
                _groundNormal = Vector2.up;
            }
        }

        void ApplyHorizontalMovement()
        {
            float target = _moveInput * moveSpeed;
            bool hasInput = Mathf.Abs(target) > 0.01f;

            float accel;
            if (IsGrounded) accel = hasInput ? groundAcceleration : groundDeceleration;
            else accel = hasInput ? airAcceleration : airDeceleration;

            float vx = Mathf.MoveTowards(_rb.velocity.x, target, accel * Time.fixedDeltaTime);
            float vy = _rb.velocity.y;

            // 贴合地面：在斜坡上把水平速度换算成沿坡的垂直速度，让胶囊
            // 沿坡走上/走下，而不是依赖摩擦力；在平地上则把垂直速度归零，
            // 避免爬坡时的速度被带到下一块平台上。
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

        void DoJump()
        {
            float gravity = Mathf.Abs(Physics2D.gravity.y * _rb.gravityScale);
            if (gravity < 0.01f) gravity = 9.81f;

            // 加上半帧重力补偿，抵消离散积分带来的误差，
            // 使实测顶点高度尽量贴近 jumpHeight。
            float launchVelocity = Mathf.Sqrt(2f * gravity * jumpHeight)
                                   + 0.5f * gravity * Time.fixedDeltaTime;

            _rb.velocity = new Vector2(_rb.velocity.x, launchVelocity);
            _jumpBufferCounter = 0f;
            _coyoteCounter = 0f;
            _isJumping = true;
            IsGrounded = false;
        }

        void ApplyExtraFallGravity()
        {
            if (_rb.velocity.y < 0f && fallGravityMultiplier > 1f)
            {
                float extra = Physics2D.gravity.y * (fallGravityMultiplier - 1f) * Time.fixedDeltaTime;
                _rb.velocity += new Vector2(0f, extra);
            }
        }

        void ClampFallSpeed()
        {
            if (_rb.velocity.y < -maxFallSpeed)
                _rb.velocity = new Vector2(_rb.velocity.x, -maxFallSpeed);
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
            _rb.velocity = Vector2.zero;
            transform.position = _spawnPoint;
            _isJumping = false;
        }

        /// <summary>把刚体瞬移到指定位置并停止。</summary>
        public void Teleport(Vector2 position)
        {
            _rb.velocity = Vector2.zero;
            _rb.position = position;
            transform.position = position;
            Physics2D.SyncTransforms();
        }

        /// <summary>重设出生点（进入新区域后很有用）。</summary>
        public void SetSpawnPoint(Vector3 worldPosition)
        {
            _spawnPoint = worldPosition;
        }

        /// <summary>接管键盘输入（传 0 表示停下）。</summary>
        public void SetMoveInput(float horizontal)
        {
            _manualInput = true;
            _moveInput = Mathf.Clamp(horizontal, -1f, 1f);
        }

        /// <summary>把控制权交还给键盘。</summary>
        public void ClearManualInput()
        {
            _manualInput = false;
        }

        /// <summary>缓存一次跳跃输入（等价于按下跳跃键）。</summary>
        public void JumpPressed()
        {
            _jumpBufferCounter = jumpBufferTime;
        }

        /// <summary>应用可变跳高截断（等价于松开跳跃键）。</summary>
        public void JumpReleased()
        {
            if (_rb != null && _rb.velocity.y > 0f)
                _rb.velocity = new Vector2(_rb.velocity.x, _rb.velocity.y * jumpCutMultiplier);
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
