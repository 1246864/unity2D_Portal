using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Platformer.Rendering
{
    /// <summary>
    /// Full-screen outline for the 2D renderer.
    ///
    /// Pixels whose neighbourhood has a colour discontinuity get the outline colour.
    /// Because flat, uniformly coloured terrain has no internal discontinuities, any
    /// number of adjacent blocks (of any size) melts into a single silhouette outline.
    /// </summary>
    public class ScreenOutlineFeature : ScriptableRendererFeature
    {
        public Material outlineMaterial;
        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingTransparents;
        [Range(0.25f, 8f)] public float widthPixels = 1f;
        [Range(0.001f, 0.5f)] public float edgeThreshold = 0.06f;

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
