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
    public class PortalPair : MonoBehaviour
    {
        [Header("玩家")]
        [Tooltip("玩家对象，遮罩朝向和传送都以它为参照")]
        public Transform player;

        [Tooltip("眼睛相对玩家中心点的高度（世界单位）")]
        public float playerEyeOffset = 0.4f;

        [Header("玩家与相机总控")]
        [Tooltip("统一处理输入、相机镜像和相机传送；留空时从跟随相机解析。")]
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

        [Tooltip("传送后玩家中心离碰撞箱外表面的额外距离（世界单位），建议大于物理接触偏移 0.01")]
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

        [Header("地形开洞")]
        [Tooltip("传送门激活时，把门洞矩形覆盖到的地形格改为无碰撞，再重建 CompositeCollider2D")]
        public bool carveTerrain = true;

        [Tooltip("开洞矩形沿门朝向反方向的长度（世界单位）")]
        [Min(0.01f)] public float carveWidth = 0.5f;

        [Tooltip("为了让玩家胶囊能贴到门面，额外向门面方向扩展的长度；0 = 严格从中轴开始")]
        [Min(0f)] public float carveFrontMargin = 0.1f;

        /// <summary>门洞高度，由上下遮罩偏移算出，两扇门共用。</summary>
        public float PortalHeight => Mathf.Abs(maskTopOffset - maskBottomOffset);

        CaptureDisplayGroup _groupA;
        CaptureDisplayGroup _groupB;

        BoxCollider2D _gateA;
        BoxCollider2D _gateB;
        PortalTerrainCarver _terrainCarver;

        float _cooldown;
        bool _forceCapture;
        Transform _lastPlayer;
        CaptureDisplayGroup _automaticViewSource;
        CapsuleCollider2D _ignoredPlayerBody;
        int _originalBodyExcludeLayers;

        /// <summary>门碰撞箱所在层（复用 Snapshot 层，玩家身体胶囊会排除该层）。</summary>
        const int GateLayer = 8;

        /// <summary>手动选中的接近基准门；null 表示自动仲裁。</summary>
        public CaptureDisplayGroup SelectedPortal { get; private set; }

        /// <summary>玩家眼睛的世界坐标。</summary>
        public Vector3 PlayerEye => player != null
            ? player.position + Vector3.up * playerEyeOffset
            : Vector3.zero;

        /// <summary>当前是否处于传送冷却中。</summary>
        public bool IsTeleportCoolingDown => _cooldown > 0f;

        void Awake()
        {
            CacheGroups();
            EnsureGates();
            EnsureTerrainCarver();
            PushPlayerToGroups();
            PushViewSettingsToGroups();
            IgnorePlayerBodyCollision();
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
            UpdateViewTransparency(true);
        }

        void OnEnable()
        {
            // 重新启用后重新绑定玩家碰撞过滤；禁用时会恢复原始位。
            _lastPlayer = null;
            UpdateViewTransparency(true);
        }

        void OnDisable()
        {
            RestorePlayerBodyCollision();
            if (_groupA != null) _groupA.SetViewAlpha(1f);
            if (_groupB != null) _groupB.SetViewAlpha(1f);
            _automaticViewSource = null;
        }

        void Update()
        {
            // 支持运行时换玩家：重新下发引用，并让新玩家的身体胶囊排除门层。
            if (_lastPlayer != player)
            {
                _lastPlayer = player;
                PushPlayerToGroups();
                IgnorePlayerBodyCollision();
                UpdateViewTransparency(true);
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
            if (player == null || _groupA == null || _groupB == null)
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
        /// CaptureDisplayGroup 在 OnEnable/OnDisable 时调用：门激活期间在关卡地形上
        /// 按门洞矩形开洞，门停用后恢复。传送碰撞箱本身不在这里处理。
        /// </summary>
        public void SetPortalTerrainState(CaptureDisplayGroup group, bool open)
        {
            if (!carveTerrain) return;
            if (!gameObject.scene.IsValid() || !gameObject.scene.isLoaded) return;

            EnsureTerrainCarver();
            if (_terrainCarver != null) _terrainCarver.SetPortal(group, open);
        }

        /// <summary>
        /// 地形开洞组件是运行时按需补上的，这样不用改场景也能生效。
        /// 补上后把当前已激活的两扇门登记一次。
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
            _terrainCarver.enabled = true;

            if (_groupA != null)
                _terrainCarver.SetPortal(_groupA, _groupA.isActiveAndEnabled);
            if (_groupB != null)
                _terrainCarver.SetPortal(_groupB, _groupB.isActiveAndEnabled);
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
            bool parametersChanged =
                !Mathf.Approximately(_terrainCarver.carveWidth, width) ||
                !Mathf.Approximately(_terrainCarver.carveFrontMargin, frontMargin);

            _terrainCarver.carveWidth = width;
            _terrainCarver.carveFrontMargin = frontMargin;

            if (!_terrainCarver.enabled)
                _terrainCarver.enabled = true; // OnEnable 会恢复旧洞并按当前门状态重切。
            else if (parametersChanged)
                _terrainCarver.RefreshNow();
        }

        /// <summary>
        /// CaptureDisplayGroup 撞到门时转发到这里。只有玩家中心的碰撞点命中、
        /// 且不在冷却中时才真正执行传送。
        /// </summary>
        public void OnGateHit(CaptureDisplayGroup source, Collision2D collision)
        {
            if (source == null || _cooldown > 0f) return;
            if (player == null) return;

            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null || collision.collider != controller.CenterPoint) return;

            var partner = source.partner;
            if (partner == null) return;

            // 传送时先打断所有门正在执行的开门动画，避免上一次动画的进度/遮罩状态
            // 带到下一次传送。随后只让本次规则选中的门重新从 0 开始执行。
            StopAllOpenAnimations();

            // 哪扇门发起传送，哪扇门就重播一次自己的开门动画；另一扇门保持打开。
            source.PlayOpenAnimation();

            Teleport(source, partner);
        }

        /// <summary>停止所有门的开门动画，并让它们保持完全打开。</summary>
        void StopAllOpenAnimations()
        {
            if (_groupA != null) _groupA.StopOpenAnimation();
            if (_groupB != null) _groupB.StopOpenAnimation();
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
            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null) return;

            bool sameFacing = source.Facing == partner.Facing;

            var rb = player.GetComponent<Rigidbody2D>();
            Vector3 playerPosition = player.position;
            Vector3 sourceCenter = source.transform.position;
            Vector3 partnerCenter = partner.transform.position;

            // 出口：对方门开口方向外侧 + 入口时沿门切线（门 Y+）的相对位置。
            // 不能用世界 Y 来保留高度；门旋转后，世界 Y 会掺进法线方向，
            // 导致玩家被放到门里或离门太近，出现“刚出来又碰到门”。
            float front = gateThickness * 0.5f + controller.centerPointRadius + exitMargin;
            Vector2 exitDirection = partner.OpeningDirection;
            Vector2 sourceTangent = source.transform.up;
            Vector2 partnerTangent = partner.transform.up;

            float tangentOffset = Vector2.Dot(
                (Vector2)playerPosition - (Vector2)sourceCenter, sourceTangent);

            Vector3 target = partnerCenter +
                             (Vector3)(exitDirection * front) +
                             (Vector3)(partnerTangent * tangentOffset);

            // 传送前记录相机在玩家显示空间中的全部相对变换（位置 + 旋转）。
            Camera followCamera = source.capture != null ? source.capture.sourceCamera : null;
            if (followCamera == null) followCamera = Camera.main;
            PlayerControlManager control = ResolvePlayerControl(followCamera);

            Transform playerVisual = controller.VisualRoot != null
                ? controller.VisualRoot
                : player;

            // 完整相对空间：位置、旋转、缩放都包含在矩阵里。
            Matrix4x4 cameraRelative = followCamera != null
                ? playerVisual.worldToLocalMatrix * followCamera.transform.localToWorldMatrix
                : Matrix4x4.identity;


            // 门间角度差叠加到玩家当前显示角；回正未完成时连续传送也不会跳回绝对角。
            float sourceAngle = Vector2.SignedAngle(Vector2.up, source.transform.up);
            float partnerAngle = Vector2.SignedAngle(Vector2.up, partner.transform.up);
            float portalDelta = Mathf.DeltaAngle(sourceAngle, partnerAngle);
            float exitAngle = Mathf.DeltaAngle(0f, controller.CurrentRotationAngle + portalDelta);

            // 进门前的速度/受力结果也按传送门坐标系一起变换：
            // 法线分量沿出口正面反向，切线分量保留。
            Vector2 velocity = TransformPortalVelocity(
                controller.VelocityBeforePhysics, source, partner);

            controller.TeleportWithPortalRotation(target, exitAngle);

            if (preserveVelocity && rb != null)
                rb.velocity = velocity;

            // 玩家已经完成瞬移和旋转；相机具体映射由总控交给跟随组件执行。
            if (control != null)
                control.TeleportRelative(playerVisual, cameraRelative);


            // 一对门共享一个冷却，避免刚落地就被另一扇门立刻传回去。
            _cooldown = teleportCooldown;

            // 本帧 LateUpdate 里强制两扇门各捕获一次，并重新生成半屏画面。
            _forceCapture = true;

            // 同向传送仍由 PortalPair 决定联动规则，总控只执行状态和相机指令。
            if (sameFacing && control != null)
            {
                control.ToggleMirrorAndInput();
                control.MirrorPendingCameraPosition(playerVisual);
            }

            // 出口门必须当帧恢复不透明，目标交换不等待下一次 LateUpdate。
            UpdateViewTransparency(true);
        }

        /// <summary>
        /// 把进门时的速度/受力向量变换到出口门的坐标系：
        /// 沿门法线（开口方向）的分量反向，沿门切线（门 Y+）的分量保留。
        /// 这样两门同向时会被反向，相对旋转时速度也会跟着转。
        /// </summary>
        static Vector2 TransformPortalVelocity(
            Vector2 velocity, CaptureDisplayGroup source, CaptureDisplayGroup partner)
        {
            Vector2 sourceNormal = source.OpeningDirection;
            Vector2 sourceTangent = source.transform.up;
            Vector2 partnerNormal = partner.OpeningDirection;
            Vector2 partnerTangent = partner.transform.up;

            float normalSpeed = Vector2.Dot(velocity, sourceNormal);
            float tangentSpeed = Vector2.Dot(velocity, sourceTangent);

            return partnerNormal * (-normalSpeed) + partnerTangent * tangentSpeed;
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
            gate.offset = Vector2.zero;
            gate.size = new Vector2(gateThickness, group.PortalHeight);

            // 门碰撞箱放到 Snapshot 层，玩家身体胶囊用 excludeLayers 忽略该层。
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
        }

        /// <summary>
        /// 身体胶囊不参与和碰撞箱的碰撞，只让玩家中心的碰撞点去撞。
        /// 用层过滤而不是 Physics2D.IgnoreCollision：后者在碰撞箱被禁用/重新启用后会被重置，
        /// 重新开门时玩家会把薄碰撞箱当成墙。
        /// </summary>
        void IgnorePlayerBodyCollision()
        {
            RestorePlayerBodyCollision();
            if (player == null) return;

            var body = player.GetComponent<CapsuleCollider2D>();
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
