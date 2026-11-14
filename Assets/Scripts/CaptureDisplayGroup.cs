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
        [Tooltip("门的朝向：朝左或朝右。传送玩家时两种朝向的行为不同")]
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

        void Awake()
        {
            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            ResolveMaskRig();
            CachePlayer();
        }

        void Start()
        {
            // 获取本门显示精灵所在的游戏对象。
            if (output != null)
                viewObject = output.gameObject;

            CachePlayer();
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
            ApplyHalfCrop();
            UpdateMasks();
        }

        void Update()
        {
            CachePlayer();
            UpdateViewColor();
        }

        /// <summary>根据玩家距离更新本门显示精灵的颜色。</summary>
        void UpdateViewColor()
        {
            if (player == null) return;

            // 玩家到本对象局部 Y 轴的垂直距离。
            float playerDistanceToYAxis = Mathf.Abs(transform.InverseTransformPoint(player.transform.position).x);

            float color_offs = 1f;
            if(playerDistanceToYAxis <= 10f)
            {
                color_offs = playerDistanceToYAxis / 10f;
            }
            if (facing == PortalFacing.Left) {
                viewObject.GetComponent<SpriteRenderer>().color = new Color(1f, 1f - color_offs, 1f - color_offs, 1f);
            }
            else
            {
                viewObject.GetComponent<SpriteRenderer>().color = new Color(1f - color_offs, 1f - color_offs, 1f, 1f);
            }
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
