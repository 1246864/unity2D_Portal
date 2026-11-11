Shader "Hidden/Platformer/ScreenOutline"
{
    Properties
    {
        _OutlineColor ("Outline Color", Color) = (0.70, 0.79, 0.92, 1)
        _OutlineWidth ("Width (pixels)", Range(0.25, 8)) = 1
        _Threshold ("Edge Threshold", Range(0.001, 0.5)) = 0.06
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        ZTest Always
        ZWrite Off
        Cull Off

        Pass
        {
            Name "ScreenOutline"

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            float4 _OutlineColor;
            float _OutlineWidth;
            float _Threshold;
            float4 _SourceTexelSize;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texel = _SourceTexelSize.xy * _OutlineWidth;

                half3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 left   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x, 0.0)).rgb;
                half3 right  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0.0)).rgb;
                half3 down   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y)).rgb;
                half3 up     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y)).rgb;

                // Any colour discontinuity in the neighbourhood becomes an outline.
                // Flat, uniformly coloured terrain therefore gets one merged silhouette.
                half edge = max(max(distance(center, left), distance(center, right)),
                                max(distance(center, down), distance(center, up)));

                half mask = smoothstep(_Threshold, _Threshold + 0.03, edge);
                half3 result = lerp(center, _OutlineColor.rgb, mask * _OutlineColor.a);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
