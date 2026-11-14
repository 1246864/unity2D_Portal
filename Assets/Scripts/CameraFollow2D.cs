using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 平滑跟随的 2D 相机，可选水平前瞻。
    /// 挂在正交主相机上，并指定跟随目标。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        [Tooltip("相机跟随的目标（一般是玩家）")]
        public Transform target;

        [Tooltip("相对目标的固定偏移（世界单位）")]
        public Vector2 offset = new Vector2(0f, 1.2f);

        [Tooltip("相机追上目标的平滑时间")]
        public float smoothTime = 0.12f;

        [Header("前瞻")]
        [Tooltip("是否按移动方向把相机往前推")]
        public bool useLookAhead = true;
        [Tooltip("全速奔跑时的最大水平前瞻距离")]
        public float lookAheadDistance = 1.5f;
        [Tooltip("达到最大前瞻所需的水平速度")]
        public float lookAheadSpeedReference = 7f;
        [Tooltip("前瞻偏移的平滑时间")]
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

        /// <summary>立即把相机对准目标（不做平滑）。</summary>
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
