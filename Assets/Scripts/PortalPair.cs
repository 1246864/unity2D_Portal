using UnityEngine;

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

        /// <summary>门碰撞箱所在层（复用 Snapshot 层，玩家身体胶囊会排除该层）。</summary>
        const int GateLayer = 8;

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
            IgnorePlayerBodyCollision();
        }

        void Start()
        {
            // 再执行一次：确保所有 CaptureDisplayGroup 的 Awake 都已执行完毕。
            CacheGroups();
            EnsureGates();
            EnsureTerrainCarver();
            PushPlayerToGroups();
            IgnorePlayerBodyCollision();
        }

        void Update()
        {
            // 支持运行时换玩家：重新下发引用，并让新玩家的身体胶囊排除门层。
            if (_lastPlayer != player)
            {
                _lastPlayer = player;
                PushPlayerToGroups();
                IgnorePlayerBodyCollision();
            }

            if (_cooldown > 0f)
                _cooldown -= Time.deltaTime;

            // 允许在 Inspector/运行时改开关与宽度。
            if (carveTerrain)
            {
                if (_terrainCarver == null) EnsureTerrainCarver();
                else if (!Mathf.Approximately(_terrainCarver.carveWidth, Mathf.Max(0.01f, carveWidth)) ||
                         !Mathf.Approximately(_terrainCarver.carveFrontMargin, Mathf.Max(0f, carveFrontMargin)))
                {
                    _terrainCarver.carveWidth = Mathf.Max(0.01f, carveWidth);
                    _terrainCarver.carveFrontMargin = Mathf.Max(0f, carveFrontMargin);
                    _terrainCarver.RefreshNow();
                }
            }
            else if (_terrainCarver != null && _terrainCarver.enabled)
            {
                _terrainCarver.enabled = false;
            }
        }

        void LateUpdate()
        {
            UpdateGateSizes();

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

            // 哪扇门发起传送，哪扇门就重播一次自己的开门动画；另一扇门不动。
            source.PlayOpenAnimation();

            Teleport(source, partner);
        }

        /// <summary>强制两扇门的捕获相机各拍一次，然后重新做半屏裁剪成像。</summary>
        public void RefreshCaptures()
        {
            if (_groupA != null && _groupA.capture != null) _groupA.capture.CaptureNow();
            if (_groupB != null && _groupB.capture != null) _groupB.capture.CaptureNow();

            if (_groupA != null) _groupA.ApplyHalfCrop();
            if (_groupB != null) _groupB.ApplyHalfCrop();
        }

        /// <summary>
        /// 把玩家送到另一扇门朝向的一侧，只离开碰撞箱一点点；
        /// 垂直方向保留玩家相对本门的高度，速度与相机相对位置一并迁移。
        /// </summary>
        void Teleport(CaptureDisplayGroup source, CaptureDisplayGroup partner)
        {
            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null) return;

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


            // 出口旋转 = 出口门角度 - 入口门角度。
            // 玩家进门时是竖直的，所以不需要再叠玩家角度。
            float sourceAngle = Vector2.SignedAngle(Vector2.up, source.transform.up);
            float partnerAngle = Vector2.SignedAngle(Vector2.up, partner.transform.up);
            float exitAngle = Mathf.DeltaAngle(0f, partnerAngle - sourceAngle);

            // 进门前的速度/受力结果也按传送门坐标系一起变换：
            // 法线分量沿出口正面反向，切线分量保留。
            Vector2 velocity = TransformPortalVelocity(
                controller.VelocityBeforePhysics, source, partner);

            controller.TeleportWithPortalRotation(target, exitAngle);

            if (preserveVelocity && rb != null)
                rb.velocity = velocity;


            // 一对门共享一个冷却，避免刚落地就被另一扇门立刻传回去。
            _cooldown = teleportCooldown;

            // 本帧 LateUpdate 里强制两扇门各捕获一次，并重新生成半屏画面。
            _forceCapture = true;
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
            if (player == null) return;

            var body = player.GetComponent<CapsuleCollider2D>();
            if (body == null) return;

            body.excludeLayers |= 1 << GateLayer;
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
