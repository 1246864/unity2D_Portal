using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Platformer.Tests
{
    /// <summary>
    /// Play-mode tests for the character controller and camera.
    /// Run from the Test Runner window, or in batch:
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

            // Match the project-wide gravity used by the template.
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

            // Let the capsule settle on the ground.
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

            // Batch mode renders very fast, so wait in game time rather than frames.
            yield return new WaitForSeconds(1.5f);
            yield return null;

            Vector3 expected = _player.transform.position + new Vector3(0f, 1.2f, -10f);
            float error = Vector3.Distance(cameraGo.transform.position, expected);

            Object.Destroy(cameraGo);

            Debug.Log($"[Test] Camera follow error: {error:F3} units");
            Assert.Less(error, 1.5f, $"Camera should stay close to the player, error was {error:F2}.");
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
