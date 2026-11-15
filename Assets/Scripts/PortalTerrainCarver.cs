using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer
{
    /// <summary>
    /// 传送门激活时，从门的“中轴”出发，沿 facing 的反方向切掉一块地形碰撞。
    ///
    /// 矩形定义：
    /// - 起点：门的中心（PortalPair 下的 Portal A/B 根节点）；
    /// - 长度方向：门的朝向反向量（背向门面的方向），长度 = carveWidth；
    /// - 高度方向：与背向垂直，长度 = PortalHeight，居中。
    /// - 为了让玩家胶囊能贴到门面，额外向门面方向扩展 carveFrontMargin；
    ///   设为 0 就是严格从门中轴开始。
    ///
    /// 这不是按瓦片格整格删除，而是把矩形和每一格的 Sprite 物理形状做布尔差：
    /// 一个 1×1 方块可以被切成 0.5 格、甚至更小的碎片。视觉 Tile 保持不变；
    /// 被影响的格子从 TilemapCollider2D 中移除，剩余碰撞用运行时 PolygonCollider2D 补回。
    /// 这样不需要在运行时 OverridePhysicsShape，也不会在门禁用/重新启用后残留 carve 状态。
    ///
    /// 门停用/场景卸载时移除残余碰撞、恢复原 ColliderType，并重建 CompositeCollider2D。
    /// </summary>
    [RequireComponent(typeof(PortalPair))]
    public class PortalTerrainCarver : MonoBehaviour
    {
        [Header("地形引用（留空则自动查找）")]
        [Tooltip("关卡地形 Tilemap；一般不需要手动指定")]
        public Tilemap terrain;
        [Tooltip("地形上的 TilemapCollider2D")]
        public TilemapCollider2D terrainCollider;
        [Tooltip("地形上的 CompositeCollider2D")]
        public CompositeCollider2D composite;

        [Header("开洞矩形")]
        [Tooltip("沿门朝向反方向的长度（世界单位）")]
        [Min(0.01f)] public float carveWidth = 0.5f;

        [Tooltip("为了让玩家胶囊能贴到门面，额外向门面方向扩展的长度；0 = 严格从门中轴开始")]
        [Min(0f)] public float carveFrontMargin = 0.1f;

        readonly Dictionary<CaptureDisplayGroup, bool> _openPortals =
            new Dictionary<CaptureDisplayGroup, bool>();
        readonly Dictionary<Vector3Int, CarvedCell> _carved =
            new Dictionary<Vector3Int, CarvedCell>();
        readonly Dictionary<Sprite, List<Vector2[]>> _spriteShapes =
            new Dictionary<Sprite, List<Vector2[]>>();
        readonly List<CarveRect> _lastRects = new List<CarveRect>();

        bool _warnedNoTerrain;

        /// <summary>当前被切过碰撞的格子数。</summary>
        public int CarvedCellCount => _carved.Count;

        /// <summary>一个格子的原始碰撞信息，用于恢复。</summary>
        sealed class CarvedCell
        {
            public Tile.ColliderType originalColliderType;
            public GameObject residualCollider;
        }

        /// <summary>
        /// 有朝向的矩形。origin 是门中轴起点，axis 是“门背后”的单位向量，
        /// perp 与 axis 垂直；local.x ∈ [-frontMargin, length]，
        /// local.y ∈ [-height/2, height/2]。
        /// </summary>
        struct CarveRect
        {
            public Vector2 origin;
            public Vector2 axis;
            public Vector2 perp;
            public float frontMargin;
            public float length;
            public float height;

            public Vector2 ToLocal(Vector2 world)
            {
                Vector2 delta = world - origin;
                return new Vector2(Vector2.Dot(delta, axis), Vector2.Dot(delta, perp));
            }

            public Vector2 ToWorld(Vector2 local)
            {
                return origin + axis * local.x + perp * local.y;
            }
        }

        void Awake()
        {
            ResolveTerrain();
        }

        void OnEnable()
        {
            ResolveTerrain();

            // 重新启用时先把上一轮留下的运行时碰撞恢复掉，再按当前门状态重切。
            RestoreCells();
            _lastRects.Clear(); // 重新启用时强制按当前门状态重建一次
            Refresh();
        }

        void OnDisable()
        {
            RestoreAll();
        }

        void LateUpdate()
        {
            // 门移动、朝向改变、门洞高度改变时，矩形会变化。
            if (_openPortals.Count > 0) Refresh();
        }

        /// <summary>
        /// 由 CaptureDisplayGroup 在 OnEnable/OnDisable 时调用。
        /// </summary>
        public void SetPortal(CaptureDisplayGroup group, bool open)
        {
            if (group == null) return;

            if (open) _openPortals[group] = true;
            else _openPortals.Remove(group);

            Refresh();
        }

        /// <summary>立即按当前门状态重算一次。</summary>
        public void RefreshNow()
        {
            Refresh();
        }

        void Refresh()
        {
            if (!isActiveAndEnabled) return;

            // 场景卸载时 OnDisable 仍会触发；此时场景正在销毁，不能再查对象。
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;

            ResolveTerrain();
            if (terrain == null || terrainCollider == null) return;

            var rects = CollectCarveRects();
            if (SameRects(rects, _lastRects)) return;

            // 先全部恢复，再按最新矩形重新切。这样门移动/旋转时不会残留旧洞。
            RestoreCells();
            _lastRects.AddRange(rects);

            if (rects.Count > 0) ApplyCarve(rects);
            RebuildColliders();
        }

        /// <summary>收集所有激活门的背向矩形。</summary>
        List<CarveRect> CollectCarveRects()
        {
            var result = new List<CarveRect>();
            foreach (var pair in _openPortals)
            {
                CaptureDisplayGroup group = pair.Key;
                if (group == null || !group.isActiveAndEnabled) continue;

                // facing 的反方向就是门背后；矩形从这里开始向墙里延伸。
                // 每次 Refresh 都重新读位置与方向，门移动/旋转后会按新矩形重切。
                Vector2 back = group.BackDirection;
                if (back.sqrMagnitude < 0.000001f) continue;
                back.Normalize();

                result.Add(new CarveRect
                {
                    origin = group.transform.position,
                    axis = back,
                    perp = new Vector2(-back.y, back.x),
                    frontMargin = Mathf.Max(0f, carveFrontMargin),
                    length = Mathf.Max(0.01f, carveWidth),
                    height = Mathf.Max(0.01f, group.PortalHeight)
                });
            }

            // 字典遍历顺序不稳定，排序后再比较/应用，避免每帧误判为变化。
            result.Sort((a, b) =>
            {
                int c = a.origin.x.CompareTo(b.origin.x);
                if (c != 0) return c;
                c = a.origin.y.CompareTo(b.origin.y);
                if (c != 0) return c;
                c = a.axis.x.CompareTo(b.axis.x);
                if (c != 0) return c;
                return a.axis.y.CompareTo(b.axis.y);
            });
            return result;
        }

        static bool SameRects(List<CarveRect> a, List<CarveRect> b)
        {
            if (a.Count != b.Count) return false;
            for (int i = 0; i < a.Count; i++)
            {
                if ((a[i].origin - b[i].origin).sqrMagnitude > 0.0000001f) return false;
                if ((a[i].axis - b[i].axis).sqrMagnitude > 0.0000001f) return false;
                if ((a[i].perp - b[i].perp).sqrMagnitude > 0.0000001f) return false;
                if (Mathf.Abs(a[i].frontMargin - b[i].frontMargin) > 0.0001f) return false;
                if (Mathf.Abs(a[i].length - b[i].length) > 0.0001f) return false;
                if (Mathf.Abs(a[i].height - b[i].height) > 0.0001f) return false;
            }
            return true;
        }

        /// <summary>遍历地形格，对每一格的碰撞形状依次减去所有矩形。</summary>
        void ApplyCarve(List<CarveRect> rects)
        {
            BoundsInt bounds = terrain.cellBounds;
            foreach (Vector3Int cell in bounds.allPositionsWithin)
            {
                if (!terrain.HasTile(cell)) continue;

                Tile.ColliderType colliderType = terrain.GetColliderType(cell);
                if (colliderType == Tile.ColliderType.None) continue;

                if (!(terrain.GetTile(cell) is Tile tile) || tile.sprite == null) continue;

                Vector2 center = terrain.GetCellCenterWorld(cell);
                List<Vector2[]> worldShapes = GetWorldShapes(tile.sprite, center, colliderType);
                if (worldShapes.Count == 0) continue;

                float originalArea = 0f;
                var remaining = new List<Vector2[]>();

                foreach (Vector2[] shape in worldShapes)
                {
                    originalArea += Mathf.Abs(SignedArea(shape));

                    // 一个格子的物理形状可能是多个轮廓；逐个和所有矩形做差。
                    var pieces = new List<Vector2[]> { shape };
                    foreach (CarveRect rect in rects)
                    {
                        var next = new List<Vector2[]>();
                        foreach (Vector2[] piece in pieces)
                            SubtractRect(piece, rect, next);
                        pieces = next;
                        if (pieces.Count == 0) break;
                    }

                    remaining.AddRange(pieces);
                }

                float remainingArea = 0f;
                foreach (Vector2[] shape in remaining)
                    remainingArea += Mathf.Abs(SignedArea(shape));

                // 没有真正切到这一格，保持原样。
                if (Mathf.Abs(originalArea - remainingArea) <= 0.0001f) continue;

                // 不再替换 Tile/Sprite，只把这一格从 TilemapCollider 里移除。
                // 剩余碰撞用独立的 PolygonCollider2D 补回来，避免运行时改 Sprite 物理形状。
                terrain.SetColliderType(cell, Tile.ColliderType.None);

                GameObject residual = null;
                if (remaining.Count > 0)
                {
                    residual = new GameObject("Carved Collider " + cell.x + "," + cell.y);
                    residual.layer = terrain.gameObject.layer;
                    residual.transform.SetParent(terrain.transform, true);
                    residual.transform.position = center;
                    residual.transform.rotation = Quaternion.identity;
                    residual.transform.localScale = Vector3.one;

                    var polygon = residual.AddComponent<PolygonCollider2D>();
                    polygon.pathCount = remaining.Count;
                    for (int i = 0; i < remaining.Count; i++)
                    {
                        Vector2[] shape = remaining[i];
                        var points = new Vector2[shape.Length];
                        for (int j = 0; j < shape.Length; j++)
                            points[j] = residual.transform.InverseTransformPoint(shape[j]);
                        polygon.SetPath(i, points);
                    }
                }

                _carved[cell] = new CarvedCell
                {
                    originalColliderType = colliderType,
                    residualCollider = residual
                };
            }
        }

        // ------------------------------------------------------------------
        // 矩形布尔差
        // ------------------------------------------------------------------

        /// <summary>
        /// 把世界空间凸多边形减去一个背向矩形。矩形外的区域拆成四个互不相交的区域：
        /// 左侧、右侧、矩形正下方的窄条、矩形正上方的窄条，分别裁剪后拼起来，
        /// 因此可以切出半格、四分之一格，而不需要和 Tilemap 网格对齐。
        /// </summary>
        static void SubtractRect(Vector2[] worldPolygon, CarveRect rect, List<Vector2[]> output)
        {
            var local = new List<Vector2>(worldPolygon.Length);
            foreach (Vector2 p in worldPolygon)
                local.Add(rect.ToLocal(p));

            float halfHeight = rect.height * 0.5f;
            float front = -rect.frontMargin;

            // 1) 矩形左侧（local.x <= front）
            AddPiece(ClipHalfPlane(local, 0, front, false), rect, output);

            // 2) 矩形右侧（local.x >= length）
            AddPiece(ClipHalfPlane(local, 0, rect.length, true), rect, output);

            // 3) 矩形正下方窄条（front <= local.x <= length 且 local.y <= -h/2）
            AddPiece(ClipBox(local, front, rect.length, float.NegativeInfinity, -halfHeight),
                rect, output);

            // 4) 矩形正上方窄条（front <= local.x <= length 且 local.y >= +h/2）
            AddPiece(ClipBox(local, front, rect.length, halfHeight, float.PositiveInfinity),
                rect, output);
        }

        static void AddPiece(List<Vector2> localPolygon, CarveRect rect, List<Vector2[]> output)
        {
            if (localPolygon == null || localPolygon.Count < 3) return;
            if (Mathf.Abs(SignedArea(localPolygon)) <= 0.000001f) return;

            var world = new Vector2[localPolygon.Count];
            for (int i = 0; i < localPolygon.Count; i++)
                world[i] = rect.ToWorld(localPolygon[i]);
            output.Add(world);
        }

        /// <summary>沿局部 X 或 Y 轴做半平面裁剪。axis=0 为 X，axis=1 为 Y。</summary>
        static List<Vector2> ClipHalfPlane(List<Vector2> polygon, int axis, float value, bool keepGreater)
        {
            if (polygon == null || polygon.Count < 3) return null;

            var output = new List<Vector2>(polygon.Count + 4);
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];

                float da = (axis == 0 ? a.x : a.y) - value;
                float db = (axis == 0 ? b.x : b.y) - value;
                bool ain = keepGreater ? da >= -0.000001f : da <= 0.000001f;
                bool bin = keepGreater ? db >= -0.000001f : db <= 0.000001f;

                if (ain) output.Add(a);
                if (ain == bin) continue;

                float denominator = da - db;
                if (Mathf.Abs(denominator) < 0.0000001f) continue;
                float t = Mathf.Clamp01(da / denominator);
                output.Add(Vector2.Lerp(a, b, t));
            }

            return output.Count >= 3 && Mathf.Abs(SignedArea(output)) > 0.000001f
                ? output
                : null;
        }

        /// <summary>裁剪到局部矩形 [xMin,xMax] × [yMin,yMax]。</summary>
        static List<Vector2> ClipBox(List<Vector2> polygon, float xMin, float xMax, float yMin, float yMax)
        {
            List<Vector2> result = ClipHalfPlane(polygon, 0, xMin, true);
            result = ClipHalfPlane(result, 0, xMax, false);
            result = ClipHalfPlane(result, 1, yMin, true);
            result = ClipHalfPlane(result, 1, yMax, false);
            return result;
        }

        static float SignedArea(List<Vector2> polygon)
        {
            if (polygon == null || polygon.Count < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < polygon.Count; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        static float SignedArea(Vector2[] polygon)
        {
            if (polygon == null || polygon.Length < 3) return 0f;
            float area = 0f;
            for (int i = 0; i < polygon.Length; i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[(i + 1) % polygon.Length];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        // ------------------------------------------------------------------
        // 从 Sprite 物理形状创建“被裁过”的运行时 Tile
        // ------------------------------------------------------------------

        /// <summary>
        /// 取一格地形的世界空间物理形状。Sprite 物理形状的顶点是相对轴心、
        /// 以 Sprite 尺寸为基准的坐标（完整方块是 ±0.5），乘以 bounds.size
        /// 再加格子中心即可得到世界坐标。
        /// </summary>
        List<Vector2[]> GetWorldShapes(Sprite sprite, Vector2 center, Tile.ColliderType colliderType)
        {
            var result = new List<Vector2[]>();
            List<Vector2[]> localShapes = GetLocalShapes(sprite);

            if (localShapes.Count == 0)
            {
                // Sprite 没有物理轮廓时，Grid 类型按整格处理；Sprite 类型视为无碰撞。
                if (colliderType != Tile.ColliderType.Grid) return result;
                result.Add(new[]
                {
                    center + new Vector2(-0.5f, -0.5f),
                    center + new Vector2(0.5f, -0.5f),
                    center + new Vector2(0.5f, 0.5f),
                    center + new Vector2(-0.5f, 0.5f)
                });
                return result;
            }

            Vector2 size = sprite.bounds.size;
            foreach (Vector2[] local in localShapes)
            {
                var world = new Vector2[local.Length];
                for (int i = 0; i < local.Length; i++)
                {
                    world[i] = new Vector2(
                        center.x + local[i].x * size.x,
                        center.y + local[i].y * size.y);
                }
                result.Add(world);
            }
            return result;
        }

        List<Vector2[]> GetLocalShapes(Sprite sprite)
        {
            if (_spriteShapes.TryGetValue(sprite, out List<Vector2[]> cached)) return cached;

            var shapes = new List<Vector2[]>();
            int count = sprite.GetPhysicsShapeCount();
            for (int i = 0; i < count; i++)
            {
                var buffer = new List<Vector2>();
                int n = sprite.GetPhysicsShape(i, buffer);
                if (n < 3) continue;

                var shape = new Vector2[n];
                for (int j = 0; j < n; j++) shape[j] = buffer[j];
                shapes.Add(shape);
            }

            _spriteShapes[sprite] = shapes;
            return shapes;
        }


        // ------------------------------------------------------------------
        // 恢复与重建
        // ------------------------------------------------------------------

        /// <summary>把所有被切过的格子恢复原 Tile/ColliderType，并销毁运行时资源。</summary>
        void RestoreCells()
        {
            foreach (var pair in _carved)
            {
                CarvedCell entry = pair.Value;
                if (entry.residualCollider != null)
                {
                    entry.residualCollider.SetActive(false);
                    Destroy(entry.residualCollider);
                }

                if (terrain != null)
                    terrain.SetColliderType(pair.Key, entry.originalColliderType);
            }

            _carved.Clear();
            _lastRects.Clear();
        }

        void RestoreAll()
        {
            RestoreCells();

            if (terrain != null && terrainCollider != null &&
                gameObject.scene.IsValid() && gameObject.scene.isLoaded)
            {
                RebuildColliders();
            }
        }

        /// <summary>
        /// TilemapCollider2D / CompositeCollider2D 按批处理更新，改完要显式重建。
        /// 注意不能调用 Tilemap.RefreshAllTiles()：它会从 Tile 资产重读 TileData，
        /// 把逐格替换的 Tile / ColliderType 覆盖回原值。
        /// </summary>
        void RebuildColliders()
        {
            if (terrain == null || terrainCollider == null) return;

            terrainCollider.ProcessTilemapChanges();

            if (composite == null)
                composite = terrainCollider.GetComponent<CompositeCollider2D>();
            if (composite != null) composite.GenerateGeometry();

            Physics2D.SyncTransforms();
        }

        /// <summary>找场景里带 TilemapCollider2D 的关卡 Tilemap（Outline 层没有碰撞体，不会被选中）。</summary>
        void ResolveTerrain()
        {
            if (terrain != null && terrainCollider != null) return;

            // additive 加载时，Awake 阶段 FindObjectsOfType 可能还看不到另一个场景的对象。
            if (terrain == null)
            {
                var terrainGo = GameObject.Find("Terrain");
                if (terrainGo != null) terrain = terrainGo.GetComponent<Tilemap>();
            }

            if (terrain != null)
            {
                if (terrainCollider == null) terrainCollider = terrain.GetComponent<TilemapCollider2D>();
                if (composite == null) composite = terrain.GetComponent<CompositeCollider2D>();
                if (terrainCollider != null) return;
            }

            TilemapCollider2D[] colliders = FindObjectsOfType<TilemapCollider2D>();
            foreach (var collider in colliders)
            {
                var map = collider.GetComponent<Tilemap>();
                if (map == null) continue;

                terrain = map;
                terrainCollider = collider;
                composite = collider.GetComponent<CompositeCollider2D>();
                return;
            }

            if (!_warnedNoTerrain && Time.frameCount > 5)
            {
                _warnedNoTerrain = true;
                Debug.LogWarning("[PortalTerrainCarver] 没有找到带 TilemapCollider2D 的关卡 Tilemap。", this);
            }
        }

        void OnDestroy()
        {
            RestoreAll();
        }
    }
}
