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
            int cells = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "Player") player = root;

                // The scene may contain additional captured regions, so take the
                // tilemap with the most tiles (the main level terrain).
                foreach (Tilemap candidate in root.GetComponentsInChildren<Tilemap>(true))
                {
                    int candidateCells = CountCells(candidate);
                    if (candidateCells > cells)
                    {
                        cells = candidateCells;
                        tilemap = candidate;
                    }
                }
            }

            Assert.IsNotNull(player, "Player not found in Level01.");
            Assert.IsNotNull(tilemap, "Tilemap not found in Level01.");

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
        public IEnumerator Mirror_Follows_Capture_Window()
        {
            yield return SceneManager.LoadSceneAsync(LevelScene, LoadSceneMode.Additive);

            Scene scene = SceneManager.GetSceneByName(LevelScene);
            Assert.IsTrue(scene.IsValid() && scene.isLoaded, "Level01 could not be loaded.");

            yield return new WaitForSeconds(0.3f);

            var mirror = Object.FindObjectOfType<RegionMirror>();
            Assert.IsNotNull(mirror, "RegionMirror not found in Level01.");
            Assert.Greater(mirror.VisualCount, 0, "Mirror should show the content of the window.");

            int initial = mirror.VisualCount;

            // Shrinking the window must drop visuals on the next frame.
            mirror.region.size = new Vector2(2f, 2f);
            yield return null;
            yield return null;
            int smaller = mirror.VisualCount;
            Assert.Less(smaller, initial, "Mirror should shrink when the window shrinks.");

            // Growing it again must add visuals back.
            mirror.region.size = new Vector2(20f, 14f);
            yield return null;
            yield return null;
            Assert.Greater(mirror.VisualCount, smaller, "Mirror should grow when the window grows.");
        }

        static int CountCells(Tilemap tilemap)        {
            int count = 0;
            foreach (Vector3Int position in tilemap.cellBounds.allPositionsWithin)
            {
                if (tilemap.HasTile(position)) count++;
            }

            return count;
        }
    }
}
