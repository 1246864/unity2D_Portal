using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// Live copy of a <see cref="CaptureRegion"/>.
    ///
    /// Static content (terrain tiles and unmarked sprites) is copied once and only
    /// rebuilt when the window is created, moved or resized. Objects marked with
    /// <see cref="DynamicCaptureAttribute"/> (player, physics props) are copied every
    /// frame. Both use pooled, display-only sprites.
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

        readonly List<GameObject> _staticVisuals = new List<GameObject>();
        readonly List<GameObject> _dynamicVisuals = new List<GameObject>();
        SimpleObjectPool _pool;

        bool _hasWindow;
        Vector3 _lastPosition;
        Vector2 _lastSize;

        /// <summary>Sprites currently shown by the mirror.</summary>
        public int VisualCount => _staticVisuals.Count + _dynamicVisuals.Count;

        /// <summary>How many times the static content was rebuilt (window changes).</summary>
        public int StaticRebuilds { get; private set; }

        /// <summary>How many times the dynamic content was rebuilt (every frame).</summary>
        public int DynamicRebuilds { get; private set; }

        /// <summary>Total objects created by the pool (it grows only when needed).</summary>
        public int PoolCreated => _pool?.Created ?? 0;

        SimpleObjectPool Pool
        {
            get
            {
                if (_pool == null) _pool = new SimpleObjectPool(CreateVisual, transform, 32);
                return _pool;
            }
        }

        void LateUpdate()
        {
            if (region == null || destination == null) return;

            bool windowChanged = !_hasWindow
                || region.transform.position != _lastPosition
                || region.size != _lastSize;

            if (windowChanged)
            {
                RebuildStatic();
                RememberWindow();
            }

            RebuildDynamic();
        }

        /// <summary>Rebuilds everything immediately (also used by preview rendering).</summary>
        public void RebuildNow()
        {
            if (region == null || destination == null) return;

            RebuildStatic();
            RebuildDynamic();
            RememberWindow();
        }

        /// <summary>Hides every pooled sprite.</summary>
        public void ClearVisuals()
        {
            if (_pool == null) return;

            _pool.ReleaseAll(_staticVisuals);
            _pool.ReleaseAll(_dynamicVisuals);
        }

        void RememberWindow()
        {
            _lastPosition = region.transform.position;
            _lastSize = region.size;
            _hasWindow = true;
        }

        void RebuildStatic()
        {
            SimpleObjectPool pool = Pool;
            pool.ReleaseAll(_staticVisuals);

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
                        _staticVisuals.Add(visual);
                    }
                }
            }

            foreach (var sprite in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (!IsMirrorable(sprite, area)) continue;
                if (IsDynamic(sprite.gameObject)) continue;
                _staticVisuals.Add(CreateSpriteVisual(sprite, area, origin, pool));
            }

            StaticRebuilds++;
        }

        void RebuildDynamic()
        {
            SimpleObjectPool pool = Pool;
            pool.ReleaseAll(_dynamicVisuals);

            Bounds area = region.WorldBounds;
            Vector3 origin = destination.position;

            foreach (var sprite in UnityEngine.Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (!IsMirrorable(sprite, area)) continue;
                if (!IsDynamic(sprite.gameObject)) continue;
                _dynamicVisuals.Add(CreateSpriteVisual(sprite, area, origin, pool));
            }

            DynamicRebuilds++;
        }

        bool IsMirrorable(SpriteRenderer sprite, Bounds area)
        {
            if (sprite.transform.IsChildOf(transform)) return false;
            if (sprite.sprite == null) return false;
            return sprite.bounds.Intersects(area);
        }

        GameObject CreateSpriteVisual(SpriteRenderer source, Bounds area, Vector3 origin, SimpleObjectPool pool)
        {
            GameObject visual = pool.Get();
            var renderer = visual.GetComponent<SpriteRenderer>();
            renderer.sprite = source.sprite;
            renderer.color = source.color;
            renderer.flipX = source.flipX;
            renderer.flipY = source.flipY;
            renderer.sortingOrder = spriteSortingOrder;

            visual.transform.position = origin + (source.transform.position - area.min);
            visual.transform.rotation = source.transform.rotation;
            visual.transform.localScale = source.transform.lossyScale;
            return visual;
        }

        static bool IsDynamic(GameObject go)
        {
            foreach (var behaviour in go.GetComponents<MonoBehaviour>())
            {
                if (behaviour == null) continue;
                if (Attribute.IsDefined(behaviour.GetType(), typeof(DynamicCaptureAttribute), true))
                    return true;
            }

            return false;
        }

        GameObject CreateVisual()
        {
            var go = new GameObject("Mirror Visual");
            go.AddComponent<SpriteRenderer>();
            return go;
        }
    }
}
