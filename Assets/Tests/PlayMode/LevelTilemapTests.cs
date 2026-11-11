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
    }
}
