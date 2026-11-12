using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Platformer
{
    /// <summary>
    /// Shows the image of a dedicated camera as a sprite.
    ///
    /// The captured area is <see cref="viewScale"/> times the main camera view. When the
    /// main view grows, the captured area grows with it and the render texture (the
    /// output canvas) grows proportionally, so the sprite always stays 1:1 with the
    /// captured world: at scale 1 it is exactly as big as the area it shows.
    ///
    /// The capture camera is not restricted to other layers, so if its output sprite is
    /// inside its view the snapshot contains itself, producing a mirror-in-mirror effect.
    /// </summary>
    public class RegionSnapshotCamera : MonoBehaviour
    {
        [Header("来源")]
        [Tooltip("主相机：只参考它的视野大小，不参考位置")]
        public Camera sourceCamera;

        [Tooltip("捕获范围相对主相机视野的倍数")]
        public float viewScale = 1.2f;

        [Header("捕获相机")]
        [Tooltip("真正执行渲染的相机（正交）")]
        public Camera captureCamera;

        [Tooltip("生成图像的像素密度（每世界单位的像素数）")]
        public float pixelsPerUnit = 40f;

        [Tooltip("生成贴图的尺寸上限")]
        public int maxTextureSize = 2048;

        [Tooltip("两次捕获之间的间隔（秒），0 表示每帧")]
        public float captureInterval = 0.05f;

        [Header("输出")]
        [Tooltip("显示捕获画面的精灵渲染器")]
        public SpriteRenderer output;

        RenderTexture _renderTexture;
        Texture2D _texture;
        Sprite _sprite;
        float _spriteWorldHeight;
        float _timer;
        bool _renderingAvailable;

        /// <summary>World height the snapshot covers.</summary>
        float CapturedWorldHeight => sourceCamera.orthographicSize * 2f * viewScale;

        void Start()
        {
            // Headless runs (batch tests with -nographics) cannot render to a texture;
            // calling Camera.Render there blocks forever, so the snapshot disables itself.
            _renderingAvailable = SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null;
            if (!_renderingAvailable)
            {
                enabled = false;
                return;
            }

            if (!EnsureResources()) return;
            captureCamera.targetTexture = _renderTexture;
            // URP renders enabled cameras automatically; a manual Camera.Render() on a
            // disabled camera is ignored, which is why this one stays enabled.
            captureCamera.enabled = true;
            Capture();
        }

        void LateUpdate()
        {
            if (!EnsureResources()) return;

            _timer -= Time.deltaTime;
            if (_timer <= 0f)
            {
                _timer = Mathf.Max(0.0001f, captureInterval);
                Capture();
            }
        }

        /// <summary>Removes the preview sprite from the output renderer.</summary>
        public void ClearOutput()
        {
            if (output != null) output.sprite = null;
        }

        /// <summary>Captures immediately (also used by preview rendering).</summary>
        public void CaptureNow()
        {
            if (!_renderingAvailable && SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;
            if (!EnsureResources()) return;
            Capture();
        }

        bool EnsureResources()
        {
            if (sourceCamera == null || captureCamera == null || output == null) return false;

            // The capture camera keeps its own view; only the size follows the main one.
            captureCamera.orthographicSize = sourceCamera.orthographicSize * viewScale;

            float worldHeight = CapturedWorldHeight;
            float aspect = sourceCamera.aspect > 0f ? sourceCamera.aspect : 16f / 9f;
            int height = Mathf.Clamp(Mathf.RoundToInt(worldHeight * pixelsPerUnit), 16, maxTextureSize);
            int width = Mathf.Clamp(Mathf.RoundToInt(worldHeight * aspect * pixelsPerUnit), 16, maxTextureSize);

            // Rebuild the canvas when the view changes so the texel density and the
            // 1:1 world size are preserved.
            if (_texture == null || _texture.width != width || _texture.height != height ||
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
                _spriteWorldHeight = worldHeight;
            }

            return true;
        }

        void Capture()
        {
            // Render on demand: this works in edit mode and in batch play-mode tests,
            // where the automatic camera loop does not present frames.
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
