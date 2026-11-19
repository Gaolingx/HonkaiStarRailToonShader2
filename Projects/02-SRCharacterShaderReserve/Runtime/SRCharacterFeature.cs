using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Rebuilds the HSR character chain from capture frame 13063 with explicit pass order:
    ///   1. G-buffer (3 MRT + camera depth/stencil, stencil = material ref)
    ///   2. Outline (front-face cull, back-face stencil replace, same MRT)
    ///   3. Fullscreen toon shading (one draw per stencil group, stencil Equal + ReadMask 0xC0)
    ///   4. Additive forward overlay (specular / rim / emission)
    /// The passes must run after opaques so the camera depth already holds the scene and the
    /// main light shadow maps are resolved.
    /// </summary>
    public sealed class SRCharacterFeature : ScriptableRendererFeature
    {
        public SRCharacterSettings settings = new SRCharacterSettings();

        private SRCharacterGBufferPass m_GBufferPass;
        private SRCharacterOutlinePass m_OutlinePass;
        private SRCharacterShadingPass m_ShadingPass;
        private SRCharacterOverlayPass m_OverlayPass;
        private bool m_WarnedAboutMsaa;

        public override void Create()
        {
            settings.ResolveShaders();

            m_GBufferPass = new SRCharacterGBufferPass(settings.characterLayers)
            {
                renderPassEvent = settings.passEvent
            };
            m_OutlinePass = new SRCharacterOutlinePass(settings.characterLayers, m_GBufferPass)
            {
                renderPassEvent = settings.passEvent
            };
            m_ShadingPass = new SRCharacterShadingPass(settings, m_GBufferPass)
            {
                renderPassEvent = settings.passEvent
            };
            m_OverlayPass = new SRCharacterOverlayPass(settings.characterLayers)
            {
                renderPassEvent = settings.passEvent
            };
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            CameraType cameraType = renderingData.cameraData.cameraType;
            if (cameraType == CameraType.Preview || cameraType == CameraType.Reflection)
                return;

            settings.ResolveShaders();
            if (settings.characterShader == null || settings.shadingShader == null || m_GBufferPass == null)
                return;

            // Custom MRTs cannot be mixed with an MSAA depth attachment on D3D11.
            if (renderingData.cameraData.cameraTargetDescriptor.msaaSamples > 1)
            {
                if (!m_WarnedAboutMsaa)
                {
                    Debug.LogWarning("SRCharacterFeature requires MSAA = 1 (the G-buffer MRTs are single-sampled). The feature is skipped for this camera.");
                    m_WarnedAboutMsaa = true;
                }
                return;
            }

            m_GBufferPass.renderPassEvent = settings.passEvent;
            m_OutlinePass.renderPassEvent = settings.passEvent;
            m_ShadingPass.renderPassEvent = settings.passEvent;
            m_OverlayPass.renderPassEvent = settings.passEvent;

            renderer.EnqueuePass(m_GBufferPass);

            if (settings.enableOutline)
                renderer.EnqueuePass(m_OutlinePass);

            renderer.EnqueuePass(m_ShadingPass);

            if (settings.enableOverlay)
                renderer.EnqueuePass(m_OverlayPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_GBufferPass?.Dispose();
            m_ShadingPass?.Dispose();

            m_GBufferPass = null;
            m_OutlinePass = null;
            m_ShadingPass = null;
            m_OverlayPass = null;
        }
    }
}