using UnityEngine;

namespace Platformer
{
    /// <summary>本组属于捕获对中的哪一半。</summary>
    public enum CaptureGroupSide
    {
        [InspectorName("A 门")] A,
        [InspectorName("B 门")] B
    }

    /// <summary>门的朝向：朝左或朝右。</summary>
    public enum PortalFacing
    {
        [InspectorName("朝左")] Left,
        [InspectorName("朝右")] Right
    }

    /// <summary>
    /// 一个捕获显示组：把快照相机、输出精灵和捕获控制器打包在同一个空节点下。
    ///
    /// 两组并排放在同一父节点下。每组会自己找到另一侧的那一组并交叉连线：
    /// 本组的相机画到对方的输出上，对方的相机画到本组的输出上。
    ///
    /// 两块画面遮罩把输出裁成门洞的形状，并且始终朝向玩家的眼睛。
    /// </summary>
    public class CaptureDisplayGroup : MonoBehaviour
    {
        [Tooltip("本组是 A 还是 B；会自动和另一侧配对")]
        public CaptureGroupSide side = CaptureGroupSide.A;

        [Tooltip("自动解析出的另一扇门，无需手填")]
        public CaptureDisplayGroup partner;

        [Header("内部引用")]
        [Tooltip("本组的捕获控制器")]
        public RegionSnapshotCamera capture;
        [Tooltip("本组的画面精灵（会被对方那组画上画面）")]
        public SpriteRenderer output;

        [Tooltip("本门显示精灵所在的游戏对象（Start 时自动获取）")]
        public GameObject viewObject;

        [Tooltip("左右两半在中线各让出的像素数。留一点缝可以避免递归把共用像素列放大成一条亮线")]
        [Range(0f, 16f)] public float centerGapPixels = 1f;

        [Header("玩家")]
        [Tooltip("本对传送门绑定的玩家游戏对象，由 Portal Pair 下发，无需手填")]
        public GameObject player;

        [Header("传送")]
        [Tooltip("传送冷却（秒），避免连续帧反复触发")]
        [Min(0f)] public float teleportCooldown = 0.25f;

        [Tooltip("传送后是否保留速度（两门朝向相同则水平速度取反，相背则保持）")]
        public bool preserveVelocity = true;

        [Header("碰撞箱")]
        [Tooltip("传送门薄碰撞箱的厚度（世界单位）；越薄越依赖连续碰撞")]
        [Min(0.001f)] public float gateThickness = 0.05f;

        [Tooltip("传送后玩家中心离碰撞箱外表面的额外距离（世界单位），建议大于物理接触偏移 0.01")]
        [Min(0f)] public float exitMargin = 0.03f;

        [Header("传送门")]
        [Tooltip("门的朝向：定义出口在 Y 轴（竖直方向）的哪一侧；传送时据此决定出面对方向")]
        public PortalFacing facing = PortalFacing.Right;

        [Tooltip("上遮罩的 Y 偏移（世界单位）")]
        public float maskTopOffset = 1.5f;

        [Tooltip("下遮罩的 Y 偏移（世界单位）")]
        public float maskBottomOffset = -1.5f;

        [Header("遮罩")]
        [Tooltip("上遮罩节点（自动查找 View Mask Top）")]
        public Transform maskTop;
        [Tooltip("下遮罩节点（自动查找 View Mask Bottom）")]
        public Transform maskBottom;

        /// <summary>门洞高度，由上下两块遮罩的偏移算出。</summary>
        public float PortalHeight => Mathf.Abs(maskTopOffset - maskBottomOffset);

        /// <summary>本门朝左时返回 true。</summary>
        public bool FacesLeft => facing == PortalFacing.Left;

        /// <summary>朝左为 -1，朝右为 +1。</summary>
        public float FacingSign => FacesLeft ? -1f : 1f;

        /// <summary>玩家的 Transform（未下发玩家时为 null）。</summary>
        public Transform PlayerTransform => player != null ? player.transform : null;

        Sprite _halfSprite;
        PortalPair _pair;

        float _teleportCooldown;
        BoxCollider2D _gate;
        bool _forceCapture;

