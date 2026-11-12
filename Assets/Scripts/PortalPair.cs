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
    }
}
