using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 玩家输入与主相机控制门面。
    /// 独占原始设备输入；玩家物理和激活相机行为仍由各自组件执行。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PlayerControlManager : CameraController2D
    {
        [Tooltip("由本组件管理的玩家；留空时从激活相机行为的 Target 解析")]
        public PlayerController2D player;

        [Tooltip("接收当前玩家放门请求的 PortalPair；玩家实例必须与 PortalPair.player 一致")]
        public PortalPair portalPair;

        PlayerController2D _player;
        bool _hasInputBaseline;
        bool _inputBaseline;
        bool _inputOverrideActive;
        bool _inputInverted;
        bool _heldInputInverted;
        bool _heldInputLeft;
        bool _heldInputRight;

        Vector3 _previousMousePosition;
        bool _hasPreviousMousePosition;
        readonly bool[] _mouseHeld = new bool[3];
        readonly bool[] _mousePressed = new bool[3];
        readonly bool[] _mouseReleased = new bool[3];

        public Vector3 MousePosition { get; private set; }
        public Vector2 MouseDelta { get; private set; }
        public Vector2 MouseScrollDelta { get; private set; }

        /// <summary>鼠标在当前相机投影下对应的世界平面位置（Z = 0）。</summary>
        public Vector3 MouseWorldPosition
        {
            get
            {
                Vector3 screenPosition = MousePosition;
                Camera camera = ControlledCamera;
                screenPosition.z = camera != null ? Mathf.Abs(camera.transform.position.z) : 0f;
                return ScreenToWorldPoint(screenPosition);
            }
        }

        protected override void Awake()
        {
            base.Awake();
        }

        protected override void OnEnable()
        {
            base.OnEnable();
            _inputInverted = mirrorHorizontally;
            SetPlayerInputInverted(_inputInverted);
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            ClearHeldInputInversion();
            if (_player != null) _player.ClearDeviceInput();
            RestorePlayerInput();
            _hasPreviousMousePosition = false;
            MouseDelta = Vector2.zero;
        }

        void Update()
        {
            if (!Application.isPlaying) return;

            CaptureMouse();
            bool leftHeld = Input.GetKey(KeyCode.A);
            bool rightHeld = Input.GetKey(KeyCode.D);
            UpdateHeldInputInversion(leftHeld, rightHeld);

            PlayerController2D target = FindPlayer();
            if (target == null) return;

            DispatchPortalPlacementInput(target);

            float horizontal = 0f;
            if (leftHeld) horizontal -= 1f;
            if (rightHeld) horizontal += 1f;
            if (_heldInputInverted) horizontal = -horizontal;

            target.SubmitDeviceInput(
                horizontal,
                Input.GetKeyDown(KeyCode.Space),
                Input.GetKeyUp(KeyCode.Space));
        }

        void DispatchPortalPlacementInput(PlayerController2D target)
        {
            if (portalPair == null || portalPair.player != target.transform) return;

            // Unity 鼠标编号：0 左键、1 右键、2 中键。
            bool placeA = MouseButtonPressed(0);
            bool placeB = MouseButtonPressed(1);
            if (!placeA && !placeB) return;

            Vector2 origin = target.CenterPoint != null
                ? target.CenterPoint.transform.position
                : target.transform.position;
            Vector2 mouseTarget = MouseWorldPosition;
            if (placeA) portalPair.TryPlacePortal(CaptureGroupSide.A, origin, mouseTarget);
            if (placeB) portalPair.TryPlacePortal(CaptureGroupSide.B, origin, mouseTarget);
        }

        protected override void OnValidate()
        {
            base.OnValidate();
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

        /// <summary>执行同向门传送的相机、输入与精灵镜像规则。</summary>
        public void ApplySameFacingPortalTeleport(PlayerController2D controller)
        {
            if (controller == null) return;

            if (followPlayerTeleport)
            {
                ToggleMirrorAndInput();
                controller.MirrorSpriteHorizontally();
                return;
            }

            // 相机不跟随传送时，投影、精灵和持久输入保持不变，只处理仍按住的设备方向。
            ToggleHeldInputInversion();
        }

        void ToggleHeldInputInversion()
        {
            bool leftHeld = Input.GetKey(KeyCode.A);
            bool rightHeld = Input.GetKey(KeyCode.D);
            UpdateHeldInputInversion(leftHeld, rightHeld);

            if (_heldInputInverted)
            {
                ClearHeldInputInversion();
                return;
            }

            if (!leftHeld && !rightHeld) return;

            _heldInputInverted = true;
            _heldInputLeft = leftHeld;
            _heldInputRight = rightHeld;
        }

        void UpdateHeldInputInversion(bool leftHeld, bool rightHeld)
        {
            if (!_heldInputInverted) return;

            bool anyTriggerKeyHeld = (_heldInputLeft && leftHeld) ||
                                     (_heldInputRight && rightHeld);
            if (!anyTriggerKeyHeld) ClearHeldInputInversion();
        }

        void ClearHeldInputInversion()
        {
            _heldInputInverted = false;
            _heldInputLeft = false;
            _heldInputRight = false;
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
            Camera camera = ControlledCamera;
            return camera != null ? camera.ScreenToWorldPoint(screenPosition) : Vector3.zero;
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

            Transform behaviorTarget = ActiveBehavior != null ? ActiveBehavior.Target : null;
            if (behaviorTarget != null)
            {
                var followedPlayer = behaviorTarget.GetComponent<PlayerController2D>();
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
                ClearHeldInputInversion();
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
    }
}
