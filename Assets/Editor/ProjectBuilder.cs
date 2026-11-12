using System;
using System.Collections.Generic;
using System.IO;
using Platformer.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D.Sprites;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.Tilemaps;

namespace Platformer.EditorTools
{
    /// <summary>
    /// Builds the playable template from scratch: imports placeholder art,
    /// creates prefabs, assembles the level and saves the scene.
    ///
    /// Menu: Platformer > Rebuild Level + Prefabs
    /// Batch: -executeMethod Platformer.EditorTools.ProjectBuilder.BuildAll -quit
    /// </summary>
    public static class ProjectBuilder
    {
        const string ArtDir = "Assets/Art/Placeholders";
        const string PrefabDir = "Assets/Prefabs";
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = "Assets/Scenes/Level01.unity";
        const string PlayerPhysicsMaterialPath = "Assets/Settings/PlayerPhysicsMaterial.physicsMaterial2D";
        const string TerrainPhysicsMaterialPath = "Assets/Settings/TerrainPhysicsMaterial.physicsMaterial2D";
        const string OutlineMaterialPath = "Assets/Materials/ScreenOutline.mat";
        const string OutlineFeaturePath = "Assets/Settings/ScreenOutlineFeature.asset";
        const string RendererDataPath = "Assets/Settings/Renderer2D.asset";
        const string TileDir = "Assets/Tiles";
        const string TerrainTilePath = "Assets/Tiles/TerrainTile.asset";
        const string DarkTilePath = "Assets/Tiles/DarkTerrainTile.asset";

        const int GroundLayer = 6;
        const int PlayerLayer = 7;
        const int SnapshotLayer = 8;

        const float PlayerWidth = 0.72f;
        const float PlayerHeight = 1.5f;
        const float JumpHeight = 6f;

        static readonly Vector2 Spawn = new Vector2(-9f, 0.75f);

        // One colour for every terrain piece: floor, wall, step, platform.
        static readonly Color TerrainColor = new Color(0.8784314f, 0.8784314f, 0.8784314f); // #E0E0E0
        static readonly Color DarkTerrainColor = new Color(0.27f, 0.30f, 0.36f);
        static readonly Color BackgroundColor = new Color(0.086f, 0.102f, 0.153f);
        static readonly Color OutlineColor = new Color(0.70f, 0.79f, 0.92f, 1f);

        [MenuItem("Platformer/Rebuild Level + Prefabs", priority = 10)]
        public static void BuildAll()
        {
            try
            {
                EnsureFolder(ArtDir);
                EnsureFolder(PrefabDir);
                EnsureFolder(SceneDir);
                EnsureFolder("Assets/Materials");
                EnsureFolder(TileDir);

                ConfigureLayers();
                ConfigurePhysics();
                ConfigurePlayerSettings();
                ImportSprites();

                Dictionary<string, Sprite> sprites = LoadSprites();
                PhysicsMaterial2D playerMaterial = EnsurePlayerMaterial();
                PhysicsMaterial2D terrainMaterial = EnsureTerrainMaterial();
                Material litMaterial = LoadLitSpriteMaterial();
                Material outlineMaterial = EnsureOutlineMaterial();

                GameObject playerPrefab = BuildPlayerPrefab(sprites, playerMaterial, litMaterial);
                Tile terrainTile = EnsureTile(TerrainTilePath, "TerrainTile", sprites["tile1x1"], TerrainColor);
                Tile darkTile = EnsureTile(DarkTilePath, "DarkTerrainTile", sprites["tile1x1"], DarkTerrainColor);
                Tile[] slopeTiles = EnsureSlopeTiles(sprites);

                EnsureScreenOutlineFeature(outlineMaterial);
                BuildLevelScene(playerPrefab, terrainTile, darkTile, slopeTiles, terrainMaterial);
                RegisterBuildScene();
                DeleteSampleScene();

                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log("[Platformer] Build finished. Scene: " + ScenePath);
            }
            catch (Exception e)
            {
                Debug.LogError("[Platformer] Build failed: " + e);
                throw;
            }
        }

