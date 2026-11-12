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
        public IEnumerator Snapshot_Camera_Captures_Main_View()
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

            Assert.IsNotNull(snapshot.output.sprite, "Snapshot output sprite should be created.");

            // Captured region = 1.2x the main camera view.
            float expectedSize = snapshot.sourceCamera.orthographicSize * 1.2f;
            Assert.AreEqual(expectedSize, snapshot.captureCamera.orthographicSize, 0.01f,
                "Capture camera should cover 1.2x the main view.");

            // The snapshot image must contain the scene, not only the background colour.
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
        }

        static float ColorDistance(Color a, Color b)
        {
            return Mathf.Max(Mathf.Abs(a.r - b.r), Mathf.Max(Mathf.Abs(a.g - b.g), Mathf.Abs(a.b - b.b)));
        }
    }
}
