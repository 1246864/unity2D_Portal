using System.Collections;
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

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            int groundLayer = LayerMask.NameToLayer("Ground");
            Assert.AreNotEqual(-1, groundLayer,
                "Layer 'Ground' is missing. Run Platformer > Rebuild Level + Prefabs first.");

            // 与模板使用的工程级重力保持一致。
            Physics2D.gravity = new Vector2(0f, -40f);

            _ground = new GameObject("TestGround");
            _ground.layer = groundLayer;
            _ground.transform.position = new Vector3(0f, -0.5f, 0f);
            var groundCollider = _ground.AddComponent<BoxCollider2D>();
            groundCollider.size = new Vector2(100f, 1f);

#if UNITY_EDITOR
            var prefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Assets/Prefabs/Player.prefab");
#else
            GameObject prefab = null;
#endif
            Assert.IsNotNull(prefab, "Player prefab is missing. Run Platformer > Rebuild Level + Prefabs first.");

            _player = Object.Instantiate(prefab, new Vector3(0f, 0.9f, 0f), Quaternion.identity);
            _controller = _player.GetComponent<PlayerController2D>();

            // 让胶囊在地面上稳定下来。
            for (int i = 0; i < 15; i++)
                yield return new WaitForFixedUpdate();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
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
            slope.layer = LayerMask.NameToLayer("Ground");
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
