// SRCharacter/Transparent
// Semi-transparent ornament shell (capture P35, eids 5349/5361/5404/5585/5595).
// The capture draws the same 219-vertex mesh five times, in this order:
//   1. flat      5349  PS 15507: palette * (1 + cb0[297].z) * cb0[287], discards above the height line
//   2. main      5361  PS 15531: height palette + fresnel rim + stylised specular
//   3. depth     5404  PS 15520: colour writes off, writes the back-side shell depth
//   4. rim       5585  PS 15503: palette * cb0[291], alpha = (1 - N.V)^4
//   5. outline   5595  PS 15524: palette * cb0[290]; the captured pass masks its colour writes,
//                               leaving only the depth/stencil effect (band left by layer 4's ref)
// The decoded constants are Vector properties (not Color): Unity sRGB-decodes a Color
// property on upload, which would shift the captured linear values (see README 2.6).
// All colour layers are SrcAlpha/OneMinusSrcAlpha with ZWrite off; every layer carries its own
// stencil ref (capture 0 / 24 / 10 / 11 / 11) with NOT_EQUAL -> REPLACE, so overlapping shells
// dedupe per layer instead of blocking the stack.
// The material is drawn by SRCharacterTransparentPass, not by URP's forward pass.
Shader "SRCharacter/Transparent"
{
    Properties
    {
        [Header(Textures)]
        _BaseMap ("Base Map (hair depth prepass only)", 2D) = "white" {}
        [Toggle(_NORMALMAP)] [Normal] _NormalMap ("Normal Map", 2D) = "bump" {}

        [Header(Surface)]
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _NormalScale ("Normal Scale", Range(0, 2)) = 1
        _SRAlpha ("Global Alpha", Range(0, 1)) = 1
        _SRVertexColorWeight ("Vertex Color Weight", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
        [Toggle(_ALPHATEST_ON)] _AlphaClipEnabled ("Alpha Clip (hair depth pass)", Float) = 0
        _AlphaClip ("Alpha Clip Threshold", Range(0, 1)) = 0.05

        [Header(Stencil)]
        _SRArea ("Area (1 body, 2 face, 3 hair)", Int) = 3
        _SRStencilRefFlat ("Flat Ref (capture 0)", Int) = 0
        _SRStencilRefMain ("Main Ref (capture 24)", Int) = 24
        _SRStencilRefDepth ("Depth Shell Ref (capture 10)", Int) = 10
        _SRStencilRefRim ("Fresnel Ref (capture 11)", Int) = 11
        _SRStencilRefOutline ("Outline Ref (capture 11)", Int) = 11

        [Header(Height Palette (PS 15531))]
        _SRHeightLine ("Height Line, world Y (cb0[293])", Float) = -15.063829
        _SRHeightFade ("Height Fade Bias (cb0[297].w)", Float) = 0.06
        _SRPaletteBase ("Palette Base Y (cb0[87].y)", Float) = -15.925059
        _SRPaletteRange ("Palette Range (cb2[37].w)", Range(0.01, 8)) = 0.5
        _SRPaletteOffset ("Palette Stop Offset (cb2[37].z)", Float) = 0.65
        _SRPaletteLow ("Palette Low Stop (cb2[38])", Vector) = (0.933333, 0.886275, 0.862745, 0.5)
        _SRPaletteMid ("Palette Mid Stop (cb2[39])", Vector) = (0.866667, 0.796078, 0.776471, 0.5)
        _SRPaletteHigh ("Palette High Stop (cb2[40], unobservable)", Vector) = (0.866667, 0.796078, 0.776471, 0.5)
        _SRGradientDark ("Gradient Dark (cb0[285], t0.x = 1)", Vector) = (0, 0.165493, 0.5, 1)
        _SRAboveLineColor ("Above Line Colour (cb0[288])", Vector) = (1, 1, 1, 1)

        [Header(Flat Layer (eid 5349))]
        _SRFlatTint ("Flat Tint (cb0[287])", Vector) = (0.462077, 0.672443, 1, 1)
        _SRFlatBrightness ("Flat Brightness Add (cb0[297].z)", Float) = 0.59
        _SRFlatAlpha ("Below-Line Alpha (cb0[299].z)", Range(0, 1)) = 0.686

        [Header(Main Layer (eid 5361))]
        _SRMainRimColor ("Rim Colour (cb0[131], HDR)", Vector) = (2.244329, 2.244329, 2.244329, 1)
        _SRMainRimExponent ("Rim Exponent (cb0[300].x)", Float) = 4
        _SRMainFresnelExponent ("Fresnel Exponent (cb0[300].y)", Float) = 1.37
        _SRMainAlphaCut ("Alpha Cut (1 - cb0[298].z)", Range(0, 1)) = 0.065
        _SRMainSpecExponent ("Spec Exponent (cb0[96].y)", Float) = 21.8
        _SRMainSpecCutoff ("Spec Cutoff (cb0[97].z)", Float) = 0.502
        _SRMainSpecIntensity ("Spec Intensity (cb0[96].w)", Float) = 2.17
        _SRMainAbsorbTint ("Absorption Tint (cb2[33])", Vector) = (1, 0.91213, 0.867925, 0.5)
        _SRMainAbsorbLit ("Absorption Lit (cb0[289])", Vector) = (0.882075, 0.950156, 1, 1)
        _SRMainAbsorbSpec ("Absorption Spec (cb0[290])", Vector) = (1, 1, 1, 1)

        [Header(Depth Shell (eid 5404))]
        _SRDepthShellExtrude ("Depth Shell Extrude", Float) = 0

        [Header(Fresnel Layer (eid 5585))]
        _SRRimLayerTint ("Rim Layer Tint (cb0[291])", Vector) = (1.259696, 1.259696, 1.259696, 1)
        _SRRimLayerExponent ("Rim Layer Exponent", Float) = 4
        _SRRimLayerAlpha ("Rim Layer Alpha (cb0[299].y)", Range(0, 1)) = 1

        [Header(Outline Colour Layer (eid 5595))]
        _SRLayerOutlineColor ("Outline Layer Colour (cb0[290])", Vector) = (0.132076, 0.039249, 0.061282, 1)
        _SRLayerOutlineAlpha ("Outline Layer Alpha", Range(0, 1)) = 1
        _SROutlineColorVisible ("Outline Colour Visible (capture = 0)", Range(0, 1)) = 0
        _OutlineWidth ("Outline Width", Range(0, 2)) = 0.25
        _SROutlineWidthScale ("Outline Width Scale", Range(0, 4)) = 1
        _OutlineZOffset ("Outline Z Offset", Range(-1, 1)) = 0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
        }

        // ------------------------------------------------------------------
        // Layer 1 (eid 5349, PS 15507): flat bright colour, fragments above the
        // height line are discarded.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterTransparentFlat"
            Tags { "LightMode" = "SRCharacterTransparentFlat" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_SRStencilRefFlat]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SRTransparentVertex
            #pragma fragment TransparentFlatFragment
            #pragma multi_compile_instancing

            #include "SRCharacterTransparentCommon.hlsl"

            half4 TransparentFlatFragment(SRCharacterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                clip(_SRHeightLine - input.positionWS.y);

                float3 palette = SRTransparentPalette(input.positionWS.y);
                float3 rgb = palette * (1.0 + _SRFlatBrightness) * _SRFlatTint.rgb;
                float alpha = _SRFlatAlpha * SRTransparentVertexAlpha(input.color) * _SRAlpha;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Layer 2 (eid 5361, PS 15531): main shading. No main light, no toon ramp;
        // the colour is the world-height palette times a fresnel/absorption term
        // and a stylised Blinn highlight, and alpha comes from the height line
        // and the fresnel term (see README 2.6).
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterTransparentShade"
            Tags { "LightMode" = "SRCharacterTransparentShade" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_SRStencilRefMain]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SRTransparentVertex
            #pragma fragment TransparentShadeFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _NORMALMAP

            #include "SRCharacterTransparentCommon.hlsl"

            half4 TransparentShadeFragment(SRCharacterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float worldY = input.positionWS.y;
                float3 normalWS = SRGetNormalWS(input.normalWS, input.tangentWS, input.uv);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 palette = SRTransparentPalette(worldY);

                // v5.w (threshold) and v6.y (world Y) in the capture vertex stage.
                float threshold = _SRHeightLine;
                bool flag1 = (threshold - 0.01 * _SRHeightFade) < worldY;
                bool flag2 = threshold < worldY;

                float3 gradient = _SRGradientDark.rgb;   // t0.x samples 1 on every traced pixel
                if (flag1)
                    gradient = _SRAboveLineColor.rgb;

                float nv = saturate(dot(normalWS, viewDirWS));
                float logOneMinusNv = log2(max(1.0 - nv, 1e-8));
                float rim = min(exp2(logOneMinusNv * _SRMainRimExponent), 1.0);
                float fres = max(exp2(logOneMinusNv * _SRMainFresnelExponent), 0.001);
                // Quadratic rim blend, not a lerp: g' = (1 - rim) * g + rim^2 * rimColour.
                gradient = (1.0 - rim) * gradient + rim * rim * _SRMainRimColor.rgb;

                // The capture blends three stylised normal-space hair vectors into L;
                // the engine equivalent is the main light direction.
                Light mainLight = GetMainLight();
                float3 halfDir = normalize(mainLight.direction + viewDirWS);
                float nh = saturate(dot(normalWS, halfDir));
                float specPower = exp2(log2(max(nh, 1e-8)) * _SRMainSpecExponent);
                float specMask = saturate((specPower - _SRMainSpecCutoff) * 10.0);
                float spec = (specMask * specMask * (3.0 - 2.0 * specMask))
                    * _SRMainSpecIntensity * (flag2 ? 1.0 : 0.0);

                float3 absorb = 1.0 + _SRMainAbsorbTint.a * (_SRMainAbsorbTint.rgb - 1.0);
                float3 tintLit = lerp(_SRMainAbsorbLit.rgb, _SRMainAbsorbSpec.rgb, spec);
                float3 tint = (absorb * tintLit - 0.1) * (spec + fres) + 0.1;
                float blendWeight = saturate(spec + (1.0 - _SRFlatAlpha) + (flag2 ? 0.0 : 1.0) + fres);
                float3 rgb = palette * lerp(gradient, tint, blendWeight);

                float alpha = saturate(max(fres - _SRMainAlphaCut, 0.0) + spec
                    + (flag2 ? 0.0 : _SRFlatAlpha)) * SRTransparentVertexAlpha(input.color) * _SRAlpha;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Layer 3 (eid 5404, PS 15520): depth-only. Writes the back-side shell
        // depth so layer 5 only survives near the silhouette.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterTransparentDepthShell"
            Tags { "LightMode" = "SRCharacterTransparentDepthShell" }

            Cull Front
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Blend Off

            Stencil
            {
                Ref [_SRStencilRefDepth]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SRTransparentDepthShellVertex
            #pragma fragment TransparentDepthShellFragment
            #pragma multi_compile_instancing

            #include "SRCharacterTransparentCommon.hlsl"

            half4 TransparentDepthShellFragment(SRTransparentShellVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Layer 4 (eid 5585, PS 15503): fresnel shell, visible only at grazing angles.
        // ------------------------------------------------------------------
        Pass
        {
            Name "SRCharacterTransparentRim"
            Tags { "LightMode" = "SRCharacterTransparentRim" }

            Cull [_Cull]
            ZWrite Off
            ZTest LEqual
            Blend SrcAlpha OneMinusSrcAlpha

            Stencil
            {
                Ref [_SRStencilRefRim]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SRTransparentVertex
            #pragma fragment TransparentRimFragment
            #pragma multi_compile_instancing
            #pragma shader_feature_local_fragment _ _NORMALMAP

            #include "SRCharacterTransparentCommon.hlsl"

            half4 TransparentRimFragment(SRCharacterVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 normalWS = SRGetNormalWS(input.normalWS, input.tangentWS, input.uv);
                float3 viewDirWS = normalize(_WorldSpaceCameraPos - input.positionWS);
                float3 palette = SRTransparentPalette(input.positionWS.y);

                float nv = saturate(dot(normalWS, viewDirWS));
                float rim = max(pow(1.0 - nv, _SRRimLayerExponent), 0.001);

                float3 rgb = palette * _SRRimLayerTint.rgb;
                float alpha = rim * _SRRimLayerAlpha * SRTransparentVertexAlpha(input.color) * _SRAlpha;
                return half4(rgb, alpha);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Layer 5 (eid 5595, PS 15524): outline colour shell. The capture masks its
        // colour writes; with the layer-4 stencil ref already written, only the
        // silhouette band is left for this pass to touch.
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
                Ref [_SRStencilRefOutline]
                ReadMask 255
                WriteMask 255
                Comp NotEqual
                Pass Replace
                Fail Keep
                ZFail Keep
            }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex SRTransparentOutlineVertex
            #pragma fragment TransparentOutlineFragment
            #pragma multi_compile_instancing

            #include "SRCharacterTransparentCommon.hlsl"

            half4 TransparentOutlineFragment(SRTransparentShellVaryings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);

                float3 palette = SRTransparentPalette(input.positionWS.y);
                float3 rgb = palette * _SRLayerOutlineColor.rgb;
                float alpha = _SRLayerOutlineAlpha * SRTransparentVertexAlpha(input.color) * _SRAlpha;
                // Multiplying colour and alpha by 0 is the capture's colour-write mask:
                // SrcAlpha = 0 leaves the destination untouched while depth/stencil still apply.
                return half4(rgb * _SROutlineColorVisible, alpha * _SROutlineColorVisible);
            }
            ENDHLSL
        }

        // ------------------------------------------------------------------
        // Front hair shadow input (reference-based, not in the capture):
        // the shell never enters the G-buffer, so it is rasterized here into the
        // hair depth target (linear01 eye depth, MIN blend) for the fullscreen
        // face shading. Alpha is clipped so soft strand tips fade out.
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
        // Shadow caster (capture P16 eids 3836/3848): the same 1056-index shell
        // is rasterized into the character's 512x512 per-object shadow tile.
        // 3836 reuses PS 15503 (no discard) and 3848 reuses PS 15507 (clips
        // above the height line); the merged single material keeps the
        // no-discard union and leaves the height clip to the colour layers.
        // Neither captured PS samples a texture, so the caster writes depth
        // without alpha clip (also keeps the pass free of UnityPerMaterial reads,
        // which SRP Batcher does not tolerate consistently when the shadow pass
        // and the colour passes share one material).
        // URP drives this pass from its main-light shadow caster list, so the
        // transparent queue casts shadows like SRCharacter/Character does.
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
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW

            #include "SRCharacterCommon.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
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
                return output;
            }

            half4 ShadowCasterFragment(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                return 0;
            }
            ENDHLSL
        }
    }
}
