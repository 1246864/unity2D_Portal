using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 平滑跟随的 2D 相机。
    /// 位置和角度都独立平滑；角色回正时相机角度会滞后，不会硬锁定到角色角度。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : MonoBehaviour
    {
        [Tooltip("相机跟随的目标（一般是玩家）")]
        public Transform target;

        [Tooltip("相机相对玩家的局部偏移")]
        public Vector2 offset = new Vector2(0f, 1.2f);

        [Tooltip("相机位置追上目标的平滑时间")]
        public float smoothTime = 0.12f;

        [Header("旋转跟随")]
        [Tooltip("普通状态下相机角度追上玩家显示角度的平滑时间；0 表示硬锁定")]
        [Min(0f)] public float rotationSmoothTime = 0.15f;

        [Tooltip("角色回正时相机角度追上玩家显示角度的平滑时间；越大越迟钝")]
        [Min(0f)] public float realignRotationSmoothTime = 0.25f;

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
        Vector3 _followVelocity;
        float _lookAhead;
        float _lookAheadVelocity;
        float _cameraAngle;
        float _cameraAngleVelocity;
        bool _initialized;
        bool _wasRealigning;
        bool _pendingTeleport;
        bool _pendingMirrorPosition;
        Transform _pendingVisual;
        Matrix4x4 _pendingRelative;

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

            _cameraAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);
            _initialized = true;
        }

        void LateUpdate()
        {
            if (target == null) return;
            if (!_initialized) Initialize();
            if (_visual == null) return;

            // 传送帧由 PortalPair 记录相对矩阵；等玩家这一帧最终位置确定后再映射。
            if (_pendingTeleport)
            {
                ApplyPendingTeleport();
                return;
            }

            PlayerController2D controller = target.GetComponent<PlayerController2D>();
            bool realigning = controller != null && controller.IsRealigning;

            if (realigning)
            {
                // 回正期间禁用前瞻，避免相机从玩家相对位置被拉开。
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

            float targetAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);

            if (realigning)
            {
                // 传送/回正开始的第一帧先对齐，之后以“迟钝”的角度跟随。
                if (!_wasRealigning)
                {
                    _cameraAngle = targetAngle;
                    _cameraAngleVelocity = 0f;
                }

                _cameraAngle = Mathf.SmoothDampAngle(
                    _cameraAngle, targetAngle,
                    ref _cameraAngleVelocity, realignRotationSmoothTime);
            }
            else if (rotationSmoothTime > 0.0001f)
            {
                _cameraAngle = Mathf.SmoothDampAngle(
                    _cameraAngle, targetAngle,
                    ref _cameraAngleVelocity, rotationSmoothTime);
            }
            else
            {
                _cameraAngle = targetAngle;
                _cameraAngleVelocity = 0f;
            }

            Quaternion cameraRotation = Quaternion.Euler(0f, 0f, _cameraAngle);
            Vector3 rotatedOffset = cameraRotation * new Vector3(
                offset.x + _lookAhead, offset.y, 0f);

            Vector3 desired = new Vector3(
                target.position.x + rotatedOffset.x,
                target.position.y + rotatedOffset.y,
                transform.position.z);

            if (smoothTime > 0.0001f)
            {
                transform.position = Vector3.SmoothDamp(
                    transform.position, desired, ref _followVelocity, smoothTime);
            }
            else
            {
                transform.position = desired;
                _followVelocity = Vector3.zero;
            }

            transform.rotation = cameraRotation;
            _wasRealigning = realigning;
        }

        /// <summary>
        /// 传送触发时调用：记录相机相对玩家显示节点的完整矩阵（位置/旋转/缩放）。
        /// 真正应用延迟到 LateUpdate，等玩家最终位置确定后映射到绝对变换。
        /// </summary>
        public void TeleportRelative(Transform playerVisual, Matrix4x4 relativeTransform)
        {
            if (playerVisual == null) return;

            _pendingVisual = playerVisual;
            _pendingRelative = relativeTransform;
            _pendingTeleport = true;
            _pendingMirrorPosition = false;
        }

        /// <summary>
        /// 后处理：在待应用的传送相机位置基础上，沿玩家相对 Y 轴镜像一次 X。
        /// </summary>
        public void MirrorPendingCameraPosition()
        {
            if (_pendingTeleport)
                _pendingMirrorPosition = true;
        }

        void ApplyPendingTeleport()
        {
            _pendingTeleport = false;
            if (_pendingVisual == null) return;

            Matrix4x4 world = _pendingVisual.localToWorldMatrix * _pendingRelative;
            Decompose2D(world, out Vector3 position, out Quaternion rotation, out Vector3 scale);

            if (_pendingMirrorPosition)
            {
                _pendingMirrorPosition = false;
                Vector3 localPosition = _pendingVisual.InverseTransformPoint(position);
                localPosition.x = -localPosition.x;
                position = _pendingVisual.TransformPoint(localPosition);
            }

            transform.SetPositionAndRotation(position, rotation);
            transform.localScale = scale;

            _cameraAngle = Mathf.DeltaAngle(0f, rotation.eulerAngles.z);
            _cameraAngleVelocity = 0f;
            _followVelocity = Vector3.zero;
            _lookAhead = 0f;
            _lookAheadVelocity = 0f;

            // 如果新传送仍然处于回正阶段，下一帧不要再次执行“回正第一帧硬对齐”，
            // 否则会把刚映射好的相机角度重新拉向玩家，造成连续传送时卡顿。
            PlayerController2D controller = target != null
                ? target.GetComponent<PlayerController2D>()
                : null;
            _wasRealigning = controller != null && controller.IsRealigning;
            _initialized = true;
        }

        /// <summary>从二维相机矩阵提取位置、Z 旋转和缩放。</summary>
        public static void Decompose2D(
            Matrix4x4 matrix, out Vector3 position, out Quaternion rotation, out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            scale = new Vector3(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
            float angle = Mathf.Atan2(matrix.m10, matrix.m00) * Mathf.Rad2Deg;
            rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>立即把相机对准目标（不做平滑），并同步旋转。</summary>
        public void SnapToTarget()
        {
            if (target == null) return;
            if (!_initialized) Initialize();
            if (_visual == null) return;

            _cameraAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);
            _cameraAngleVelocity = 0f;
            _followVelocity = Vector3.zero;
            _lookAhead = 0f;
            _lookAheadVelocity = 0f;

            Quaternion rotation = Quaternion.Euler(0f, 0f, _cameraAngle);
            Vector3 rotatedOffset = rotation * new Vector3(offset.x, offset.y, 0f);

            transform.position = new Vector3(
                target.position.x + rotatedOffset.x,
                target.position.y + rotatedOffset.y,
                transform.position.z);
            transform.rotation = rotation;
        }
    }
}
