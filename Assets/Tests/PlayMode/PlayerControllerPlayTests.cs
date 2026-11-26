// 文档导航（相对于项目根目录）：
// 模块说明：.trae/documents/player-controller-modules.md
// 架构说明：.trae/documents/player-controller-architecture.md
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

            // 让玩家身体在地面上稳定下来。
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

        GameObject CreateBoxObstacle(string name, Vector2 center, Vector2 size)
        {
            var obstacle = new GameObject(name);
            obstacle.layer = _ground.layer;
            obstacle.transform.position = center;
            var collider = obstacle.AddComponent<BoxCollider2D>();
            collider.size = size;
            collider.sharedMaterial = _player.GetComponent<Collider2D>().sharedMaterial;
            Physics2D.SyncTransforms();
            return obstacle;
        }

        GameObject CreatePolygonObstacle(string name, Vector2[] points)
        {
            var obstacle = new GameObject(name);
            obstacle.layer = _ground.layer;
            var collider = obstacle.AddComponent<PolygonCollider2D>();
            collider.pathCount = 1;
            collider.SetPath(0, points);
            collider.sharedMaterial = _player.GetComponent<Collider2D>().sharedMaterial;
            Physics2D.SyncTransforms();
            return obstacle;
        }

        GameObject CreateChamferObstacle(int side)
        {
            var points = new[]
            {
                new Vector2(0f, 2.5f),
                new Vector2(0.5f, 2f),
                new Vector2(3f, 2f),
                new Vector2(3f, 6f),
                new Vector2(0f, 6f)
            };
            if (side < 0)
            {
                for (int i = 0; i < points.Length; i++)
                    points[i] = new Vector2(-points[i].x, points[i].y);
                System.Array.Reverse(points);
            }
            return CreatePolygonObstacle("Test Ceiling Chamfer", points);
        }

        [UnityTest]
        public IEnumerator Player_Stands_On_Ground()
        {
            Assert.IsTrue(_controller.IsGrounded, "Player should be grounded after settling.");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Flat_Feet_Stay_Grounded_On_Narrow_Platform_Edge()
        {
            var bodyCollider = _player.GetComponent<PolygonCollider2D>();
            Assert.IsNotNull(bodyCollider, "玩家身体应使用平底圆头 PolygonCollider2D。");
            Assert.AreEqual(1, _player.GetComponents<Collider2D>().Length,
                "玩家根节点只能有一个身体碰撞体，Center Point 应留在子节点。");
            Assert.AreEqual(1, bodyCollider.pathCount, "身体只能包含一条凸路径。");
            Vector2[] bodyPath = bodyCollider.GetPath(0);
            Assert.AreEqual(-0.36f, bodyPath[0].x, 0.0001f);
            Assert.AreEqual(-0.75f, bodyPath[0].y, 0.0001f);
            Assert.AreEqual(0.36f, bodyPath[1].x, 0.0001f);
            Assert.AreEqual(-0.75f, bodyPath[1].y, 0.0001f,
                "底边两个顶点必须直接相邻，不能插入圆角或中间点。");
            Assert.AreEqual(0.72f, bodyCollider.bounds.size.x, 0.0001f);
            Assert.AreEqual(1.5f, bodyCollider.bounds.size.y, 0.0001f);

            var groundCollider = _ground.GetComponent<BoxCollider2D>();
            groundCollider.size = new Vector2(1f, 1f);
            _player.GetComponent<Rigidbody2D>().gravityScale = 1f;
            Physics2D.SyncTransforms();

            const float supportWidth = 0.04f;
            float rootY = groundCollider.bounds.max.y - bodyPath[0].y;
            float rootX = groundCollider.bounds.max.x - supportWidth + bodyPath[1].x;
            _controller.Teleport(new Vector2(rootX, rootY));
            _controller.SetMoveInput(0f);

            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(_controller.IsGrounded, "少量平底仍在平台上时应保持接地。");
            Assert.AreEqual(rootY, _player.transform.position.y, 0.05f,
                "平底边缘支撑不应让玩家滑落。");
            Assert.AreEqual(rootX, _player.transform.position.x, 0.05f,
                "边缘接触不应把玩家横向推离平台。");
        }

        [UnityTest]
        public IEnumerator Ground_Movement_Steps_Up_Right_010()
        {
            yield return RunStepUpCase(1f, 0.1f);
        }

        [UnityTest]
        public IEnumerator Ground_Movement_Steps_Up_Left_010()
        {
            yield return RunStepUpCase(-1f, 0.1f);
        }

        [UnityTest]
        public IEnumerator Ground_Movement_Steps_Up_Right_020()
        {
            yield return RunStepUpCase(1f, 0.2f);
        }

        [UnityTest]
        public IEnumerator Ground_Movement_Steps_Up_Left_020()
        {
            yield return RunStepUpCase(-1f, 0.2f);
        }

        IEnumerator RunStepUpCase(float direction, float stepHeight)
        {
            var step = CreateBoxObstacle("Test Low Step",
                new Vector2(direction * 4f, stepHeight * 0.5f),
                new Vector2(6f, stepHeight));
            try
            {
                _controller.SetMoveInput(direction);
                for (int i = 0; i < 35; i++)
                    yield return new WaitForFixedUpdate();

                Assert.Greater(_player.transform.position.x * direction, 1.5f,
                    "玩家应通过低台阶，而不是停在原侧墙前。");
                Assert.AreEqual(0.75f + stepHeight, _player.transform.position.y, 0.08f,
                    "爬升后的脚底应落在台阶顶面。");
                Assert.IsTrue(_controller.IsGrounded);
                Assert.IsFalse(_controller.IsPhysicsActionActive,
                    "低台阶由地面玩家系统处理，不应变成跳跃。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                Object.Destroy(step);
            }
        }

        [UnityTest]
        public IEnumerator Ground_Movement_Does_Not_Step_Up_High_Wall_Right()
        {
            yield return RunHighWallRejectionCase(1f);
        }

        [UnityTest]
        public IEnumerator Ground_Movement_Does_Not_Step_Up_High_Wall_Left()
        {
            yield return RunHighWallRejectionCase(-1f);
        }

        IEnumerator RunHighWallRejectionCase(float direction)
        {
            const float wallHeight = 0.24f;
            var wall = CreateBoxObstacle("Test High Step Rejection",
                new Vector2(direction * 4f, wallHeight * 0.5f),
                new Vector2(6f, wallHeight));
            try
            {
                _controller.SetMoveInput(direction);
                for (int i = 0; i < 25; i++)
                    yield return new WaitForFixedUpdate();

                Assert.Less(_player.transform.position.x * direction, 0.9f,
                    "超过 0.2 的墙面不能自动爬升或穿过。");
                Assert.AreEqual(0.75f, _player.transform.position.y, 0.05f);
            }
            finally
            {
                _controller.SetMoveInput(0f);
                Object.Destroy(wall);
            }
        }

        [UnityTest]
        public IEnumerator Step_Up_Rejects_Insufficient_Headroom()
        {
            var step = CreateBoxObstacle("Test Step Under Ceiling",
                new Vector2(4f, 0.1f), new Vector2(6f, 0.2f));
            var ceiling = CreateBoxObstacle("Test Step Ceiling",
                new Vector2(3f, 1.65f), new Vector2(9f, 0.1f));
            try
            {
                _controller.SetMoveInput(1f);
                for (int i = 0; i < 25; i++)
                    yield return new WaitForFixedUpdate();

                Assert.Less(_player.transform.position.x, 0.9f);
                Assert.AreEqual(0.75f, _player.transform.position.y, 0.05f,
                    "爬升会碰到天花板时必须保持原位置高度。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                Object.Destroy(ceiling);
                Object.Destroy(step);
            }
        }

        [UnityTest]
        public IEnumerator Step_Up_Rejects_Blocked_Target_Space()
        {
            var step = CreateBoxObstacle("Test Step With Blocked Target",
                new Vector2(4f, 0.1f), new Vector2(6f, 0.2f));
            var wall = CreateBoxObstacle("Test Step Second Wall",
                new Vector2(1.12f, 1.2f), new Vector2(0.2f, 2f));
            try
            {
                _controller.SetMoveInput(1f);
                for (int i = 0; i < 25; i++)
                    yield return new WaitForFixedUpdate();

                Assert.Less(_player.transform.position.x, 0.9f);
                Assert.AreEqual(0.75f, _player.transform.position.y, 0.05f,
                    "台阶后的第二堵墙会占据目标空间时不能爬升。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                Object.Destroy(wall);
                Object.Destroy(step);
            }
        }

        [UnityTest]
        public IEnumerator Jump_Rounded_Head_Corrects_Right_Chamfer_And_Keeps_Rising()
        {
            yield return RunRoundedHeadChamferCase(1);
        }

        [UnityTest]
        public IEnumerator Jump_Rounded_Head_Corrects_Left_Chamfer_And_Keeps_Rising()
        {
            yield return RunRoundedHeadChamferCase(-1);
        }

        IEnumerator RunRoundedHeadChamferCase(int side)
        {
            var chamfer = CreateChamferObstacle(side);
            try
            {
                _controller.Teleport(new Vector2(-side * 0.15f, 0.75f));
                _controller.SetMoveInput(0f);
                yield return new WaitForFixedUpdate();

                float initialX = _player.transform.position.x;
                float initialY = _player.transform.position.y;
                float maxY = initialY;
                float maxCorrection = 0f;
                float previousY = initialY;
                bool corrected = false;
                bool roseAfterCorrection = false;
                _controller.JumpPressed();

                for (int i = 0; i < 18; i++)
                {
                    yield return new WaitForFixedUpdate();
                    float correction = side * (initialX - _player.transform.position.x);
                    maxCorrection = Mathf.Max(maxCorrection, correction);
                    if (correction > 0.08f) corrected = true;
                    if (corrected && _controller.Velocity.y > 0f &&
                        _player.transform.position.y > previousY + 0.001f)
                        roseAfterCorrection = true;

                    maxY = Mathf.Max(maxY, _player.transform.position.y);
                    previousY = _player.transform.position.y;
                }

                Assert.Greater(maxCorrection, 0.08f,
                    "零水平输入时，圆头倒角也应自动产生横向纠正。");
                Assert.LessOrEqual(maxCorrection,
                    _controller.headCorrectionMaxDistance + 0.02f);
                Assert.Greater(maxY, initialY + 1.5f,
                    "纠偏后仍应继续获得明显净上升高度。");
                Assert.IsTrue(roseAfterCorrection,
                    "横向纠偏之后必须继续向上运动，而不是被碰撞顶停。");
                Assert.AreEqual(-side * 0.36f, _player.transform.position.x, 0.12f,
                    "纠偏后应贴近倒角后的竖墙。");
                Assert.IsFalse(_controller.IsWallSliding);
            }
            finally
            {
                Object.Destroy(chamfer);
            }
        }

        [UnityTest]
        public IEnumerator Jump_Flat_Ceiling_Does_Not_Correct_Horizontally()
        {
            var ceiling = CreateBoxObstacle("Test Flat Ceiling",
                new Vector2(0f, 2.25f), new Vector2(5f, 0.5f));
            try
            {
                float initialX = _player.transform.position.x;
                float maxY = _player.transform.position.y;
                _controller.SetMoveInput(0f);
                _controller.JumpPressed();
                for (int i = 0; i < 12; i++)
                {
                    yield return new WaitForFixedUpdate();
                    maxY = Mathf.Max(maxY, _player.transform.position.y);
                }

                Assert.AreEqual(initialX, _player.transform.position.x, 0.02f,
                    "平天花板没有可循的倒角方向，不能擅自横移。");
                Assert.Less(maxY, 1.35f, "平天花板仍应正常阻挡上升。");
            }
            finally
            {
                Object.Destroy(ceiling);
            }
        }

        [UnityTest]
        public IEnumerator Jump_Chamfer_Rejects_Blocked_Correction_Space()
        {
            var chamfer = CreateChamferObstacle(1);
            var blocker = CreateBoxObstacle("Test Chamfer Escape Blocker",
                new Vector2(-0.65f, 4f), new Vector2(0.2f, 6f));
            try
            {
                _controller.Teleport(new Vector2(-0.15f, 0.75f));
                _controller.SetMoveInput(0f);
                yield return new WaitForFixedUpdate();
                float initialX = _player.transform.position.x;
                _controller.JumpPressed();

                for (int i = 0; i < 12; i++)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(initialX, _player.transform.position.x, 0.04f,
                    "逃逸方向被第二堵墙占据时不能横向纠正。");
                Assert.GreaterOrEqual(_player.GetComponent<Collider2D>().bounds.min.x,
                    blocker.GetComponent<Collider2D>().bounds.max.x - 0.02f,
                    "纠偏查询不能把玩家送进侧面阻挡。");
            }
            finally
            {
                Object.Destroy(blocker);
                Object.Destroy(chamfer);
            }
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

            Assert.AreEqual(13f, _controller.Velocity.x, 0.1f,
                "空中同向超速不应继续累积加速，也不应被立即压回跑速。");
            _controller.JumpReleased();
        }

        [UnityTest]
        public IEnumerator Air_Input_Approaches_Move_Speed_Without_Runaway_Acceleration()
        {
            float originalFallLimit = _controller.fallLimitY;
            _controller.fallLimitY = -100f;
            _ground.SetActive(false);
            _controller.Teleport(new Vector2(0f, 20f));
            _controller.SetMoveInput(1f);
            float startX = _player.transform.position.x;

            try
            {
                for (int i = 0; i < 15; i++)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(_controller.moveSpeed, _controller.Velocity.x, 0.05f,
                    "持续空中输入只能让速度接近目标值，不能每帧无限叠加物理加速度。");
                Assert.Greater(_player.transform.position.x - startX, 0.5f,
                    "目标速度求解仍应产生实际水平位移。");
                Assert.LessOrEqual(_player.transform.position.x - startX,
                    _controller.moveSpeed * Time.fixedDeltaTime * 15f + 0.1f,
                    "水平位移不能超过目标速度在当前时间段内产生的最大位移。");

                for (int i = 0; i < 10; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(0f, _controller.AirControlAuthority, 0.02f);
                Assert.AreEqual(_controller.moveSpeed, _controller.Velocity.x, 0.05f,
                    "到达目标速度并耗尽权限后，继续保持输入也不能再次加速。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
        }

        [UnityTest]
        public IEnumerator Jump_Air_Control_Fades_Then_Releases_Momentum()
        {
            float originalFallLimit = _controller.fallLimitY;
            _controller.fallLimitY = -100f;
            _controller.SetMoveInput(0f);
            _controller.JumpPressed();
            yield return new WaitForFixedUpdate();
            _ground.SetActive(false);

            try
            {
                for (int i = 0; i < 9; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(1f, _controller.AirControlAuthority, 0.02f,
                    "跳跃后的前 0.3 秒应保持完整方向权限。");

                for (int i = 0; i < 15; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(0.6f, _controller.AirControlAuthority, 0.08f,
                    "跳跃后 0.5 秒附近应处于 0.3～0.8 秒的线性衰减阶段。");

                var body = _player.GetComponent<Rigidbody2D>();
                float authority = _controller.AirControlAuthority;
                body.velocity = new Vector2(0f, body.velocity.y);
                _controller.SetMoveInput(1f);
                yield return new WaitForFixedUpdate();
                Assert.AreEqual(
                    _controller.airAcceleration * authority * Time.fixedDeltaTime,
                    _controller.Velocity.x, 0.15f,
                    "衰减阶段只应降低方向键调整速度的效果，不能缩放或清除已有速度。");
                _controller.SetMoveInput(0f);
                body.velocity = new Vector2(0f, body.velocity.y);

                for (int i = 0; i < 16; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(0f, _controller.AirControlAuthority, 0.02f,
                    "跳跃后 0.8 秒应完全失去方向权限。");

                body.velocity = new Vector2(6f, body.velocity.y);
                _controller.SetMoveInput(-1f);
                for (int i = 0; i < 5; i++)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(6f, _controller.Velocity.x, 0.001f,
                    "权限归零后，反向输入和主动刹车都不能消耗水平动量。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                _controller.JumpReleased();
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
        }

        [UnityTest]
        public IEnumerator Jump_Release_Input_Preserves_Air_Momentum_After_Control_Ends()
        {
            float originalFallLimit = _controller.fallLimitY;
            _controller.fallLimitY = -100f;
            _controller.SetMoveInput(1f);
            _controller.JumpPressed();
            yield return new WaitForFixedUpdate();
            _ground.SetActive(false);

            try
            {
                for (int i = 0; i < 45; i++)
                    yield return new WaitForFixedUpdate();
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "空中运动必须仍由物理动作系统接管。");
                Assert.AreEqual(0f, _controller.AirControlAuthority, 0.02f);

                float speedBeforeRelease = _controller.Velocity.x;
                Assert.Greater(Mathf.Abs(speedBeforeRelease), 0.5f,
                    "测试前必须已经形成可验证的水平动量。");
                _controller.SetMoveInput(0f);
                for (int i = 0; i < 8; i++)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(speedBeforeRelease, _controller.Velocity.x, 0.001f,
                    "物理动作系统松开方向后不应刹车或缩放已有水平速度。");
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "松开方向不能把空中运动交还玩家动作系统。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                _controller.JumpReleased();
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
        }

        [UnityTest]
        public IEnumerator Landing_Immediately_Restores_Ground_Control_And_Brakes()
        {
            _controller.SetMoveInput(1f);
            _controller.JumpPressed();
            yield return new WaitForFixedUpdate();
            _controller.JumpReleased();
            _controller.SetMoveInput(0f);

            bool landed = false;
            for (int i = 0; i < 100; i++)
            {
                yield return new WaitForFixedUpdate();
                if (!_controller.IsGrounded) continue;
                landed = true;
                break;
            }

            Assert.IsTrue(landed, "测试玩家应在短跳后重新落地。");
            Assert.IsFalse(_controller.IsPhysicsActionActive,
                "落地应立即从物理动作系统切换到玩家动作系统。");
            Assert.AreEqual(1f, _controller.AirControlAuthority,
                "落地必须立即恢复完整玩家控制。");

            for (int i = 0; i < 8; i++)
                yield return new WaitForFixedUpdate();
            Assert.Less(Mathf.Abs(_controller.Velocity.x), 0.5f,
                "落地后应立即使用地面减速，不能继续按空中外来动量长距离滑行。");
        }

        [UnityTest]
        public IEnumerator Direct_Fall_Loses_Control_After_Point_Three_Seconds()
        {
            float originalFallLimit = _controller.fallLimitY;
            _controller.fallLimitY = -100f;
            _ground.SetActive(false);
            _controller.Teleport(new Vector2(0f, 20f));
            _controller.SetMoveInput(0f);

            try
            {
                for (int i = 0; i < 15; i++)
                    yield return new WaitForFixedUpdate();
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "直接离地后必须进入物理动作系统。");
                Assert.AreEqual(1f, _controller.AirControlAuthority, 0.02f,
                    "非跳跃离地在 0.3 秒时仍应保留完整权限。");

                for (int i = 0; i < 2; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(0f, _controller.AirControlAuthority,
                    "非跳跃离地超过 0.3 秒应立即失去方向权限。");

                var body = _player.GetComponent<Rigidbody2D>();
                body.velocity = new Vector2(5f, body.velocity.y);
                _controller.SetMoveInput(-1f);
                for (int i = 0; i < 3; i++)
                    yield return new WaitForFixedUpdate();

                Assert.AreEqual(5f, _controller.Velocity.x, 0.001f,
                    "直接坠落失去权限后，水平速度应完全交给物理动量。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
        }

        [UnityTest]
        public IEnumerator Portal_Exit_Momentum_Continues_After_Control_Ends()
        {
            float originalFallLimit = _controller.fallLimitY;
            _controller.fallLimitY = -100f;
            _ground.SetActive(false);
            var body = _player.GetComponent<Rigidbody2D>();
            body.gravityScale = 0f;
            _controller.TeleportWithPortalRotation(_player.transform.position, 0f);
            _controller.SetPortalExitVelocity(new Vector2(30f, 0f));
            _controller.SetMoveInput(0f);
            float startX = _player.transform.position.x;

            try
            {
                for (int i = 0; i < 25; i++)
                    yield return new WaitForFixedUpdate();

                float speedAfterControl = _controller.Velocity.x;
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "传送出口后的空中动量必须由物理动作系统接管。");
                Assert.AreEqual(0f, _controller.AirControlAuthority, 0.02f,
                    "普通空中权限归零后仍应保持出口动量。");
                Assert.Greater(speedAfterControl, 28f,
                    "出口高速动量不能在几个世界单位内被普通空中刹车归零。");
                Assert.Greater(_player.transform.position.x - startX, 13.5f,
                    "出口后的前 0.5 秒应持续飞行，而不是提前停下。");

                for (int i = 0; i < 20; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(speedAfterControl, _controller.Velocity.x, 0.05f,
                    "方向权限归零后不应继续消耗水平动量。");
            }
            finally
            {
                _controller.SetMoveInput(0f);
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
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

            var playerMaterial = _player.GetComponent<Collider2D>().sharedMaterial;
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
        public IEnumerator Falling_Uses_Native_Gravity_Until_Max_Fall_Speed()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            _controller.Teleport(new Vector2(0f, 20f));
            _controller.SetMoveInput(0f);
            var body = _player.GetComponent<Rigidbody2D>();

            try
            {
                for (int i = 0; i < 4; i++)
                    yield return new WaitForFixedUpdate();

                float gravity = Mathf.Abs(Physics2D.gravity.y * body.gravityScale);
                float expectedSpeed = gravity * Time.fixedDeltaTime * 4f;
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "自由下落必须由物理动作系统接管。");
                Assert.AreEqual(expectedSpeed, -_controller.Velocity.y, 0.25f,
                    "达到上限前，下落加速度应等于 Physics2D 重力乘刚体重力缩放。");

                for (int i = 0; i < 20; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(_controller.maxFallSpeed,
                    -_controller.Velocity.y, 0.35f,
                    "持续下落达到 maxFallSpeed 后应保持最大下落速度。");
            }
            finally
            {
                _controller.fallLimitY = originalFallLimit;
                _ground.SetActive(true);
            }
        }

        [UnityTest]
        public IEnumerator High_Speed_Fall_Is_Capped_At_Max_Fall_Speed()
        {
            float originalFallLimit = _controller.fallLimitY;
            _controller.fallLimitY = -100f;
            _ground.SetActive(false);
            var body = _player.GetComponent<Rigidbody2D>();
            _controller.Teleport(new Vector2(0f, 20f));
            body.velocity = new Vector2(0f, -80f);

            try
            {
                yield return new WaitForFixedUpdate();
                float firstSpeed = -_controller.Velocity.y;
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "高速下落不能被截停或交还玩家系统。");
                Assert.AreEqual(_controller.maxFallSpeed, firstSpeed, 0.35f,
                    "超过上限的外部下落速度应被物理系统截到最大值。");

                for (int i = 0; i < 9; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(_controller.maxFallSpeed,
                    -_controller.Velocity.y, 0.35f,
                    "持续高速下落应保持配置的最大速度。");
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
                Assert.IsTrue(_controller.HasWallContact, "普通墙滑必须来自真实地形墙接触。");
                Assert.IsFalse(_controller.IsPhysicsActionActive,
                    "低速贴墙应交给玩家动作系统执行普通墙滑。");
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
                Assert.IsFalse(_controller.IsPhysicsActionActive,
                    "连续普通墙滑恢复到高速后也不能反复跳回物理系统。");

                _controller.SetMoveInput(0f);
                yield return new WaitForFixedUpdate();
                Assert.IsFalse(_controller.IsWallSliding, "松开推墙方向应立即退出贴墙下落。");
                yield return new WaitForFixedUpdate();
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "解除连续墙滑锁存后，高速下落应重新交给物理系统。");
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
        public IEnumerator High_Speed_Wall_Contact_Without_Push_Preserves_Physics_Fall()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            var wall = new GameObject("TestHighSpeedWallCoast");
            wall.layer = _ground.layer;
            wall.transform.position = new Vector3(1f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 100f);

            try
            {
                _controller.Teleport(new Vector2(0.14f, 10f));
                var body = _player.GetComponent<Rigidbody2D>();
                body.velocity = new Vector2(0f, -40f);
                _controller.SetMoveInput(0f);
                yield return new WaitForFixedUpdate();

                float firstSpeed = -_controller.Velocity.y;
                Assert.IsTrue(_controller.HasWallContact, "测试玩家应已接触真实墙面。");
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "高速贴墙但未推墙时必须保持物理动作系统。");
                Assert.IsFalse(_controller.IsWallSliding,
                    "高速贴墙本身不能触发玩家墙滑。");

                for (int i = 0; i < 5; i++)
                    yield return new WaitForFixedUpdate();
                Assert.AreEqual(_controller.maxFallSpeed,
                    -_controller.Velocity.y, 0.35f,
                    "高速贴墙不推墙时仍应遵循物理系统的最大下落速度。");
                Assert.IsTrue(_controller.IsPhysicsActionActive);
                Assert.IsFalse(_controller.IsWallSliding);
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
        public IEnumerator High_Speed_Wall_Push_Brakes_Then_Hands_Off_To_Player()
        {
            float originalFallLimit = _controller.fallLimitY;
            _ground.SetActive(false);
            _controller.fallLimitY = -100f;
            var wall = new GameObject("TestHighSpeedWallBrake");
            wall.layer = _ground.layer;
            wall.transform.position = new Vector3(1f, 0f, 0f);
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 100f);

            try
            {
                _controller.Teleport(new Vector2(0.14f, 10f));
                var body = _player.GetComponent<Rigidbody2D>();
                body.velocity = new Vector2(0f, -40f);
                _controller.SetMoveInput(1f);
                yield return new WaitForFixedUpdate();

                float firstBrakedSpeed = -_controller.Velocity.y;
                Assert.Less(firstBrakedSpeed, 38f,
                    "高速推墙第一步就应产生超过原生重力反向作用的明显制动。");
                Assert.IsTrue(_controller.IsPhysicsActionActive,
                    "高速制动期间仍由物理动作系统独占运动。");
                Assert.IsFalse(_controller.IsWallSliding,
                    "高速制动不能同时执行玩家普通墙滑。");

                for (int i = 0; i < 2; i++)
                    yield return new WaitForFixedUpdate();
                Assert.Less(-_controller.Velocity.y, firstBrakedSpeed - 5f,
                    "持续推墙应继续以可观速度降低下落速度。");
                Assert.Greater(-_controller.Velocity.y, _controller.wallSlideStartSpeed,
                    "交接前测试应仍处于高速物理制动阶段。");
                Assert.IsTrue(_controller.IsPhysicsActionActive);

                for (int i = 0; i < 20 && _controller.IsPhysicsActionActive; i++)
                    yield return new WaitForFixedUpdate();

                Assert.IsFalse(_controller.IsPhysicsActionActive,
                    "降到低速后必须从物理系统交还玩家系统。");
                Assert.IsTrue(_controller.HasWallContact);
                Assert.IsTrue(_controller.IsWallSliding,
                    "持续推墙交接后应进入普通玩家墙滑。");
                Assert.Less(Mathf.Abs(_controller.Velocity.y),
                    _controller.wallSlideStartSpeed + 0.5f,
                    "系统交接应发生在约定的低速阈值附近。");
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
            Assert.AreEqual(Quaternion.identity, _player.transform.rotation,
                "传送门只能旋转显示子节点，物理根节点不能旋转。");
            Assert.AreEqual(0f, _player.GetComponent<Rigidbody2D>().rotation, 0.0001f);
            var bodyCollider = _player.GetComponent<PolygonCollider2D>();
            Vector2[] originalBodyPath = bodyCollider.GetPath(0);
            Assert.AreEqual(0.72f, bodyCollider.bounds.size.x, 0.0001f);
            Assert.AreEqual(1.5f, bodyCollider.bounds.size.y, 0.0001f);

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
            Assert.AreEqual(Quaternion.identity, _player.transform.rotation);
            Assert.AreEqual(0f, _player.GetComponent<Rigidbody2D>().rotation, 0.0001f);
            CollectionAssert.AreEqual(originalBodyPath, bodyCollider.GetPath(0),
                "显示回正不能改写身体碰撞形状。");
            Assert.AreEqual(0.72f, bodyCollider.bounds.size.x, 0.0001f);
            Assert.AreEqual(1.5f, bodyCollider.bounds.size.y, 0.0001f);

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
