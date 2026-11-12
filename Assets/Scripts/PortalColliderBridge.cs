using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// Portal collision bridge.
    ///
    /// The whole centre line of a group is the portal entrance. Every frame this bridge
    /// samples the terrain collision shapes within <see cref="sampleCells"/> cells on one
    /// side of that line and copies them 1:1 (no grid snapping) into the other group,
    /// offset relative to the two centres.
    /// </summary>
    public class PortalColliderBridge : MonoBehaviour
    {
        [Tooltip("Terrain the collision shapes are sampled from.")]
        public Tilemap terrain;

        [Tooltip("Group whose entrance line is sampled.")]
        public CaptureDisplayGroup source;

        [Tooltip("Group that receives the copied collision shapes.")]
        public Transform target;

        [Tooltip("Size of the sampled area in world units.")]
        public Vector2 areaSize = new Vector2(24f, 13.5f);

        [Tooltip("How many cells beyond the entrance line are sampled.")]
        public int sampleCells = 4;

        [Tooltip("True: sample the left side of the entrance line. False: the right side.")]
        public bool sampleLeftSide = true;

        [Tooltip("Layer for the copied colliders (the player's ground layer).")]
        public int colliderLayer = 6;

        readonly List<PolygonCollider2D> _pool = new List<PolygonCollider2D>();
        readonly List<Vector2> _points = new List<Vector2>();
        int _used;

        void LateUpdate()
        {
            if (terrain == null || source == null || target == null) return;

            _used = 0;

            Vector3 centre = source.transform.position;
            float halfHeight = areaSize.y * 0.5f;

            int lineX = Mathf.FloorToInt(centre.x);
            int firstX = sampleLeftSide ? lineX - sampleCells : lineX;
            int lastX = sampleLeftSide ? lineX - 1 : lineX + sampleCells - 1;
            int minY = Mathf.FloorToInt(centre.y - halfHeight);
            int maxY = Mathf.CeilToInt(centre.y + halfHeight);

            for (int x = firstX; x <= lastX; x++)
            {
                for (int y = minY; y <= maxY; y++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    var tile = terrain.GetTile(cell) as Tile;
                    if (tile == null || tile.sprite == null) continue;

                    // Real shape of the tile, in sprite pixels relative to the pivot.
                    _points.Clear();
                    if (tile.sprite.GetPhysicsShape(0, _points) == 0 || _points.Count < 3) continue;

                    float pixelsPerUnit = tile.sprite.pixelsPerUnit;
                    var shape = new Vector2[_points.Count];
                    for (int i = 0; i < _points.Count; i++)
                    {
                        shape[i] = _points[i] / pixelsPerUnit;
                    }

                    Vector3 cellCentre = terrain.CellToWorld(cell) + new Vector3(0.5f, 0.5f, 0f);
                    Vector3 offset = cellCentre - centre;

                    PolygonCollider2D polygon = GetCollider();
                    polygon.transform.position = target.position + offset;
                    polygon.transform.rotation = Quaternion.identity;
                    polygon.transform.localScale = Vector3.one;
                    polygon.pathCount = 1;
                    polygon.SetPath(0, shape);
                }
            }

            for (int i = _used; i < _pool.Count; i++)
            {
                if (_pool[i].gameObject.activeSelf) _pool[i].gameObject.SetActive(false);
            }
        }

        PolygonCollider2D GetCollider()
        {
            if (_used < _pool.Count)
            {
                PolygonCollider2D existing = _pool[_used++];
                if (!existing.gameObject.activeSelf) existing.gameObject.SetActive(true);
                return existing;
            }

            var go = new GameObject("Portal Collider");
            go.transform.SetParent(transform, true);
            go.layer = colliderLayer;

            var polygon = go.AddComponent<PolygonCollider2D>();
            _pool.Add(polygon);
            _used++;
            return polygon;
        }
    }
}