        void Awake()
        {
            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            ResolveMaskRig();
            EnsureGate();
            CachePlayer();
            ConfigurePlayerCollision();
        }

        void Start()
        {
            // 获取本门显示精灵所在的游戏对象。
            if (output != null)
                viewObject = output.gameObject;

            EnsureGate();
            CachePlayer();
            ConfigurePlayerCollision();
        }

        /// <summary>接收 PortalPair 下发的玩家实例。</summary>
        public void SetPlayer(GameObject playerObject)
        {
            player = playerObject;
            ConfigurePlayerCollision();
        }

        /// <summary>玩家尚未下发时，从所属的 PortalPair 上取一次。</summary>
        void CachePlayer()
        {
            if (player != null) return;

            if (_pair == null) _pair = GetComponentInParent<PortalPair>();
            if (_pair != null && _pair.player != null)
                player = _pair.player.gameObject;
        }

        void LateUpdate()
        {
            UpdateGateSize();

            if (_forceCapture)
            {
                _forceCapture = false;
                ForceCaptureBoth();
            }

            ApplyHalfCrop();
            UpdateMasks();
        }

        void Update()
        {
            CachePlayer();
            UpdateViewColor();
            UpdateVisibility();

            if (_teleportCooldown > 0f)
                _teleportCooldown -= Time.deltaTime;
        }

        /// <summary>
        /// 用 Y 轴正方向按 facing 旋转 90° 得到开口方向（朝右 -90°，朝左 +90°）。
        /// 玩家相对门的向量与开口方向的夹角 ≥ 90° 时，说明玩家绕到了门后面，隐藏本门画面。
        /// </summary>
        void UpdateVisibility()
        {
            if (output == null) return;

            CachePlayer();
            if (player == null)
            {
                output.enabled = true;
                return;
            }

            Vector3 openingDirection = Quaternion.Euler(0f, 0f, FacingSign * -90f) * Vector3.up;
            Vector3 toPlayer = player.transform.position - transform.position;

            // 点积 <= 0 等价于夹角 >= 90°，此时玩家在门的背面。
            output.enabled = Vector3.Dot(openingDirection, toPlayer) > 0f;
        }

        /// <summary>根据玩家距离更新本门显示精灵的颜色。</summary>
        void UpdateViewColor()
        {
            if (player == null) return;

            // 玩家到本对象局部 Y 轴的垂直距离。
            float playerDistanceToYAxis = Mathf.Abs(transform.InverseTransformPoint(player.transform.position).x);

            float color_offs = 0.5f;
            if(playerDistanceToYAxis <= 5f)
            {
                color_offs = playerDistanceToYAxis / 10f;
            }
            if (side == CaptureGroupSide.B) {
                viewObject.GetComponent<SpriteRenderer>().color = new Color(1f, 1f - color_offs, 1f - color_offs, 1f);
            }
            else
            {
                viewObject.GetComponent<SpriteRenderer>().color = new Color(1f - color_offs, 1f - color_offs, 1f, 1f);
            }
        }

        /// <summary>玩家中心的碰撞点撞到本门的薄碰撞箱时触发传送。</summary>
        void OnCollisionEnter2D(Collision2D collision)
        {
            TryTeleportFromCollision(collision);
        }

        /// <summary>冷却结束时如果仍贴着门，也要能触发。</summary>
        void OnCollisionStay2D(Collision2D collision)
        {
            TryTeleportFromCollision(collision);
        }

        void TryTeleportFromCollision(Collision2D collision)
        {
            if (_teleportCooldown > 0f) return;
            if (player == null) CachePlayer();
            if (player == null || partner == null) return;

            // 只有玩家中心的碰撞点命中才算触发；身体胶囊已经和碰撞箱忽略了。
            var controller = player.GetComponent<PlayerController2D>();
            if (controller == null || collision.collider != controller.CenterPoint) return;

            TeleportToPartner();
        }

