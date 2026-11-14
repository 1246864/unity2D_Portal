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
    /// 一个捕获显示组：把快照相机、Portal View Center（视觉中心）、输出精灵和捕获控制器打包在同一个组节点下。
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

        [Tooltip("Portal View 的父节点（视觉中心）；留空时自动取 output 的父节点")]
        public Transform viewCenter;

        [Tooltip("本门显示精灵所在的游戏对象（Start 时自动获取）")]
        public GameObject viewObject;

        [Tooltip("左右两半在中线各让出的像素数。留一点缝可以避免半屏拼接把共用像素列放大成一条亮线")]
        [Range(0f, 16f)] public float centerGapPixels = 1f;

        [Header("另一扇门透明度")]
        [Tooltip("玩家离一扇门小于该距离时，另一扇门线性变透明；距离 0 时完全透明。单位与颜色距离公式一致")]
        [Min(0f)] public float otherViewFadeDistance = 3f;

        [Header("玩家")]
        [Tooltip("本对传送门绑定的玩家游戏对象，由 Portal Pair 下发，无需手填")]
        public GameObject player;

        [Header("传送门")]
        [Tooltip("门的朝向：定义出口在 Y 轴（竖直方向）的哪一侧；传送时据此决定出面对方向")]
        [SerializeField] PortalFacing facing = PortalFacing.Right;

        public PortalFacing Facing
        {
            get { return facing; }
            set
            {
                if (facing == value) return;
                facing = value;
                _lastFacing = value;
                _hasLastFacing = true;
                ChangeFacing(value);
            }
        }

        PortalFacing _lastFacing;
        bool _hasLastFacing;

        [Header("遮罩")]
        [Tooltip("上遮罩节点（自动查找 View Mask Top）")]
        public Transform maskTop;
        [Tooltip("下遮罩节点（自动查找 View Mask Bottom）")]
        public Transform maskBottom;

        [Header("开门动画")]
        [Tooltip("开门动画时长（秒）：上下遮罩偏移从 0 增长到 PortalPair 配置值")]
        [Min(0f)] public float openDuration = 0.6f;

        [Tooltip("开门曲线：0 = 关闭，1 = 完全打开")]
        public AnimationCurve openCurve = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        /// <summary>门洞高度，由 PortalPair 统一配置的上下遮罩偏移算出。</summary>
        public float PortalHeight => Pair != null ? Pair.PortalHeight : 3f;

        /// <summary>开门动画已播放次数（调试/测试用，含 OnEnable 的首次播放）。</summary>
        public int OpenAnimationCount { get; private set; }

        /// <summary>所属的 PortalPair（必要时向上查找）。</summary>
        public PortalPair Pair
        {
            get
            {
                if (_pair == null) _pair = GetComponentInParent<PortalPair>();
                return _pair;
            }
        }

        /// <summary>本门朝左时返回 true。</summary>
        public bool FacesLeft => facing == PortalFacing.Left;

        /// <summary>朝左为 -1，朝右为 +1。</summary>
        public float FacingSign => FacesLeft ? -1f : 1f;

        /// <summary>玩家的 Transform（未下发玩家时为 null）。</summary>
        public Transform PlayerTransform => player != null ? player.transform : null;

        /// <summary>门的视觉中心：优先用 Portal View Center，否则退回组根节点。</summary>
        Transform ViewAnchor => viewCenter != null
            ? viewCenter
            : (output != null && output.transform.parent != null ? output.transform.parent : transform);

        Sprite _halfSprite;
        PortalPair _pair;
        Transform _viewAnchor;
        Transform _partnerViewAnchor;

        Vector3 _viewBaseLocalPosition;
        bool _hasViewBase;

        float _openElapsed;
        float _openProgress = 1f;
        bool _isOpening;

        void Awake()
        {
            _lastFacing = facing;
            _hasLastFacing = true;

            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            ResolveMaskRig();
            CachePlayer();
            CacheViewAnchors();
        }

        void OnEnable()
        {
            // 首次激活、以及禁用后重新启用 GameObject/组件时都会触发，重播开门动画。
            PlayOpenAnimation();
        }

        void Start()
        {
            // 获取本门显示精灵所在的游戏对象，并记住它在视觉中心下的初始局部位置。
            // ApplyHalfCrop 只在此基础上叠加 X 偏移，不能把 Portal View Center 的 Z 偏移覆盖掉。
            if (output != null)
            {
                viewObject = output.gameObject;

                if (viewCenter == null)
                    viewCenter = output.transform.parent;

                _viewBaseLocalPosition = output.transform.localPosition;
                _hasViewBase = true;

                // 传送门相机排除 Portal View 所在层，避免把自己的输出拍进画面形成递归。
                if (capture != null && capture.captureCamera != null && output.gameObject.layer != 0)
                    capture.captureCamera.cullingMask &= ~(1 << output.gameObject.layer);
            }

            ChangeFacing(facing);
            CachePlayer();
            CacheViewAnchors();
        }

        /// <summary>
        /// Inspector 直接修改序列化字段 facing，不会执行 Facing 属性的 setter。
        /// 缓存上一次朝向，确保只有值实际变化时才调用 ChangeFacing；首次加载只记录初始值。
        /// </summary>
        void OnValidate()
        {
            if (!_hasLastFacing)
            {
                _lastFacing = facing;
                _hasLastFacing = true;
                return;
            }

            if (_lastFacing == facing) return;

            _lastFacing = facing;
            ChangeFacing(facing);
        }

        void ChangeFacing(PortalFacing newFacing)
        {
            Debug.Log("ChangeFacing: " + newFacing);
        }

        /// <summary>
        /// 播放一次开门动画：上下遮罩偏移在 openDuration 内从 0 增长到 PortalPair 配置的偏移。
        /// 可通过 openCurve 调整节奏；时长为 0 时直接完全打开。
        /// </summary>
        public void PlayOpenAnimation()
        {
            OpenAnimationCount++;
            _openElapsed = 0f;
            _isOpening = openDuration > 0f;
            _openProgress = _isOpening ? 0f : 1f;

            // 立刻把遮罩拉回关闭状态（偏移 0），
            // 这样重新启用时不会先看到上一帧的打开位置，动画一定从 0 开始。
            ApplyMaskOffsets(_openProgress);
        }

        void AdvanceOpenAnimation()
        {
            if (!_isOpening) return;

            _openElapsed += Time.deltaTime;

            float t = openDuration > 0f ? Mathf.Clamp01(_openElapsed / openDuration) : 1f;
            _openProgress = openCurve != null ? Mathf.Clamp01(openCurve.Evaluate(t)) : t;

            if (t >= 1f)
            {
                _isOpening = false;
                _openProgress = 1f;
            }
        }

        /// <summary>接收 PortalPair 下发的玩家实例。</summary>
        public void SetPlayer(GameObject playerObject)
        {
            player = playerObject;
        }

        /// <summary>玩家尚未下发时，从所属的 PortalPair 上取一次。</summary>
        void CachePlayer()
        {
            if (player != null) return;

            if (_pair == null) _pair = GetComponentInParent<PortalPair>();
            if (_pair != null && _pair.player != null)
                player = _pair.player.gameObject;
        }

        /// <summary>缓存本门与另一扇门的视觉中心，避免每帧重复解析层级。</summary>
        void CacheViewAnchors()
        {
            _viewAnchor = ViewAnchor;
            _partnerViewAnchor = partner != null ? partner.ViewAnchor : null;
        }

        void LateUpdate()
        {
            AdvanceOpenAnimation();
            ApplyHalfCrop();
            UpdateMasks();
        }

        void Update()
        {
            CachePlayer();
            UpdateViewColor();
            UpdateVisibility();
        }

        /// <summary>
        /// 用 Y 轴正方向按 Facing 旋转 90° 得到开口方向（朝右 -90°，朝左 +90°）。
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
            Vector3 anchor = _viewAnchor != null ? _viewAnchor.position : transform.position;
            Vector3 toPlayer = player.transform.position - anchor;

            // 点积 <= 0 等价于夹角 >= 90°，此时玩家在门的背面。
            output.enabled = Vector3.Dot(openingDirection, toPlayer) > 0f;
        }

        /// <summary>
        /// 根据玩家与两扇门的距离更新画面颜色：
        /// 到本门的距离决定 RGB 冷暖，到另一扇门的距离决定 alpha（越近越透明）。
        /// </summary>
        void UpdateViewColor()
        {
            if (player == null || output == null) return;

            Vector3 playerPosition = player.transform.position;

            // 玩家到本门视觉中心局部 Y 轴的垂直距离。
            Transform ownAnchor = _viewAnchor != null ? _viewAnchor : transform;
            float playerDistanceToYAxis = Mathf.Abs(ownAnchor.InverseTransformPoint(playerPosition).x);

            float color_offs = 0.5f;
            if (playerDistanceToYAxis <= 5f)
            {
                color_offs = playerDistanceToYAxis / 10f;
            }

            // 另一扇门：玩家越靠近它，本门画面越透明；距离达到阈值时恢复 1。
            float alpha = 1f;
            if (_partnerViewAnchor != null && otherViewFadeDistance > 0f)
            {
                float distanceToPartner = Mathf.Abs(_partnerViewAnchor.InverseTransformPoint(playerPosition).x);
                alpha = Mathf.Clamp01(distanceToPartner / otherViewFadeDistance);
            }

            output.color = side == CaptureGroupSide.B
                ? new Color(1f, 1f - color_offs, 1f - color_offs, alpha)
                : new Color(1f - color_offs, 1f - color_offs, 1f, alpha);
        }

        /// <summary>门的碰撞转发给 PortalPair，由它统一处理传送。</summary>
        void OnCollisionEnter2D(Collision2D collision)
        {
            ForwardGateCollision(collision);
        }

        /// <summary>冷却结束时如果仍贴着门，也要能触发。</summary>
        void OnCollisionStay2D(Collision2D collision)
        {
            ForwardGateCollision(collision);
        }

        void ForwardGateCollision(Collision2D collision)
        {
            if (_pair == null) _pair = GetComponentInParent<PortalPair>();
            if (_pair != null) _pair.OnGateHit(this, collision);
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
            ApplyMaskOffsets(_openProgress);

            if (player == null) return;

            // 眼睛位置 = 玩家中心 + 门对配置的眼睛高度。
            PortalPair pair = Pair;
            float eyeOffset = pair != null ? pair.playerEyeOffset : 0.4f;
            Vector3 eye = player.transform.position + Vector3.up * eyeOffset;
            FacePoint(maskTop, eye);
            FacePoint(maskBottom, eye);
        }

        /// <summary>按 0..1 的开门比例设置上下遮罩的 Y 偏移（0 = 关闭，1 = 完全打开）。</summary>
        void ApplyMaskOffsets(float progress)
        {
            if (maskTop == null || maskBottom == null) return;

            PortalPair pair = Pair;
            float open = Mathf.Clamp01(progress);
            float topOffset = (pair != null ? pair.maskTopOffset : 1.5f) * open;
            float bottomOffset = (pair != null ? pair.maskBottomOffset : -1.5f) * open;

            maskTop.localPosition = new Vector3(0f, topOffset, 0f);
            maskBottom.localPosition = new Vector3(0f, bottomOffset, 0f);
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

            if (!_hasViewBase)
            {
                _viewBaseLocalPosition = output.transform.localPosition;
                _hasViewBase = true;
            }

            // 在 Portal View 的初始局部位置（可能带有 Portal View Center 的 Z 偏移）上叠加半屏 X 偏移。
            output.transform.localPosition = _viewBaseLocalPosition + new Vector3(offset, 0f, 0f);
        }
    }
}
