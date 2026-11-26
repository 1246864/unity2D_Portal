using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 平滑跟随的 2D 相机。
    /// 位置独立平滑；角度在玩家显示空间中平滑，玩家回正不会污染相机的相对角速度。
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
        // 角度状态全部是相机相对玩家显示空间的角度及其速度，不是世界角。
        float _relativeAngle;
        float _relativeAngleVelocity;
        float _previousPlayerAngle;
        bool _initialized;

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

            _relativeAngle = 0f;
            _previousPlayerAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);
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

            // r = c - p。玩家本帧转过 Δp 时，先用 r -= Δp 保持相机原有世界朝向，
            // 再把 r 平滑到 0；既保留回正滞后，也不直接平滑不断变化的世界目标角。
            float playerAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);
            float playerDelta = Mathf.DeltaAngle(_previousPlayerAngle, playerAngle);
            _relativeAngle = Mathf.DeltaAngle(0f, _relativeAngle - playerDelta);
            _previousPlayerAngle = playerAngle;

            float angleSmoothTime = realigning
                ? realignRotationSmoothTime
                : rotationSmoothTime;

            if (angleSmoothTime > 0.0001f)
            {
                _relativeAngle = Mathf.SmoothDampAngle(
                    _relativeAngle, 0f,
                    ref _relativeAngleVelocity, angleSmoothTime);
            }
            else
            {
                _relativeAngle = 0f;
                _relativeAngleVelocity = 0f;
            }

            float cameraAngle = Mathf.DeltaAngle(0f, playerAngle + _relativeAngle);
            Quaternion cameraRotation = Quaternion.Euler(0f, 0f, cameraAngle);
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
        }

        protected override void OnTeleported(CameraTeleportContext context)
        {
            // 与相对位姿共用同一个世界线性映射，镜像时已经包含反射，
            // 不能再按新相机局部 X 二次翻转，否则任意初始角度下会产生方向偏差。
            _followVelocity = context.VectorTransform.MultiplyVector(_followVelocity);
            if (context.MirrorRelative)
            {
                // r' = -r，因而相对角速度 ṙ' = -ṙ；这与玩家当前世界角速度无关。
                _relativeAngleVelocity = -_relativeAngleVelocity;

                // 前瞻是相机局部水平状态，偏移及其平滑速度随局部 X 镜像。
                _lookAhead = -_lookAhead;
                _lookAheadVelocity = -_lookAheadVelocity;
            }

            _visual = GetVisual();
            if (_visual == null) _visual = context.PlayerVisual;

            // 位姿已由控制器映射，这里只从映射结果恢复同一套相对角状态。
            // 不在回正首帧硬对齐或清零，连续传送会保留并继续这份运动状态。
            float playerAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);
            float cameraAngle = Mathf.DeltaAngle(0f, ControlledCamera.transform.eulerAngles.z);
            _relativeAngle = Mathf.DeltaAngle(playerAngle, cameraAngle);
            _previousPlayerAngle = playerAngle;
            _initialized = true;
        }

        /// <summary>立即把相机对准目标（不做平滑），并同步旋转。</summary>
        public override void SnapToTarget()
        {
            if (target == null) return;
            if (!_initialized) Initialize();
            if (_visual == null) return;

            _relativeAngle = 0f;
            _relativeAngleVelocity = 0f;
            _followVelocity = Vector3.zero;
            _lookAhead = 0f;
            _lookAheadVelocity = 0f;

            float playerAngle = Mathf.DeltaAngle(0f, _visual.eulerAngles.z);
            _previousPlayerAngle = playerAngle;
            Quaternion rotation = Quaternion.Euler(0f, 0f, playerAngle);
            Vector3 rotatedOffset = rotation * new Vector3(offset.x, offset.y, 0f);

            transform.position = new Vector3(
                target.position.x + rotatedOffset.x,
                target.position.y + rotatedOffset.y,
                transform.position.z);
            transform.rotation = rotation;
        }
    }
}
