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

        static void PrepareTransparencySandbox(PortalPair pair, CaptureDisplayGroup a, CaptureDisplayGroup b)
        {
            pair.carveTerrain = false;
            a.GetComponent<BoxCollider2D>().enabled = false;
            b.GetComponent<BoxCollider2D>().enabled = false;

            Rigidbody2D rb = pair.player.GetComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 0f;
            rb.velocity = Vector2.zero;
        }

        static void PlacePlayerEye(PortalPair pair, Vector3 eye)
        {
            var controller = pair.player.GetComponent<PlayerController2D>();
            controller.Teleport((Vector2)(eye - Vector3.up * pair.playerEyeOffset));
        }

        static void SetFacingTransparencyLayout(CaptureDisplayGroup a, CaptureDisplayGroup b)
        {
            a.transform.position = new Vector3(-1f, 0f, 0f);
            b.transform.position = new Vector3(1f, 0f, 0f);
            a.transform.rotation = b.transform.rotation = Quaternion.identity;
            a.Facing = PortalFacing.Right;
            b.Facing = PortalFacing.Left;
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
            // 场景当前是 ±2（高度 4），这里改成 ±3（高度 6），避免用相同值做无效断言。
            pair.maskTopOffset = 3f;
            pair.maskBottomOffset = -3f;
            yield return null;
            yield return null;

            Assert.AreEqual(6f, a.PortalHeight, 0.001f, "改偏移后 A 的高度应变为 6。");
            Assert.AreEqual(6f, b.PortalHeight, 0.001f, "改偏移后 B 的高度应变为 6。");
            Assert.AreEqual(6f, gateA.size.y, 0.001f, "A 的碰撞箱高度应更新为 6。");
            Assert.AreEqual(6f, gateB.size.y, 0.001f, "B 的碰撞箱高度应更新为 6。");
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

            // 先禁用再启用 B 的 GameObject，复现“重新开门后碰撞箱被当成墙”。
            b.gameObject.SetActive(false);
            yield return null;
            b.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // 用 B 门做入口：B 在场景中位置较高，起点贴近门一些，
            // 避免玩家在下落过程中错过门洞。
            Vector3 start = (Vector3)b.transform.position
                            + (Vector3)b.OpeningDirection * 0.45f;
            controller.Teleport(start);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            controller.SetMoveInput(-b.FacingSign);

            bool teleported = false;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                // 出口是 A；两门存在相对旋转和高度差，不能用单一 X 判断，改用距离。
                if (Vector2.Distance(player.transform.position, a.transform.position) < 2f)
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

            // 放慢回正，便于断言出口门 Y+ 对齐。
            controller.portalRealignSpeed = 5f;

            int aOpenCount = a.OpenAnimationCount;
            int bOpenCount = b.OpenAnimationCount;

            // 用 B 门做入口：B 在当前场景中位置较高，起点贴近门以减少下落，
            // 出口是 A。
            Vector3 start = (Vector3)b.transform.position
                            + (Vector3)b.OpeningDirection * 0.45f;
            controller.Teleport(start);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            controller.SetMoveInput(-b.FacingSign);

            bool teleported = false;
            float elapsed = 0f;
            while (elapsed < 3f)
            {
                yield return new WaitForFixedUpdate();
                elapsed += Time.fixedDeltaTime;

                // 出口 A 被旋转/抬高，用距离判断是否已经到对面门。
                if (Vector2.Distance(player.transform.position, a.transform.position) < 2f)
                {
                    teleported = true;
                    break;
                }
            }

            controller.SetMoveInput(0f);
            if (teleported)
            {
                Assert.AreEqual(1f, a.output.color.a, 0.05f,
                    "B → A 传送完成后，出口 A 必须当帧恢复不透明，不能等待下一次 LateUpdate。");
            }
            yield return null; // wait for CameraFollow2D LateUpdate

            Assert.IsTrue(teleported, "玩家走进门后应该被传送到另一扇门的正面。");

            // 出口是 A、入口是 B：出口旋转 = A 角度 - B 角度。
            // 当前场景 B 本身也旋转了 45°，所以不能假设 B 是水平门。
            float expectedAngle = Mathf.DeltaAngle(
                0f, a.transform.eulerAngles.z - b.transform.eulerAngles.z);
            Vector3 expectedUp = Quaternion.Euler(0f, 0f, expectedAngle) * Vector3.up;

            Transform visual = controller.VisualRoot != null
                ? controller.VisualRoot
                : player.transform;
            Assert.Less(Vector2.Distance(
                    (Vector2)visual.up, (Vector2)expectedUp), 0.05f,
                "出传送门后，玩家显示部分的 Y+ 应与出口旋转一致。");
            Assert.AreEqual(expectedAngle, controller.CurrentRotationAngle, 5f,
                "玩家角度应等于出口门角度减入口门角度。");

            if (Camera.main != null)
            {
                Assert.AreEqual(controller.CurrentRotationAngle,
                    Mathf.DeltaAngle(0f, Camera.main.transform.eulerAngles.z), 3f,
                    "相机旋转角度应等于玩家旋转角度。");
            }

            Assert.AreEqual(bOpenCount + 1, b.OpenAnimationCount,
                "从 B 门进入应让 B 自己重播一次开门动画。");
            Assert.AreEqual(aOpenCount, a.OpenAnimationCount,
                "从 B 门进入不应触发 A 的开门动画。");
            Assert.Greater(Vector2.Distance(player.transform.position, b.transform.position), 5f,
                "传送后玩家应该已经离开 B 的位置。");

            float distance = Vector2.Dot(
                (Vector2)player.transform.position - (Vector2)a.transform.position,
                a.OpeningDirection);
            float safe = pair.gateThickness * 0.5f + controller.centerPointRadius + pair.exitMargin;
            Assert.GreaterOrEqual(distance, safe - 0.02f,
                "传送后玩家中心不应该停在对方门的碰撞箱里。");
        }

        [UnityTest]
        public IEnumerator Other_Portal_Fades_When_Player_Is_Near()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();
            PrepareTransparencySandbox(pair, a, b);

            a.transform.position = Vector3.zero;
            b.transform.position = new Vector3(10f, 0f, 0f);
            a.transform.rotation = b.transform.rotation = Quaternion.identity;
            a.Facing = b.Facing = PortalFacing.Right;

            float fade = a.otherViewFadeDistance;
            Assert.Greater(fade, 0f, "淡出距离应大于 0。");

            // 玩家眼睛位于 A 开口正面的一半衰减距离，B 应保持半透明。
            PlacePlayerEye(pair, a.ViewAnchor.position +
                               (Vector3)a.OpeningDirection * (fade * 0.5f));
            yield return null;

            Assert.AreEqual(0.5f, b.output.color.a, 0.05f,
                "靠近 A 到一半距离时，B 的画面应为半透明。");
            Assert.AreEqual(1f, a.output.color.a, 0.05f,
                "A 是接近基准门，必须保持不透明。");

            PlacePlayerEye(pair, a.ViewAnchor.position +
                               (Vector3)a.OpeningDirection * (fade * 2f));
            yield return null;

            Assert.AreEqual(1f, b.output.color.a, 0.05f,
                "超过淡出距离后，B 的画面应恢复不透明。");
            Assert.AreEqual(1f, a.output.color.a, 0.05f);
        }

        [UnityTest]
        public IEnumerator Transparency_Adapts_When_Both_Portals_Are_Inside_Fade_Range()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();
            PrepareTransparencySandbox(pair, a, b);
            SetFacingTransparencyLayout(a, b);

            // 使用不同上限，验证自适应交换不依赖两门配置完全一致。
            a.otherViewFadeDistance = 2f;
            b.otherViewFadeDistance = 2.8f;

            // A 评分 0.5、B 评分 1.5；有效阈值缩小到 1.5，而不是使用上限 2。
            PlacePlayerEye(pair, new Vector3(-0.5f, 0f, 0f));
            yield return null;
            Assert.AreEqual(1f, a.output.color.a, 0.01f);
            Assert.AreEqual(1f / 3f, b.output.color.a, 0.03f,
                "双门都在范围内时，应使用搭档评分作为实际衰减阈值。");

            // 等分点两门评分都是 1，交换前 target 已恢复为 1。
            PlacePlayerEye(pair, Vector3.zero);
            yield return null;
            Assert.AreEqual(1f, a.output.color.a, 0.01f,
                "自动交换中线上 A 必须完全不透明。");
            Assert.AreEqual(1f, b.output.color.a, 0.01f,
                "自动交换中线上 B 必须完全不透明，不能从半透明跳变。");

            // 越过中线后 B 成为 source，A 从 1 平滑进入新的距离曲线。
            PlacePlayerEye(pair, new Vector3(0.5f, 0f, 0f));
            yield return null;
            Assert.AreEqual(1f / 3f, a.output.color.a, 0.03f);
            Assert.AreEqual(1f, b.output.color.a, 0.01f);
        }

        [UnityTest]
        public IEnumerator Selected_Portal_Overrides_Automatic_Transparency()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();
            PrepareTransparencySandbox(pair, a, b);
            SetFacingTransparencyLayout(a, b);
            a.otherViewFadeDistance = b.otherViewFadeDistance = 3f;

            PlacePlayerEye(pair, new Vector3(-0.75f, 0f, 0f));
            yield return null;
            Assert.Less(b.output.color.a, 1f, "自动模式下接近 A 应让 B 透明。");

            // 强制改以 B 为基准；当前 B 比 A 远，因此两门都应不透明。
            pair.SelectPortal(b);
            Assert.AreSame(b, pair.SelectedPortal);
            Assert.AreEqual(1f, a.output.color.a, 0.01f);
            Assert.AreEqual(1f, b.output.color.a, 0.01f,
                "选中覆盖不能让自动 source 继续决定透明目标。");

            // 玩家靠近被选中的 B 后，A 按 B 的接近评分透明。
            PlacePlayerEye(pair, new Vector3(0.75f, 0f, 0f));
            yield return null;
            Assert.Less(a.output.color.a, 0.2f);
            Assert.AreEqual(1f, b.output.color.a, 0.01f);

            pair.ClearSelectedPortal();
            Assert.IsNull(pair.SelectedPortal);

            // 清除后回到 A 附近，自动模式应再次让 B 透明。
            PlacePlayerEye(pair, new Vector3(-0.75f, 0f, 0f));
            yield return null;
            Assert.AreEqual(1f, a.output.color.a, 0.01f);
            Assert.Less(b.output.color.a, 0.2f);

            var foreignGo = new GameObject("ForeignPortal");
            foreignGo.SetActive(false);
            var foreign = foreignGo.AddComponent<CaptureDisplayGroup>();
            Assert.Throws<System.ArgumentException>(() => pair.SelectPortal(foreign));
            Object.Destroy(foreignGo);
        }

        [UnityTest]
        public IEnumerator Facing_Penalty_Prevents_Back_Portal_From_Becoming_Source()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();
            PrepareTransparencySandbox(pair, a, b);

            a.transform.position = new Vector3(-1f, 0f, 0f);
            b.transform.position = new Vector3(1f, 0f, 0f);
            a.transform.rotation = b.transform.rotation = Quaternion.identity;
            a.Facing = b.Facing = PortalFacing.Right;
            a.otherViewFadeDistance = b.otherViewFadeDistance = 3f;

            // B 的直线距离只有 0.2，但玩家在 B 背面；A 距离 1.8 且位于正面。
            PlacePlayerEye(pair, new Vector3(0.8f, 0f, 0f));
            yield return null;
            Assert.AreEqual(1f, a.output.color.a, 0.01f,
                "正面较远的 A 应胜过直线距离更近但背面的 B。");
            Assert.AreEqual(0.6f, b.output.color.a, 0.03f);

            // B 改为朝左后，玩家位于 B 正面，透明目标立即换成 A。
            b.Facing = PortalFacing.Left;
            yield return null;
            Assert.Less(a.output.color.a, 0.2f);
            Assert.AreEqual(1f, b.output.color.a, 0.01f);
        }

        [UnityTest]
        public IEnumerator Portal_Hint_Beam_Matches_Height_And_Sorting()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(pair);

            var hintA = a.HintRenderer;
            var hintB = b.HintRenderer;
            Assert.IsNotNull(hintA, "A 应有门位置光带。");
            Assert.IsNotNull(hintB, "B 应有门位置光带。");

            float expectedHeight = pair.PortalHeight + a.hintExtraHeight;
            Assert.AreEqual(expectedHeight, hintA.transform.lossyScale.y, 0.01f,
                "线高应等于门洞高度 + hintExtraHeight。");
            Assert.Greater(hintA.transform.lossyScale.y, pair.PortalHeight,
                "线高应比门洞高一点，两端才能从遮罩外露出来。");
            Assert.AreEqual(a.hintBeamLength, hintA.transform.lossyScale.x, 0.01f,
                "光带长度应符合 hintBeamLength。");

            Assert.AreEqual(0, hintA.gameObject.layer,
                "光带应在默认层，传送门相机才拍得到。");
            Assert.AreNotEqual(0, a.capture.captureCamera.cullingMask & (1 << hintA.gameObject.layer),
                "传送门相机不应排除光带层。");
            Assert.AreEqual("Portal light band", hintA.sortingLayerName,
                "光带应使用 Portal light band 排序层。");

            var materialA = hintA.sharedMaterial;
            var materialB = hintB.sharedMaterial;
            Assert.IsNotNull(materialA, "A 光带应有提示材质。");
            Assert.IsNotNull(materialB, "B 光带应有提示材质。");
            Assert.AreEqual("Hidden/Platformer/PortalHintBeam", materialA.shader.name,
                "光带应使用 PortalHintBeam shader。");
            Assert.Greater(materialA.GetColor("_Color").b, materialA.GetColor("_Color").r,
                "A 门光带应为蓝色。");
            Assert.Greater(materialB.GetColor("_Color").r, materialB.GetColor("_Color").b,
                "B 门光带应为红色。");
            Assert.AreEqual(a.FacingSign, materialA.GetFloat("_Direction"), 0.01f,
                "A 的渐变方向应朝 facing。");
            Assert.AreEqual(b.FacingSign, materialB.GetFloat("_Direction"), 0.01f,
                "B 的渐变方向应朝 facing。");
            Assert.Less(materialA.GetFloat("_Alpha"), 0.7f,
                "整体透明度应拉低。");
            Assert.AreEqual(0.5f, materialA.GetFloat("_BreathFrequency"), 0.001f,
                "代码配置的呼吸频率应传递给材质。");
            Assert.Greater(materialA.GetFloat("_NoiseStrength"), 0f,
                "光带应带噪音失真。");
            Assert.Greater(materialA.GetFloat("_NoiseSpeed"), 0.5f,
                "噪音掩码应持续移动。");

            Assert.Greater(-a.FacingSign * (a.HintLinePosition.x - a.transform.position.x), 0f,
                "细线应在门的背面，方向与 facing 相反。");
            Assert.Greater(-b.FacingSign * (b.HintLinePosition.x - b.transform.position.x), 0f,
                "细线应在门的背面，方向与 facing 相反。");

            // 高度只看 PortalPair，不跟开门动画。
            a.PlayOpenAnimation();
            yield return null;
            Assert.AreEqual(expectedHeight, hintA.transform.lossyScale.y, 0.01f,
                "开门动画不应改变线高。");

            // 粒子：A 蓝 / B 红，从细线沿 facing 方向发射，排在输出之上且可被捕获。
            var particlesA = a.HintParticles;
            var particlesB = b.HintParticles;
            Assert.IsNotNull(particlesA, "A 应有门位置粒子。");
            Assert.IsNotNull(particlesB, "B 应有门位置粒子。");
            Assert.AreEqual(0, particlesA.gameObject.layer,
                "粒子应在默认层，传送门相机才拍得到。");
            Assert.AreNotEqual(0, a.capture.captureCamera.cullingMask & (1 << particlesA.gameObject.layer),
                "传送门相机不应排除粒子层。");

            var particlesRendererA = particlesA.GetComponent<ParticleSystemRenderer>();
            Assert.AreEqual("Portal light band", particlesRendererA.sortingLayerName,
                "粒子和光带应使用 Portal light band 排序层。");
            Assert.Greater(particlesRendererA.sortingOrder, 0,
                "粒子应排在 Portal light band 层内较前的位置。");
            Assert.Greater(particlesA.main.startColor.color.b, particlesA.main.startColor.color.r,
                "A 门粒子应为蓝色。");
            Assert.Greater(particlesB.main.startColor.color.r, particlesB.main.startColor.color.b,
                "B 门粒子应为红色。");
            Assert.Greater(particlesA.shape.scale.y, 0f,
                "静态粒子发射区域应由场景对象自身定义。");
            Assert.AreEqual(a.hintParticleRate * 0.25f,
                particlesA.emission.rateOverTime.constant, 0.01f,
                "粒子发射率应为场景中配置值。");
            Assert.Less(particlesA.emission.rateOverTime.constant, 30f,
                "粒子密度应较稀。");
            Assert.GreaterOrEqual(particlesA.main.maxParticles, 256,
                "粒子池应更大。");
        }

        [UnityTest]
        public IEnumerator Portal_Hint_Stays_Static_While_Material_Follows_Facing()
        {
            FindGroups(out var a, out _);

            Assert.IsNotNull(a);
            Assert.IsNotNull(a.viewCenter, "A 应有视觉中心。");
            Assert.IsNotNull(a.HintRenderer, "A 应有门位置光带。");
            Assert.IsNotNull(a.HintParticles, "A 应有门位置粒子。");

            Transform center = a.viewCenter;
            Vector3 originalCenter = center.localPosition;
            PortalFacing originalFacing = a.Facing;

            Vector3 originalHintPosition = a.HintRenderer.transform.position;
            Quaternion originalHintRotation = a.HintRenderer.transform.rotation;
            Vector3 originalHintScale = a.HintRenderer.transform.lossyScale;
            Vector3 originalParticlePosition = a.HintParticles.transform.position;

            // 光带/粒子是场景静态对象；移动视觉中心不应移动它们。
            center.localPosition = originalCenter + new Vector3(0.5f, 0f, 0f);
            yield return null;

            Assert.AreEqual(originalHintPosition, a.HintRenderer.transform.position,
                "静态光带的位置不应被运行时移动。");
            Assert.AreEqual(originalHintRotation, a.HintRenderer.transform.rotation,
                "静态光带的旋转不应被运行时修改。");
            Assert.AreEqual(originalHintScale, a.HintRenderer.transform.lossyScale,
                "静态光带的缩放不应被运行时修改。");
            Assert.AreEqual(originalParticlePosition, a.HintParticles.transform.position,
                "静态粒子的位置不应被运行时移动。");

            // 改变朝向：材质方向实时同步，但不移动静态对象。
            PortalFacing newFacing = originalFacing == PortalFacing.Left
                ? PortalFacing.Right
                : PortalFacing.Left;
            a.Facing = newFacing;

            Assert.AreEqual(newFacing == PortalFacing.Left ? -1f : 1f, a.FacingSign, 0.001f,
                "FacingSign 应立即跟随朝向。");
            Assert.AreEqual(a.FacingSign, a.HintRenderer.sharedMaterial.GetFloat("_Direction"), 0.001f,
                "材质渐变方向应立即跟随朝向。");
            Assert.AreEqual(originalHintPosition, a.HintRenderer.transform.position,
                "改变朝向后静态光带也不应被移动。");
            Assert.AreEqual(originalParticlePosition, a.HintParticles.transform.position,
                "改变朝向后静态粒子也不应被移动。");

            // 还原，避免影响后续测试。
            a.Facing = originalFacing;
            center.localPosition = originalCenter;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Facing_Repeated_Switches_Restore_View_Centers()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(pair);
            Assert.IsNotNull(a.viewCenter);
            Assert.IsNotNull(b.viewCenter);

            PortalFacing originalFacing = a.Facing;
            PortalFacing oppositeFacing = originalFacing == PortalFacing.Left
                ? PortalFacing.Right
                : PortalFacing.Left;
            Vector3 originalScale = a.viewCenter.localScale;
            Vector3 partnerScale = b.viewCenter.localScale;

            try
            {
                for (int i = 0; i < 2; i++)
                {
                    a.Facing = oppositeFacing;
                    CaptureDisplayGroup.ApplyFacingStates(a, b);
                    yield return null;

                    a.Facing = originalFacing;
                    CaptureDisplayGroup.ApplyFacingStates(a, b);
                    yield return null;

                    Assert.AreEqual(originalScale, a.viewCenter.localScale,
                        "重复换向后再回到原朝向，视觉状态应与初始状态一致。");
                    Assert.AreEqual(partnerScale, b.viewCenter.localScale,
                        "重复换向不应在 partner 的 View Center 上累积缩放。");
                }
            }
            finally
            {
                a.Facing = originalFacing;
                CaptureDisplayGroup.ApplyFacingStates(a, b);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Facing_AllFourCombinations_ApplyBothPortals_AtOnce()
        {
            FindGroups(out var a, out var b);
            var pair = Object.FindObjectOfType<PortalPair>();

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(pair);

            PortalFacing originalA = a.Facing;
            PortalFacing originalB = b.Facing;
            PortalFacing[] directions = { PortalFacing.Left, PortalFacing.Right };

            // 先记录左右半屏各自的正确位置，后续验证动态换向不会保留旧 X。
            a.SetOutputHalf(PortalViewHalf.Left);
            float aLeftX = a.output.transform.localPosition.x;
            a.SetOutputHalf(PortalViewHalf.Right);
            float aRightX = a.output.transform.localPosition.x;
            b.SetOutputHalf(PortalViewHalf.Left);
            float bLeftX = b.output.transform.localPosition.x;
            b.SetOutputHalf(PortalViewHalf.Right);
            float bRightX = b.output.transform.localPosition.x;

            try
            {
                foreach (PortalFacing aFacing in directions)
                {
                    foreach (PortalFacing bFacing in directions)
                    {
                        a.Facing = aFacing;
                        b.Facing = bFacing;
                        CaptureDisplayGroup.ApplyFacingStates(a, b);

                        bool aLeft = aFacing == PortalFacing.Left;
                        bool bLeft = bFacing == PortalFacing.Left;

                        Assert.AreEqual(new Vector3(aLeft ? 1f : -1f, aLeft ? -1f : 1f, 1f),
                            a.maskTop.localScale, $"A={aFacing}, B={bFacing}：A 上遮罩。");
                        Assert.AreEqual(a.maskTop.localScale, a.maskBottom.localScale,
                            $"A={aFacing}, B={bFacing}：A 两块遮罩应一致。");
                        float viewX = aLeft == bLeft ? -1f : 1f;
                        Assert.AreEqual(new Vector3(viewX, aLeft ? 1f : -1f, 1f),
                            a.viewCenter.localScale, $"A={aFacing}, B={bFacing}：A 视图。");
                        Assert.AreEqual(!aLeft, a.output.flipY,
                            $"A={aFacing}, B={bFacing}：A 输出 Y 翻转。");

                        Assert.AreEqual(new Vector3(1f, bLeft ? -1f : 1f, 1f),
                            b.maskTop.localScale, $"A={aFacing}, B={bFacing}：B 上遮罩。");
                        Assert.AreEqual(b.maskTop.localScale, b.maskBottom.localScale,
                            $"A={aFacing}, B={bFacing}：B 两块遮罩应一致。");
                        Assert.AreEqual(new Vector3(viewX, -1f, 1f),
                            b.viewCenter.localScale, $"A={aFacing}, B={bFacing}：B 视图。");
                        Assert.IsTrue(b.output.flipY,
                            $"A={aFacing}, B={bFacing}：B 输出 Y 始终翻转。");

                        Assert.IsFalse(a.output.flipX,
                            $"A={aFacing}, B={bFacing}：A 输出 X 不翻转。");
                        Assert.IsFalse(b.output.flipX,
                            $"A={aFacing}, B={bFacing}：B 输出 X 不翻转。");
                        Assert.AreEqual(aLeft ? PortalViewHalf.Left : PortalViewHalf.Right, b.OutputHalf,
                            $"A={aFacing}, B={bFacing}：B 使用 A 相机的对应半屏。");
                        Assert.AreEqual(bLeft ? PortalViewHalf.Left : PortalViewHalf.Right, a.OutputHalf,
                            $"A={aFacing}, B={bFacing}：A 使用 B 相机的对应半屏。");
                        Assert.AreEqual(aLeft ? bLeftX : bRightX, b.output.transform.localPosition.x, 0.0001f,
                            $"A={aFacing}, B={bFacing}：B 换半屏后必须同步更新 X，不能保留旧位置。");
                        Assert.AreEqual(bLeft ? aLeftX : aRightX, a.output.transform.localPosition.x, 0.0001f,
                            $"A={aFacing}, B={bFacing}：A 换半屏后必须同步更新 X，不能保留旧位置。");
                    }
                }
            }
            finally
            {
                a.Facing = originalA;
                b.Facing = originalB;
                CaptureDisplayGroup.ApplyFacingStates(a, b);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Initially_Inactive_Portal_Does_Not_Accumulate_View_X()
        {
            var root = new GameObject("InactivePortalViewLifecycleTest");
            root.SetActive(false);
            var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            Sprite fullA = null;
            Sprite fullB = null;

            CaptureDisplayGroup CreateGroup(CaptureGroupSide side)
            {
                var groupObject = new GameObject($"Portal {side}");
                groupObject.transform.SetParent(root.transform, false);

                var centerObject = new GameObject("Portal View Center");
                centerObject.transform.SetParent(groupObject.transform, false);
                var viewObject = new GameObject("Portal View");
                viewObject.transform.SetParent(centerObject.transform, false);
                var renderer = viewObject.AddComponent<SpriteRenderer>();
                renderer.sprite = Sprite.Create(texture, new Rect(0f, 0f, 64f, 32f),
                    new Vector2(0.5f, 0.5f), 40f);

                var topObject = new GameObject("View Mask Top");
                topObject.transform.SetParent(groupObject.transform, false);
                var bottomObject = new GameObject("View Mask Bottom");
                bottomObject.transform.SetParent(groupObject.transform, false);

                var group = groupObject.AddComponent<CaptureDisplayGroup>();
                group.side = side;
                group.viewCenter = centerObject.transform;
                group.output = renderer;
                group.maskTop = topObject.transform;
                group.maskBottom = bottomObject.transform;
                group.openDuration = 0f;
                return group;
            }

            var a = CreateGroup(CaptureGroupSide.A);
            var b = CreateGroup(CaptureGroupSide.B);
            fullA = a.output.sprite;
            fullB = b.output.sprite;

            try
            {
                a.partner = b;
                b.partner = a;

                // 模拟 PortalPair.Start 在门自身 Awake/Start 之前设置四态视觉。
                CaptureDisplayGroup.ApplyFacingStates(a, b);
                float expectedAX = a.output.transform.localPosition.x;
                float expectedBX = b.output.transform.localPosition.x;

                root.SetActive(true);
                yield return null;
                yield return null;

                Assert.AreEqual(expectedAX, a.output.transform.localPosition.x, 0.0001f,
                    "A 首次启用时不能把已裁切的 X 当成初始基准并再次叠加偏移。");
                Assert.AreEqual(expectedBX, b.output.transform.localPosition.x, 0.0001f,
                    "B 首次启用时不能把已裁切的 X 当成初始基准并再次叠加偏移。");
            }
            finally
            {
                Object.Destroy(root);
                if (fullA != null) Object.Destroy(fullA);
                if (fullB != null) Object.Destroy(fullB);
                Object.Destroy(texture);
            }
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

            // 开口方向的反侧 = 门背面（A 被旋转过，用实际开口向量）。
            Vector3 opening = a.OpeningDirection;
            Vector3 behind = a.transform.position - opening * 2f;
            controller.Teleport(behind);
            yield return null;
            yield return null;
            Assert.IsFalse(a.output.enabled, "玩家在门背面时应该隐藏该门的画面。");

            // 回到开口方向一侧
            Vector3 front = a.transform.position + opening * 2f;
            controller.Teleport(front);
            yield return null;
            yield return null;
            Assert.IsTrue(a.output.enabled, "玩家回到门正面时应该恢复显示。");
        }
    }
}
