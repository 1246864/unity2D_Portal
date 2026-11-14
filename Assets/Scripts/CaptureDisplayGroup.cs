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

        [Header("传送门")]
        [Tooltip("门的朝向：定义出口在 Y 轴（竖直方向）的哪一侧；传送时据此决定出面对方向")]
        public PortalFacing facing = PortalFacing.Right;

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

        Sprite _halfSprite;
        PortalPair _pair;

        float _openElapsed;
        float _openProgress = 1f;
        bool _isOpening;

        void Awake()
        {
            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            ResolveMaskRig();
            CachePlayer();
        }

        void OnEnable()
        {
            // 首次激活、以及禁用后重新启用 GameObject/组件时都会触发，重播开门动画。
            PlayOpenAnimation();
        }

        void Start()
        {
            // 获取本门显示精灵所在的游戏对象。
            if (output != null)
                viewObject = output.gameObject;

            CachePlayer();
        }

        /// <summary>
        /// 播放一次开门动画：上下遮罩偏移在 openDuration 内从 0 增长到 PortalPair 配置的偏移。
        /// 可通过 openCurve 调整节奏；时长为 0 时直接完全打开。
        /// </summary>
        public void PlayOpenAnimation()
        {
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
            output.transform.localPosition = new Vector3(offset, 0f, 0f);
        }
    }
}
