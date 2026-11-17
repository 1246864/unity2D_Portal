using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Platformer
{
    /// <summary>
    /// 把一台专用相机的画面显示成精灵。
    ///
    /// 捕获范围支持两种模式：
    /// - 静态：使用 <see cref="captureWidth"/> × <see cref="captureHeight"/>，不随窗口变化；
    /// - 动态：使用主相机视野宽/高分别乘 <see cref="cameraSizeMultiplier"/> 的 X/Y，随窗口/主相机变化。
    /// 输出精灵始终与拍摄区域保持 1:1：缩放为 1 时，画面覆盖多大，精灵就有多大。
    ///
    /// 传送门端会由 <see cref="CaptureDisplayGroup"/> 把输出精灵所在的 Snapshot 层
    /// 从捕获相机中排除，因此快照不会拍到自己的输出、不会递归；普通快照相机如果
    /// 不排除任何层，仍可能拍到自己。
    /// </summary>
    public class RegionSnapshotCamera : MonoBehaviour
    {
        [Header("来源")]
        [Tooltip("主相机引用：动态模式读取它的视野；静态模式仍由传送门控制器用作跟随相机")]
        public Camera sourceCamera;

        // 尺寸参数由 PortalPair 统一配置并下发；这里保留 public 供运行时使用，
        // 不在 RegionSnapshotCamera 的 Inspector 中重复显示。
        [HideInInspector] public bool useDynamicSize = false;
        [HideInInspector] public Vector2 cameraSizeMultiplier = new Vector2(1.2f, 2f);
        [HideInInspector] public float captureWidth = 50f;
        [HideInInspector] public float captureHeight = 50f;

        [Header("捕获相机")]
        [Tooltip("真正执行渲染的相机（正交）")]
        public Camera captureCamera;

        [Tooltip("生成图像的像素密度（每世界单位的像素数）")]
        public float pixelsPerUnit = 40f;

        [Tooltip("生成贴图的尺寸上限")]
        public int maxTextureSize = 2048;

        [Tooltip("两次捕获之间的间隔（秒），0 表示每帧渲染")]
        public float captureInterval = 0f;

        [Header("输出")]
        [Tooltip("显示捕获画面的精灵渲染器")]
        public SpriteRenderer output;

        RenderTexture _renderTexture;
        Texture2D _texture;
        Sprite _sprite;
        float _spriteWorldWidth;
        float _spriteWorldHeight;
        float _timer;
        bool _renderingAvailable;
        bool _warnedTextureClamp;
        bool _warnedMissingSource;

        void Start()
        {
            // 无头运行（-nographics 的批处理测试）无法渲染到纹理，
            // 此时调用 Camera.Render 会永久阻塞，因此快照自动禁用自己。
            _renderingAvailable = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!_renderingAvailable)
            {
                enabled = false;
                return;
            }

            if (!EnsureResources()) return;
            captureCamera.targetTexture = _renderTexture;
            // URP 会自动渲染已启用的相机；对已禁用的相机手动调用 Camera.Render()
            // 会被忽略，所以这台相机保持启用状态。
            captureCamera.enabled = true;
            Capture();
        }

        void LateUpdate()
        {
            if (!EnsureResources()) return;

            // captureInterval 为 0 时每帧都重新渲染/读取一次，分辨率不变。
            if (captureInterval <= 0f)
            {
                Capture();
                return;
            }

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = captureInterval;
                Capture();
            }
        }

        /// <summary>清空输出渲染器上的预览精灵。</summary>
        public void ClearOutput()
        {
            if (output != null) output.sprite = null;
        }

        /// <summary>立即捕获一次（预览渲染也会用到）。</summary>
        public void CaptureNow()
        {
            if (!_renderingAvailable && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (!EnsureResources()) return;
            Capture();
        }

        bool EnsureResources()
        {
            if (captureCamera == null || output == null) return false;

            float worldWidth;
            float worldHeight;

            if (useDynamicSize && sourceCamera != null)
            {
                // 动态模式：宽、高分别跟随主相机视野的宽/高，并分别乘 X/Y 倍率。
                float cameraHeight = sourceCamera.orthographicSize * 2f;
                float aspect = sourceCamera.aspect > 0f ? sourceCamera.aspect : 16f / 9f;
                float cameraWidth = cameraHeight * aspect;

                worldWidth = Mathf.Max(0.01f,
                    cameraWidth * Mathf.Max(0.01f, cameraSizeMultiplier.x));
                worldHeight = Mathf.Max(0.01f,
                    cameraHeight * Mathf.Max(0.01f, cameraSizeMultiplier.y));
            }
            else
            {
                if (useDynamicSize && sourceCamera == null && !_warnedMissingSource)
                {
                    _warnedMissingSource = true;
                    Debug.LogWarning(
                        "[RegionSnapshotCamera] useDynamicSize 已开启，但 sourceCamera 为空，已回退到静态尺寸。",
                        this);
                }

                // 静态模式：使用 PortalPair 下发的 captureWidth/captureHeight。
                worldWidth = Mathf.Max(0.01f, captureWidth);
                worldHeight = Mathf.Max(0.01f, captureHeight);
            }

            captureCamera.orthographicSize = worldHeight * 0.5f;
            captureCamera.aspect = worldWidth / worldHeight;

            int desiredWidth = Mathf.RoundToInt(worldWidth * pixelsPerUnit);
            int desiredHeight = Mathf.RoundToInt(worldHeight * pixelsPerUnit);
            int width = Mathf.Clamp(desiredWidth, 16, maxTextureSize);
            int height = Mathf.Clamp(desiredHeight, 16, maxTextureSize);

            if (width != desiredWidth || height != desiredHeight)
            {
                if (!_warnedTextureClamp)
                {
                    _warnedTextureClamp = true;
                    Debug.LogWarning(
                        $"[RegionSnapshotCamera] 捕获尺寸 {worldWidth}×{worldHeight} 在 {pixelsPerUnit} PPU 下" +
                        $"需要 {desiredWidth}×{desiredHeight} 纹理，超过 maxTextureSize={maxTextureSize}，" +
                        "纹理分辨率被限制，像素密度会降低。",
                        this);
                }
            }
            else
            {
                _warnedTextureClamp = false;
            }

            // 尺寸/像素密度变化时重建画布；固定尺寸下只会创建一次。
            if (_texture == null || _texture.width != width || _texture.height != height ||
                !Mathf.Approximately(_spriteWorldWidth, worldWidth) ||
                !Mathf.Approximately(_spriteWorldHeight, worldHeight))
            {
                ReleaseTargets();

                _renderTexture = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
                _renderTexture.Create();
                if (captureCamera != null) captureCamera.targetTexture = _renderTexture;

                _texture = new Texture2D(width, height, TextureFormat.RGB24, false);
                _texture.name = "Region Snapshot";

                float pixelsPerWorldUnit = height / Mathf.Max(0.001f, worldHeight);
                _sprite = Sprite.Create(_texture, new Rect(0, 0, width, height),
                    new Vector2(0.5f, 0.5f), pixelsPerWorldUnit);
                _sprite.name = "Region Snapshot Sprite";
                output.sprite = _sprite;
                _spriteWorldWidth = worldWidth;
                _spriteWorldHeight = worldHeight;
            }

            return true;
        }

        void Capture()
        {
            // 按需渲染：在编辑模式和批处理 PlayMode 测试中都有效，
            // 因为这些场景里相机的自动渲染循环不会出帧。
            captureCamera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            _texture.ReadPixels(new Rect(0, 0, _texture.width, _texture.height), 0, 0);
            _texture.Apply();
            RenderTexture.active = previous;
        }

        void ReleaseTargets()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
                _renderTexture = null;
            }

            if (_sprite != null)
            {
                Destroy(_sprite);
                _sprite = null;
            }

            if (_texture != null)
            {
                Destroy(_texture);
                _texture = null;
            }
        }

        void OnDestroy()
        {
            if (captureCamera != null)
            {
                captureCamera.targetTexture = null;
                captureCamera.enabled = false;
            }

            ReleaseTargets();
        }
    }
}
