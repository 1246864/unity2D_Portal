using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 平滑跟随的 2D 相机。
    ///
    /// 相机在场景里独立，但跟随状态保存在“玩家显示节点的局部空间”：
    /// 局部偏移不变时，玩家传送/旋转后相机跟着同一个刚体变换走，
    /// 所以传送瞬间画面相对玩家是连续的。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        [Tooltip("相机跟随的目标（一般是玩家）")]
        public Transform target;

        [Tooltip("相机在玩家显示空间里的固定局部偏移")]
        public Vector2 offset = new Vector2(0f, 1.2f);

        [Tooltip("局部偏移追上目标偏移的平滑时间")]
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
        Transform _visual;
        Vector3 _localOffset;
        Vector3 _localVelocity;
        float _lookAhead;
        float _lookAheadVelocity;
        bool _initialized;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void Start()
        {
            Initialize();
        }

        Transform GetVisual()
        {
            PlayerController2D controller = target != null
                ? target.GetComponent<PlayerController2D>()
                : null;
            return controller != null && controller.VisualRoot != null
                ? controller.VisualRoot
                : target;
        }

        void Initialize()
        {
            if (_initialized || target == null) return;

            _visual = GetVisual();
            if (_visual == null) return;

            _localOffset = _visual.InverseTransformPoint(transform.position);
            _initialized = true;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (!_initialized) Initialize();
            if (_visual == null) return;

            PlayerController2D controller = target.GetComponent<PlayerController2D>();
            bool realigning = controller != null && controller.IsRealigning;

            if (realigning)
            {
                // 回正期间不允许前瞻把相机从玩家相对位置拉开。
                _lookAhead = 0f;
                _lookAheadVelocity = 0f;
            }
            else if (useLookAhead)
            {
                Rigidbody2D rb = target.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    float localSpeed = Vector2.Dot(rb.velocity, _visual.right);
                    float speed01 = Mathf.Clamp(
                        localSpeed / Mathf.Max(0.01f, lookAheadSpeedReference), -1f, 1f);
                    float desiredLookAhead = speed01 * lookAheadDistance;
                    _lookAhead = Mathf.SmoothDamp(
                        _lookAhead, desiredLookAhead,
                        ref _lookAheadVelocity, lookAheadSmoothTime);
                }
            }
            else
            {
                _lookAhead = 0f;
                _lookAheadVelocity = 0f;
            }

            // 关键在于只平滑局部偏移；玩家本身传送/旋转时，不做世界位置重算，
            // 直接复用 _localOffset，画面相对玩家就不会跳。
            Vector3 desiredLocal = new Vector3(
                offset.x + _lookAhead,
                offset.y,
                _localOffset.z);

            if (smoothTime > 0.0001f)
            {
                _localOffset = Vector3.SmoothDamp(
                    _localOffset, desiredLocal, ref _localVelocity, smoothTime);
            }
            else
            {
                _localOffset = desiredLocal;
                _localVelocity = Vector3.zero;
            }

            transform.position = _visual.TransformPoint(_localOffset);
            transform.rotation = _visual.rotation;
        }

        /// <summary>立即把相机对准目标（不做平滑），并同步旋转。</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            if (!_initialized) Initialize();
            if (_visual == null) return;

            _localOffset = new Vector3(offset.x, offset.y, _localOffset.z);
            _localVelocity = Vector3.zero;
            _lookAhead = 0f;
            _lookAheadVelocity = 0f;

            transform.position = _visual.TransformPoint(_localOffset);
            transform.rotation = _visual.rotation;
        }
    }
}
