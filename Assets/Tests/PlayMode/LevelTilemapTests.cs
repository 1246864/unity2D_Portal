using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.Tilemaps;

namespace Platformer.Tests
{
    /// <summary>
    /// Tilemap、玩家和捕获相机的组件级 PlayMode 测试。
    /// 每个用例只创建自身所需的地形和对象，不加载正式关卡。
    /// </summary>
    public class LevelTilemapTests
    {
        TilemapTestRig _terrainRig;
        PortalTestRig _portalRig;
        PlayerController2D _player;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_player != null) Object.Destroy(_player.gameObject);
            if (_portalRig != null) _portalRig.Destroy();
            if (_terrainRig != null) _terrainRig.Destroy();
            yield return null;
        }

        [UnityTest]
        public IEnumerator Player_Stands_On_Generated_Tilemap()
        {
            _terrainRig = TilemapTestRig.Create(null);
            Tile solid = _terrainRig.CreateSolidTile();
            _terrainRig.FillSolidRect(solid, -20, 20, -2, -1);
            _terrainRig.TilemapCollider.ProcessTilemapChanges();
            _terrainRig.Composite.GenerateGeometry();

            _player = PlayTestFixtures.CreatePlayer(null, new Vector3(0f, 0.9f, 0f));
            for (int i = 0; i < 20; i++)
                yield return new WaitForFixedUpdate();

            int cells = 0;
            foreach (Vector3Int position in _terrainRig.Tilemap.cellBounds.allPositionsWithin)
            {
                if (_terrainRig.Tilemap.HasTile(position)) cells++;
            }
            Assert.AreEqual(41 * 2, cells, "夹具应生成指定的两行地面瓦片。");

            var bodyCollider = _player.GetComponent<Collider2D>();
            float groundTop = _terrainRig.Tilemap.GetCellCenterWorld(new Vector3Int(0, -1, 0)).y +
                              _terrainRig.Tilemap.layoutGrid.cellSize.y * 0.5f;
            float expectedY = groundTop + bodyCollider.bounds.size.y * 0.5f;
            Assert.IsTrue(_player.IsGrounded, "玩家应站在临时 Tilemap 的合并碰撞体上。");
            Assert.AreEqual(expectedY, _player.transform.position.y, 0.15f,
                "落地高度应由临时瓦片顶面和身体碰撞体高度计算。");
        }

        [UnityTest]
        public IEnumerator Generated_Tilemap_Has_Outline_Layer()
        {
            _terrainRig = TilemapTestRig.Create(null);
            Tile solid = _terrainRig.CreateSolidTile();
            _terrainRig.FillSolidRect(solid, 0, 1, 0, 1);
            _terrainRig.TerrainObject.AddComponent<TilemapOutline>();
            yield return null;

            Transform outlineGo = _terrainRig.TerrainObject.transform.parent.Find("Outline");
            Assert.IsNotNull(outlineGo, "TilemapOutline 应在同一 Grid 下创建描边层。");
            var outline = outlineGo.GetComponent<Tilemap>();
            Assert.IsNotNull(outline);

            int cells = 0;
            foreach (Vector3Int position in outline.cellBounds.allPositionsWithin)
            {
                if (outline.HasTile(position)) cells++;
            }
            Assert.Greater(cells, 0, "描边层应根据临时地形的邻居规则生成瓦片。");
        }

        [UnityTest]
        public IEnumerator Portal_View_Keeps_Offset_Under_View_Center()
        {
            _portalRig = PortalTestRig.Create();
            yield return null;

            CaptureDisplayGroup groupA = _portalRig.A;
            CaptureDisplayGroup groupB = _portalRig.B;
            foreach (var group in new[] { groupA, groupB })
            {
                // 清空引用后重新解析，验证自动查找只依赖夹具声明的组件契约。
                group.viewCenter = null;
                group.output = null;
                group.maskTop = null;
                group.maskBottom = null;
                group.ResolveViewRig();

                Assert.IsNotNull(group.viewCenter);
                Assert.AreEqual("Portal View Center", group.viewCenter.name);
                Assert.IsNotNull(group.output);
                Assert.AreEqual("Portal View", group.output.name);
                Assert.AreEqual(group.viewCenter, group.output.transform.parent);
                Assert.IsNotNull(group.maskTop);
                Assert.IsNotNull(group.maskBottom);

                int outputLayer = group.output.gameObject.layer;
                Assert.AreNotEqual(0, outputLayer);
                Assert.AreEqual(0, group.capture.captureCamera.cullingMask & (1 << outputLayer));

                float baseLocalZ = group.output.transform.localPosition.z;
                float baseWorldZ = group.output.transform.position.z;
                var texture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
                var sprite = Sprite.Create(texture, new Rect(0, 0, 64, 32),
                    new Vector2(0.5f, 0.5f), 40f);
                group.output.sprite = sprite;
                group.ApplyHalfCrop();

                Assert.AreEqual(baseLocalZ, group.output.transform.localPosition.z, 0.001f);
                Assert.AreEqual(baseWorldZ, group.output.transform.position.z, 0.001f);
                if (group.OutputHalf == PortalViewHalf.Right)
                    Assert.Greater(group.output.transform.localPosition.x, 0f);
                else
                    Assert.Less(group.output.transform.localPosition.x, 0f);

                group.output.sprite = null;
                Object.Destroy(sprite);
                Object.Destroy(texture);
            }

            groupA.OutputHalf = PortalViewHalf.Right;
            groupB.OutputHalf = PortalViewHalf.Left;
            var apiTexture = new Texture2D(64, 32, TextureFormat.RGBA32, false);
            var apiSprite = Sprite.Create(apiTexture, new Rect(0, 0, 64, 32),
                new Vector2(0.5f, 0.5f), 40f);
            groupB.output.sprite = apiSprite;
            groupB.ApplyHalfCrop();
            float xBeforeSelfSwitch = groupB.output.transform.localPosition.x;

            groupB.OutputHalf = PortalViewHalf.Right;
            float xAfterSelfSwitch = groupB.output.transform.localPosition.x;
            Assert.AreEqual(PortalViewHalf.Right, groupB.OutputHalf);
            Assert.Greater(xAfterSelfSwitch, xBeforeSelfSwitch);

            groupA.SetPartnerOutputHalf(PortalViewHalf.Left);
            Assert.AreEqual(PortalViewHalf.Left, groupB.OutputHalf);
            Assert.AreEqual(PortalViewHalf.Left, groupA.PartnerOutputHalf);
            Assert.AreEqual(PortalViewHalf.Right, groupA.OutputHalf);
            Assert.AreEqual(xAfterSelfSwitch, groupB.output.transform.localPosition.x, 0.001f);

            groupA.SetPartnerCameraHalf(PortalViewHalf.Right);
            Assert.AreEqual(PortalViewHalf.Right, groupB.OutputHalf);
            Assert.AreEqual(PortalViewHalf.Right, groupA.OutputHalf);
            Assert.AreEqual(xAfterSelfSwitch, groupB.output.transform.localPosition.x, 0.001f);

            groupB.output.sprite = null;
            Object.Destroy(apiSprite);
            Object.Destroy(apiTexture);
        }

        [UnityTest]
        public IEnumerator Player_Climbs_Generated_Tilemap_Ramp()
        {
            _terrainRig = TilemapTestRig.Create(null);
            Tile solid = _terrainRig.CreateSolidTile();
            Tile slope = _terrainRig.CreateSlopeTile();

            // 平地顶面 y=0；单格斜坡从 (1,0) 升至 (0,1)，左侧接 y=1 平台。
            _terrainRig.FillSolidRect(solid, -4, 5, -1, -1);
            _terrainRig.Tilemap.SetTile(new Vector3Int(0, 0, 0), slope);
            _terrainRig.Tilemap.SetTile(new Vector3Int(-2, 0, 0), solid);
            _terrainRig.Tilemap.SetTile(new Vector3Int(-1, 0, 0), solid);
            _terrainRig.TilemapCollider.ProcessTilemapChanges();
            _terrainRig.Composite.GenerateGeometry();

            _player = PlayTestFixtures.CreatePlayer(null, new Vector3(1.5f, 0.9f, 0f));
            for (int i = 0; i < 15; i++)
                yield return new WaitForFixedUpdate();

            _player.SetMoveInput(-1f);
            float peakY = _player.transform.position.y;
            for (int i = 0; i < 120; i++)
            {
                yield return new WaitForFixedUpdate();
                peakY = Mathf.Max(peakY, _player.transform.position.y);
            }
            _player.SetMoveInput(0f);

            Assert.Greater(peakY, 1.5f,
                $"玩家应爬上临时平台，峰值高度为 {peakY:F2}。");
        }

        [UnityTest]
        public IEnumerator Snapshot_Camera_Captures_Own_View()
        {
            _portalRig = PortalTestRig.Create();
            yield return null;

            CaptureDisplayGroup groupA = _portalRig.A;
            CaptureDisplayGroup groupB = _portalRig.B;
            PortalPair pair = _portalRig.Pair;
            Assert.AreEqual(groupB.output, groupA.capture.output);
            Assert.AreEqual(groupA.output, groupB.capture.output);

            var snapshot = groupA.capture;
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null ||
                snapshot.output.sprite == null)
                Assert.Ignore("当前运行环境禁用渲染，跳过快照图像检查。");

            // 该用例显式提高 PPU，覆盖纹理限额；其他用例仍使用夹具的低成本默认值。
            groupA.capture.pixelsPerUnit = 40f;
            groupB.capture.pixelsPerUnit = 40f;
            pair.useDynamicSize = false;
            pair.captureWidth = 50f;
            pair.captureHeight = 35f;
            pair.cameraSizeMultiplier = new Vector2(1.2f, 2f);
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            Assert.AreEqual(pair.captureHeight, snapshot.captureHeight, 0.001f);
            Assert.AreEqual(pair.captureWidth, snapshot.captureWidth, 0.001f);
            Texture texture = snapshot.output.sprite.texture;
            Assert.IsNotNull(texture);
            RenderTexture renderTexture = snapshot.captureCamera.targetTexture;
            Assert.IsNotNull(renderTexture);
            Sprite persistentSprite = snapshot.output.sprite;
            var outputProperties = new MaterialPropertyBlock();
            snapshot.output.GetPropertyBlock(outputProperties);
            Assert.AreSame(renderTexture, outputProperties.GetTexture("_MainTex"),
                "Portal View 应直接采样捕获相机的 RenderTexture。");

            Vector3 capturePosition = snapshot.captureCamera.transform.position;
            snapshot.sourceCamera.transform.position += new Vector3(4f, 0f, 0f);
            yield return null;
            yield return null;
            Assert.AreEqual(capturePosition, snapshot.captureCamera.transform.position,
                "捕获相机必须保持自身位置，不跟随主相机。");
            Assert.AreSame(persistentSprite, snapshot.output.sprite,
                "持续捕获不应重建几何 Sprite。");

            Assert.AreEqual(snapshot.captureHeight * 0.5f,
                snapshot.captureCamera.orthographicSize, 0.01f);
            Assert.AreEqual(snapshot.captureWidth,
                snapshot.captureCamera.aspect * snapshot.captureHeight, 0.01f);
            Assert.AreEqual(snapshot.captureHeight, snapshot.output.sprite.bounds.size.y, 0.001f);
            Assert.AreEqual(snapshot.captureWidth,
                snapshot.output.sprite.bounds.size.x, 0.2f,
                "持久 Sprite 保留完整网格，半屏由 UV 裁剪。");

            PortalViewHalf originalHalf = groupB.OutputHalf;
            groupB.OutputHalf = originalHalf == PortalViewHalf.Right
                ? PortalViewHalf.Left
                : PortalViewHalf.Right;
            Assert.AreSame(persistentSprite, snapshot.output.sprite,
                "切换半屏不应重新创建 Sprite。");
            groupB.OutputHalf = originalHalf;
            Assert.AreSame(persistentSprite, snapshot.output.sprite);

            if (groupA.OutputHalf == PortalViewHalf.Right)
                Assert.Greater(groupA.output.transform.localPosition.x, 0f);
            else
                Assert.Less(groupA.output.transform.localPosition.x, 0f);
            if (groupB.OutputHalf == PortalViewHalf.Right)
                Assert.Greater(groupB.output.transform.localPosition.x, 0f);
            else
                Assert.Less(groupB.output.transform.localPosition.x, 0f);

            Vector3 outputScale = snapshot.output.transform.lossyScale;
            Assert.AreEqual(1f, Mathf.Abs(outputScale.x), 0.001f);
            Assert.AreEqual(1f, Mathf.Abs(outputScale.y), 0.001f);
            Assert.AreEqual(1f, Mathf.Abs(outputScale.z), 0.001f);
            Assert.AreEqual(0f, groupA.output.transform.position.z, 0.01f);
            Assert.AreEqual(0f, groupB.output.transform.position.z, 0.01f);
            Assert.AreEqual(snapshot.pixelsPerUnit,
                texture.height / snapshot.output.sprite.bounds.size.y, 0.5f);

            float originalOrtho = snapshot.sourceCamera.orthographicSize;
            int textureHeightBefore = texture.height;
            float captureSizeBefore = snapshot.captureCamera.orthographicSize;
            snapshot.sourceCamera.orthographicSize = originalOrtho * 2f;
            yield return null;
            yield return null;
            Assert.AreEqual(captureSizeBefore, snapshot.captureCamera.orthographicSize, 0.01f);
            Assert.AreEqual(textureHeightBefore, snapshot.output.sprite.texture.height);
            snapshot.sourceCamera.orthographicSize = originalOrtho;
            yield return null;
            yield return null;

            // 单轴超限时，两轴按同一比例降低 PPU，世界捕获尺寸保持不变。
            int originalMaxTextureSize = snapshot.maxTextureSize;
            snapshot.maxTextureSize = 512;
            pair.captureWidth = 128f;
            pair.captureHeight = 8f;
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            Texture clampedTexture = snapshot.output.sprite.texture;
            Assert.LessOrEqual(clampedTexture.width, snapshot.maxTextureSize);
            Assert.LessOrEqual(clampedTexture.height, snapshot.maxTextureSize);
            Assert.AreEqual(4f, clampedTexture.width / pair.captureWidth, 0.001f);
            Assert.AreEqual(clampedTexture.width / pair.captureWidth,
                clampedTexture.height / pair.captureHeight, 0.001f);
            Assert.AreEqual(8f, snapshot.captureCamera.orthographicSize * 2f, 0.001f);
            Assert.AreEqual(128f,
                snapshot.captureCamera.aspect * snapshot.captureCamera.orthographicSize * 2f, 0.001f);
            Assert.AreEqual(8f, snapshot.output.sprite.bounds.size.y, 0.001f);
            Assert.AreEqual(128f, snapshot.output.sprite.bounds.size.x, 0.001f);

            snapshot.output.GetPropertyBlock(outputProperties);
            float gapUv = groupB.centerGapPixels / clampedTexture.width;
            float edgeUv = 0.25f + gapUv * 0.5f;
            Vector4 expectedTransform = new Vector4(1f, 1f,
                groupB.OutputHalf == PortalViewHalf.Right ? edgeUv : -edgeUv, 0f);
            Vector4 expectedClip = new Vector4(edgeUv, 0f, 0.75f - gapUv * 0.5f, 1f);
            Assert.Less(Vector4.Distance(expectedTransform,
                outputProperties.GetVector("_CaptureUvTransform")), 0.0001f);
            Assert.Less(Vector4.Distance(expectedClip,
                outputProperties.GetVector("_CaptureUvClip")), 0.0001f);

            snapshot.maxTextureSize = originalMaxTextureSize;
            pair.captureWidth = 50f;
            pair.captureHeight = 35f;
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            pair.useDynamicSize = true;
            pair.cameraSizeMultiplier = new Vector2(1.2f, 2f);
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            Assert.IsTrue(snapshot.useDynamicSize);
            Assert.AreEqual(1.2f, snapshot.cameraSizeMultiplier.x, 0.001f);
            Assert.AreEqual(2f, snapshot.cameraSizeMultiplier.y, 0.001f);
            float sourceHeight = snapshot.sourceCamera.orthographicSize * 2f;
            float sourceWidth = sourceHeight * snapshot.sourceCamera.aspect;
            float dynamicHeight = sourceHeight * 2f;
            float dynamicWidth = sourceWidth * 1.2f;
            Assert.AreEqual(dynamicHeight, snapshot.captureCamera.orthographicSize * 2f, 0.02f);
            Assert.AreEqual(dynamicWidth, snapshot.captureCamera.aspect * dynamicHeight, 0.02f);
            Assert.AreEqual(dynamicHeight, snapshot.output.sprite.bounds.size.y, 0.01f);
            Assert.AreEqual(Mathf.RoundToInt(dynamicHeight * snapshot.pixelsPerUnit),
                snapshot.output.sprite.texture.height);
            Assert.AreEqual(Mathf.RoundToInt(dynamicWidth * snapshot.pixelsPerUnit),
                snapshot.output.sprite.texture.width);

            pair.useDynamicSize = false;
            pair.PushViewSettingsToGroups();
            yield return null;
            yield return null;

            var playerRenderer = _portalRig.Player.VisualRoot.GetComponent<SpriteRenderer>();
            Assert.Greater(playerRenderer.sortingOrder, snapshot.output.sortingOrder);
            int outputLayerBit = 1 << snapshot.output.gameObject.layer;
            Assert.AreEqual(0, snapshot.captureCamera.cullingMask & outputLayerBit);
        }
    }
}
