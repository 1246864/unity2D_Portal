using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// Controls one pair of portals. Lives on the "Portal Pair" parent that contains
    /// Portal A and Portal B.
    /// </summary>
    public class PortalPair : MonoBehaviour
    {
        [Tooltip("玩家对象，遮罩朝向和传送都以它为参照")]
        public Transform player;

        [Tooltip("眼睛相对玩家中心点的高度（世界单位）")]
        public float playerEyeOffset = 0.4f;

        /// <summary>World position of the player's eyes.</summary>
        public Vector3 PlayerEye => player != null
            ? player.position + Vector3.up * playerEyeOffset
            : Vector3.zero;
    }
}
