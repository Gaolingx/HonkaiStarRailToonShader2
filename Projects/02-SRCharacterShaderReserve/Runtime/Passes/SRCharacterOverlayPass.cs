using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Additive forward overlay (capture P35, eids 5083/5096): specular + rim + emission.
    /// Drawn after the fullscreen shading pass with Blend One One and no depth write.
    /// </summary>
    internal sealed class SRCharacterOverlayPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_Tag = new ShaderTagId("SRCharacterOverlay");

        private readonly LayerMask m_LayerMask;

        public SRCharacterOverlayPass(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
            profilingSampler = new ProfilingSampler("SRCharacter Overlay");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            ConfigureTarget(cameraColor, cameraDepth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            // Additive: order does not matter, keep the same sorting as the opaque stage.
            SortingSettings sorting = new SortingSettings(renderingData.cameraData.camera)
            {
                criteria = SortingCriteria.CommonOpaque
            };
            DrawingSettings drawing = new DrawingSettings(s_Tag, sorting)
            {
                perObjectData = PerObjectData.None,
                enableDynamicBatching = false,
                enableInstancing = true
            };
            FilteringSettings filtering = new FilteringSettings(RenderQueueRange.opaque, m_LayerMask);

            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
        }
    }
}