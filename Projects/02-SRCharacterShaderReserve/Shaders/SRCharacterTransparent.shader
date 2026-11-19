// SRCharacter/Transparent
// Semi-transparent hair / bangs (capture P35, eids 5349/5361/5404/5585/5595).
// The capture never writes these meshes into the G-buffer: they are alpha-blended straight
// into the lit HDR target, with a stencil-based first-layer guard and a back-face outline:
//   1. fill:    Stencil Comp != Ref, Pass Replace  (NOT_EQUAL dedup of overlapping shells)
//   2. outline: Cull Front, ZWrite On, back-face stencil Replace
// The material is drawn by SRCharacterTransparentPass, not by URP's forward pass.
Shader "SRCharacter/Transparent"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Base Map", 2D) = "white" {}
        [Toggle(_NORMALMAP)] [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _ILMMap ("ILM Map (R=spec mask, G=shadow shape, B=gloss, A=material id)", 2D) = "white" {}
        _EmissionMap ("Emission Map", 2D) = "black" {}

        [Header(Surface)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _NormalScale ("Normal Scale", Range(0, 2)) = 1
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _SREmissionIntensity ("Emission Intensity", Range(0, 8)) = 0
        _SRVertexColorWeight ("Vertex Color Tint", Range(0, 1)) = 0
        _SRAlpha ("Alpha", Range(0, 1)) = 0.5
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle(_ALPHATEST_ON)] _AlphaClipEnabled ("Alpha Clip", Float) = 0
        _AlphaClip ("Alpha Clip Threshold", Range(0, 1)) = 0.05

        [Header(Stencil)]
        [HideInInspector] _SRStencilRef ("Stencil Ref (group << 6 | material id)", Int) = 24
        _SRArea ("Area (1 body, 2 face, 3 hair)", Int) = 3
        _SRShadingGroup ("Shading Group (0..3)", Int) = 0
        _SRMaterialID ("Material ID (0..63)", Int) = 24

        [Header(Toon Shadow)]
        _SRShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.1

        [Header(Rim and Outline)]
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _SRRimIntensity ("Rim Intensity", Range(0, 4)) = 0.3
        _SRRimWidth ("Rim Width", Range(0.1, 16)) = 4
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 2)) = 0.25
        _SROutlineWidthScale ("Outline Width Scale", Range(0, 4)) = 1
        _OutlineZOffset ("Outline Z Offset", Range(-1, 1)) = 0
        [Toggle(_OUTLINE_NORMAL_TANGENT)] _OutlineNormalTangent ("Outline uses tangent as normal", Float) = 0

        [Header(Indirect)]
        _SRIndirectIntensity ("Indirect Intensity", Range(0, 4)) = 1
    }

    CustomEditor "SRCharacter.SRCharacterShaderGUI"

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        // ------------------------------------------------------------------
        // Alpha-blended fill with a stencil first-layer guard.
        // Capture DSS 8553 / 15533: depth GREATER_EQUAL, ZWrite Off, stencil Replace.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterTransparent"
            Tags { "LightMode" = "SRCharacterTransparent" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_SRStencilRef]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex TransparentVertex
            #pragma fragment TransparentFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _NORMALMAP
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SR_SHADE_HAIR
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "SRCharacterCommon.hlsl"

            TEXTURE2D(_SRRampCool);         SAMPLER(sampler_SRRampCool);
            TEXTURE2D(_SRRampWarm);         SAMPLER(sampler_SRRampWarm);
            TEXTURE2D(_SRHairRampCool);     SAMPLER(sampler_SRHairRampCool);
            TEXTURE2D(_SRHairRampWarm);     SAMPLER(sampler_SRHairRampWarm);

            #define SR_RAMP_ROWS 16.0

            SRCharacterVaryings TransparentVertex(SRCharacterAttributes input)
            {
                SRCharacterVaryings output = (SRCharacterVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.tangentWS = float4(TransformObjectToWorldDir(input.tangentOS.xyz), input.tangentOS.w * GetOddNegativeScale());
                output.uv = SRTransformBaseMapUV(input.uv);
                output.uv2 = input.uv2;
                output.color = input.color;
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
                return lerp(cool, warm, saturate(1.0 - rampU));
            }

            half4 TransparentFragment(SRCharacterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = baseMap.a * _BaseColor.a * _SRAlpha * lerp(1.0h, input.color.a, _SRVertexColorWeight);

                #if defined(_ALPHATEST_ON)
                    clip(alpha - _AlphaClip);
                #endif

                half4 ilm = SAMPLE_TEXTURE2D(_ILMMap, sampler_ILMMap, input.uv);
                half3 albedo = baseMap.rgb * _BaseColor.rgb * lerp(1.0h, input.color.rgb, _SRVertexColorWeight);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uv).rgb * _EmissionColor.rgb * _SREmissionIntensity;

                float3 normalWS = SRGetNormalWS(input.normalWS, input.tangentWS, input.uv);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float remappedNoL = saturate(dot(normalWS, mainLight.direction) * 0.5 + 0.5);
                float lit = smoothstep(1.0 - ilm.g - _SRShadowSoftness, 1.0 - ilm.g + _SRShadowSoftness, remappedNoL);
                lit *= mainLight.shadowAttenuation;

                half3 diffuse = albedo * lerp(SampleRamp((uint)_SRMaterialID, remappedNoL), 1.0, lit);
                half3 indirect = SampleSH(normalWS) * albedo * _SRIndirectIntensity;

                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), max(_SRRimWidth, 0.001));
                half3 rimColor = _RimColor.rgb * (_SRRimIntensity * rim) * mainLight.color;

                return half4(diffuse + indirect + rimColor + emission, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Front hair shadow input (reference-based, not in the capture):
        // the bangs never enter the G-buffer, so they are rasterized here into
        // the hair depth target (linear01 eye depth, MIN blend) for the
        // fullscreen face shading. Alpha is clipped so soft strand tips fade out.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterHairDepth"
            Tags { "LightMode" = "SRCharacterHairDepth" }

            Cull [_Cull]
            ZWrite Off
            ZTest Always
            ColorMask R
            Blend One One
            BlendOp Min

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex HairDepthVertex
            #pragma fragment HairDepthFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _NORMALMAP
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON

            #include "SRCharacterCommon.hlsl"

            struct HairDepthVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            HairDepthVaryings HairDepthVertex(SRCharacterAttributes input)
            {
                HairDepthVaryings output = (HairDepthVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = SRTransformBaseMapUV(input.uv);
                return output;
            }

            half4 HairDepthFragment(HairDepthVaryings input) : SV_Target
            {
                clip(_SRArea - 2.5);

                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half alpha = baseMap.a * _BaseColor.a * _SRAlpha;
                clip(alpha - max(_AlphaClip, 0.01));

                float linear01 = saturate(-TransformWorldToView(input.positionWS).z / max(_ProjectionParams.z, 0.001));
                return half4(linear01, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Back-face outline of the transparent piece (capture eids 5404/5595, DSS 1935/15538).
        // Cull Front leaves only back faces, so a single stencil state reproduces the
        // captured back-face pass: Replace the material ref and write depth.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterTransparentOutline"
            Tags { "LightMode" = "SRCharacterTransparentOutline" }

            Cull Front
            ZWrite On
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_SRStencilRef]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OutlineVertex
            #pragma fragment OutlineFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_vertex _OUTLINE_NORMAL_TANGENT
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON

            #include "SRCharacterCommon.hlsl"

            struct OutlineVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            OutlineVaryings OutlineVertex(SRCharacterAttributes input)
            {
                OutlineVaryings output = (OutlineVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                #if defined(_OUTLINE_NORMAL_TANGENT)
                    float3 normalOS = input.tangentOS.xyz;
                #else
                    float3 normalOS = input.normalOS;
                #endif

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(normalOS);
                float3 normalVS = TransformWorldToViewDir(normalWS, true);
                float3 positionVS = TransformWorldToView(positionWS);
                float distanceToCamera = -positionVS.z;

                float fovFix = IsPerspectiveProjection()
                    ? 2.414 / unity_CameraProjection._m11
                    : 1.5996 / unity_CameraProjection._m11;
                float vertexWidth = max(input.color.a, 0.05);
                float width = _OutlineWidth * _SROutlineWidthScale * vertexWidth
                    * clamp(fovFix * 0.025, 0.04, 0.1) * distanceToCamera * 0.0588;

                normalVS.z = -0.1;
                positionVS += normalize(normalVS) * width;
                positionVS.z += _OutlineZOffset * distanceToCamera;

                output.positionCS = TransformWViewToHClip(positionVS);
                output.uv = SRTransformBaseMapUV(input.uv);
                return output;
            }

            half4 OutlineFragment(OutlineVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input); 
                float alpha = 1.0;
                #if defined(_ALPHATEST_ON)
                    alpha = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a * _SRAlpha;
                    clip(alpha - _AlphaClip);
                #endif
                return half4(_OutlineColor.rgb, _OutlineColor.a * alpha);
            }
            ENDHLSL
        }
    }
}