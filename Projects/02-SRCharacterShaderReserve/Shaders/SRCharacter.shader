// SRCharacter/Character
// Geometry stage of the HSR-style character chain reconstructed from frame 13063:
//   Pass "SRCharacterGBuffer" -> albedo/normal/mask MRT + depth, stencil = material ref
//   Pass "SRCharacterOutline" -> front-face culled inflated shell, back-face stencil replace
//   Pass "SRCharacterOverlay" -> additive forward layer (specular / rim / emission)
// ShadowCaster / DepthOnly / DepthNormals keep the character compatible with URP.
Shader "SRCharacter/Character"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Base Map", 2D) = "white" {}
        [Toggle(_NORMALMAP)] [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}
        _ILMMap ("ILM Map (R=spec mask, G=shadow shape, B=gloss, A=material id)", 2D) = "white" {}
        _EmissionMap ("Emission Map", 2D) = "black" {}
        _FaceSDFMap ("Face SDF Map (A = SDF)", 2D) = "white" {}

        [Header(Surface)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _BackColor ("Back Face Color", Color) = (1, 1, 1, 1)
        [ToggleUI] _BackFaceUV2 ("Back Face Uses UV2", Float) = 1
        _NormalScale ("Normal Scale", Range(0, 2)) = 1
        _EmissionColor ("Emission Color", Color) = (0, 0, 0, 1)
        _SREmissionIntensity ("Emission Intensity", Range(0, 8)) = 1
        _SRVertexColorWeight ("Vertex Color Tint", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle(_ALPHATEST_ON)] _AlphaClipEnabled ("Alpha Clip", Float) = 0
        _AlphaClip ("Alpha Clip Threshold", Range(0, 1)) = 0.5

        [Header(Stencil)]
        [HideInInspector] _SRStencilRef ("Stencil Ref (group << 6 | material id)", Int) = 16
        _SRArea ("Area (1 body, 2 face, 3 hair)", Int) = 1
        _SRShadingGroup ("Shading Group (0..3)", Int) = 0
        _SRMaterialID ("Material ID (0..63)", Int) = 16

        [Header(Toon Shadow)]
        _SRShadowSoftness ("Shadow Softness", Range(0.001, 0.5)) = 0.1
        _SRShadowOffset ("Face SDF Offset", Range(-0.5, 0.5)) = 0
        _SRSdfSoftness ("Face SDF Softness", Range(0.001, 0.5)) = 0.05
        _SRUseUv2Face ("Face SDF uses UV2", Float) = 1

        [Header(Specular)]
        _SRSpecIntensity ("Specular Intensity", Range(0, 4)) = 0.5
        _SRSpecShininess ("Specular Shininess", Range(1, 256)) = 48
        _SRSpecThreshold ("Specular Threshold", Range(0, 1)) = 0.5
        _SRSpecRoughness ("Specular Roughness", Range(0.001, 1)) = 0.1

        [Header(Rim and Outline)]
        _RimColor ("Rim Color", Color) = (1, 1, 1, 1)
        _SRRimIntensity ("Rim Intensity", Range(0, 4)) = 0
        _SRRimWidth ("Rim Width", Range(0.1, 16)) = 4
        _OutlineColor ("Outline Color", Color) = (0, 0, 0, 1)
        _OutlineWidth ("Outline Width", Range(0, 2)) = 0.25
        _SROutlineWidthScale ("Outline Width Scale", Range(0, 4)) = 1
        _OutlineZOffset ("Outline Z Offset", Range(-1, 1)) = 0
        [Toggle(_OUTLINE_NORMAL_TANGENT)] _OutlineNormalTangent ("Outline uses tangent as normal", Float) = 0

        [Header(Indirect)]
        _SRIndirectIntensity ("Indirect Intensity", Range(0, 4)) = 1

        [Header(Head Axes)]
        _SRHeadRight ("Head Right (WS)", Vector) = (1, 0, 0, 0)
        _SRHeadForward ("Head Forward (WS)", Vector) = (0, 0, 1, 0)
        _SRHeadUp ("Head Up (WS)", Vector) = (0, 1, 0, 0)

        [Header(Eye Overlay and Stencil Bit 0)]
        _SREyeProxy ("Eye Proxy (0 or 1)", Float) = 0
        _SREyeColor ("Eye Highlight Color", Color) = (1, 1, 1, 1)
        _SREyeSpecIntensity ("Eye Spec Intensity (cb3[10].x)", Range(0, 4)) = 1
        _SREyeShadowStrength ("Eye Main-light Shadow Strength", Range(0, 1)) = 1
        _SREyeIrisMask ("Eye Iris Mask (capture normal.a)", Range(0, 1)) = 0.624
        _SREyeIrisBase ("Eye Iris Base Tint", Range(0, 1)) = 0.04
        _SREyeSpecRadius ("Eye Spec Radius (cb3[11].x)", Range(0, 4)) = 1
        _SREyeFadeDistance ("Eye Distance Fade (cb3[9].w)", Range(0.1, 100)) = 5
        _SREyeLightIntensity ("Eye Light Intensity (cb3[9].rgb = 2.0/1.5/1.0)", Range(0, 8)) = 1
    }

    CustomEditor "SRCharacter.SRCharacterShaderGUI"

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        // ------------------------------------------------------------------
        // Deferred character G-buffer: 3 MRT + camera depth/stencil.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterGBuffer"
            Tags { "LightMode" = "SRCharacterGBuffer" }

            Stencil
            {
                Ref [_SRStencilRef]
                ReadMask 255
                WriteMask 255
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            Cull [_Cull]
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex GBufferVertex
            #pragma fragment GBufferFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _NORMALMAP
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON
            #pragma shader_feature_local_fragment _SR_AREA_FACE

            #include "SRCharacterCommon.hlsl"

            struct GBufferOutput
            {
                float4 albedo : SV_Target0;   // rgb = linear albedo, a = area|material id
                float4 normal : SV_Target1;   // rgb = world normal, a = device depth
                float4 mask   : SV_Target2;   // r = silhouette, g = shadow shape, b = 0, a = face lit
            };

            SRCharacterVaryings GBufferVertex(SRCharacterAttributes input)
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

            GBufferOutput GBufferFragment(
                SRCharacterVaryings input,
                FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 texUV = SRDualFaceUV(input.uv, input.uv2, isFrontFace);
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, texUV);
                half3 albedo = baseMap.rgb * SRDualFaceBaseColor(isFrontFace)
                    * lerp(1.0h, input.color.rgb, _SRVertexColorWeight);

                #if defined(_ALPHATEST_ON)
                    clip(baseMap.a * SRDualFaceBaseAlpha(isFrontFace) - _AlphaClip);
                #endif

                half4 ilm = SAMPLE_TEXTURE2D(_ILMMap, sampler_ILMMap, texUV);
                float3 normalWS = SRGetNormalWS(SRDualFaceNormal(input.normalWS, isFrontFace), input.tangentWS, texUV);

                float faceLit = 1.0;
                #if defined(_SR_AREA_FACE)
                    float2 faceUV = _SRUseUv2Face > 0.5 ? input.uv2 : input.uv;
                    faceLit = SRFaceSdfLit(faceUV, normalize(_SRHeadRight.xyz), normalize(_SRHeadForward.xyz), normalize(_SRHeadUp.xyz));
                #endif

                GBufferOutput output;
                output.albedo = float4(albedo, SREncodeMaterialPayload(_SRArea, _SRMaterialID));
                output.normal = float4(normalWS, input.positionCS.z);
                output.mask = float4(1.0, ilm.g, 0.0, faceLit);
                return output;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Outline: inflated back-face shell drawn into the same G-buffer so the
        // fullscreen shading pass colors it like any other character pixel.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterOutline"
            Tags { "LightMode" = "SRCharacterOutline" }

            // The capture wrote the outline with back-face stencil replace (DSS 1935).
            Stencil
            {
                Ref [_SRStencilRef]
                ReadMask 255
                WriteMask 255
                Comp Always
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            Cull Front
            ZWrite On
            ZTest LEqual

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
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float4 color      : TEXCOORD2;
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
                output.normalWS = normalWS;
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            struct OutlineOutput
            {
                float4 albedo : SV_Target0;
                float4 normal : SV_Target1;
                float4 mask   : SV_Target2;
            };

            OutlineOutput OutlineFragment(OutlineVaryings input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                #if defined(_ALPHATEST_ON)
                    half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, SRTransformBaseMapUV(input.uv));
                    clip(baseMap.a * _BaseColor.a - _AlphaClip);
                #endif

                OutlineOutput output;
                output.albedo = float4(_OutlineColor.rgb, SREncodeMaterialPayload(_SRArea, _SRMaterialID));
                output.normal = float4(normalize(input.normalWS), input.positionCS.z);
                // shadow shape = 1 keeps the outline color from being re-tinted by the toon ramp.
                output.mask = float4(1.0, 1.0, 0.0, 1.0);
                return output;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Front hair shadow input (reference-based, not in the capture):
        // hair writes linear01 eye depth into a half-res R32F target with MIN
        // blending, so the nearest strand per pixel survives. The fullscreen
        // face shading samples it with a light-direction screen-space offset.
        // Non-hair materials run the vertex stage but are clipped in the fragment.
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
                // Body (1) and face (2) must not occlude hair in this prepass.
                clip(_SRArea - 2.5);

                #if defined(_ALPHATEST_ON)
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv).a * _BaseColor.a - _AlphaClip);
                #endif

                float linear01 = saturate(-TransformWorldToView(input.positionWS).z / max(_ProjectionParams.z, 0.001));
                return half4(linear01, 0.0, 0.0, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Additive forward layer drawn after the fullscreen shading pass:
        // specular + rim + emission (HSR eids 5083/5096, additive blend 208).
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterOverlay"
            Tags { "LightMode" = "SRCharacterOverlay" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend One One

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex OverlayVertex
            #pragma fragment OverlayFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _NORMALMAP
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "SRCharacterCommon.hlsl"

            SRCharacterVaryings OverlayVertex(SRCharacterAttributes input)
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

            half4 OverlayFragment(
                SRCharacterVaryings input,
                FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float2 texUV = SRDualFaceUV(input.uv, input.uv2, isFrontFace);
                half4 baseMap = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, texUV);
                #if defined(_ALPHATEST_ON)
                    clip(baseMap.a * SRDualFaceBaseAlpha(isFrontFace) - _AlphaClip);
                #endif

                half4 ilm = SAMPLE_TEXTURE2D(_ILMMap, sampler_ILMMap, texUV);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, texUV).rgb * _EmissionColor.rgb * _SREmissionIntensity;

                float3 normalWS = SRGetNormalWS(SRDualFaceNormal(input.normalWS, isFrontFace), input.tangentWS, texUV);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                float3 lightColor = mainLight.color * mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                float nDotL = saturate(dot(normalWS, mainLight.direction));
                float3 halfDirWS = normalize(mainLight.direction + viewDirWS);

                float blinnPhong = pow(saturate(dot(halfDirWS, normalWS)), max(_SRSpecShininess, 1.0));
                float threshold = 1.0 - _SRSpecThreshold;
                float stepPhong = smoothstep(threshold - _SRSpecRoughness, threshold + _SRSpecRoughness, blinnPhong);
                float3 specular = lightColor * stepPhong * (_SRSpecIntensity * ilm.r) * lerp(0.25, 1.0, nDotL);

                float rim = pow(1.0 - saturate(dot(normalWS, viewDirWS)), max(_SRRimWidth, 0.001));
                float3 rimColor = _RimColor.rgb * (_SRRimIntensity * rim) * lerp(0.3, 1.0, nDotL);

                return half4(specular + rimColor + emission, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Eye overlay, step 1 (capture eids 4968/4999, DSS 1303):
        // the eye proxy is redrawn with the same depth as the G-buffer; wherever it
        // fails the depth test (bangs/eyelid in front) stencil bit 0 is set.
        // Colour writes are disabled, only the transient flag is authored.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterEyeMark"
            Tags { "LightMode" = "SRCharacterEyeMark" }

            Cull Back
            ZWrite Off
            ZTest LEqual
            ColorMask 0

            // Capture: depth GREATER_EQUAL (LEqual under reversed Z), stencil Always,
            // ZFail = Replace with Ref 1 / WriteMask 1, Pass and Fail = Keep.
            Stencil
            {
                Ref 1
                ReadMask 255
                WriteMask 1
                Comp Always
                Pass Keep
                Fail Keep
                ZFail Replace
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex EyeProxyVertex
            #pragma fragment EyeMarkFragment
            #pragma multi_compile_instancing

            #include "SRCharacterCommon.hlsl"

            struct EyeVaryings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            EyeVaryings EyeProxyVertex(SRCharacterAttributes input)
            {
                EyeVaryings output = (EyeVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 EyeMarkFragment(EyeVaryings input) : SV_Target
            {
                // Every character renderer is submitted to this pass; only the proxy draws.
                clip(_SREyeProxy - 0.5);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Eye overlay, step 2 (capture eids 4989/5015, DSS 1305):
        // back faces of the eye proxy add a screen-space eye highlight, but only where
        // (group bits | bit0) matches and bit0 is clear, i.e. the eye is not covered by hair.
        // All stencil ops are Zero with WriteMask 1, so the flag is consumed here.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterEyeShade"
            Tags { "LightMode" = "SRCharacterEyeShade" }

            Cull Front
            ZWrite Off
            ZTest GEqual
            Blend One One
            ColorMask RGB

            // Capture: back faces Equal (ReadMask 0x81), Pass/Fail/ZFail = Zero, WriteMask 1.
            Stencil
            {
                Ref [_SRStencilRef]
                ReadMask 129
                WriteMask 1
                Comp Equal
                Pass Zero
                Fail Zero
                ZFail Zero
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex EyeShadeVertex
            #pragma fragment EyeShadeFragment
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH

            #include "SRCharacterCommon.hlsl"

            TEXTURE2D(_SRCharacterAlbedo);
            TEXTURE2D(_SRCharacterNormal);

            struct EyeShadeVaryings
            {
                float4 positionCS : SV_POSITION;
                float2 ndc        : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            EyeShadeVaryings EyeShadeVertex(SRCharacterAttributes input)
            {
                EyeShadeVaryings output = (EyeShadeVaryings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float4 positionCS = TransformWorldToHClip(positionWS);
                output.positionCS = positionCS;
                output.ndc = positionCS.xy / positionCS.w;
                return output;
            }

            // PS 9584 instructions 111..166: analytic refraction of the light through the eye
            // surface. Only the non total-internal-reflection path needs it; the TIR path uses
            // (1, |N.L|). Inputs are D (reflectance), nl = N.L, cnt = sqrt(1 - k^2), vl = V.L,
            // k = refraction radius. Returns (refracted cos factor, transmission cos factor).
            float2 SREyeRefract(float D, float nl, float cnt, float vl, float k)
            {
                float sn = 2.0 * D * nl - vl;
                float r7z = k / sqrt(max(1.0 - sn * sn, 1e-4));
                float r7w = nl - sn * D;
                float r8w = r7w * r7z;
                float r9w = 2.0 * nl;
                r9w = r9w * nl - 1.0;
                float cw = (-sn * vl + r9w) * r7z;
                r9w = 1.0 - D * D - nl * nl - vl * vl;
                float r7y = 2.0 * D * nl;
                r7y = sqrt(max(r7y * vl + r9w, 1e-4)) * r7z;
                r9w = nl * (2.0 * r7y);
                float r10x = D * cnt + nl;
                r7z = r7z * r7w + r10x;
                r7w = vl * cnt + cw + 1.0;
                float r10y = r7w * r7y;
                float r10z = r7w * r7z;
                float r10w = r7z * r9w;
                float r11x = r10z * (r10w * 0.25 - 0.5 * r10y);
                r10w *= -2.0 * r10y + r10w;
                r10y = r10y * r10y + r10w;
                r10w = r7w * r7w;
                r7w = (-0.5 * (vl * cnt + r7w) - 0.5) * r10z;
                r7w = r10x * r10w + r7w;
                r7z = r7z * r7w + r10y;
                r7w = 2.0 * r11x;
                r10x = max(r7z * r7z + r11x * r11x, 0.001);
                r7w /= r10x;
                r7z *= r7w;
                r7w = 1.0 - r7w * r11x;
                r7y = r7w * r8w + r7y * r7z;
                cw = r7w * cw + r9w * r7z;
                r7y = D * cnt + r7y;
                float r5x = vl * cnt + cw;
                float r5w = 1.0 / sqrt(max(2.0 * r5x + 2.0, 1e-4));
                return float2(saturate(r5w * (nl + r7y)), saturate(r5w * (r5x + 1.0)));
            }

            half4 EyeShadeFragment(EyeShadeVaryings input) : SV_Target
            {
                clip(_SREyeProxy - 0.5);

                uint2 pixel = (uint2)input.positionCS.xy;
                float4 albedo = LOAD_TEXTURE2D(_SRCharacterAlbedo, pixel);
                float4 normalDepth = LOAD_TEXTURE2D(_SRCharacterNormal, pixel);

                uint area;
                uint materialId;
                SRDecodeMaterialPayload(albedo.a, area, materialId);
                if (area != (uint)SR_AREA_FACE)
                    discard;

                // Same reconstruction as the fullscreen shading pass: the G-buffer alpha holds
                // the device depth, so the screen-space normal and position come from one pass.
                float4 positionWS4 = mul(unity_MatrixInvVP, float4(input.ndc, normalDepth.a, 1.0));
                float3 positionWS = positionWS4.xyz / positionWS4.w;
                float3 normalWS = normalize(normalDepth.rgb);

                float3 toCamera = _WorldSpaceCameraPos - positionWS;
                float dist = max(length(toCamera), 1e-4);
                float3 viewDirWS = toCamera / dist;

                Light mainLight = GetMainLight(TransformWorldToShadowCoord(positionWS));
                float3 lightDirWS = mainLight.direction;

                float nl = saturate(dot(normalWS, lightDirWS));
                float nvRaw = dot(normalWS, viewDirWS);
                float nv = saturate(nvRaw);
                float vl = dot(viewDirWS, lightDirWS);

                // PS 9584 cb3[9].w = 5 and cb3[11].z = 2: (1 - (d / fadeDistance)^2)^2.
                float fade01 = saturate(1.0 - (dist * dist) / max(_SREyeFadeDistance * _SREyeFadeDistance, 1e-4));
                float fade = fade01 * fade01;

                // PS 9584 cb3[11].x = 1: reflectance from the eye radius / distance.
                float specT = sqrt(saturate(_SREyeSpecRadius * _SREyeSpecRadius / (dist * dist + 1.0)));
                float specX = max(nvRaw, -specT) + specT;
                float specular = saturate(specX * specX / max(4.0 * specT, 1e-6));

                // cb3[16] = (0, 1): the G-buffer normal.a is the eye mask in the capture. The
                // half-vector cosine comes from V.L because our key light is directional.
                float irisMask = saturate(_SREyeIrisMask);
                float halfCos = sqrt(saturate((1.0 + vl) * 0.5));
                float fresnel5 = pow(1.0 - halfCos, 5.0);
                float3 tint = _SREyeIrisBase + (1.0 - _SREyeIrisBase) * fresnel5;

                float radius = saturate(irisMask * _SREyeSpecRadius / dist);
                float strength = saturate((1.0 - irisMask) * (1.0 - irisMask) + 1.0 / (3.6 * nl + 0.4));

                float cnt = sqrt(saturate(1.0 - radius * radius));
                float sn = 2.0 * specular * nl - vl;
                float refracted = 1.0;
                float transCos = abs(nl);
                if (sn < cnt)
                {
                    float2 r = SREyeRefract(specular, nl, cnt, vl, radius);
                    refracted = r.x;
                    transCos = r.y;
                }

                float s2 = strength * strength;
                float q = sqrt(saturate(s2 + nl * nl * (1.0 - s2)));
                float p = sqrt(saturate(specular * specular * (1.0 - s2) + s2));
                float a2 = refracted * refracted * (s2 - 1.0) + 1.0;
                float refraction = saturate((s2 * 0.15915494) / max((specular * q + nl * p) * a2 * a2, 1e-5));
                float3 refractionColor = saturate(refraction * tint);

                float transmission = strength /
                    (((3.0 * sqrt(strength) + radius) * radius * 0.25) / (transCos + 0.001) + strength);

                float3 lightColor = mainLight.color * mainLight.distanceAttenuation
                    * lerp(1.0, mainLight.shadowAttenuation, _SREyeShadowStrength)
                    * (fade * _SREyeLightIntensity);

                half3 color = _SREyeColor.rgb
                    * (specular * albedo.rgb * lightColor * _SREyeSpecIntensity
                     + refractionColor * nv * lightColor * transmission);
                return half4(color, 1.0);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // URP compatibility passes.
        // ------------------------------------------------------------------
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }

            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowCasterVertex
            #pragma fragment ShadowCasterFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "SRCharacterCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings ShadowCasterVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);

                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                    float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                    float3 lightDirectionWS = _LightDirection;
                #endif

                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                    positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                    positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif

                output.positionCS = positionCS;
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.uv2 = input.uv2;
                return output;
            }

            half4 ShadowCasterFragment(
                Varyings input,
                FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    float2 texUV = SRDualFaceUV(input.uv, input.uv2, isFrontFace);
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, texUV).a * SRDualFaceBaseAlpha(isFrontFace) - _AlphaClip);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }

            ZWrite On
            ColorMask 0
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthOnlyVertex
            #pragma fragment DepthOnlyFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON

            #include "SRCharacterCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthOnlyVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.uv2 = input.uv2;
                return output;
            }

            half4 DepthOnlyFragment(
                Varyings input,
                FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    float2 texUV = SRDualFaceUV(input.uv, input.uv2, isFrontFace);
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, texUV).a * SRDualFaceBaseAlpha(isFrontFace) - _AlphaClip);
                #endif
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }

            ZWrite On
            Cull [_Cull]

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex DepthNormalsVertex
            #pragma fragment DepthNormalsFragment
            #pragma multi_compile_instancing
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT
            #pragma shader_feature_local_fragment _ _ALPHATEST_ON

            #include "SRCharacterCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
                float2 uv2        : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float2 uv         : TEXCOORD1;
                float2 uv2        : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            Varyings DepthNormalsVertex(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                output.uv2 = input.uv2;
                return output;
            }

            half4 DepthNormalsFragment(
                Varyings input,
                FRONT_FACE_TYPE isFrontFace : FRONT_FACE_SEMANTIC) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                #if defined(_ALPHATEST_ON)
                    float2 texUV = SRDualFaceUV(input.uv, input.uv2, isFrontFace);
                    clip(SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, texUV).a * SRDualFaceBaseAlpha(isFrontFace) - _AlphaClip);
                #endif
                float3 normalWS = normalize(SRDualFaceNormal(input.normalWS, isFrontFace));
                #if defined(_GBUFFER_NORMALS_OCT)
                    float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                    float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                    return half4(PackFloat2To888(remappedOctNormalWS), 0.0);
                #else
                    return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}