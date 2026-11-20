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
        public IEnumerator Jump_Reaches_Six_Cell_Height()
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
            Assert.AreEqual(6f, measured, 0.25f,
                $"Expected a 6-cell jump, measured {measured:F2} cells.");
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
