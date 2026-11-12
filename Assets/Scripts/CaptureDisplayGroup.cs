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

        [Tooltip("Display scale of the output sprite. Below 1 the camera can see the world " +
                 "and the other group's image around it, which makes the recursion visible.")]
        [Range(0.05f, 1f)] public float displayScale = 0.5f;

        // DEBUG: coloured frame around the output so its position is obvious.
        // A = red, B = blue. Remove this block before shipping.
        [Header("Debug")]
        public bool debugOutline = true;
        LineRenderer _debugOutline;

        void Awake()
        {
            ResolvePartner();
            WireUp();
            ApplyDisplayScale();
            RefreshDebugOutline();
        }

        void LateUpdate()
        {
            RefreshDebugOutline();
        }

        /// <summary>DEBUG: rectangular frame around the output sprite.</summary>
        public void RefreshDebugOutline()
        {
            if (!debugOutline || output == null || output.sprite == null)
            {
                if (_debugOutline != null) _debugOutline.enabled = false;
                return;
            }

            if (_debugOutline == null)
            {
                var go = new GameObject("Debug Outline");
                go.transform.SetParent(transform, false);
                _debugOutline = go.AddComponent<LineRenderer>();
                _debugOutline.useWorldSpace = false;
                _debugOutline.loop = true;
                _debugOutline.positionCount = 4;
                _debugOutline.startWidth = 0.1f;
                _debugOutline.endWidth = 0.1f;
                _debugOutline.material = new Material(Shader.Find("Sprites/Default"));
                _debugOutline.sortingOrder = 60;
            }

            Color color = side == CaptureGroupSide.A ? Color.red : Color.blue;
            _debugOutline.enabled = true;
            _debugOutline.startColor = color;
            _debugOutline.endColor = color;

            Vector2 half = output.sprite.bounds.extents * output.transform.lossyScale.x;
            _debugOutline.SetPosition(0, new Vector3(-half.x, -half.y, 0f));
            _debugOutline.SetPosition(1, new Vector3(half.x, -half.y, 0f));
            _debugOutline.SetPosition(2, new Vector3(half.x, half.y, 0f));
            _debugOutline.SetPosition(3, new Vector3(-half.x, half.y, 0f));
        }

        /// <summary>Scales the output sprite down so both the world and the other group show.</summary>
        public void ApplyDisplayScale()
        {
            if (output != null) output.transform.localScale = Vector3.one * displayScale;
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
    }
}
