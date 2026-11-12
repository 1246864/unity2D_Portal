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
        public Transform outputTarget;
        [Tooltip("Display scale of the snapshot sprite.")]
        public float outputScale = 0.5f;

        RenderTexture _renderTexture;
        Texture2D _texture;
        Sprite _sprite;
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

            FollowSource();

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
            FollowSource();
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

            if (_sprite == null)
            {
                // One pixel per world unit = textureHeight / captured world height,
                // so the snapshot is shown at the region's true size.
                float capturedWorldHeight = sourceCamera.orthographicSize * 2f * viewScale;
                float pixelsPerUnit = textureHeight / Mathf.Max(0.001f, capturedWorldHeight);
                _sprite = Sprite.Create(_texture, new Rect(0, 0, textureWidth, textureHeight),
                    new Vector2(0.5f, 0.5f), pixelsPerUnit);
                _sprite.name = "Region Snapshot Sprite";
                output.sprite = _sprite;
            }

            if (outputTarget != null && !Mathf.Approximately(outputTarget.localScale.x, outputScale))
            {
                outputTarget.localScale = Vector3.one * outputScale;
            }

            return true;
        }

        void FollowSource()
        {
            Vector3 position = sourceCamera.transform.position;
            captureCamera.transform.position = new Vector3(position.x, position.y, position.z);
            captureCamera.orthographicSize = sourceCamera.orthographicSize * viewScale;
        }

        void Capture()
        {
            captureCamera.targetTexture = _renderTexture;
            captureCamera.Render();

            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = _renderTexture;
            _texture.ReadPixels(new Rect(0, 0, textureWidth, textureHeight), 0, 0);
            _texture.Apply();
            RenderTexture.active = previous;

            captureCamera.targetTexture = null;
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
