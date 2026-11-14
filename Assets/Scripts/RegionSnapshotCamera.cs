using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Platformer
{
    /// <summary>
    /// 把一台专用相机的画面显示成精灵。
    ///
    /// 捕获范围是主相机视野的 <see cref="viewScale"/> 倍。主视野变大时，捕获范围
    /// 随之变大，渲染纹理（输出画布）也等比放大，因此输出精灵始终与拍摄到的世界
    /// 保持 1:1：缩放为 1 时，画面覆盖多大，精灵就有多大。
    ///
    /// 传送门端会由 <see cref="CaptureDisplayGroup"/> 把输出精灵所在的 Snapshot 层
    /// 从捕获相机中排除，因此快照不会拍到自己的输出、不会递归；普通快照相机如果
    /// 不排除任何层，仍可能拍到自己。
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

        [Tooltip("两次捕获之间的间隔（秒），0 表示每帧渲染")]
        public float captureInterval = 0f;

        [Header("输出")]
        [Tooltip("显示捕获画面的精灵渲染器")]
        public SpriteRenderer output;

        RenderTexture _renderTexture;
        Texture2D _texture;
        Sprite _sprite;
        float _spriteWorldHeight;
        float _timer;
        bool _renderingAvailable;

        /// <summary>快照覆盖的世界高度。</summary>
        float CapturedWorldHeight => sourceCamera.orthographicSize * 2f * viewScale;

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
            if (sourceCamera == null || captureCamera == null || output == null) return false;

            // 捕获相机保持自己的视角；只有尺寸跟随主相机。
            captureCamera.orthographicSize = sourceCamera.orthographicSize * viewScale;

            float worldHeight = CapturedWorldHeight;
            float aspect = sourceCamera.aspect > 0f ? sourceCamera.aspect : 16f / 9f;
            int height = Mathf.Clamp(Mathf.RoundToInt(worldHeight * pixelsPerUnit), 16, maxTextureSize);
            int width = Mathf.Clamp(Mathf.RoundToInt(worldHeight * aspect * pixelsPerUnit), 16, maxTextureSize);

            // 视野变化时重建画布，保持像素密度和 1:1 的世界尺寸不变。
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
