// Dashed, flowing line for a LineRenderer. Expects Texture Mode = Stretch (PrismDashedLine sets that up).
Shader "PrismFX/DashedLine"
{
    Properties
    {
        [HDR] _Color ("Color", Color) = (0.2, 0.9, 1, 1)
        _DashLength ("Dash Length (m)", Float) = 0.08
        _GapLength ("Gap Length (m)", Float) = 0.05
        _Speed ("Flow Speed (m/s)", Float) = 0.25
        _LineLength ("Line Length (script sets)", Float) = 1
        _EdgeSoftness ("Edge Softness", Range(0.001, 1)) = 0.35
        _GlowStrength ("Glow Strength", Range(0, 4)) = 1
        _EndFade ("End Fade (m)", Float) = 0.03
        _Fade ("Fade", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent+10" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        Pass
        {
            Name "DashedLine"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off ZTest [_ZTest] Cull Off

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                float _DashLength;
                float _GapLength;
                float _Speed;
                float _LineLength;
                float _EdgeSoftness;
                float _GlowStrength;
                float _EndFade;
                float _Fade;
                float _ZTest;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4  color      : COLOR;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes v)
            {
                Varyings o = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.color = v.color;
                o.uv = v.uv;
                return o;
            }

            half4 Frag(Varyings i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float d = i.uv.x * _LineLength;            // meters along the line
                float p = d - _Time.y * _Speed;
                float period = max(_DashLength + _GapLength, 1e-4);
                float f = frac(p / period) * period;
                float aa = fwidth(p) * 1.5 + 1e-5;
                float dash = (_GapLength <= 0.0) ? 1.0 : saturate((_DashLength * 0.5 - abs(f - _DashLength * 0.5)) / aa + 0.5);

                float across = 1.0 - abs(i.uv.y * 2.0 - 1.0);   // 1 in the middle, 0 at the edges
                float core = smoothstep(0.0, _EdgeSoftness, across);
                float glow = across * across * _GlowStrength * 0.5;

                float endFade = max(_EndFade, 1e-4);
                float ends = saturate(d / endFade) * saturate((_LineLength - d) / endFade);

                float a = saturate(core + glow) * dash * ends * _Color.a * i.color.a * _Fade;
                half3 rgb = _Color.rgb * i.color.rgb * (1.0 + glow);
                return half4(rgb, a);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
