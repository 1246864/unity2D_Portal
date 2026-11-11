using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// Live copy of a <see cref="CaptureRegion"/>.
    ///
    /// Every frame the content inside the region (tiles and sprites) is mirrored to
    /// <see cref="destination"/> using pooled, display-only sprites. The region can be
    /// moved and resized at runtime; the mirror follows without allocating.
    /// </summary>
    public class RegionMirror : PlatformerBehaviour
    {
        [Header("Source")]
        [Tooltip("The window that is mirrored. Can be moved or resized at runtime.")]
        public CaptureRegion region;

        [Tooltip("Terrain the tiles are read from.")]
        public Tilemap sourceTerrain;

        [Header("Output")]
        [Tooltip("World position where the mirrored copy is drawn.")]
        public Transform destination;

        public int terrainSortingOrder = 0;
        public int spriteSortingOrder = 10;

        readonly List<GameObject> _active = new List<GameObject>();
        SimpleObjectPool _pool;

        /// <summary>Sprites currently shown by the mirror.</summary>
        public int VisualCount => _active.Count;

        /// <summary>Total objects created by the pool (it grows only when needed).</summary>
        public int PoolCreated => _pool?.Created ?? 0;

        SimpleObjectPool Pool
        {
            get
            {
                if (_pool == null)
                {
                    _pool = new SimpleObjectPool(CreateVisual, transform, 32);
                }

                return _pool;
            }
        }

        void LateUpdate()
        {
            if (region == null || destination == null) return;
            Rebuild();
        }

        /// <summary>Rebuilds the mirror immediately (also used by preview rendering).</summary>
        public void RebuildNow()
        {
            if (region == null || destination == null) return;
            Rebuild();
        }

        /// <summary>Hides every pooled sprite.</summary>
        public void ClearVisuals()
        {
            _pool?.ReleaseAll(_active);
        }

        void Rebuild()
        {
            SimpleObjectPool pool = Pool;
            pool.ReleaseAll(_active);

            Bounds area = region.WorldBounds;
            Vector3 origin = destination.position;

            if (sourceTerrain != null)
            {
                Vector3Int min = new Vector3Int(
                    Mathf.FloorToInt(area.min.x), Mathf.FloorToInt(area.min.y), 0);
                Vector3Int max = new Vector3Int(
                    Mathf.CeilToInt(area.max.x), Mathf.CeilToInt(area.max.y), 0);

                for (int x = min.x; x <= max.x; x++)
                {
                    for (int y = min.y; y <= max.y; y++)
                    {
                        var cell = new Vector3Int(x, y, 0);
                        var tile = sourceTerrain.GetTile(cell) as Tile;
                        if (tile == null || tile.sprite == null) continue;

                        GameObject visual = pool.Get();
                        var renderer = visual.GetComponent<SpriteRenderer>();
                        renderer.sprite = tile.sprite;
                        renderer.color = sourceTerrain.GetColor(cell);
                        renderer.flipX = false;
                        renderer.flipY = false;
                        renderer.sortingOrder = terrainSortingOrder;

                        visual.transform.position = origin + new Vector3(x - min.x, y - min.y, 0f);
                        visual.transform.rotation = Quaternion.identity;
                        visual.transform.localScale = Vector3.one;
                        _active.Add(visual);
                    }
                }
            }

            // Sprites (player, props) that overlap the window.
            foreach (var sprite in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (sprite.transform.IsChildOf(transform)) continue;
                if (sprite.sprite == null) continue;
                if (!sprite.bounds.Intersects(area)) continue;

                GameObject visual = pool.Get();
                var renderer = visual.GetComponent<SpriteRenderer>();
                renderer.sprite = sprite.sprite;
                renderer.color = sprite.color;
                renderer.flipX = sprite.flipX;
                renderer.flipY = sprite.flipY;
                renderer.sortingOrder = spriteSortingOrder;

                visual.transform.position = origin + (sprite.transform.position - area.min);
                visual.transform.rotation = sprite.transform.rotation;
                visual.transform.localScale = sprite.transform.lossyScale;
                _active.Add(visual);
            }
        }

        GameObject CreateVisual()
        {
            var go = new GameObject("Mirror Visual");
            go.AddComponent<SpriteRenderer>();
            return go;
        }
    }
}
