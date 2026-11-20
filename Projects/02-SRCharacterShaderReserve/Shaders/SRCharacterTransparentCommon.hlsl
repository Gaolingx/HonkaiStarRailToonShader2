#ifndef SR_CHARACTER_TRANSPARENT_COMMON_INCLUDED
#define SR_CHARACTER_TRANSPARENT_COMMON_INCLUDED

#include "SRCharacterCommon.hlsl"

// PS 15531 instructions 0..20: world-height palette shared by all four shading variants
// (flat 5349 / main 5361 / fresnel 5585 / outline 5595).
float3 SRTransparentPalette(float worldY)
{
    float u = saturate((worldY - _SRPaletteBase) / max(_SRPaletteRange, 0.001));
    float s1 = u * u * (3.0 - 2.0 * u);
    float v = saturate(2.0 * ((worldY - _SRPaletteBase) - _SRPaletteOffset));
    float s2 = v * v * (3.0 - 2.0 * v);
    float mid = saturate(s1 - s2);
    float3 p = s2 * _SRPaletteLow.rgb * _SRPaletteLow.a
             + mid * _SRPaletteMid.rgb * _SRPaletteMid.a
             + (1.0 - s1) * _SRPaletteHigh.rgb * _SRPaletteHigh.a;
    return 2.0 * saturate(p);
}

// v2.x in the capture (per-vertex alpha channel of the shell mesh).
float SRTransparentVertexAlpha(float4 vertexColor)
{
    return lerp(1.0, vertexColor.a, _SRVertexColorWeight);
}

SRCharacterVaryings SRTransparentVertex(SRCharacterAttributes input)
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

struct SRTransparentShellVaryings
{
    float4 positionCS : SV_POSITION;
    float3 positionWS : TEXCOORD0;
    float4 color      : TEXCOORD1;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

// Back-face shell used by the depth-only pass (capture 5404 / VS 15518). The capture's
// extrusion (cb0[298].w + cb0[299].x) * 0.01 evaluates to 0 in this frame, so the shell
// is written where the back side already is.
SRTransparentShellVaryings SRTransparentDepthShellVertex(SRCharacterAttributes input)
{
    SRTransparentShellVaryings output = (SRTransparentShellVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
    float3 normalVS = TransformWorldToViewDir(normalWS, true);
    float3 positionVS = TransformWorldToView(positionWS);

    positionVS += normalize(normalVS) * (_SRDepthShellExtrude * -positionVS.z);

    output.positionCS = TransformWViewToHClip(positionVS);
    output.positionWS = positionWS;
    output.color = input.color;
    return output;
}

// Extruded back-face shell used by the outline colour layer (capture 5595 / VS 15522),
// same view-space expansion as the Character outline.
SRTransparentShellVaryings SRTransparentOutlineVertex(SRCharacterAttributes input)
{
    SRTransparentShellVaryings output = (SRTransparentShellVaryings)0;
    UNITY_SETUP_INSTANCE_ID(input);
    UNITY_TRANSFER_INSTANCE_ID(input, output);

    float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
    float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
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
    output.positionWS = positionWS;
    output.color = input.color;
    return output;
}

#endif // SR_CHARACTER_TRANSPARENT_COMMON_INCLUDED