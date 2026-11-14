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

                half4 centerSample = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                half3 center = centerSample.rgb;
                half3 left   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(texel.x, 0.0)).rgb;
                half3 right  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, 0.0)).rgb;
                half3 down   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - float2(0.0, texel.y)).rgb;
                half3 up     = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(0.0, texel.y)).rgb;
                half3 downLeft  = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv - texel).rgb;
                half3 downRight = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(texel.x, -texel.y)).rgb;
                half3 upLeft    = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + float2(-texel.x, texel.y)).rgb;
                half3 upRight   = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv + texel).rgb;

                half3 background = _BackgroundColor.rgb;

                // 颜色突变（用于区分不同瓦片颜色：普通 vs 深灰）。
                half edge = max(
                    max(max(distance(center, left), distance(center, right)),
                        max(distance(center, down), distance(center, up))),
                    max(max(distance(center, downLeft), distance(center, downRight)),
                        max(distance(center, upLeft), distance(center, upRight))));

                // 与相机背景做轮廓判定。直接在抗锯齿的 45 度斜边上采样原始
                // 色差会出现棋盘格，因此改为判断"内部像素是否与背景像素相邻"，
                // 并且只画在内侧：干净的一个像素，没有光晕。
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

                // 轮廓内侧 1px + 外侧 1px。由于内外判定是与背景色做二值比较，
                // 这条带子在抗锯齿的斜边上保持连续，不会变成棋盘格。
                half silhouette = inCenter * (1.0 - allNeighboursInside)
                                + (1.0 - inCenter) * anyNeighbourInside;

                // 只有两侧都在轮廓内部时才把颜色类型边界描出来；
                // 否则交给上面的轮廓项处理。
                half colorEdge = smoothstep(_Threshold, _Threshold + 0.03, edge) * inCenter * allNeighboursInside;

                half mask = saturate(silhouette + colorEdge);
                half3 result = lerp(center, _OutlineColor.rgb, mask * _OutlineColor.a);

                // 保留源 alpha，让透明背景的相机（捕获组）能输出可透视的快照。
                return half4(result, centerSample.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}
