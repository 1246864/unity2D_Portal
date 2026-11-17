using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// 用独立 Tilemap 层实现地图描边，替代原来的全屏描边 shader。
    ///
    /// 逐格枚举上下左右邻居：
    /// - 地形格：邻格为空或颜色不同 → 在该侧画 1px 内描边；
    /// - 空格：邻格是地形（斜坡除外）→ 在该侧画 1px 外描边；
    /// - 斜坡格：铺对应的斜线描边瓦片。
    ///
    /// 地形瓦片本身（颜色、瓦片内描边）保持不变，本组件只负责生成描边层。
    /// </summary>
    [RequireComponent(typeof(Tilemap))]
    public class TilemapOutline : MonoBehaviour
    {
        [Tooltip("描边颜色")]
        public Color outlineColor = new Color(0.70f, 0.79f, 0.92f, 1f);

        [Tooltip("描边层排序层")]
        public string sortingLayerName = "Default";

        [Tooltip("描边层排序序号（地形为 0）")]
        public int sortingOrder = 1;

        [Tooltip("描边线宽（像素，1 = 单像素）")]
        [Range(1, 4)] public int lineWidth = 1;

        const int TextureSize = 32;
        const int PixelsPerUnit = 32;

        static readonly Vector3Int[] Directions =
        {
            Vector3Int.up,
            Vector3Int.right,
            Vector3Int.down,
            Vector3Int.left
        };

        Tilemap _terrain;
        Tilemap _outline;
        Tile[] _edgeTiles;
        Tile[] _slopeTiles;
        readonly List<Object> _owned = new List<Object>();
        Color _builtColor;
        int _builtLineWidth;
        bool _hasBuiltTileParameters;

        void Awake()
        {
            _terrain = GetComponent<Tilemap>();
        }

        void OnEnable()
        {
            _terrain = GetComponent<Tilemap>();
            EnsureOutlineLayer();
            Build();
        }

        void OnDisable()
        {
            DestroyOutlineLayer();
            DestroyOwned();
        }

        void OnDestroy()
        {
            DestroyOwned();
        }

        /// <summary>按当前地形重新生成描边层。</summary>
        public void Build()
        {
            if (_terrain == null) _terrain = GetComponent<Tilemap>();
            if (_terrain == null) return;

            if (_hasBuiltTileParameters &&
                (_builtColor != outlineColor || _builtLineWidth != lineWidth))
            {
                DestroyOwned();
            }

            EnsureOutlineLayer();
            EnsureTiles();
            _builtColor = outlineColor;
            _builtLineWidth = lineWidth;
            _hasBuiltTileParameters = true;
            if (_outline == null) return;

            _outline.ClearAllTiles();

            BoundsInt bounds = _terrain.cellBounds;
            for (int y = bounds.yMin; y < bounds.yMax; y++)
            {
                for (int x = bounds.xMin; x < bounds.xMax; x++)
                {
                    var cell = new Vector3Int(x, y, 0);
                    if (!_terrain.HasTile(cell)) continue;

                    if (IsSlope(cell))
                    {
                        _outline.SetTile(cell, SlopeTile(cell));
                        continue;
                    }

                    // 只在实心地形格内部画 1px；空格不再额外画外圈，
                    // 避免 32px 贴图在 2 倍屏幕缩放下变成双倍粗的线。
                    bool dark = IsDark(cell);
                    int mask = 0;
                    for (int d = 0; d < Directions.Length; d++)
                    {
                        Vector3Int neighbor = cell + Directions[d];
                        if (!_terrain.HasTile(neighbor)) mask |= 1 << d;
                        else if (dark && IsDark(neighbor) != dark) mask |= 1 << d;
                    }

                    if (mask != 0) _outline.SetTile(cell, _edgeTiles[mask]);
                }
            }
        }

        bool IsDark(Vector3Int cell)
        {
            TileBase tile = _terrain.GetTile(cell);
            return tile != null && tile.name.IndexOf("Dark", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        bool IsSlope(Vector3Int cell)
        {
            TileBase tile = _terrain.GetTile(cell);
            return tile != null && tile.name.IndexOf("Slope", System.StringComparison.OrdinalIgnoreCase) >= 0;
        }

        Tile SlopeTile(Vector3Int cell)
        {
            string name = _terrain.GetTile(cell).name;
            if (name.IndexOf("UpLeft", System.StringComparison.OrdinalIgnoreCase) >= 0) return _slopeTiles[1];
            if (name.IndexOf("DownRight", System.StringComparison.OrdinalIgnoreCase) >= 0) return _slopeTiles[2];
            if (name.IndexOf("DownLeft", System.StringComparison.OrdinalIgnoreCase) >= 0) return _slopeTiles[3];
            return _slopeTiles[0]; // UpRight
        }

        void EnsureOutlineLayer()
        {
            if (_outline == null)
            {
                Transform parent = transform.parent;
                if (parent == null) return;

                Transform existing = parent.Find("Outline");
                GameObject go = existing != null ? existing.gameObject : new GameObject("Outline");
                go.hideFlags = HideFlags.DontSave;
                go.transform.SetParent(parent, false);

                _outline = go.GetComponent<Tilemap>();
                if (_outline == null) _outline = go.AddComponent<Tilemap>();
            }

            var renderer = _outline.GetComponent<TilemapRenderer>();
            if (renderer == null) renderer = _outline.gameObject.AddComponent<TilemapRenderer>();
            renderer.sortingLayerName = sortingLayerName;
            renderer.sortingOrder = sortingOrder;
        }

        void DestroyOutlineLayer()
        {
            if (_outline == null) return;
            DestroyRuntimeObject(_outline.gameObject);
            _outline = null;
        }

        void EnsureTiles()
        {
            if (_edgeTiles != null && _slopeTiles != null) return;

            if (_edgeTiles == null)
            {
                _edgeTiles = new Tile[16];
                for (int mask = 0; mask < 16; mask++)
                    _edgeTiles[mask] = CreateTile(CreateEdgeSprite(mask));
            }

            if (_slopeTiles == null)
            {
                Sprite diagonalA = CreateSlopeSprite(true);   // 斜边 y = x
                Sprite diagonalB = CreateSlopeSprite(false);  // 斜边 y = 31 - x

                _slopeTiles = new Tile[4];
                _slopeTiles[0] = CreateTile(diagonalA); // UpRight
                _slopeTiles[1] = CreateTile(diagonalB); // UpLeft
                _slopeTiles[2] = CreateTile(diagonalB); // DownRight
                _slopeTiles[3] = CreateTile(diagonalA); // DownLeft
            }
        }

        Tile CreateTile(Sprite sprite)
        {
            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.sprite = sprite;
            tile.color = Color.white;
            tile.colliderType = Tile.ColliderType.None;
            tile.hideFlags = HideFlags.DontSave;
            _owned.Add(tile);
            return tile;
        }

        Sprite CreateEdgeSprite(int mask)
        {
            var pixels = new Color32[TextureSize * TextureSize];
            Color32 color = outlineColor;
            int width = Mathf.Clamp(lineWidth, 1, TextureSize / 2);

            for (int i = 0; i < width; i++)
            {
                if ((mask & 1) != 0) // 上
                    for (int x = 0; x < TextureSize; x++) pixels[(TextureSize - 1 - i) * TextureSize + x] = color;
                if ((mask & 2) != 0) // 右
                    for (int y = 0; y < TextureSize; y++) pixels[y * TextureSize + (TextureSize - 1 - i)] = color;
                if ((mask & 4) != 0) // 下
                    for (int x = 0; x < TextureSize; x++) pixels[i * TextureSize + x] = color;
                if ((mask & 8) != 0) // 左
                    for (int y = 0; y < TextureSize; y++) pixels[y * TextureSize + i] = color;
            }

            return CreateSprite(pixels);
        }

        Sprite CreateSlopeSprite(bool diagonalA)
        {
            var pixels = new Color32[TextureSize * TextureSize];
            Color32 color = outlineColor;

            int width = Mathf.Clamp(lineWidth, 1, TextureSize / 2);
            for (int i = 0; i < TextureSize; i++)
            {
                int x = i;
                int y = diagonalA ? i : TextureSize - 1 - i;
                for (int offset = 0; offset < width; offset++)
                {
                    int lineY = Mathf.Clamp(y + offset, 0, TextureSize - 1);
                    pixels[lineY * TextureSize + x] = color;
                }
            }

            return CreateSprite(pixels);
        }

        Sprite CreateSprite(Color32[] pixels)
        {
            var texture = new Texture2D(TextureSize, TextureSize, TextureFormat.RGBA32, false)
            {
                hideFlags = HideFlags.DontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            texture.SetPixels32(pixels);
            texture.Apply();
            _owned.Add(texture);

            var sprite = Sprite.Create(
                texture, new Rect(0f, 0f, TextureSize, TextureSize),
                new Vector2(0.5f, 0.5f), PixelsPerUnit);
            sprite.hideFlags = HideFlags.DontSave;
            _owned.Add(sprite);
            return sprite;
        }

        void DestroyOwned()
        {
            for (int i = 0; i < _owned.Count; i++)
            {
                if (_owned[i] != null) DestroyRuntimeObject(_owned[i]);
            }
            _owned.Clear();
            _edgeTiles = null;
            _slopeTiles = null;
            _hasBuiltTileParameters = false;
        }

        static void DestroyRuntimeObject(Object obj)
        {
            if (obj == null) return;
            if (Application.isPlaying) Destroy(obj);
            else DestroyImmediate(obj);
        }
    }
}
