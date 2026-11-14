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

        /// <summary>门洞高度，由上下遮罩偏移算出，两扇门共用。</summary>
        public float PortalHeight => Mathf.Abs(maskTopOffset - maskBottomOffset);

        CaptureDisplayGroup _groupA;
        CaptureDisplayGroup _groupB;

        BoxCollider2D _gateA;
        BoxCollider2D _gateB;

        float _cooldown;
        bool _forceCapture;

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
            PushPlayerToGroups();
            IgnorePlayerBodyCollision();
        }

        void Start()
        {
            // 再执行一次：确保所有 CaptureDisplayGroup 的 Awake 都已执行完毕。
            CacheGroups();
            EnsureGates();
            PushPlayerToGroups();
            IgnorePlayerBodyCollision();
        }

        void Update()
        {
            if (_cooldown > 0f)
                _cooldown -= Time.deltaTime;
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

            // 出口：对方门 facing 一侧，半厚 + 中心碰撞点半径 + 余量。
            float front = gateThickness * 0.5f + controller.centerPointRadius + exitMargin;
            Vector3 target = new Vector3(
                partnerCenter.x + partner.FacingSign * front,
                partnerCenter.y + (playerPosition.y - sourceCenter.y),
                playerPosition.z);

            // 传送前记录摄像机与玩家的相对位置（主相机由 CameraFollow2D 跟随玩家）。
            Camera followCamera = source.capture != null ? source.capture.sourceCamera : null;
            if (followCamera == null) followCamera = Camera.main;
            Vector3 cameraOffset = followCamera != null
                ? followCamera.transform.position - playerPosition
                : Vector3.zero;

            // 碰撞回调里 rb.velocity 可能已经被求解器改掉，优先用物理步开始前的速度。
            Vector2 velocity = controller.VelocityBeforePhysics;

            // 走角色控制器的瞬移接口（会同时同步刚体和 Transform）。
            controller.Teleport(target);

            if (preserveVelocity && rb != null)
            {
                // 两门朝向相同则水平速度取反；相背则保持原方向。
                if (source.FacingSign == partner.FacingSign)
                    velocity.x = -velocity.x;
                rb.velocity = velocity;
            }

            // 摄像机瞬间跟到传送后的玩家身边，保持传送前的相对位置。
            if (followCamera != null)
                followCamera.transform.position = player.position + cameraOffset;

            // 一对门共享一个冷却，避免刚落地就被另一扇门立刻传回去。
            _cooldown = teleportCooldown;

            // 本帧 LateUpdate 里强制两扇门各捕获一次，并重新生成半屏画面。
            _forceCapture = true;
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
