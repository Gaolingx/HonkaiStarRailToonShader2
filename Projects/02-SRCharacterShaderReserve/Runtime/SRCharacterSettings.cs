using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    [System.Serializable]
    public sealed class SRCharacterSettings
    {
        public Shader characterShader;
        public Shader shadingShader;
        public Shader transparentShader;

        public LayerMask characterLayers = ~0;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;
        public bool enableOutline = true;
        public bool enableOverlay = true;

        [Tooltip("Eye mark + eye shade passes (stencil bit 0). Requires eye proxies on the character layer with _SREyeProxy = 1.")]
        public bool enableEye = true;

        [Tooltip("Semi-transparent hair / bangs submitted from the transparent queue with a stencil first-layer guard.")]
        public bool enableTransparent = true;

        [Header("Transparent layers (ablation switches, capture eids 5349/5361/5404/5585/5595)")]
        public bool enableTransparentFlat = true;
        public bool enableTransparentShade = true;
        public bool enableTransparentDepthShell = true;
        public bool enableTransparentRim = true;
        public bool enableTransparentOutline = true;

        [Header("Front hair shadow (reference-based, not in the capture)")]
        [Tooltip("Render hair/bangs into a half-res depth texture and sample it in the face shading with a light-direction offset.")]
        public bool enableFrontHairShadow = true;
        [Tooltip("Screen-space offset distance. The capture-equivalent reference default is 0.2.")]
        [Range(0f, 8f)] public float hairShadowDistance = 0.2f;
        [Range(0f, 1f)] public float hairShadowStrength = 1f;
        [Tooltip("Depth bias in normalized [0,1] eye depth units; positive values suppress acne.")]
        [Range(-0.05f, 0.05f)] public float hairShadowDepthBias = 0f;
        [Tooltip("Hair depth texture downscale (2 = half resolution).")]
        [Range(1, 4)] public int hairDepthDownscale = 2;
        [Tooltip("Character model scale used by the distance compensation.")]
        [Min(0.01f)] public float modelScale = 1f;

        [Header("Ramps (16 rows, indexed by material id)")]
        public Texture2D rampCool;
        public Texture2D rampWarm;
        public Texture2D hairRampCool;
        public Texture2D hairRampWarm;

        [Header("Lighting")]
        [Range(0.001f, 0.5f)] public float shadowSoftness = 0.1f;
        [Range(0f, 1f)] public float shadowAttenuationStrength = 1f;
        [Range(0f, 4f)] public float indirectIntensity = 1f;

        public void ResolveShaders()
        {
            if (characterShader == null)
                characterShader = Shader.Find("SRCharacter/Character");
            if (shadingShader == null)
                shadingShader = Shader.Find("SRCharacter/Shading");
            if (transparentShader == null)
                transparentShader = Shader.Find(SRCharacterShaderIDs.TransparentShaderName);
        }
    }
}