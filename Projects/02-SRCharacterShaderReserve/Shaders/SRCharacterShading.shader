// SRCharacter/Shading
// Fullscreen toon lighting, the P35 equivalent of the capture:
//   one fullscreen triangle per stencil group, gated by
//   Stencil { Ref group << 6, ReadMask 0xC0, Comp Equal }.
// The GBuffer alpha carries (area << 6 | material id); the mask G channel carries
// the per-texel shadow shape taken from the ILM map, A carries the face SDF lit term.
Shader "SRCharacter/Shading"
{
    Properties
    {
        _SRShadeRef ("Stencil Ref (group << 6)", Int) = 0
        _SRToonShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.1
        _SRShadowAttenuationStrength ("Main Light Shadow Strength", Range(0, 1)) = 1
        _SRIndirectIntensity ("Indirect Intensity", Range(0, 4)) = 1
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "SRCharacterShading"
            Tags { "LightMode" = "SRCharacterShading" }

            ZTest Always
            ZWrite Off
            Cull Off
            Blend One Zero
            ColorMask RGB

            // ReadMask 0xC0 reproduces the capture: only the two group bits select the material class.
            Stencil
            {
                Ref [_SRShadeRef]
                ReadMask 192
                WriteMask 0
                Comp Equal
                Pass Keep
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex FullscreenVertex
            #pragma fragment ShadingFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma shader_feature_local_fragment _SR_SHADE_HAIR

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "SRCharacterDefines.hlsl"

            TEXTURE2D(_SRCharacterAlbedo);
            TEXTURE2D(_SRCharacterNormal);
            TEXTURE2D(_SRCharacterMask);

            TEXTURE2D(_SRRampCool);         SAMPLER(sampler_SRRampCool);
            TEXTURE2D(_SRRampWarm);         SAMPLER(sampler_SRRampWarm);
            TEXTURE2D(_SRHairRampCool);     SAMPLER(sampler_SRHairRampCool);
            TEXTURE2D(_SRHairRampWarm);     SAMPLER(sampler_SRHairRampWarm);

            CBUFFER_START(UnityPerMaterial)
                float _SRShadeRef;
                float _SRToonShadowSoftness;
                float _SRShadowAttenuationStrength;
                float _SRIndirectIntensity;
            CBUFFER_END

            #define SR_RAMP_ROWS 16.0

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 ndc        : TEXCOORD0;
            };

            Varyings FullscreenVertex(uint vertexID : SV_VertexID)
            {
                Varyings output;
                float4 positionCS = GetFullScreenTriangleVertexPosition(vertexID);
                output.positionCS = positionCS;
                output.ndc = positionCS.xy;
                return output;
            }

            half3 SampleRamp(uint materialId, float rampU)
            {
                float rampV = (materialId + 0.5) / SR_RAMP_ROWS;
                float2 rampUV = float2(saturate(rampU), rampV);
                #if defined(_SR_SHADE_HAIR)
                    float3 cool = SAMPLE_TEXTURE2D(_SRHairRampCool, sampler_SRHairRampCool, rampUV).rgb;
                    float3 warm = SAMPLE_TEXTURE2D(_SRHairRampWarm, sampler_SRHairRampWarm, rampUV).rgb;
                #else
                    float3 cool = SAMPLE_TEXTURE2D(_SRRampCool, sampler_SRRampCool, rampUV).rgb;
                    float3 warm = SAMPLE_TEXTURE2D(_SRRampWarm, sampler_SRRampWarm, rampUV).rgb;
                #endif
                // Cool/warm split keeps the shadow tint from turning flat gray.
                return lerp(cool, warm, saturate(1.0 - rampU));
            }

            half4 ShadingFragment(Varyings input) : SV_Target
            {
                uint2 pixel = (uint2)input.positionCS.xy;
                float4 albedo = LOAD_TEXTURE2D(_SRCharacterAlbedo, pixel);
                float4 normalDepth = LOAD_TEXTURE2D(_SRCharacterNormal, pixel);
                float4 charMask = LOAD_TEXTURE2D(_SRCharacterMask, pixel);

                uint area;
                uint materialId;
                SRDecodeMaterialPayload(albedo.a, area, materialId);
                if (area == 0u)
                    discard;

                float deviceDepth = normalDepth.a;
                float4 positionWS4 = mul(unity_MatrixInvVP, float4(input.ndc, deviceDepth, 1.0));
                float3 positionWS = positionWS4.xyz / positionWS4.w;

                float3 normalWS = normalize(normalDepth.rgb);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float nDotL = dot(normalWS, mainLight.direction);
                float remappedNoL = saturate(nDotL * 0.5 + 0.5);

                float shadowShape = charMask.g;
                float lit = smoothstep(
                    1.0 - shadowShape - _SRToonShadowSoftness,
                    1.0 - shadowShape + _SRToonShadowSoftness,
                    remappedNoL);

                // Face SDF was resolved per texel in the GBuffer pass (it needs UVs).
                if (area == (uint)SR_AREA_FACE)
                    lit = charMask.a;

                lit *= lerp(1.0, mainLight.shadowAttenuation, _SRShadowAttenuationStrength);

                half3 rampTint = SampleRamp(materialId, remappedNoL);
                half3 albedoLinear = albedo.rgb;
                half3 diffuse = albedoLinear * lerp(rampTint, 1.0, lit);

                half3 indirect = SampleSH(normalWS) * albedoLinear * _SRIndirectIntensity;

                return half4(diffuse + indirect, 1.0);
            }
            ENDHLSL
        }
    }
}