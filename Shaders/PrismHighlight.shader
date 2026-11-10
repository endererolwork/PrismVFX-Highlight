// Not meant to go on a material. PrismHighlightFeature draws these passes with DrawRenderer.
// Pass order matters less than the names, the C# side looks passes up by name.
Shader "Hidden/PrismFX/Highlight"
{
    Properties
    {
        [HideInInspector] _HL_FadeVec ("Fade (outline, glow, overlay, inner)", Vector) = (1,1,1,1)
        [HideInInspector] _HL_Spot0 ("Touch spot 0 (xyz, strength)", Vector) = (0,0,0,0)
        [HideInInspector] _HL_Spot1 ("Touch spot 1 (xyz, strength)", Vector) = (0,0,0,0)
        [HideInInspector] _HL_CenterWS ("Center WS", Vector) = (0,0,0,0)
        [HideInInspector] _Intensities ("Intensities (outline, glow, overlay, innerGlow)", Vector) = (1,1,1,1)
        [HideInInspector] _HullCull ("Hull Cull", Float) = 1
        [HideInInspector] _PrismStencilBit ("Stencil Bit (URP user bits 1,2,4,8)", Float) = 8

        [HDR] _OutlineColor ("Outline Color", Color) = (0.2,0.9,1,1)
        _OutlineParams ("Outline (width, widthMode, normalMode, glowWidth)", Vector) = (2.5,0,0,8)
        _OutlineZTest ("Outline ZTest", Float) = 4
        _DashParams ("Dash (mode, count, fill, speed)", Vector) = (0,32,0.55,0.5)
        _DashParams2 ("Dash (angle, softness, glowDashed, -)", Vector) = (45,0.05,0,0)

        [HDR] _GlowColor ("Glow Color", Color) = (0.2,0.9,1,1)
        _GlowZTest ("Glow ZTest", Float) = 4
        _GlowParams ("Glow (passes, dither, pulseSpeed, pulseAmount)", Vector) = (4,0.5,1,0.3)

        [HDR] _InnerGlowColor ("Inner Glow Color", Color) = (1,1,1,1)
        _InnerGlowPower ("Inner Glow Power", Float) = 2.5

        [HDR] _OverlayColor ("Overlay Color", Color) = (1,1,0,1)
        _OverlayTex ("Overlay Texture", 2D) = "white" {}
        _OverlayParams ("Overlay (blend, minIntensity, animSpeed, mapping)", Vector) = (0.5,0.5,1,0)
        _OverlayTexParams ("Overlay Tex (scale, scrollX, scrollY, -)", Vector) = (1,0,0,0)
        _SurfaceZTest ("Surface ZTest", Float) = 4

        [HDR] _SeeThroughColor ("See Through Color", Color) = (1,0.2,0.2,1)
        _SeeThroughParams ("See Through (intensity, colorBlend, noise, border)", Vector) = (0,0.5,0.5,0.5)

        [HDR] _HitColor ("Hit Color", Color) = (1,1,1,1)
        _HitParams ("Hit (startTime, duration, mode, intensity)", Vector) = (-1000,0.25,0,1)

        _TargetTex ("Target Texture", 2D) = "white" {}
        [HDR] _TargetColor ("Target Color", Color) = (1,1,1,1)
        _TargetZTest ("Target ZTest", Float) = 8
        _TargetParams ("Target (alpha, pulse, -, -)", Vector) = (1,0.15,0,0)

        [HDR] _ProxColor ("Touch Glow Color", Color) = (0.6,0.95,1,1)
        _ProxParams ("Touch Glow (radius, falloff, ring, hoverDistance)", Vector) = (0.035,2,0.5,0.1)
    }

    HLSLINCLUDE
    #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

    CBUFFER_START(UnityPerMaterial)
        float4 _HL_FadeVec;
        float4 _HL_Spot0;
        float4 _HL_Spot1;
        float4 _HL_CenterWS;
        float4 _Intensities;
        float  _HullCull;
        float  _PrismStencilBit;
        half4  _OutlineColor;
        float4 _OutlineParams;
        float  _OutlineZTest;
        float4 _DashParams;
        float4 _DashParams2;
        half4  _GlowColor;
        float  _GlowZTest;
        float4 _GlowParams;
        half4  _InnerGlowColor;
        float  _InnerGlowPower;
        half4  _OverlayColor;
        float4 _OverlayTex_ST;
        float4 _OverlayParams;
        float4 _OverlayTexParams;
        float  _SurfaceZTest;
        half4  _SeeThroughColor;
        float4 _SeeThroughParams;
        half4  _HitColor;
        float4 _HitParams;
        float4 _TargetTex_ST;
        half4  _TargetColor;
        float  _TargetZTest;
        float4 _TargetParams;
        half4  _ProxColor;
        float4 _ProxParams;
    CBUFFER_END

    TEXTURE2D(_OverlayTex); SAMPLER(sampler_OverlayTex);
    TEXTURE2D(_TargetTex);  SAMPLER(sampler_TargetTex);

    #define PRISM_TAU 6.28318530718

    struct Attributes
    {
        float4 positionOS : POSITION;
        float3 normalOS   : NORMAL;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_INPUT_INSTANCE_ID
    };

    // noise / util
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

    float ValueNoise(float2 p)
    {
        float2 i = floor(p);
        float2 f = frac(p);
        float a = Hash21(i);
        float b = Hash21(i + float2(1, 0));
        float c = Hash21(i + float2(0, 1));
        float d = Hash21(i + float2(1, 1));
        float2 u = f * f * (3.0 - 2.0 * f);
        return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y);
    }

    float InterleavedGradientNoise(float2 pixel)
    {
        return frac(52.9829189 * frac(dot(pixel, float2(0.06711056, 0.00583715))));
    }

    // oscillates between 1 and 1 - amount
    float Pulse(float speed, float amount)
    {
        return 1.0 - amount * (0.5 + 0.5 * sin(_Time.y * speed * PRISM_TAU));
    }

    float Rim(float3 positionWS, float3 normalWS, float power)
    {
        float3 V = normalize(GetWorldSpaceViewDir(positionWS));
        float ndv = saturate(abs(dot(normalize(normalWS), V)));
        return pow(1.0 - ndv, power);
    }

    // hull (outline / glow)
    struct VaryingsHull
    {
        float4 positionCS : SV_POSITION;
        float4 clipPos    : TEXCOORD0;
        float4 centerCS   : TEXCOORD1;
        float3 positionWS : TEXCOORD2;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    VaryingsHull HullVertex(Attributes v, float width)
    {
        VaryingsHull o = (VaryingsHull)0;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

        // FromCenter avoids gaps on flat-shaded meshes where normals are split
        float3 dirOS = (_OutlineParams.z > 0.5) ? SafeNormalize(v.positionOS.xyz) : v.normalOS;
        float3 positionWS = TransformObjectToWorld(v.positionOS.xyz);
        float3 dirWS = SafeNormalize(TransformObjectToWorldDir(dirOS, false));

        float4 positionCS;
        if (_OutlineParams.y > 0.5)
        {
            // world mode, width in cm
            positionWS += dirWS * width * 0.01;
            positionCS = TransformWorldToHClip(positionWS);
        }
        else
        {
            // pixel mode: push along the projected normal, * w to cancel the perspective divide
            positionCS = TransformWorldToHClip(positionWS);
            float3 dirCS = mul((float3x3)GetWorldToHClipMatrix(), dirWS);
            float len = length(dirCS.xy);
            float2 offset = (len > 1e-5) ? dirCS.xy / len : float2(0, 0);
            offset *= saturate(len * 4.0);
            offset *= width * 2.0 * positionCS.w / _ScaledScreenParams.xy;
            positionCS.xy += offset;
        }

        o.positionCS = positionCS;
        o.clipPos = positionCS;
        o.centerCS = TransformWorldToHClip(_HL_CenterWS.xyz);
        o.positionWS = positionWS;
        return o;
    }

    // mode: 0 solid, 1 radial (around the silhouette), 2 screen stripes, 3 world stripes
    float DashMask(float4 clipPos, float4 centerCS, float3 positionWS)
    {
        float mode = _DashParams.x;
        if (mode < 0.5) return 1.0;

        float aspect = _ScaledScreenParams.x / max(_ScaledScreenParams.y, 1.0);
        float2 ndc = clipPos.xy / clipPos.w;
        float a = radians(_DashParams2.x);
        float coord;

        if (mode < 1.5)
        {
            float2 c = centerCS.xy / centerCS.w;
            float2 d = (ndc - c) * float2(aspect, 1.0);
            coord = (atan2(d.y, d.x) / PRISM_TAU + 0.5) * _DashParams.y;
        }
        else if (mode < 2.5)
        {
            coord = dot(ndc * float2(aspect, 1.0), float2(cos(a), sin(a))) * _DashParams.y * 0.5;
        }
        else
        {
            float3 axis = normalize(float3(sin(a), cos(a), sin(a)));
            coord = dot(positionWS, axis) * _DashParams.y;
        }

        coord -= _Time.y * _DashParams.w;
        float f = frac(coord);
        float fill = _DashParams.z;
        float aa = fwidth(coord) + _DashParams2.y * 0.5 + 1e-4;
        return saturate((fill * 0.5 - abs(f - fill * 0.5)) / aa + 0.5);
    }

    VaryingsHull OutlineVert(Attributes v) { return HullVertex(v, _OutlineParams.x); }

    half4 OutlineFrag(VaryingsHull i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float a = _OutlineColor.a * _Intensities.x * _HL_FadeVec.x * DashMask(i.clipPos, i.centerCS, i.positionWS);
        return half4(_OutlineColor.rgb, saturate(a));
    }

    VaryingsHull GlowVertex(Attributes v, float index)
    {
        float passes = max(_GlowParams.x, 1.0);
        return HullVertex(v, _OutlineParams.w * (index + 1.0) / passes);
    }

    VaryingsHull GlowVert0(Attributes v) { return GlowVertex(v, 0.0); }
    VaryingsHull GlowVert1(Attributes v) { return GlowVertex(v, 1.0); }
    VaryingsHull GlowVert2(Attributes v) { return GlowVertex(v, 2.0); }
    VaryingsHull GlowVert3(Attributes v) { return GlowVertex(v, 3.0); }

    half4 GlowFrag(VaryingsHull i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float passes = max(_GlowParams.x, 1.0);
        float a = _GlowColor.a * _Intensities.y * _HL_FadeVec.y / passes * Pulse(_GlowParams.z, _GlowParams.w);
        if (_DashParams2.z > 0.5) a *= DashMask(i.clipPos, i.centerCS, i.positionWS);
        half3 rgb = _GlowColor.rgb * a;
        // a bit of noise so the glow doesn't band on 8-bit targets
        float n = InterleavedGradientNoise(i.positionCS.xy) - 0.5;
        rgb = max(rgb + n * _GlowParams.y * (1.0 / 48.0) * saturate(a * 8.0), 0.0);
        return half4(rgb, 0.0);
    }

    // surface (overlay + inner glow + hit)
    struct VaryingsSurf
    {
        float4 positionCS : SV_POSITION;
        float3 positionWS : TEXCOORD0;
        float3 normalWS   : TEXCOORD1;
        float2 uv         : TEXCOORD2;
        float4 clipPos    : TEXCOORD3;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    VaryingsSurf SurfVert(Attributes v)
    {
        VaryingsSurf o = (VaryingsSurf)0;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
        o.normalWS = TransformObjectToWorldNormal(v.normalOS);
        o.positionCS = TransformWorldToHClip(o.positionWS);
        o.clipPos = o.positionCS;
        o.uv = v.uv;
        return o;
    }

    half4 SampleOverlay(VaryingsSurf i)
    {
        float mapping = _OverlayParams.w;
        float scale = _OverlayTexParams.x;
        float2 scroll = _OverlayTexParams.yz * _Time.y;

        if (mapping < 0.5)
            return SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, i.uv * scale + scroll);

        if (mapping < 1.5)
        {
            float3 w = abs(normalize(i.normalWS));
            w /= (w.x + w.y + w.z + 1e-5);
            float3 p = i.positionWS * scale;
            half4 tx = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, p.zy + scroll);
            half4 ty = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, p.xz + scroll);
            half4 tz = SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, p.xy + scroll);
            return tx * w.x + ty * w.y + tz * w.z;
        }

        float2 ndc = i.clipPos.xy / i.clipPos.w;
        ndc.x *= _ScaledScreenParams.x / max(_ScaledScreenParams.y, 1.0);
        return SAMPLE_TEXTURE2D(_OverlayTex, sampler_OverlayTex, ndc * scale + scroll);
    }

    float TouchSpot(float4 p, float3 positionWS, float3 normalWS)
    {
        if (p.w <= 0.001) return 0.0;
        float3 toTip = p.xyz - positionWS;
        float d = length(toTip);
        float radius = _ProxParams.x + (1.0 - p.w) * _ProxParams.w;
        float core = pow(saturate(1.0 - d / radius), _ProxParams.y);
        float ring = saturate(1.0 - abs(d - radius * 0.85) / (radius * 0.08)) * _ProxParams.z;
        // only the side facing the finger, so thin parts don't glow through
        float facing = saturate(dot(normalize(normalWS), toTip / max(d, 1e-4)) * 1.5 + 0.2);
        return (core + ring * 0.6) * p.w * facing;
    }

    half4 SurfaceFrag(VaryingsSurf i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float rim = Rim(i.positionWS, i.normalWS, _InnerGlowPower);

        // overlay, premultiplied. blend 0 = alpha, 1 = additive
        half4 tex = SampleOverlay(i);
        float speed = _OverlayParams.z;
        float pulse = speed > 0.0 ? lerp(_OverlayParams.y, 1.0, 0.5 + 0.5 * sin(_Time.y * speed * PRISM_TAU)) : 1.0;
        float ovA = saturate(_OverlayColor.a * tex.a * _Intensities.z * pulse * _HL_FadeVec.z);
        half3 rgb = _OverlayColor.rgb * tex.rgb * ovA;
        float alpha = ovA * (1.0 - _OverlayParams.x);

        // inner glow
        rgb += _InnerGlowColor.rgb * (_InnerGlowColor.a * _Intensities.w * rim * _HL_FadeVec.w);

        // touch glow: spot under the fingertip. Starts wide and faint while hovering,
        // tightens up as the finger gets closer (radius grows with hover distance).
        float spot = TouchSpot(_HL_Spot0, i.positionWS, i.normalWS) + TouchSpot(_HL_Spot1, i.positionWS, i.normalWS);
        rgb += _ProxColor.rgb * (_ProxColor.a * saturate(spot));

        // hit flash ignores the highlight fade on purpose
        float hitT = (_Time.y - _HitParams.x) / max(_HitParams.y, 1e-3);
        float hit = (hitT >= 0.0 && hitT < 1.0) ? (1.0 - hitT) * (1.0 - hitT) * _HitParams.w : 0.0;
        float mode = _HitParams.z;
        if (mode < 0.5 || mode > 1.5)
        {
            rgb = lerp(rgb, _HitColor.rgb, hit);
            alpha = lerp(alpha, 1.0, hit);
        }
        if (mode > 0.5)
            rgb += _HitColor.rgb * rim * hit * 2.0;

        return half4(rgb, saturate(alpha));
    }

    // see-through
    half4 SeeThroughFrag(VaryingsSurf i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        float intensity = _SeeThroughParams.x;
        float rim = Rim(i.positionWS, i.normalWS, 2.0);
        float shade = lerp(1.0, 0.35 + 0.65 * (1.0 - rim), _SeeThroughParams.y);
        float3 p = i.positionWS * 6.0;
        float n = ValueNoise(p.xz + p.y + _Time.y * 3.0);
        float noise = lerp(1.0, 0.5 + n, _SeeThroughParams.z);
        half3 rgb = _SeeThroughColor.rgb * shade * noise + _SeeThroughColor.rgb * rim * _SeeThroughParams.w * 2.0;
        float a = saturate(intensity * _SeeThroughColor.a * noise + rim * _SeeThroughParams.w * intensity);
        return half4(rgb, a);
    }

    // mask / clear
    struct VaryingsSimple
    {
        float4 positionCS : SV_POSITION;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    VaryingsSimple SimpleVert(Attributes v)
    {
        VaryingsSimple o = (VaryingsSimple)0;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
        return o;
    }

    half4 NullFrag(VaryingsSimple i) : SV_Target { return 0; }

    // target reticle
    struct VaryingsTarget
    {
        float4 positionCS : SV_POSITION;
        float2 uv         : TEXCOORD0;
        UNITY_VERTEX_OUTPUT_STEREO
    };

    VaryingsTarget TargetVert(Attributes v)
    {
        VaryingsTarget o = (VaryingsTarget)0;
        UNITY_SETUP_INSTANCE_ID(v);
        UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
        o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
        o.uv = v.uv;
        return o;
    }

    half4 TargetFrag(VaryingsTarget i) : SV_Target
    {
        UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);
        half4 tex = SAMPLE_TEXTURE2D(_TargetTex, sampler_TargetTex, i.uv);
        float a = saturate(tex.a * _TargetColor.a * _TargetParams.x * Pulse(1.5, _TargetParams.y));
        return half4(tex.rgb * _TargetColor.rgb * a, a);
    }
    ENDHLSL

    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" "RenderPipeline"="UniversalPipeline" }

        // silhouette -> stencil, so outline/glow stay outside the object
        Pass
        {
            Name "PrismMask"
            ZWrite Off ZTest Always Cull Off ColorMask 0
            Stencil { Ref [_PrismStencilBit] WriteMask [_PrismStencilBit] Comp Always Pass Replace }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex SimpleVert
            #pragma fragment NullFrag
            ENDHLSL
        }

        // clear our bit again so the next object starts clean
        Pass
        {
            Name "PrismClear"
            ZWrite Off ZTest Always Cull Off ColorMask 0
            Stencil { Ref 0 WriteMask [_PrismStencilBit] Comp Always Pass Zero }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex SimpleVert
            #pragma fragment NullFrag
            ENDHLSL
        }

        // glow layers, each one a bit wider (additive)
        Pass
        {
            Name "PrismGlow0"
            ZWrite Off ZTest [_GlowZTest] Cull [_HullCull] Blend One One
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] Comp NotEqual }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex GlowVert0
            #pragma fragment GlowFrag
            ENDHLSL
        }
        Pass
        {
            Name "PrismGlow1"
            ZWrite Off ZTest [_GlowZTest] Cull [_HullCull] Blend One One
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] Comp NotEqual }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex GlowVert1
            #pragma fragment GlowFrag
            ENDHLSL
        }
        Pass
        {
            Name "PrismGlow2"
            ZWrite Off ZTest [_GlowZTest] Cull [_HullCull] Blend One One
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] Comp NotEqual }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex GlowVert2
            #pragma fragment GlowFrag
            ENDHLSL
        }
        Pass
        {
            Name "PrismGlow3"
            ZWrite Off ZTest [_GlowZTest] Cull [_HullCull] Blend One One
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] Comp NotEqual }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex GlowVert3
            #pragma fragment GlowFrag
            ENDHLSL
        }

        Pass
        {
            Name "PrismOutline"
            ZWrite Off ZTest [_OutlineZTest] Cull [_HullCull] Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] Comp NotEqual }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex OutlineVert
            #pragma fragment OutlineFrag
            ENDHLSL
        }

        Pass
        {
            Name "PrismSurface"
            ZWrite Off ZTest [_SurfaceZTest] Cull Back Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex SurfVert
            #pragma fragment SurfaceFrag
            ENDHLSL
        }

        // only where the object is behind something; stencil keeps it to one layer
        Pass
        {
            Name "PrismSeeThrough"
            ZWrite Off ZTest Greater Cull Back Blend SrcAlpha OneMinusSrcAlpha
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] WriteMask [_PrismStencilBit] Comp NotEqual Pass Replace }
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex SurfVert
            #pragma fragment SeeThroughFrag
            ENDHLSL
        }

        Pass
        {
            Name "PrismTarget"
            ZWrite Off ZTest [_TargetZTest] Cull Off Blend One OneMinusSrcAlpha
            HLSLPROGRAM
            #pragma target 3.0
            #pragma multi_compile_instancing
            #pragma vertex TargetVert
            #pragma fragment TargetFrag
            ENDHLSL
        }
    }
    Fallback Off
}
