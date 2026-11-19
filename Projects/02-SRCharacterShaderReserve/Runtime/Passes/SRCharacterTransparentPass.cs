using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Semi-transparent hair / bangs (capture P35, eids 5349/5361/5404/5585/5595).
    /// These meshes never enter the G-buffer: they are alpha-blended into the lit target after
    /// the fullscreen shading and the eye overlay, with a stencil first-layer guard and a
    /// back-face outline. Both passes come from the same renderer (two ShaderTagIds).
    /// </summary>
    internal sealed class SRCharacterTransparentPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_FillTag = new ShaderTagId("SRCharacterTransparent");
        private static readonly ShaderTagId s_OutlineTag = new ShaderTagId("SRCharacterTransparentOutline");

        private readonly LayerMask m_LayerMask;
        private readonly SRCharacterSettings m_Settings;

        public SRCharacterTransparentPass(LayerMask layerMask, SRCharacterSettings settings)
        {
            m_LayerMask = layerMask;
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("SRCharacter Transparent");
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
                cmd.SetGlobalTexture(SRCharacterShaderIDs.RampCool, GetRamp(m_Settings.rampCool));
                cmd.SetGlobalTexture(SRCharacterShaderIDs.RampWarm, GetRamp(m_Settings.rampWarm));
                cmd.SetGlobalTexture(SRCharacterShaderIDs.HairRampCool, GetRamp(m_Settings.hairRampCool));
                cmd.SetGlobalTexture(SRCharacterShaderIDs.HairRampWarm, GetRamp(m_Settings.hairRampWarm));
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                SortingSettings sorting = new SortingSettings(renderingData.cameraData.camera)
                {
                    criteria = SortingCriteria.CommonTransparent
                };
                DrawingSettings drawing = new DrawingSettings(s_FillTag, sorting)
                {
                    perObjectData = PerObjectData.None,
                    enableDynamicBatching = false,
                    enableInstancing = true
                };
                drawing.SetShaderPassName(1, s_OutlineTag);

                FilteringSettings filtering = new FilteringSettings(RenderQueueRange.transparent, m_LayerMask);

                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static Texture GetRamp(Texture2D ramp)
        {
            return ramp != null ? ramp : (Texture)Texture2D.whiteTexture;
        }
    }
}