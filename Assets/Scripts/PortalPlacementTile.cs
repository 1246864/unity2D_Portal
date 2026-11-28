using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>统一描述一个表面对指定传送门的放置资格。</summary>
    public interface IPortalPlacementSurface
    {
        bool CanPlacePortal(CaptureGroupSide side);
    }

    /// <summary>在 Tile 资产上声明该方块是否允许放置传送门。</summary>
    [CreateAssetMenu(
        fileName = "PortalPlacementTile",
        menuName = "Platformer/Portal Placement Tile")]
    public sealed class PortalPlacementTile : Tile, IPortalPlacementSurface
    {
        [SerializeField]
        [InspectorName("允许放置传送门")]
        bool allowPortalPlacement = true;

        public bool AllowPortalPlacement
        {
            get => allowPortalPlacement;
            set => allowPortalPlacement = value;
        }

        public bool CanPlacePortal(CaptureGroupSide side) => allowPortalPlacement;
    }
}
