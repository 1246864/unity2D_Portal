using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Marks a rectangular world-space area. The region capture tool copies every
    /// tile and sprite inside it into a new parent object.
    /// Add it to an empty GameObject and resize "size" in the Inspector.
    /// </summary>
    public class CaptureRegion : MonoBehaviour
    {
        [Tooltip("Capture area size in world units, centred on this transform.")]
        public Vector2 size = new Vector2(14f, 10f);

        /// <summary>World-space rectangle covered by this marker.</summary>
        public Bounds WorldBounds => new Bounds(
            transform.position,
            new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), 1f));

        void OnDrawGizmos()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(transform.position, new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), 0.01f));
        }
    }
}