        /// <summary>
        /// 把玩家强制送到对方门朝向的一侧，只离开碰撞箱一点点；
        /// 垂直方向保留玩家相对本门的高度。
        /// </summary>
        void TeleportToPartner()
        {
            Vector3 playerPosition = player.transform.position;
            Vector3 portalCenter = transform.position;
            Vector3 partnerCenter = partner.transform.position;

            var rb = player.GetComponent<Rigidbody2D>();
            var controller = player.GetComponent<PlayerController2D>();

            // 只推到对方门正面、碰撞箱外一点点：半厚 + 中心碰撞点半径 + 余量。
            float centerRadius = controller != null ? controller.centerPointRadius : 0.08f;
            float front = gateThickness * 0.5f + centerRadius + exitMargin;

            Vector3 target = new Vector3(
                partnerCenter.x + partner.FacingSign * front,
                partnerCenter.y + (playerPosition.y - portalCenter.y),
                playerPosition.z);

            // 传送前记录摄像机与玩家的相对位置（主相机由 CameraFollow2D 跟随玩家）。
            Camera followCamera = capture != null ? capture.sourceCamera : null;
            if (followCamera == null) followCamera = Camera.main;
            Vector3 cameraOffset = followCamera != null
                ? followCamera.transform.position - playerPosition
                : Vector3.zero;

            // 碰撞回调里 rb.velocity 可能已经被求解器改掉，优先用物理步开始前的速度。
            Vector2 velocity = controller != null
                ? controller.VelocityBeforePhysics
                : (rb != null ? rb.velocity : Vector2.zero);

            // 优先走角色控制器的瞬移接口（会同时同步刚体和 Transform）。
            if (controller != null)
            {
                controller.Teleport(target);
            }
            else if (rb != null)
            {
                rb.position = target;
                player.transform.position = target;
                Physics2D.SyncTransforms();
            }
            else
            {
                player.transform.position = target;
            }

            if (preserveVelocity && rb != null)
            {
                // 两门朝向相同则水平速度取反；相反则保持原方向。
                if (FacingSign == partner.FacingSign)
                    velocity.x = -velocity.x;
                rb.velocity = velocity;
            }

            // 摄像机瞬间跟到传送后的玩家身边，保持传送前的相对位置。
            if (followCamera != null)
                followCamera.transform.position = player.transform.position + cameraOffset;

            // 两扇门一起进入冷却，避免刚落地就被另一扇门立刻传回去。
            _teleportCooldown = teleportCooldown;
            partner._teleportCooldown = teleportCooldown;

            // 本帧 LateUpdate 里强制两扇门各捕获一次，并重新生成半屏画面。
            _forceCapture = true;
        }

        /// <summary>强制两扇门的捕获相机各拍一次，然后重新做半屏裁剪成像。</summary>
        void ForceCaptureBoth()
        {
            // 本组的相机画到对方的画面上，所以两边都要拍。
            if (capture != null) capture.CaptureNow();
            if (partner != null)
            {
                partner._forceCapture = false;
                if (partner.capture != null) partner.capture.CaptureNow();
            }

            ApplyHalfCrop();
            if (partner != null) partner.ApplyHalfCrop();
        }

        /// <summary>创建/刷新本门的薄碰撞箱，高度等于门洞高度。</summary>
        void EnsureGate()
        {
            if (_gate == null)
            {
                _gate = GetComponent<BoxCollider2D>();
                if (_gate == null)
                    _gate = gameObject.AddComponent<BoxCollider2D>();
            }

            _gate.isTrigger = false;
            _gate.offset = Vector2.zero;
            _gate.size = new Vector2(gateThickness, PortalHeight);
        }

        void UpdateGateSize()
        {
            if (_gate == null)
            {
                EnsureGate();
                return;
            }

            Vector2 size = new Vector2(gateThickness, PortalHeight);
            if (_gate.size != size)
                _gate.size = size;
        }

        /// <summary>身体胶囊不参与和碰撞箱的碰撞，只让玩家中心的碰撞点去撞。</summary>
        void ConfigurePlayerCollision()
        {
            CachePlayer();
            if (player == null || _gate == null) return;

            var body = player.GetComponent<CapsuleCollider2D>();
            if (body != null)
                Physics2D.IgnoreCollision(body, _gate, true);
        }

