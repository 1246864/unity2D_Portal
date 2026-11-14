Shader "Hidden/Platformer/PortalHintBeam"
{
    Properties
    {
        _Color ("颜色", Color) = (1,1,1,1)
        _Direction ("朝向（1 = 朝右，-1 = 朝左）", Float) = 1
        _CoreWidth ("核心线宽（占光带长度的比例）", Range(0.01, 0.5)) = 0.18
        _FlowSpeed ("流光速度", Float) = 2.5
        _EndWidth ("两端高亮宽度（纹理纵向比例）", Range(0.01, 0.5)) = 0.12
        _Alpha ("整体透明度", Range(0, 1)) = 0.5
        _BreathFrequency ("呼吸频率（赫兹）", Float) = 0.5
        _NoiseStrength ("噪音失真强度", Range(0, 0.3)) = 0.05
        _NoiseScale ("噪音缩放", Float) = 7
        _NoiseSpeed ("噪音流动速度", Float) = 0.9
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Cull Off
        Lighting Off
        ZWrite Off
        Blend SrcAlpha One

        Pass
        {
            Tags { "LightMode" = "Universal2D" }

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata_t
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                fixed4 color : COLOR;
            };

            fixed4 _Color;
            float _Direction;
            float _CoreWidth;
            float _FlowSpeed;
            float _EndWidth;
            float _Alpha;
            float _BreathFrequency;
            float _NoiseStrength;
            float _NoiseScale;
            float _NoiseSpeed;

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

            v2f vert(appdata_t v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // u = 0 在细线（门背面），u = 1 在 facing 方向的末端。
                float u = _Direction > 0.0 ? i.uv.x : 1.0 - i.uv.x;

                // 低频噪音在 UV 两轴上持续滚动，让掩码和边缘一直动。
                float2 noiseUV = i.uv * _NoiseScale + float2(
                    _Time.y * _NoiseSpeed,
                    _Time.y * _NoiseSpeed * 1.31);
                float noise = noise21(noiseUV) * 0.65 + noise21(noiseUV * 2.17 + 13.1) * 0.35;
                float du = u + (noise - 0.5) * _NoiseStrength;

                float fade = pow(saturate(1.0 - du), 1.5);
                float core = 1.0 - smoothstep(0.0, max(_CoreWidth, 0.0001), abs(du));
                float flow = 0.5 + 0.5 * sin((i.uv.y * 12.0 - _Time.y * _FlowSpeed + noise * 2.0) * 6.2831853);
                float endFade = smoothstep(0.0, max(_EndWidth, 0.0001), min(i.uv.y, 1.0 - i.uv.y));

                // 呼吸亮度在 0.2 ~ 1.0 之间变化，不再完全消失。
                float breath = lerp(0.2, 1.0, 0.5 + 0.5 * sin(_Time.y * _BreathFrequency * 6.2831853 + 1.7));

                float alpha = saturate(fade * (0.25 + 0.75 * flow) + core * 0.9);
                alpha *= endFade * _Alpha * breath * (0.85 + 0.15 * noise);

                float3 rgb = _Color.rgb * (0.65 + 0.35 * flow + core * 0.6);
                rgb *= (0.9 + 0.2 * noise) * (0.75 + 0.25 * breath);

                return fixed4(rgb * i.color.rgb, alpha * _Color.a * i.color.a);
            }
            ENDCG
        }
    }

    Fallback "Sprites/Default"
}
