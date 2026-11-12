using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// Boolean cut-out for a portal.
    ///
    /// Cells inside the portal's displayed half are swapped to collision-free variants of
    /// the same tiles, so the real terrain stops being solid there and only the collision
    /// copied through the portal remains. Cells are restored when the region moves away.
    /// </summary>
    public class PortalCutout : MonoBehaviour
    {
        [Tooltip("Terrain whose cells are swapped.")]
        public Tilemap terrain;

        [Tooltip("Group that owns the portal region.")]
        public CaptureDisplayGroup group;

        [Tooltip("Collision-free variants of the terrain tiles.")]
        public Tile[] noColliderTiles;

        [Tooltip("Size of the capture area in world units.")]
        public Vector2 areaSize = new Vector2(24f, 13.5f);

        [Tooltip("True: cut the left half (group B). False: cut the right half (group A).")]
        public bool cutLeftHalf = false;

        readonly Dictionary<Vector3Int, TileBase> _swapped = new Dictionary<Vector3Int, TileBase>();
        readonly Dictionary<Sprite, Tile> _variants = new Dictionary<Sprite, Tile>();
        readonly List<Vector3Int> _restore = new List<Vector3Int>();
        bool _ready;

        void LateUpdate()
        {
            if (terrain == null || group == null) return;

            if (!_ready) BuildLookup();
            if (_variants.Count == 0) return;

            Vector3 centre = group.transform.position;
            float halfHeight = areaSize.y * 0.5f;
            int lineX = Mathf.FloorToInt(centre.x);
            int span = Mathf.CeilToInt(areaSize.x * 0.5f);

            int firstX = cutLeftHalf ? lineX - span : lineX;
            int lastX = cutLeftHalf ? lineX - 1 : lineX + span - 1;
            int minY = Mathf.FloorToInt(centre.y - halfHeight);
            int maxY = Mathf.CeilToInt(centre.y + halfHeight);

            for (int x = firstX; x <= lastX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (_swapped.ContainsKey(cell)) continue;

                    var tile = terrain.GetTile(cell) as Tile;
                    if (tile == null || tile.sprite == null) continue;
                    if (!_variants.TryGetValue(tile.sprite, out Tile variant)) continue;

                    _swapped[cell] = tile;
                    terrain.SetTile(cell, variant);
                }
            }

            _restore.Clear();
            foreach (var pair in _swapped)
            {
                var cell = pair.Key;
                bool inside = cell.x >= firstX && cell.x <= lastX && cell.y >= minY && cell.y <= maxY;
                if (!inside) _restore.Add(cell);
            }

            foreach (var cell in _restore)
            {
                terrain.SetTile(cell, _swapped[cell]);
                _swapped.Remove(cell);
            }
        }

        void BuildLookup()
        {
            _ready = true;
            _variants.Clear();

            if (noColliderTiles == null) return;
            foreach (var tile in noColliderTiles)
            {
                if (tile != null && tile.sprite != null) _variants[tile.sprite] = tile;
            }
        }
    }
}
