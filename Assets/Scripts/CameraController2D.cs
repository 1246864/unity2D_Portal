using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 主相机控制器：管理激活行为、一次性相对传送和投影镜像。
    /// 持续行为只负责常规相机运动，不接触这些独立状态。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public abstract class CameraController2D : MonoBehaviour
    {
        [Header("相机行为")]
        [Tooltip("当前激活的相机行为；留空时自动使用同对象上的第一个行为。")]
        [SerializeField] CameraBehavior2D activeBehavior;

        [Tooltip("开启时，玩家传送后相机保持与玩家的相对位姿；关闭时跳过相机位姿传送。")]
        public bool followPlayerTeleport = true;

        [Header("投影镜像")]
        [Tooltip("开启时，玩家相机画面左右翻转（绕竖直轴 180°）。")]
        public bool mirrorHorizontally = false;

        Camera _camera;
        CameraTeleportRequest? _pendingTeleport;

        readonly struct CameraTeleportRequest
        {
            public Transform PlayerVisual { get; }
            public Matrix4x4 RelativeTransform { get; }
            public bool MirrorRelative { get; }

            public CameraTeleportRequest(
                Transform playerVisual,
                Matrix4x4 relativeTransform,
                bool mirrorRelative)
            {
                PlayerVisual = playerVisual;
                RelativeTransform = relativeTransform;
                MirrorRelative = mirrorRelative;
            }
        }

        public CameraBehavior2D ActiveBehavior => activeBehavior;
        public bool IsMirrored => mirrorHorizontally;

        protected Camera ControlledCamera
        {
            get
            {
                if (_camera == null) _camera = GetComponent<Camera>();
                return _camera;
            }
        }

        protected virtual void Awake()
        {
            _camera = GetComponent<Camera>();
            ResolveBehavior();
        }

        protected virtual void OnEnable()
        {
            ApplyMirror();
        }

        protected virtual void OnDisable()
        {
            if (_camera != null) _camera.ResetProjectionMatrix();
        }

        protected virtual void LateUpdate()
        {
            // 没有可执行行为时，控制器仍要保证相机按相对位姿传送。
            if (activeBehavior == null || !activeBehavior.isActiveAndEnabled)
                TryApplyPendingTeleport(out _);

            ApplyMirror();
        }

        protected virtual void OnValidate()
        {
            _camera = GetComponent<Camera>();
            ResolveBehavior();
            ApplyMirror();
        }

        public void SetActiveBehavior(CameraBehavior2D behavior)
        {
            activeBehavior = behavior;
            if (activeBehavior != null) activeBehavior.Attach(this);
        }

        internal void RegisterBehavior(CameraBehavior2D behavior)
        {
            if (behavior == null) return;

            behavior.Attach(this);
            if (activeBehavior == null) activeBehavior = behavior;
        }

        void ResolveBehavior()
        {
            if (activeBehavior == null)
            {
                CameraBehavior2D[] behaviors = GetComponents<CameraBehavior2D>();
                if (behaviors.Length > 0) activeBehavior = behaviors[0];
            }

            if (activeBehavior != null) activeBehavior.Attach(this);
        }

        /// <summary>独立设置相机投影镜像，不改变输入方向。</summary>
        public void SetCameraMirrored(bool mirrored)
        {
            mirrorHorizontally = mirrored;
            ApplyMirror();
        }

        protected void ApplyMirror()
        {
            if (ControlledCamera == null) return;

            // 先恢复原始投影，避免每帧在已经镜像的矩阵上重复取反。
            ControlledCamera.ResetProjectionMatrix();
            if (mirrorHorizontally)
            {
                Matrix4x4 projection = ControlledCamera.projectionMatrix;
                projection.m00 = -projection.m00;
                ControlledCamera.projectionMatrix = projection;
            }
        }

        /// <summary>一次登记完整的相机相对传送，由激活行为在 LateUpdate 消费。</summary>
        public void TeleportRelative(
            Transform playerVisual,
            Matrix4x4 relativeTransform,
            bool mirrorRelative)
        {
            if (!followPlayerTeleport || playerVisual == null)
            {
                ClearPendingTeleport();
                return;
            }

            _pendingTeleport = new CameraTeleportRequest(
                playerVisual, relativeTransform, mirrorRelative);
        }

        internal bool TryApplyPendingTeleport(out CameraTeleportContext context)
        {
            context = default;
            if (!_pendingTeleport.HasValue) return false;

            CameraTeleportRequest request = _pendingTeleport.Value;
            Transform playerVisual = request.PlayerVisual;
            if (!followPlayerTeleport || playerVisual == null || ControlledCamera == null)
            {
                ClearPendingTeleport();
                return false;
            }

            Decompose2D(
                request.RelativeTransform,
                out Vector3 relativePosition,
                out Quaternion relativeRotation,
                out Vector3 relativeScale);

            Transform cameraTransform = ControlledCamera.transform;
            Quaternion previousPlayerRotation =
                cameraTransform.rotation * Quaternion.Inverse(relativeRotation);

            bool mirrorRelative = request.MirrorRelative;
            if (mirrorRelative)
            {
                // 位置与旋转必须在同一个旧玩家局部坐标系中反射。
                // 相机 Transform 保存反射后的正向旋转，画面 handedness 由投影镜像负责。
                relativePosition.x = -relativePosition.x;
                relativeRotation = Quaternion.Inverse(relativeRotation);
            }

            Matrix4x4 relative = Matrix4x4.TRS(relativePosition, relativeRotation, relativeScale);
            Matrix4x4 world = playerVisual.localToWorldMatrix * relative;
            Decompose2D(world, out Vector3 position, out Quaternion rotation, out Vector3 scale);

            // 位置速度与位姿共用：旧玩家世界系 → 可选局部 X 反射 → 新玩家世界系。
            // 非镜像时退化为玩家世界旋转增量，镜像时行列式为 -1。
            Matrix4x4 mirror = mirrorRelative
                ? Matrix4x4.Scale(new Vector3(-1f, 1f, 1f))
                : Matrix4x4.identity;
            Matrix4x4 vectorTransform =
                Matrix4x4.Rotate(playerVisual.rotation) *
                mirror *
                Matrix4x4.Rotate(Quaternion.Inverse(previousPlayerRotation));

            cameraTransform.SetPositionAndRotation(position, rotation);
            cameraTransform.localScale = scale;
            context = new CameraTeleportContext(playerVisual, vectorTransform, mirrorRelative);
            ClearPendingTeleport();
            return true;
        }

        void ClearPendingTeleport()
        {
            _pendingTeleport = null;
        }

        public void SnapCameraToTarget()
        {
            if (activeBehavior != null) activeBehavior.SnapToTarget();
        }

        /// <summary>从二维相机矩阵提取位置、Z 旋转和缩放。</summary>
        public static void Decompose2D(
            Matrix4x4 matrix,
            out Vector3 position,
            out Quaternion rotation,
            out Vector3 scale)
        {
            position = matrix.GetColumn(3);
            scale = new Vector3(
                matrix.GetColumn(0).magnitude,
                matrix.GetColumn(1).magnitude,
                matrix.GetColumn(2).magnitude);
            float angle = Mathf.Atan2(matrix.m10, matrix.m00) * Mathf.Rad2Deg;
            rotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
