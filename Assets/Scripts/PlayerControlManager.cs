using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 玩家输入与主相机控制门面。
    /// 独占原始设备输入；玩家物理和相机跟随仍由各自组件执行。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PlayerControlManager : MonoBehaviour
    {
        [Tooltip("开启时，玩家相机画面左右翻转（绕竖直轴 180°）。")]
        public bool mirrorHorizontally = false;

        [Tooltip("由本组件管理的玩家；留空时从 CameraFollow2D.target 解析")]
        public PlayerController2D player;

        Camera _camera;
        PlayerController2D _player;
        bool _hasInputBaseline;
        bool _inputBaseline;
        bool _inputOverrideActive;
        bool _inputInverted;

        Vector3 _previousMousePosition;
        bool _hasPreviousMousePosition;
        readonly bool[] _mouseHeld = new bool[3];
        readonly bool[] _mousePressed = new bool[3];
        readonly bool[] _mouseReleased = new bool[3];

        public bool IsMirrored => mirrorHorizontally;
        public Vector3 MousePosition { get; private set; }
        public Vector2 MouseDelta { get; private set; }
        public Vector2 MouseScrollDelta { get; private set; }

        /// <summary>鼠标在当前相机投影下对应的世界平面位置（Z = 0）。</summary>
        public Vector3 MouseWorldPosition
        {
            get
            {
                Vector3 screenPosition = MousePosition;
                screenPosition.z = _camera != null ? Mathf.Abs(_camera.transform.position.z) : 0f;
                return ScreenToWorldPoint(screenPosition);
            }
        }

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            _inputInverted = mirrorHorizontally;
            Apply();
            SetPlayerInputInverted(_inputInverted);
        }

        void OnDisable()
        {
            if (_camera != null)
                _camera.ResetProjectionMatrix();

            if (_player != null)
                _player.ClearDeviceInput();
            RestorePlayerInput();
            _hasPreviousMousePosition = false;
            MouseDelta = Vector2.zero;
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            CaptureMouse();

            PlayerController2D target = FindPlayer();
            if (target == null) return;

            float horizontal = 0f;
            if (Input.GetKey(KeyCode.A)) horizontal -= 1f;
            if (Input.GetKey(KeyCode.D)) horizontal += 1f;

            target.SubmitDeviceInput(
                horizontal,
                Input.GetKeyDown(KeyCode.Space),
                Input.GetKeyUp(KeyCode.Space));
        }

        void LateUpdate()
        {
            Apply();
        }

        void OnValidate()
        {
            if (_camera != null)
                Apply();

            if (Application.isPlaying)
            {
                _inputInverted = mirrorHorizontally;
                SetPlayerInputInverted(_inputInverted);
            }
        }

        /// <summary>独立设置输入反转，不改变相机投影。</summary>
        public void SetInputInverted(bool inverted)
        {
            _inputInverted = inverted;
            SetPlayerInputInverted(inverted);
        }

        /// <summary>独立设置相机镜像，不改变输入方向。</summary>
        public void SetCameraMirrored(bool mirrored)
        {
            mirrorHorizontally = mirrored;
            Apply();
        }

        /// <summary>同时设置相机镜像和玩家水平输入方向。</summary>
        public void SetMirrorAndInput(bool mirrored)
        {
            SetCameraMirrored(mirrored);
            SetInputInverted(mirrored);
        }

        /// <summary>以当前相机镜像状态为基准，同时反转相机和输入。</summary>
        public void ToggleMirrorAndInput()
        {
            SetMirrorAndInput(!mirrorHorizontally);
        }

        public void SetMoveInput(float horizontal)
        {
            PlayerController2D target = FindPlayer();
            if (target != null) target.SetMoveInput(horizontal);
        }

        public void ClearManualInput()
        {
            PlayerController2D target = FindPlayer();
            if (target != null) target.ClearManualInput();
        }

        public void JumpPressed()
        {
            PlayerController2D target = FindPlayer();
            if (target != null) target.JumpPressed();
        }

        public void JumpReleased()
        {
            PlayerController2D target = FindPlayer();
            if (target != null) target.JumpReleased();
        }

        public bool MouseButtonHeld(int button)
        {
            return button >= 0 && button < _mouseHeld.Length && _mouseHeld[button];
        }

        public bool MouseButtonPressed(int button)
        {
            return button >= 0 && button < _mousePressed.Length && _mousePressed[button];
        }

        public bool MouseButtonReleased(int button)
        {
            return button >= 0 && button < _mouseReleased.Length && _mouseReleased[button];
        }

        /// <summary>
        /// 后续鼠标与世界交互统一经过这里，使用当前镜像投影做坐标转换。
        /// </summary>
        public Vector3 ScreenToWorldPoint(Vector3 screenPosition)
        {
            if (_camera == null) _camera = GetComponent<Camera>();
            return _camera != null ? _camera.ScreenToWorldPoint(screenPosition) : Vector3.zero;
        }

        /// <summary>传送门调用：登记相机的相对传送，具体应用仍由跟随组件负责。</summary>
        public void TeleportRelative(Transform playerVisual, Matrix4x4 relativeTransform)
        {
            CameraFollow2D follow = GetComponent<CameraFollow2D>();
            if (follow != null)
            {
                follow.TeleportRelative(playerVisual, relativeTransform);
                return;
            }

            if (playerVisual == null || _camera == null) return;
            Matrix4x4 world = playerVisual.localToWorldMatrix * relativeTransform;
            CameraFollow2D.Decompose2D(world, out Vector3 position, out Quaternion rotation, out Vector3 scale);
            _camera.transform.SetPositionAndRotation(position, rotation);
            _camera.transform.localScale = scale;
        }

        /// <summary>传送门调用：登记或立即执行相机位置的水平镜像。</summary>
        public void MirrorPendingCameraPosition(Transform playerVisual)
        {
            CameraFollow2D follow = GetComponent<CameraFollow2D>();
            if (follow != null)
            {
                follow.MirrorPendingCameraPosition();
                return;
            }

            if (playerVisual == null || _camera == null) return;
            Vector3 localPosition = playerVisual.InverseTransformPoint(_camera.transform.position);
            localPosition.x = -localPosition.x;
            _camera.transform.position = playerVisual.TransformPoint(localPosition);
        }

        public void SnapCameraToTarget()
        {
            CameraFollow2D follow = GetComponent<CameraFollow2D>();
            if (follow != null) follow.SnapToTarget();
        }

        void CaptureMouse()
        {
            Vector3 current = Input.mousePosition;
            MouseDelta = _hasPreviousMousePosition ? (Vector2)(current - _previousMousePosition) : Vector2.zero;
            MousePosition = current;
            MouseScrollDelta = Input.mouseScrollDelta;
            _previousMousePosition = current;
            _hasPreviousMousePosition = true;

            for (int button = 0; button < _mouseHeld.Length; button++)
            {
                _mouseHeld[button] = Input.GetMouseButton(button);
                _mousePressed[button] = Input.GetMouseButtonDown(button);
                _mouseReleased[button] = Input.GetMouseButtonUp(button);
            }
        }

        PlayerController2D FindPlayer()
        {
            if (player != null) return player;
            if (_player != null) return _player;

            var follow = GetComponent<CameraFollow2D>();
            if (follow != null && follow.target != null)
            {
                var followedPlayer = follow.target.GetComponent<PlayerController2D>();
                if (followedPlayer != null) return followedPlayer;
            }

            return FindObjectOfType<PlayerController2D>();
        }

        void SetPlayerInputInverted(bool inverted)
        {
            // 先保留旧引用；解析到不同玩家时，先恢复旧玩家的输入基线。
            PlayerController2D previous = _player;
            PlayerController2D target = FindPlayer();

            if (target != previous)
            {
                if (previous != null && _inputOverrideActive && _hasInputBaseline)
                    previous.SetHorizontalInputInverted(_inputBaseline);

                _inputOverrideActive = false;
                _hasInputBaseline = false;
                _player = target;
            }

            if (target == null) return;

            if (!_inputOverrideActive)
            {
                _inputBaseline = target.IsHorizontalInputInverted;
                _hasInputBaseline = true;
            }

            target.SetHorizontalInputInverted(inverted);
            _inputOverrideActive = true;
        }

        void RestorePlayerInput()
        {
            if (_player != null && _inputOverrideActive && _hasInputBaseline)
                _player.SetHorizontalInputInverted(_inputBaseline);

            _inputOverrideActive = false;
            _hasInputBaseline = false;
        }

        void Apply()
        {
            if (_camera == null) return;

            // 先恢复相机的原始投影，避免每帧在已经镜像的矩阵上重复取反。
            _camera.ResetProjectionMatrix();
            if (!mirrorHorizontally) return;

            Matrix4x4 projection = _camera.projectionMatrix;
            projection.m00 = -projection.m00;
            _camera.projectionMatrix = projection;
        }
    }
}
