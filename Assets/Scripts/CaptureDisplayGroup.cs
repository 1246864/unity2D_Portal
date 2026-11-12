using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// One capture display group: a snapshot camera, an output sprite and the capture
    /// controller, packed under a single empty object.
    ///
    /// Two groups are cross-connected: this group's camera paints the other group's
    /// output, and the other group's camera paints this group's output.
    /// </summary>
    public class CaptureDisplayGroup : MonoBehaviour
    {
        [Tooltip("The other group. The two groups must reference each other.")]
        public CaptureDisplayGroup otherGroup;

        [Header("Internal")]
        public RegionSnapshotCamera capture;
        public SpriteRenderer output;

        void Awake()
        {
            WireUp();
        }

        /// <summary>Connects both groups so each camera feeds the other group's output.</summary>
        public void WireUp()
        {
            if (capture == null || output == null) return;
            if (otherGroup == null || otherGroup.capture == null || otherGroup.output == null) return;

            capture.output = otherGroup.output;
            otherGroup.capture.output = output;
        }
    }
}
