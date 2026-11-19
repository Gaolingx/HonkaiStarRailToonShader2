using UnityEngine;

namespace SRCharacter
{
    public static class SRCharacterShaderIDs
    {
        // G-buffer targets (set as globals for the shading/overlay passes).
        public static readonly int CharacterAlbedo = Shader.PropertyToID("_SRCharacterAlbedo");
        public static readonly int CharacterNormal = Shader.PropertyToID("_SRCharacterNormal");
        public static readonly int CharacterMask = Shader.PropertyToID("_SRCharacterMask");

        // Ramp tables (global textures, 16 rows keyed by material id).
        public static readonly int RampCool = Shader.PropertyToID("_SRRampCool");
        public static readonly int RampWarm = Shader.PropertyToID("_SRRampWarm");
        public static readonly int HairRampCool = Shader.PropertyToID("_SRHairRampCool");
        public static readonly int HairRampWarm = Shader.PropertyToID("_SRHairRampWarm");

        // Shading material.
        public static readonly int ShadeRef = Shader.PropertyToID("_SRShadeRef");
        public static readonly int ShadeWriteMask = Shader.PropertyToID("_SRShadeWriteMask");
        public static readonly int ShadePassOp = Shader.PropertyToID("_SRShadePassOp");
        public static readonly int ToonShadowSoftness = Shader.PropertyToID("_SRToonShadowSoftness");
        public static readonly int ShadowAttenuationStrength = Shader.PropertyToID("_SRShadowAttenuationStrength");
        public static readonly int IndirectIntensity = Shader.PropertyToID("_SRIndirectIntensity");

        // Character material.
        public static readonly int StencilRef = Shader.PropertyToID("_SRStencilRef");
        public static readonly int Area = Shader.PropertyToID("_SRArea");
        public static readonly int ShadingGroup = Shader.PropertyToID("_SRShadingGroup");
        public static readonly int MaterialID = Shader.PropertyToID("_SRMaterialID");
        public static readonly int HeadRight = Shader.PropertyToID("_SRHeadRight");
        public static readonly int HeadForward = Shader.PropertyToID("_SRHeadForward");
        public static readonly int HeadUp = Shader.PropertyToID("_SRHeadUp");
        public static readonly int EyeProxy = Shader.PropertyToID("_SREyeProxy");

        // Transparent hair material.
        public static readonly int Alpha = Shader.PropertyToID("_SRAlpha");

        public const string AreaFaceKeyword = "_SR_AREA_FACE";
        public const string HairShadeKeyword = "_SR_SHADE_HAIR";
        public const string TransparentShaderName = "SRCharacter/Transparent";
    }
}