        [MenuItem("Platformer/Rebuild + Render Preview", priority = 11)]
        public static void BuildAllAndRenderPreview()
        {
            BuildAll();
            RenderPreview();
        }

        [MenuItem("Platformer/Open Level 01", priority = 20)]
        public static void OpenLevel()
        {
            if (File.Exists(ScenePath))
                EditorSceneManager.OpenScene(ScenePath);
            else
                Debug.LogWarning("[Platformer] Level01.unity not found. Run Rebuild first.");
        }

        // ------------------------------------------------------------------
        // Project settings
        // ------------------------------------------------------------------

        static void ConfigureLayers()
        {
            UnityEngine.Object tagManager =
                AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0];

            var so = new SerializedObject(tagManager);
            SerializedProperty layers = so.FindProperty("layers");

            SetLayerName(layers, GroundLayer, "Ground");
            SetLayerName(layers, PlayerLayer, "Player");
            SetLayerName(layers, SnapshotLayer, "Snapshot");

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        static void SetLayerName(SerializedProperty layers, int index, string name)
        {
            if (index < 0 || index >= layers.arraySize) return;

            SerializedProperty element = layers.GetArrayElementAtIndex(index);
            if (string.IsNullOrEmpty(element.stringValue) || element.stringValue == name)
                element.stringValue = name;
        }

        static void ConfigurePhysics()
        {
            // A snappier-than-real gravity makes a 6-cell jump feel good.
            Physics2D.gravity = new Vector2(0f, -40f);
            Physics2D.velocityIterations = 8;
            Physics2D.positionIterations = 3;
        }

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.productName = "PlatformerTemplate";
            PlayerSettings.companyName = "YourStudio";
            PlayerSettings.defaultScreenWidth = 1280;
            PlayerSettings.defaultScreenHeight = 720;
            PlayerSettings.fullScreenMode = FullScreenMode.Windowed;
            PlayerSettings.resizableWindow = true;
            PlayerSettings.runInBackground = true;
        }

        static void RegisterBuildScene()
        {
            EditorBuildSettings.scenes = new[]
            {
                new EditorBuildSettingsScene(ScenePath, true)
            };
        }

