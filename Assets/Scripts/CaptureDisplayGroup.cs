using UnityEngine;

namespace Platformer
{
    /// <summary>Which half of the capture pair this group is.</summary>
    public enum CaptureGroupSide
    {
        [InspectorName("A 门")] A,
        [InspectorName("B 门")] B
    }

    /// <summary>Direction the portal faces: left or right.</summary>
    public enum PortalFacing
    {
        [InspectorName("朝左")] Left,
        [InspectorName("朝右")] Right
    }

    /// <summary>
    /// One capture display group: a snapshot camera, an output sprite and the capture
    /// controller packed under a single empty object.
    ///
    /// Groups live side by side under one parent. A group finds the group of the other
    /// side on its own and cross-connects them: this group's camera paints the other
    /// group's output and the other group's camera paints this group's output.
    ///
    /// Two view masks clip the output to the portal opening and always turn toward the
    /// player's eyes.
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

        [Tooltip("左右两半在中线各让出的像素数。留一点缝可以避免递归把共用像素列放大成一条亮线")]
        [Range(0f, 16f)] public float centerGapPixels = 1f;

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

        /// <summary>Portal opening height, derived from the two mask offsets.</summary>
        public float PortalHeight => Mathf.Abs(maskTopOffset - maskBottomOffset);

        /// <summary>True when this portal opens toward the left.</summary>
        public bool FacesLeft => facing == PortalFacing.Left;

        /// <summary>-1 when facing left, +1 when facing right.</summary>
        public float FacingSign => FacesLeft ? -1f : 1f;

        Sprite _halfSprite;
        PortalPair _pair;

        void Awake()
        {
            ResolvePartner();
            WireUp();

            _pair = GetComponentInParent<PortalPair>();
            ResolveMaskRig();
        }

        void LateUpdate()
        {
            ApplyHalfCrop();
            UpdateMasks();
        }

        /// <summary>Finds the sibling group with the other side and connects both.</summary>
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

        /// <summary>Connects both groups so each camera feeds the other group's output.</summary>
        public void WireUp()
        {
            if (capture == null || output == null) return;
            if (partner == null || partner.capture == null || partner.output == null) return;

            capture.output = partner.output;
            partner.capture.output = output;
        }

        /// <summary>Finds the mask rig created under this group.</summary>
        public void ResolveMaskRig()
        {
            if (maskTop != null && maskBottom != null) return;

            Transform maskRoot = transform.Find("Portal Mask");
            if (maskRoot == null) maskRoot = transform;
            if (maskTop == null) maskTop = maskRoot.Find("View Mask Top");
            if (maskBottom == null) maskBottom = maskRoot.Find("View Mask Bottom");
        }

        /// <summary>
        /// Puts the two masks on their offsets and turns them toward the player's eyes, so
        /// the opening always faces the player.
        /// </summary>
        public void UpdateMasks()
        {
            if (maskTop == null || maskBottom == null) return;

            if (_pair == null) _pair = GetComponentInParent<PortalPair>();

            maskTop.localPosition = new Vector3(0f, maskTopOffset, 0f);
            maskBottom.localPosition = new Vector3(0f, maskBottomOffset, 0f);

            if (_pair == null || _pair.player == null) return;

            Vector3 eye = _pair.PlayerEye;
            FacePoint(maskTop, eye);
            FacePoint(maskBottom, eye);
        }

        static void FacePoint(Transform mask, Vector3 point)
        {
            Vector3 direction = point - mask.position;
            // Aim at the eye, then turn another 90 degrees.
            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            mask.rotation = Quaternion.Euler(0f, 0f, angle);
        }

        /// <summary>
        /// Keeps only one half of the captured image: the sprite is rebuilt from that half
        /// of the texture and moved to where it sits in the full image.
        /// </summary>
        public void ApplyHalfCrop()
        {
            if (output == null) return;

            Sprite full = output.sprite;
            if (full == null || full == _halfSprite) return;

            float fullWidth = full.bounds.size.x;
            float pixelsPerUnit = full.pixelsPerUnit;

            // A keeps the right half, B keeps the left half. Both stop just short of the
            // centre line: a shared pixel column would be amplified by the cross-display
            // recursion into a visible seam line.
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

            // Centre the kept region where it sits in the full image.
            float gapWorld = gapPixels / pixelsPerUnit;
            float offset = (fullWidth * 0.25f + gapWorld * 0.5f) * (rightHalf ? 1f : -1f);
            output.transform.localPosition = new Vector3(offset, 0f, 0f);
        }
    }
}
