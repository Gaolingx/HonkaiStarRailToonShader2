// Validation-only receiver: outputs the main-light shadow attenuation, so the
// batch verification can prove the transparent ShadowCaster pass reaches URP's
// shadow map without relying on the Lit shader's lighting.
Shader "SRCharacter/ShadowTest"
{
    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Opaque"
            "Queue" = "Geometry"
        }

        Pass
        {
            Name "ShadowTest"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex ShadowTestVertex
            #pragma fragment ShadowTestFragment
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings ShadowTestVertex(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionWS = positionWS;
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 ShadowTestFragment(Varyings input) : SV_Target
            {
                Light mainLight = GetMainLight(TransformWorldToShadowCoord(input.positionWS));
                half attenuation = mainLight.shadowAttenuation;
                return half4(attenuation, attenuation, attenuation, 1.0);
            }
            ENDHLSL
        }
    }
}