        void OnDrawGizmosSelected()
        {
            // 把不可见的薄碰撞箱画出来，方便调厚度。
            Gizmos.color = new Color(1f, 0.45f, 0.2f, 0.9f);
            Gizmos.DrawWireCube(transform.position,
                new Vector3(Mathf.Max(gateThickness, 0.01f), PortalHeight, 0.01f));
        }

        /// <summary>在兄弟节点中找到另一侧的组，并把两者连接起来。</summary>
        public void ResolvePartner()
        {
            if (partner != null && partner.side != side) return;

            CaptureDisplayGroup[] candidates = transform.parent != null
                ? transform.parent.GetComponentsInChildren<CaptureDisplayGroup>(true)
                : FindObjectsOfType<CaptureDisplayGroup>();

            partner = null;
            foreach (var candidate in candidates)
            {
                if (candidate == this) continue;
                if (candidate.side == side) continue;

                partner = candidate;
                break;
            }
        }

        /// <summary>双向连线：每组的相机都画到另一组的输出上。</summary>
        public void WireUp()
        {
            if (capture == null || output == null) return;
            if (partner == null || partner.capture == null || partner.output == null) return;

            capture.output = partner.output;
            partner.capture.output = output;
        }

        /// <summary>找到本组下面创建的遮罩节点。</summary>
        public void ResolveMaskRig()
        {
            if (maskTop != null && maskBottom != null) return;

            Transform maskRoot = transform.Find("Portal Mask");
            if (maskRoot == null) maskRoot = transform;
            if (maskTop == null) maskTop = maskRoot.Find("View Mask Top");
            if (maskBottom == null) maskBottom = maskRoot.Find("View Mask Bottom");
        }

        /// <summary>
        /// 把两块遮罩放回各自的偏移位置，并转向玩家的眼睛，
        /// 让门洞始终正对玩家。
        /// </summary>
        public void UpdateMasks()
        {
            if (maskTop == null || maskBottom == null) return;

            CachePlayer();

            maskTop.localPosition = new Vector3(0f, maskTopOffset, 0f);
            maskBottom.localPosition = new Vector3(0f, maskBottomOffset, 0f);

            if (player == null) return;

            // 眼睛位置 = 玩家中心 + 门对配置的眼睛高度。
            float eyeOffset = _pair != null ? _pair.playerEyeOffset : 0.4f;
            Vector3 eye = player.transform.position + Vector3.up * eyeOffset;
            FacePoint(maskTop, eye);
            FacePoint(maskBottom, eye);
        }

        static void FacePoint(Transform mask, Vector3 point)
        {
            Vector3 direction = point - mask.position;
            // 先对准眼睛，再旋转 90 度。
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            mask.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// 只保留捕获画面的一半：用贴图的一半重建精灵，
        /// 并把它摆到完整画面中对应的位置。
        /// </summary>
        public void ApplyHalfCrop()
        {
            if (output == null) return;

            Sprite full = output.sprite;
            if (full == null || full == _halfSprite) return;

            float fullWidth = full.bounds.size.x;
            float pixelsPerUnit = full.pixelsPerUnit;

            // A 保留右半，B 保留左半。两半都在中线处留出一点缝隙：
            // 共用的像素列会被交叉显示的递归放大成一条明显的接缝。
            bool rightHalf = side == CaptureGroupSide.A;
            float halfPixels = full.rect.width * 0.5f;
            float gapPixels = Mathf.Clamp(centerGapPixels, 0f, halfPixels * 0.5f);

            var halfRect = new Rect(
                rightHalf ? full.rect.x + halfPixels + gapPixels : full.rect.x,
                full.rect.y,
                halfPixels - gapPixels,
                full.rect.height);

            if (_halfSprite != null) Destroy(_halfSprite);
            _halfSprite = Sprite.Create(full.texture, halfRect, new Vector2(0.5f, 0.5f), pixelsPerUnit);
            _halfSprite.name = "Snapshot Half";

            output.sprite = _halfSprite;

            // 把保留的区域摆到它在完整画面中的位置。
            float gapWorld = gapPixels / pixelsPerUnit;
            float offset = (fullWidth * 0.25f + gapWorld * 0.5f) * (rightHalf ? 1f : -1f);
            output.transform.localPosition = new Vector3(offset, 0f, 0f);
        }
    }
}
