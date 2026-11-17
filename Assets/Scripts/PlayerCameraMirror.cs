using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 玩家相机滤镜：把主相机画面沿竖直轴左右镜像。
    /// 可同时反转玩家的水平输入，只应挂在玩家使用的主相机上。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class PlayerCameraMirror : MonoBehaviour
    {
        [Tooltip("开启时，玩家相机画面左右翻转（绕竖直轴 180°）。")]
        public bool mirrorHorizontally = false;

        [Tooltip("输入方向由本组件管理的玩家；留空时从 CameraFollow2D.target 解析")]
        public PlayerController2D player;

        Camera _camera;
        PlayerController2D _player;
        bool _hasInputBaseline;
        bool _inputBaseline;
        bool _inputOverrideActive;

        public bool IsMirrored => mirrorHorizontally;

        void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        void OnEnable()
        {
            _camera = GetComponent<Camera>();
            Apply();
            SetPlayerInputInverted(mirrorHorizontally);
        }

        void OnDisable()
        {
            if (_camera != null)
                _camera.ResetProjectionMatrix();

            RestorePlayerInput();
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
                SetPlayerInputInverted(mirrorHorizontally);
        }

        /// <summary>
        /// 一次调用同时切换相机镜像和玩家水平输入方向。
        /// 初始为正常；调用一次反转，再调用一次恢复正常。
        /// </summary>
        public void ToggleMirrorAndInput()
        {
            mirrorHorizontally = !mirrorHorizontally;
            Apply();
            SetPlayerInputInverted(mirrorHorizontally);
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
