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
    /// - 高度方向：与背向垂直，长度 = PortalHeight，居中；顶边向下缩进 carveTopMargin。
    /// - 为了让玩家身体能贴到门面，额外向门面方向扩展 carveFrontMargin；
    ///   设为 0 就是严格从门中轴开始。
    ///
    /// 这不是按瓦片格整格删除，而是把矩形和每一格的 Sprite 物理形状做布尔差：
    /// 一个 1×1 方块可以被切成 0.5 格、甚至更小的碎片。视觉 Tile 保持不变；
    /// 被影响的格子从 TilemapCollider2D 中移除，剩余碰撞用运行时 PolygonCollider2D 补回。
    /// 这样不需要在运行时 OverridePhysicsShape，也不会在门禁用/重新启用后残留 carve 状态。
    /// 每个门洞另有一个等大的补墙 BoxCollider2D：玩家离开正面区域时启用，
    /// 接近正面时关闭，因此进出检测区域不需要重新执行地形布尔裁剪。
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

        [Tooltip("为了让玩家身体能贴到门面，额外向门面方向扩展的长度；0 = 严格从门中轴开始")]
        [Min(0f)] public float carveFrontMargin = 0.1f;

        [Tooltip("开洞顶边向下缩进的距离，保留一条地形碰撞支撑玩家；0 = 与门洞顶边齐平")]
        [Min(0f)] public float carveTopMargin = 0.12f;

        [Header("正面接近检测")]
        [Tooltip("玩家进入门前这段距离后关闭补墙碰撞体，应略大于单帧最大移动距离")]
        [Min(0.01f)] public float carveActivationDepth = 1f;

        [Tooltip("退出正面检测区域时的额外容差，避免边界抖动导致碰撞体反复开关")]
        [Min(0f)] public float carveActivationExitMargin = 0.15f;

        readonly Dictionary<CaptureDisplayGroup, PortalCarveState> _openPortals =
            new Dictionary<CaptureDisplayGroup, PortalCarveState>();
        readonly Dictionary<Vector3Int, CarvedCell> _carved =
            new Dictionary<Vector3Int, CarvedCell>();
        readonly Dictionary<Sprite, List<Vector2[]>> _spriteShapes =
            new Dictionary<Sprite, List<Vector2[]>>();
        readonly List<CarveRect> _lastRects = new List<CarveRect>();
        readonly List<CarveRect> _rectBuffer = new List<CarveRect>();
        readonly HashSet<Vector3Int> _affectedCells = new HashSet<Vector3Int>();
        readonly HashSet<Vector3Int> _lastAffectedCells = new HashSet<Vector3Int>();
        readonly HashSet<Vector3Int> _candidateCells = new HashSet<Vector3Int>();

        bool _warnedNoTerrain;

        /// <summary>当前被切过碰撞的格子数。</summary>
        public int CarvedCellCount => _carved.Count;

        sealed class PortalCarveState
        {
            public GameObject backfillObject;
            public BoxCollider2D backfill;
            public Transform player;
            public Collider2D playerBody;
            public bool playerInFront;
        }

        /// <summary>一个格子的原始碰撞信息，用于恢复。</summary>
        sealed class CarvedCell
        {
            public Tile.ColliderType originalColliderType;
            public GameObject residualCollider;
            public PolygonCollider2D residualPolygon;
        }

        /// <summary>
        /// 有朝向的矩形。origin 是门中轴起点，axis 是“门背后”的单位向量，
        /// perp 与 axis 垂直；local.x ∈ [-frontMargin, length]，
        /// local.y ∈ [-height/2, height/2 - topMargin]。
        /// </summary>
        struct CarveRect
        {
            public CaptureDisplayGroup portal;
            public Vector2 origin;
            public Vector2 axis;
            public Vector2 perp;
            public float frontMargin;
            public float length;
            public float height;
            public float topMargin;

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

            if (open)
            {
                if (!_openPortals.ContainsKey(group))
                    _openPortals.Add(group, new PortalCarveState());
            }
            else
            {
                if (_openPortals.TryGetValue(group, out PortalCarveState state))
                {
                    DestroyBackfill(state);
                    _openPortals.Remove(group);
                }
            }

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

            List<CarveRect> rects = CollectCarveRects(_rectBuffer);
            bool geometryChanged = !SameRects(rects, _lastRects);

            // 补墙开关每帧更新；只有门洞几何变化才更新尺寸并执行地形布尔裁剪。
            SyncBackfills(rects, geometryChanged);
            if (!geometryChanged) return;

            _lastRects.Clear();
            _lastRects.AddRange(rects);

            // 世界 AABB 先换算成格子 broadphase，只处理新旧门洞涉及的少量格子。
            _affectedCells.Clear();
            CollectAffectedCells(rects, _affectedCells);
            ApplyCarve(rects, _affectedCells);

            _lastAffectedCells.Clear();
            _lastAffectedCells.UnionWith(_affectedCells);
        }

        /// <summary>收集所有激活门的背向矩形。</summary>
        List<CarveRect> CollectCarveRects(List<CarveRect> result)
        {
            result.Clear();
            foreach (var pair in _openPortals)
            {
                CaptureDisplayGroup group = pair.Key;
                if (group == null || !group.isActiveAndEnabled) continue;

                // facing 的反方向就是门背后；矩形从这里开始向墙里延伸。
                // 每次 Refresh 都重新读位置与方向，门移动/旋转后会按新矩形重切。
                Vector2 back = group.BackDirection;
                if (back.sqrMagnitude < 0.000001f) continue;
                back.Normalize();

                // perp 始终指向门自身上方，保证左右朝向的门都只缩减顶边。
                Vector2 perp = new Vector2(-back.y, back.x);
                if (Vector2.Dot(perp, group.transform.up) < 0f) perp = -perp;

                float height = Mathf.Max(0.01f, group.PortalHeight);
                result.Add(new CarveRect
                {
                    portal = group,
                    origin = group.transform.position,
                    axis = back,
                    perp = perp,
                    frontMargin = Mathf.Max(0f, carveFrontMargin),
                    length = Mathf.Max(0.01f, carveWidth),
                    height = height,
                    topMargin = Mathf.Clamp(carveTopMargin, 0f, height - 0.01f)
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

        void SyncBackfills(List<CarveRect> rects, bool geometryChanged)
        {
            foreach (CarveRect rect in rects)
            {
                if (!_openPortals.TryGetValue(rect.portal, out PortalCarveState state)) continue;

                bool created = state.backfill == null;
                if (created) CreateBackfill(state, rect.portal);
                if (created || geometryChanged) UpdateBackfillGeometry(state, rect);
                UpdateBackfillActivation(state, rect);
            }
        }

        void CreateBackfill(PortalCarveState state, CaptureDisplayGroup group)
        {
            var go = new GameObject(group.name + " Backfill Collider");
            go.layer = terrain.gameObject.layer;
            go.transform.SetParent(transform, true);

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.interpolation = RigidbodyInterpolation2D.None;

            var box = go.AddComponent<BoxCollider2D>();
            box.sharedMaterial = terrainCollider.sharedMaterial;
            box.isTrigger = false;
            box.enabled = false;

            state.backfillObject = go;
            state.backfill = box;
        }

        static void UpdateBackfillGeometry(PortalCarveState state, CarveRect rect)
        {
            Vector2 center = rect.ToWorld(new Vector2(
                (rect.length - rect.frontMargin) * 0.5f,
                -rect.topMargin * 0.5f));
            float angle = Mathf.Atan2(rect.axis.y, rect.axis.x) * Mathf.Rad2Deg;
            state.backfillObject.transform.SetPositionAndRotation(
                center, Quaternion.Euler(0f, 0f, angle));
            state.backfill.size = new Vector2(
                rect.length + rect.frontMargin,
                rect.height - rect.topMargin);
        }

        void UpdateBackfillActivation(PortalCarveState state, CarveRect rect)
        {
            Transform player = rect.portal.PlayerTransform;
            if (player == null)
            {
                state.player = null;
                state.playerBody = null;
                state.playerInFront = false;
                if (!state.backfill.enabled) state.backfill.enabled = true;
                return;
            }

            if (state.player != player)
            {
                state.player = player;
                state.playerBody = player.GetComponent<Collider2D>();
                state.playerInFront = false;
            }

            Vector2 local = rect.ToLocal(player.position);
            float exitMargin = state.playerInFront
                ? Mathf.Max(0f, carveActivationExitMargin)
                : 0f;
            float forward = -local.x;
            float halfHeight = rect.height * 0.5f;
            bool inFront =
                forward >= -rect.frontMargin - exitMargin &&
                forward <= Mathf.Max(0.01f, carveActivationDepth) + exitMargin &&
                Mathf.Abs(local.y) <= halfHeight + exitMargin;
            state.playerInFront = inFront;

            bool overlapsPlayer;
            if (state.playerBody != null && state.playerBody.isActiveAndEnabled)
            {
                // 使用 AABB 做保守检查：宁可稍晚启用，也不能在玩家身上生成实体碰撞。
                overlapsPlayer = state.backfill.bounds.Intersects(state.playerBody.bounds);
            }
            else
            {
                overlapsPlayer =
                    local.x >= -rect.frontMargin && local.x <= rect.length &&
                    local.y >= -halfHeight && local.y <= halfHeight - rect.topMargin;
            }

            bool enable = !inFront && !overlapsPlayer;
            if (state.backfill.enabled != enable) state.backfill.enabled = enable;
        }

        void DestroyBackfill(PortalCarveState state)
        {
            if (state == null) return;
            if (state.backfillObject != null)
            {
                state.backfillObject.SetActive(false);
                Destroy(state.backfillObject);
            }

            state.backfillObject = null;
            state.backfill = null;
            state.player = null;
            state.playerBody = null;
            state.playerInFront = false;
        }

        void CollectAffectedCells(List<CarveRect> rects, HashSet<Vector3Int> output)
        {
            BoundsInt terrainBounds = terrain.cellBounds;
            if (terrainBounds.size == Vector3Int.zero) return;

            foreach (CarveRect rect in rects)
            {
                float bottom = -rect.height * 0.5f;
                float top = -bottom - rect.topMargin;
                var minimum = new Vector3Int(int.MaxValue, int.MaxValue, int.MaxValue);
                var maximum = new Vector3Int(int.MinValue, int.MinValue, int.MinValue);

                IncludeCell(rect.ToWorld(new Vector2(-rect.frontMargin, bottom)),
                    ref minimum, ref maximum);
                IncludeCell(rect.ToWorld(new Vector2(-rect.frontMargin, top)),
                    ref minimum, ref maximum);
                IncludeCell(rect.ToWorld(new Vector2(rect.length, bottom)),
                    ref minimum, ref maximum);
                IncludeCell(rect.ToWorld(new Vector2(rect.length, top)),
                    ref minimum, ref maximum);

                // 向外扩一格，覆盖门洞恰好压到格子边界以及浮点误差。
                minimum -= Vector3Int.one;
                maximum += Vector3Int.one;
                minimum = Vector3Int.Max(minimum, terrainBounds.min);
                maximum = Vector3Int.Min(maximum, terrainBounds.max - Vector3Int.one);

                for (int z = minimum.z; z <= maximum.z; z++)
                for (int y = minimum.y; y <= maximum.y; y++)
                for (int x = minimum.x; x <= maximum.x; x++)
                    output.Add(new Vector3Int(x, y, z));
            }
        }

        void IncludeCell(Vector2 world, ref Vector3Int minimum, ref Vector3Int maximum)
        {
            Vector3Int cell = terrain.WorldToCell(world);
            minimum = Vector3Int.Min(minimum, cell);
            maximum = Vector3Int.Max(maximum, cell);
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
                if (Mathf.Abs(a[i].topMargin - b[i].topMargin) > 0.0001f) return false;
            }
            return true;
        }

        /// <summary>只重算新旧门洞覆盖的格子；持续被切割的格子复用残余碰撞器。</summary>
        void ApplyCarve(List<CarveRect> rects, HashSet<Vector3Int> affectedCells)
        {
            _candidateCells.Clear();
            _candidateCells.UnionWith(_lastAffectedCells);
            _candidateCells.UnionWith(affectedCells);

            bool tilemapChanged = false;
            bool residualChanged = false;

            foreach (Vector3Int cell in _candidateCells)
            {
                _carved.TryGetValue(cell, out CarvedCell entry);
                bool hadCarve = entry != null;
                bool inNewCarve = affectedCells.Contains(cell) && terrain.HasTile(cell);

                if (!inNewCarve)
                {
                    if (hadCarve && RestoreCarvedCell(cell, entry)) tilemapChanged = true;
                    continue;
                }

                Tile.ColliderType originalType = hadCarve
                    ? entry.originalColliderType
                    : terrain.GetColliderType(cell);
                if (originalType == Tile.ColliderType.None ||
                    !(terrain.GetTile(cell) is Tile tile) || tile.sprite == null)
                {
                    if (hadCarve && RestoreCarvedCell(cell, entry)) tilemapChanged = true;
                    continue;
                }

                Vector2 center = terrain.GetCellCenterWorld(cell);
                List<Vector2[]> worldShapes = GetWorldShapes(tile.sprite, center, originalType);
                if (worldShapes.Count == 0)
                {
                    if (hadCarve && RestoreCarvedCell(cell, entry)) tilemapChanged = true;
                    continue;
                }

                float originalArea = 0f;
                var remaining = new List<Vector2[]>();
                foreach (Vector2[] shape in worldShapes)
                {
                    originalArea += Mathf.Abs(SignedArea(shape));
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

                // broadphase 只说明矩形包围盒相交；面积没变时不修改 Tilemap。
                if (Mathf.Abs(originalArea - remainingArea) <= 0.0001f)
                {
                    if (hadCarve && RestoreCarvedCell(cell, entry)) tilemapChanged = true;
                    continue;
                }

                if (!hadCarve)
                {
                    entry = new CarvedCell { originalColliderType = originalType };
                    _carved.Add(cell, entry);
                }

                if (terrain.GetColliderType(cell) != Tile.ColliderType.None)
                {
                    terrain.SetColliderType(cell, Tile.ColliderType.None);
                    tilemapChanged = true;
                }

                UpdateResidualCollider(cell, center, entry, remaining);
                residualChanged = true;
            }

            // 多次 SetColliderType 只提交一批；仅残余路径变化时不重建 Composite。
            if (tilemapChanged) RebuildColliders();
            else if (residualChanged) Physics2D.SyncTransforms();
        }

        void UpdateResidualCollider(
            Vector3Int cell,
            Vector2 center,
            CarvedCell entry,
            List<Vector2[]> remaining)
        {
            if (remaining.Count == 0)
            {
                // 全切掉时保留禁用对象，之后门在同一格移回一部分可直接复用。
                if (entry.residualPolygon != null) entry.residualPolygon.enabled = false;
                if (entry.residualCollider != null) entry.residualCollider.SetActive(false);
                return;
            }

            if (entry.residualCollider == null)
            {
                entry.residualCollider = new GameObject("Carved Collider " + cell.x + "," + cell.y);
                entry.residualCollider.layer = terrain.gameObject.layer;
                entry.residualCollider.transform.SetParent(terrain.transform, true);
                entry.residualCollider.transform.rotation = Quaternion.identity;
                entry.residualCollider.transform.localScale = Vector3.one;
                entry.residualPolygon = entry.residualCollider.AddComponent<PolygonCollider2D>();
            }

            entry.residualCollider.transform.position = center;
            PolygonCollider2D polygon = entry.residualPolygon;
            polygon.sharedMaterial = terrainCollider.sharedMaterial;
            polygon.pathCount = remaining.Count;
            for (int i = 0; i < remaining.Count; i++)
            {
                Vector2[] shape = remaining[i];
                var points = new Vector2[shape.Length];
                for (int j = 0; j < shape.Length; j++)
                    points[j] = entry.residualCollider.transform.InverseTransformPoint(shape[j]);
                polygon.SetPath(i, points);
            }

            polygon.enabled = true;
            entry.residualCollider.SetActive(true);
        }

        bool RestoreCarvedCell(Vector3Int cell, CarvedCell entry)
        {
            if (entry.residualCollider != null)
            {
                entry.residualCollider.SetActive(false);
                Destroy(entry.residualCollider);
            }

            bool changed = terrain != null &&
                           terrain.GetColliderType(cell) != entry.originalColliderType;
            if (changed) terrain.SetColliderType(cell, entry.originalColliderType);
            _carved.Remove(cell);
            return changed;
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

            float bottom = -rect.height * 0.5f;
            float top = -bottom - rect.topMargin;
            float front = -rect.frontMargin;

            // 1) 矩形左侧（local.x <= front）
            AddPiece(ClipHalfPlane(local, 0, front, false), rect, output);

            // 2) 矩形右侧（local.x >= length）
            AddPiece(ClipHalfPlane(local, 0, rect.length, true), rect, output);

            // 3) 矩形正下方窄条（front <= local.x <= length 且 local.y <= bottom）
            AddPiece(ClipBox(local, front, rect.length, float.NegativeInfinity, bottom),
                rect, output);

            // 4) 矩形正上方窄条（front <= local.x <= length 且 local.y >= top）
            AddPiece(ClipBox(local, front, rect.length, top, float.PositiveInfinity),
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
            _affectedCells.Clear();
            _lastAffectedCells.Clear();
            _candidateCells.Clear();
        }

        void RestoreAll()
        {
            RestoreCells();
            foreach (var pair in _openPortals) DestroyBackfill(pair.Value);

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
