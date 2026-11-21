using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace Platformer.Tests
{
    /// <summary>
    /// PlayMode 测试专用的最小传送门环境。所有层级和参数都由测试创建，
    /// 不依赖正式关卡中的对象名称、坐标或 Inspector 配置。
    /// </summary>
    internal sealed class PortalTestRig
    {
        public GameObject Root { get; private set; }
        public PortalPair Pair { get; private set; }
        public CaptureDisplayGroup A { get; private set; }
        public CaptureDisplayGroup B { get; private set; }
        public PlayerController2D Player { get; private set; }
        public Camera MainCamera { get; private set; }
        public PlayerControlManager Control { get; private set; }
        public CameraFollow2D Follow { get; private set; }
        public TilemapTestRig Terrain { get; private set; }

        Texture2D _viewTexture;
        Sprite _viewSprite;

        public static PortalTestRig Create(bool withTerrain = false)
        {
            var rig = new PortalTestRig
            {
                Root = new GameObject("Portal PlayMode Test Rig")
            };
            rig.Root.SetActive(false);

            // 即使无图形设备也要能验证半屏裁剪；快照用例会单独跳过真实渲染断言。
            rig._viewTexture = new Texture2D(64, 32, TextureFormat.RGBA32, false)
            {
                name = "Test Portal View Texture",
                hideFlags = HideFlags.DontSave
            };
            rig._viewSprite = Sprite.Create(
                rig._viewTexture, new Rect(0f, 0f, 64f, 32f),
                new Vector2(0.5f, 0.5f), 40f);
            rig._viewSprite.name = "Test Portal View Sprite";
            rig._viewSprite.hideFlags = HideFlags.DontSave;

            if (withTerrain)
                rig.Terrain = TilemapTestRig.Create(rig.Root.transform);

            rig.Player = PlayTestFixtures.CreatePlayer(
                rig.Root.transform, new Vector3(-10f, 4f, 0f), gravityScale: 0f);

            var cameraGo = PlayTestFixtures.CreateChild(rig.Root.transform, "Main Test Camera");
            cameraGo.tag = "MainCamera";
            cameraGo.transform.localPosition = new Vector3(0f, 1.2f, -10f);
            rig.MainCamera = cameraGo.AddComponent<Camera>();
            rig.MainCamera.orthographic = true;
            rig.MainCamera.orthographicSize = 5f;
            rig.Control = cameraGo.AddComponent<PlayerControlManager>();
            rig.Follow = cameraGo.AddComponent<CameraFollow2D>();
            rig.Control.player = rig.Player;
            rig.Follow.target = rig.Player.transform;
            rig.Follow.useLookAhead = false;

            rig.Pair = rig.Root.AddComponent<PortalPair>();
            rig.Pair.player = rig.Player.transform;
            rig.Pair.playerControl = rig.Control;
            rig.Pair.carveTerrain = withTerrain;

            if (withTerrain)
            {
                var carver = rig.Root.AddComponent<PortalTerrainCarver>();
                carver.terrain = rig.Terrain.Tilemap;
                carver.terrainCollider = rig.Terrain.TilemapCollider;
                carver.composite = rig.Terrain.Composite;
            }

            rig.A = CreatePortal(rig, CaptureGroupSide.A, new Vector3(-4f, 0f, 0f));
            rig.B = CreatePortal(rig, CaptureGroupSide.B, new Vector3(4f, 0f, 0f));
            rig.A.Facing = PortalFacing.Right;
            rig.B.Facing = PortalFacing.Left;

            rig.Root.SetActive(true);
            return rig;
        }

        static CaptureDisplayGroup CreatePortal(
            PortalTestRig rig,
            CaptureGroupSide side,
            Vector3 localPosition)
        {
            var portalGo = PlayTestFixtures.CreateChild(rig.Root.transform, $"Test Portal {side}");
            portalGo.transform.localPosition = localPosition;

            var center = PlayTestFixtures.CreateChild(portalGo.transform, "Portal View Center");
            center.transform.localPosition = new Vector3(0f, 0f, -2f);
            var view = PlayTestFixtures.CreateChild(center.transform, "Portal View");
            view.transform.localPosition = new Vector3(0f, 0f, 2f);
            view.layer = 8;
            var output = view.AddComponent<SpriteRenderer>();
            output.sprite = rig._viewSprite;
            output.sortingOrder = 20;

            var maskRoot = PlayTestFixtures.CreateChild(portalGo.transform, "Portal Mask");
            var maskTop = PlayTestFixtures.CreateChild(maskRoot.transform, "View Mask Top");
            var maskBottom = PlayTestFixtures.CreateChild(maskRoot.transform, "View Mask Bottom");

            var hint = PlayTestFixtures.CreateChild(portalGo.transform, "Portal Hint");
            hint.transform.localScale = new Vector3(
                0.45f, rig.Pair.PortalHeight + 0.3f, 1f);
            var hintRenderer = hint.AddComponent<SpriteRenderer>();

            var particlesGo = PlayTestFixtures.CreateChild(portalGo.transform, "Portal Hint Particles");
            var particles = particlesGo.AddComponent<ParticleSystem>();
            var main = particles.main;
            main.playOnAwake = false;
            var particlesRenderer = particlesGo.GetComponent<ParticleSystemRenderer>();
            particlesRenderer.sortingOrder = 1;

            var captureGo = PlayTestFixtures.CreateChild(portalGo.transform, "Portal Capture Camera");
            captureGo.transform.localPosition = new Vector3(0f, 0f, -10f);
            var captureCamera = captureGo.AddComponent<Camera>();
            captureCamera.orthographic = true;
            captureCamera.clearFlags = CameraClearFlags.SolidColor;
            captureCamera.backgroundColor = Color.clear;
            captureCamera.depth = -10f;
            var capture = captureGo.AddComponent<RegionSnapshotCamera>();
            capture.captureCamera = captureCamera;
            capture.sourceCamera = rig.MainCamera;
            capture.pixelsPerUnit = 4f;
            capture.maxTextureSize = 2048;
            capture.captureInterval = 1f;

            var group = portalGo.AddComponent<CaptureDisplayGroup>();
            group.side = side;
            group.capture = capture;
            group.output = output;
            group.viewCenter = center.transform;
            group.maskTop = maskTop.transform;
            group.maskBottom = maskBottom.transform;
            group.hintRenderer = hintRenderer;
            group.hintParticles = particles;
            group.connectionRevealDuration = 0f;
            return group;
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Root.SetActive(false);
                Object.Destroy(Root);
                Root = null;
            }
            if (Terrain != null) Terrain.DestroyAssets();
            if (_viewSprite != null) Object.Destroy(_viewSprite);
            if (_viewTexture != null) Object.Destroy(_viewTexture);
            _viewSprite = null;
            _viewTexture = null;
        }
    }

    /// <summary>运行时 Tilemap、碰撞体及其拥有的测试资源。</summary>
    internal sealed class TilemapTestRig
    {
        readonly List<Object> _assets = new List<Object>();

        public GameObject Root { get; private set; }
        public GameObject TerrainObject { get; private set; }
        public Tilemap Tilemap { get; private set; }
        public TilemapCollider2D TilemapCollider { get; private set; }
        public CompositeCollider2D Composite { get; private set; }

        public static TilemapTestRig Create(Transform parent)
        {
            var rig = new TilemapTestRig
            {
                Root = PlayTestFixtures.CreateChild(parent, "Test Tilemap Grid")
            };
            rig.Root.AddComponent<Grid>();

            rig.TerrainObject = PlayTestFixtures.CreateChild(rig.Root.transform, "Test Terrain");
            rig.TerrainObject.layer = 6;
            rig.Tilemap = rig.TerrainObject.AddComponent<Tilemap>();
            rig.TerrainObject.AddComponent<TilemapRenderer>();

            var body = rig.TerrainObject.AddComponent<Rigidbody2D>();
            body.bodyType = RigidbodyType2D.Static;
            rig.TilemapCollider = rig.TerrainObject.AddComponent<TilemapCollider2D>();
            rig.TilemapCollider.usedByComposite = true;
            var material = new PhysicsMaterial2D("Test Ground Material");
            rig._assets.Add(material);
            rig.TilemapCollider.sharedMaterial = material;
            rig.Composite = rig.TerrainObject.AddComponent<CompositeCollider2D>();
            rig.Composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            return rig;
        }

        public Tile CreateSolidTile()
        {
            return CreateTile("Test Solid", new[]
            {
                new Vector2(0f, 0f),
                new Vector2(32f, 0f),
                new Vector2(32f, 32f),
                new Vector2(0f, 32f)
            });
        }

        public Tile CreateSlopeTile()
        {
            return CreateTile("Test Slope UpLeft", new[]
            {
                new Vector2(0f, 0f),
                new Vector2(32f, 0f),
                new Vector2(0f, 32f)
            });
        }

        Tile CreateTile(string name, Vector2[] physicsShape)
        {
            // OverridePhysicsShape 使用相对纹理矩形左下角的像素坐标。
            const int size = 32;
            var pixels = new Color32[size * size];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = Color.white;

            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                name = name + " Texture",
                hideFlags = HideFlags.DontSave
            };
            texture.SetPixels32(pixels);
            texture.Apply();

            var sprite = Sprite.Create(
                texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size);
            sprite.name = name + " Sprite";
            sprite.hideFlags = HideFlags.DontSave;
            sprite.OverridePhysicsShape(new[] { physicsShape });

            var tile = ScriptableObject.CreateInstance<Tile>();
            tile.name = name;
            tile.sprite = sprite;
            tile.colliderType = Tile.ColliderType.Sprite;
            tile.hideFlags = HideFlags.DontSave;

            _assets.Add(tile);
            _assets.Add(sprite);
            _assets.Add(texture);
            return tile;
        }

        public void FillSolidRect(Tile tile, int xMin, int xMax, int yMin, int yMax)
        {
            for (int x = xMin; x <= xMax; x++)
            {
                for (int y = yMin; y <= yMax; y++)
                    Tilemap.SetTile(new Vector3Int(x, y, 0), tile);
            }
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Root.SetActive(false);
                Object.Destroy(Root);
                Root = null;
            }
            DestroyAssets();
        }

        public void DestroyAssets()
        {
            foreach (Object asset in _assets)
            {
                if (asset != null) Object.Destroy(asset);
            }
            _assets.Clear();
        }
    }

    internal static class PlayTestFixtures
    {
        public static GameObject CreateChild(Transform parent, string name)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            return go;
        }

        public static PlayerController2D CreatePlayer(
            Transform parent,
            Vector3 position,
            float gravityScale = 4f)
        {
            var player = CreateChild(parent, "Test Player");
            player.SetActive(false);
            player.transform.position = position;

            var body = player.AddComponent<Rigidbody2D>();
            body.gravityScale = gravityScale;
            body.freezeRotation = true;
            body.interpolation = RigidbodyInterpolation2D.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

            var capsule = player.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(0.72f, 1.5f);
            capsule.sharedMaterial = new PhysicsMaterial2D("Test Player Zero Friction")
            {
                friction = 0f,
                bounciness = 0f,
                hideFlags = HideFlags.DontSave
            };

            var renderer = player.AddComponent<SpriteRenderer>();
            renderer.sortingOrder = 100;

            var controller = player.AddComponent<PlayerController2D>();
            controller.groundMask = 1 << 6;
            player.SetActive(true);
            return controller;
        }
    }
}
