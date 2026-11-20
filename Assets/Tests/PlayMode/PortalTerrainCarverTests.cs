using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Platformer.Tests
{
    /// <summary>
    /// 传送门地形开洞的 PlayMode 测试：使用测试自行生成的 Tilemap，
    /// 验证背向矩形切割、移动跟随以及停用后的碰撞恢复。
    /// </summary>
    public class PortalTerrainCarverTests
    {
        PortalTestRig _rig;
        Tile _solidTile;
        int _groundMask;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _rig = PortalTestRig.Create(withTerrain: true);
            _rig.Root.SetActive(false);
            _solidTile = _rig.Terrain.CreateSolidTile();
            _rig.Terrain.FillSolidRect(_solidTile, -4, 4, -4, 8);
            _groundMask = _rig.Player.groundMask;

            // A 朝左，背向 +X 切墙；B 放在没有地形的远处，避免参与本组断言。
            _rig.A.transform.SetPositionAndRotation(
                new Vector3(0.1f, 0.5f, 0f), Quaternion.identity);
            _rig.A.Facing = PortalFacing.Left;
            _rig.B.transform.position = new Vector3(20f, 20f, 0f);

            _rig.Root.SetActive(true);
            _rig.Terrain.TilemapCollider.ProcessTilemapChanges();
            _rig.Terrain.Composite.GenerateGeometry();
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_rig != null) _rig.Destroy();
            yield return null;
        }

        static int CountResidualColliders(GameObject terrainGo)
        {
            int count = 0;
            foreach (var collider in terrainGo.GetComponentsInChildren<PolygonCollider2D>(true))
            {
                if (collider.name.StartsWith("Carved Collider ")) count++;
            }
            return count;
        }

        [UnityTest]
        public IEnumerator Portal_Carves_Terrain_And_Restores_When_Deactivated()
        {
            PortalPair pair = _rig.Pair;
            var carver = pair.GetComponent<PortalTerrainCarver>();
            CaptureDisplayGroup groupA = _rig.A;
            GameObject terrainGo = _rig.Terrain.TerrainObject;
            Tilemap terrain = _rig.Terrain.Tilemap;
            TilemapCollider2D tilemapCollider = _rig.Terrain.TilemapCollider;

            // 先关闭 A，保证从“未开洞”的干净状态开始。
            groupA.gameObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            yield return null;

            Vector2 portal = groupA.transform.position;
            Vector2 back = groupA.BackDirection;
            Vector2 inside = portal + back * 0.2f;
            Vector2 outside = portal + back * (pair.carveWidth + 0.15f);

            var gateCell = new Vector3Int(
                Mathf.FloorToInt(portal.x),
                Mathf.FloorToInt(portal.y),
                0);
            Assert.IsTrue(terrain.HasTile(gateCell), "夹具应在门洞所在格生成实心瓦片。");
            Assert.IsNotNull(Physics2D.OverlapPoint(inside, _groundMask),
                "开门前，矩形内部的点应该能检测到地面碰撞。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, _groundMask),
                "开门前，矩形外部的点应该能检测到地面碰撞。");

            groupA.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.GreaterOrEqual(carver.CarvedCellCount, 1);
            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask),
                "门激活时，矩形内部的地面碰撞应该被切掉。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, _groundMask),
                "门激活时，矩形外部的地面碰撞应该保留（支持半格切割）。");

            groupA.gameObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNotNull(Physics2D.OverlapPoint(inside, _groundMask),
                "门停用后，矩形内部的地面碰撞应该恢复。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, _groundMask));

            // 再启用一次，覆盖重复开洞不能残留旧物理形状的回归点。
            groupA.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask));
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, _groundMask));

            foreach (var polygon in terrainGo.GetComponentsInChildren<PolygonCollider2D>(true))
            {
                if (!polygon.name.StartsWith("Carved Collider ")) continue;
                Assert.AreSame(tilemapCollider.sharedMaterial, polygon.sharedMaterial,
                    "残余 PolygonCollider2D 应继承 TilemapCollider2D 的物理材质。");
            }
            Assert.Greater(CountResidualColliders(terrainGo), 0);

            // 全局开关也必须可逆：false 恢复旧洞，true 按当前门状态重切。
            pair.carveTerrain = false;
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsFalse(carver.enabled);
            Assert.AreEqual(0, carver.CarvedCellCount);
            Assert.AreEqual(0, CountResidualColliders(terrainGo));
            Assert.IsNotNull(Physics2D.OverlapPoint(inside, _groundMask));

            pair.carveTerrain = true;
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsTrue(carver.enabled);
            Assert.GreaterOrEqual(carver.CarvedCellCount, 1);
            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask));
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, _groundMask));

            pair.carveTerrain = false;
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(0, carver.CarvedCellCount);
            Assert.AreEqual(0, CountResidualColliders(terrainGo));
            Assert.IsNotNull(Physics2D.OverlapPoint(inside, _groundMask));
        }

        [UnityTest]
        public IEnumerator OpeningDirection_Follows_Transform_Rotation()
        {
            CaptureDisplayGroup groupA = _rig.A;
            groupA.transform.rotation = Quaternion.identity;
            yield return null;
            Assert.Less(Vector2.Distance(new Vector2(-1f, 0f), groupA.OpeningDirection), 0.001f);

            groupA.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            yield return null;
            Assert.Less(Vector2.Distance(new Vector2(0f, -1f), groupA.OpeningDirection), 0.001f);
        }

        [UnityTest]
        public IEnumerator Carve_Follows_Portal_When_Moved()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup groupA = _rig.A;
            Vector2 originalPosition = groupA.transform.position;
            Vector2 oldInside = originalPosition + groupA.BackDirection * 0.2f;

            Assert.IsNull(Physics2D.OverlapPoint(oldInside, _groundMask),
                "移动前，A 门洞内部应该没有地面碰撞。");

            // 夹具在新旧两个位置都生成了瓦片，两个位置的开洞断言都有效。
            groupA.transform.position =
                (Vector3)originalPosition + Vector3.up * (pair.PortalHeight + 0.5f);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNotNull(Physics2D.OverlapPoint(oldInside, _groundMask),
                "移动后，旧位置的地形碰撞应该恢复。");

            Vector2 newInside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsNull(Physics2D.OverlapPoint(newInside, _groundMask),
                "移动后，新位置的门洞内部应该没有地面碰撞。");
        }
    }
}
