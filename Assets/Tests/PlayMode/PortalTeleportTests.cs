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

            // 用 B 门做入口：起点放在 B 的开口方向外侧，向门内移动。
            Vector3 start = (Vector3)b.transform.position
                            + (Vector3)b.OpeningDirection * 1.2f;
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

            // 用 B 门做入口：B 当前旋转 45°，起点沿其开口方向外侧，出口是 A。
            Vector3 start = (Vector3)b.transform.position
                            + (Vector3)b.OpeningDirection * 1.2f;
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
            var player = FindPlayer();

            Assert.IsNotNull(a);
            Assert.IsNotNull(b);
            Assert.IsNotNull(player);

            var controller = player.GetComponent<PlayerController2D>();
            Assert.IsNotNull(controller);

            // B 的 alpha 由“玩家到 A”的距离和 B 自己的 otherViewFadeDistance 决定。
            float fade = b.otherViewFadeDistance;
            Assert.Greater(fade, 0f, "淡出距离应大于 0。");

            // 用 A 的实际视觉中心作为距离基准，并临时关闭重力，
            // 避免玩家在等待 LateUpdate 时下落导致距离和 alpha 不精确。
            Transform anchor = a.viewCenter != null ? a.viewCenter : a.transform;
            Rigidbody2D rb = player.GetComponent<Rigidbody2D>();
            float originalGravity = rb != null ? rb.gravityScale : 1f;
            RigidbodyType2D originalBodyType = rb != null ? rb.bodyType : RigidbodyType2D.Dynamic;
            if (rb != null)
            {
                // 这个测试只验证距离与 alpha，不验证物理；改成 Kinematic 避免
                // 传送点落在地形/残余碰撞里时被物理解算器推开。
                rb.bodyType = RigidbodyType2D.Kinematic;
                rb.gravityScale = 0f;
                rb.velocity = Vector2.zero;
            }

            try
            {
                // 靠近 A 到淡出距离的一半：另一扇门 B 应半透明，A 自己保持不透明。
                controller.Teleport((Vector2)anchor.position
                                    + (Vector2)anchor.right * (fade * 0.5f));
                yield return new WaitForFixedUpdate();
                yield return null;

                Assert.AreEqual(0.5f, b.output.color.a, 0.05f,
                    "靠近 A 到一半距离时，B 的画面应为半透明。");
                Assert.AreEqual(1f, a.output.color.a, 0.05f,
                    "A 自己的画面不应变透明。");

                // 退到淡出距离之外：B 恢复不透明。
                controller.Teleport((Vector2)anchor.position
                                    + (Vector2)anchor.right * (fade * 2f));
                yield return new WaitForFixedUpdate();
                yield return null;

                Assert.AreEqual(1f, b.output.color.a, 0.05f,
                    "超过淡出距离后，B 的画面应恢复不透明。");
            }
            finally
            {
                if (rb != null)
                {
                    rb.gravityScale = originalGravity;
                    rb.bodyType = originalBodyType;
                }
            }
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

            Assert.Greater(a.HintLinePosition.x, a.transform.position.x,
                "A 朝左，细线应在门的背面（右侧）。");
            Assert.Less(b.HintLinePosition.x, b.transform.position.x,
                "B 朝右，细线应在门的背面（左侧）。");

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
