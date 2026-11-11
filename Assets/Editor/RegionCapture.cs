using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer.EditorTools
{
    /// <summary>
    /// Copies every tile and sprite inside a <see cref="CaptureRegion"/> into a new
    /// parent object, offset above the source area.
    ///
    /// Menu: Platformer > Capture Region > Capture Into New Object
    /// Batch: -executeMethod Platformer.EditorTools.RegionCapture.Capture
    /// </summary>
    public static class RegionCapture
    {
        const string TerrainMaterialPath = "Assets/Settings/TerrainPhysicsMaterial.physicsMaterial2D";
        const float CopyGap = 2f;

        [MenuItem("Platformer/Capture Region/Create Region Marker", priority = 40)]
        public static void CreateMarker()
        {
            var go = new GameObject("Capture Region");
            Undo.RegisterCreatedObjectUndo(go, "Create Capture Region");
            go.transform.position = new Vector3(-3f, 3f, 0f);
            go.AddComponent<CaptureRegion>();
            Selection.activeGameObject = go;
        }

        [MenuItem("Platformer/Capture Region/Capture Into New Object", priority = 41)]
        public static void Capture()
        {
            CaptureInternal(false);
        }

        /// <summary>Batch entry point: captures and saves the active scene.</summary>
        public static void CaptureAndSave()
        {
            CaptureInternal(true);
        }

        /// <summary>Rebuilds the level, captures the region and renders the previews.</summary>
        [MenuItem("Platformer/Capture Region/Rebuild, Capture and Render Preview", priority = 42)]
        public static void RebuildCaptureAndPreview()
        {
            ProjectBuilder.BuildAll();
            CaptureInternal(true);
            ProjectBuilder.RenderPreview();
        }

        static void CaptureInternal(bool saveScene)
        {
            var marker = Object.FindObjectOfType<CaptureRegion>();
            if (marker == null)
            {
                Debug.LogError("[RegionCapture] No CaptureRegion marker in the scene.");
                return;
            }

            var source = Object.FindObjectOfType<Tilemap>();
            if (source == null)
            {
                Debug.LogError("[RegionCapture] No Tilemap found in the scene.");
                return;
            }

            Bounds area = marker.WorldBounds;
            Vector3Int cellMin = new Vector3Int(
                Mathf.FloorToInt(area.min.x), Mathf.FloorToInt(area.min.y), 0);
            Vector3Int cellMax = new Vector3Int(
                Mathf.CeilToInt(area.max.x), Mathf.CeilToInt(area.max.y), 0);

            var root = new GameObject("Captured Region");
            Undo.RegisterCreatedObjectUndo(root, "Capture Region");
            // Placed above the source area so it does not overlap it.
            root.transform.position = new Vector3(area.min.x, area.max.y + CopyGap, 0f);

            BuildTerrain(root.transform, source, cellMin, cellMax);
            int sprites = CopySprites(root.transform, area);
            int tiles = CountTiles(root.transform);

            var scene = EditorSceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            if (saveScene) EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = root;

            Debug.Log($"[RegionCapture] Captured {tiles} tiles and {sprites} sprites into " +
                      $"'{root.name}' at {root.transform.position}.");
        }

        static void BuildTerrain(Transform root, Tilemap source, Vector3Int cellMin, Vector3Int cellMax)
        {
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(root, false);

            var grid = gridGo.AddComponent<Grid>();
            if (source.layoutGrid != null) grid.cellSize = source.layoutGrid.cellSize;

            var go = new GameObject("Terrain");
            go.layer = LayerMask.NameToLayer("Ground");
            go.transform.SetParent(gridGo.transform, false);

            var tilemap = go.AddComponent<Tilemap>();
            go.AddComponent<TilemapRenderer>().sortingOrder = 0;

            var body = go.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;

            var composite = go.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;

            var collider = go.AddComponent<TilemapCollider2D>();
            collider.usedByComposite = true;

            var material = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(TerrainMaterialPath);
            composite.sharedMaterial = material;
            collider.sharedMaterial = material;

            for (int x = cellMin.x; x <= cellMax.x; x++)
            {
                for (int y = cellMin.y; y <= cellMax.y; y++)
                {
                    var from = new Vector3Int(x, y, 0);
                    TileBase tile = source.GetTile(from);
                    if (tile == null) continue;

                    var to = new Vector3Int(x - cellMin.x, y - cellMin.y, 0);
                    tilemap.SetTile(to, tile);
                    // Keep per-cell colour overrides so the copy matches the source exactly.
                    Color cellColor = source.GetColor(from);
                    if (cellColor != Color.white) tilemap.SetColor(to, cellColor);
                }
            }

            tilemap.RefreshAllTiles();
            collider.ProcessTilemapChanges();
            composite.GenerateGeometry();
        }

        static int CopySprites(Transform root, Bounds area)
        {
            var sources = new List<GameObject>();
            foreach (var sprite in Object.FindObjectsOfType<SpriteRenderer>())
            {
                if (sprite.transform.IsChildOf(root)) continue;
                if (!sprite.bounds.Intersects(area)) continue;
                sources.Add(sprite.gameObject);
            }

            foreach (var source in sources)
            {
                var copy = Object.Instantiate(source, root);
                copy.name = source.name + " (Copy)";
                copy.transform.position = root.position + (source.transform.position - area.min);

                // Copies only render: disable their logic and freeze their physics.
                foreach (var behaviour in copy.GetComponentsInChildren<PlatformerBehaviour>(true))
                {
                    behaviour.MarkAsCopy();
                }
            }

            return sources.Count;
        }

        static int CountTiles(Transform root)
        {
            int count = 0;
            foreach (var tilemap in root.GetComponentsInChildren<Tilemap>())
            {
                foreach (var cell in tilemap.cellBounds.allPositionsWithin)
                {
                    if (tilemap.HasTile(cell)) count++;
                }
            }

            return count;
        }
    }
}
