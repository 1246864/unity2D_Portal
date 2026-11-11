using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Base class for every runtime behaviour in the template.
    ///
    /// A behaviour flagged as a copy does nothing but render: its logic is disabled
    /// and any rigidbody/collider is frozen so the copy can never affect gameplay.
    /// </summary>
    public class PlatformerBehaviour : MonoBehaviour
    {
        [SerializeField, Tooltip("Set on objects created by region capture: they only render.")]
        bool isCopy;

        /// <summary>True when this object is a display-only copy.</summary>
        public bool IsCopy => isCopy;

        /// <summary>Stops all logic on this object and freezes its physics for display only.</summary>
        public virtual void MarkAsCopy()
        {
            isCopy = true;
            enabled = false;

            var body = GetComponent<Rigidbody2D>();
            if (body != null)
            {
                body.velocity = Vector2.zero;
                body.angularVelocity = 0f;
                body.bodyType = RigidbodyType2D.Static;
            }

            var collider = GetComponent<Collider2D>();
            if (collider != null)
            {
                collider.enabled = false;
            }
        }
    }
}
