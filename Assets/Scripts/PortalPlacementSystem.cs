using UnityEngine;
using UnityEngine.Tilemaps;

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
            if (!CanPlaceOnSurface(side, hit)) return false;

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

        static bool CanPlaceOnSurface(CaptureGroupSide side, RaycastHit2D hit)
        {
            if (hit.collider == null) return false;

            // 普通碰撞体可直接挂载接口实现；未声明资格的普通表面保持可放置。
            var surface = hit.collider.GetComponent<IPortalPlacementSurface>();
            if (surface != null) return surface.CanPlacePortal(side);

            Tilemap tilemap = hit.collider.GetComponentInParent<Tilemap>();
            if (tilemap == null) return true;

            // 射线点位于碰撞表面，向砖块内部偏移一点再取格，避免落到相邻空格。
            Vector2 insidePoint = hit.point - hit.normal.normalized * 0.001f;
            TileBase tile = tilemap.GetTile(tilemap.WorldToCell(insidePoint));
            if (tile is IPortalPlacementSurface tileSurface)
                return tileSurface.CanPlacePortal(side);

            // 兼容未迁移的内置 Tile；Tilemap 已命中但无法解析方块时安全地拒绝。
            return tile != null;
        }
    }
}
