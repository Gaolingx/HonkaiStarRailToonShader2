using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Eye occlusion marking (capture eids 4968/4999, DSS 1303): redraw the eye proxy with
    /// ZFail = Replace on stencil bit 0, so any proxy pixel that fails the depth test (bangs or
    /// eyelid in front) is flagged. No colour is written.
    /// </summary>
    internal sealed class SRCharacterEyeMarkPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_Tag = new ShaderTagId("SRCharacterEyeMark");

        private readonly LayerMask m_LayerMask;

        public SRCharacterEyeMarkPass(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
            profilingSampler = new ProfilingSampler("SRCharacter Eye Mark");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            ConfigureTarget(cameraColor, cameraDepth);
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

    /// <summary>
    /// Eye shading overlay (capture eids 4989/5015, DSS 1305): back faces of the eye proxy add a
    /// screen-space highlight where (group | bit0) matches with bit0 clear, then clear the flag.
    /// </summary>
    internal sealed class SRCharacterEyeShadePass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_Tag = new ShaderTagId("SRCharacterEyeShade");

        private readonly LayerMask m_LayerMask;
        private readonly SRCharacterGBufferPass m_Targets;

        public SRCharacterEyeShadePass(LayerMask layerMask, SRCharacterGBufferPass targets)
        {
            m_LayerMask = layerMask;
            m_Targets = targets;
            profilingSampler = new ProfilingSampler("SRCharacter Eye Shade");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            ConfigureTarget(cameraColor, cameraDepth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalTexture(SRCharacterShaderIDs.CharacterAlbedo, m_Targets.Albedo);
                cmd.SetGlobalTexture(SRCharacterShaderIDs.CharacterNormal, m_Targets.Normal);
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

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

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }
    }
}