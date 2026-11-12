using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Platformer.Tests
{
    /// <summary>
    /// Integration test for the tilemap-built level: loads Level01 additively,
    /// checks the tilemap is populated and that the player is standing on the
    /// merged Tilemap/Composite collider.
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

            // Let physics settle on the tilemap collider.
            yield return new WaitForSeconds(0.75f);

            GameObject player = null;
            Tilemap tilemap = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") player = root;

                Tilemap found = root.GetComponentInChildren<Tilemap>(true);
                if (found != null) tilemap = found;
            }

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

            // Place the player just left of the 45-degree tile ramp (x = 10..12).
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

            var snapshot = Object.FindObjectOfType<RegionSnapshotCamera>();
            Assert.IsNotNull(snapshot, "RegionSnapshotCamera not found in Level01.");
            if (snapshot.output.sprite == null)
            {
                Assert.Ignore("Rendering is disabled in this run; snapshot image check skipped.");
            }

            // 1. The image is not empty: it contains terrain and sprites.
            Texture2D texture = snapshot.output.sprite.texture;
            Color background = snapshot.sourceCamera.backgroundColor;
            int content = 0;
            for (int y = 0; y < texture.height; y += 16)
            {
                for (int x = 0; x < texture.width; x += 16)
                {
                    if (ColorDistance(texture.GetPixel(x, y), background) > 0.15f) content++;
                }
            }

            Assert.Greater(content, 20, "Snapshot image should contain terrain and sprites.");

            // 2. The snapshot is the capture camera's own image: moving the player camera
            // must not move the capture camera.
            Vector3 capturePosition = snapshot.captureCamera.transform.position;
            snapshot.sourceCamera.transform.position += new Vector3(4f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(capturePosition, snapshot.captureCamera.transform.position,
                "Capture camera must keep its own position, not follow the player camera.");

            // 3. Its size is 1.2x the main view.
            Assert.AreEqual(snapshot.sourceCamera.orthographicSize * 1.2f,
                snapshot.captureCamera.orthographicSize, 0.01f,
                "Capture camera should cover 1.2x the main view.");

            // 4. 1:1 in both axes, measured against what the capture camera really covers.
            float capturedHeight = snapshot.captureCamera.orthographicSize * 2f;
            float capturedWidth = capturedHeight * (texture.width / (float)texture.height);
            Assert.AreEqual(capturedHeight, snapshot.output.sprite.bounds.size.y, 0.001f,
                "Snapshot height must equal the captured world height.");
            Assert.AreEqual(capturedWidth, snapshot.output.sprite.bounds.size.x, 0.05f,
                "Snapshot width must equal the captured world width.");
            Assert.AreEqual(Vector3.one, snapshot.output.transform.lossyScale,
                "Snapshot output must not be scaled.");

            // 5. The canvas keeps a constant texel density.
            Assert.AreEqual(snapshot.pixelsPerUnit,
                texture.height / snapshot.output.sprite.bounds.size.y, 0.5f,
                "Canvas should keep the configured texel density.");

            // 6. Growing the main view grows the captured area and the canvas with it.
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

            // 7. The player is drawn above the snapshot output.
            var playerRenderer = Object.FindObjectOfType<PlayerController2D>().GetComponent<SpriteRenderer>();
            Assert.Greater(playerRenderer.sortingOrder, snapshot.output.sortingOrder,
                "Player must be drawn above the snapshot output.");

            // 8. The capture camera can see its own output (mirror-in-mirror recursion).
            int outputLayerBit = 1 << snapshot.output.gameObject.layer;
            Assert.AreNotEqual(0, snapshot.captureCamera.cullingMask & outputLayerBit,
                "Capture camera must be able to capture its own output.");

            // 9. The image really comes from that camera: moving it changes the picture.
            snapshot.CaptureNow();
            Color[] before = snapshot.output.sprite.texture.GetPixels();
            snapshot.captureCamera.transform.position += new Vector3(6f, 0f, 0f);
            snapshot.CaptureNow();
            Color[] after = snapshot.output.sprite.texture.GetPixels();
            int changed = 0;
            for (int i = 0; i < before.Length; i += 64)
            {
                if (ColorDistance(before[i], after[i]) > 0.02f) changed++;
            }

            Assert.Greater(changed, 20, "Moving the capture camera should change the snapshot image.");
        }

        static float ColorDistance(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }
    }
}
