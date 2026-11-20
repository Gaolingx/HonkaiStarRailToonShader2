using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Semi-transparent ornament shell (capture P35, eids 5349/5361/5404/5585/5595).
    /// The capture draws the same mesh five times as global layers, not as a per-renderer
    /// pass chain, so each layer gets its own ShaderTagId and its own DrawRenderers call
    /// in capture order: flat -> main shade -> depth shell -> fresnel -> outline.
    /// The layer toggles exist for the ablation suite in SRCharacterVerify.
    /// </summary>
    internal sealed class SRCharacterTransparentPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_FlatTag = new ShaderTagId("SRCharacterTransparentFlat");
        private static readonly ShaderTagId s_ShadeTag = new ShaderTagId("SRCharacterTransparentShade");
        private static readonly ShaderTagId s_DepthShellTag = new ShaderTagId("SRCharacterTransparentDepthShell");
        private static readonly ShaderTagId s_RimTag = new ShaderTagId("SRCharacterTransparentRim");
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

                FilteringSettings filtering = new FilteringSettings(RenderQueueRange.transparent, m_LayerMask);

                DrawLayer(context, ref renderingData, filtering, s_FlatTag, m_Settings.enableTransparentFlat);
                DrawLayer(context, ref renderingData, filtering, s_ShadeTag, m_Settings.enableTransparentShade);
                DrawLayer(context, ref renderingData, filtering, s_DepthShellTag, m_Settings.enableTransparentDepthShell);
                DrawLayer(context, ref renderingData, filtering, s_RimTag, m_Settings.enableTransparentRim);
                DrawLayer(context, ref renderingData, filtering, s_OutlineTag, m_Settings.enableTransparentOutline);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static void DrawLayer(ScriptableRenderContext context, ref RenderingData renderingData,
            FilteringSettings filtering, ShaderTagId tag, bool enabled)
        {
            if (!enabled)
                return;

            SortingSettings sorting = new SortingSettings(renderingData.cameraData.camera)
            {
                criteria = SortingCriteria.CommonTransparent
            };
            DrawingSettings drawing = new DrawingSettings(tag, sorting)
            {
                perObjectData = PerObjectData.None,
                enableDynamicBatching = false,
                enableInstancing = true
            };

            context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
        }

        private static Texture GetRamp(Texture2D ramp)
        {
            return ramp != null ? ramp : (Texture)Texture2D.whiteTexture;
        }
    }
}