// 文档导航（相对于项目根目录）：
// 模块说明：.trae/documents/player-controller-modules.md
// 架构说明：.trae/documents/player-controller-architecture.md
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Platformer.Tests
{
    /// <summary>
    /// 传送门的 PlayMode 测试：薄碰撞箱尺寸、碰撞传送、背面隐藏。
    /// </summary>
    public class PortalTeleportTests
    {
        PortalTestRig _rig;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = PortalTestRig.Create(withProjectileVisuals: true);
            yield return null;
            yield return new WaitForFixedUpdate();
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_rig != null) _rig.Destroy();
            yield return null;
        }

        GameObject FindPlayer()
        {
            return _rig.Player.gameObject;
        }

        void FindGroups(out CaptureDisplayGroup a, out CaptureDisplayGroup b)
        {
            a = _rig.A;
            b = _rig.B;
        }

        static BoxCollider2D CreatePlacementSurface(
            Transform parent, string name, Vector2 position, Vector2 size, float angle = 0f)
        {
            GameObject surface = PlayTestFixtures.CreateChild(parent, name);
            surface.layer = 6;
            surface.transform.SetPositionAndRotation(
                position, Quaternion.Euler(0f, 0f, angle));
            var collider = surface.AddComponent<BoxCollider2D>();
            collider.size = size;
            return collider;
        }

        static PortalProjectile[] GetActiveProjectiles(PortalProjectileSystem system)
        {
            return system.GetComponentsInChildren<PortalProjectile>(false);
        }

        static PortalProjectile GetSingleProjectile(PortalProjectileSystem system)
        {
            PortalProjectile[] active = GetActiveProjectiles(system);
            Assert.AreEqual(1, active.Length, "应只有一个活动子弹。");
            return active[0];
        }

        static PortalProjectile FindNewProjectile(
            PortalProjectileSystem system,
            params PortalProjectile[] known)
        {
            PortalProjectile[] active = GetActiveProjectiles(system);
            Assert.AreEqual(known.Length + 1, active.Length);
            foreach (PortalProjectile candidate in active)
            {
                if (System.Array.IndexOf(known, candidate) < 0) return candidate;
            }
            Assert.Fail("没有找到本次发射创建的子弹。");
            return null;
        }

        static void AssertColor(Color actual, Color expected)
        {
            Assert.AreEqual(expected.r, actual.r, 0.01f);
            Assert.AreEqual(expected.g, actual.g, 0.01f);
            Assert.AreEqual(expected.b, actual.b, 0.01f);
            Assert.AreEqual(expected.a, actual.a, 0.01f);
        }

        static void AssertProjectileColor(PortalProjectile projectile, Color expected)
        {
            Color trail = Color.Lerp(expected, Color.white, Mathf.Clamp01(projectile.trailColorBrighten));
            trail.a = expected.a;
            AssertColor(projectile.trailParticles.main.startColor.color, trail);
            AssertColor(projectile.residualParticles.main.startColor.color, expected);
            AssertColor(projectile.impactParticles.main.startColor.color, expected);
        }

        static IEnumerator WaitForArrival(PortalProjectile projectile, int maxFrames = 240)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (projectile.HasArrived) yield break;
                yield return null;
            }
            Assert.Fail("子弹未在限定帧数内抵达终点。");
        }

        static IEnumerator WaitForImpact(PortalProjectile projectile, int maxFrames = 120)
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (projectile.impactParticles.particleCount > 0) yield break;
                yield return null;
            }
            Assert.Fail("撞墙粒子未在限定帧数内发射。");
        }

        static IEnumerator WaitForAllProjectilesReleased(
            PortalProjectileSystem system,
            int maxFrames = 10000,
            string stage = "")
        {
            for (int frame = 0; frame < maxFrames; frame++)
            {
                if (system.ActiveProjectileCount == 0) yield break;
                yield return null;
            }

            string details = stage + $" 活动数={system.ActiveProjectileCount}";
            foreach (PortalProjectile projectile in GetActiveProjectiles(system))
            {
                details += $"；arrived={projectile.HasArrived}" +
                           $"，trailAlive={projectile.trailParticles.IsAlive(true)}" +
                           $"，residualAlive={projectile.residualParticles.IsAlive(true)}" +
                           $"，impactAlive={projectile.impactParticles.IsAlive(true)}" +
                           $"，trailPlaying={projectile.trailParticles.isPlaying}" +
                           $"，impactPlaying={projectile.impactParticles.isPlaying}" +
                           $"，trailCount={projectile.trailParticles.particleCount}" +
                           $"，impactCount={projectile.impactParticles.particleCount}";
            }
            Assert.Fail("子弹未在粒子结束后归还对象池。" + details);
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
        public IEnumerator Placement_Moves_Only_Target_And_Replays_Open_Animation()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            CreatePlacementSurface(
                _rig.Root.transform, "Placement Wall",
                new Vector2(-5f, 4f), new Vector2(1f, 4f));
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector2 mouse = origin + Vector2.right * 0.2f;
            Vector3 oldBPosition = b.transform.position;
            Quaternion oldBRotation = b.transform.rotation;
            int oldACount = a.OpenAnimationCount;
            int oldBCount = b.OpenAnimationCount;

            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.A, origin, mouse));
            Assert.AreEqual(-5.6f, a.transform.position.x, 0.001f);
            Assert.AreEqual(4f, a.transform.position.y, 0.001f);
            Assert.AreEqual(oldACount + 1, a.OpenAnimationCount);
            Assert.AreEqual(oldBCount, b.OpenAnimationCount);
            Assert.AreEqual(oldBPosition, b.transform.position);
            Assert.AreEqual(oldBRotation, b.transform.rotation);

            Vector3 placedAPosition = a.transform.position;
            int placedACount = a.OpenAnimationCount;
            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.B, origin, mouse));
            Assert.AreEqual(-5.6f, b.transform.position.x, 0.001f);
            Assert.AreEqual(oldBCount + 1, b.OpenAnimationCount);
            Assert.AreEqual(placedAPosition, a.transform.position);
            Assert.AreEqual(placedACount, a.OpenAnimationCount);

            a.StopOpenAnimation();
            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.A, origin, mouse));
            Assert.AreEqual(placedACount + 1, a.OpenAnimationCount);
            Assert.AreEqual(oldBCount + 1, b.OpenAnimationCount);
            Assert.AreEqual(0f, a.maskTop.localPosition.y, 0.0001f);
            Assert.AreEqual(0f, a.maskBottom.localPosition.y, 0.0001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Uses_Nearest_Terrain_And_Ignores_Trigger_And_Gates()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            BoxCollider2D trigger = CreatePlacementSurface(
                _rig.Root.transform, "Ignored Trigger",
                new Vector2(-8.5f, 4f), new Vector2(0.2f, 4f));
            trigger.isTrigger = true;
            CreatePlacementSurface(
                _rig.Root.transform, "Near Wall",
                new Vector2(-6f, 4f), new Vector2(1f, 4f));
            CreatePlacementSurface(
                _rig.Root.transform, "Far Wall",
                new Vector2(-3f, 4f), new Vector2(1f, 4f));
            a.transform.position = new Vector3(-8f, 4f, 0f);
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.B, origin, origin + Vector2.right * 0.2f));
            Assert.AreEqual(-6.6f, b.transform.position.x, 0.001f,
                "鼠标只决定方向，应选择最近的真实地形，而不是 Trigger、门或远端墙。");
            Assert.AreEqual(4f, b.transform.position.y, 0.001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Skips_Player_Colliders()
        {
            PortalPair pair = _rig.Pair;
            _rig.Player.gameObject.layer = 6;
            _rig.Player.CenterPoint.gameObject.layer = 6;
            CreatePlacementSurface(
                _rig.Root.transform, "Wall Behind Player",
                new Vector2(-6f, 4f), new Vector2(1f, 4f));
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.A, origin, origin + Vector2.right));
            Assert.AreEqual(-6.6f, _rig.A.transform.position.x, 0.001f,
                "即使玩家碰撞体被加入查询层，也必须跳过自身并命中后面的墙。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Rejects_Zero_Direction_Without_Placement_Or_Projectile()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector3 oldAPosition = a.transform.position;
            Vector3 oldBPosition = b.transform.position;
            Quaternion oldARotation = a.transform.rotation;
            Quaternion oldBRotation = b.transform.rotation;
            int oldACount = a.OpenAnimationCount;
            int oldBCount = b.OpenAnimationCount;

            Assert.IsFalse(pair.TryPlacePortal(CaptureGroupSide.A, origin, origin));
            Assert.AreEqual(oldAPosition, a.transform.position);
            Assert.AreEqual(oldBPosition, b.transform.position);
            Assert.AreEqual(oldARotation, a.transform.rotation);
            Assert.AreEqual(oldBRotation, b.transform.rotation);
            Assert.AreEqual(oldACount, a.OpenAnimationCount);
            Assert.AreEqual(oldBCount, b.OpenAnimationCount);
            Assert.AreEqual(0, _rig.ProjectileSystem.ActiveProjectileCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Aligns_Opening_With_Wall_Ground_Ceiling_And_Slope()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            pair.placementSurfaceOffset = 0.25f;
            CreatePlacementSurface(
                _rig.Root.transform, "Right Wall",
                new Vector2(-5f, 4f), new Vector2(1f, 4f));
            CreatePlacementSurface(
                _rig.Root.transform, "Ground",
                new Vector2(-10f, 0f), new Vector2(4f, 1f));
            CreatePlacementSurface(
                _rig.Root.transform, "Ceiling",
                new Vector2(-10f, 8f), new Vector2(4f, 1f));
            CreatePlacementSurface(
                _rig.Root.transform, "Rotated Slope",
                new Vector2(-15f, 4f), new Vector2(2f, 2f), 30f);
            a.transform.position = new Vector3(a.transform.position.x, a.transform.position.y, 1.5f);
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector2[] directions = { Vector2.right, Vector2.down, Vector2.up, Vector2.left };
            foreach (Vector2 direction in directions)
            {
                RaycastHit2D expected = Physics2D.Raycast(
                    origin, direction, Mathf.Infinity, 1 << 6);
                Assert.IsNotNull(expected.collider, "测试表面未按预期进入射线。");

                Assert.IsTrue(pair.TryPlacePortal(
                    CaptureGroupSide.A, origin, origin + direction * 0.2f));
                Vector2 normal = expected.normal.normalized;
                PortalFacing expectedFacing = normal.x < 0f
                    ? PortalFacing.Left : PortalFacing.Right;
                float normalAngle = Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg;
                float expectedAngle = expectedFacing == PortalFacing.Left
                    ? normalAngle - 180f : normalAngle;

                Vector2 expectedPosition = expected.point + normal * pair.placementSurfaceOffset;
                Assert.AreEqual(expectedPosition.x, a.transform.position.x, 0.001f);
                Assert.AreEqual(expectedPosition.y, a.transform.position.y, 0.001f);
                Assert.AreEqual(1.5f, a.transform.position.z, 0.001f);
                Assert.AreEqual(expectedFacing, a.Facing);
                Assert.AreEqual(0f, Mathf.DeltaAngle(
                    expectedAngle, a.transform.eulerAngles.z), 0.01f);
                Assert.AreEqual(1f, Vector2.Dot(a.OpeningDirection, normal), 0.001f);
                Assert.AreEqual(-1f, Vector2.Dot(a.BackDirection, normal), 0.001f);
            }
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Hit_Starts_Placement_And_Concurrent_Colored_Projectiles()
        {
            PortalPair pair = _rig.Pair;
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.projectileSpeed = 2f;
            CreatePlacementSurface(
                _rig.Root.transform, "Projectile Target Wall",
                new Vector2(-5f, 4f), new Vector2(1f, 4f));
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector2 mouse = origin + Vector2.right * 0.2f;
            Color blue = new Color(0.35f, 0.65f, 1f, 1f);
            Color red = new Color(1f, 0.35f, 0.35f, 1f);

            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.A, origin, mouse));
            Assert.AreEqual(-5.6f, _rig.A.transform.position.x, 0.001f,
                "放门必须在调用当帧完成，不能等待子弹。 ");
            PortalProjectile projectileA1 = GetSingleProjectile(system);
            Assert.IsTrue(projectileA1.IsFlying);
            Assert.AreEqual(origin, projectileA1.StartPoint);
            Assert.Less(Vector2.Distance(new Vector2(-5.5f, 4f), projectileA1.EndPoint), 0.001f);
            Assert.IsTrue(projectileA1.EmitImpactOnArrival);
            AssertProjectileColor(projectileA1, blue);

            Vector2 secondOrigin = origin + Vector2.up * 0.5f;
            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.A, secondOrigin, secondOrigin + Vector2.right * 0.2f));
            PortalProjectile projectileA2 = FindNewProjectile(system, projectileA1);
            Assert.AreNotSame(projectileA1, projectileA2, "同侧连射必须同时使用不同实例。");
            Assert.Less(Vector2.Distance(new Vector2(-5.5f, 4.5f), projectileA2.EndPoint), 0.001f);
            AssertProjectileColor(projectileA2, blue);

            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.B, origin, mouse));
            PortalProjectile projectileB = FindNewProjectile(system, projectileA1, projectileA2);
            Assert.AreEqual(-5.6f, _rig.B.transform.position.x, 0.001f);
            Assert.Less(Vector2.Distance(new Vector2(-5.5f, 4f), projectileB.EndPoint), 0.001f);
            AssertProjectileColor(projectileB, red);
            Assert.AreEqual(3, system.ActiveProjectileCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Failure_Does_Not_Block_Projectile_Or_Impact()
        {
            PortalPair pair = _rig.Pair;
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.projectileSpeed = 2000f;
            CreatePlacementSurface(
                _rig.Root.transform, "Failed Placement Wall",
                new Vector2(-5f, 4f), new Vector2(1f, 4f));
            Physics2D.SyncTransforms();
            Vector2 origin = _rig.Player.CenterPoint.transform.position;

            Object.Destroy(_rig.A.gameObject);
            yield return null;

            Assert.IsFalse(pair.TryPlacePortal(
                CaptureGroupSide.A, origin, origin + Vector2.right * 0.2f));
            PortalProjectile projectile = GetSingleProjectile(system);
            Assert.Less(Vector2.Distance(new Vector2(-5.5f, 4f), projectile.EndPoint), 0.001f);
            Assert.IsTrue(projectile.EmitImpactOnArrival);

            yield return WaitForArrival(projectile);
            yield return WaitForImpact(projectile);
            Assert.Greater(projectile.impactParticles.particleCount, 0,
                "放门失败不能撤销有效命中对应的撞墙粒子。 ");
        }

        [UnityTest]
        public IEnumerator Placement_Tile_Permission_Controls_Placement_And_Preserves_Failure_Workflow()
        {
            _rig.Destroy();
            _rig = PortalTestRig.Create(withTerrain: true, withProjectileVisuals: true);
            yield return null;
            yield return new WaitForFixedUpdate();

            var forbiddenTile = _rig.Terrain.CreateSolidTile(false);
            _rig.Terrain.FillSolidRect(forbiddenTile, -6, -6, 2, 5);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector3 oldPosition = _rig.A.transform.position;
            Quaternion oldRotation = _rig.A.transform.rotation;
            PortalFacing oldFacing = _rig.A.Facing;
            int oldOpenCount = _rig.A.OpenAnimationCount;

            Assert.IsFalse(_rig.Pair.TryPlacePortal(
                CaptureGroupSide.A, origin, origin + Vector2.right));
            Assert.AreEqual(oldPosition, _rig.A.transform.position);
            Assert.AreEqual(oldRotation, _rig.A.transform.rotation);
            Assert.AreEqual(oldFacing, _rig.A.Facing);
            Assert.AreEqual(oldOpenCount, _rig.A.OpenAnimationCount);

            PortalProjectile projectile = GetSingleProjectile(_rig.ProjectileSystem);
            Assert.Less(Vector2.Distance(new Vector2(-6f, 4f), projectile.EndPoint), 0.001f);
            Assert.IsTrue(projectile.EmitImpactOnArrival,
                "不可放置只拦截开门，不应撤销有效地形命中的撞墙表现。");

            var allowedTile = _rig.Terrain.CreateSolidTile(true);
            _rig.Terrain.FillSolidRect(allowedTile, -6, -6, 2, 5);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();

            Assert.IsTrue(_rig.Pair.TryPlacePortal(
                CaptureGroupSide.B, origin, origin + Vector2.right));
            Assert.AreEqual(-6.1f, _rig.B.transform.position.x, 0.001f);
            Assert.AreEqual(4f, _rig.B.transform.position.y, 0.001f);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Tile_Wall_Requires_Full_Height_And_Offsets_From_Corner()
        {
            _rig.Destroy();
            _rig = PortalTestRig.Create(withTerrain: true, withProjectileVisuals: true);
            yield return null;
            yield return new WaitForFixedUpdate();

            PortalPair pair = _rig.Pair;
            pair.maskTopOffset = 2f;
            pair.maskBottomOffset = -2f;
            var allowedTile = _rig.Terrain.CreateSolidTile(true);
            _rig.Terrain.FillSolidRect(allowedTile, -6, -6, 4, 6);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();

            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector3 oldPosition = _rig.A.transform.position;
            Assert.IsFalse(pair.TryPlacePortal(
                CaptureGroupSide.A, origin, origin + Vector2.right));
            Assert.AreEqual(oldPosition, _rig.A.transform.position,
                "连续墙面只有 3 格、门高为 4 时必须放置失败。");
            PortalProjectile shortWallProjectile = GetSingleProjectile(_rig.ProjectileSystem);
            Assert.Less(Vector2.Distance(
                new Vector2(-6f, 4f), shortWallProjectile.EndPoint), 0.001f);

            _rig.Terrain.FillSolidRect(allowedTile, -6, -6, 7, 7);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();

            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.B, origin, origin + Vector2.right));
            Assert.AreEqual(-6.1f, _rig.B.transform.position.x, 0.001f);
            Assert.AreEqual(6f, _rig.B.transform.position.y, 0.001f,
                "命中 4 格墙的底角时，门应向上偏移到完整墙面内。");
            PortalProjectile cornerProjectile = FindNewProjectile(
                _rig.ProjectileSystem, shortWallProjectile);
            Assert.Less(Vector2.Distance(
                new Vector2(-6f, 4f), cornerProjectile.EndPoint), 0.001f,
                "自动偏移只改变门的位置，不改变子弹实际命中点。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Tile_Slope_Scans_Tangent_And_Offsets_On_Same_Normal()
        {
            _rig.Destroy();
            _rig = PortalTestRig.Create(withTerrain: true);
            yield return null;
            yield return new WaitForFixedUpdate();

            PortalPair pair = _rig.Pair;
            pair.maskTopOffset = 2f;
            pair.maskBottomOffset = -2f;
            var slopeTile = _rig.Terrain.CreateSolidTile(true);
            slopeTile.sprite.OverridePhysicsShape(new[]
            {
                new[]
                {
                    new Vector2(32f, 0f), new Vector2(32f, 32f), new Vector2(0f, 32f)
                }
            });
            _rig.Terrain.Tilemap.SetTile(new Vector3Int(0, 0, 0), slopeTile);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();

            Vector2 origin = new Vector2(-2f, -2f);
            Vector2 hitPoint = new Vector2(0.9f, 0.1f);
            Vector3 oldPosition = _rig.A.transform.position;
            Assert.IsFalse(pair.TryPlacePortal(CaptureGroupSide.A, origin, hitPoint),
                "单格斜边短于门高时，不能因为格子存在就放置成功。");
            Assert.AreEqual(oldPosition, _rig.A.transform.position);

            for (int i = 1; i < 4; i++)
                _rig.Terrain.Tilemap.SetTile(new Vector3Int(-i, i, 0), slopeTile);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();
            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.B, origin, hitPoint));

            Vector2 normal = new Vector2(-1f, -1f).normalized;
            Vector2 tangent = new Vector2(-normal.y, normal.x);
            Vector2 segmentEnd = new Vector2(1f, 0f);
            Vector2 expectedSurface = segmentEnd - tangent * (pair.PortalHeight * 0.5f);
            Vector2 expectedPosition = expectedSurface + normal * pair.placementSurfaceOffset;
            Assert.AreEqual(expectedPosition.x, _rig.B.transform.position.x, 0.001f,
                "门中心应沿命中斜面的切线方向向连续边内部偏移。");
            Assert.AreEqual(expectedPosition.y, _rig.B.transform.position.y, 0.001f);
            Assert.AreEqual(1f, Vector2.Dot(_rig.B.OpeningDirection, normal), 0.001f,
                "斜面放置必须保留实际命中面的法线，不能吸附到竖墙。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator Disabled_Projectile_System_Does_Not_Block_Placement()
        {
            PortalPair pair = _rig.Pair;
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.enabled = false;
            CreatePlacementSurface(
                _rig.Root.transform, "Placement Without Visuals Wall",
                new Vector2(-5f, 4f), new Vector2(1f, 4f));
            Physics2D.SyncTransforms();
            Vector2 origin = _rig.Player.CenterPoint.transform.position;

            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.A, origin, origin + Vector2.right * 0.2f));
            Assert.AreEqual(-5.6f, _rig.A.transform.position.x, 0.001f);
            Assert.AreEqual(0, system.ActiveProjectileCount);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Ray_Is_Not_Limited_By_Miss_Visual_Distance()
        {
            PortalPair pair = _rig.Pair;
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.projectileSpeed = 2f;
            _rig.MainCamera.orthographicSize = 0.05f;
            _rig.MainCamera.aspect = 1f;
            CreatePlacementSurface(
                _rig.Root.transform, "Far Placement Wall",
                new Vector2(20f, 4f), new Vector2(1f, 4f));
            Physics2D.SyncTransforms();
            Vector2 origin = _rig.Player.CenterPoint.transform.position;

            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.A, origin, origin + Vector2.right * 0.2f));
            Assert.AreEqual(19.4f, _rig.A.transform.position.x, 0.001f);
            PortalProjectile projectile = GetSingleProjectile(system);
            Assert.Less(Vector2.Distance(new Vector2(19.5f, 4f), projectile.EndPoint), 0.001f);
            float missDistance = 4f * Mathf.Sqrt(2f) * 0.05f;
            Assert.Greater(Vector2.Distance(origin, projectile.EndPoint), missDistance);
            yield return null;
        }

        [UnityTest]
        public IEnumerator Placement_Miss_Launches_Two_Screen_Diagonals_Without_Impact()
        {
            PortalPair pair = _rig.Pair;
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.projectileSpeed = 5000f;
            _rig.MainCamera.orthographicSize = 3f;
            _rig.MainCamera.aspect = 1.5f;
            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Vector3 oldAPosition = _rig.A.transform.position;
            Vector3 oldBPosition = _rig.B.transform.position;
            int oldACount = _rig.A.OpenAnimationCount;
            int oldBCount = _rig.B.OpenAnimationCount;

            Assert.IsFalse(pair.TryPlacePortal(
                CaptureGroupSide.B, origin, origin + Vector2.up * 0.2f));
            PortalProjectile projectile = GetSingleProjectile(system);
            float expectedDistance = 4f * Mathf.Sqrt(3f * 3f + 4.5f * 4.5f);
            Vector2 expectedEnd = origin + Vector2.up * expectedDistance;
            Assert.AreEqual(expectedEnd.x, projectile.EndPoint.x, 0.001f);
            Assert.AreEqual(expectedEnd.y, projectile.EndPoint.y, 0.001f);
            Assert.IsFalse(projectile.EmitImpactOnArrival);
            Assert.AreEqual(oldAPosition, _rig.A.transform.position);
            Assert.AreEqual(oldBPosition, _rig.B.transform.position);
            Assert.AreEqual(oldACount, _rig.A.OpenAnimationCount);
            Assert.AreEqual(oldBCount, _rig.B.OpenAnimationCount);

            yield return WaitForArrival(projectile);
            Assert.AreEqual(0, projectile.impactParticles.particleCount);
            Assert.IsFalse(projectile.impactParticles.isPlaying,
                "miss 只保留沿途轨迹，不能播放撞墙粒子。 ");
            yield return WaitForAllProjectilesReleased(system);
        }

        [UnityTest]
        public IEnumerator Projectile_Pool_Reuses_Instance_Across_Colors_Hit_And_Miss()
        {
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.projectileSpeed = 2000f;
            Vector2 origin = new Vector2(40f, 40f);
            Vector2 endPoint = origin + Vector2.right * 0.2f;
            Color blue = new Color(0.35f, 0.65f, 1f, 1f);
            Color red = new Color(1f, 0.35f, 0.35f, 1f);
            Assert.AreEqual(2, system.IdleProjectileCount, "夹具应预热两个实例。");

            system.Launch(CaptureGroupSide.A, origin, endPoint, true);
            PortalProjectile projectile = GetSingleProjectile(system);
            Assert.AreEqual(1, system.IdleProjectileCount);
            AssertProjectileColor(projectile, blue);
            yield return WaitForAllProjectilesReleased(system, stage: "首次 hit");
            Assert.AreEqual(2, system.IdleProjectileCount);
            Assert.IsFalse(projectile.gameObject.activeSelf);

            system.Launch(CaptureGroupSide.B, origin, endPoint, false);
            Assert.AreSame(projectile, GetSingleProjectile(system), "最近归还的实例应被复用。");
            Assert.IsFalse(projectile.EmitImpactOnArrival);
            AssertProjectileColor(projectile, red);
            Assert.AreEqual(0, projectile.trailParticles.particleCount);
            Assert.AreEqual(0, projectile.residualParticles.particleCount);
            Assert.AreEqual(0, projectile.impactParticles.particleCount);
            yield return WaitForAllProjectilesReleased(system, stage: "跨色 miss");

            system.Launch(CaptureGroupSide.A, origin, endPoint, true);
            Assert.AreSame(projectile, GetSingleProjectile(system));
            Assert.IsTrue(projectile.EmitImpactOnArrival);
            AssertProjectileColor(projectile, blue);
            Assert.AreEqual(0, projectile.trailParticles.particleCount);
            Assert.AreEqual(0, projectile.residualParticles.particleCount);
            Assert.AreEqual(0, projectile.impactParticles.particleCount);
            yield return WaitForArrival(projectile);
            yield return WaitForImpact(projectile);
            yield return WaitForAllProjectilesReleased(system, stage: "复用后 hit");
        }

        [UnityTest]
        public IEnumerator Projectile_Moves_Toward_Target_With_World_Trail_And_Gravity_Impact()
        {
            PortalProjectileSystem system = _rig.ProjectileSystem;
            system.projectileSpeed = 1f;
            Vector2 origin = new Vector2(40f, 40f);
            system.Launch(CaptureGroupSide.A, origin, origin + Vector2.right * 100f, true);
            PortalProjectile projectile = GetSingleProjectile(system);

            var trailMain = projectile.trailParticles.main;
            var trailEmission = projectile.trailParticles.emission;
            var residualMain = projectile.residualParticles.main;
            var residualEmission = projectile.residualParticles.emission;
            var impactMain = projectile.impactParticles.main;
            var impactEmission = projectile.impactParticles.emission;
            Assert.AreEqual(ParticleSystemSimulationSpace.World, trailMain.simulationSpace);
            Assert.AreEqual(0f, trailMain.gravityModifier.constant);
            Assert.Greater(trailMain.startSpeed.constant, 0f);
            Assert.AreEqual(0f, trailEmission.rateOverTime.constant);
            Assert.GreaterOrEqual(trailEmission.rateOverDistance.constant, 100f);
            Assert.IsFalse(trailEmission.enabled,
                "短粒子在飞满起始发射距离前必须保持关闭。");
            Assert.AreEqual(ParticleSystemShapeType.Cone, projectile.trailParticles.shape.shapeType);
            Assert.LessOrEqual(trailMain.startLifetime.constant, 0.2f);
            Assert.Greater(projectile.trailConeAngle, 0f);
            Assert.Less(projectile.trailConeAngle, 90f);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, residualMain.simulationSpace);
            Assert.AreEqual(ParticleSystemCurveMode.TwoConstants, residualMain.gravityModifier.mode);
            Assert.Greater(residualMain.gravityModifier.constantMin, 0f);
            Assert.Greater(residualMain.gravityModifier.constantMax,
                residualMain.gravityModifier.constantMin);
            Assert.Less(residualMain.gravityModifier.constantMax, 0.1f);
            Assert.Greater(residualMain.startSpeed.constant, 0f);
            Assert.Greater(residualMain.startSize.constant, trailMain.startSize.constant);
            Assert.Greater(residualMain.startLifetime.constant, trailMain.startLifetime.constant);
            Assert.GreaterOrEqual(residualEmission.rateOverDistance.constant, 3f);
            Assert.AreEqual(ParticleSystemSimulationSpace.World, impactMain.simulationSpace);
            Assert.Greater(impactMain.gravityModifier.constant, 0f);
            Assert.Greater(impactMain.startSpeed.constant, 0f);
            Assert.Greater(impactMain.startSize.constant, trailMain.startSize.constant);
            Assert.AreEqual(1, impactEmission.burstCount);
            Assert.Greater(impactEmission.GetBurst(0).count.constant, 0f);
            Assert.IsNull(projectile.GetComponent<SpriteRenderer>());
            Assert.IsNull(projectile.GetComponent<Rigidbody2D>());
            Assert.IsNull(projectile.GetComponent<Collider2D>());

            yield return null;
            Assert.Greater(projectile.transform.position.x, origin.x);
            Assert.Less(projectile.transform.position.x, origin.x + 100f,
                "子弹必须逐帧移动且不能越过预计算终点。 ");
            Assert.IsTrue(projectile.IsFlying);
            projectile.trailStartDistance = 0.01f;
            for (int frame = 0;
                 frame < 30 && !projectile.trailParticles.emission.enabled;
                 frame++)
                yield return null;
            Assert.IsTrue(projectile.trailParticles.emission.enabled,
                "飞满起始发射距离后必须打开短粒子发射。 ");
            system.ReleaseAllProjectiles();
        }

        [UnityTest]
        public IEnumerator Disabling_Pair_Cleans_Flying_And_Arrived_Projectiles()
        {
            PortalProjectileSystem system = _rig.ProjectileSystem;
            Vector2 origin = new Vector2(40f, 40f);
            system.projectileSpeed = 1f;
            system.Launch(CaptureGroupSide.A, origin, origin + Vector2.right * 100f, false);
            PortalProjectile flying = GetSingleProjectile(system);
            system.projectileSpeed = 2000f;
            system.Launch(CaptureGroupSide.B, origin, origin + Vector2.right * 0.2f, true);
            PortalProjectile arrived = FindNewProjectile(system, flying);

            yield return WaitForArrival(arrived);
            yield return WaitForImpact(arrived);
            Assert.IsTrue(flying.IsFlying);
            _rig.Pair.enabled = false;

            Assert.AreEqual(0, system.ActiveProjectileCount);
            Assert.IsFalse(flying.gameObject.activeSelf);
            Assert.IsFalse(arrived.gameObject.activeSelf);
            Assert.AreEqual(0, flying.trailParticles.particleCount);
            Assert.AreEqual(0, arrived.impactParticles.particleCount);
            Assert.AreEqual(0, flying.residualParticles.particleCount);

            _rig.Pair.enabled = true;
            Assert.AreEqual(0, system.ActiveProjectileCount);
            Assert.IsFalse(flying.gameObject.activeSelf);
            Assert.IsFalse(arrived.gameObject.activeSelf);
        }

        [UnityTest]
        public IEnumerator Placed_Portal_Does_Not_Create_Old_To_New_Swept_Corridor()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            PlayerController2D controller = _rig.Player;
            PrepareTransparencySandbox(pair, a, b);

            a.transform.SetPositionAndRotation(new Vector3(-1f, 0f, 0f), Quaternion.identity);
            b.transform.position = new Vector3(6f, 0f, 0f);
            controller.Teleport(Vector2.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            CreatePlacementSurface(
                _rig.Root.transform, "Relocation Target",
                new Vector2(1.1f, 0f), new Vector2(0.2f, 4f));
            Physics2D.SyncTransforms();
            Assert.IsTrue(pair.TryPlacePortal(CaptureGroupSide.A, Vector2.zero, Vector2.right * 0.2f));
            Assert.AreEqual(0.9f, a.transform.position.x, 0.001f);

            yield return new WaitForFixedUpdate();
            Assert.Less(Vector2.Distance(controller.transform.position, Vector2.zero), 0.05f,
                "放门属于显式瞬移，不能把旧、新位置之间扫成传送通道。");
            Assert.Greater(Vector2.Distance(controller.transform.position, b.transform.position), 5f);
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

            var pair = _rig.Pair;
            Assert.IsNotNull(pair, "测试夹具缺少 PortalPair。");
            Assert.AreEqual(pair.PortalHeight, a.PortalHeight, 0.001f, "A 的门洞高度应该由 PortalPair 决定。");
            Assert.AreEqual(pair.PortalHeight, b.PortalHeight, 0.001f, "B 的门洞高度应该由 PortalPair 决定。");
            Assert.AreEqual(pair.PortalHeight, gateA.size.y, 0.001f, "A 的碰撞箱高度应等于门洞高度。");
            Assert.AreEqual(pair.PortalHeight, gateB.size.y, 0.001f, "B 的碰撞箱高度应等于门洞高度。");

            // 改 PortalPair 上的一对偏移，两扇门的门洞高度与碰撞箱应一起变化。
            // 将夹具默认的 ±1.5（高度 3）改为 ±3（高度 6），确保配置变化被实际验证。
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
        public IEnumerator Falling_Through_Real_Gates_Preserves_Exit_Momentum()
        {
            FindGroups(out var a, out var b);
            var pair = _rig.Pair;
            var controller = _rig.Player;
            var body = controller.GetComponent<Rigidbody2D>();
            pair.carveTerrain = false;
            pair.preserveVelocity = true;

            // A 开口向上，B 开口向右；玩家真实下落穿过 A 后应水平飞出 B。
            a.Facing = PortalFacing.Right;
            b.Facing = PortalFacing.Right;
            a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 0f, 90f));
            b.transform.SetPositionAndRotation(new Vector3(10f, 0f, 0f), Quaternion.identity);
            body.gravityScale = 0f;
            controller.Teleport(new Vector2(0f, 1.5f));
            controller.SetMoveInput(0f);

            // 先耗尽普通空中权限，确保穿门不会借系统切换重新赋予控制权。
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();
            Assert.IsTrue(controller.IsPhysicsActionActive,
                "进入真实门前应已由物理动作系统接管。");
            Assert.AreEqual(0f, controller.AirControlAuthority, 0.02f,
                "进入真实门前普通空中权限应已经归零。");

            body.velocity = new Vector2(0f, -30f);
            bool teleported = false;
            for (int i = 0; i < 20; i++)
            {
                yield return new WaitForFixedUpdate();
                if (controller.transform.position.x < 5f) continue;
                teleported = true;
                break;
            }

            Assert.IsTrue(teleported, "玩家应通过真实门碰撞/扫掠从 A 传送到 B。");
            Assert.IsTrue(controller.IsPhysicsActionActive,
                "入口/出口碰撞不能切换到玩家动作系统。");
            Assert.AreEqual(0f, controller.AirControlAuthority, 0.02f,
                "传送不能重置物理系统的空中权限进度。");
            Assert.IsFalse(controller.IsGrounded, "出口门碰撞不能被识别为地面。");
            Assert.IsFalse(controller.HasWallContact, "入口/出口门碰撞不能被识别为墙。");
            Assert.IsFalse(controller.IsWallSliding, "传送碰撞不能触发玩家墙滑。");

            float exitSpeed = body.velocity.x;
            Assert.Greater(exitSpeed, 20f, "下落速度应映射为水平出口速度。");
            float startX = controller.transform.position.x;

            for (int i = 0; i < 25; i++)
                yield return new WaitForFixedUpdate();

            Assert.IsTrue(controller.IsPhysicsActionActive,
                "出口后的持续空中运动仍应归物理动作系统。");
            Assert.AreEqual(0f, controller.AirControlAuthority, 0.02f);
            Assert.Greater(body.velocity.x, exitSpeed - 2f,
                "真实传送出口后的动量不能在后续控制步里丢失。");
            Assert.Greater(controller.transform.position.x - startX, 10f,
                "出口后应形成持续水平位移，而不是几格内停下。");
        }

        [UnityTest]
        public IEnumerator Portal_Layer_Is_Excluded_Even_When_Included_In_Ground_Mask()
        {
            FindGroups(out var a, out var b);
            var pair = _rig.Pair;
            var controller = _rig.Player;
            pair.carveTerrain = false;
            typeof(PortalPair).GetField("_cooldown", System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic).SetValue(pair, 10f);
            Assert.IsTrue(pair.IsTeleportCoolingDown,
                "测试必须冻结实际传送逻辑，只保留真实门碰撞与地墙探测。");
            a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            b.transform.SetPositionAndRotation(new Vector3(10f, 0f, 0f), Quaternion.identity);

            controller.groundMask |= controller.portalContactMask;
            Assert.AreNotEqual(0, controller.groundMask.value & (1 << a.gameObject.layer),
                "测试必须确实把真实门层包含进 groundMask。");
            controller.Teleport(Vector2.zero);
            controller.SetMoveInput(0f);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            Assert.IsTrue(controller.IsPhysicsActionActive,
                "只有门接触时不得建立玩家系统的地墙支撑。");
            Assert.IsFalse(controller.IsGrounded, "门层即使包含在 groundMask 中也不能算作地面。");
            Assert.IsFalse(controller.HasWallContact, "门层即使包含在 groundMask 中也不能算作墙。");
            Assert.IsFalse(controller.IsWallSliding, "门接触不能建立玩家墙滑状态。");
            Assert.Greater(Vector2.Distance(controller.transform.position, b.transform.position), 5f,
                "冷却中的门接触应保持原位，便于验证接触过滤。");
        }

        [UnityTest]
        public IEnumerator Portal_Opens_From_Zero_To_Pair_Height()
        {
            FindGroups(out var a, out _);
            var pair = _rig.Pair;

            Assert.IsNotNull(a, "找不到 Portal A。");
            Assert.IsNotNull(pair, "测试夹具缺少 PortalPair。");
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

            // 用 B 门做入口，起点由夹具中的门面和开口方向计算。
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

                // 出口是 A；用出口距离判断，避免绑定固定世界坐标或单一坐标轴。
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
            var pair = _rig.Pair;

            Assert.IsNotNull(controller, "玩家缺少 PlayerController2D。");
            Assert.IsNotNull(pair, "测试夹具缺少 PortalPair。");

            // 放慢回正，便于断言出口门 Y+ 对齐。
            controller.portalRealignSpeed = 5f;

            int aOpenCount = a.OpenAnimationCount;
            int bOpenCount = b.OpenAnimationCount;

            // 用 B 门做入口，A 作为出口；夹具已关闭重力，避免无关下落影响结果。
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

                // 用出口距离判断，避免把断言绑定到某个世界坐标轴。
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

            // 玩家进门时由 Teleport 复位为 0°；同向门反射相对角，普通门保留相对角。
            float sourceAngle = Mathf.DeltaAngle(0f, b.transform.eulerAngles.z);
            float partnerAngle = Mathf.DeltaAngle(0f, a.transform.eulerAngles.z);
            float playerRelativeAngle = Mathf.DeltaAngle(sourceAngle, 0f);
            float expectedAngle = Mathf.DeltaAngle(
                0f,
                a.Facing == b.Facing
                    ? partnerAngle - playerRelativeAngle
                    : partnerAngle + playerRelativeAngle);
            Vector3 expectedUp = Quaternion.Euler(0f, 0f, expectedAngle) * Vector3.up;

            Transform visual = controller.VisualRoot != null
                ? controller.VisualRoot
                : player.transform;
            Assert.Less(Vector2.Distance(
                    (Vector2)visual.up, (Vector2)expectedUp), 0.05f,
                "出传送门后，玩家显示部分的 Y+ 应与出口旋转一致。");
            Assert.AreEqual(expectedAngle, controller.CurrentRotationAngle, 5f,
                "玩家角度应按门的朝向组合保留或反射相对角。");

            Camera camera = _rig.MainCamera;
            Assert.IsNotNull(camera, "测试夹具缺少主相机。");
            Assert.AreEqual(controller.CurrentRotationAngle,
                Mathf.DeltaAngle(0f, camera.transform.eulerAngles.z), 3f,
                "相机旋转角度应等于玩家旋转角度。");

            Assert.AreEqual(bOpenCount + 1, b.OpenAnimationCount,
                "从 B 门进入应让 B 自己重播一次开门动画。");
            Assert.AreEqual(aOpenCount, a.OpenAnimationCount,
                "从 B 门进入不应触发 A 的开门动画。");
            Assert.Greater(Vector2.Distance(player.transform.position, b.transform.position), 5f,
                "传送后玩家应该已经离开 B 的位置。");
        }

        [UnityTest]
        public IEnumerator Diagonal_Portal_Transforms_Jump_Direction_With_Portal_Axes()
        {
            PortalPair pair = _rig.Pair;
            FindGroups(out var a, out var b);
            a.Facing = PortalFacing.Right;
            b.Facing = PortalFacing.Left;
            a.transform.rotation = Quaternion.identity;
            b.transform.rotation = Quaternion.Euler(0f, 0f, 45f);

            var teleportMethod = typeof(PortalPair).GetMethod(
                "Teleport", System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);
            teleportMethod.Invoke(pair, new object[] { a, b });

            Vector2 expected = ((Vector2)b.transform.up).normalized;
            Assert.Less(Vector2.Distance(expected, _rig.Player.VariableJumpDirection), 0.001f,
                "斜向出口应使用门的切线变换跳跃方向，不能固定保留世界 Y 轴。");
            yield return null;
        }

        [UnityTest]
        public IEnumerator SameFacing_BToA_Camera_Preserves_Relative_Pose_With_Projection_Mirror()
        {
            PortalPair pair = _rig.Pair;
            FindGroups(out var a, out var b);
            GameObject player = _rig.Player.gameObject;
            var controller = _rig.Player;
            Camera camera = _rig.MainCamera;
            var control = _rig.Control;
            var follow = _rig.Follow;

            PrepareTransparencySandbox(pair, a, b);
            a.transform.SetPositionAndRotation(new Vector3(-4f, 0f, 0f), Quaternion.identity);
            b.transform.SetPositionAndRotation(
                new Vector3(4f, 0f, 0f), Quaternion.Euler(0f, 0f, 45f));
            a.Facing = PortalFacing.Right;
            b.Facing = PortalFacing.Right;

            Assert.AreSame(camera, b.capture.sourceCamera,
                "B 的捕获源相机必须是本次传送实际使用的主相机。");
            Assert.AreEqual(0f, Mathf.DeltaAngle(0f, a.transform.eulerAngles.z), 0.001f);
            Assert.AreEqual(45f, Mathf.DeltaAngle(0f, b.transform.eulerAngles.z), 0.001f);
            Assert.AreEqual(PortalFacing.Right, a.Facing);
            Assert.AreEqual(PortalFacing.Right, b.Facing);

            var rb = player.GetComponent<Rigidbody2D>();
            rb.velocity = Vector2.zero;
            controller.portalRealignSpeed = 0.01f;
            controller.SetMoveInput(0f);
            controller.Teleport((Vector2)(a.transform.position +
                                          (Vector3)a.OpeningDirection * 0.45f));
            control.SetMirrorAndInput(false);
            follow.SnapToTarget();
            yield return null;

            var teleportMethod = typeof(PortalPair).GetMethod(
                "Teleport", System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);

            // 先走一次实际正常的 A→B，再让回正真实推进，形成非零相机相对角。
            teleportMethod.Invoke(pair, new object[] { a, b });
            Assert.AreEqual(45f, controller.CurrentRotationAngle, 0.001f);
            yield return null;
            Assert.AreEqual(45f, Mathf.DeltaAngle(0f, camera.transform.eulerAngles.z), 0.1f,
                "A→B 的初始相对角为 0，相机视野不应受本次修正影响。");
            Assert.IsTrue(control.IsMirrored);

            controller.portalRealignSpeed = 80f;
            yield return new WaitForSeconds(0.2f);
            controller.portalRealignSpeed = 0.01f;

            Transform visual = controller.VisualRoot;
            float oldPlayerAngle = controller.CurrentRotationAngle;
            Matrix4x4 oldRelative = visual.worldToLocalMatrix *
                                    camera.transform.localToWorldMatrix;
            CameraController2D.Decompose2D(
                oldRelative, out Vector3 oldPosition, out Quaternion oldRotation, out _);
            float oldAngle = Mathf.DeltaAngle(0f, oldRotation.eulerAngles.z);
            Assert.Greater(Mathf.Abs(oldAngle), 0.1f,
                "连续回正应形成非零相对角，以覆盖 B→A 的方向相关偏差。");

            teleportMethod.Invoke(pair, new object[] { b, a });
            float expectedExitAngle = Mathf.DeltaAngle(0f, 45f - oldPlayerAngle);
            Assert.AreEqual(expectedExitAngle, controller.CurrentRotationAngle, 0.001f,
                "B→A 同向门应反射玩家相对 B 的角度，不能叠加普通门的角度差。");
            Assert.IsFalse(control.IsMirrored, "连续两次同向传送后应恢复原始投影。");
            yield return null;

            Vector3 expectedPosition = new Vector3(-oldPosition.x, oldPosition.y, oldPosition.z);
            Vector3 actualPosition = visual.InverseTransformPoint(camera.transform.position);
            Quaternion actualRotation = Quaternion.Inverse(visual.rotation) * camera.transform.rotation;
            float actualAngle = Mathf.DeltaAngle(0f, actualRotation.eulerAngles.z);
            Assert.Greater(Mathf.Abs(oldPosition.x), 0.1f,
                "本用例必须具有非零相对 X，才能验证位置镜像。");
            Assert.Less(Vector3.Distance(expectedPosition, actualPosition),
                0.001f, "完整 B→A 链路应沿旧玩家局部 X 镜像相机相对位置。");
            Assert.AreEqual(-oldAngle, actualAngle, 0.001f,
                "同向门是反射映射，相机 Transform 的旧相对角必须取反。");

            yield return null;
            Vector3 nextPosition = visual.InverseTransformPoint(camera.transform.position);
            float nextAngle = Mathf.DeltaAngle(
                0f, (Quaternion.Inverse(visual.rotation) * camera.transform.rotation).eulerAngles.z);
            Assert.Greater(Mathf.Abs(nextAngle), 5f,
                "后续跟随帧可以平滑趋近玩家，但不能把相机相对角立即硬对齐掉。");
            Assert.Less(Vector3.Distance(expectedPosition, nextPosition),
                0.2f, "后续跟随帧不应再次瞬移相机。");

            // 固定玩家为 0° 直接走 B→A，精确覆盖本次 90° 偏差。
            controller.Teleport((Vector2)(b.transform.position +
                                          (Vector3)b.OpeningDirection * 0.45f));
            teleportMethod.Invoke(pair, new object[] { b, a });
            Vector2 expectedPlayerPosition = (Vector2)a.transform.position - a.OpeningDirection * 0.45f;
            Vector2 exitOpening = a.OpeningDirection;
            float expectedNormalOffset = Vector2.Dot(
                expectedPlayerPosition - (Vector2)a.transform.position, exitOpening);
            float safeExitOffset = -pair.gateContactOffset + pair.gateThickness * 0.5f +
                                   controller.centerPointRadius + pair.exitMargin;
            if (expectedNormalOffset < safeExitOffset)
            {
                expectedPlayerPosition += exitOpening * (safeExitOffset - expectedNormalOffset);
            }

            Assert.Less(Vector2.Distance(player.transform.position, expectedPlayerPosition), 0.0001f,
                "同向门应将入口法向偏移镜像到出口的反方向，而不是直接传送到门中心。");
            Assert.AreEqual(45f, controller.CurrentRotationAngle, 0.001f,
                "玩家 0° 从 B(45°)→A(0°) 时应为 +45°；旧结果的 -45° 正好偏了 90°。");
        }

        [UnityTest]
        public IEnumerator Other_Portal_Fades_When_Player_Is_Near()
        {
            FindGroups(out var a, out var b);
            var pair = _rig.Pair;
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
            var pair = _rig.Pair;
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
            var pair = _rig.Pair;
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
            var pair = _rig.Pair;
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
            var pair = _rig.Pair;

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
                "粒子发射区域应具有有效高度。");
            Assert.AreEqual(a.hintParticleRate * 0.25f,
                particlesA.emission.rateOverTime.constant, 0.01f,
                "粒子发射率应按组件配置计算。");
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

            // 光带/粒子是测试创建的静态对象；移动视觉中心不应移动它们。
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
            var pair = _rig.Pair;

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
            var pair = _rig.Pair;

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
        public IEnumerator Moving_Portal_Crosses_Stationary_Player_And_Transfers_Relative_Velocity()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            PlayerController2D controller = _rig.Player;
            PrepareTransparencySandbox(pair, a, b);

            a.Facing = PortalFacing.Right;
            b.Facing = PortalFacing.Left;
            a.transform.SetPositionAndRotation(new Vector3(-0.2f, 0f, 0f), Quaternion.identity);
            b.transform.SetPositionAndRotation(new Vector3(6f, 0f, 0f), Quaternion.identity);
            controller.Teleport(Vector2.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // 玩家保持静止，门在一个物理步内越过玩家；实体碰撞已关闭，只能由扫掠命中。
            a.transform.position = new Vector3(0.2f, 0f, 0f);
            yield return new WaitForFixedUpdate();

            Assert.Less(Vector2.Distance(controller.transform.position, b.transform.position), 2f,
                "移动门越过静止玩家时，应把玩家送到另一扇门。");
            Assert.Less(controller.Velocity.x, -5f,
                "出口速度应扣除入口门的运动速度，而不是把静止玩家直接当成零相对速度。");
        }

        [UnityTest]
        public IEnumerator Rotating_Portal_Sweeps_Stationary_Player_Between_End_Poses()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            PlayerController2D controller = _rig.Player;
            PrepareTransparencySandbox(pair, a, b);
            pair.preserveVelocity = false;

            a.Facing = PortalFacing.Right;
            b.Facing = PortalFacing.Left;
            a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            b.transform.SetPositionAndRotation(new Vector3(6f, 0f, 0f), Quaternion.identity);
            controller.Teleport(new Vector2(1f, 0f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // 起止姿态都是竖直门，玩家均不在门内；中间转到水平时才扫过玩家。
            a.transform.rotation = Quaternion.Euler(0f, 0f, 180f);
            yield return new WaitForFixedUpdate();

            Assert.Less(Vector2.Distance(controller.transform.position, b.transform.position), 2f,
                "旋转门的中间姿态扫到玩家时也应传送，不能只检测起止姿态。");
        }

        [UnityTest]
        public IEnumerator Relocated_Portal_Does_Not_Create_Old_To_New_Swept_Corridor()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            PlayerController2D controller = _rig.Player;
            PrepareTransparencySandbox(pair, a, b);

            a.transform.SetPositionAndRotation(new Vector3(-1f, 0f, 0f), Quaternion.identity);
            b.transform.position = new Vector3(6f, 0f, 0f);
            controller.Teleport(Vector2.zero);
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            // 显式通知即使位移低于自动阈值，也不能把旧位置和新位置连成走廊。
            a.transform.position = new Vector3(1f, 0f, 0f);
            pair.NotifyPortalRelocated(a);
            yield return new WaitForFixedUpdate();
            Assert.Less(Vector2.Distance(controller.transform.position, Vector2.zero), 0.05f,
                "显式瞬移门不应在旧、新位置之间扫掠并误传送玩家。");

            // 不走通知的大位移由兜底规则识别；新位置没有罩住玩家，因此仍不能传送。
            a.transform.position = new Vector3(20f, 0f, 0f);
            yield return new WaitForFixedUpdate();
            Assert.Less(Vector2.Distance(controller.transform.position, Vector2.zero), 0.05f,
                "跨地图门重定位不应产生旧到新位置的假扫掠。");
            Assert.Greater(Vector2.Distance(controller.transform.position, b.transform.position), 5f);
        }

        [UnityTest]
        public IEnumerator Arbitrarily_Oriented_Portals_Map_Incoming_Velocity()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup a = _rig.A;
            CaptureDisplayGroup b = _rig.B;
            PlayerController2D controller = _rig.Player;
            Rigidbody2D rb = controller.GetComponent<Rigidbody2D>();
            PrepareTransparencySandbox(pair, a, b);
            pair.teleportCooldown = 10f;

            a.transform.SetPositionAndRotation(Vector3.zero, Quaternion.Euler(0f, 0f, 37f));
            b.transform.SetPositionAndRotation(new Vector3(6f, 0f, 0f),
                Quaternion.Euler(0f, 0f, -53f));
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();

            rb.velocity = new Vector2(3.2f, -1.7f);
            yield return new WaitForFixedUpdate();
            Vector2 incoming = controller.VelocityBeforePhysics;
            Vector2 expected = b.OpeningDirection * -Vector2.Dot(incoming, a.OpeningDirection) +
                               (Vector2)b.transform.up * Vector2.Dot(incoming, a.transform.up);

            Vector2 sourceOffset = (Vector2)controller.transform.position - (Vector2)a.transform.position;
            Vector2 expectedPlayerPosition = (Vector2)b.transform.position -
                (Vector2)b.transform.right * Vector2.Dot(sourceOffset, a.transform.right) +
                (Vector2)b.transform.up * Vector2.Dot(sourceOffset, a.transform.up);
            Vector2 exitOpening = b.OpeningDirection;
            float expectedNormalOffset = Vector2.Dot(
                expectedPlayerPosition - (Vector2)b.transform.position, exitOpening);
            float safeExitOffset = -pair.gateContactOffset + pair.gateThickness * 0.5f +
                                   controller.centerPointRadius + pair.exitMargin;
            if (expectedNormalOffset < safeExitOffset)
            {
                expectedPlayerPosition += exitOpening * (safeExitOffset - expectedNormalOffset);
            }

            pair.teleportCooldown = 0f;
            var teleportMethod = typeof(PortalPair).GetMethod(
                "Teleport", System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.NonPublic);
            teleportMethod.Invoke(pair, new object[] { a, b });

            Assert.Less(Vector2.Distance(expectedPlayerPosition, controller.transform.position), 0.001f,
                "反向门的位置应按双方局部 X 镜像，保留出口正面而不是落到门背后。");
            Assert.Less(Vector2.Distance(expected, rb.velocity), 0.001f,
                "任意旋转门的出口速度应使用各自法线和切线映射进门速度。");
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
