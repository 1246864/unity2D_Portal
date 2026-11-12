using UnityEngine;
using UnityEngine.Rendering;

namespace Platformer
{
    /// <summary>
    /// Captures the region the main camera sees (enlarged by <see cref="viewScale"/>,
    /// default 1.2) with a dedicated orthographic camera and shows the result as an
    /// image sprite.
    ///
    /// The capture camera renders into a RenderTexture on demand; the pixels are read
    /// into a Texture2D that backs the output sprite, so the snapshot is a real image
    /// and not a copy of the scene objects.
    /// </summary>
    public class RegionSnapshotCamera : MonoBehaviour
    {
        [Header("Source")]
        [Tooltip("Main camera whose view defines the captured region.")]
        public Camera sourceCamera;

        [Tooltip("Captured region size relative to the main camera view.")]
        public float viewScale = 1.2f;

        [Header("Capture Camera")]
        public Camera captureCamera;
        public int textureWidth = 960;
        public int textureHeight = 540;

        [Tooltip("Seconds between captures. 0 = every frame.")]
        public float captureInterval = 0.05f;

        [Header("Output")]
        public SpriteRenderer output;

        RenderTexture _renderTexture;
        Texture2D _texture;
        Sprite _sprite;
        float _spriteWorldHeight;
        float _timer;

        bool _renderingAvailable;

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
            Capture();
        }

        void LateUpdate()
        {
            if (sourceCamera == null || captureCamera == null || output == null) return;

            SyncCaptureSize();

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
            SyncCaptureSize();
            Capture();
        }

        bool EnsureResources()
        {
            if (sourceCamera == null || captureCamera == null || output == null) return false;

            if (_renderTexture == null)
            {
                _renderTexture = new RenderTexture(textureWidth, textureHeight, 24, RenderTextureFormat.ARGB32);
                _renderTexture.Create();
            }

            if (_texture == null)
            {
                _texture = new Texture2D(textureWidth, textureHeight, TextureFormat.RGB24, false);
                _texture.name = "Region Snapshot";
            }

            // 1:1 guarantee: one texture pixel maps to the same world distance as in the
            // captured region, so at scale 1 the snapshot is exactly as big as the
            // objects that were photographed.
            float capturedWorldHeight = sourceCamera.orthographicSize * 2f * viewScale;
            if (_sprite == null || !Mathf.Approximately(_spriteWorldHeight, capturedWorldHeight))
            {
                if (_sprite != null) Destroy(_sprite);

                float pixelsPerUnit = textureHeight / Mathf.Max(0.001f, capturedWorldHeight);
                _sprite = Sprite.Create(_texture, new Rect(0, 0, textureWidth, textureHeight),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit);
                _sprite.name = "Region Snapshot Sprite";
                output.sprite = _sprite;
                _spriteWorldHeight = capturedWorldHeight;
            }

            return true;
        }

        void SyncCaptureSize()
        {
            // The snapshot comes from its own camera with its own view: only the size
            // follows the main camera (1.2x), the capture camera never moves with it.
            captureCamera.orthographicSize = sourceCamera.orthographicSize * viewScale;
        }

        void Capture()
        {
            captureCamera.targetTexture = _renderTexture;
            captureCamera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            _texture.ReadPixels(new Rect(0, 0, textureWidth, textureHeight), 0, 0);
            ClearBorder();
            _texture.Apply();
            RenderTexture.active = previous;

            captureCamera.targetTexture = null;
        }

        void ClearBorder()
        {
            // A thin background-coloured border keeps the screen-space outline from
            // tracing the snapshot sprite as if it were terrain.
            Color background = sourceCamera.backgroundColor;
            const int border = 2;

            for (int x = 0; x < textureWidth; x++)
            {
                for (int y = 0; y < border; y++)
                {
                    _texture.SetPixel(x, y, background);
                    _texture.SetPixel(x, textureHeight - 1 - y, background);
                }
            }

            for (int y = 0; y < textureHeight; y++)
            {
                for (int x = 0; x < border; x++)
                {
                    _texture.SetPixel(x, y, background);
                    _texture.SetPixel(textureWidth - 1 - x, y, background);
                }
            }
        }

        void OnDestroy()
        {
            if (_renderTexture != null)
            {
                _renderTexture.Release();
                Destroy(_renderTexture);
            }

            if (_sprite != null) Destroy(_sprite);
            if (_texture != null) Destroy(_texture);
        }
    }
}
