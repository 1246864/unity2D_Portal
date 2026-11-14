using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Platformer.Tests
{
    /// <summary>
    /// 传送门的 PlayMode 测试：薄碰撞箱尺寸、碰撞传送、背面隐藏。
    /// </summary>
    public class PortalTeleportTests
    {
        const string LevelScene = "Level01";

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);
            yield return new WaitForSeconds(0.5f);
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene scene = SceneManager.GetSceneByName(LevelScene);
            if (scene.IsValid() && scene.isLoaded)
                yield return SceneManager.UnloadSceneAsync(scene);
        }

        static GameObject FindPlayer()
        {
            foreach (var root in SceneManager.GetSceneByName(LevelScene).GetRootGameObjects())
            {
                if (root.name == "Player") return root;
            }
            return null;
        }

        static void FindGroups(out CaptureDisplayGroup a, out CaptureDisplayGroup b)
        {
            a = null;
            b = null;
            foreach (var group in Object.FindObjectsOfType<CaptureDisplayGroup>())
            {
                if (group.side == CaptureGroupSide.A) a = group;
                else b = group;
            }
        }

        [UnityTest]
        public IEnumerator Portal_Gates_Match_Portal_Height()
        {
            FindGroups(out var a, out var b);

            Assert.IsNotNull(a, "找不到 Portal A 的 CaptureDisplayGroup。");
            Assert.IsNotNull(b, "找不到 Portal B 的 CaptureDisplayGroup。");

            var gateA = a.GetComponent<BoxCollider2D>();
            var gateB = b.GetComponent<BoxCollider2D>();

            Assert.IsNotNull(gateA, "Portal A 上应该有运行时生成的薄碰撞箱。");
            Assert.IsNotNull(gateB, "Portal B 上应该有运行时生成的薄碰撞箱。");
            Assert.IsFalse(gateA.isTrigger, "碰撞箱应该是实体碰撞，不能用 Trigger。");
            Assert.Less(gateA.size.x, 0.2f, "碰撞箱应该很薄。");

            var pair = Object.FindObjectOfType<PortalPair>();
            Assert.IsNotNull(pair, "场景缺少 PortalPair。");
            Assert.AreEqual(pair.PortalHeight, a.PortalHeight, 0.001f, "A 的门洞高度应该由 PortalPair 决定。");
            Assert.AreEqual(pair.PortalHeight, b.PortalHeight, 0.001f, "B 的门洞高度应该由 PortalPair 决定。");
            Assert.AreEqual(pair.PortalHeight, gateA.size.y, 0.001f, "A 的碰撞箱高度应等于门洞高度。");
            Assert.AreEqual(pair.PortalHeight, gateB.size.y, 0.001f, "B 的碰撞箱高度应等于门洞高度。");

            // 改 PortalPair 上的一对偏移，两扇门的门洞高度与碰撞箱应一起变化。
            pair.maskTopOffset = 2f;
            pair.maskBottomOffset = -2f;
            yield return null;
            yield return null;

            Assert.AreEqual(4f, a.PortalHeight, 0.001f, "改偏移后 A 的高度应变为 4。");
            Assert.AreEqual(4f, b.PortalHeight, 0.001f, "改偏移后 B 的高度应变为 4。");
            Assert.AreEqual(4f, gateA.size.y, 0.001f, "A 的碰撞箱高度应更新为 4。");
            Assert.AreEqual(4f, gateB.size.y, 0.001f, "B 的碰撞箱高度应更新为 4。");
        }

        [UnityTest]
        public IEnumerator Portal_Opens_From_Zero_To_Pair_Height()
        {
            FindGroups(out var a, out _);
            var pair = Object.FindObjectOfType<PortalPair>();

            Assert.IsNotNull(a, "找不到 Portal A。");
            Assert.IsNotNull(pair, "场景缺少 PortalPair。");
            Assert.IsNotNull(a.maskTop, "Portal A 缺少上遮罩节点。");
            Assert.IsNotNull(a.maskBottom, "Portal A 缺少下遮罩节点。");

            a.PlayOpenAnimation();
            yield return null;
            yield return null;

            Assert.Less(a.maskTop.localPosition.y, pair.maskTopOffset * 0.5f,
                "开门动画刚开始时，上遮罩应该仍接近中线。");

            yield return new WaitForSeconds(a.openDuration + 0.2f);

            Assert.AreEqual(pair.maskTopOffset, a.maskTop.localPosition.y, 0.01f,
                "动画结束后上偏移应等于 PortalPair 配置值。");
            Assert.AreEqual(pair.maskBottomOffset, a.maskBottom.localPosition.y, 0.01f,
                "动画结束后下偏移应等于 PortalPair 配置值。");

            // 禁用再启用 GameObject 应该重新播放开门动画。
            a.gameObject.SetActive(false);
            yield return null;
            a.gameObject.SetActive(true);
            yield return null;
            yield return null;

            Assert.Less(a.maskTop.localPosition.y, pair.maskTopOffset * 0.5f,
                "重新启用传送门后应重新播放开门动画。");
        }

        [UnityTest]
        public IEnumerator Player_Still_Teleports_After_Portal_Reenable()
        {
            FindGroups(out var a, out var b);
            var player = FindPlayer();

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(player);

            var controller = player.GetComponent<PlayerController2D>();
            Assert.IsNotNull(controller);

            // 先禁用再启用 A 的 GameObject，复现“重新开门后碰撞箱被当成墙”。
            a.gameObject.SetActive(false);
            yield return null;
            a.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Vector3 start = new Vector3(
                a.transform.position.x + a.FacingSign * 1.2f,
                0.75f,
                0f);
            controller.Teleport(start);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            controller.SetMoveInput(-a.FacingSign);

            bool teleported = false;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                float toPartner = (player.transform.position.x - b.transform.position.x) * b.FacingSign;
                if (toPartner > 0f && toPartner < 1.5f)
                {
                    teleported = true;
                    break;
                }
            }

            controller.SetMoveInput(0f);

            Assert.IsTrue(teleported,
                "重新启用传送门后，玩家应能穿门传送，而不是被薄碰撞箱当成墙挡住。");
        }

        [UnityTest]
        public IEnumerator Player_Teleports_Through_Portal()
        {
            FindGroups(out var a, out var b);
            var player = FindPlayer();

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(player);

            var controller = player.GetComponent<PlayerController2D>();
            var pair = Object.FindObjectOfType<PortalPair>();

            Assert.IsNotNull(controller, "玩家缺少 PlayerController2D。");
            Assert.IsNotNull(pair, "场景缺少 PortalPair。");

            // 从 A 的正面（facing 一侧）往门里走。
            Vector3 start = new Vector3(
                a.transform.position.x + a.FacingSign * 1.2f,
                0.75f,
                0f);
            controller.Teleport(start);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            controller.SetMoveInput(-a.FacingSign);

            bool teleported = false;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                float toPartner = (player.transform.position.x - b.transform.position.x) * b.FacingSign;
                if (toPartner > 0f && toPartner < 1.5f)
                {
                    teleported = true;
                    break;
                }
            }

            controller.SetMoveInput(0f);

            Assert.IsTrue(teleported, "玩家走进门后应该被传送到另一扇门的正面。");
            Assert.Less(player.transform.position.x, a.transform.position.x - 5f,
                "传送后玩家应该已经离开 A 的位置。");

            float distance = (player.transform.position.x - b.transform.position.x) * b.FacingSign;
            float safe = pair.gateThickness * 0.5f + controller.centerPointRadius + pair.exitMargin;
            Assert.GreaterOrEqual(distance, safe - 0.02f,
                "传送后玩家中心不应该停在对方门的碰撞箱里。");
        }

        [UnityTest]
        public IEnumerator Portal_View_Hides_When_Player_Is_Behind()
        {
            FindGroups(out var a, out _);
            var player = FindPlayer();

            Assert.IsNotNull(a);
            Assert.IsNotNull(player);
            Assert.IsNotNull(a.output, "Portal A 缺少输出精灵。");

            var controller = player.GetComponent<PlayerController2D>();

            // 开口方向的反侧 = 门背面
            Vector3 behind = a.transform.position - new Vector3(a.FacingSign * 2f, 0f, 0f);
            controller.Teleport(behind);
            yield return null;
            yield return null;
            Assert.IsFalse(a.output.enabled, "玩家在门背面时应该隐藏该门的画面。");

            // 回到开口方向一侧
            Vector3 front = a.transform.position + new Vector3(a.FacingSign * 2f, 0f, 0f);
            controller.Teleport(front);
            yield return null;
            yield return null;
            Assert.IsTrue(a.output.enabled, "玩家回到门正面时应该恢复显示。");
        }
    }
}
