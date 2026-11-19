using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Front hair shadow prepass. This mechanism is NOT present in capture frame 13063
    /// (the face there is group 0 albedo and the captured tex9580 only feeds the hair group);
    /// it follows StarRailNPRShader's HairDepthOnly pass + GetHairShadow sampling instead.
    ///
    /// Hair and semi-transparent bangs are rasterized into a half-res R32F target that stores
    /// linear eye depth normalized by the far plane. MIN blending keeps the nearest strand, so
    /// no depth attachment is needed. The fullscreen shading pass samples it with a screen-space
    /// offset along the light direction and darkens face pixels whose strand is in front.
    /// </summary>
    internal sealed class SRCharacterHairDepthPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_Tag = new ShaderTagId("SRCharacterHairDepth");

        private readonly LayerMask m_LayerMask;
        private readonly SRCharacterSettings m_Settings;
        private RTHandle m_HairDepth;

        public SRCharacterHairDepthPass(LayerMask layerMask, SRCharacterSettings settings)
        {
            m_LayerMask = layerMask;
            m_Settings = settings;
            profilingSampler = new ProfilingSampler("SRCharacter HairDepth");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            int downscale = Mathf.Clamp(m_Settings.hairDepthDownscale, 1, 4);
            desc.width = Mathf.Max(1, desc.width / downscale);
            desc.height = Mathf.Max(1, desc.height / downscale);
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.bindMS = false;
            desc.graphicsFormat = GraphicsFormat.R32_SFloat;

            RenderingUtils.ReAllocateIfNeeded(
                ref m_HairDepth, desc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: "_SRHairDepthTexture");

            ConfigureTarget(m_HairDepth);
            // 1 = far / no hair. Fragments blend with MIN, so the nearest strand wins.
            ConfigureClear(ClearFlag.Color, Color.white);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalTexture(SRCharacterShaderIDs.HairDepth, m_HairDepth);

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
                FilteringSettings filtering = new FilteringSettings(RenderQueueRange.all, m_LayerMask);

                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();

                context.DrawRenderers(renderingData.cullResults, ref drawing, ref filtering);
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public void Dispose()
        {
            m_HairDepth?.Release();
            m_HairDepth = null;
        }
    }
}