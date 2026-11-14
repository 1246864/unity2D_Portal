using System;
using System.Collections.Generic;
using System.IO;
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
    /// 从零构建可运行的模板：导入占位美术、创建预制体、搭建关卡并保存场景。
    ///
    /// 菜单：Platformer > Rebuild Level + Prefabs
    /// 批处理：-executeMethod Platformer.EditorTools.ProjectBuilder.BuildAll -quit
    /// </summary>
    public static class ProjectBuilder
    {
        const string ArtDir = "Assets/Art/Placeholders";
        const string PrefabDir = "Assets/Prefabs";
        const string SceneDir = "Assets/Scenes";
        const string ScenePath = "Assets/Scenes/Level01.unity";
        const string PlayerPhysicsMaterialPath = "Assets/Settings/PlayerPhysicsMaterial.physicsMaterial2D";
        const string TerrainPhysicsMaterialPath = "Assets/Settings/TerrainPhysicsMaterial.physicsMaterial2D";
        const string TileDir = "Assets/Tiles";
        const string TerrainTilePath = "Assets/Tiles/TerrainTile.asset";
        const string DarkTilePath = "Assets/Tiles/DarkTerrainTile.asset";

        const int GroundLayer = 6;
        const int PlayerLayer = 7;
        const int SnapshotLayer = 8;

        const float ViewScale = 1.2f;
        const float PortalOpeningHeight = 3f;
        const string MaskSpritePath = "Assets/Art/Placeholders/mask_white.png";
        const string PortalALayerName = "Portal A";
        const string PortalBLayerName = "Portal B";
        const string PlayerSortingLayer = "Player";

        const float PlayerWidth = 0.72f;
        const float PlayerHeight = 1.5f;
        const float JumpHeight = 6f;

        static readonly Vector2 Spawn = new Vector2(-9f, 0.75f);

        // 所有地形（地板、墙、台阶、平台）共用同一种颜色。
        static readonly Color TerrainColor = new Color(0.8784314f, 0.8784314f, 0.8784314f); // #E0E0E0
        static readonly Color DarkTerrainColor = new Color(0.27f, 0.30f, 0.36f);
        static readonly Color BackgroundColor = new Color(0.086f, 0.102f, 0.153f);

        [MenuItem("Platformer/Rebuild Level + Prefabs", priority = 10)]
        public static void BuildAll()
        {
            try
            {
                EnsureFolder(ArtDir);
                EnsureFolder(PrefabDir);
                EnsureFolder(SceneDir);
                EnsureFolder(TileDir);

                ConfigureLayers();
                ConfigurePhysics();
                ConfigurePlayerSettings();
                ImportSprites();

                Dictionary<string, Sprite> sprites = LoadSprites();
                PhysicsMaterial2D playerMaterial = EnsurePlayerMaterial();
                PhysicsMaterial2D terrainMaterial = EnsureTerrainMaterial();
                Material litMaterial = LoadLitSpriteMaterial();

                GameObject playerPrefab = BuildPlayerPrefab(sprites, playerMaterial, litMaterial);
                Tile terrainTile = EnsureTile(TerrainTilePath, "TerrainTile", sprites["tile1x1"], TerrainColor);
                Tile darkTile = EnsureTile(DarkTilePath, "DarkTerrainTile", sprites["tile1x1"], DarkTerrainColor);
                Tile[] slopeTiles = EnsureSlopeTiles(sprites);

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
        // 工程设置
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

            ConfigureSortingLayer(so, "Portal A");
            ConfigureSortingLayer(so, "Portal B");
            ConfigureSortingLayer(so, PlayerSortingLayer);

            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.SaveAssets();
        }

        static void ConfigureSortingLayer(SerializedObject tagManager, string name)
        {
            SerializedProperty sortingLayers = tagManager.FindProperty("m_SortingLayers");
            for (int i = 0; i < sortingLayers.arraySize; i++)
            {
                SerializedProperty element = sortingLayers.GetArrayElementAtIndex(i);
                if (element.FindPropertyRelative("name").stringValue == name) return;
            }

            sortingLayers.InsertArrayElementAtIndex(sortingLayers.arraySize);
            SerializedProperty added = sortingLayers.GetArrayElementAtIndex(sortingLayers.arraySize - 1);
            added.FindPropertyRelative("name").stringValue = name;
            added.FindPropertyRelative("uniqueID").intValue = name.GetHashCode();
            added.FindPropertyRelative("locked").intValue = 0;
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
            // 比真实重力更强的重力，让 6 格跳跃手感更干脆。
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
        // 美术导入
        // ------------------------------------------------------------------

        // 斜坡精灵的精确三角形物理形状。若不指定，Unity 的默认形状会
        // 沿抗锯齿的斜边内缩，导致玩家陷进斜坡。
        // 坐标是以精灵轴心（中心）为原点的像素值，逆时针方向。
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

        static PhysicsMaterial2D EnsurePlayerMaterial()
        {
            return EnsurePhysicsMaterial(PlayerPhysicsMaterialPath, "PlayerPhysicsMaterial", 1f, 0f);
        }

        static PhysicsMaterial2D EnsureTerrainMaterial()
        {
            // 所有障碍物（方块和斜坡）共用一个材质，这样在 Inspector 里
            // 改一次，摩擦和弹性就全部生效。
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

            // 已存在的资产保留在 Inspector 里调好的数值。
            return mat;
        }

        // ------------------------------------------------------------------
        // 预制体
        // ------------------------------------------------------------------

        static GameObject BuildPlayerPrefab(
            Dictionary<string, Sprite> sprites, PhysicsMaterial2D playerMaterial, Material spriteMaterial)
        {
            var go = new GameObject("Player");
            go.layer = PlayerLayer;

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite = sprites["player_capsule"];
            sr.color = Color.white; // 精灵已经着色
            sr.sortingLayerName = PlayerSortingLayer; // 绘制在两扇门的画面之上
            sr.sortingOrder = 100;
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
            tile.colliderType = Tile.ColliderType.Sprite; // 用精灵轮廓作为碰撞形状
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
        // 场景
        // ------------------------------------------------------------------

        static void BuildLevelScene(
            GameObject playerPrefab, Tile terrainTile, Tile darkTile, Tile[] slopeTiles,
            PhysicsMaterial2D terrainMaterial)
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var level = new GameObject("Level").transform;

            // ---- Tilemap 地形：一个 Grid、一个 Tilemap、一个合并碰撞体 ----
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

            // 描边由独立的 Tilemap 层按邻居规则生成，不再使用全屏描边 shader。
            tilemapGo.AddComponent<TilemapOutline>();

            var body = tilemapGo.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;

            var composite = tilemapGo.AddComponent<CompositeCollider2D>();
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous;
            composite.sharedMaterial = terrainMaterial;

            var tilemapCollider = tilemapGo.AddComponent<TilemapCollider2D>();
            tilemapCollider.usedByComposite = true;
            tilemapCollider.sharedMaterial = terrainMaterial;

            // ---- 地面：2 格厚，x=4 到 x=8 之间有一个 4 格宽的坑 ----
            for (int y = -2; y <= -1; y++)
            {
                PaintRow(tilemap, terrainTile, -12, 3, y);
                PaintRow(tilemap, terrainTile, 8, 23, y);
            }

            // ---- 悬浮平台 ----
            for (int y = 2; y <= 3; y++) PaintRow(tilemap, terrainTile, -8, -5, y);
            for (int y = 6; y <= 7; y++) PaintRow(tilemap, terrainTile, -4, -1, y);

            // ---- 细节瓦片：2 格宽的台阶板和 1 格宽的立柱 ----
            PaintRow(tilemap, terrainTile, 2, 3, 3);
            PaintColumn(tilemap, terrainTile, -2, 0, 1);

            // ---- 台阶：每级高 2 格 ----
            for (int y = 0; y <= 1; y++) PaintRow(tilemap, terrainTile, 12, 15, y);
            for (int y = 0; y <= 3; y++) PaintRow(tilemap, terrainTile, 16, 23, y);

            // ---- 深灰瓦片：同色融合，异色永不融合 ----
            for (int y = 4; y <= 5; y++) PaintRow(tilemap, darkTile, 20, 23, y);

            // ---- 45 度坡道：两个右上斜坡瓦片，中间夹一个实心瓦片 ----
            PaintCell(tilemap, slopeTiles[0], 10, 0);
            PaintCell(tilemap, terrainTile, 11, 0);
            PaintCell(tilemap, slopeTiles[0], 11, 1);

            // 瓦片是在创建碰撞体之后才写入的，这里强制碰撞体立即处理，
            // 否则保存的场景里碰撞体是空的。
            tilemap.RefreshAllTiles();
            tilemapCollider.ProcessTilemapChanges();
            composite.GenerateGeometry();

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Player";
            player.transform.position = new Vector3(Spawn.x, Spawn.y, 0f);

            Camera mainCamera = BuildCamera(player);
            BuildCaptureGroups(mainCamera, player);
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
            cam.orthographicSize = 5.625f; // 高 11.25 单位，16:9 下宽约 20 单位
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

        static void BuildCaptureGroups(Camera mainCamera, GameObject player)
        {
            // 两组都放在同一个空节点下，按 side 自动配对。
            var pair = new GameObject("Portal Pair");
            pair.transform.position = new Vector3(3f, 3f, 0f);

            // 门对控制器：保存玩家引用，供画面遮罩使用。
            var pairControl = pair.AddComponent<PortalPair>();
            pairControl.player = player.transform;

            CreateCaptureGroup("Portal A", pair.transform, new Vector3(0.83f, -1.46f, 0f), CaptureGroupSide.A, mainCamera);
            CreateCaptureGroup("Portal B", pair.transform, new Vector3(-14.89f, -1.48f, 0f), CaptureGroupSide.B, mainCamera);
        }

        static void BuildPortalMaskRig(Transform group, CaptureGroupSide side, string sortingLayer, int sortingOrder,
            out Transform maskTop, out Transform maskBottom)
        {
            maskTop = null;
            maskBottom = null;

            var sprite = AssetDatabase.LoadAssetAtPath<Sprite>(MaskSpritePath);
            if (sprite == null)
            {
                Debug.LogWarning("[Platformer] Mask sprite missing at " + MaskSpritePath);
                return;
            }

            var root = new GameObject("Portal Mask");
            root.transform.SetParent(group, false);

            // A 门的整块遮罩要上下翻转，B 门不翻转。
            float flip = side == CaptureGroupSide.A ? -1f : 1f;
            maskTop = CreateMaskUnit("View Mask Top", root.transform, sprite, sortingLayer, sortingOrder, 0f, 50f, flip);
            maskBottom = CreateMaskUnit("View Mask Bottom", root.transform, sprite, sortingLayer, sortingOrder, 0f, -50f, flip);
        }

        static Transform CreateMaskUnit(string name, Transform parent, Sprite sprite, string sortingLayer,
            int sortingOrder, float x, float y, float flipY)
        {
            var unit = new GameObject(name);
            unit.transform.SetParent(parent, false);
            unit.transform.localScale = new Vector3(1f, flipY, 1f);

            // 真正裁剪画面的是下面那个巨大的 SpriteMask 子节点；
            // 这个节点只负责携带由 CaptureDisplayGroup 驱动的偏移和旋转。
            var maskGo = new GameObject("Mask");
            maskGo.transform.SetParent(unit.transform, false);
            maskGo.transform.localPosition = new Vector3(x, y, 0f);
            maskGo.transform.localScale = new Vector3(200f, 100f, 1f);

            var mask = maskGo.AddComponent<SpriteMask>();
            mask.sprite = sprite;
            mask.alphaCutoff = 0.01f;
            mask.frontSortingOrder = sortingOrder + 20;
            mask.backSortingOrder = 0;
            int layerId = SortingLayer.NameToID(sortingLayer);
            mask.frontSortingLayerID = layerId;
            mask.backSortingLayerID = layerId;

            return unit.transform;
        }
        static CaptureDisplayGroup CreateCaptureGroup(string name, Transform parent, Vector3 localPosition, CaptureGroupSide side, Camera mainCamera)
        {
            var root = new GameObject(name);
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;

            // 组内所有元素都位于组原点（localPosition 为 0）。
            var cameraGo = new GameObject("Portal Camera");
            cameraGo.transform.SetParent(root.transform, false);
            // 把相机沿 Z 轴后拉；z = 0 时它位于地图平面内，什么都看不到。
            cameraGo.transform.localPosition = new Vector3(0f, 0f, -10f);

            var capture = cameraGo.AddComponent<Camera>();
            capture.orthographic = true;
            capture.orthographicSize = mainCamera.orthographicSize * 1.2f;
            capture.clearFlags = CameraClearFlags.SolidColor;
            capture.backgroundColor = BackgroundColor;
            capture.nearClipPlane = 0.3f;
            capture.farClipPlane = 100f;
            capture.enabled = false; // 由 RegionSnapshotCamera 按需渲染

            // 排除 Portal View 所在的 Snapshot 层，避免传送门相机拍到自己的输出形成递归。
            capture.cullingMask &= ~(1 << SnapshotLayer);

            // 视觉中心：Portal View 挂在它下面，方便整体移动画面而不动门/碰撞箱。
            var viewCenterGo = new GameObject("Portal View Center");
            viewCenterGo.transform.SetParent(root.transform, false);
            viewCenterGo.transform.localPosition = Vector3.zero;

            var outputGo = new GameObject("Portal View");
            outputGo.layer = SnapshotLayer;
            outputGo.transform.SetParent(viewCenterGo.transform, false);
            outputGo.transform.localPosition = Vector3.zero;

            var outputSprite = outputGo.AddComponent<SpriteRenderer>();
            outputSprite.sortingOrder = 20;
            // 每扇门使用自己的排序层，这样它的遮罩只会裁剪自己的画面。
            string sortingLayer = side == CaptureGroupSide.A ? PortalALayerName : PortalBLayerName;
            outputSprite.sortingLayerName = sortingLayer;
            outputSprite.maskInteraction = SpriteMaskInteraction.VisibleOutsideMask;

            // 画面下面放两块遮挡：除门洞那条带以外全部遮住，
            // 于是传送门只从一条水平缝隙里透出画面。
            BuildPortalMaskRig(root.transform, side, sortingLayer, outputSprite.sortingOrder + 1,
                out Transform maskTop, out Transform maskBottom);

            var captureGo = new GameObject("Portal Capture");
            captureGo.transform.SetParent(root.transform, false);
            captureGo.transform.localPosition = Vector3.zero;

            var controller = captureGo.AddComponent<RegionSnapshotCamera>();
            controller.sourceCamera = mainCamera;
            controller.captureCamera = capture;
            controller.output = outputSprite;

            var group = root.AddComponent<CaptureDisplayGroup>();
            group.side = side;
            group.Facing = side == CaptureGroupSide.A ? PortalFacing.Left : PortalFacing.Right;
            group.openDuration = 0.3f;
            group.capture = controller;
            group.output = outputSprite;
            group.viewCenter = viewCenterGo.transform;
            group.maskTop = maskTop;
            group.maskBottom = maskBottom;
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
        // 校验与预览
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

            var terrainForOutline = GameObject.Find("Terrain");
            var outlineComponent = terrainForOutline != null
                ? terrainForOutline.GetComponent<TilemapOutline>()
                : null;
            Debug.Log($"[Platformer] Tilemap outline component: {(outlineComponent != null ? "present" : "MISSING")}");
            if (outlineComponent == null) issues++;

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

            // 描边层是运行时的；出预览图前强制生成一次。
            var terrainForOutline = GameObject.Find("Terrain");
            var outline = terrainForOutline != null ? terrainForOutline.GetComponent<TilemapOutline>() : null;
            if (outline != null) outline.Build();

            Camera cam = Camera.main;
            if (cam == null) { Debug.LogError("[Platformer] No camera for preview."); return; }

            string projectRoot = Directory.GetParent(Application.dataPath).FullName;

            // 游戏视角（玩家出生时看到的画面）。
            Vector3 gameplayPos = cam.transform.position;
            float gameplaySize = cam.orthographicSize;
            RenderShot(cam, Path.Combine(projectRoot, "Preview_Gameplay.png"), gameplayPos, gameplaySize);

            // 快照只在运行时渲染；为了出预览图，这里强制捕获一次。
            var groups = UnityEngine.Object.FindObjectsOfType<CaptureDisplayGroup>();
            foreach (var group in groups)
            {
                group.ResolvePartner();
                group.WireUp();
            }

            // 多跑几轮，让交叉显示的反馈在出图前稳定下来。
            for (int pass = 0; pass < 3; pass++)
            {
                foreach (var group in groups) if (group.capture != null) group.capture.CaptureNow();
            }

            foreach (var group in groups)
            {
                group.ResolveMaskRig();
                group.ApplyHalfCrop();
                group.UpdateMasks();
            }


            // 俯瞰视角：取景范围覆盖场景中所有地形和精灵渲染器。
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
        // 辅助函数
        // ------------------------------------------------------------------

        static void EnsureFolder(string assetPath)
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            Directory.CreateDirectory(Path.Combine(projectRoot, assetPath));
            AssetDatabase.Refresh();
        }
    }
}
