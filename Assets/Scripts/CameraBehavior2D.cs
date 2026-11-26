using UnityEngine;

namespace Platformer
{
    /// <summary>相机完成一次相对传送后提供给行为的变换信息。</summary>
    public readonly struct CameraTeleportContext
    {
        public Transform PlayerVisual { get; }

        /// <summary>旧玩家世界系到可选反射、再到新玩家世界系的速度变换。</summary>
        public Matrix4x4 VectorTransform { get; }

        /// <summary>是否在旧玩家局部 X 方向反射相对位姿与运动状态。</summary>
        public bool MirrorRelative { get; }

        public CameraTeleportContext(
            Transform playerVisual,
            Matrix4x4 vectorTransform,
            bool mirrorRelative)
        {
            PlayerVisual = playerVisual;
            VectorTransform = vectorTransform;
            MirrorRelative = mirrorRelative;
        }
    }

    /// <summary>
    /// 可插拔相机行为。控制器先处理一次性传送，行为再决定是否执行常规运动。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public abstract class CameraBehavior2D : MonoBehaviour
    {
        public CameraController2D Owner { get; private set; }
        protected Camera ControlledCamera { get; private set; }

        /// <summary>行为当前追踪的目标；非跟随行为可以返回 null。</summary>
        public virtual Transform Target => null;

        protected virtual void Awake()
        {
            ControlledCamera = GetComponent<Camera>();
            Owner = GetComponent<CameraController2D>();
            if (Owner != null) Owner.RegisterBehavior(this);
        }

        internal void Attach(CameraController2D owner)
        {
            Owner = owner;
            if (ControlledCamera == null) ControlledCamera = GetComponent<Camera>();
        }

        protected virtual void LateUpdate()
        {
            if (Owner != null && Owner.ActiveBehavior != this) return;

            if (Owner != null && Owner.TryApplyPendingTeleport(out CameraTeleportContext context))
            {
                OnTeleported(context);
                return;
            }

            Tick();
        }

        /// <summary>执行本帧常规相机行为。</summary>
        protected abstract void Tick();

        /// <summary>相机完成相对传送后，变换行为内部的平滑或追踪状态。</summary>
        protected virtual void OnTeleported(CameraTeleportContext context)
        {
        }

        public virtual void SnapToTarget()
        {
        }
    }
}
