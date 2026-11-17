Shader "Custom/VFX/WuxiaWhiteWindDistortion"
{
    Properties
    {
        [Header(Pure White Sheer Wind Streaks)]
        _WindStreakTex ("Pure White Wind Streaks (RGB + A)", 2D) = "white" {}
        [HDR] _TintColor ("Pure White Wind Color (HDR)", Color) = (1.0, 1.0, 1.0, 0.55)
        _StreakContrast ("Wind Streak Sharpness", Range(0.5, 4.0)) = 1.75

        [Header(Outer Razor White Qi Rim)]
        [HDR] _RimColor ("Pure White Outer Rim (HDR)", Color) = (1.8, 1.8, 1.8, 1.0)
        _OuterRimPower ("Outer Rim Sharpness", Range(1.0, 24.0)) = 10.0
        _OuterRimIntensity ("Outer Rim Intensity", Range(0.0, 4.0)) = 1.65

        [Header(Screen Space Air Refraction)]
        _BumpMap ("Air Distortion Normal Map", 2D) = "bump" {}
        _DistortionStrength ("Air Distortion Strength", Range(0.0, 0.20)) = 0.042

        [Header(Wind Flow Speed)]
        _ScrollSpeedU ("Wind Flow Speed (U)", Float) = -3.2
        _ScrollSpeedV ("Wind Flow Speed (V)", Float) = 0.0
        _DistortionScrollU ("Distortion Scroll Speed (U)", Float) = -3.8
        _DistortionScrollV ("Distortion Scroll Speed (V)", Float) = 0.15

        [Header(Master Fade)]
        _Opacity ("Master Opacity", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+60"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "WuxiaWhiteWindDistortionForward"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma target 3.0

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 color      : COLOR;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float4 screenPos  : TEXCOORD0;
                float2 uvStreak   : TEXCOORD1;
                float2 uvBump     : TEXCOORD2;
                float2 rawUV      : TEXCOORD3;
                float4 color      : COLOR;
            };

            TEXTURE2D(_WindStreakTex);
            SAMPLER(sampler_WindStreakTex);

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _WindStreakTex_ST;
                float4 _BumpMap_ST;
                half4  _TintColor;
                half   _StreakContrast;
                half4  _RimColor;
                half   _OuterRimPower;
                half   _OuterRimIntensity;
                half   _DistortionStrength;
                float  _ScrollSpeedU;
                float  _ScrollSpeedV;
                float  _DistortionScrollU;
                float  _DistortionScrollV;
                half   _Opacity;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;

                VertexPositionInputs posInputs = GetVertexPositionInputs(input.positionOS.xyz);
                output.positionCS = posInputs.positionCS;
                output.screenPos = ComputeScreenPos(posInputs.positionCS);

                float t = _Time.y;
                output.rawUV = input.uv;
                output.uvStreak = TRANSFORM_TEX(input.uv, _WindStreakTex) + float2(_ScrollSpeedU * t, _ScrollSpeedV * t);
                output.uvBump = TRANSFORM_TEX(input.uv, _BumpMap) + float2(_DistortionScrollU * t, _DistortionScrollV * t);

                // Supports Vertex Color & Alpha from both ParticleSystem and TrailRenderer
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Smooth head/tail & inner/outer envelope (prevents any hard rectangular edges)
                float u = saturate(input.rawUV.x);
                float v = saturate(input.rawUV.y);

                float headTailFade = sin(u * 3.14159265);
                headTailFade = pow(saturate(headTailFade), 0.65);

                float innerFade = smoothstep(0.0, 0.30, v);
                float outerFeather = 1.0 - smoothstep(0.95, 1.0, v);
                float radialMask = innerFade * outerFeather;

                float envelope = saturate(headTailFade * radialMask * input.color.a * _Opacity);
                if (envelope <= 0.001)
                {
                    discard;
                }

                // 2. Screen-Space Air Refraction via _CameraOpaqueTexture
                half4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uvBump);
                half3 normalTS = UnpackNormal(packedNormal);

                float2 distortionOffset = normalTS.xy * _DistortionStrength * envelope;
                float2 screenUV = input.screenPos.xy / max(0.0001, input.screenPos.w);
                float2 refractedUV = saturate(screenUV + distortionOffset);
                half3 sceneColor = SampleSceneColor(refractedUV);

                // 3. Pure White Wind Streaks (desaturated to guarantee zero green/blue color cast)
                half4 streakSample = SAMPLE_TEXTURE2D(_WindStreakTex, sampler_WindStreakTex, input.uvStreak);
                half streakLum = dot(streakSample.rgb, half3(0.3333, 0.3333, 0.3334)) * streakSample.a;
                half streakMask = pow(saturate(streakLum), _StreakContrast);

                // 4. Razor-Sharp Outer Pure White Qi Rim (v -> 0.85..0.95)
                float rimFactor = saturate((v - 0.15) / 0.80);
                half outerRim = pow(rimFactor, _OuterRimPower) * outerFeather * _OuterRimIntensity;

                // 5. Enforce Pure White palette multiplied by Vertex Color RGB
                half3 pureWhiteTint = _TintColor.rgb * input.color.rgb;
                half3 sheerWindLayer = pureWhiteTint * streakMask;
                half3 razorRimLayer = _RimColor.rgb * input.color.rgb * outerRim;

                half windAlpha = saturate(streakMask * _TintColor.a + outerRim * 0.75);
                half3 finalRGB = lerp(sceneColor, sceneColor + sheerWindLayer, saturate(_TintColor.a * 1.15)) + razorRimLayer;

                half distortionVisibility = saturate(length(normalTS.xy) * (_DistortionStrength * 13.5));
                half finalAlpha = saturate(max(distortionVisibility, windAlpha) * envelope);

                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
