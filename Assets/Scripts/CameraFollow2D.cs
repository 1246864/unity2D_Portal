using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Smooth 2D camera follow with optional horizontal look-ahead.
    /// Attach to the orthographic main camera and assign a target.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : PlatformerBehaviour
    {
        [Tooltip("Transform the camera follows (usually the player).")]
        public Transform target;

        [Tooltip("Constant offset from the target, in world units.")]
        public Vector2 offset = new Vector2(0f, 1.2f);

        [Tooltip("Approximate time for the camera to catch up with the target.")]
        public float smoothTime = 0.12f;

        [Header("Look Ahead")]
        [Tooltip("Shift the camera in the direction the player is moving.")]
        public bool useLookAhead = true;
        [Tooltip("Maximum horizontal shift while running at full speed.")]
        public float lookAheadDistance = 1.5f;
        [Tooltip("Horizontal speed that corresponds to the full look-ahead offset.")]
        public float lookAheadSpeedReference = 7f;
        [Tooltip("Smoothing time for the look-ahead offset.")]
        public float lookAheadSmoothTime = 0.4f;

        Camera _camera;
        Vector3 _followVelocity;
        float _lookAhead;
        float _lookAheadVelocity;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void LateUpdate()
        {
            if (target == null) return;

            float desiredLookAhead = 0f;
            if (useLookAhead)
            {
                Rigidbody2D rb = target.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    float speed01 = Mathf.Clamp(
                        rb.velocity.x / Mathf.Max(0.01f, lookAheadSpeedReference), -1f, 1f);
                    desiredLookAhead = speed01 * lookAheadDistance;
                }
            }

            _lookAhead = Mathf.SmoothDamp(
                _lookAhead, desiredLookAhead, ref _lookAheadVelocity, lookAheadSmoothTime);

            Vector3 desired = new Vector3(
                target.position.x + offset.x + _lookAhead,
                target.position.y + offset.y,
                transform.position.z);

            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref _followVelocity, smoothTime);
        }

        /// <summary>Instantly centers the camera on the target (no smoothing).</summary>
        public void SnapToTarget()
        {
            if (target == null) return;

            transform.position = new Vector3(
                target.position.x + offset.x,
                target.position.y + offset.y,
                transform.position.z);

            _followVelocity = Vector3.zero;
            _lookAhead = 0f;
            _lookAheadVelocity = 0f;
        }
    }
}
