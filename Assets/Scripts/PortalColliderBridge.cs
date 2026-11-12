using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// Portal collision bridge.
    ///
    /// Every frame it samples the terrain collision boxes of one half of this group's
    /// area and copies them into the other group, so the terrain visible inside the other
    /// portal can be walked on.
    /// </summary>
    public class PortalColliderBridge : MonoBehaviour
    {
        [Tooltip("Terrain the collision boxes are sampled from.")]
        public Tilemap terrain;

        [Tooltip("Group whose area is sampled.")]
        public CaptureDisplayGroup source;

        [Tooltip("Group that receives the copied collision boxes.")]
        public Transform target;

        [Tooltip("Size of the sampled area in world units.")]
        public Vector2 areaSize = new Vector2(24f, 13.5f);

        [Tooltip("Layer for the copied colliders (the player's ground layer).")]
        public int colliderLayer = 6;

        readonly List<BoxCollider2D> _pool = new List<BoxCollider2D>();
        int _used;

        void LateUpdate()
        {
            if (terrain == null || source == null || target == null) return;

            _used = 0;

            Vector3 centre = source.transform.position;
            float halfWidth = areaSize.x * 0.5f;
            float halfHeight = areaSize.y * 0.5f;

            int minX = Mathf.FloorToInt(centre.x - halfWidth);
            int maxX = Mathf.CeilToInt(centre.x + halfWidth);
            int minY = Mathf.FloorToInt(centre.y - halfHeight);
            int maxY = Mathf.CeilToInt(centre.y + halfHeight);

            for (int x = minX; x <= maxX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (!terrain.HasTile(cell)) continue;

                    Vector3 cellCentre = terrain.CellToWorld(cell) + new Vector3(0.5f, 0.5f, 0f);
                    Vector3 offset = cellCentre - centre;

                    BoxCollider2D box = GetCollider();
                    box.transform.position = target.position + offset;
                    box.transform.rotation = Quaternion.identity;
                }
            }

            for (int i = _used; i < _pool.Count; i++)
            {
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
            }
        }

        BoxCollider2D GetCollider()
        {
            if (_used < _pool.Count)
            {
                BoxCollider2D existing = _pool[_used++];
                if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
                return existing;
            }

            var go = new GameObject("Portal Collider");
            go.transform.SetParent(transform, true);
            go.layer = colliderLayer;

            var box = go.AddComponent<BoxCollider2D>();
            box.size = Vector2.one;

            _pool.Add(box);
            _used++;
            return box;
        }
    }
}
