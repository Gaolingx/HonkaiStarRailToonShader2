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
    float4 _EmissionColor;
    float4 _OutlineColor;
    float4 _RimColor;
    float4 _SRHeadRight;
    float4 _SRHeadForward;
    float4 _SRHeadUp;

    float  _NormalScale;
    float  _AlphaClip;
    float  _Cull;
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
    float  _SRPadding0;
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