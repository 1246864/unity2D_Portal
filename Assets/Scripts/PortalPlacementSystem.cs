using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>把一次有效地形命中转换为传送门的位置、朝向和开门表现。</summary>
    sealed class PortalPlacementSystem
    {
        const float SurfaceEpsilon = 0.001f;
        const float ProbeDistance = 0.05f;

        readonly PortalPair _owner;
        readonly RaycastHit2D[] _probeHits = new RaycastHit2D[16];

        public PortalPlacementSystem(PortalPair owner)
        {
            _owner = owner;
        }

        public bool TryPlace(
            CaptureGroupSide side, RaycastHit2D hit, ContactFilter2D filter)
        {
            CaptureDisplayGroup group = _owner.GetGroup(side);
            if (group == null) return false;

            Vector2 normal = hit.normal;
            if (normal.sqrMagnitude <= 0.00000001f) return false;
            normal.Normalize();

            Vector2 surfacePosition = hit.point;
            Tilemap tilemap = hit.collider != null
                ? hit.collider.GetComponentInParent<Tilemap>()
                : null;
            if (tilemap != null)
            {
                if (!TryFitContinuousSurface(
                        side, hit, filter, tilemap, normal, ref surfacePosition))
                    return false;
            }
            else if (!CanPlaceOnSurface(side, hit))
            {
                return false;
            }

            PortalFacing nextFacing = normal.x < 0f ? PortalFacing.Left : PortalFacing.Right;
            float normalAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
            float targetAngle = nextFacing == PortalFacing.Left ? normalAngle - 180f : normalAngle;
            Quaternion targetRotation = Quaternion.Euler(
                0f, 0f, Mathf.DeltaAngle(0f, targetAngle));
            float surfaceOffset = Mathf.Max(0f, _owner.placementSurfaceOffset);
            Vector3 targetPosition = surfacePosition + normal * surfaceOffset;
            targetPosition.z = group.transform.position.z;

            group.Facing = nextFacing;
            group.transform.SetPositionAndRotation(targetPosition, targetRotation);
            _owner.NotifyPortalRelocated(group);
            _owner.SetPortalTerrainState(group, true);
            group.PlayOpenAnimation();
            _owner.RequestCaptureRefresh();
            return true;
        }

        bool TryFitContinuousSurface(
            CaptureGroupSide side,
            RaycastHit2D hit,
            ContactFilter2D filter,
            Tilemap tilemap,
            Vector2 normal,
            ref Vector2 surfacePosition)
        {
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            Vector3Int hitCell = ResolveHitCell(tilemap, hit.point, normal, tangent);
            if (!CanPlaceTile(side, tilemap.GetTile(hitCell))) return false;

            float cellStride = CalculateCellStride(tilemap, tangent);
            if (cellStride <= 0.000001f) return false;
            if (!TryGetSurfaceCellInterval(
                    tilemap, hitCell, hit.point, tangent,
                    out float minProjection, out float maxProjection)) return false;

            int scanRange = 1 + (int)_owner.PortalHeight;
            for (int direction = -1; direction <= 1; direction += 2)
            {
                Vector3Int previousCell = hitCell;
                for (int offset = 1; offset <= scanRange; offset++)
                {
                    Vector2 samplePoint = hit.point +
                                          tangent * (direction * offset * cellStride);
                    if (!TryProbeSurface(
                            side, filter, tilemap, hit.point, normal, tangent,
                            samplePoint, out Vector3Int cell)) break;
                    if (!AreAdjacent(previousCell, cell)) break;
                    if (!TryGetSurfaceCellInterval(
                            tilemap, cell, hit.point, tangent,
                            out float cellMin, out float cellMax)) break;

                    minProjection = Mathf.Min(minProjection, cellMin);
                    maxProjection = Mathf.Max(maxProjection, cellMax);
                    previousCell = cell;
                }
            }

            float portalHeight = _owner.PortalHeight;
            if (maxProjection - minProjection < portalHeight) return false;

            float halfHeight = portalHeight * 0.5f;
            float hitProjection = Vector2.Dot(hit.point, tangent);
            float centerProjection = Mathf.Clamp(
                hitProjection, minProjection + halfHeight, maxProjection - halfHeight);
            surfacePosition = hit.point + tangent * (centerProjection - hitProjection);
            return true;
        }

        bool TryProbeSurface(
            CaptureGroupSide side,
            ContactFilter2D filter,
            Tilemap tilemap,
            Vector2 originalPoint,
            Vector2 normal,
            Vector2 tangent,
            Vector2 samplePoint,
            out Vector3Int cell)
        {
            cell = default;
            Vector2 origin = samplePoint + normal * ProbeDistance;
            int hitCount = Physics2D.Raycast(
                origin, -normal, filter, _probeHits, ProbeDistance * 2f);

            RaycastHit2D nearest = default;
            bool found = false;
            Rigidbody2D playerBody = _owner.player != null
                ? _owner.player.GetComponent<Rigidbody2D>()
                : null;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D candidate = _probeHits[i];
                if (candidate.collider == null) continue;
                if (playerBody != null && candidate.collider.attachedRigidbody == playerBody) continue;
                if (_owner.player != null &&
                    (candidate.collider.transform == _owner.player ||
                     candidate.collider.transform.IsChildOf(_owner.player))) continue;
                if (found && candidate.distance >= nearest.distance) continue;
                nearest = candidate;
                found = true;
            }

            if (!found || nearest.collider.GetComponentInParent<Tilemap>() != tilemap)
                return false;
            if (Vector2.Dot(nearest.normal.normalized, normal) < 0.999f) return false;
            if (Mathf.Abs(Vector2.Dot(nearest.point - originalPoint, normal)) >
                SurfaceEpsilon) return false;

            cell = ResolveHitCell(tilemap, nearest.point, normal, tangent);
            return CanPlaceTile(side, tilemap.GetTile(cell));
        }

        static Vector3Int ResolveHitCell(
            Tilemap tilemap, Vector2 point, Vector2 normal, Vector2 tangent)
        {
            Vector2 insidePoint = point - normal * SurfaceEpsilon;
            Vector3Int cell = tilemap.WorldToCell(insidePoint);
            if (tilemap.GetTile(cell) != null) return cell;

            // 命中格子边角时，沿命中面的两侧寻找实际被击中的方块。
            for (int direction = -1; direction <= 1; direction += 2)
            {
                Vector2 adjacentPoint = insidePoint +
                                        tangent * (direction * SurfaceEpsilon * 2f);
                Vector3Int adjacentCell = tilemap.WorldToCell(adjacentPoint);
                if (tilemap.GetTile(adjacentCell) != null) return adjacentCell;
            }
            return cell;
        }

        static float CalculateCellStride(Tilemap tilemap, Vector2 tangent)
        {
            Vector3 localTangent = tilemap.transform.InverseTransformDirection(tangent);
            Vector3 cellSize = tilemap.layoutGrid.cellSize;
            float cellsPerUnitX = Mathf.Abs(cellSize.x) > 0.000001f
                ? Mathf.Abs(localTangent.x / cellSize.x) : 0f;
            float cellsPerUnitY = Mathf.Abs(cellSize.y) > 0.000001f
                ? Mathf.Abs(localTangent.y / cellSize.y) : 0f;
            float dominantCellsPerUnit = Mathf.Max(cellsPerUnitX, cellsPerUnitY);
            return dominantCellsPerUnit > 0.000001f ? 1f / dominantCellsPerUnit : 0f;
        }

        static bool TryGetSurfaceCellInterval(
            Tilemap tilemap,
            Vector3Int cell,
            Vector2 linePoint,
            Vector2 tangent,
            out float minProjection,
            out float maxProjection)
        {
            minProjection = float.PositiveInfinity;
            maxProjection = float.NegativeInfinity;
            Vector2 origin = tilemap.CellToWorld(cell);
            Vector2 cellX = (Vector2)tilemap.CellToWorld(cell + Vector3Int.right) - origin;
            Vector2 cellY = (Vector2)tilemap.CellToWorld(cell + Vector3Int.up) - origin;
            Vector2 xCorner = origin + cellX;
            Vector2 yCorner = origin + cellY;
            Vector2 oppositeCorner = xCorner + cellY;

            IncludeEdgeInterval(linePoint, tangent, origin, xCorner,
                ref minProjection, ref maxProjection);
            IncludeEdgeInterval(linePoint, tangent, xCorner, oppositeCorner,
                ref minProjection, ref maxProjection);
            IncludeEdgeInterval(linePoint, tangent, oppositeCorner, yCorner,
                ref minProjection, ref maxProjection);
            IncludeEdgeInterval(linePoint, tangent, yCorner, origin,
                ref minProjection, ref maxProjection);
            return minProjection <= maxProjection;
        }

        static void IncludeEdgeInterval(
            Vector2 linePoint,
            Vector2 tangent,
            Vector2 edgeStart,
            Vector2 edgeEnd,
            ref float minProjection,
            ref float maxProjection)
        {
            Vector2 edge = edgeEnd - edgeStart;
            Vector2 relative = edgeStart - linePoint;
            float denominator = Cross(tangent, edge);
            if (Mathf.Abs(denominator) <= 0.000001f)
            {
                // 墙面与格子边重合时，直接采用这条格子边的两个端点。
                if (Mathf.Abs(Cross(relative, tangent)) > SurfaceEpsilon) return;
                IncludeProjection(edgeStart, tangent, ref minProjection, ref maxProjection);
                IncludeProjection(edgeEnd, tangent, ref minProjection, ref maxProjection);
                return;
            }

            float edgeRatio = Cross(relative, tangent) / denominator;
            if (edgeRatio < -SurfaceEpsilon || edgeRatio > 1f + SurfaceEpsilon) return;
            float lineDistance = Cross(relative, edge) / denominator;
            float projection = Vector2.Dot(linePoint, tangent) + lineDistance;
            minProjection = Mathf.Min(minProjection, projection);
            maxProjection = Mathf.Max(maxProjection, projection);
        }

        static void IncludeProjection(
            Vector2 point,
            Vector2 tangent,
            ref float minProjection,
            ref float maxProjection)
        {
            float projection = Vector2.Dot(point, tangent);
            minProjection = Mathf.Min(minProjection, projection);
            maxProjection = Mathf.Max(maxProjection, projection);
        }

        static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        static bool AreAdjacent(Vector3Int a, Vector3Int b)
        {
            int deltaX = Mathf.Abs(a.x - b.x);
            int deltaY = Mathf.Abs(a.y - b.y);
            return deltaX <= 1 && deltaY <= 1 && deltaX + deltaY > 0;
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
            Vector2 insidePoint = hit.point - hit.normal.normalized * SurfaceEpsilon;
            TileBase tile = tilemap.GetTile(tilemap.WorldToCell(insidePoint));
            return CanPlaceTile(side, tile);
        }

        static bool CanPlaceTile(CaptureGroupSide side, TileBase tile)
        {
            if (tile is IPortalPlacementSurface tileSurface)
                return tileSurface.CanPlacePortal(side);

            // 兼容未迁移的内置 Tile；空格和无法解析的方块安全地拒绝。
            return tile != null;
        }
    }
}
