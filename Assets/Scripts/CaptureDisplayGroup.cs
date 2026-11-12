using UnityEngine;

namespace Platformer
{
    /// <summary>Which half of the capture pair this group is.</summary>
    public enum CaptureGroupSide
    {
        A,
        B
    }

    /// <summary>
    /// One capture display group: a snapshot camera, an output sprite and the capture
    /// controller packed under a single empty object.
    ///
    /// Groups live side by side under one parent. A group finds the group of the other
    /// side on its own and cross-connects them: this group's camera paints the other
    /// group's output and the other group's camera paints this group's output.
    ///
    /// The output is split at its centre line: only the right half of the captured image
    /// is shown, the left half is cut away.
    /// </summary>
    public class CaptureDisplayGroup : MonoBehaviour
    {
        [Tooltip("A or B. The group automatically pairs with the group of the other side.")]
        public CaptureGroupSide side = CaptureGroupSide.A;

        [Tooltip("Resolved automatically from the sibling groups.")]
        public CaptureDisplayGroup partner;

        [Header("Internal")]
        public RegionSnapshotCamera capture;
        public SpriteRenderer output;

        Sprite _halfSprite;

        void Awake()
        {
            ResolvePartner();
            WireUp();
        }

        void LateUpdate()
        {
            ApplyHalfCrop();
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

        /// <summary>
        /// Keeps only the right half of the captured image: the sprite is rebuilt from the
        /// right half of the texture and moved to where that half sits.
        /// </summary>
        public void ApplyHalfCrop()
        {
            if (output == null) return;

            Sprite full = output.sprite;
            if (full == null || full == _halfSprite) return;

            float fullWidth = full.bounds.size.x;

            var halfRect = new Rect(
                full.rect.x + full.rect.width * 0.5f,
                full.rect.y,
                full.rect.width * 0.5f,
                full.rect.height);

            if (_halfSprite != null) Destroy(_halfSprite);
            _halfSprite = Sprite.Create(full.texture, halfRect, new Vector2(0.5f, 0.5f), full.pixelsPerUnit);
            _halfSprite.name = "Snapshot Half";

            output.sprite = _halfSprite;
            // The right half sits a quarter of the full width to the right of the centre.
            output.transform.localPosition = new Vector3(fullWidth * 0.25f, 0f, 0f);
        }
    }
}
