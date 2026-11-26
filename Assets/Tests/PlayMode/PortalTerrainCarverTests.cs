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
            PlacePlayerInFront(_rig, _rig.A);
            _rig.Player.GetComponent<Rigidbody2D>().simulated = false;

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

        static void PlacePlayerInFront(PortalTestRig rig, CaptureDisplayGroup group)
        {
            rig.Player.transform.position =
                group.transform.position + (Vector3)group.OpeningDirection * 0.75f;
        }

        static BoxCollider2D FindBackfill(PortalPair pair, CaptureDisplayGroup group)
        {
            foreach (BoxCollider2D collider in pair.GetComponentsInChildren<BoxCollider2D>(true))
            {
                if (collider.name == group.name + " Backfill Collider") return collider;
            }
            return null;
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
        public IEnumerator Carve_Leaves_Top_Support_Strip()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup groupA = _rig.A;
            groupA.Facing = PortalFacing.Right; // 与 Level01 的 A 门一致，背向为 -X。
            PlacePlayerInFront(_rig, groupA);
            pair.carveTopMargin = 0.12f;
            yield return new WaitForFixedUpdate();
            yield return null;

            Vector2 back = groupA.BackDirection;
            Vector2 up = groupA.transform.up;
            Vector2 inside = (Vector2)groupA.transform.position + back * 0.2f;
            float top = pair.PortalHeight * 0.5f;
            Vector2 support = inside + up * (top - pair.carveTopMargin * 0.5f);
            Vector2 belowSupport = inside + up * (top - pair.carveTopMargin - 0.05f);

            Assert.IsNotNull(Physics2D.OverlapPoint(support, _groundMask),
                "门洞顶边下方应保留一条地形碰撞，支撑站在门顶后方的玩家。");
            Assert.IsNull(Physics2D.OverlapPoint(belowSupport, _groundMask),
                "顶部保留条以下的门洞内部仍应正常切除。");
        }

        [UnityTest]
        public IEnumerator Backfill_Toggles_Without_Recarving()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup groupA = _rig.A;
            var carver = pair.GetComponent<PortalTerrainCarver>();
            GameObject terrainGo = _rig.Terrain.TerrainObject;
            BoxCollider2D backfill = FindBackfill(pair, groupA);
            Assert.IsNotNull(backfill, "激活门应创建一个背面补墙碰撞体。");

            int carvedCellCount = carver.CarvedCellCount;
            PolygonCollider2D[] originalResiduals =
                terrainGo.GetComponentsInChildren<PolygonCollider2D>(true);
            Vector2 inside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsFalse(backfill.enabled, "玩家在正面区域内时补墙碰撞体应关闭。");
            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask));

            // 玩家离开正面但仍处于补墙区域内部时，不能在其身上启用实体碰撞。
            // 直接跨门改 Transform 会被 PortalPair 当成真实扫掠穿门；这里只测试回填位置。
            _rig.Player.Teleport(
                (Vector2)groupA.transform.position + groupA.BackDirection * 0.4f);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(backfill.enabled, "玩家与补墙区域重叠时应延迟启用。");

            _rig.Player.Teleport(
                (Vector2)groupA.transform.position + groupA.BackDirection * 4f);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsTrue(backfill.enabled, "玩家离开正面和补墙区域后应恢复实体碰撞。");
            Assert.IsNotNull(Physics2D.OverlapPoint(inside, _groundMask),
                "补墙碰撞体应填回已切开的门洞。");

            // 门前没有接近距离上限，覆盖高速玩家单帧跨过旧检测深度时的碰撞回归。
            _rig.Player.Teleport(
                (Vector2)groupA.transform.position + groupA.OpeningDirection * 20f);
            Physics2D.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.IsFalse(backfill.enabled,
                "玩家即使从极远处高速接近，只要在门前半平面就应关闭补墙碰撞体。");
            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask));
            Assert.AreSame(backfill, FindBackfill(pair, groupA),
                "玩家进出只应切换原碰撞体，不应销毁重建。");
            Assert.AreEqual(carvedCellCount, carver.CarvedCellCount,
                "玩家进出不应改变已裁剪格子数量。");
            CollectionAssert.AreEquivalent(originalResiduals,
                terrainGo.GetComponentsInChildren<PolygonCollider2D>(true),
                "玩家进出不应重新执行地形布尔裁剪或重建残余碰撞体。");
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
            PlacePlayerInFront(_rig, groupA);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNotNull(Physics2D.OverlapPoint(oldInside, _groundMask),
                "移动后，旧位置的地形碰撞应该恢复。");

            Vector2 newInside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsNull(Physics2D.OverlapPoint(newInside, _groundMask),
                "移动后，新位置的门洞内部应该没有地面碰撞。");
        }

        [UnityTest]
        public IEnumerator Local_Carve_Updates_Reuse_Residual_Colliders()
        {
            CaptureDisplayGroup groupA = _rig.A;
            GameObject terrainGo = _rig.Terrain.TerrainObject;

            PolygonCollider2D[] Before()
            {
                var residuals = new System.Collections.Generic.List<PolygonCollider2D>();
                foreach (var collider in terrainGo.GetComponentsInChildren<PolygonCollider2D>(true))
                {
                    if (collider.name.StartsWith("Carved Collider ")) residuals.Add(collider);
                }
                return residuals.ToArray();
            }

            PolygonCollider2D[] original = Before();
            Assert.Greater(original.Length, 0);

            // 沿门洞长度方向小位移，不改变扩大一格后的 broadphase 集合。
            groupA.transform.position += Vector3.right * 0.05f;
            yield return new WaitForFixedUpdate();
            yield return null;

            PolygonCollider2D[] updated = Before();
            CollectionAssert.AreEquivalent(original, updated,
                "门洞仍覆盖同一组格子时，不应销毁并新建残余 PolygonCollider2D。");
            foreach (PolygonCollider2D collider in updated)
                Assert.IsTrue(collider.enabled && collider.gameObject.activeSelf);

            Vector2 inside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask),
                "复用残余碰撞器后，新门洞仍应正确切除地形。");
        }

        [UnityTest]
        public IEnumerator Rotated_Carve_Uses_Current_Portal_Axes()
        {
            CaptureDisplayGroup groupA = _rig.A;
            groupA.transform.rotation = Quaternion.Euler(0f, 0f, 35f);
            yield return new WaitForFixedUpdate();
            yield return null;

            Vector2 inside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Vector2 outside = (Vector2)groupA.transform.position +
                              groupA.BackDirection * (_rig.Pair.carveWidth + 0.2f);
            Assert.IsNull(Physics2D.OverlapPoint(inside, _groundMask),
                "旋转门的背向矩形内部应被切除。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, _groundMask),
                "旋转门背向矩形之外的地形应保留。");
        }

        [UnityTest]
        public IEnumerator Placement_Restores_Old_Carve_And_Carves_New_Surface()
        {
            PortalPair pair = _rig.Pair;
            CaptureDisplayGroup groupA = _rig.A;
            Vector2 oldInside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsNull(Physics2D.OverlapPoint(oldInside, _groundMask),
                "放门前，旧门洞内部应已切除。");

            _rig.Player.Teleport(new Vector2(-6f, 0.5f));
            Physics2D.SyncTransforms();
            Vector2 origin = _rig.Player.CenterPoint.transform.position;
            Assert.IsTrue(pair.TryPlacePortal(
                CaptureGroupSide.A, origin + Vector2.right * 0.2f));
            Assert.AreEqual(-4f, groupA.transform.position.x, 0.001f,
                "射线应命中测试 Tilemap 的左侧外表面。");

            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNotNull(Physics2D.OverlapPoint(oldInside, _groundMask),
                "放门后，旧门洞的地形碰撞应恢复。");
            Vector2 newInside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsNull(Physics2D.OverlapPoint(newInside, _groundMask),
                "放门后，新命中表面的门洞应正确切除。");
        }
    }
}
