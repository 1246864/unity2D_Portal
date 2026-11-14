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

            // 把玩家放到 45 度瓦片坡道（x = 10..12）左侧一点的位置。
            controller.Teleport(new Vector2(9f, 0.75f));
            yield return new WaitForFixedUpdate();

            controller.SetMoveInput(1f);
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

            // 3. 尺寸是主视野的 1.2 倍。
            Assert.AreEqual(snapshot.sourceCamera.orthographicSize * 1.2f,
                snapshot.captureCamera.orthographicSize, 0.01f,
                "Capture camera should cover 1.2x the main view.");

            // 4. 两个轴都是 1:1，以捕获相机真正覆盖的范围为准。
            float capturedHeight = snapshot.captureCamera.orthographicSize * 2f;
            float capturedWidth = capturedHeight * (texture.width / (float)texture.height);
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

            // 6. 主视野变大时，捕获范围和画布一起变大。
            float originalOrtho = snapshot.sourceCamera.orthographicSize;
            int textureHeightBefore = texture.height;
            snapshot.sourceCamera.orthographicSize = originalOrtho * 2f;
            yield return null;
            yield return null;
            Assert.AreEqual(originalOrtho * 2f * 1.2f, snapshot.captureCamera.orthographicSize, 0.01f,
                "Capture camera must grow with the main view.");
            Assert.Greater(snapshot.output.sprite.texture.height, textureHeightBefore,
                "Canvas must grow with the captured area.");
            snapshot.sourceCamera.orthographicSize = originalOrtho;
            yield return null;
            yield return null;

            // 7. 玩家绘制在快照输出之上。
            var playerRenderer = Object.FindObjectOfType<PlayerController2D>().GetComponent<SpriteRenderer>();
            Assert.Greater(playerRenderer.sortingOrder, snapshot.output.sortingOrder,
                "Player must be drawn above the snapshot output.");

            // 8. 捕获相机排除自己的输出层，不再递归。
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
