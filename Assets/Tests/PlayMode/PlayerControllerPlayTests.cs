using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Platformer.Tests
{
    /// <summary>
    /// 角色控制器与相机的 PlayMode 测试。
    /// 在 Test Runner 窗口运行，或用批处理：
    ///   Unity.exe -batchmode -projectPath . -runTests -testPlatform PlayMode
    /// </summary>
    public class PlayerControllerPlayTests
    {
        GameObject _ground;
        GameObject _player;
        PlayerController2D _controller;
        Vector2 _originalGravity;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            // 与模板使用的工程级重力保持一致，并在 TearDown 恢复，避免污染其他测试。
            _originalGravity = Physics2D.gravity;
            Physics2D.gravity = new Vector2(0f, -40f);

            _controller = PlayTestFixtures.CreatePlayer(null, new Vector3(0f, 0.9f, 0f));
            _player = _controller.gameObject;

            int groundLayer = 0;
            while ((_controller.groundMask.value & (1 << groundLayer)) == 0)
                groundLayer++;

            _ground = new GameObject("TestGround");
            _ground.layer = groundLayer;
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            var groundCollider = _ground.AddComponent<BoxCollider2D>();
            groundCollider.size = new Vector2(100f, 1f);

            // 让胶囊在地面上稳定下来。
            for (int i = 0; i < 15; i++)
                yield return new WaitForFixedUpdate();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Physics2D.gravity = _originalGravity;
            if (_player != null) Object.Destroy(_player);
            if (_ground != null) Object.Destroy(_ground);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Player_Stands_On_Ground()
        {
            Assert.IsTrue(_controller.IsGrounded, "Player should be grounded after settling.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Jump_Reaches_Target_Cell_Height()
        {
            float groundY = _player.transform.position.y;

            _controller.SetMoveInput(0f);
            _controller.JumpPressed();

            float peakY = groundY;
            for (int i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                peakY = Mathf.Max(peakY, _player.transform.position.y);
            }

            float measured = peakY - groundY;
            Debug.Log($"[Test] Jump apex measured: {measured:F3} cells");
            Assert.AreEqual(6.2f, measured, 0.2f,
                $"Expected a 6.2-cell jump, measured {measured:F2} cells.");
        }

        [UnityTest]
        public IEnumerator Portal_Preserves_Held_Jump_Progress()
        {
            float groundY = _player.transform.position.y;
            _controller.SetMoveInput(0f);
            _controller.JumpPressed();
            for (int i = 0; i < 3; i++)
                yield return new WaitForFixedUpdate();

            var body = _player.GetComponent<Rigidbody2D>();
            Vector2 exitVelocity = _controller.Velocity;
            _controller.TeleportWithPortalRotation(_player.transform.position, 0f);
            body.velocity = exitVelocity;

            float peakY = groundY;
            for (int i = 0; i < 150; i++)
            {
                yield return new WaitForFixedUpdate();
                peakY = Mathf.Max(peakY, _player.transform.position.y);
            }
            _controller.JumpReleased();

            float measured = peakY - groundY;
            Assert.AreEqual(6.2f, measured, 0.2f,
                $"传送不应清除剩余长按跳跃，实测高度 {measured:F2} 格。");
        }

        [UnityTest]
        public IEnumerator Diagonal_Portal_Maintains_Jump_Along_Exit_Tangent()
        {
            _controller.SetMoveInput(0f);
            _controller.JumpPressed();
            for (int i = 0; i < 3; i++)
                yield return new WaitForFixedUpdate();

            Vector2 exitTangent = new Vector2(-1f, 1f).normalized;
            float directionalSpeed = _controller.Velocity.y;
            var body = _player.GetComponent<Rigidbody2D>();
            _controller.TeleportWithPortalRotation(
                _player.transform.position, 45f, exitTangent, true);
            body.velocity = exitTangent * directionalSpeed;

            yield return new WaitForFixedUpdate();

            Assert.Less(Vector2.Distance(exitTangent, _controller.VariableJumpDirection), 0.001f,
                "斜向门应保留变换后的跳跃推进方向。");
            Assert.AreEqual(directionalSpeed,
                Vector2.Dot(_controller.Velocity, exitTangent), 0.15f,
                "剩余长按应沿出口切线维持，而不是强制恢复世界 Y 轴速度。");
            _controller.JumpReleased();
        }

        [UnityTest]
        public IEnumerator Horizontal_Movement_Works()
        {
            _controller.SetMoveInput(1f);
            float startX = _player.transform.position.x;

            for (int i = 0; i < 60; i++)
                yield return new WaitForFixedUpdate();

            Assert.Greater(_player.transform.position.x - startX, 4f,
                "Player should have moved at least 4 units to the right in 1 second.");
            _controller.SetMoveInput(0f);
        }

        [UnityTest]
        public IEnumerator Running_Jump_Preserves_Horizontal_Boost()
        {
            _controller.SetMoveInput(1f);
            for (int i = 0; i < 6; i++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(9f, _controller.Velocity.x, 0.1f);
            _controller.JumpPressed();
            yield return new WaitForFixedUpdate();
            Assert.Greater(_controller.Velocity.x, 12f, "跑跳应获得水平助推。");

            for (int i = 0; i < 4; i++)
                yield return new WaitForFixedUpdate();

            Assert.Greater(_controller.Velocity.x, 9.5f,
                "空中同向超速应缓慢衰减，而不是立即压回跑速。");
            _controller.JumpReleased();
        }

        [UnityTest]
        public IEnumerator Same_Frame_Press_And_Release_Produces_Short_Jump()
        {
            float groundY = _player.transform.position.y;
            _controller.SetMoveInput(0f);
            _controller.JumpPressed();
            _controller.JumpReleased();

            float peakY = groundY;
            for (int i = 0; i < 60; i++)
            {
                yield return new WaitForFixedUpdate();
                peakY = Mathf.Max(peakY, _player.transform.position.y);
            }

            float measured = peakY - groundY;
            Debug.Log($"[Test] Same-frame short jump apex: {measured:F3} cells");
            Assert.Greater(measured, 0.5f, "同帧点放不能丢失跳跃。");
            Assert.Less(measured, 2.2f, "同帧点放应保持为短跳。");
        }

        [UnityTest]
        public IEnumerator Jump_Buffer_Triggers_After_Landing()
        {
            _controller.SetMoveInput(0f);
            _controller.Teleport(new Vector2(0f, 4f));

            bool pressed = false;
            for (int i = 0; i < 100 && !pressed; i++)
            {
                yield return new WaitForFixedUpdate();
                if (i >= 6 && _player.transform.position.y <= 2f)
                {
                    _controller.JumpPressed();
                    _controller.JumpReleased();
                    pressed = true;
                }
            }

            Assert.IsTrue(pressed, "未能在落地前提交测试跳跃输入。");
            bool jumped = false;
            float jumpY = float.PositiveInfinity;
            for (int i = 0; i < 40; i++)
            {
                yield return new WaitForFixedUpdate();
                if (!_controller.IsGrounded && _controller.Velocity.y > 1f)
                {
                    jumped = true;
                    jumpY = _player.transform.position.y;
                    break;
                }
            }

            Assert.IsTrue(jumped, "落地前的跳跃预输入应在接地时自动起跳。");
            Assert.Less(jumpY, 1.5f, "预输入应在地面附近触发，而不是在空中提前起跳。");
        }

        [UnityTest]
        public IEnumerator Coyote_Time_Allows_Jump_After_Leaving_Ground()
        {
            _controller.SetMoveInput(0f);
            _controller.Teleport(new Vector2(60f, 0.9f));
            for (int i = 0; i < 2; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsFalse(_controller.IsGrounded);
            _controller.JumpPressed();
            _controller.JumpReleased();
            yield return new WaitForFixedUpdate();

            Assert.Greater(_controller.Velocity.y, 1f,
                "离开平台后的土狼时间内仍应允许起跳。");
        }

        [UnityTest]
        public IEnumerator High_Friction_Ground_Does_Not_Change_Movement()
        {
            var highFriction = new PhysicsMaterial2D("Test High Friction Ground")
            {
                friction = 1f,
                bounciness = 0f,
                hideFlags = HideFlags.DontSave
            };
            _ground.GetComponent<Collider2D>().sharedMaterial = highFriction;

            var playerMaterial = _player.GetComponent<CapsuleCollider2D>().sharedMaterial;
            Assert.IsNotNull(playerMaterial);
            Assert.AreEqual(0f, playerMaterial.friction, 0.0001f);

            _controller.SetMoveInput(1f);
            for (int i = 0; i < 30; i++)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(9f, _controller.Velocity.x, 0.1f,
                "高摩擦地形不应降低显式控制的跑速。");

            _controller.SetMoveInput(0f);
            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();
            Assert.AreEqual(0f, _controller.Velocity.x, 0.1f,
                "高摩擦地形不应改变停止时间。");
        }

        [UnityTest]
        public IEnumerator Falling_Uses_Softer_Gravity_And_Terminal_Speed()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            _controller.Teleport(new Vector2(0f, 20f));
            _controller.SetMoveInput(0f);

            try
            {
                for (int i = 0; i < 4; i++)
                    yield return new WaitForFixedUpdate();

                float expectedSpeed = 40f * 4f * 0.75f * Time.fixedDeltaTime * 4f;
                Assert.AreEqual(expectedSpeed, -_controller.Velocity.y, 0.2f,
                    "下落前几步应使用较低的下落重力，而不是完整基础重力。");

                for (int i = 0; i < 20; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(24f, -_controller.Velocity.y, 0.05f,
                    "下落应稳定在调整后的终端速度。");
            }
            finally
            {
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
        }

        [UnityTest]
        public IEnumerator Wall_Slide_Slows_Then_Gradually_Restores_Fall_Speed()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            var wall = new GameObject("TestWallSlide");
            wall.layer = _ground.layer;
            wall.transform.position = new Vector3(1f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 100f);

            try
            {
                _controller.Teleport(new Vector2(0.14f, 4f));
                _controller.SetMoveInput(1f);
                for (int i = 0; i < 12 && !_controller.IsWallSliding; i++)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(_controller.IsWallSliding, "推向墙面时应进入贴墙下落。");
                for (int i = 0; i < 10; i++)
                    yield return new WaitForFixedUpdate();

                float earlyFallSpeed = -_controller.Velocity.y;
                Debug.Log($"[Test] Early wall-slide fall speed: {earlyFallSpeed:F3}");
                Assert.Greater(earlyFallSpeed, 3f);
                Assert.Less(earlyFallSpeed, 12f, "贴墙下落开始时应明显减速。");

                for (int i = 0; i < 55; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Greater(-_controller.Velocity.y, 20f,
                    "持续滑墙后下落上限应逐渐恢复，不能无限缓慢悬挂。");

                _controller.SetMoveInput(0f);
                yield return new WaitForFixedUpdate();
                Assert.IsFalse(_controller.IsWallSliding, "松开推墙方向应立即退出贴墙下落。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
                Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator Wall_Jump_Pushes_Away_And_Locks_Input_Briefly()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            var wall = new GameObject("TestWallJump");
            wall.layer = _ground.layer;
            wall.transform.position = new Vector3(1f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 100f);

            try
            {
                _controller.Teleport(new Vector2(0.14f, 4f));
                _controller.SetMoveInput(1f);
                for (int i = 0; i < 8; i++)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(_controller.IsWallSliding);
                float jumpStartY = _player.transform.position.y;
                _controller.JumpPressed();
                yield return new WaitForFixedUpdate();

                Assert.Less(_controller.Velocity.x, -10f, "墙跳应施加背离墙面的水平速度。");
                Assert.Greater(_controller.Velocity.y, 10f, "墙跳应使用独立的垂直初速度。");

                for (int i = 0; i < 5; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Less(_controller.Velocity.x, -9f,
                    "方向锁期间，持续推向原墙也不能立刻把玩家拉回去。");

                float peakY = _player.transform.position.y;
                for (int i = 0; i < 60; i++)
                {
                    yield return new WaitForFixedUpdate();
                    peakY = Mathf.Max(peakY, _player.transform.position.y);
                }
                float measuredHeight = peakY - jumpStartY;
                Assert.AreEqual(4.8f, measuredHeight, 0.25f,
                    $"墙跳应使用较低的独立高度，实测 {measuredHeight:F2} 格。");
            }
            finally
            {
                _controller.JumpReleased();
                _controller.SetMoveInput(0f);
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
                Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator Same_Wall_Return_Is_Lower_Than_Wall_Jump_Start()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            var wall = new GameObject("TestSameWallReturn");
            wall.layer = _ground.layer;
            wall.transform.position = new Vector3(1f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 100f);

            try
            {
                _controller.Teleport(new Vector2(0.14f, 4f));
                _controller.SetMoveInput(1f);
                for (int i = 0; i < 8; i++)
                    yield return new WaitForFixedUpdate();

                Assert.IsTrue(_controller.IsWallSliding);
                float jumpStartY = _player.transform.position.y;
                _controller.JumpPressed();
                yield return new WaitForFixedUpdate();

                bool returnedToWall = false;
                for (int i = 0; i < 180; i++)
                {
                    yield return new WaitForFixedUpdate();
                    if (_controller.IsWallSliding)
                    {
                        returnedToWall = true;
                        break;
                    }
                }

                Assert.IsTrue(returnedToWall, "持续推向原墙时应能自然返回并重新贴墙。");
                float returnY = _player.transform.position.y;
                Debug.Log($"[Test] Same-wall return height delta: {returnY - jumpStartY:F3}");
                Assert.Less(returnY, jumpStartY - 0.1f,
                    $"返回同墙时应低于墙跳起点；起跳 {jumpStartY:F2}，返回 {returnY:F2}。");
            }
            finally
            {
                _controller.JumpReleased();
                _controller.SetMoveInput(0f);
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
                Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator Camera_Follows_Player()
        {
            var cameraGo = new GameObject("TestCamera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.orthographic = true;
            var follow = cameraGo.AddComponent<CameraFollow2D>();
            follow.target = _player.transform;
            cameraGo.transform.position = _player.transform.position + new Vector3(0f, 1.2f, -10f);

            yield return null;

            _controller.Teleport(new Vector2(_player.transform.position.x + 12f, _player.transform.position.y));

            // 批处理模式渲染极快，所以按游戏时间等待，而不是按帧。
            yield return new WaitForSeconds(1.5f);
            yield return null;

            Vector3 expected = _player.transform.position + new Vector3(0f, 1.2f, -10f);
            float error = Vector3.Distance(cameraGo.transform.position, expected);

            Object.Destroy(cameraGo);

            Debug.Log($"[Test] Camera follow error: {error:F3} units");
            Assert.Less(error, 1.5f, $"Camera should stay close to the player, error was {error:F2}.");
        }

        [UnityTest]
        public IEnumerator Camera_Teleport_Preserves_And_Rotates_Momentum()
        {
            var targetGo = new GameObject("RotationMomentumTarget");
            var cameraGo = new GameObject("RotationMomentumCamera");
            cameraGo.SetActive(false);
            cameraGo.transform.position = new Vector3(0f, 1.2f, -10f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            var follow = cameraGo.AddComponent<CameraFollow2D>();
            follow.target = targetGo.transform;
            cameraGo.SetActive(true);

            try
            {
                SetCameraMomentum(follow, new Vector3(1f, 0f, 0f), 12f, 0.75f, -3f);
                Matrix4x4 relative = targetGo.transform.worldToLocalMatrix *
                                     cameraGo.transform.localToWorldMatrix;

                targetGo.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                control.TeleportRelative(targetGo.transform, relative, false);
                yield return null;

                AssertCameraMomentum(follow, new Vector3(0f, 1f, 0f), 12f, 0.75f, -3f);
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
                Object.Destroy(targetGo);
            }
        }

        [UnityTest]
        public IEnumerator Camera_Mirrored_Teleport_Transforms_Momentum()
        {
            var targetGo = new GameObject("MirrorMomentumTarget");
            var cameraGo = new GameObject("MirrorMomentumCamera");
            cameraGo.SetActive(false);
            cameraGo.transform.position = new Vector3(0f, 1.2f, -10f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            var follow = cameraGo.AddComponent<CameraFollow2D>();
            follow.target = targetGo.transform;
            cameraGo.SetActive(true);

            try
            {
                SetCameraMomentum(follow, new Vector3(1f, 0f, 0f), 12f, 0.75f, -3f);
                Matrix4x4 relative = targetGo.transform.worldToLocalMatrix *
                                     cameraGo.transform.localToWorldMatrix;

                targetGo.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
                control.TeleportRelative(targetGo.transform, relative, true);
                yield return null;

                AssertCameraMomentum(follow, new Vector3(0f, -1f, 0f), 12f, -0.75f, 3f);
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
                Object.Destroy(targetGo);
            }
        }

        [UnityTest]
        public IEnumerator Camera_SameFacing_Teleport_Mirrors_Relative_Pose()
        {
            var targetGo = new GameObject("MirrorRelativePoseTarget");
            var cameraGo = new GameObject("MirrorRelativePoseCamera");
            cameraGo.SetActive(false);

            Vector3 oldRelativePosition = new Vector3(2.4f, 1.1f, -10f);
            Quaternion oldRelativeRotation = Quaternion.Euler(0f, 0f, 30f);
            cameraGo.transform.position = targetGo.transform.TransformPoint(oldRelativePosition);
            cameraGo.transform.rotation = oldRelativeRotation;

            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            cameraGo.SetActive(true);

            try
            {
                Matrix4x4 relative = targetGo.transform.worldToLocalMatrix *
                                     cameraGo.transform.localToWorldMatrix;

                // 使用非零出口位置和 -45° 出口角，验证完整的相对位姿映射。
                targetGo.transform.position = new Vector3(2f, 1f, 0f);
                targetGo.transform.rotation = Quaternion.Euler(0f, 0f, -45f);
                control.TeleportRelative(targetGo.transform, relative, true);
                yield return null;

                Vector3 actualLocalPosition = targetGo.transform.InverseTransformPoint(
                    cameraGo.transform.position);
                Vector3 cameraLocalPosition = Quaternion.Inverse(oldRelativeRotation) * oldRelativePosition;
                cameraLocalPosition.x = -cameraLocalPosition.x;
                Vector3 expectedLocalPosition = oldRelativeRotation * cameraLocalPosition;
                Assert.Less(Vector3.Distance(expectedLocalPosition, actualLocalPosition), 0.0001f,
                    "同向门应沿旧相机局部 X 镜像相对位置，再映射到出口玩家空间。");

                Quaternion localRotation = Quaternion.Inverse(targetGo.transform.rotation) *
                                           cameraGo.transform.rotation;
                float actualLocalAngle = Mathf.DeltaAngle(0f, localRotation.eulerAngles.z);
                Assert.AreEqual(30f, actualLocalAngle, 0.0001f,
                    "投影已负责画面镜像，同向门不应再次取反相机的旧玩家相对角度。");
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
                Object.Destroy(targetGo);
            }
        }

        [UnityTest]
        public IEnumerator SameFacing_Teleport_Flips_Sprite_Without_Mirroring_Player_Space()
        {
            var cameraGo = new GameObject("SameFacingSpriteCamera");
            cameraGo.SetActive(false);
            cameraGo.AddComponent<Camera>();
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            cameraGo.SetActive(true);

            var sprite = _controller.VisualRoot.GetComponent<SpriteRenderer>();
            Assert.IsNotNull(sprite);
            _controller.SetMoveInput(0f);
            sprite.flipX = false;
            Vector3 originalScale = _controller.VisualRoot.localScale;
            Quaternion originalRotation = _controller.VisualRoot.localRotation;
            Vector3 originalCameraPosition = cameraGo.transform.position;
            Quaternion originalCameraRotation = cameraGo.transform.rotation;

            try
            {
                control.ApplySameFacingPortalTeleport(_controller);
                Assert.IsTrue(sprite.flipX, "第一次同向传送应水平翻转一次精灵。");
                Assert.Less(Vector3.Distance(originalCameraPosition, cameraGo.transform.position), 0.0001f,
                    "没有待传送请求时，同向门状态切换不应直接移动相机。");
                Assert.Less(Quaternion.Angle(originalCameraRotation, cameraGo.transform.rotation), 0.0001f,
                    "没有待传送请求时，同向门状态切换不应直接旋转相机。");
                Assert.AreEqual(originalScale, _controller.VisualRoot.localScale,
                    "精灵镜像不能通过缩放玩家显示空间实现。");
                Assert.AreEqual(originalRotation, _controller.VisualRoot.localRotation,
                    "精灵镜像不能改变玩家显示空间的旋转。");

                control.ApplySameFacingPortalTeleport(_controller);
                Assert.IsFalse(sprite.flipX, "第二次同向传送应再次翻转精灵并恢复朝向。");
                Assert.Less(Vector3.Distance(originalCameraPosition, cameraGo.transform.position), 0.0001f);
                Assert.Less(Quaternion.Angle(originalCameraRotation, cameraGo.transform.rotation), 0.0001f);
                Assert.AreEqual(originalScale, _controller.VisualRoot.localScale);
                Assert.AreEqual(originalRotation, _controller.VisualRoot.localRotation);
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
            }

            yield return null;
        }

        static void SetCameraMomentum(
            CameraFollow2D follow,
            Vector3 followVelocity,
            float cameraAngleVelocity,
            float lookAhead,
            float lookAheadVelocity)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            System.Type type = typeof(CameraFollow2D);
            type.GetField("_followVelocity", flags).SetValue(follow, followVelocity);
            type.GetField("_cameraAngleVelocity", flags).SetValue(follow, cameraAngleVelocity);
            type.GetField("_lookAhead", flags).SetValue(follow, lookAhead);
            type.GetField("_lookAheadVelocity", flags).SetValue(follow, lookAheadVelocity);
        }

        static void AssertCameraMomentum(
            CameraFollow2D follow,
            Vector3 expectedFollowVelocity,
            float expectedCameraAngleVelocity,
            float expectedLookAhead,
            float expectedLookAheadVelocity)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
            System.Type type = typeof(CameraFollow2D);
            var followVelocity = (Vector3)type.GetField("_followVelocity", flags).GetValue(follow);
            float cameraAngleVelocity = (float)type.GetField("_cameraAngleVelocity", flags).GetValue(follow);
            float lookAhead = (float)type.GetField("_lookAhead", flags).GetValue(follow);
            float lookAheadVelocity = (float)type.GetField("_lookAheadVelocity", flags).GetValue(follow);

            Assert.Less(Vector3.Distance(expectedFollowVelocity, followVelocity), 0.0001f,
                $"位置动量应为 {expectedFollowVelocity}，实际为 {followVelocity}。");
            Assert.AreEqual(expectedCameraAngleVelocity, cameraAngleVelocity, 0.0001f,
                "传送后的角度动量不符合旋转/镜像规则。");
            Assert.AreEqual(expectedLookAhead, lookAhead, 0.0001f,
                "传送后的前瞻偏移不符合旋转/镜像规则。");
            Assert.AreEqual(expectedLookAheadVelocity, lookAheadVelocity, 0.0001f,
                "传送后的前晃动量不符合旋转/镜像规则。");
        }

        [UnityTest]
        public IEnumerator Camera_Mirror_Toggle_Parity_And_Disable_Restore_Input()
        {
            bool originalInput = _controller.IsHorizontalInputInverted;
            var cameraGo = new GameObject("MirrorParityTestCamera");
            cameraGo.SetActive(false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var mirror = cameraGo.AddComponent<PlayerControlManager>();
            mirror.player = _controller;
            cameraGo.SetActive(true);

            try
            {
                mirror.ToggleMirrorAndInput();
                Assert.IsTrue(mirror.IsMirrored);
                Assert.IsTrue(_controller.IsHorizontalInputInverted);
                Assert.Less(camera.projectionMatrix.m00, 0f, "第一次切换应镜像投影。");

                mirror.ToggleMirrorAndInput();
                Assert.IsFalse(mirror.IsMirrored);
                Assert.IsFalse(_controller.IsHorizontalInputInverted);
                Assert.Greater(camera.projectionMatrix.m00, 0f, "第二次切换应恢复投影。");

                mirror.ToggleMirrorAndInput();
                Assert.IsTrue(mirror.IsMirrored);
                Assert.IsTrue(_controller.IsHorizontalInputInverted);
                Assert.Less(camera.projectionMatrix.m00, 0f, "第三次切换应再次镜像投影。");

                mirror.enabled = false;
                Assert.AreEqual(originalInput, _controller.IsHorizontalInputInverted,
                    "禁用镜像组件应恢复玩家进入镜像前的输入基线。");
                Assert.Greater(camera.projectionMatrix.m00, 0f,
                    "禁用镜像组件应恢复相机原始投影。");
            }
            finally
            {
                if (mirror != null) mirror.enabled = false;
                _controller.SetHorizontalInputInverted(originalInput);
                Object.Destroy(cameraGo);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Camera_Mirror_Switching_Players_Restores_Both_Baselines()
        {
            bool originalFirstInput = _controller.IsHorizontalInputInverted;
            var secondController = PlayTestFixtures.CreatePlayer(
                null, new Vector3(20f, 0.9f, 0f));
            var secondPlayer = secondController.gameObject;
            bool originalSecondInput = secondController.IsHorizontalInputInverted;

            _controller.SetHorizontalInputInverted(false);
            secondController.SetHorizontalInputInverted(true);

            var cameraGo = new GameObject("MirrorSwitchPlayerTestCamera");
            cameraGo.SetActive(false);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var mirror = cameraGo.AddComponent<PlayerControlManager>();
            mirror.player = _controller;
            cameraGo.SetActive(true);

            try
            {
                mirror.ToggleMirrorAndInput();
                Assert.IsTrue(_controller.IsHorizontalInputInverted);

                mirror.player = secondController;
                mirror.ToggleMirrorAndInput();

                Assert.IsFalse(_controller.IsHorizontalInputInverted,
                    "换玩家后应恢复旧玩家的 false 基线，而不是把新玩家状态留给旧玩家。");
                Assert.IsFalse(secondController.IsHorizontalInputInverted,
                    "新玩家应接收当前未镜像状态。");

                mirror.enabled = false;
                Assert.IsFalse(_controller.IsHorizontalInputInverted,
                    "禁用组件不应再次覆盖旧玩家。");
                Assert.IsTrue(secondController.IsHorizontalInputInverted,
                    "禁用组件应恢复新玩家自己的 true 基线。");
            }
            finally
            {
                if (mirror != null) mirror.enabled = false;
                _controller.SetHorizontalInputInverted(originalFirstInput);
                if (secondController != null)
                    secondController.SetHorizontalInputInverted(originalSecondInput);
                Object.Destroy(cameraGo);
                Object.Destroy(secondPlayer);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Control_Manager_Does_Not_Overwrite_Program_Move_Input()
        {
            bool originalInput = _controller.IsHorizontalInputInverted;
            _controller.SetHorizontalInputInverted(false);

            var cameraGo = new GameObject("ProgramInputPriorityTestCamera");
            cameraGo.SetActive(false);
            cameraGo.AddComponent<Camera>();
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            cameraGo.SetActive(true);

            try
            {
                control.SetMoveInput(0.75f);
                yield return null;

                Assert.IsTrue(_controller.IsManualInput, "程序移动应保持接管状态。");
                Assert.AreEqual(0.75f, _controller.MoveInput, 0.001f,
                    "总控的设备输入帧不能覆盖程序移动。");

                control.SetMoveInput(0f);
                yield return null;
                Assert.IsTrue(_controller.IsManualInput, "程序传入 0 仍应保持接管。");
                Assert.AreEqual(0f, _controller.MoveInput, 0.001f);

                control.ClearManualInput();
                yield return null;
                Assert.IsFalse(_controller.IsManualInput, "清除后应恢复设备输入。");
                Assert.AreEqual(0f, _controller.MoveInput, 0.001f,
                    "没有按键时，恢复设备输入后应保持静止。");
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
                _controller.SetHorizontalInputInverted(originalInput);
                _controller.SetMoveInput(0f);
            }
        }

        [UnityTest]
        public IEnumerator Independent_And_Linked_States_Do_Not_Overwrite_Each_Other()
        {
            bool originalInput = _controller.IsHorizontalInputInverted;
            _controller.SetHorizontalInputInverted(false);

            var cameraGo = new GameObject("IndependentStateTestCamera");
            cameraGo.SetActive(false);
            cameraGo.AddComponent<Camera>();
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            cameraGo.SetActive(true);

            try
            {
                control.SetMirrorAndInput(false);
                control.SetInputInverted(true);

                Assert.IsTrue(_controller.IsHorizontalInputInverted);
                Assert.IsFalse(control.IsMirrored, "独立输入反转不能镜像相机。");
                Assert.Greater(cameraGo.GetComponent<Camera>().projectionMatrix.m00, 0f);

                control.SetCameraMirrored(true);
                Assert.IsTrue(_controller.IsHorizontalInputInverted,
                    "独立相机镜像不能改变输入方向。");
                Assert.Less(cameraGo.GetComponent<Camera>().projectionMatrix.m00, 0f);

                control.SetMirrorAndInput(false);
                Assert.IsFalse(_controller.IsHorizontalInputInverted);
                Assert.IsFalse(control.IsMirrored);
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
                _controller.SetHorizontalInputInverted(originalInput);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Mouse_World_RoundTrip_Matches_Current_Mirror_State()
        {
            var cameraGo = new GameObject("MirrorMouseProjectionTestCamera");
            cameraGo.SetActive(false);
            cameraGo.transform.position = new Vector3(0f, 0f, -10f);
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var control = cameraGo.AddComponent<PlayerControlManager>();
            control.player = _controller;
            cameraGo.SetActive(true);

            Vector3[] worldPoints =
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(2f, 1f, 0f),
                new Vector3(-3f, -1f, 0f)
            };

            try
            {
                for (int state = 0; state < 2; state++)
                {
                    bool mirrored = state == 1;
                    control.SetCameraMirrored(mirrored);

                    foreach (Vector3 world in worldPoints)
                    {
                        Vector3 screen = camera.WorldToScreenPoint(world);
                        Vector3 roundTrip = control.ScreenToWorldPoint(screen);

                        Assert.AreEqual(world.x, roundTrip.x, 0.001f,
                            $"镜像={mirrored}，世界点 {world} 的 X 往返不一致。");
                        Assert.AreEqual(world.y, roundTrip.y, 0.001f,
                            $"镜像={mirrored}，世界点 {world} 的 Y 往返不一致。");
                    }
                }
            }
            finally
            {
                if (control != null) control.enabled = false;
                Object.Destroy(cameraGo);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Portal_Exit_Rotates_Player_And_Camera_Then_Realigns()
        {
            float originalSpeed = _controller.portalRealignSpeed;
            _controller.portalRealignSpeed = 5f;

            // 玩家当前角度 0°，出口门 Y+ 角度 -45°，相加后玩家应为 -45°。
            _controller.TeleportWithPortalRotation(new Vector2(0f, 0.9f), -45f);
            yield return null;

            Assert.AreEqual(-45f, _controller.CurrentRotationAngle, 2f,
                "玩家角度应等于当前角度与出口门角度的和。");

            var cameraGo = new GameObject("RotateTestCamera");
            var camera = cameraGo.AddComponent<Camera>();
            camera.orthographic = true;
            var follow = cameraGo.AddComponent<CameraFollow2D>();
            follow.target = _player.transform;
            follow.offset = new Vector2(0f, 1.2f);
            follow.useLookAhead = false;
            follow.smoothTime = 0f;
            follow.SnapToTarget();
            yield return null;

            Assert.AreEqual(-45f, Mathf.DeltaAngle(0f, camera.transform.eulerAngles.z), 1f,
                "相机 Z 旋转应等于玩家旋转。");
            Vector3 expectedOffset = Quaternion.Euler(0f, 0f, -45f) * new Vector3(0f, 1.2f, 0f);
            Assert.Less(Vector3.Distance(camera.transform.position,
                _player.transform.position + expectedOffset), 0.05f,
                "相机应绕玩家旋转同样的角度，保持原来的相对偏移。");

            _controller.portalRealignSpeed = 180f;
            yield return new WaitForSeconds(0.8f);

            Assert.AreEqual(0f, _controller.CurrentRotationAngle, 2f,
                "回正动画结束后玩家应恢复竖直。");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, camera.transform.eulerAngles.z), 2f,
                "回正后相机也应恢复 0°。");

            Object.Destroy(cameraGo);
            _controller.portalRealignSpeed = originalSpeed;
        }

        [UnityTest]
        public IEnumerator Falls_Below_Limit_Respawn()
        {
            _controller.SetMoveInput(0f);
            _controller.Teleport(new Vector2(0f, -40f));

            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();

            Assert.Greater(_player.transform.position.y, -1f, "Player should respawn above the kill plane.");
        }

        [UnityTest]
        public IEnumerator Walks_Up_45_Degree_Slope()
        {
            var slope = new GameObject("TestSlope");
            slope.layer = _ground.layer;
            slope.transform.position = new Vector3(3f, 0f, 0f);
            var poly = slope.AddComponent<PolygonCollider2D>();
            poly.points = new[]
            {
                new Vector2(0f, 0f),
                new Vector2(2f, 0f),
                new Vector2(2f, 2f)
            };

            _controller.Teleport(new Vector2(1.2f, 0.86f));
            for (int i = 0; i < 5; i++)
                yield return new WaitForFixedUpdate();

            _controller.SetMoveInput(1f);
            float peakY = _player.transform.position.y;
            for (int i = 0; i < 90; i++)
            {
                yield return new WaitForFixedUpdate();
                peakY = Mathf.Max(peakY, _player.transform.position.y);
            }

            _controller.SetMoveInput(0f);
            Object.Destroy(slope);

            Debug.Log($"[Test] Slope climb peak y: {peakY:F3}");
            Assert.Greater(peakY, 1.2f,
                $"Player should climb the 45 degree slope, peak y was {peakY:F2}.");
        }
    }
}
