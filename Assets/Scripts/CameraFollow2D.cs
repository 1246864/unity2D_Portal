using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 平滑跟随的 2D 相机。
    /// 位置和角度都独立平滑；角色回正时相机角度会滞后，不会硬锁定到角色角度。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public class CameraFollow2D : CameraBehavior2D
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

        Transform _visual;
        Vector3 _followVelocity;
        float _lookAhead;
        float _lookAheadVelocity;
        float _cameraAngle;
        float _cameraAngleVelocity;
        bool _initialized;
        bool _wasRealigning;

        public override Transform Target => target;

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

        protected override void Tick()
        {
            if (target == null) return;

            Transform currentVisual = GetVisual();
            if (currentVisual != _visual)
            {
                _visual = currentVisual;
                _initialized = false;
            }

            if (!_initialized) Initialize();
            if (_visual == null) return;

            PlayerController2D controller = target.GetComponent<PlayerController2D>();
            bool realigning = controller != null && controller.IsRealigning;

            if (realigning)
            {
                if (useLookAhead)
                {
                    // 回正期间不再产生新的前瞻，但保留已有惯性并平滑衰减。
                    _lookAhead = Mathf.SmoothDamp(
                        _lookAhead, 0f,
                        ref _lookAheadVelocity, lookAheadSmoothTime);
                }
                else
                {
                    _lookAhead = 0f;
                    _lookAheadVelocity = 0f;
                }
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

        protected override void OnTeleported(CameraTeleportContext context)
        {
            _followVelocity = context.PlayerDeltaRotation * _followVelocity;
            if (context.MirrorRelative)
            {
                // 与相机位置一致，沿传送后的相机局部 X 镜像位置速度。
                Vector3 localVelocity = ControlledCamera.transform.InverseTransformVector(_followVelocity);
                localVelocity.x = -localVelocity.x;
                _followVelocity = ControlledCamera.transform.TransformVector(localVelocity);

                // 反射会反转世界 Z 角速度；若继续保留旧方向，连续同向传送时
                // 相机会沿镜像后的错误方向继续回正。
                _cameraAngleVelocity = -_cameraAngleVelocity;

                // 前瞻是相机局部水平状态，偏移及其平滑速度随局部 X 镜像。
                _lookAhead = -_lookAhead;
                _lookAheadVelocity = -_lookAheadVelocity;
            }

            _visual = GetVisual();
            if (_visual == null) _visual = context.PlayerVisual;

            _cameraAngle = Mathf.DeltaAngle(0f, ControlledCamera.transform.eulerAngles.z);

            // 如果新传送仍然处于回正阶段，下一帧不要再次执行“回正第一帧硬对齐”，
            // 否则会把刚映射好的相机角度重新拉向玩家，造成连续传送时卡顿。
            PlayerController2D controller = target != null
                ? target.GetComponent<PlayerController2D>()
                : null;
            _wasRealigning = controller != null && controller.IsRealigning;
            _initialized = true;
        }

        /// <summary>立即把相机对准目标（不做平滑），并同步旋转。</summary>
        public override void SnapToTarget()
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
