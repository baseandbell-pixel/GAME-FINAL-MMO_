Shader "Custom/VFX/WuxiaAirDistortion"
{
    Properties
    {
        [Header(Sheer Wind Streaks)]
        _MainTex ("Wind Streaks Mask (RGB + A)", 2D) = "white" {}
        [HDR] _TintColor ("Sheer Wind Color (HDR)", Color) = (0.75, 0.96, 1.0, 0.65)
        _WindStreakContrast ("Streak Contrast", Range(0.5, 4.0)) = 1.65

        [Header(Outer Razor Qi Rim)]
        [HDR] _RimColor ("Outer Razor Rim Color (HDR)", Color) = (2.8, 3.8, 4.2, 1.0)
        _OuterRimPower ("Outer Rim Sharpness", Range(1.0, 24.0)) = 8.0
        _OuterRimIntensity ("Outer Rim Intensity", Range(0.0, 5.0)) = 2.2

        [Header(Screen Space Air Refraction)]
        _BumpMap ("Air Distortion Normal Map", 2D) = "bump" {}
        _DistortionStrength ("Air Distortion Strength", Range(0.0, 0.25)) = 0.045
        _ChromaticAberration ("Prismatic Edge Split", Range(0.0, 0.03)) = 0.004

        [Header(Wind Flow Animation)]
        _ScrollSpeedU ("Flow Speed Along Slash (U)", Float) = -2.8
        _ScrollSpeedV ("Flow Speed Radial (V)", Float) = 0.15
        _DistortionScrollU ("Distortion Scroll Speed (U)", Float) = -3.4
        _DistortionScrollV ("Distortion Scroll Speed (V)", Float) = 0.25

        [Header(Soft Blending)]
        _Opacity ("Master Opacity", Range(0.0, 2.0)) = 1.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+50"
            "IgnoreProjector" = "True"
            "PreviewType" = "Plane"
        }

        Pass
        {
            Name "WuxiaAirDistortionForward"
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
                float2 uvMain     : TEXCOORD1;
                float2 uvBump     : TEXCOORD2;
                float2 rawUV      : TEXCOORD3;
                float4 color      : COLOR;
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                half4  _TintColor;
                half   _WindStreakContrast;
                half4  _RimColor;
                half   _OuterRimPower;
                half   _OuterRimIntensity;
                half   _DistortionStrength;
                half   _ChromaticAberration;
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
                output.uvMain = TRANSFORM_TEX(input.uv, _MainTex) + float2(_ScrollSpeedU * t, _ScrollSpeedV * t);
                output.uvBump = TRANSFORM_TEX(input.uv, _BumpMap) + float2(_DistortionScrollU * t, _DistortionScrollV * t);

                // Vertex Color carries both Particle System Color/Alpha over Lifetime AND Mesh Edge Alpha Taper
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                // 1. Edge & Tip Soft Masks from raw Mesh UV (U = 0..1 along arc, V = 0..1 inner to outer edge)
                float u = saturate(input.rawUV.x);
                float v = saturate(input.rawUV.y);

                // Taper head & tail of the crescent slash smoothly
                float arcTipFade = sin(u * 3.14159265);
                arcTipFade = pow(saturate(arcTipFade), 0.75);

                // Fade inner radius (v -> 0) and keep outer cutting edge (v -> 1) crisp
                float innerFade = smoothstep(0.0, 0.35, v);
                float outerEdgeDropoff = 1.0 - smoothstep(0.94, 1.0, v);
                float radialMask = innerFade * outerEdgeDropoff;

                // Combined envelope with Particle Vertex Alpha
                float envelope = saturate(arcTipFade * radialMask * input.color.a * _Opacity);
                if (envelope <= 0.001)
                {
                    discard;
                }

                // 2. Unpack Normal Map for Screen-Space Air Refraction
                half4 packedNormal = SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uvBump);
                half3 normalTS = UnpackNormal(packedNormal);

                // Fade distortion smoothly by envelope so the refraction boundary never shows hard seams
                float2 distortionOffset = normalTS.xy * _DistortionStrength * envelope;

                // 3. Sample _CameraOpaqueTexture with subtle prismatic chromatic split
                float2 screenUV = input.screenPos.xy / max(0.0001, input.screenPos.w);
                float2 uvR = saturate(screenUV + distortionOffset * (1.0 + _ChromaticAberration * 8.0));
                float2 uvG = saturate(screenUV + distortionOffset);
                float2 uvB = saturate(screenUV + distortionOffset * (1.0 - _ChromaticAberration * 8.0));

                half sceneR = SampleSceneColor(uvR).r;
                half sceneG = SampleSceneColor(uvG).g;
                half sceneB = SampleSceneColor(uvB).b;
                half3 refractedScene = half3(sceneR, sceneG, sceneB);

                // 4. Sample Sheer Wind Streaks Mask
                half4 streakSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uvMain);
                half streakMask = pow(saturate(streakSample.r * streakSample.a), _WindStreakContrast);

                // 5. Compute Razor-Sharp Outer Qi Rim near the outer blade edge (v -> 0.85..0.96)
                float rimBand = saturate((v - 0.15) / 0.80);
                half outerRim = pow(rimBand, _OuterRimPower) * outerEdgeDropoff * _OuterRimIntensity;

                // 6. Combine Refracted Background + Sheer Wind Streaks + HDR Outer Rim
                half3 sheerWindRGB = _TintColor.rgb * input.color.rgb * streakMask;
                half3 rimRGB = _RimColor.rgb * input.color.rgb * outerRim;

                // Where wind streaks or rim are bright, add their HDR luminance on top of the refracted air
                half windVisibility = saturate(streakMask * _TintColor.a + outerRim * 0.65);
                half3 finalRGB = lerp(refractedScene, refractedScene + sheerWindRGB, saturate(_TintColor.a * 1.25)) + rimRGB;

                // Final Alpha: visible wherever air is distorted OR wind streaks/rim shine
                half distortionPresence = saturate(length(normalTS.xy) * (_DistortionStrength * 14.0));
                half finalAlpha = saturate(max(distortionPresence, windVisibility) * envelope);

                return half4(finalRGB, finalAlpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
