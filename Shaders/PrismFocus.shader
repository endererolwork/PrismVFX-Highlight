// Focus: dims / desaturates / blurs everything except the highlighted objects.
// Drawn as one fullscreen triangle by PrismHighlightFeature. The highlighted objects are
// masked out with the stencil bit beforehand, so they stay untouched.
Shader "Hidden/PrismFX/Focus"
{
    Properties
    {
        [HideInInspector] _PrismStencilBit ("Stencil Bit", Float) = 8
        [HideInInspector] _FocusColor ("Dim Color", Color) = (0,0,0,1)
        [HideInInspector] _FocusParams ("Focus (amount, dim, desaturate, blur)", Vector) = (1,0.6,0.7,0.5)
        [HideInInspector] _FocusBlurRadius ("Blur Radius (px)", Float) = 3
    }

    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }

        Pass
        {
            Name "PrismFocus"
            ZWrite Off ZTest Always Cull Off Blend Off
            Stencil { Ref [_PrismStencilBit] ReadMask [_PrismStencilBit] Comp NotEqual }

            HLSLPROGRAM
            #pragma target 3.0
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            half4 _FocusColor;
            float4 _FocusParams;
            float _FocusBlurRadius;
            float _PrismStencilBit;

            half3 Fetch(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv).rgb;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 uv = input.texcoord;
                float amount = _FocusParams.x;

                half3 c = Fetch(uv);

                // 8 taps on a ring plus the center. Cheap, and with bilinear filtering
                // it reads as a soft blur at small radii. Skipped entirely when blur is 0.
                float blur = _FocusParams.w * amount;
                if (blur > 0.001)
                {
                    float2 r = _FocusBlurRadius / _ScaledScreenParams.xy;
                    half3 sum = c;
                    sum += Fetch(uv + r * float2( 1.0,  0.0));
                    sum += Fetch(uv + r * float2(-1.0,  0.0));
                    sum += Fetch(uv + r * float2( 0.0,  1.0));
                    sum += Fetch(uv + r * float2( 0.0, -1.0));
                    sum += Fetch(uv + r * float2( 0.7,  0.7));
                    sum += Fetch(uv + r * float2(-0.7,  0.7));
                    sum += Fetch(uv + r * float2( 0.7, -0.7));
                    sum += Fetch(uv + r * float2(-0.7, -0.7));
                    c = lerp(c, sum / 9.0, saturate(blur));
                }

                half lum = dot(c, half3(0.2126, 0.7152, 0.0722));
                c = lerp(c, lum.xxx, _FocusParams.z * amount);
                c = lerp(c, _FocusColor.rgb, _FocusParams.y * amount);
                return half4(c, 1.0);
            }
            ENDHLSL
        }
    }
    Fallback Off
}
