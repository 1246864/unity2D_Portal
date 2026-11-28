// 文档导航（相对于项目根目录）：
// 模块说明：.trae/documents/player-controller-modules.md
// 架构说明：.trae/documents/player-controller-architecture.md
using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace Platformer
{
    /// <summary>
    /// 控制一对传送门。挂在包含 Portal A 与 Portal B 的 "Portal Pair" 父节点上。
    ///
    /// 传送业务统一在这里管理：门的薄碰撞箱、碰撞触发、位移/速度/相机、冷却，
    /// 以及传送后的双门成像刷新。CaptureDisplayGroup 只负责画面显示与把碰撞转发过来。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class PortalPair : MonoBehaviour
    {
        [Header("玩家")]
        [Tooltip("玩家对象，遮罩朝向和传送都以它为参照")]
        public Transform player;

        [Tooltip("眼睛相对玩家中心点的高度（世界单位）")]
        public float playerEyeOffset = 0.4f;

        [Header("玩家与相机总控")]
        [Tooltip("处理相机镜像和相机传送；留空时从跟随相机解析。放门请求由玩家输入系统直接调用")]
        [FormerlySerializedAs("cameraMirror")]
        public PlayerControlManager playerControl;

        [Header("传送")]
        [Tooltip("传送冷却（秒），一对门共享，避免连续帧反复触发")]
        [Min(0f)] public float teleportCooldown = 0.25f;

        [Tooltip("传送后是否保留速度（两门朝向相同则水平速度取反，相背则保持）")]
        public bool preserveVelocity = true;

        [Header("碰撞箱")]
        [Tooltip("传送门薄碰撞箱的厚度（世界单位）；越薄越依赖连续碰撞")]
        [Min(0.001f)] public float gateThickness = 0.05f;

        [Tooltip("玩家接触碰撞箱沿传送门开口反方向后退的距离（世界单位），用于减少出口处的重复接触传送；只偏移碰撞体，不移动门本体")]
        [Min(0f)] public float gateContactOffset = 0.04f;
        [Tooltip("出口中心碰撞点越过门碰撞箱外表面的额外安全距离（世界单位），范围 0~任意正值；0 表示只保留碰撞体与半径所需间距，过大可能让出口位置明显外移")]
        [Min(0f)] public float exitMargin = 0.03f;

        [Header("门洞")]
        [Tooltip("上遮罩相对门中心的 Y 偏移（世界单位）；两扇门共用")]
        public float maskTopOffset = 1.5f;

        [Tooltip("下遮罩相对门中心的 Y 偏移（世界单位）；两扇门共用")]
        public float maskBottomOffset = -1.5f;

        [Header("传送门画面尺寸")]
        [Tooltip("关闭：使用固定 captureWidth × captureHeight；开启：使用主相机视野 × cameraSizeMultiplier")]
        public bool useDynamicSize = false;

        [Tooltip("动态模式：X = 宽度相对主相机宽度的倍率，Y = 高度相对主相机高度的倍率")]
        public Vector2 cameraSizeMultiplier = new Vector2(1.2f, 2f);

        [Tooltip("静态模式：捕获矩形宽度（世界单位）")]
        [Min(0.01f)] public float captureWidth = 50f;

        [Tooltip("静态模式：捕获矩形高度（世界单位）")]
        [Min(0.01f)] public float captureHeight = 50f;

        [Header("画面透明仲裁")]
        [Tooltip("开口法向距离在接近评分中的权重")]
        [Min(0f)] public float approachNormalWeight = 1f;

        [Tooltip("沿门面方向的偏移在接近评分中的权重")]
        [Min(0f)] public float approachTangentWeight = 1f;

        [Tooltip("玩家位于门背面时增加的接近惩罚分")]
        [Min(0f)] public float approachBackPenalty = 10f;

        [Header("射击协调")]
        [Tooltip("射线未命中且无法从相机计算可视范围时使用的子弹射程（世界单位）")]
        [Min(0.01f)] public float placementMissDistanceFallback = 50f;

        [Header("地形开洞")]
        [Tooltip("传送门激活时，把门洞矩形覆盖到的地形格改为无碰撞，再重建 CompositeCollider2D")]
        public bool carveTerrain = true;

        [Tooltip("开洞矩形沿门朝向反方向的长度（世界单位）")]
        [Min(0.01f)] public float carveWidth = 0.5f;

        [Tooltip("为了让玩家碰撞体能贴到门面，额外向门面方向扩展的长度；0 = 严格从中轴开始")]
        [Min(0f)] public float carveFrontMargin = 0.1f;

        [Tooltip("开洞顶边向下缩进的距离，保留一条地形碰撞支撑玩家；0 = 与门洞顶边齐平")]
        [Min(0f)] public float carveTopMargin = 0.12f;

        /// <summary>门洞高度，由上下遮罩偏移算出，两扇门共用。</summary>
        public float PortalHeight => Mathf.Abs(maskTopOffset - maskBottomOffset);

        CaptureDisplayGroup _groupA;
        CaptureDisplayGroup _groupB;

        BoxCollider2D _gateA;
        BoxCollider2D _gateB;
        PortalTerrainCarver _terrainCarver;

        float _cooldown;
        bool _forceCapture;
        bool _wasLinked;
        Transform _lastPlayer;
        CaptureDisplayGroup _automaticViewSource;
        Collider2D _ignoredPlayerBody;
        int _originalBodyExcludeLayers;

        Rigidbody2D _gateBodyA;
        Rigidbody2D _gateBodyB;
        PortalMotion _motionA;
        PortalMotion _motionB;
        Vector2 _previousPlayerPosition;
        int _observedPlayerRevision;
        bool _hasPlayerBaseline;
        readonly RaycastHit2D[] _placementHits = new RaycastHit2D[16];
        PortalPlacementSystem _placementSystem;
        PortalProjectileSystem _projectileSystem;

        /// <summary>一个物理步内的门位姿与速度；角度始终使用物理根节点的 Z 旋转。</summary>
        struct PortalFrameState
        {
            public Vector2 Position;
            public float Angle;
            public float FacingSign;
            public Vector2 LinearVelocity;
            public float AngularVelocity;

            public Vector2 Right => DirectionFromUp(Angle - 90f);
            public Vector2 Tangent => DirectionFromUp(Angle);
            public Vector2 Opening => DirectionFromUp(Angle + FacingSign * -90f);

            static Vector2 DirectionFromUp(float angle)
            {
                float radians = angle * Mathf.Deg2Rad;
                return new Vector2(-Mathf.Sin(radians), Mathf.Cos(radians));
            }

            public static PortalFrameState Lerp(
                PortalFrameState from, PortalFrameState to, float t)
            {
                return new PortalFrameState
                {
                    Position = Vector2.LerpUnclamped(from.Position, to.Position, t),
                    Angle = Mathf.LerpAngle(from.Angle, to.Angle, t),
                    FacingSign = t < 0.5f ? from.FacingSign : to.FacingSign,
                    LinearVelocity = Vector2.LerpUnclamped(
                        from.LinearVelocity, to.LinearVelocity, t),
                    AngularVelocity = Mathf.LerpUnclamped(
                        from.AngularVelocity, to.AngularVelocity, t)
                };
            }

            public Vector2 PointVelocity(Vector2 point)
            {
                Vector2 offset = point - Position;
                float radiansPerSecond = AngularVelocity * Mathf.Deg2Rad;
                return LinearVelocity + new Vector2(
                    -radiansPerSecond * offset.y,
                    radiansPerSecond * offset.x);
            }
        }

        sealed class PortalMotion
        {
            public CaptureDisplayGroup Group;
            public Rigidbody2D Body;
            public PortalFrameState Previous;
            public PortalFrameState Current;
            public bool HasPrevious;
            public bool RelocationPending;
            public bool DiscontinuousThisStep;
        }

        /// <summary>门碰撞箱所在层（复用 Snapshot 层，玩家身体碰撞体会排除该层）。</summary>
        const int GateLayer = 8;

        /// <summary>手动选中的接近基准门；null 表示自动仲裁。</summary>
        public CaptureDisplayGroup SelectedPortal { get; private set; }

        /// <summary>玩家眼睛的世界坐标。</summary>
        public Vector3 PlayerEye => player != null
            ? player.position + Vector3.up * playerEyeOffset
            : Vector3.zero;

        /// <summary>当前是否处于传送冷却中。</summary>
        public bool IsTeleportCoolingDown => _cooldown > 0f;

        /// <summary>A/B 两扇门均存在且处于激活状态时，传送门对才算已连接。</summary>
        public bool IsLinked => IsPortalActive(_groupA) && IsPortalActive(_groupB);

        static bool IsPortalActive(CaptureDisplayGroup group)
        {
            return group != null && group.gameObject.activeInHierarchy && group.isActiveAndEnabled;
        }

        void Awake()
        {
            CacheGroups();
            EnsureGates();
            EnsureTerrainCarver();
            PushPlayerToGroups();
            PushViewSettingsToGroups();
            IgnorePlayerBodyCollision();
            _placementSystem = new PortalPlacementSystem(this);
            _projectileSystem = GetComponent<PortalProjectileSystem>();
        }

        void Start()
        {
            // 再执行一次：确保所有 CaptureDisplayGroup 的 Awake 都已执行完毕。
            CacheGroups();
            EnsureGates();
            EnsureTerrainCarver();
            PushPlayerToGroups();
            PushViewSettingsToGroups();
            IgnorePlayerBodyCollision();
            CaptureDisplayGroup.ApplyFacingStates(_groupA, _groupB);
            SyncPortalLinkState();
            UpdateViewTransparency(true);
        }

        void OnEnable()
        {
            // 重新启用后重新绑定玩家碰撞过滤；禁用时会恢复原始位。
            _lastPlayer = null;
            SyncPortalLinkState();
            UpdateViewTransparency(true);
            ResetMotionTracking();
        }

        void OnDisable()
        {
            RestorePlayerBodyCollision();
            SetGateActive(_gateA, false);
            SetGateActive(_gateB, false);
            SetCaptureActive(_groupA, false);
            SetCaptureActive(_groupB, false);
            if (_groupA != null)
            {
                _groupA.SetViewAlpha(1f);
                _groupA.SetConnectionClosedImmediate();
            }
            if (_groupB != null)
            {
                _groupB.SetViewAlpha(1f);
                _groupB.SetConnectionClosedImmediate();
            }
            if (_terrainCarver != null) _terrainCarver.enabled = false;
            _automaticViewSource = null;
            _wasLinked = false;
            _forceCapture = false;
            if (_projectileSystem != null) _projectileSystem.ReleaseAllProjectiles();
            ResetMotionTracking();
        }

        void FixedUpdate()
        {
            EnsureMotionStates();

            float dt = Mathf.Max(0.000001f, Time.fixedDeltaTime);
            SampleMotion(_motionA, dt);
            SampleMotion(_motionB, dt);

            bool gatePoseChanged = SyncGateBody(_motionA);
            gatePoseChanged |= SyncGateBody(_motionB);
            if (gatePoseChanged) Physics2D.SyncTransforms();
            if (_terrainCarver != null && _terrainCarver.enabled)
                _terrainCarver.RefreshNow();

            if (player == null || !IsLinked)
            {
                _hasPlayerBaseline = false;
                return;
            }

            var controller = player.GetComponent<PlayerController2D>();
            var rb = player.GetComponent<Rigidbody2D>();
            if (controller == null || rb == null)
            {
                _hasPlayerBaseline = false;
                return;
            }

            Vector2 currentPlayerPosition = rb.position;
            int currentRevision = controller.PositionRevision;
            if (!_hasPlayerBaseline || _observedPlayerRevision != currentRevision)
            {
                // 重生、测试 Teleport 和上一次传送都是显式跳变，不能扫成一条走廊。
                _previousPlayerPosition = currentPlayerPosition;
                _observedPlayerRevision = currentRevision;
                _hasPlayerBaseline = true;
                return;
            }

            if (_cooldown <= 0f)
            {
                TrySweepingPortals(
                    controller, _previousPlayerPosition, currentPlayerPosition);
            }

            // 如果刚才发生传送，这里记录的是出口位置与新版本号。
            _previousPlayerPosition = rb.position;
            _observedPlayerRevision = controller.PositionRevision;
            _hasPlayerBaseline = true;
        }

        /// <summary>
        /// 通知门对：某扇门刚才发生了瞬移，而不是连续运动。
        /// 下一物理步只检查新门洞，不连接瞬移前后的位置做扫掠。
        /// </summary>
        public void NotifyPortalRelocated(CaptureDisplayGroup portal)
        {
            EnsureMotionStates();
            if (portal == _groupA && _motionA != null) _motionA.RelocationPending = true;
            if (portal == _groupB && _motionB != null) _motionB.RelocationPending = true;
        }

        /// <summary>把 A/B 两门都标记为刚刚瞬移。</summary>
        public void NotifyPortalsRelocated()
        {
            NotifyPortalRelocated(_groupA);
            NotifyPortalRelocated(_groupB);
        }

        void EnsureMotionStates()
        {
            if (_motionA == null) _motionA = new PortalMotion();
            if (_motionB == null) _motionB = new PortalMotion();
            _motionA.Group = _groupA;
            _motionA.Body = _gateBodyA;
            _motionB.Group = _groupB;
            _motionB.Body = _gateBodyB;
        }

        static PortalFrameState ReadFrameState(CaptureDisplayGroup group)
        {
            return new PortalFrameState
            {
                Position = group.transform.position,
                Angle = group.transform.eulerAngles.z,
                FacingSign = group.FacingSign,
                LinearVelocity = Vector2.zero,
                AngularVelocity = 0f
            };
        }

        static void SampleMotion(PortalMotion motion, float dt)
        {
            if (motion == null || motion.Group == null) return;

            PortalFrameState next = ReadFrameState(motion.Group);
            if (!motion.HasPrevious)
            {
                motion.Previous = next;
                motion.Current = next;
                motion.HasPrevious = true;
                motion.RelocationPending = false;
                motion.DiscontinuousThisStep = true;
                return;
            }

            motion.Previous = motion.Current;
            motion.Current = next;

            // 未走显式通知的跨地图 Transform 改写，用单步位移上限兜底识别。
            // 正常高速运动由线段扫掠处理，不需要靠提高碰撞箱厚度兜底。
            float relocationDistance = Mathf.Max(8f, motion.Group.PortalHeight * 4f);
            motion.DiscontinuousThisStep = motion.RelocationPending ||
                Vector2.Distance(motion.Previous.Position, next.Position) > relocationDistance;
            motion.RelocationPending = false;

            if (motion.DiscontinuousThisStep)
            {
                motion.Previous = next;
                motion.Current.LinearVelocity = Vector2.zero;
                motion.Current.AngularVelocity = 0f;
                return;
            }

            motion.Current.LinearVelocity =
                (motion.Current.Position - motion.Previous.Position) / dt;
            motion.Current.AngularVelocity = Mathf.DeltaAngle(
                motion.Previous.Angle, motion.Current.Angle) / dt;
        }

        static bool SyncGateBody(PortalMotion motion)
        {
            if (motion == null || motion.Body == null || motion.Group == null) return false;

            PortalFrameState current = motion.Current;
            bool changed = Vector2.Distance(motion.Body.position, current.Position) > 0.000001f ||
                           Mathf.Abs(Mathf.DeltaAngle(
                               motion.Body.rotation, current.Angle)) > 0.0001f;
            if (!changed) return false;

            motion.Body.position = current.Position;
            motion.Body.rotation = current.Angle;
            return true;
        }

        PortalFrameState CurrentFrameState(CaptureDisplayGroup group)
        {
            PortalMotion motion = group == _groupA ? _motionA : _motionB;
            return motion != null && motion.Group == group && motion.HasPrevious
                ? motion.Current
                : ReadFrameState(group);
        }

        void TrySweepingPortals(
            PlayerController2D controller,
            Vector2 previousPlayer,
            Vector2 currentPlayer)
        {
            CaptureDisplayGroup hitSource = null;
            PortalFrameState hitSourceState = default;
            Vector2 hitPoint = currentPlayer;
            float earliest = float.MaxValue;

            TrySweepingPortal(
                _motionA, previousPlayer, currentPlayer, controller.centerPointRadius,
                ref hitSource, ref hitSourceState, ref hitPoint, ref earliest);
            TrySweepingPortal(
                _motionB, previousPlayer, currentPlayer, controller.centerPointRadius,
                ref hitSource, ref hitSourceState, ref hitPoint, ref earliest);

            if (hitSource == null) return;
            CaptureDisplayGroup partner = hitSource.partner;
            if (!IsPortalActive(partner)) return;

            PlayGateHitAnimations(hitSource);
            TeleportAt(
                hitSource,
                partner,
                hitSourceState,
                CurrentFrameState(partner),
                hitPoint,
                controller.VelocityBeforePhysics);
        }

        void TrySweepingPortal(
            PortalMotion motion,
            Vector2 previousPlayer,
            Vector2 currentPlayer,
            float radius,
            ref CaptureDisplayGroup hitSource,
            ref PortalFrameState hitSourceState,
            ref Vector2 hitPoint,
            ref float earliest)
        {
            if (motion == null || !IsPortalActive(motion.Group) || !motion.HasPrevious) return;

            if (TryGateCrossing(
                    motion, previousPlayer, currentPlayer, radius,
                    out float crossingTime, out Vector2 crossingPoint) &&
                crossingTime < earliest)
            {
                earliest = crossingTime;
                hitSource = motion.Group;
                hitSourceState = PortalFrameState.Lerp(
                    motion.Previous, motion.Current, crossingTime);
                // 位姿按命中时刻插值，速度使用本步测得的瞬时速度，不能随命中时间衰减。
                hitSourceState.LinearVelocity = motion.Current.LinearVelocity;
                hitSourceState.AngularVelocity = motion.Current.AngularVelocity;
                hitPoint = crossingPoint;
            }
        }

        bool TryGateCrossing(
            PortalMotion motion,
            Vector2 previousPlayer,
            Vector2 currentPlayer,
            float radius,
            out float crossingTime,
            out Vector2 crossingPoint)
        {
            // 瞬移门不连接旧位置；但新门洞直接罩住玩家时仍然触发。
            if (motion.DiscontinuousThisStep)
            {
                if (SegmentIntersectsGate(
                        currentPlayer, currentPlayer, motion.Current,
                        motion.Group.PortalHeight, radius, out _))
                {
                    crossingTime = 1f;
                    crossingPoint = currentPlayer;
                    return true;
                }

                crossingTime = 0f;
                crossingPoint = currentPlayer;
                return false;
            }

            float angleDelta = Mathf.Abs(Mathf.DeltaAngle(
                motion.Previous.Angle, motion.Current.Angle));
            int steps = Mathf.Clamp(Mathf.CeilToInt(angleDelta / 10f), 1, 36);
            Vector2 segmentStart = previousPlayer;
            PortalFrameState segmentStartState = motion.Previous;

            for (int i = 1; i <= steps; i++)
            {
                float t = i / (float)steps;
                Vector2 segmentEnd = Vector2.LerpUnclamped(previousPlayer, currentPlayer, t);
                PortalFrameState segmentEndState = PortalFrameState.Lerp(
                    motion.Previous, motion.Current, t);
                PortalFrameState middleState = PortalFrameState.Lerp(
                    segmentStartState, segmentEndState, 0.5f);

                if (SegmentIntersectsGate(
                        segmentStart, segmentEnd, middleState,
                        motion.Group.PortalHeight, radius, out float localTime))
                {
                    crossingTime = Mathf.Lerp((i - 1) / (float)steps, t, localTime);
                    crossingPoint = Vector2.LerpUnclamped(segmentStart, segmentEnd, localTime);
                    return true;
                }

                segmentStart = segmentEnd;
                segmentStartState = segmentEndState;
            }

            crossingTime = 0f;
            crossingPoint = currentPlayer;
            return false;
        }

        bool SegmentIntersectsGate(
            Vector2 start,
            Vector2 end,
            PortalFrameState state,
            float height,
            float radius,
            out float intersectionTime)
        {
            Vector2 normal = state.Opening;
            Vector2 tangent = state.Tangent;
            Vector2 gateCenter = state.Position - state.Opening * gateContactOffset;
            Vector2 deltaStart = start - gateCenter;
            Vector2 deltaEnd = end - gateCenter;
            var localStart = new Vector2(
                Vector2.Dot(deltaStart, normal), Vector2.Dot(deltaStart, tangent));
            var localEnd = new Vector2(
                Vector2.Dot(deltaEnd, normal), Vector2.Dot(deltaEnd, tangent));
            var halfSize = new Vector2(
                Mathf.Max(0.001f, gateThickness) * 0.5f + radius,
                Mathf.Max(0.01f, height) * 0.5f + radius);
            return SegmentIntersectsBox(
                localStart, localEnd, halfSize, out intersectionTime);
        }

        static bool SegmentIntersectsBox(
            Vector2 start, Vector2 end, Vector2 halfSize, out float intersectionTime)
        {
            Vector2 delta = end - start;
            float minimum = 0f;
            float maximum = 1f;

            for (int axis = 0; axis < 2; axis++)
            {
                float origin = axis == 0 ? start.x : start.y;
                float direction = axis == 0 ? delta.x : delta.y;
                float extent = axis == 0 ? halfSize.x : halfSize.y;

                if (Mathf.Abs(direction) < 0.0000001f)
                {
                    if (Mathf.Abs(origin) > extent)
                    {
                        intersectionTime = 0f;
                        return false;
                    }
                    continue;
                }

                float near = (-extent - origin) / direction;
                float far = (extent - origin) / direction;
                if (near > far)
                {
                    float swap = near;
                    near = far;
                    far = swap;
                }

                minimum = Mathf.Max(minimum, near);
                maximum = Mathf.Min(maximum, far);
                if (minimum > maximum)
                {
                    intersectionTime = 0f;
                    return false;
                }
            }

            intersectionTime = Mathf.Clamp01(minimum);
            return true;
        }

        void ResetMotionTracking()
        {
            _hasPlayerBaseline = false;
            _observedPlayerRevision = 0;
            EnsureMotionStates();
            ResetMotion(_motionA);
            ResetMotion(_motionB);
        }

        static void ResetMotion(PortalMotion motion)
        {
            motion.HasPrevious = false;
            motion.RelocationPending = false;
            motion.DiscontinuousThisStep = false;
        }

        void Update()
        {
            SyncPortalLinkState();

            // 支持运行时换玩家：重新下发引用，并让新玩家的身体碰撞体排除门层。
            if (_lastPlayer != player)
            {
                _lastPlayer = player;
                PushPlayerToGroups();
                IgnorePlayerBodyCollision();
                UpdateViewTransparency(true);
                _hasPlayerBaseline = false;
            }

            if (_cooldown > 0f)
                _cooldown -= Time.deltaTime;

            // 画面尺寸由 PortalPair 统一管理；运行时改 Inspector 也会立即下发。
            PushViewSettingsToGroups();

            // 开关、参数和组件 enabled 状态在同一个幂等入口同步。
            SyncTerrainCarver();
        }

        void LateUpdate()
        {
            UpdateGateSizes();
            UpdateViewTransparency();

            if (_forceCapture)
            {
                _forceCapture = false;
                RefreshCaptures();
            }
        }

        /// <summary>
        /// 对一次玩家射击做射线检测，并在同一帧独立调度放门系统和子弹系统。
        /// 返回值只表示门是否成功生成；未命中或放门失败不会撤销子弹视觉。
        /// </summary>
        public bool TryPlacePortal(CaptureGroupSide side, Vector2 origin, Vector2 mouseTarget)
        {
            if (!isActiveAndEnabled || player == null) return false;
            if (side != CaptureGroupSide.A && side != CaptureGroupSide.B) return false;

            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null) return false;

            Vector2 delta = mouseTarget - origin;
            if (delta.sqrMagnitude <= 0.00000001f) return false;

            int layerMask = controller.groundMask.value & ~controller.portalContactMask.value;
            if (layerMask == 0) return false;

            Vector2 direction = delta.normalized;
            var filter = new ContactFilter2D
            {
                useLayerMask = true,
                layerMask = layerMask,
                useTriggers = false
            };
            int hitCount = Physics2D.Raycast(
                origin, direction, filter, _placementHits, Mathf.Infinity);

            Rigidbody2D playerBody = player.GetComponent<Rigidbody2D>();
            RaycastHit2D nearest = default;
            bool found = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit2D hit = _placementHits[i];
                if (hit.collider == null) continue;
                if (hit.collider.attachedRigidbody == playerBody && playerBody != null) continue;
                if (hit.collider.transform == player || hit.collider.transform.IsChildOf(player)) continue;
                if (found && hit.distance >= nearest.distance) continue;

                nearest = hit;
                found = true;
            }

            if (!found)
            {
                Vector2 missEnd = origin + direction * ResolveMissDistance(origin);
                if (_projectileSystem != null)
                    _projectileSystem.Launch(side, origin, missEnd, false);
                return false;
            }

            bool placed = _placementSystem != null && _placementSystem.TryPlace(side, nearest);
            if (_projectileSystem != null)
                _projectileSystem.Launch(side, origin, nearest.point, true);
            return placed;
        }

        float ResolveMissDistance(Vector2 origin)
        {
            Camera camera = playerControl != null
                ? playerControl.GetComponent<Camera>()
                : null;
            if (camera == null) camera = Camera.main;

            if (camera != null)
            {
                float halfHeight = 0f;
                if (camera.orthographic)
                {
                    halfHeight = camera.orthographicSize;
                }
                else
                {
                    float depth = camera.transform.InverseTransformPoint(origin).z;
                    if (depth > 0f)
                        halfHeight = depth * Mathf.Tan(camera.fieldOfView * 0.5f * Mathf.Deg2Rad);
                }

                float halfWidth = halfHeight * Mathf.Abs(camera.aspect);
                float distance = 4f * Mathf.Sqrt(
                    halfHeight * halfHeight + halfWidth * halfWidth);
                if (distance > 0f && !float.IsNaN(distance) && !float.IsInfinity(distance))
                    return distance;
            }

            return Mathf.Max(0.01f, placementMissDistanceFallback);
        }

        /// <summary>把绑定的玩家实例下发给本对传送门下的每一个 CaptureDisplayGroup。</summary>
        public void PushPlayerToGroups()
        {
            CaptureDisplayGroup[] groups = GetComponentsInChildren<CaptureDisplayGroup>(true);
            foreach (CaptureDisplayGroup group in groups)
            {
                if (group == null) continue;
                group.SetPlayer(player != null ? player.gameObject : null);
            }
        }

        /// <summary>
        /// 把画面尺寸配置下发给 A/B 两端的 RegionSnapshotCamera。
        /// 尺寸只在本组件上配置，两个捕获相机共用同一组值。
        /// </summary>
        public void PushViewSettingsToGroups()
        {
            ApplyViewSettings(_groupA);
            ApplyViewSettings(_groupB);
        }

        void ApplyViewSettings(CaptureDisplayGroup group)
        {
            if (group == null) return;
            RegionSnapshotCamera snapshot = group.capture;
            if (snapshot == null) return;

            snapshot.useDynamicSize = useDynamicSize;
            snapshot.cameraSizeMultiplier = new Vector2(
                Mathf.Max(0.01f, cameraSizeMultiplier.x),
                Mathf.Max(0.01f, cameraSizeMultiplier.y));
            snapshot.captureWidth = Mathf.Max(0.01f, captureWidth);
            snapshot.captureHeight = Mathf.Max(0.01f, captureHeight);
        }

        /// <summary>单门 OnEnable/OnDisable 时通知门对重新仲裁连接状态。</summary>
        public void NotifyPortalStateChanged()
        {
            SyncPortalLinkState();
        }

        void SyncPortalLinkState()
        {
            if (_groupA == null && _groupB == null) return;

            bool linked = IsLinked;
            SetGateActive(_gateA, linked && IsPortalActive(_groupA));
            SetGateActive(_gateB, linked && IsPortalActive(_groupB));
            SetCaptureActive(_groupA, linked && IsPortalActive(_groupA));
            SetCaptureActive(_groupB, linked && IsPortalActive(_groupB));

            if (linked == _wasLinked) return;
            _wasLinked = linked;

            if (linked)
            {
                // 渐显前先拿到最新画面，避免第一帧露出旧纹理或空纹理。
                RefreshCaptures();
                _groupA.PlayConnectionReveal();
                _groupB.PlayConnectionReveal();
            }
            else
            {
                if (IsPortalActive(_groupA)) _groupA.PlayConnectionClose();
                else if (_groupA != null) _groupA.SetConnectionClosedImmediate();

                if (IsPortalActive(_groupB)) _groupB.PlayConnectionClose();
                else if (_groupB != null) _groupB.SetConnectionClosedImmediate();
            }

            SyncTerrainCarverPortals();
            UpdateViewTransparency(true);
        }

        static void SetGateActive(BoxCollider2D gate, bool active)
        {
            if (gate != null) gate.enabled = active;
        }

        static void SetCaptureActive(CaptureDisplayGroup group, bool active)
        {
            if (group != null && group.capture != null)
                group.capture.SetCaptureActive(active);
        }

        /// <summary>选中一扇门作为接近基准，其搭档接收透明。</summary>
        public void SelectPortal(CaptureDisplayGroup portal)
        {
            if (portal == null) throw new ArgumentNullException(nameof(portal));
            if (portal != _groupA && portal != _groupB)
                throw new ArgumentException("只能选中当前 PortalPair 的 A/B 门。", nameof(portal));

            SelectedPortal = portal;
            UpdateViewTransparency(true);
        }

        /// <summary>清除手动选中并立即恢复自动仲裁。</summary>
        public void ClearSelectedPortal()
        {
            SelectedPortal = null;
            UpdateViewTransparency(true);
        }

        /// <summary>
        /// 每对门只有一个透明目标。source 始终不透明，target 的 alpha 由两门评分共同决定。
        /// </summary>
        void UpdateViewTransparency(bool force = false)
        {
            if (player == null || !IsLinked)
            {
                if (_groupA != null) _groupA.SetViewAlpha(1f);
                if (_groupB != null) _groupB.SetViewAlpha(1f);
                _automaticViewSource = null;
                return;
            }

            float scoreA = CalculateApproachScore(_groupA);
            float scoreB = CalculateApproachScore(_groupB);

            bool keepPrevious = !force && SelectedPortal == null &&
                                _automaticViewSource != null &&
                                Mathf.Approximately(scoreA, scoreB);

            CaptureDisplayGroup source;
            if (SelectedPortal != null)
                source = SelectedPortal;
            else if (keepPrevious)
                source = _automaticViewSource;
            else
                source = scoreA <= scoreB ? _groupA : _groupB;

            CaptureDisplayGroup target = source == _groupA ? _groupB : _groupA;
            float sourceScore = source == _groupA ? scoreA : scoreB;
            float partnerScore = target == _groupA ? scoreA : scoreB;

            if (SelectedPortal == null)
                _automaticViewSource = source;

            source.SetViewAlpha(1f);
            target.SetViewAlpha(CalculateTargetViewAlpha(
                sourceScore, partnerScore, source.otherViewFadeDistance));
        }

        float CalculateApproachScore(CaptureDisplayGroup portal)
        {
            Transform anchor = portal.ViewAnchor;
            Vector2 delta = PlayerEye - anchor.position;
            float normal = Vector2.Dot(delta, portal.OpeningDirection);
            float tangent = Vector2.Dot(delta, (Vector2)portal.transform.up);

            float normalCost = normal >= 0f
                ? normal
                : Mathf.Max(0f, approachBackPenalty) + Mathf.Abs(normal);

            return normalCost * Mathf.Max(0f, approachNormalWeight) +
                   Mathf.Abs(tangent) * Mathf.Max(0f, approachTangentWeight);
        }

        static float CalculateTargetViewAlpha(float sourceScore, float partnerScore, float maxFade)
        {
            if (maxFade <= 0f) return 1f;

            // 双门都进入衰减范围时缩小阈值，使自动交换点前 target 已恢复为 1。
            float effectiveFade = Mathf.Min(maxFade, Mathf.Max(0f, partnerScore));
            if (effectiveFade <= 0.0001f) return 1f;
            return Mathf.Clamp01(sourceScore / effectiveFade);
        }

        /// <summary>
        /// CaptureDisplayGroup 在 OnEnable/OnDisable 时调用：双门连接期间在关卡地形上
        /// 按门洞矩形开洞，单门或门停用后恢复。传送碰撞箱本身不在这里处理。
        /// </summary>
        public void SetPortalTerrainState(CaptureDisplayGroup group, bool open)
        {
            if (!carveTerrain) return;
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;

            EnsureTerrainCarver();
            if (_terrainCarver != null)
                _terrainCarver.SetPortal(group, open && IsLinked && IsPortalActive(group));
        }

        /// <summary>
        /// 地形开洞组件是运行时按需补上的，这样不用改场景也能生效。
        /// 补上后按当前双门连接状态登记门洞。
        /// </summary>
        void EnsureTerrainCarver()
        {
            if (!carveTerrain)
            {
                if (_terrainCarver != null) _terrainCarver.enabled = false;
                return;
            }

            if (_terrainCarver == null)
                _terrainCarver = GetComponent<PortalTerrainCarver>();
            if (_terrainCarver == null)
                _terrainCarver = gameObject.AddComponent<PortalTerrainCarver>();

            _terrainCarver.carveWidth = Mathf.Max(0.01f, carveWidth);
            _terrainCarver.carveFrontMargin = Mathf.Max(0f, carveFrontMargin);
            _terrainCarver.carveTopMargin = Mathf.Max(0f, carveTopMargin);
            _terrainCarver.enabled = true;

            if (_groupA != null)
                _terrainCarver.SetPortal(_groupA, IsLinked && IsPortalActive(_groupA));
            if (_groupB != null)
                _terrainCarver.SetPortal(_groupB, IsLinked && IsPortalActive(_groupB));
        }

        void SyncTerrainCarverPortals()
        {
            if (!carveTerrain)
            {
                if (_terrainCarver != null) _terrainCarver.enabled = false;
                return;
            }

            if (_terrainCarver == null)
            {
                EnsureTerrainCarver();
                return;
            }

            _terrainCarver.SetPortal(_groupA, IsLinked && IsPortalActive(_groupA));
            _terrainCarver.SetPortal(_groupB, IsLinked && IsPortalActive(_groupB));
        }

        void SyncTerrainCarver()
        {
            if (!carveTerrain)
            {
                if (_terrainCarver != null && _terrainCarver.enabled)
                    _terrainCarver.enabled = false;
                return;
            }

            if (_terrainCarver == null)
            {
                EnsureTerrainCarver();
                return;
            }

            float width = Mathf.Max(0.01f, carveWidth);
            float frontMargin = Mathf.Max(0f, carveFrontMargin);
            float topMargin = Mathf.Max(0f, carveTopMargin);
            bool parametersChanged =
                !Mathf.Approximately(_terrainCarver.carveWidth, width) ||
                !Mathf.Approximately(_terrainCarver.carveFrontMargin, frontMargin) ||
                !Mathf.Approximately(_terrainCarver.carveTopMargin, topMargin);

            _terrainCarver.carveWidth = width;
            _terrainCarver.carveFrontMargin = frontMargin;
            _terrainCarver.carveTopMargin = topMargin;

            if (!_terrainCarver.enabled)
            {
                _terrainCarver.enabled = true;
                SyncTerrainCarverPortals();
            }
            else if (parametersChanged)
            {
                _terrainCarver.RefreshNow();
            }
        }

        /// <summary>
        /// CaptureDisplayGroup 撞到门时转发到这里。只有玩家中心的碰撞点命中、
        /// 且不在冷却中时才真正执行传送。
        /// </summary>
        public void OnGateHit(CaptureDisplayGroup source, Collision2D collision)
        {
            if (!isActiveAndEnabled || !IsPortalActive(source) || _cooldown > 0f) return;
            if (player == null) return;

            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null || collision.collider != controller.CenterPoint) return;

            var partner = source.partner;
            if (!IsPortalActive(partner)) return;

            PlayGateHitAnimations(source);
            Teleport(source, partner);
        }

        void PlayGateHitAnimations(CaptureDisplayGroup source)
        {
            // 联通渐显期间如果已经触发有效传送，立即结束双门的画面渐变，
            // 不再让单门纹理遮挡本次传送后的正常捕获画面。
            if (_groupA != null) _groupA.CompleteConnectionReveal();
            if (_groupB != null) _groupB.CompleteConnectionReveal();

            // 传送时先打断所有门正在执行的开门动画，避免上一次动画的进度/遮罩状态
            // 带到下一次传送。随后只让本次规则选中的门重新从 0 开始执行。
            StopAllOpenAnimations();

            // 哪扇门发起传送，哪扇门就重播一次自己的开门动画；另一扇门保持打开。
            source.PlayOpenAnimation();
        }

        /// <summary>停止所有门的开门动画，并让它们保持完全打开。</summary>
        void StopAllOpenAnimations()
        {
            if (_groupA != null) _groupA.StopOpenAnimation();
            if (_groupB != null) _groupB.StopOpenAnimation();
        }

        internal void RequestCaptureRefresh()
        {
            _forceCapture = true;
        }

        /// <summary>强制两扇门的捕获相机各拍一次，然后重新做半屏裁剪成像。</summary>
        public void RefreshCaptures()
        {
            if (_groupA != null && _groupA.capture != null) _groupA.capture.CaptureNow();
            if (_groupB != null && _groupB.capture != null) _groupB.capture.CaptureNow();

            if (_groupA != null) _groupA.ApplyHalfCrop();
            if (_groupB != null) _groupB.ApplyHalfCrop();
        }

        PlayerControlManager ResolvePlayerControl(Camera followCamera)
        {
            if (playerControl != null) return playerControl;

            if (followCamera != null)
                return followCamera.GetComponent<PlayerControlManager>();

            return FindObjectOfType<PlayerControlManager>();
        }

        /// <summary>
        /// 把玩家送到另一扇门朝向的一侧，只离开碰撞箱一点点；
        /// 垂直方向保留玩家相对本门的高度，速度与相机相对位置一并迁移。
        /// </summary>
        void Teleport(CaptureDisplayGroup source, CaptureDisplayGroup partner)
        {
            if (player == null) return;
            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null) return;

            // 直接传送使用调用当下的门位姿；只有物理扫掠才使用本步采样/插值状态。
            TeleportAt(
                source,
                partner,
                ReadFrameState(source),
                ReadFrameState(partner),
                player.position,
                controller.VelocityBeforePhysics);
        }

        void TeleportAt(
            CaptureDisplayGroup source,
            CaptureDisplayGroup partner,
            PortalFrameState sourceState,
            PortalFrameState partnerState,
            Vector2 playerPosition,
            Vector2 incomingVelocity)
        {
            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null) return;

            bool sameFacing = source.Facing == partner.Facing;
            var rb = player.GetComponent<Rigidbody2D>();

            // 位置按门根节点局部坐标做 X 镜像：(x, y) → (-x, y)。
            // 同 Facing 时 Opening 分量取反，反 Facing 时保留。
            Vector2 sourceOffset = playerPosition - sourceState.Position;
            float xOffset = Vector2.Dot(sourceOffset, sourceState.Right);
            float tangentOffset = Vector2.Dot(sourceOffset, sourceState.Tangent);
            Vector3 target = (Vector3)(partnerState.Position -
                                       partnerState.Right * xOffset +
                                       partnerState.Tangent * tangentOffset);

            // 中心碰撞点必须落在出口门外表面之外；否则冷却期间它会撞门并清零整份刚体速度。
            Vector2 exitOpening = partnerState.Opening;
            float targetNormalOffset = Vector2.Dot(
                (Vector2)target - partnerState.Position, exitOpening);
            float safeExitOffset = Mathf.Max(0f, -gateContactOffset + gateThickness * 0.5f +
                                   controller.centerPointRadius + exitMargin);
            if (targetNormalOffset < safeExitOffset)
            {
                target += (Vector3)(exitOpening * (safeExitOffset - targetNormalOffset));
            }

            // 先解析实际执行传送的总控，再从它所在的相机记录相对位姿。
            Camera followCamera = source.capture != null ? source.capture.sourceCamera : null;
            if (followCamera == null) followCamera = Camera.main;
            PlayerControlManager control = ResolvePlayerControl(followCamera);
            Camera controlledCamera = control != null ? control.GetComponent<Camera>() : null;

            Transform playerVisual = controller.VisualRoot != null
                ? controller.VisualRoot
                : player;

            // 记录来源和后续移动目标必须是同一台相机；缺失时不提交伪单位矩阵。
            Matrix4x4 cameraRelative = controlledCamera != null
                ? playerVisual.worldToLocalMatrix * controlledCamera.transform.localToWorldMatrix
                : Matrix4x4.identity;

            // 普通门保留玩家相对门切线的角度；同向门是镜像映射，相对角必须取反。
            float playerRelativeAngle = Mathf.DeltaAngle(
                sourceState.Angle, controller.CurrentRotationAngle);
            float exitAngle = Mathf.DeltaAngle(
                0f,
                sameFacing
                    ? partnerState.Angle - playerRelativeAngle
                    : partnerState.Angle + playerRelativeAngle);

            // 先扣除入口门在接触点的速度，映射后再叠加出口门点速度。
            // 因而静止玩家被移动门扫到、或从旋转门边缘进入时，结果仍遵循相对运动。
            Vector2 relativeVelocity = incomingVelocity - sourceState.PointVelocity(playerPosition);
            Vector2 velocity = TransformPortalVector(
                relativeVelocity, sourceState, partnerState) + partnerState.PointVelocity(target);
            Vector2 variableJumpDirection = TransformPortalVector(
                controller.VariableJumpDirection, sourceState, partnerState);

            // 剩余长按跳跃与速度走同一套门坐标变换，斜向门不会把它强行折回世界 Y 轴。
            controller.TeleportWithPortalRotation(
                target, exitAngle, variableJumpDirection, preserveVelocity);

            if (preserveVelocity)
                controller.SetPortalExitVelocity(velocity);

            // 玩家已经完成瞬移和旋转；一次性传全相对位姿与同向镜像规则。
            if (controlledCamera != null)
                control.TeleportRelative(playerVisual, cameraRelative, sameFacing);

            // 一对门共享一个冷却，避免刚落地就被另一扇门立刻传回去。
            _cooldown = teleportCooldown;
            _previousPlayerPosition = rb != null ? rb.position : (Vector2)target;
            _observedPlayerRevision = controller.PositionRevision;
            _hasPlayerBaseline = true;

            // 本帧 LateUpdate 里强制两扇门各捕获一次，并重新生成半屏画面。
            _forceCapture = true;

            // 同向传送仍由 PortalPair 判定，总控协调相机、输入和一次性精灵镜像。
            if (sameFacing && control != null)
                control.ApplySameFacingPortalTeleport(controller);

            // 出口门必须当帧恢复不透明，目标交换不等待下一次 LateUpdate。
            UpdateViewTransparency(true);
        }

        /// <summary>
        /// 把进门时的速度/受力向量变换到出口门的坐标系：
        /// 沿门法线（开口方向）的分量反向，沿门切线（门 Y+）的分量保留。
        /// 同 Facing 时等价于门局部坐标的 X 镜像，相对旋转则由两门的坐标轴处理。
        /// </summary>
        static Vector2 TransformPortalVector(
            Vector2 vector, PortalFrameState source, PortalFrameState partner)
        {
            float normalComponent = Vector2.Dot(vector, source.Opening);
            float tangentComponent = Vector2.Dot(vector, source.Tangent);
            return partner.Opening * -normalComponent + partner.Tangent * tangentComponent;
        }

        internal CaptureDisplayGroup GetGroup(CaptureGroupSide side)
        {
            if (side == CaptureGroupSide.A) return _groupA;
            if (side == CaptureGroupSide.B) return _groupB;
            return null;
        }

        void CacheGroups()
        {
            CaptureDisplayGroup[] groups = GetComponentsInChildren<CaptureDisplayGroup>(true);

            _groupA = null;
            _groupB = null;
            foreach (CaptureDisplayGroup group in groups)
            {
                if (group == null) continue;
                if (group.side == CaptureGroupSide.A) _groupA = group;
                else _groupB = group;
            }
        }

        void EnsureGates()
        {
            _gateA = EnsureGate(_groupA);
            _gateB = EnsureGate(_groupB);
        }

        BoxCollider2D EnsureGate(CaptureDisplayGroup group)
        {
            if (group == null) return null;

            var gate = group.GetComponent<BoxCollider2D>();
            if (gate == null)
                gate = group.gameObject.AddComponent<BoxCollider2D>();

            gate.isTrigger = false;
            gate.offset = new Vector2(-group.FacingSign * gateContactOffset, 0f);
            gate.size = new Vector2(gateThickness, group.PortalHeight);
            gate.enabled = IsLinked && IsPortalActive(group);

            var body = group.GetComponent<Rigidbody2D>();
            if (body == null) body = group.gameObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Kinematic;
            body.gravityScale = 0f;
            body.constraints = RigidbodyConstraints2D.FreezeRotation;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.interpolation = RigidbodyInterpolation2D.None;
            body.useFullKinematicContacts = true;
            if (group == _groupA) _gateBodyA = body;
            if (group == _groupB) _gateBodyB = body;

            // 门碰撞箱放到 Snapshot 层，玩家身体碰撞体用 excludeLayers 忽略该层。
            // 层过滤不会像 Physics2D.IgnoreCollision 那样在碰撞箱重新启用后被重置。
            group.gameObject.layer = GateLayer;
            return gate;
        }

        void UpdateGateSizes()
        {
            UpdateGateSize(_gateA, _groupA);
            UpdateGateSize(_gateB, _groupB);
        }

        void UpdateGateSize(BoxCollider2D gate, CaptureDisplayGroup group)
        {
            if (gate == null || group == null) return;

            Vector2 size = new Vector2(gateThickness, group.PortalHeight);
            if (gate.size != size)
                gate.size = size;

            Vector2 offset = new Vector2(-group.FacingSign * gateContactOffset, 0f);
            if (gate.offset != offset)
                gate.offset = offset;
        }

        /// <summary>
        /// 身体碰撞体不参与和碰撞箱的碰撞，只让玩家中心的碰撞点去撞。
        /// 用层过滤而不是 Physics2D.IgnoreCollision：后者在碰撞箱被禁用/重新启用后会被重置，
        /// 重新开门时玩家会把薄碰撞箱当成墙。
        /// </summary>
        void IgnorePlayerBodyCollision()
        {
            RestorePlayerBodyCollision();
            if (player == null) return;

            var body = player.GetComponent<Collider2D>();
            if (body == null) return;

            _ignoredPlayerBody = body;
            _originalBodyExcludeLayers = body.excludeLayers;
            body.excludeLayers |= 1 << GateLayer;
        }

        void RestorePlayerBodyCollision()
        {
            if (_ignoredPlayerBody == null) return;

            int gateMask = 1 << GateLayer;
            int layers = _ignoredPlayerBody.excludeLayers;
            if ((_originalBodyExcludeLayers & gateMask) != 0) layers |= gateMask;
            else layers &= ~gateMask;

            // 只恢复本系统负责的位，保留其他系统运行期间所做的层过滤修改。
            _ignoredPlayerBody.excludeLayers = layers;
            _ignoredPlayerBody = null;
        }

        void OnDrawGizmosSelected()
        {
            // 把不可见的薄碰撞箱画出来，方便调厚度。
            CaptureDisplayGroup[] groups = GetComponentsInChildren<CaptureDisplayGroup>(true);
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
            foreach (CaptureDisplayGroup group in groups)
            {
                if (group == null) continue;
                Gizmos.DrawWireCube(group.transform.position,
                    new Vector3(Mathf.Max(gateThickness, 0.01f), group.PortalHeight, 0.01f));
            }
        }
    }
}
