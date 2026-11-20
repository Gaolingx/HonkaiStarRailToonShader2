#ifndef SR_CHARACTER_COMMON_INCLUDED
#define SR_CHARACTER_COMMON_INCLUDED

#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/ShaderVariablesFunctions.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/SpaceTransforms.hlsl"
#include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Packing.hlsl"
#include "SRCharacterDefines.hlsl"

TEXTURE2D(_BaseMap);        SAMPLER(sampler_BaseMap);
TEXTURE2D(_NormalMap);      SAMPLER(sampler_NormalMap);
TEXTURE2D(_ILMMap);         SAMPLER(sampler_ILMMap);
TEXTURE2D(_EmissionMap);    SAMPLER(sampler_EmissionMap);
TEXTURE2D(_FaceSDFMap);     SAMPLER(sampler_FaceSDFMap);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    float4 _BaseColor;
    float4 _BackColor;          // back-face albedo tint (capture PS 8537 cb0[89])
    float4 _EmissionColor;
    float4 _OutlineColor;
    float4 _RimColor;
    float4 _SRHeadRight;
    float4 _SRHeadForward;
    float4 _SRHeadUp;

    float  _NormalScale;
    float  _AlphaClip;
    float  _Cull;
    float  _BackFaceUV2;        // 1 = back faces sample the second UV set (capture cb0[226].y)
    float  _OutlineWidth;
    float  _OutlineZOffset;
    float  _SROutlineWidthScale;
    float  _OutlineNormalTangent;

    float  _SRStencilRef;
    float  _SRArea;
    float  _SRShadingGroup;
    float  _SRMaterialID;

    float  _SRShadowSoftness;
    float  _SRShadowOffset;
    float  _SRSdfSoftness;
    float  _SRUseUv2Face;

    float  _SRSpecIntensity;
    float  _SRSpecShininess;
    float  _SRSpecThreshold;
    float  _SRSpecRoughness;

    float  _SRRimIntensity;
    float  _SRRimWidth;
    float  _SREmissionIntensity;
    float  _SRIndirectIntensity;
    float  _SRVertexColorWeight;

    // Semi-transparent hair: output alpha = base alpha * _SRAlpha * vertex alpha.
    float  _SRAlpha;

    // Semi-transparent five-layer constants (capture P35 eids 5349/5361/5404/5585/5595,
    // README 2.6). Defaults are the decoded values; per-layer stencil refs match the capture.
    float4 _SRPaletteLow;        // cb2[38] palette stop (low)
    float4 _SRPaletteMid;        // cb2[39] palette stop (mid)
    float4 _SRPaletteHigh;       // cb2[40] palette stop (high; unobservable in the capture)
    float4 _SRGradientDark;      // cb0[285] gradient dark colour (t0.x == 1)
    float4 _SRAboveLineColor;    // cb0[288] "above line" gradient colour
    float4 _SRFlatTint;          // cb0[287] flat layer colour
    float4 _SRMainRimColor;      // cb0[131] rim colour (HDR)
    float4 _SRMainAbsorbTint;    // cb2[33] absorption tint + weight (a)
    float4 _SRMainAbsorbLit;     // cb0[289] absorption tint, lit
    float4 _SRMainAbsorbSpec;    // cb0[290] absorption tint, specular
    float4 _SRRimLayerTint;      // cb0[291] fresnel layer colour
    float4 _SRLayerOutlineColor; // PS 15524 cb0[290] outline colour

    float  _SRPaletteBase;       // cb0[87].y palette ramp base height (world Y)
    float  _SRPaletteRange;      // cb2[37].w palette ramp range
    float  _SRPaletteOffset;     // cb2[37].z palette stop offset
    float  _SRHeightLine;        // cb0[293].x + cb0[293].w fade line (world Y)
    float  _SRHeightFade;        // cb0[297].w flag1 bias (scaled by 0.01)
    float  _SRFlatBrightness;    // cb0[297].z flat layer brightness add
    float  _SRFlatAlpha;         // cb0[299].z below-line alpha term (shared by flat/main)
    float  _SRMainRimExponent;   // cb0[300].x
    float  _SRMainFresnelExponent; // cb0[300].y
    float  _SRMainAlphaCut;      // 1 - cb0[298].z
    float  _SRMainSpecExponent;  // cb0[96].y
    float  _SRMainSpecCutoff;    // cb0[97].z
    float  _SRMainSpecIntensity; // cb0[96].w
    float  _SRRimLayerExponent;  // cb0[300].y of PS 15503 (4)
    float  _SRRimLayerAlpha;     // cb0[299].y
    float  _SRLayerOutlineAlpha; // sat(cb0[299].y)
    float  _SRDepthShellExtrude; // shell extrusion scale (capture 0)
    float  _SROutlineColorVisible; // 0 = capture (colour writes masked), 1 = visible band
    float  _SRStencilRefFlat;
    float  _SRStencilRefMain;
    float  _SRStencilRefDepth;
    float  _SRStencilRefRim;
    float  _SRStencilRefOutline;

    // Eye overlay (stencil bit0): only meshes flagged as eye proxies run the two eye passes.
    // The shading terms are the decoded PS 9584 constants (see README 2.5).
    float  _SREyeProxy;
    float4 _SREyeColor;
    float  _SREyeSpecIntensity;     // cb3[10].x (capture 1.0)
    float  _SREyeShadowStrength;
    float  _SREyeIrisMask;          // G-buffer normal.a in the capture (0.624 at the sampled pixel)
    float  _SREyeIrisBase;          // base refraction tint (capture 0.04)
    float  _SREyeSpecRadius;        // cb3[11].x (capture 1.0); also scales the refraction radius
    float  _SREyeFadeDistance;      // cb3[9].w (capture 5.0)
    float  _SREyeLightIntensity;    // cb3[9].rgb was (2.0, 1.505, 1.008)
