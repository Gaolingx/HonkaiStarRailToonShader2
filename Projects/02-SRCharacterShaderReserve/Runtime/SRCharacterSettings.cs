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

        public LayerMask characterLayers = ~0;
        public RenderPassEvent passEvent = RenderPassEvent.AfterRenderingOpaques;
        public bool enableOutline = true;
        public bool enableOverlay = true;

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
        }
    }
}