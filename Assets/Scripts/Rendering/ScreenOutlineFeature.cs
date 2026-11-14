using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Platformer.Rendering
{
    /// <summary>
    /// 2D 渲染器的全屏描边。
    ///
    /// 邻域存在颜色突变的像素会被涂成描边色。因为纯色平地内部没有颜色突变，
    /// 任意数量、任意尺寸的相邻瓦片都会融合成一条整体外轮廓。
    /// </summary>
    public class ScreenOutlineFeature : ScriptableRendererFeature
    {
        [Tooltip("描边使用的材质")]
        public Material outlineMaterial;
        [Tooltip("该效果插入到渲染流程的哪个阶段")]
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        [Tooltip("描边宽度（像素）")]
        [Range(0.25f, 8f)] public float widthPixels = 1f;
        [Tooltip("颜色边界的灵敏度，越大越不敏感（需高于瓦片内描边的色差）")]
        [Range(0.001f, 0.5f)] public float edgeThreshold = 0.28f;

        OutlinePass _pass;

        public override void Create()
        {
            _pass = new OutlinePass { renderPassEvent = injectionPoint };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (outlineMaterial == null) return;

            CameraType type = renderingData.cameraData.cameraType;
            if (type != CameraType.Game && type != CameraType.SceneView) return;

            _pass.Setup(outlineMaterial, widthPixels, edgeThreshold);
            renderer.EnqueuePass(_pass);
        }

        protected override void Dispose(bool disposing)
        {
            _pass?.Dispose();
            _pass = null;
        }

        sealed class OutlinePass : ScriptableRenderPass
        {
            static readonly int WidthId = Shader.PropertyToID("_OutlineWidth");
            static readonly int ThresholdId = Shader.PropertyToID("_Threshold");
            static readonly int TexelSizeId = Shader.PropertyToID("_SourceTexelSize");
            static readonly int BackgroundColorId = Shader.PropertyToID("_BackgroundColor");
            static readonly int BackgroundToleranceId = Shader.PropertyToID("_BackgroundTolerance");

            Material _material;
            float _width;
            float _threshold;
            RTHandle _temp;

            public void Setup(Material material, float width, float threshold)
            {
                _material = material;
                _width = width;
                _threshold = threshold;
            }

            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                descriptor.depthBufferBits = 0;
                descriptor.msaaSamples = 1;
                descriptor.useMipMap = false;

                RenderingUtils.ReAllocateIfNeeded(
                    ref _temp, descriptor, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_ScreenOutlineTemp");
            }

            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                if (_material == null || _temp == null) return;

                RenderTextureDescriptor descriptor = renderingData.cameraData.cameraTargetDescriptor;
                _material.SetVector(TexelSizeId, new Vector4(
                    1f / Mathf.Max(1, descriptor.width),
                    1f / Mathf.Max(1, descriptor.height),
                    descriptor.width,
                    descriptor.height));
                _material.SetFloat(WidthId, _width);
                _material.SetFloat(ThresholdId, _threshold);

                // 轮廓项通过与相机背景色比较来判断像素是否"在形状内部"，
                // 这样在抗锯齿的斜边上描边仍是干净的单像素，
                // 而不会变成虚线、棋盘格。
                Color background = renderingData.cameraData.camera.backgroundColor;
                if (QualitySettings.activeColorSpace == ColorSpace.Linear)
                    background = background.linear;
                _material.SetColor(BackgroundColorId, background);
                _material.SetFloat(BackgroundToleranceId, 0.05f);

                CommandBuffer cmd = CommandBufferPool.Get("Platformer Screen Outline");
                RTHandle source = renderingData.cameraData.renderer.cameraColorTargetHandle;

                Blitter.BlitCameraTexture(cmd, source, _temp, _material, 0);
                Blitter.BlitCameraTexture(cmd, _temp, source);

                context.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }

            public void Dispose()
            {
                _temp?.Release();
                _temp = null;
            }
        }
    }
}
