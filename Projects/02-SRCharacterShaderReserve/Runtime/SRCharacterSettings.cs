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