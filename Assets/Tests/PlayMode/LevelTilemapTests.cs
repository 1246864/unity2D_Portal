using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Platformer.Tests
{
    /// <summary>
    /// Tilemap 关卡的集成测试：以叠加方式加载 Level01，
    /// 检查 Tilemap 已绘制瓦片，且玩家站在 Tilemap/Composite 合并碰撞体上。
    /// </summary>
    public class LevelTilemapTests
    {
        const string LevelScene = "Level01";

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Scene scene = SceneManager.GetSceneByName(LevelScene);
            if (scene.IsValid() && scene.isLoaded)
            {
                yield return SceneManager.UnloadSceneAsync(scene);
            }
        }

        [UnityTest]
        public IEnumerator Tilemap_Level_Has_Grounded_Player()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByName(LevelScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Level01 could not be loaded.");

            // 让物理在 Tilemap 碰撞体上稳定下来。
            yield return new WaitForSeconds(0.75f);

            GameObject player = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") player = root;
            }

            // 场景里现在有 Terrain 和运行时的 Outline 两个 Tilemap，按名字取地形。
            var terrainGo = GameObject.Find("Terrain");
            Tilemap tilemap = terrainGo != null ? terrainGo.GetComponent<Tilemap>() : null;

            Assert.IsNotNull(player, "Player not found in Level01.");
            Assert.IsNotNull(tilemap, "Tilemap not found in Level01.");

            int cells = 0;
            foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
            {
                if (tilemap.HasTile(position)) cells++;
            }

            Assert.Greater(cells, 100, $"Expected a populated tilemap, found {cells} cells.");

            var controller = player.GetComponent<PlayerController2D>();

            var composite = tilemap.GetComponent<CompositeCollider2D>();
            var tilemapCollider = tilemap.GetComponent<TilemapCollider2D>();
            Debug.Log($"[TilemapTest] playerY={player.transform.position.y:F2} grounded={controller.IsGrounded} " +
                      $"compositePaths={(composite != null ? composite.pathCount : -1)} " +
                      $"tileShapes={(tilemapCollider != null ? tilemapCollider.shapeCount : -1)}");

            Assert.IsTrue(controller.IsGrounded, "Player should stand on the tilemap collider.");
            Assert.AreEqual(0.75f, player.transform.position.y, 0.15f,
                "Player should rest on top of the tilemap ground.");
        }

        [UnityTest]
        public IEnumerator Tilemap_Has_Outline_Layer()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByName(LevelScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Level01 could not be loaded.");

            yield return new WaitForSeconds(0.3f);

            var outlineGo = GameObject.Find("Outline");
            Assert.IsNotNull(outlineGo, "Level01 应该有一个运行时生成的 Outline 描边层。");

            var outline = outlineGo.GetComponent<Tilemap>();
            Assert.IsNotNull(outline, "Outline 物体上应该有 Tilemap。");

            int cells = 0;
            foreach (Vector3Int position in outline.cellBounds.allPositionsWithin)
            {
                if (outline.HasTile(position)) cells++;
            }

            Assert.Greater(cells, 0, "描边层应该按邻居规则绘制了描边瓦片。");
        }

        [UnityTest]
        public IEnumerator Portal_View_Keeps_Offset_Under_View_Center()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByName(LevelScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Level01 could not be loaded.");

            yield return new WaitForSeconds(0.3f);

            CaptureDisplayGroup groupA = null;
            CaptureDisplayGroup groupB = null;
            foreach (var group in Object.FindObjectsOfType<CaptureDisplayGroup>())
            {
                if (group.side == CaptureGroupSide.A) groupA = group;
                else if (group.side == CaptureGroupSide.B) groupB = group;
            }

            Assert.IsNotNull(groupA, "A 组未找到。");
            Assert.IsNotNull(groupB, "B 组未找到。");

            foreach (var group in new[] { groupA, groupB })
            {
                Assert.IsNotNull(group.viewCenter, $"{group.side} 应解析到 Portal View Center。");
                Assert.AreEqual("Portal View Center", group.viewCenter.name,
                    $"{group.side} 的视觉中心应为 Portal View Center。");
                Assert.AreEqual(group.viewCenter, group.output.transform.parent,
                    "Portal View 应是 Portal View Center 的直接子节点。");

                int outputLayer = group.output.gameObject.layer;
                Assert.AreNotEqual(0, outputLayer, "Portal View 应在专用层（Snapshot）。");
                Assert.AreEqual(0, group.capture.captureCamera.cullingMask & (1 << outputLayer),
                    "传送门相机应排除 Portal View 所在层，避免递归渲染。");

                float baseLocalZ = group.output.transform.localPosition.z;
                float baseWorldZ = group.output.transform.position.z;

                // 用临时 Sprite 让 ApplyHalfCrop 真正走完，验证它只叠加 X 偏移。
                var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 32), new Vector2(0.5f, 0.5f), 40f);
                group.output.sprite = sprite;
                group.ApplyHalfCrop();

                Assert.AreEqual(baseLocalZ, group.output.transform.localPosition.z, 0.001f,
                    "半屏裁剪不能覆盖 Portal View 的 local Z。");
                Assert.AreEqual(baseWorldZ, group.output.transform.position.z, 0.001f,
                    "半屏裁剪不能移动 Portal View 的世界 Z。");

                if (group.side == CaptureGroupSide.A)
                    Assert.Greater(group.output.transform.localPosition.x, 0f, "A 应显示右半。");
                else
                    Assert.Less(group.output.transform.localPosition.x, 0f, "B 应显示左半。");

                Object.Destroy(group.output.sprite);
                group.output.sprite = null;
                Object.Destroy(sprite);
                Object.Destroy(texture);
            }
        }

        [UnityTest]
        public IEnumerator Player_Climbs_Tilemap_Ramp()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByName(LevelScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Level01 could not be loaded.");

            yield return new WaitForSeconds(0.5f);

            GameObject player = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") player = root;
            }

            Assert.IsNotNull(player, "Player not found in Level01.");
            var controller = player.GetComponent<PlayerController2D>();

            // 这个测试只验证 Tilemap 斜坡；关闭传送门开洞，避免当前门洞矩形
            // 覆盖到坡道区域后把 Ground 碰撞替换掉，影响测试目标。
            var pair = Object.FindObjectOfType<PortalPair>();
            if (pair != null)
            {
                pair.carveTerrain = false;
                var carver = pair.GetComponent<PortalTerrainCarver>();
                if (carver != null) carver.enabled = false;
            }
            yield return new WaitForFixedUpdate();

            // 当前地形是一段向右下降的 45° 坡道（x≈9..11），从右侧向左走上去。
            controller.Teleport(new Vector2(12f, 0.75f));
            yield return new WaitForFixedUpdate();

            controller.SetMoveInput(-1f);
            float peakY = player.transform.position.y;
            for (int i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                peakY = Mathf.Max(peakY, player.transform.position.y);
            }

            controller.SetMoveInput(0f);

            Assert.Greater(peakY, 2f,
                $"Player should walk up the tile ramp to the 2-cell plateau, peak y was {peakY:F2}.");
        }

        [UnityTest]
        public IEnumerator Snapshot_Camera_Captures_Own_View()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByName(LevelScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Level01 could not be loaded.");

            yield return new WaitForSeconds(0.3f);

            var groups = Object.FindObjectsOfType<CaptureDisplayGroup>();
            Assert.AreEqual(2, groups.Length, "There should be two capture display groups.");

            CaptureDisplayGroup groupA = null;
            CaptureDisplayGroup groupB = null;
            foreach (var group in groups)
            {
                if (group.side == CaptureGroupSide.A) groupA = group;
                else groupB = group;
            }

            Assert.IsNotNull(groupA, "Capture Display Group A not found.");
            Assert.IsNotNull(groupB, "Capture Display Group B not found.");

            // 交叉显示：A 的相机画到 B 的输出上，B 的相机画到 A 的输出上。
            Assert.AreEqual(groupB.output, groupA.capture.output, "A's camera must paint B's output.");
            Assert.AreEqual(groupA.output, groupB.capture.output, "B's camera must paint A's output.");

            var snapshot = groupA.capture;
            if (snapshot.output.sprite == null)
            {
                Assert.Ignore("Rendering is disabled in this run; snapshot image check skipped.");
            }

            var pair = Object.FindObjectOfType<PortalPair>();
            Assert.IsNotNull(pair, "场景缺少 PortalPair。");

            // 尺寸参数统一在 PortalPair 上配置，由它下发给 A/B 两个捕获相机。
            pair.useDynamicSize = false;
            pair.captureWidth = 50f;
            pair.captureHeight = 35f;
            pair.cameraSizeMultiplier = new Vector2(1.2f, 2f);
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            // 验证下发后的静态尺寸不跟随主相机。
            Assert.AreEqual(pair.captureHeight, snapshot.captureHeight, 0.001f,
                "PortalPair 的静态高度应下发到捕获相机。");
            Assert.AreEqual(pair.captureWidth, snapshot.captureWidth, 0.001f,
                "PortalPair 的静态宽度应下发到捕获相机。");

            // 1. 画面来自捕获相机。批处理 PlayMode 不会渲染相机，所以像素内容
            //    改由预览渲染来验证；这里只要求输出精灵存在，且尺寸由捕获相机决定。
            Texture2D texture = snapshot.output.sprite.texture;
            Assert.IsNotNull(texture, "Snapshot output sprite should have a texture.");

            // 2. 快照是捕获相机自己的画面：移动玩家相机的不会带动捕获相机。
            Vector3 capturePosition = snapshot.captureCamera.transform.position;
            snapshot.sourceCamera.transform.position += new Vector3(4f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(capturePosition, snapshot.captureCamera.transform.position,
                "Capture camera must keep its own position, not follow the player camera.");

            // 3. 静态尺寸由 PortalPair 的 captureWidth/captureHeight 下发，不跟随主相机。
            Assert.AreEqual(snapshot.captureHeight * 0.5f,
                snapshot.captureCamera.orthographicSize, 0.01f,
                "Capture camera size must come from captureHeight.");
            Assert.AreEqual(snapshot.captureWidth,
                snapshot.captureCamera.aspect * snapshot.captureHeight, 0.01f,
                "Capture camera aspect must come from captureWidth/captureHeight.");

            // 4. 两个轴都是 1:1，以捕获相机真正覆盖的范围为准。
            float capturedHeight = snapshot.captureHeight;
            float capturedWidth = snapshot.captureWidth;
            Assert.AreEqual(capturedHeight, snapshot.output.sprite.bounds.size.y, 0.001f,
                "Snapshot height must equal the captured world height.");
            // 宽度是一半，再加上两半为遮住接缝而留出的少量重叠。
            Assert.AreEqual(capturedWidth * 0.5f, snapshot.output.sprite.bounds.size.x, 0.2f,
                "Snapshot shows half of the captured width.");

            // A 保留右半，B 保留左半。
            Assert.Greater(groupA.output.transform.localPosition.x, 0f, "Group A should show the right half.");
            Assert.Less(groupB.output.transform.localPosition.x, 0f, "Group B should show the left half.");
            Assert.AreEqual(Vector3.one, snapshot.output.transform.lossyScale,
                "Snapshot output must not be scaled.");

            // 新的 Portal View Center 层级：半屏 X 偏移不能覆盖 Portal View 自己的 Z 偏移。
            Assert.AreEqual(0f, groupA.output.transform.position.z, 0.01f,
                "A 的 Portal View 应保持在世界 z=0，不能被半屏偏移拉到 Portal View Center 的 z。");
            Assert.AreEqual(0f, groupB.output.transform.position.z, 0.01f,
                "B 的 Portal View 应保持在世界 z=0。");
            Assert.IsNotNull(groupA.viewCenter, "A 应该解析到 Portal View Center。");
            Assert.AreEqual("Portal View Center", groupA.viewCenter.name,
                "A 的视觉中心应该是 Portal View Center。");
            Assert.IsNotNull(groupB.viewCenter, "B 应该解析到 Portal View Center。");
            Assert.AreEqual("Portal View Center", groupB.viewCenter.name,
                "B 的视觉中心应该是 Portal View Center。");

            // 5. 画布保持恒定的像素密度。
            Assert.AreEqual(snapshot.pixelsPerUnit,
                texture.height / snapshot.output.sprite.bounds.size.y, 0.5f,
                "Canvas should keep the configured texel density.");

            // 6. 主视野变化时，固定捕获矩形和画布不应变化。
            float originalOrtho = snapshot.sourceCamera.orthographicSize;
            int textureHeightBefore = texture.height;
            float captureSizeBefore = snapshot.captureCamera.orthographicSize;
            snapshot.sourceCamera.orthographicSize = originalOrtho * 2f;
            yield return null;
            yield return null;
            Assert.AreEqual(captureSizeBefore, snapshot.captureCamera.orthographicSize, 0.01f,
                "Capture camera must not follow the main view.");
            Assert.AreEqual(textureHeightBefore, snapshot.output.sprite.texture.height,
                "Canvas must not grow with the main view.");
            snapshot.sourceCamera.orthographicSize = originalOrtho;
            yield return null;
            yield return null;

            // 7. 切到动态模式：宽/高分别 = 主相机视野宽/高 × X/Y 倍率。
            pair.useDynamicSize = true;
            pair.cameraSizeMultiplier = new Vector2(1.2f, 2f);
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            Assert.IsTrue(snapshot.useDynamicSize, "动态模式应下发到捕获相机。");
            Assert.AreEqual(1.2f, snapshot.cameraSizeMultiplier.x, 0.001f,
                "动态宽度倍率应下发到捕获相机。");
            Assert.AreEqual(2f, snapshot.cameraSizeMultiplier.y, 0.001f,
                "动态高度倍率应下发到捕获相机。");

            float sourceHeight = snapshot.sourceCamera.orthographicSize * 2f;
            float sourceWidth = sourceHeight * snapshot.sourceCamera.aspect;
            float dynamicHeight = sourceHeight * 2f;
            float dynamicWidth = sourceWidth * 1.2f;

            Assert.AreEqual(dynamicHeight, snapshot.captureCamera.orthographicSize * 2f, 0.02f,
                "Dynamic capture height must be main camera height × Y multiplier.");
            Assert.AreEqual(dynamicWidth, snapshot.captureCamera.aspect * dynamicHeight, 0.02f,
                "Dynamic capture width must be main camera width × X multiplier.");
            Assert.AreEqual(dynamicHeight, snapshot.output.sprite.bounds.size.y, 0.01f,
                "Dynamic snapshot sprite must match the captured world height.");
            Assert.AreEqual(Mathf.RoundToInt(dynamicHeight * snapshot.pixelsPerUnit),
                snapshot.output.sprite.texture.height,
                "Dynamic canvas height must use the fixed pixel density.");
            Assert.AreEqual(Mathf.RoundToInt(dynamicWidth * snapshot.pixelsPerUnit),
                snapshot.output.sprite.texture.width,
                "Dynamic canvas width must use the fixed pixel density.");

            // 还原静态模式，避免影响后续断言。
            pair.useDynamicSize = false;
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            // 8. 玩家绘制在快照输出之上。
            var playerRenderer = Object.FindObjectOfType<PlayerController2D>().GetComponent<SpriteRenderer>();
            Assert.Greater(playerRenderer.sortingOrder, snapshot.output.sortingOrder,
                "Player must be drawn above the snapshot output.");

            // 9. 捕获相机排除自己的输出层，不再递归。
            int outputLayerBit = 1 << snapshot.output.gameObject.layer;
            Assert.AreEqual(0, snapshot.captureCamera.cullingMask & outputLayerBit,
                "Capture camera must exclude the Portal View layer to avoid recursion.");
        }

        static float ColorDistance(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }
    }
}