CBUFFER_END

struct SRCharacterAttributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
    float2 uv2        : TEXCOORD1;
    float4 color      : COLOR;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct SRCharacterVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float3 normalWS   : TEXCOORD1;
    float4 tangentWS  : TEXCOORD2;
    float2 uv         : TEXCOORD3;
    float2 uv2        : TEXCOORD4;
    float4 color      : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float2 SRTransformBaseMapUV(float2 uv)
{
    return uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
}

// World-space normal with optional tangent-space normal map.
float3 SRGetNormalWS(float3 normalWS, float4 tangentWS, float2 uv)
{
#if defined(_NORMALMAP)
    float3 nTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, uv), _NormalScale);
    float3 bitangentWS = cross(normalize(normalWS), tangentWS.xyz) * tangentWS.w;
    return normalize(TransformTangentToWorld(nTS, half3x3(tangentWS.xyz, bitangentWS, normalWS)));
#else
    return normalize(normalWS);
#endif
}

// Dual-face rendering (capture P08 Cull None draws, e.g. eid 977 / PS 8537; the same
// select appears in PS 7229). On back faces the game samples the second UV set for the
// base and ILM maps, multiplies by a second colour constant and negates the normal before
// lighting. Here the G-buffer normal is the lighting normal, so the flip lands in the
// G-buffer write. Front faces and Cull Back materials fall through unchanged.
// Same layout as StarRailNPRShader's SetupDualFaceRendering + _BackColor.
float3 SRDualFaceNormal(float3 normalWS, FRONT_FACE_TYPE isFrontFace)
{
    return normalWS * IS_FRONT_VFACE(isFrontFace, 1.0, -1.0);
}

float2 SRDualFaceUV(float2 uv0, float2 uv1, FRONT_FACE_TYPE isFrontFace)
{
    return IS_FRONT_VFACE(isFrontFace, uv0, _BackFaceUV2 > 0.5 ? uv1 : uv0);
}

float3 SRDualFaceBaseColor(FRONT_FACE_TYPE isFrontFace)
{
    return IS_FRONT_VFACE(isFrontFace, _BaseColor.rgb, _BackColor.rgb);
}

float SRDualFaceBaseAlpha(FRONT_FACE_TYPE isFrontFace)
{
    return IS_FRONT_VFACE(isFrontFace, _BaseColor.a, _BackColor.a);
}

// Face SDF shadow (adapted from HonkaiStarRailToonShader, MIT).
// Returns 1 = lit, 0 = shadowed, in head space, before the main-light shadow attenuation.
float SRFaceSdfLit(float2 faceUV, float3 headRightWS, float3 headForwardWS, float3 headUpWS)
{
    Light mainLight = GetMainLight();
    float3 lightDirWS = normalize(mainLight.direction);

    // Project the light direction onto the head's horizontal plane.
    float3 lightDirProj = normalize(lightDirWS - dot(lightDirWS, headUpWS) * headUpWS);

    // The SDF map encodes the shadow boundary of the right cheek, so mirror U when lit from the right.
    bool isRight = dot(lightDirProj, headRightWS) > 0;
    float sdfUVx = lerp(faceUV.x, 1.0 - faceUV.x, isRight);
    float sdfValue = SAMPLE_TEXTURE2D(_FaceSDFMap, sampler_FaceSDFMap, float2(sdfUVx, faceUV.y)).a;
    sdfValue += _SRShadowOffset;

    float forwardDot = dot(headForwardWS, lightDirProj) * 0.5 + 0.5;
    float sdfShadow = smoothstep(forwardDot - _SRSdfSoftness, forwardDot + _SRSdfSoftness, 1.0 - sdfValue);
    return 1.0 - sdfShadow;
}

#endif // SR_CHARACTER_COMMON_INCLUDED