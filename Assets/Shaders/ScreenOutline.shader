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
            float4 _BackgroundColor;
            float _BackgroundTolerance;

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.texcoord;
                float2 texel = _SourceTexelSize.xy * _OutlineWidth;

                half3 center = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
                half3 left   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x, 0.0)).rgb;
                half3 right  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0.0)).rgb;
                half3 down   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y)).rgb;
                half3 up     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y)).rgb;
                half3 downLeft  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel).rgb;
                half3 downRight = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, -texel.y)).rgb;
                half3 upLeft    = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, texel.y)).rgb;
                half3 upRight   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel).rgb;

                half3 background = _BackgroundColor.rgb;

                // Colour discontinuity (used for different tile colours: normal vs dark).
                half edge = max(
                    max(max(distance(center, left), distance(center, right)),
                        max(distance(center, down), distance(center, up))),
                    max(max(distance(center, downLeft), distance(center, downRight)),
                        max(distance(center, upLeft), distance(center, upRight))));

                // Silhouette test against the camera background. Sampling a raw colour
                // difference on an anti-aliased 45-degree edge produced a checkered line,
                // so the silhouette is detected as "inside pixel touching a background
                // pixel" and drawn on the inside only: one clean pixel, no halo.
                half inCenter = step(_BackgroundTolerance, distance(center, background));
                half inLeft   = step(_BackgroundTolerance, distance(left, background));
                half inRight  = step(_BackgroundTolerance, distance(right, background));
                half inDown   = step(_BackgroundTolerance, distance(down, background));
                half inUp     = step(_BackgroundTolerance, distance(up, background));
                half inDownLeft  = step(_BackgroundTolerance, distance(downLeft, background));
                half inDownRight = step(_BackgroundTolerance, distance(downRight, background));
                half inUpLeft    = step(_BackgroundTolerance, distance(upLeft, background));
                half inUpRight   = step(_BackgroundTolerance, distance(upRight, background));

                half allNeighboursInside = min(
                    min(min(inLeft, inRight), min(inDown, inUp)),
                    min(min(inDownLeft, inDownRight), min(inUpLeft, inUpRight)));
                half anyNeighbourInside = max(
                    max(max(inLeft, inRight), max(inDown, inUp)),
                    max(max(inDownLeft, inDownRight), max(inUpLeft, inUpRight)));

                // 1 px inside + 1 px outside the silhouette. Because the inside/outside
                // test is a binary comparison against the clear colour, the band stays
                // continuous on anti-aliased diagonals instead of turning into a
                // checkered line.
                half silhouette = inCenter * (1.0 - allNeighboursInside)
                                + (1.0 - inCenter) * anyNeighbourInside;

                // Colour-type boundaries are only outlined when both sides are inside the
                // silhouette; otherwise the silhouette term above handles the edge.
                half colorEdge = smoothstep(_Threshold, _Threshold + 0.03, edge) * inCenter * allNeighboursInside;

                half mask = saturate(silhouette + colorEdge);
                half3 result = lerp(center, _OutlineColor.rgb, mask * _OutlineColor.a);

                return half4(result, 1.0);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
