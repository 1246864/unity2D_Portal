using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Platformer.Tests
{
    /// <summary>
    /// 传送门地形开洞的 PlayMode 测试：门激活时按“背向矩形”精确切掉地形碰撞，
    /// 允许切到半格；门停用后恢复。
    /// </summary>
    public class PortalTerrainCarverTests
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

        static CaptureDisplayGroup FindGroup(CaptureGroupSide side)
        {
            foreach (var group in Object.FindObjectsOfType<CaptureDisplayGroup>())
                if (group.side == side) return group;
            return null;
        }

        [UnityTest]
        public IEnumerator Portal_Carves_Terrain_And_Restores_When_Deactivated()
        {
            var pair = Object.FindObjectOfType<PortalPair>();
            Assert.IsNotNull(pair, "场景缺少 PortalPair。");

            var carver = pair.GetComponent<PortalTerrainCarver>();
            Assert.IsNotNull(carver, "PortalPair 上应该运行时补上 PortalTerrainCarver。");

            var terrainGo = GameObject.Find("Terrain");
            Assert.IsNotNull(terrainGo, "找不到 Terrain。");
            var terrain = terrainGo.GetComponent<Tilemap>();
            var tilemapCollider = terrainGo.GetComponent<TilemapCollider2D>();
            var composite = terrainGo.GetComponent<CompositeCollider2D>();
            Assert.IsNotNull(terrain);
            Assert.IsNotNull(tilemapCollider);
            Assert.IsNotNull(composite);

            var groupA = FindGroup(CaptureGroupSide.A);
            Assert.IsNotNull(groupA, "找不到 Portal A。");

            // 场景里的 A 可能被移动/旋转过；测试统一放到已知的墙位，保证结果可复现。
            groupA.transform.SetPositionAndRotation(
                new Vector3(6.03f, 1.52f, 0f), Quaternion.identity);

            // 先关闭 A，保证从“未开洞”的干净状态开始。
            groupA.gameObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            yield return null;

            Vector2 portal = groupA.transform.position;
            Vector2 back = groupA.BackDirection; // A 朝左，背向是 +X
            Vector2 inside = portal + back * 0.2f;   // 矩形内部：0 < 0.2 < 0.5
            Vector2 outside = portal + back * 0.8f;  // 矩形外部：0.8 > 0.5，仍在墙格内

            var gateCell = new Vector3Int(
                Mathf.FloorToInt(portal.x),
                Mathf.FloorToInt(portal.y),
                0);
            Assert.IsTrue(terrain.HasTile(gateCell),
                $"测试要求门洞格 {gateCell} 有墙；当前场景 A 门应嵌在墙里。");

            var groundMask = 1 << 6;
            Assert.IsNotNull(Physics2D.OverlapPoint(inside, groundMask),
                "开门前，矩形内部的点应该能检测到 Ground 碰撞。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, groundMask),
                "开门前，矩形外部的点应该能检测到 Ground 碰撞。");

            // 激活 A：carver 应该只切掉背向 0.5 长的矩形。
            groupA.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.GreaterOrEqual(carver.CarvedCellCount, 1,
                "carver 应该记录了被切过的格子。");
            Assert.IsNull(Physics2D.OverlapPoint(inside, groundMask),
                "门激活时，矩形内部的 Ground 碰撞应该被切掉。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, groundMask),
                "门激活时，矩形外部的 Ground 碰撞应该保留（支持半格切割）。");

            // 停用 A：地形碰撞恢复。
            groupA.gameObject.SetActive(false);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNotNull(Physics2D.OverlapPoint(inside, groundMask),
                "门停用后，矩形内部的 Ground 碰撞应该恢复。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, groundMask),
                "门停用后，矩形外部的 Ground 碰撞应该保持。");

            // 再启用一次：这是之前的回归点，不能对 "(Carved)" Sprite 再次 OverridePhysicsShape。
            groupA.gameObject.SetActive(true);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNull(Physics2D.OverlapPoint(inside, groundMask),
                "门重新启用后，矩形内部的 Ground 碰撞应该重新被剔除。");
            Assert.IsNotNull(Physics2D.OverlapPoint(outside, groundMask),
                "门重新启用后，矩形外部的 Ground 碰撞应该保持。");
        }

        [UnityTest]
        public IEnumerator OpeningDirection_Follows_Transform_Rotation()
        {
            var groupA = FindGroup(CaptureGroupSide.A);
            Assert.IsNotNull(groupA, "找不到 Portal A。");

            Quaternion originalRotation = groupA.transform.rotation;

            groupA.transform.rotation = Quaternion.identity;
            yield return null;
            Assert.Less(Vector2.Distance(new Vector2(-1f, 0f), groupA.OpeningDirection), 0.001f,
                "A 朝左且未旋转时，开口方向应为 (-1,0)。");

            groupA.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            yield return null;
            Assert.Less(Vector2.Distance(new Vector2(0f, -1f), groupA.OpeningDirection), 0.001f,
                "门旋转 90° 后，开口方向应实时转到 (0,-1)。");

            groupA.transform.rotation = originalRotation;
            yield return null;
        }

        [UnityTest]
        public IEnumerator Carve_Follows_Portal_When_Moved()
        {
            var pair = Object.FindObjectOfType<PortalPair>();
            Assert.IsNotNull(pair, "场景缺少 PortalPair。");

            var carver = pair.GetComponent<PortalTerrainCarver>();
            Assert.IsNotNull(carver, "PortalPair 上应该运行时补上 PortalTerrainCarver。");

            var terrainGo = GameObject.Find("Terrain");
            Assert.IsNotNull(terrainGo, "找不到 Terrain。");
            var terrain = terrainGo.GetComponent<Tilemap>();
            Assert.IsNotNull(terrain);

            var groupA = FindGroup(CaptureGroupSide.A);
            Assert.IsNotNull(groupA, "找不到 Portal A。");

            // 统一放到已知墙位，避免场景手动调整影响移动测试。
            groupA.transform.SetPositionAndRotation(
                new Vector3(6.03f, 1.52f, 0f), Quaternion.identity);
            yield return new WaitForFixedUpdate();
            yield return null;

            var groundMask = 1 << 6;
            Vector2 originalPosition = groupA.transform.position;
            Vector2 back = groupA.BackDirection;
            Vector2 oldInside = originalPosition + back * 0.2f;

            Assert.IsNull(Physics2D.OverlapPoint(oldInside, groundMask),
                "移动前，A 门洞内部应该没有 Ground 碰撞。");

            // 把门整体上移 2 格，门洞矩形也应该跟着移动到新位置。
            groupA.transform.position = (Vector3)originalPosition + Vector3.up * 2f;
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.IsNotNull(Physics2D.OverlapPoint(oldInside, groundMask),
                "移动后，旧位置的地形碰撞应该恢复。");

            Vector2 newInside = (Vector2)groupA.transform.position + groupA.BackDirection * 0.2f;
            Assert.IsNull(Physics2D.OverlapPoint(newInside, groundMask),
                "移动后，新位置的门洞内部应该没有 Ground 碰撞。");

            // 还原位置，避免影响其它测试。
            groupA.transform.position = originalPosition;
            yield return new WaitForFixedUpdate();
            yield return null;
        }
    }
}
