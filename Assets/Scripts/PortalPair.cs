using UnityEngine;

namespace Platformer
{
    /// <summary>
    /// 控制一对传送门。挂在包含 Portal A 与 Portal B 的 "Portal Pair" 父节点上。
    /// </summary>
    public class PortalPair : MonoBehaviour
    {
        [Tooltip("玩家对象，遮罩朝向和传送都以它为参照")]
        public Transform player;

        [Tooltip("眼睛相对玩家中心点的高度（世界单位）")]
        public float playerEyeOffset = 0.4f;

        /// <summary>玩家眼睛的世界坐标。</summary>
        public Vector3 PlayerEye => player != null
            ? player.position + Vector3.up * playerEyeOffset
            : Vector3.zero;

        void Awake()
        {
            PushPlayerToGroups();
        }

        void Start()
        {
            // 再下发一次：确保所有 CaptureDisplayGroup 的 Awake 都已执行完毕。
            PushPlayerToGroups();
        }

        /// <summary>把绑定的玩家实例下发给本对传送门下的每一个 CaptureDisplayGroup。</summary>
        public void PushPlayerToGroups()
        {
            CaptureDisplayGroup[] groups = GetComponentsInChildren<CaptureDisplayGroup>(true);
            foreach (CaptureDisplayGroup group in groups)
            {
                if (group == null) continue;
                group.SetPlayer(player != null ? player.gameObject : null);
            }
        }
    }
}
