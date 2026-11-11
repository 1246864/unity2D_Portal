using System;
using System.Collections.Generic;
using System.IO;
using Platformer.Rendering;
using UnityEditor;
using UnityEditor.SceneManagement;
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

        const float PlayerWidth = 0.72f;
        const float PlayerHeight = 1.5f;
        const float JumpHeight = 6f;

        static readonly Vector2 Spawn = new Vector2(-9f, 0.75f);

        // One colour for every terrain piece: floor, wall, step, platform.
        static readonly Color TerrainColor = new Color(0.40f, 0.46f, 0.56f);
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
                Material unlitMaterial = LoadUnlitSpriteMaterial();
                Material outlineMaterial = EnsureOutlineMaterial();

                GameObject playerPrefab = BuildPlayerPrefab(sprites, playerMaterial, litMaterial);
                GameObject slope2x2 = BuildSlopePrefab(sprites["slope2x2"], unlitMaterial, terrainMaterial);
                Tile terrainTile = EnsureTile(TerrainTilePath, "TerrainTile", sprites["tile1x1"], TerrainColor);
                Tile darkTile = EnsureTile(DarkTilePath, "DarkTerrainTile", sprites["tile1x1"], DarkTerrainColor);

                EnsureScreenOutlineFeature(outlineMaterial);
                BuildLevelScene(playerPrefab, terrainTile, darkTile, slope2x2, terrainMaterial);
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
            }

            AssetDatabase.Refresh();
        }

        static Dictionary<string, Sprite> LoadSprites()
        {
            var result = new Dictionary<string, Sprite>();
            foreach (string name in new[] { "tile1x1", "slope2x2", "player_capsule" })
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

        static Material LoadUnlitSpriteMaterial()
        {
            var mat = AssetDatabase.LoadAssetAtPath<Material>(
                "Packages/com.unity.render-pipelines.universal/Runtime/Materials/Sprite-Unlit-Default.mat");

            if (mat == null)
                Debug.LogWarning("[Platformer] Sprite-Unlit-Default not found; using default sprite material.");

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
            mat.SetFloat("_Threshold", 0.05f);
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
            feature.edgeThreshold = 0.05f;
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
            sr.sortingOrder = 10;
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

        static GameObject BuildSlopePrefab(Sprite sprite, Material material, PhysicsMaterial2D physicsMaterial)
        {
            var go = new GameObject("Slope2x2");
            go.layer = GroundLayer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprite;
            sr.color = TerrainColor;
            sr.sortingOrder = 0;
            if (material != null) sr.sharedMaterial = material;

            // 2x2 cell right triangle: flat bottom, vertical right edge, 45-degree hypotenuse.
            var col = go.AddComponent<PolygonCollider2D>();
            col.points = new[]
            {
                new Vector2(-1f, -1f),
                new Vector2(1f, -1f),
                new Vector2(1f, 1f)
            };
            col.sharedMaterial = physicsMaterial;

            return SavePrefab(go, PrefabDir + "/Slope2x2.prefab");
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
            GameObject playerPrefab, Tile terrainTile, Tile darkTile, GameObject slope2x2,
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

            // ---- Ground: two-cell thick slabs, 4-cell pit between x=4 and x=8 ----
            for (int x = -12; x <= 2; x += 2)
                PaintModule(tilemap, terrainTile, x, -2, 2, 2);
            for (int x = 8; x <= 22; x += 2)
                PaintModule(tilemap, terrainTile, x, -2, 2, 2);

            // ---- Floating platforms, painted in 2-cell modules ----
            PaintModule(tilemap, terrainTile, -8, 2, 2, 2);
            PaintModule(tilemap, terrainTile, -6, 2, 2, 2);
            PaintModule(tilemap, terrainTile, -4, 6, 2, 2);
            PaintModule(tilemap, terrainTile, -2, 6, 2, 2);

            // Fine-detail pieces: a 2x1 ledge and a 1x2 pillar.
            PaintModule(tilemap, terrainTile, 2, 3, 2, 1);
            PaintModule(tilemap, terrainTile, -2, 0, 1, 2);

            // ---- Stairs: every step is exactly 2 cells high ----
            PaintModule(tilemap, terrainTile, 12, 0, 2, 2);
            PaintModule(tilemap, terrainTile, 14, 0, 2, 2);

            for (int x = 16; x <= 22; x += 2)
            {
                PaintModule(tilemap, terrainTile, x, 0, 2, 2);
                PaintModule(tilemap, terrainTile, x, 2, 2, 2);
            }

            // ---- Dark tiles: same-colour merges, different colour never merges ----
            PaintModule(tilemap, darkTile, 20, 4, 2, 2);
            PaintModule(tilemap, darkTile, 22, 4, 2, 2);

            // Tiles were written after the collider was created, so force the collider to
            // process them now; otherwise the saved scene contains an empty collider.
            tilemap.RefreshAllTiles();
            tilemapCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();

            // ---- Single 45-degree slope, 2 cells high, leading onto the stairs ----
            var slopeRoot = new GameObject("Slopes").transform;
            slopeRoot.SetParent(level);
            var slope = (GameObject)PrefabUtility.InstantiatePrefab(slope2x2, slopeRoot);
            slope.name = "Slope45 (2x2)";
            slope.transform.position = new Vector3(11f, 1f, 0f);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.position = new Vector3(Spawn.x, Spawn.y, 0f);

            BuildCamera(player);
            BuildGlobalLight();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, ScenePath);
        }

        static void PaintModule(Tilemap tilemap, Tile tile, int cellX, int cellY, int cellsW, int cellsH)
        {
            for (int x = 0; x < cellsW; x++)
            {
                for (int y = 0; y < cellsH; y++)
                {
                    tilemap.SetTile(new Vector3Int(cellX + x, cellY + y, 0), tile);
                }
            }
        }

        static void BuildCamera(GameObject player)
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

            var slope = GameObject.Find("Slope45 (2x2)");
            if (slope == null) { Debug.LogError("[Platformer] Slope not found."); issues++; }
            else Debug.Log($"[Platformer] Slope at {slope.transform.position}");

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

            // Wide overview of the whole level.
            RenderShot(cam, Path.Combine(projectRoot, "Preview.png"),
                new Vector3(6f, 3.5f, -10f), 10.5f);

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