        static void DeleteSampleScene()
        {
            const string sample = SceneDir + "/SampleScene.unity";
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(sample) != null)
                AssetDatabase.DeleteAsset(sample);
        }

        // ------------------------------------------------------------------
        // Art import
        // ------------------------------------------------------------------

        // Exact triangular physics shapes for the slope sprites (normalised sprite space,
        // counter-clockwise). Without this Unity's fallback shape is inset by the
        // anti-aliased hypotenuse, which makes the player sink into the ramp.
        // Points are in pixels relative to the sprite pivot (centre), counter-clockwise.
        const float HalfTilePixels = 16f;
        static readonly Dictionary<string, Vector2[]> SlopePhysicsShapes = new Dictionary<string, Vector2[]>
        {
            { "slope_up_right", new[] { new Vector2(-HalfTilePixels, -HalfTilePixels), new Vector2(HalfTilePixels, -HalfTilePixels), new Vector2(HalfTilePixels, HalfTilePixels) } },
            { "slope_up_left", new[] { new Vector2(-HalfTilePixels, -HalfTilePixels), new Vector2(HalfTilePixels, -HalfTilePixels), new Vector2(-HalfTilePixels, HalfTilePixels) } },
            { "slope_down_right", new[] { new Vector2(HalfTilePixels, -HalfTilePixels), new Vector2(HalfTilePixels, HalfTilePixels), new Vector2(-HalfTilePixels, HalfTilePixels) } },
            { "slope_down_left", new[] { new Vector2(HalfTilePixels, HalfTilePixels), new Vector2(-HalfTilePixels, HalfTilePixels), new Vector2(-HalfTilePixels, -HalfTilePixels) } },
        };

        static void ImportSprites()
        {
            string[] guids = AssetDatabase.FindAssets("t:Texture2D", new[] { ArtDir });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                var importer = AssetImporter.GetAtPath(path) as TextureImporter;
                if (importer == null) continue;

                importer.textureType = TextureImporterType.Sprite;
                importer.spriteImportMode = SpriteImportMode.Single;
                importer.spritePixelsPerUnit = 32f;
                importer.filterMode = FilterMode.Bilinear;
                importer.textureCompression = TextureImporterCompression.Uncompressed;
                importer.alphaIsTransparency = true;
                importer.mipmapEnabled = false;
                importer.wrapMode = TextureWrapMode.Clamp;
                importer.maxTextureSize = 512;
                importer.SaveAndReimport();

                string spriteName = Path.GetFileNameWithoutExtension(path);
                if (SlopePhysicsShapes.TryGetValue(spriteName, out Vector2[] shape))
                    SetSpritePhysicsShape(path, shape);
            }

            AssetDatabase.Refresh();
        }

        static void SetSpritePhysicsShape(string assetPath, Vector2[] outline)
        {
            var importer = AssetImporter.GetAtPath(assetPath) as TextureImporter;
            if (importer == null) return;

            var factories = new SpriteDataProviderFactories();
            factories.Init();

            ISpriteEditorDataProvider provider = factories.GetSpriteEditorDataProviderFromObject(importer);
            provider.InitSpriteEditorDataProvider();

            SpriteRect[] rects = provider.GetSpriteRects();
            if (rects.Length == 0) return;

            var physicsProvider = provider.GetDataProvider<ISpritePhysicsOutlineDataProvider>();
            physicsProvider.SetOutlines(rects[0].spriteID, new List<Vector2[]> { outline });
            physicsProvider.SetTessellationDetail(rects[0].spriteID, 0.01f);
            provider.Apply();

            importer.SaveAndReimport();
        }

        static Dictionary<string, Sprite> LoadSprites()
        {
            var result = new Dictionary<string, Sprite>();
            foreach (string name in new[] { "tile1x1", "slope_up_right", "slope_up_left", "slope_down_right", "slope_down_left", "player_capsule" })
            {
                string path = ArtDir + "/" + name + ".png";
                var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(path);
                if (sprite == null)
                    throw new InvalidOperationException("Missing sprite: " + path);
                result[name] = sprite;
            }
            return result;
        }

        static Material LoadLitSpriteMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Lit-Default.mat");

            if (mat == null)
                Debug.LogWarning("[Platformer] Sprite-Lit-Default not found; using default sprite material.");

            return mat;
        }

        static Material EnsureOutlineMaterial()
        {
            EnsureFolder("Assets/Materials");

            Shader shader = Shader.Find("Hidden/Platformer/ScreenOutline");
            if (shader == null)
                throw new InvalidOperationException("Shader 'Hidden/Platformer/ScreenOutline' not found.");

            var mat = AssetDatabase.LoadAssetAtPath<Material>(OutlineMaterialPath);
            if (mat == null)
            {
                mat = new Material(shader);
                AssetDatabase.CreateAsset(mat, OutlineMaterialPath);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.SetColor("_OutlineColor", OutlineColor);
            mat.SetFloat("_OutlineWidth", 1f);   // pixels
            mat.SetFloat("_Threshold", 0.28f);
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void EnsureScreenOutlineFeature(Material outlineMaterial)
        {
            var rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererDataPath);
            if (rendererData == null)
                throw new InvalidOperationException("Renderer2D.asset not found at " + RendererDataPath);

            ScreenOutlineFeature feature = AssetDatabase.LoadAssetAtPath<ScreenOutlineFeature>(OutlineFeaturePath);
            if (feature == null)
            {
                feature = ScriptableObject.CreateInstance<ScreenOutlineFeature>();
                feature.name = "Screen Outline";
                AssetDatabase.CreateAsset(feature, OutlineFeaturePath);
            }

            // Persist the feature inside Renderer2D.asset. ScriptableRendererData.SetDirty()
            // only invalidates the in-memory renderer, so the serialized list is edited
            // through SerializedObject and the asset is saved explicitly.
            var so = new SerializedObject(rendererData);
            SerializedProperty features = so.FindProperty("m_RendererFeatures");

            bool alreadyListed = false;
            for (int i = 0; i < features.arraySize; i++)
            {
                if (features.GetArrayElementAtIndex(i).objectReferenceValue == feature)
                {
                    alreadyListed = true;
                    break;
                }
            }

            if (!alreadyListed)
            {
                features.arraySize++;
                features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            }

            so.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(rendererData);

            feature.outlineMaterial = outlineMaterial;
            feature.widthPixels = 1f;
            feature.edgeThreshold = 0.28f;
            EditorUtility.SetDirty(feature);

            AssetDatabase.SaveAssets();
        }

        static PhysicsMaterial2D EnsurePlayerMaterial()
        {
            return EnsurePhysicsMaterial(PlayerPhysicsMaterialPath, "PlayerPhysicsMaterial", 1f, 0f);
        }

        static PhysicsMaterial2D EnsureTerrainMaterial()
        {
            // One shared material for every obstacle (blocks and slope) so friction and
            // bounciness can be tuned from a single asset in the Inspector.
            return EnsurePhysicsMaterial(TerrainPhysicsMaterialPath, "TerrainPhysicsMaterial", 0.6f, 0f);
        }

        static PhysicsMaterial2D EnsurePhysicsMaterial(string path, string materialName, float friction, float bounciness)
        {
            var mat = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(path);
            if (mat == null)
            {
                mat = new PhysicsMaterial2D(materialName);
                mat.friction = friction;
                mat.bounciness = bounciness;
                AssetDatabase.CreateAsset(mat, path);
                EditorUtility.SetDirty(mat);
            }

            // An existing asset keeps the values tuned in the Inspector.
            return mat;
        }

        // ------------------------------------------------------------------
        // Prefabs
        // ------------------------------------------------------------------

        static GameObject BuildPlayerPrefab(
            Dictionary<string, Sprite> sprites, PhysicsMaterial2D playerMaterial, Material spriteMaterial)
        {
            var go = new GameObject("Player");
            go.layer = PlayerLayer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites["player_capsule"];
            sr.color = Color.white; // sprite is pre-coloured
            sr.sortingOrder = 100; // always drawn above the snapshot output (order 20)
            if (spriteMaterial != null) sr.sharedMaterial = spriteMaterial;

            var rb = go.AddComponent<Rigidbody2D>();
            rb.gravityScale = 1f;
            rb.mass = 1f;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;

            var col = go.AddComponent<CapsuleCollider2D>();
            col.direction = CapsuleDirection2D.Vertical;
            col.size = new Vector2(PlayerWidth, PlayerHeight);
            col.sharedMaterial = playerMaterial;

            var controller = go.AddComponent<PlayerController2D>();
            controller.jumpHeight = JumpHeight;
            controller.playerHeight = PlayerHeight;
            controller.groundMask = 1 << GroundLayer;

            return SavePrefab(go, PrefabDir + "/Player.prefab");
        }

        static Tile EnsureTile(string path, string tileName, Sprite sprite, Color color)
        {
            var tile = AssetDatabase.LoadAssetAtPath<Tile>(path);
            if (tile == null)
            {
                tile = ScriptableObject.CreateInstance<Tile>();
                AssetDatabase.CreateAsset(tile, path);
            }

            tile.name = tileName;
            tile.sprite = sprite;
            tile.color = color;
            tile.colliderType = Tile.ColliderType.Sprite; // use the sprite outline as the collision shape
            EditorUtility.SetDirty(tile);
            return tile;
        }

        static readonly string[] SlopeNames = { "UpRight", "UpLeft", "DownRight", "DownLeft" };
        static readonly string[] SlopeSpriteNames = { "slope_up_right", "slope_up_left", "slope_down_right", "slope_down_left" };

        static Tile[] EnsureSlopeTiles(Dictionary<string, Sprite> sprites)
        {
            var tiles = new Tile[SlopeNames.Length * 2];
            for (int i = 0; i < SlopeNames.Length; i++)
            {
                string name = SlopeNames[i];
                Sprite sprite = sprites[SlopeSpriteNames[i]];

                tiles[i] = EnsureTile($"Assets/Tiles/Slope{name}.asset", $"Slope{name}", sprite, TerrainColor);
                tiles[i + SlopeNames.Length] =
                    EnsureTile($"Assets/Tiles/Slope{name}Dark.asset", $"Slope{name}Dark", sprite, DarkTerrainColor);
            }

            return tiles;
        }

        static GameObject SavePrefab(GameObject go, string path)
        {
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(go, path);
            UnityEngine.Object.DestroyImmediate(go);
            return prefab;
        }

        // ------------------------------------------------------------------
        // Scene
        // ------------------------------------------------------------------

        static void BuildLevelScene(
            GameObject playerPrefab, Tile terrainTile, Tile darkTile, Tile[] slopeTiles,
            PhysicsMaterial2D terrainMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var level = new GameObject("Level").transform;

            // ---- Tilemap terrain: one Grid, one Tilemap, one merged collider ----
            var gridGo = new GameObject("Grid");
            gridGo.transform.SetParent(level);
            var grid = gridGo.AddComponent<Grid>();
            grid.cellSize = new Vector3(1f, 1f, 0f);

            var tilemapGo = new GameObject("Terrain");
            tilemapGo.layer = GroundLayer;
            tilemapGo.transform.SetParent(gridGo.transform);

            var tilemap = tilemapGo.AddComponent<Tilemap>();
            var tilemapRenderer = tilemapGo.AddComponent<TilemapRenderer>();
            tilemapRenderer.sortingOrder = 0;

            var body = tilemapGo.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;

            var composite = tilemapGo.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            composite.sharedMaterial = terrainMaterial;

            var tilemapCollider = tilemapGo.AddComponent<TilemapCollider2D>();
            tilemapCollider.usedByComposite = true;
            tilemapCollider.sharedMaterial = terrainMaterial;

            // ---- Ground: 2 cells tall, 4-cell pit between x=4 and x=8 ----
            for (int y = -2; y <= -1; y++)
            {
                PaintRow(tilemap, terrainTile, -12, 3, y);
                PaintRow(tilemap, terrainTile, 8, 23, y);
            }

            // ---- Floating platforms ----
            for (int y = 2; y <= 3; y++) PaintRow(tilemap, terrainTile, -8, -5, y);
            for (int y = 6; y <= 7; y++) PaintRow(tilemap, terrainTile, -4, -1, y);

            // ---- Detail cells: a 2-cell ledge and a 1-cell-wide pillar ----
            PaintRow(tilemap, terrainTile, 2, 3, 3);
            PaintColumn(tilemap, terrainTile, -2, 0, 1);

            // ---- Stairs: every step is 2 cells high ----
            for (int y = 0; y <= 1; y++) PaintRow(tilemap, terrainTile, 12, 15, y);
            for (int y = 0; y <= 3; y++) PaintRow(tilemap, terrainTile, 16, 23, y);

            // ---- Dark tiles: same colour merges, different colour never merges ----
            for (int y = 4; y <= 5; y++) PaintRow(tilemap, darkTile, 20, 23, y);

            // ---- 45-degree ramp: two UpRight triangles plus the solid cell between them ----
            PaintCell(tilemap, slopeTiles[0], 10, 0);
            PaintCell(tilemap, terrainTile, 11, 0);
            PaintCell(tilemap, slopeTiles[0], 11, 1);

            // Tiles were written after the collider was created, so force the collider to
            // process them now; otherwise the saved scene contains an empty collider.
            tilemap.RefreshAllTiles();
            tilemapCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.position = new Vector3(Spawn.x, Spawn.y, 0f);

            Camera mainCamera = BuildCamera(player);
            BuildCaptureGroups(mainCamera);
            BuildGlobalLight();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void PaintCell(Tilemap tilemap, Tile tile, int cellX, int cellY)
        {
            tilemap.SetTile(new Vector3Int(cellX, cellY, 0), tile);
        }

        static void PaintRow(Tilemap tilemap, Tile tile, int fromX, int toX, int cellY)
        {
            for (int x = fromX; x <= toX; x++) PaintCell(tilemap, tile, x, cellY);
        }

        static void PaintColumn(Tilemap tilemap, Tile tile, int cellX, int fromY, int toY)
        {
            for (int y = fromY; y <= toY; y++) PaintCell(tilemap, tile, cellX, y);
        }

        static Camera BuildCamera(GameObject player)
        {
            var camGo = new GameObject("Main Camera");
            camGo.tag = "MainCamera";
            camGo.transform.position = new Vector3(Spawn.x, Spawn.y + 1.2f, -10f);

            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.orthographicSize = 5.625f; // 11.25 units tall => ~20 units wide at 16:9
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = BackgroundColor;
            cam.nearClipPlane = 0.3f;
            cam.farClipPlane = 100f;

            camGo.AddComponent<AudioListener>();

            var urpData = camGo.AddComponent<UniversalAdditionalCameraData>();
            urpData.renderPostProcessing = false;

            var follow = camGo.AddComponent<CameraFollow2D>();
            follow.target = player.transform;
            follow.offset = new Vector2(0f, 1.0f);
            follow.smoothTime = 0.12f;
            follow.useLookAhead = true;
            follow.lookAheadDistance = 1.5f;
            follow.lookAheadSpeedReference = 9f;

            return cam;
        }

        static void BuildCaptureGroups(Camera mainCamera)
        {
            // Both groups live under one empty node and pair up by their side.
            var pair = new GameObject("Capture Display Pair");
            pair.transform.position = new Vector3(3f, 3f, 0f);

            CreateCaptureGroup("Capture Display Group A", pair.transform, new Vector3(-9f, 0f, 0f), CaptureGroupSide.A, mainCamera);
            CreateCaptureGroup("Capture Display Group B", pair.transform, new Vector3(9f, 0f, 0f), CaptureGroupSide.B, mainCamera);
        }

        static CaptureDisplayGroup CreateCaptureGroup(string name, Transform parent, Vector3 localPosition, CaptureGroupSide side, Camera mainCamera)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            // Every element of the group sits at the group origin (local position zero).
            var cameraGo = new GameObject("Snapshot Camera");
            cameraGo.transform.SetParent(root.transform, false);
            // Pull the camera back on Z; at z = 0 it sits in the map plane and sees nothing.
            cameraGo.transform.localPosition = new Vector3(0f, 0f, -10f);

            var capture = cameraGo.AddComponent<Camera>();
            capture.orthographic = true;
            capture.orthographicSize = mainCamera.orthographicSize * 1.2f;
            capture.clearFlags = CameraClearFlags.SolidColor;
            capture.backgroundColor = BackgroundColor;
            capture.nearClipPlane = 0.3f;
            capture.farClipPlane = 100f;
            capture.enabled = false; // rendered on demand by RegionSnapshotCamera

            var outputGo = new GameObject("Snapshot Output");
            outputGo.layer = SnapshotLayer;
            outputGo.transform.SetParent(root.transform, false);
            outputGo.transform.localPosition = Vector3.zero;

            var outputSprite = outputGo.AddComponent<SpriteRenderer>();
            outputSprite.sortingOrder = 20;

            var captureGo = new GameObject("Snapshot Capture");
            captureGo.transform.SetParent(root.transform, false);
            captureGo.transform.localPosition = Vector3.zero;

            var controller = captureGo.AddComponent<RegionSnapshotCamera>();
            controller.sourceCamera = mainCamera;
            controller.captureCamera = capture;
            controller.output = outputSprite;

            var group = root.AddComponent<CaptureDisplayGroup>();
            group.side = side;
            group.capture = controller;
            group.output = outputSprite;
            return group;
        }

        static void BuildGlobalLight()
        {
            var lightGo = new GameObject("Global Light 2D");
            var light = lightGo.AddComponent<Light2D>();
            light.lightType = Light2D.LightType.Global;
            light.color = Color.white;
            light.intensity = 1f;
        }

        // ------------------------------------------------------------------
        // Validation & preview
        // ------------------------------------------------------------------

        [MenuItem("Platformer/Validate Level", priority = 30)]
        public static void ValidateLevel()
        {
            if (!File.Exists(ScenePath))
            {
                Debug.LogError("[Platformer] Scene missing: " + ScenePath);
                return;
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath);
            int issues = 0;

            Camera cam = Camera.main;
            if (cam == null) { Debug.LogError("[Platformer] No MainCamera found."); issues++; }
            else
            {
                Debug.Log($"[Platformer] Camera: ortho={cam.orthographic}, size={cam.orthographicSize}, pos={cam.transform.position}");
                var follow = cam.GetComponent<CameraFollow2D>();
                if (follow == null || follow.target == null)
                {
                    Debug.LogError("[Platformer] CameraFollow2D target is not assigned.");
                    issues++;
                }
                else
                {
                    Debug.Log($"[Platformer] Camera follows '{follow.target.name}'.");
                }
            }

            var player = GameObject.Find("Player");
            if (player == null) { Debug.LogError("[Platformer] Player not found."); issues++; }
            else
            {
                var pc = player.GetComponent<PlayerController2D>();
                Debug.Log($"[Platformer] Player at {player.transform.position}, jumpHeight={(pc != null ? pc.jumpHeight : -1)}, layer={LayerMask.LayerToName(player.layer)}");
            }

            var rendererData = AssetDatabase.LoadAssetAtPath<Renderer2DData>(RendererDataPath);
            bool hasOutline = false;
            if (rendererData != null)
            {
                foreach (var feature in rendererData.rendererFeatures)
                    if (feature is ScreenOutlineFeature) hasOutline = true;
            }
            Debug.Log($"[Platformer] Screen outline feature: {(hasOutline ? "present" : "MISSING")}");
            if (!hasOutline) issues++;

            var tilemapGo = GameObject.Find("Terrain");
            var tilemap = tilemapGo != null ? tilemapGo.GetComponent<Tilemap>() : null;
            int tileCount = 0;
            if (tilemap != null)
            {
                BoundsInt bounds = tilemap.cellBounds;
                foreach (Vector3Int cell in bounds.allPositionsWithin)
                {
                    if (tilemap.HasTile(cell)) tileCount++;
                }
            }
            Debug.Log($"[Platformer] Tilemap cells painted: {tileCount}");
            if (tilemap == null || tileCount == 0)
            {
                Debug.LogError("[Platformer] Tilemap 'Terrain' is missing or empty.");
                issues++;
            }

            int sprites = 0;
            foreach (var go in scene.GetRootGameObjects())
                sprites += go.GetComponentsInChildren<SpriteRenderer>(true).Length;
            Debug.Log($"[Platformer] SpriteRenderers in scene (slope + player): {sprites}");

            var terrainMaterial = AssetDatabase.LoadAssetAtPath<PhysicsMaterial2D>(TerrainPhysicsMaterialPath);
            int obstacleColliders = 0;
            int withTerrainMaterial = 0;
            foreach (var go in scene.GetRootGameObjects())
            {
                foreach (var collider in go.GetComponentsInChildren<Collider2D>(true))
                {
                    if (collider.gameObject.layer != GroundLayer) continue;
                    obstacleColliders++;
                    if (collider.sharedMaterial == terrainMaterial) withTerrainMaterial++;
                }
            }

            Debug.Log($"[Platformer] Obstacle colliders: {obstacleColliders}, using TerrainPhysicsMaterial: {withTerrainMaterial}");
            if (terrainMaterial == null || withTerrainMaterial != obstacleColliders)
            {
                Debug.LogError("[Platformer] Not every obstacle uses the shared TerrainPhysicsMaterial.");
                issues++;
            }

            if (issues == 0) Debug.Log("[Platformer] Validation passed.");
            else Debug.LogError($"[Platformer] Validation finished with {issues} issue(s).");
        }

        [MenuItem("Platformer/Render Preview Image", priority = 31)]
        public static void RenderPreview()
        {
            ValidateLevel();
            if (!File.Exists(ScenePath)) return;

            Camera cam = Camera.main;
            if (cam == null) { Debug.LogError("[Platformer] No camera for preview."); return; }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            // Gameplay framing (as the player sees it at spawn).
            Vector3 gameplayPos = cam.transform.position;
            float gameplaySize = cam.orthographicSize;
            RenderShot(cam, Path.Combine(projectRoot, "Preview_Gameplay.png"), gameplayPos, gameplaySize);

            // The snapshot only renders during play; force one capture for the preview.
            var groups = UnityEngine.Object.FindObjectsOfType<CaptureDisplayGroup>();
            foreach (var group in groups)
            {
                group.ResolvePartner();
                group.WireUp();
                group.ApplyDisplayScale();
            }

            // A few passes so the cross-display feedback settles before rendering.
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var group in groups) if (group.capture != null) group.capture.CaptureNow();
            }

            foreach (var group in groups) group.RefreshDebugOutline();

            // Overview framed to fit every terrain and sprite renderer in the scene.
            Bounds bounds = new Bounds();
            bool hasBounds = false;
            foreach (var renderer in UnityEngine.Object.FindObjectsOfType<Renderer>())
            {
                if (!(renderer is SpriteRenderer) && !(renderer is TilemapRenderer)) continue;
                if (!hasBounds) { bounds = renderer.bounds; hasBounds = true; }
                else bounds.Encapsulate(renderer.bounds);
            }

            if (!hasBounds) bounds = new Bounds(new Vector3(6f, 3.5f, 0f), new Vector3(24f, 14f, 0f));

            const float aspect = 1280f / 720f;
            float overviewSize = Mathf.Max(bounds.extents.y, bounds.extents.x / aspect) * 1.05f;
            RenderShot(cam, Path.Combine(projectRoot, "Preview.png"),
                new Vector3(bounds.center.x, bounds.center.y, -10f), overviewSize);

            foreach (var group in groups) if (group.capture != null) group.capture.ClearOutput();

            cam.transform.position = gameplayPos;
            cam.orthographicSize = gameplaySize;
        }

        static void RenderShot(Camera cam, string path, Vector3 position, float orthographicSize)
        {
            const int width = 1280;
            const int height = 720;

            var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;

            cam.transform.position = position;
            cam.orthographicSize = orthographicSize;
            cam.aspect = (float)width / height;
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(width, height, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            tex.Apply();

            RenderTexture.active = previous;
            cam.targetTexture = null;

            File.WriteAllBytes(path, tex.EncodeToPNG());

            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
            UnityEngine.Object.DestroyImmediate(rt);

            Debug.Log("[Platformer] Preview written to " + path);
        }

        // ------------------------------------------------------------------
        // Helpers
        // ------------------------------------------------------------------

        static void EnsureFolder(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(projectRoot, assetPath));
            AssetDatabase.Refresh();
        }
    }
}
