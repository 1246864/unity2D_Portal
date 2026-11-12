using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Controls one pair of portals. Lives on the "Portal Pair" parent that contains
    /// Portal A and Portal B.
    /// </summary>
    public class PortalPair : MonoBehaviour
    {
        [Tooltip("Player the portal view masking works with.")]
        public Transform player;

        [Tooltip("Eye height above the player's centre, in world units.")]
        public float playerEyeOffset = 0.4f;

        /// <summary>World position of the player's eyes.</summary>
        public Vector3 PlayerEye => player != null
            ? player.position + Vector3.up * playerEyeOffset
            : Vector3.zero;
    }
}
