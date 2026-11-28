using UnityEngine;

namespace Platformer
{
    /// <summary>把一次有效地形命中转换为传送门的位置、朝向和开门表现。</summary>
    sealed class PortalPlacementSystem
    {
        readonly PortalPair _owner;

        public PortalPlacementSystem(PortalPair owner)
        {
            _owner = owner;
        }

        public bool TryPlace(CaptureGroupSide side, RaycastHit2D hit)
        {
            CaptureDisplayGroup group = _owner.GetGroup(side);
            if (group == null) return false;

            Vector2 normal = hit.normal;
            if (normal.sqrMagnitude <= 0.00000001f) return false;
            normal.Normalize();

            PortalFacing nextFacing = normal.x < 0f ? PortalFacing.Left : PortalFacing.Right;
            float normalAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            float targetAngle = nextFacing == PortalFacing.Left ? normalAngle - 180f : normalAngle;
            Quaternion targetRotation = Quaternion.Euler(
                0f, 0f, Mathf.DeltaAngle(0f, targetAngle));
            Vector3 targetPosition = hit.point;
            targetPosition.z = group.transform.position.z;

            group.Facing = nextFacing;
            group.transform.SetPositionAndRotation(targetPosition, targetRotation);
            _owner.NotifyPortalRelocated(group);
            _owner.SetPortalTerrainState(group, true);
            group.PlayOpenAnimation();
            _owner.RequestCaptureRefresh();
            return true;
        }
    }
}
