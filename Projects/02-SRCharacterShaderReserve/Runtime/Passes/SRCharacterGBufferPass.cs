using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Character G-buffer stage (capture P08): 3 MRT + the camera depth/stencil.
    /// The stencil reference is authored per material and written with Replace,
    /// mirroring the capture's DSS 1288 / 8527 / 5978.
    /// </summary>
    internal sealed class SRCharacterGBufferPass : ScriptableRenderPass
    {
        private static readonly ShaderTagId s_Tag = new ShaderTagId("SRCharacterGBuffer");

        private readonly LayerMask m_LayerMask;
        private RTHandle m_Albedo;
        private RTHandle m_Normal;
        private RTHandle m_Mask;

        public RTHandle Albedo => m_Albedo;
        public RTHandle Normal => m_Normal;
        public RTHandle Mask => m_Mask;

        public SRCharacterGBufferPass(LayerMask layerMask)
        {
            m_LayerMask = layerMask;
            profilingSampler = new ProfilingSampler("SRCharacter GBuffer");
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            desc.msaaSamples = 1;
            desc.useMipMap = false;
            desc.autoGenerateMips = false;
            desc.bindMS = false;

            desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref m_Albedo, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SRCharacterAlbedo");

            desc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref m_Normal, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SRCharacterNormal");

            desc.graphicsFormat = GraphicsFormat.R8G8B8A8_UNorm;
            RenderingUtils.ReAllocateIfNeeded(ref m_Mask, desc, FilterMode.Point, TextureWrapMode.Clamp, name: "_SRCharacterMask");

            RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            ConfigureTarget(new[] { m_Albedo, m_Normal, m_Mask }, cameraDepth);
            // Color only: the camera depth/stencil already contains the opaque scene and must survive.
            ConfigureClear(ClearFlag.Color, Color.clear);
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

        public void Dispose()
        {
            m_Albedo?.Release();
            m_Normal?.Release();
            m_Mask?.Release();
            m_Albedo = null;
            m_Normal = null;
            m_Mask = null;
        }
    }
}