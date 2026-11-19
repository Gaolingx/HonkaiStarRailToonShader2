using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Outline stage (capture eids 3194/3366): front-face culled inflated shell drawn into the
    /// same G-buffer, back-face stencil replace. Shares the G-buffer targets, never clears them.
    /// </summary>
    internal sealed class SRCharacterOutlinePass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_Tag = new ShaderTagId("SRCharacterOutline");

        private readonly LayerMask m_LayerMask;
        private readonly SRCharacterGBufferPass m_Targets;

        public SRCharacterOutlinePass(LayerMask layerMask, SRCharacterGBufferPass targets)
        {
            m_LayerMask = layerMask;
            m_Targets = targets;
            profilingSampler = new ProfilingSampler("SRCharacter Outline");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            ConfigureTarget(new[] { m_Targets.Albedo, m_Targets.Normal, m_Targets.Mask }, cameraDepth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
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