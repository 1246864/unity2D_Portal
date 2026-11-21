Shader "Hidden/Platformer/PortalView"
{
    Properties
    {
        [PerRendererData] _MainTex ("捕获画面", 2D) = "white" {}
        [PerRendererData] _CaptureUvTransform ("捕获 UV 变换", Vector) = (1,1,0,0)
        [PerRendererData] _CaptureUvClip ("捕获 UV 裁剪", Vector) = (0,0,1,1)
        _Color ("Tint", Color) = (1,1,1,1)
        _PortalColor ("单门纹理颜色", Color) = (0.35,0.65,1,1)
        _ConnectionReveal ("连接画面显现进度", Range(0,1)) = 0
        _OpenBrightness ("开门明暗", Range(0,1)) = 1
        _ClosedNoiseScale ("关闭纹理噪音缩放", Float) = 7
        _ClosedNoiseSpeed ("关闭纹理噪音速度", Float) = 0.9
        _ClosedFlowSpeed ("关闭纹理流光速度", Float) = 2.5
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend One OneMinusSrcAlpha

        Pass
        {
            CGPROGRAM
            #pragma vertex SpriteVert
            #pragma fragment frag
            #pragma target 2.0
            #pragma multi_compile_instancing
            #include "UnitySprites.cginc"

            float4 _CaptureUvTransform;
            float4 _CaptureUvClip;
            fixed4 _PortalColor;
            float _ConnectionReveal;
            float _OpenBrightness;
            float _ClosedNoiseScale;
            float _ClosedNoiseSpeed;
            float _ClosedFlowSpeed;

            float hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            float noise21(float2 p)
            {
                float2 i = floor(p);
                float2 f = frac(p);
                f = f * f * (3.0 - 2.0 * f);
                float a = hash21(i);
                float b = hash21(i + float2(1.0, 0.0));
                float c = hash21(i + float2(0.0, 1.0));
                float d = hash21(i + float2(1.0, 1.0));
                return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
            }

            fixed4 ClosedPattern(float2 uv, float viewAlpha)
            {
                float2 noiseUV = uv * max(0.01, _ClosedNoiseScale) + float2(
                    _Time.y * _ClosedNoiseSpeed,
                    _Time.y * _ClosedNoiseSpeed * 1.31);
                float noise = noise21(noiseUV) * 0.65 +
                              noise21(noiseUV * 2.17 + 13.1) * 0.35;
                float flow = 0.5 + 0.5 * sin(
                    (uv.y * 8.0 - _Time.y * _ClosedFlowSpeed + noise * 1.5) * 6.2831853);
                float scan = 0.85 + 0.15 * sin(
                    (uv.y * 32.0 + _Time.y * _ClosedFlowSpeed * 0.5) * 6.2831853);
                float energy = saturate(0.2 + noise * 0.55 + flow * 0.45);

                float3 rgb = _PortalColor.rgb * (0.3 + energy * 0.9) * scan;
                rgb *= saturate(_OpenBrightness);
                return fixed4(rgb, _PortalColor.a * viewAlpha);
            }

            fixed4 frag(v2f input) : SV_Target
            {
                float2 captureUv = input.texcoord * _CaptureUvTransform.xy + _CaptureUvTransform.zw;
                fixed4 captured = SampleSpriteTexture(captureUv) * input.color;
                fixed4 closed = ClosedPattern(captureUv, input.color.a);
                fixed4 result = lerp(closed, captured, saturate(_ConnectionReveal));

                // 未打通噪音、已连接画面及两者的渐变都只在同一个半幅区域内显示。
                float2 inside = step(_CaptureUvClip.xy, input.texcoord) *
                                step(input.texcoord, _CaptureUvClip.zw);
                result *= inside.x * inside.y;
                result.rgb *= result.a;
                return result;
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
