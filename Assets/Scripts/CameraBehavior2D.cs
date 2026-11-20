using UnityEngine;

namespace Platformer
{
    /// <summary>相机完成一次相对传送后提供给行为的变换信息。</summary>
    public readonly struct CameraTeleportContext
    {
        public Transform PlayerVisual { get; }

        /// <summary>玩家传送前后的世界旋转增量。</summary>
        public Quaternion PlayerDeltaRotation { get; }

        /// <summary>同向门传送：镜像相机局部水平状态，并按旧投影状态映射相机角度。</summary>
        public bool MirrorRelative { get; }

        public CameraTeleportContext(
            Transform playerVisual,
            Quaternion playerDeltaRotation,
            bool mirrorRelative)
        {
            PlayerVisual = playerVisual;
            PlayerDeltaRotation = playerDeltaRotation;
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
