// Hologram shader. Works as a regular material too; PrismHighlight uses it for Overlay / Replace modes.
Shader "PrismFX/Hologram"
{
    Properties
    {
        [Header(Color)]
        [HDR] _HoloColor ("Color", Color) = (0.2, 0.8, 1, 1)
        [HDR] _RimColor ("Rim Color", Color) = (0.6, 1, 1, 1)
        _MainTex ("Texture (optional)", 2D) = "white" {}
        _TexInfluence ("Texture Influence", Range(0,1)) = 0
        _Alpha ("Alpha", Range(0,1)) = 0.35
        _FresnelPower ("Fresnel Power", Range(0.5,8)) = 2.5
        _RimIntensity ("Rim Intensity", Range(0,5)) = 1.5

        [Header(Scanlines)]
        [Enum(World,0,Object,1,Screen,2)] _ScanSpace ("Scan Space", Float) = 0
        _ScanDensity ("Scanline Density", Float) = 40
        _ScanSpeed ("Scanline Speed", Float) = 1
        _ScanThickness ("Scanline Thickness", Range(0.01,1)) = 0.25
        _ScanIntensity ("Scanline Intensity", Range(0,3)) = 1
        _FineDensity ("Fine Line Density", Float) = 300
        _FineIntensity ("Fine Line Intensity", Range(0,1)) = 0.15

        [Header(Scan Band)]
        _BandFrequency ("Band Frequency", Float) = 0.5
        _BandSpeed ("Band Speed", Float) = 0.5
        _BandWidth ("Band Width", Range(0.001,0.5)) = 0.08
        _BandIntensity ("Band Intensity", Range(0,5)) = 2

        [Header(Flicker and Glitch)]
        _FlickerSpeed ("Flicker Speed", Float) = 12
        _FlickerAmount ("Flicker Amount", Range(0,1)) = 0.15
        _GlitchAmount ("Glitch Amount", Range(0,0.5)) = 0.02
        _GlitchFrequency ("Glitch Frequency", Float) = 15
        _GlitchSpeed ("Glitch Speed", Float) = 6
        _GlitchProbability ("Glitch Probability", Range(0,1)) = 0.08

        [Header(Render)]
        _Fade ("Fade", Range(0,1)) = 1
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Enum(UnityEngine.Rendering.BlendMode)] _SrcBlend ("Src Blend", Float) = 5
        [Enum(UnityEngine.Rendering.BlendMode)] _DstBlend ("Dst Blend", Float) = 1
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 4
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        half4  _HoloColor;
        half4  _RimColor;
        float4 _MainTex_ST;
        float  _TexInfluence;
        float  _Alpha;
        float  _FresnelPower;
        float  _RimIntensity;
        float  _ScanSpace;
        float  _ScanDensity;
        float  _ScanSpeed;
        float  _ScanThickness;
        float  _ScanIntensity;
        float  _FineDensity;
        float  _FineIntensity;
        float  _BandFrequency;
        float  _BandSpeed;
        float  _BandWidth;
        float  _BandIntensity;
        float  _FlickerSpeed;
        float  _FlickerAmount;
        float  _GlitchAmount;
        float  _GlitchFrequency;
        float  _GlitchSpeed;
        float  _GlitchProbability;
        float  _Fade;
        float  _Cull;
        float  _SrcBlend;
        float  _DstBlend;
        float  _ZTest;
    CBUFFER_END

    TEXTURE2D(_MainTex); SAMPLER(sampler_MainTex);

    #define PRISM_TAU 6.28318530718

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    struct Varyings
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS   : TEXCOORD1;
        float3 positionOS : TEXCOORD2;
        float2 uv         : TEXCOORD3;
        float4 clipPos    : TEXCOORD4;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    float Hash11(float p)
    {
        p = frac(p * 0.1031);
        p *= p + 33.33;
        p *= p + p;
        return frac(p);
    }

    float Hash21(float2 p)
    {
        float3 p3 = frac(float3(p.xyx) * 0.1031);
        p3 += dot(p3, p3.yzx + 33.33);
        return frac((p3.x + p3.y) * p3.z);
    }

    // Random horizontal band offsets. Keep the probability low, it gets uncomfortable in VR.
    float3 ApplyGlitch(float3 positionWS)
    {
        if (_GlitchAmount <= 0.0) return positionWS;
        float t = floor(_Time.y * _GlitchSpeed);
        float band = floor(positionWS.y * _GlitchFrequency);
        float r = Hash21(float2(band, t));
        float active = step(1.0 - _GlitchProbability, Hash11(t * 1.37 + band * 0.113));
        float3 right = UNITY_MATRIX_V[0].xyz;
        return positionWS + right * (r - 0.5) * 2.0 * _GlitchAmount * active;
    }

    Varyings Vert(Attributes v)
    {
        Varyings o = (Varyings)0;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        float3 positionWS = ApplyGlitch(TransformObjectToWorld(v.positionOS.xyz));
        o.positionWS = positionWS;
        o.positionOS = v.positionOS.xyz;
        o.normalWS = TransformObjectToWorldNormal(v.normalOS);
        o.positionCS = TransformWorldToHClip(positionWS);
        o.clipPos = o.positionCS;
        o.uv = TRANSFORM_TEX(v.uv, _MainTex);
        return o;
    }

    float ScanCoord(Varyings i)
    {
        if (_ScanSpace < 0.5) return i.positionWS.y;
        if (_ScanSpace < 1.5) return i.positionOS.y;
        return i.clipPos.y / i.clipPos.w;
    }

    half4 Frag(Varyings i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

        float3 N = normalize(i.normalWS);
        float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
        float fres = pow(1.0 - saturate(abs(dot(N, V))), _FresnelPower);

        float c = ScanCoord(i);

        float s = frac(c * _ScanDensity - _Time.y * _ScanSpeed);
        float scan = 1.0 - smoothstep(0.0, max(_ScanThickness, 1e-3), abs(s - 0.5) * 2.0);
        float fine = 0.5 + 0.5 * sin(c * _FineDensity * PRISM_TAU);

        float b = frac(c * _BandFrequency - _Time.y * _BandSpeed);
        float band = 1.0 - smoothstep(0.0, _BandWidth, abs(b - 0.5));

        float flicker = 1.0 - _FlickerAmount * Hash11(floor(_Time.y * _FlickerSpeed));

        half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uv);
        float lum = dot(tex.rgb, float3(0.299, 0.587, 0.114));
        float texMod = lerp(1.0, lum * 1.5, _TexInfluence);

        half3 col = _HoloColor.rgb * (0.6 + scan * _ScanIntensity * 0.8 + fine * _FineIntensity) * texMod;
        col += _RimColor.rgb * fres * _RimIntensity;
        col += _HoloColor.rgb * band * _BandIntensity;

        float a = _Alpha * _HoloColor.a * (0.55 + 0.45 * scan * _ScanIntensity + fine * _FineIntensity)
                + fres * _RimIntensity * 0.5
                + band * _BandIntensity * 0.25;
        a = saturate(a) * flicker * _Fade;

        return half4(col, a);
    }

    half4 DepthFrag(Varyings i) : SV_Target { return 0; }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" "IgnoreProjector"="True" }

        // Optional depth prepass so inner faces don't stack. Toggled via SetShaderPassEnabled("SRPDefaultUnlit").
        Pass
        {
            Name "HoloDepth"
            Tags { "LightMode"="SRPDefaultUnlit" }
            ZWrite On ColorMask 0 Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment DepthFrag
            ENDHLSL
        }

        Pass
        {
            Name "HoloForward"
            Tags { "LightMode"="UniversalForward" }
            Blend [_SrcBlend] [_DstBlend]
            ZWrite Off ZTest [_ZTest] Cull [_Cull]
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex Vert
            #pragma fragment Frag
            ENDHLSL
        }
    }
    Fallback Off
}
