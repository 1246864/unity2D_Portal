using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Minimal, tunable 2D platformer character controller.
    ///
    /// Jump height is expressed in world units (1 unit = 1 grid cell) and is
    /// independent from gravity: the launch velocity is derived from the
    /// current gravity every time the player jumps, so changing
    /// Physics2D.gravity keeps the jump height at <see cref="jumpHeight"/>.
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    [RequireComponent(typeof(CapsuleCollider2D))]
    public class PlayerController2D : MonoBehaviour
    {
        [Header("Horizontal Movement")]
        [Tooltip("Top run speed, in units per second.")]
        public float moveSpeed = 9f;
        [Tooltip("Acceleration while a direction is held on the ground.")]
        public float groundAcceleration = 115f;
        [Tooltip("Deceleration while no direction is held on the ground.")]
        public float groundDeceleration = 150f;
        [Tooltip("Acceleration while a direction is held in the air.")]
        public float airAcceleration = 70f;
        [Tooltip("Deceleration while no direction is held in the air.")]
        public float airDeceleration = 45f;

        [Header("Jump")]
        [Tooltip("Maximum jump height, in world units (= grid cells).")]
        public float jumpHeight = 6f;
        [Tooltip("Character height, in grid cells. Used for the ground probe and gizmos.")]
        public float playerHeight = 1.5f;
        [Tooltip("Grace period after leaving ground during which a jump still counts.")]
        public float coyoteTime = 0.1f;
        [Tooltip("How early a jump press is remembered before landing.")]
        public float jumpBufferTime = 0.12f;
        [Tooltip("Extra gravity while falling, for a snappier arc.")]
        [Range(1f, 3f)] public float fallGravityMultiplier = 1.6f;
        [Tooltip("Terminal falling speed, in units per second.")]
        public float maxFallSpeed = 30f;
        [Tooltip("Vertical velocity kept when the jump button is released early.")]
        [Range(0f, 1f)] public float jumpCutMultiplier = 0.45f;

        [Header("Ground Check")]
        [Tooltip("Layers that count as walkable ground.")]
        public LayerMask groundMask = 1 << 6;
        [Tooltip("How far below the feet the ground probe reaches.")]
        public float groundCheckDistance = 0.15f;

        [Header("Respawn")]
        [Tooltip("Falling below this Y coordinate respawns the player.")]
        public float fallLimitY = -12f;

        Rigidbody2D _rb;
        CapsuleCollider2D _col;
        SpriteRenderer _sr;

        float _moveInput;
        bool _manualInput;
        bool _isJumping;
        float _coyoteCounter;
        float _jumpBufferCounter;
        Vector3 _spawnPoint;
        Vector2 _groundNormal = Vector2.up;

        /// <summary>True while the character stands on (or right next to) walkable ground.</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>Current rigidbody velocity.</summary>
        public Vector2 Velocity => _rb != null ? _rb.velocity : Vector2.zero;

        /// <summary>Horizontal input currently applied (-1..1).</summary>
        public float MoveInput => _moveInput;

        void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
            _col = GetComponent<CapsuleCollider2D>();
            _sr = GetComponentInChildren<SpriteRenderer>();
            _spawnPoint = transform.position;
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

            // While a jump is in progress the character is not considered grounded,
            // even if the probe still touches the floor. This keeps the slope climb
            // (which produces upward velocity) grounded, while a real jump is not
            // re-grounded on its first frames.
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

            // Follow the ground surface: on a slope, horizontal speed is converted
            // into along-slope vertical speed, so the capsule walks up/down ramps
            // instead of relying on friction. On flat ground stay glued to the floor
            // so the slope climb velocity is not carried into the next platform.
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

            // Add half a gravity step to compensate for the discrete integration,
            // so the measured apex matches jumpHeight closely.
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
        // Public API (also used by the automated play-mode tests)
        // ------------------------------------------------------------------

        /// <summary>Move back to the spawn point and stop.</summary>
        public void Respawn()
        {
            _rb.velocity = Vector2.zero;
            transform.position = _spawnPoint;
            _isJumping = false;
        }

        /// <summary>Instantly moves the rigidbody to a position and stops it.</summary>
        public void Teleport(Vector2 position)
        {
            _rb.velocity = Vector2.zero;
            _rb.position = position;
            transform.position = position;
            Physics2D.SyncTransforms();
        }

        /// <summary>Resets the spawn point (useful after entering a new area).</summary>
        public void SetSpawnPoint(Vector3 worldPosition)
        {
            _spawnPoint = worldPosition;
        }

        /// <summary>Overrides keyboard input (pass 0 to stop).</summary>
        public void SetMoveInput(float horizontal)
        {
            _manualInput = true;
            _moveInput = Mathf.Clamp(horizontal, -1f, 1f);
        }

        /// <summary>Gives control back to the keyboard.</summary>
        public void ClearManualInput()
        {
            _manualInput = false;
        }

        /// <summary>Buffers a jump press (equivalent to pressing the jump button).</summary>
        public void JumpPressed()
        {
            _jumpBufferCounter = jumpBufferTime;
        }

        /// <summary>Applies the variable-height jump cut (equivalent to releasing jump).</summary>
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
        }
    }
}
