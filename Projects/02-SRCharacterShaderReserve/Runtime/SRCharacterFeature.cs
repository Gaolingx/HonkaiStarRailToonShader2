using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Rebuilds the HSR character chain from capture frame 13063 with explicit pass order:
    ///   1. G-buffer (3 MRT + camera depth/stencil, stencil = material ref)
    ///   2. Outline (front-face cull, back-face stencil replace, same MRT)
    ///   2b. Hair depth prepass (reference-based front hair shadow input)
    ///   3. Fullscreen toon shading (one draw per stencil group, stencil Equal + ReadMask 0xC0)
    ///   4. Eye overlay (stencil bit 0 marks eye pixels covered by bangs, then gates a highlight)
    ///   5. Additive forward overlay (specular / rim / emission)
    ///   6. Semi-transparent hair / bangs (alpha blend + stencil first-layer guard + outline)
    /// The passes must run after opaques so the camera depth already holds the scene and the
    /// main light shadow maps are resolved.
    /// </summary>
    public sealed class SRCharacterFeature : ScriptableRendererFeature
    {
        public SRCharacterSettings settings = new SRCharacterSettings();

        private SRCharacterGBufferPass m_GBufferPass;
        private SRCharacterOutlinePass m_OutlinePass;
        private SRCharacterHairDepthPass m_HairDepthPass;
        private SRCharacterShadingPass m_ShadingPass;
        private SRCharacterEyeMarkPass m_EyeMarkPass;
        private SRCharacterEyeShadePass m_EyeShadePass;
        private SRCharacterOverlayPass m_OverlayPass;
        private SRCharacterTransparentPass m_TransparentPass;
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
            m_HairDepthPass = new SRCharacterHairDepthPass(settings.characterLayers, settings)
            {
                renderPassEvent = settings.passEvent
            };
            m_ShadingPass = new SRCharacterShadingPass(settings, m_GBufferPass)
            {
                renderPassEvent = settings.passEvent
            };
            m_EyeMarkPass = new SRCharacterEyeMarkPass(settings.characterLayers)
            {
                renderPassEvent = settings.passEvent
            };
            m_EyeShadePass = new SRCharacterEyeShadePass(settings.characterLayers, m_GBufferPass)
            {
                renderPassEvent = settings.passEvent
            };
            m_OverlayPass = new SRCharacterOverlayPass(settings.characterLayers)
            {
                renderPassEvent = settings.passEvent
            };
            m_TransparentPass = new SRCharacterTransparentPass(settings.characterLayers, settings)
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
            m_HairDepthPass.renderPassEvent = settings.passEvent;
            m_ShadingPass.renderPassEvent = settings.passEvent;
            m_EyeMarkPass.renderPassEvent = settings.passEvent;
            m_EyeShadePass.renderPassEvent = settings.passEvent;
            m_OverlayPass.renderPassEvent = settings.passEvent;
            m_TransparentPass.renderPassEvent = settings.passEvent;

            renderer.EnqueuePass(m_GBufferPass);

            if (settings.enableOutline)
                renderer.EnqueuePass(m_OutlinePass);

            if (settings.enableFrontHairShadow)
                renderer.EnqueuePass(m_HairDepthPass);
            else
                Shader.SetGlobalTexture(SRCharacterShaderIDs.HairDepth, Texture2D.whiteTexture);

            renderer.EnqueuePass(m_ShadingPass);

            if (settings.enableEye)
            {
                renderer.EnqueuePass(m_EyeMarkPass);
                renderer.EnqueuePass(m_EyeShadePass);
            }

            if (settings.enableOverlay)
                renderer.EnqueuePass(m_OverlayPass);

            if (settings.enableTransparent)
                renderer.EnqueuePass(m_TransparentPass);
        }

        protected override void Dispose(bool disposing)
        {
            m_GBufferPass?.Dispose();
            m_HairDepthPass?.Dispose();
            m_ShadingPass?.Dispose();

            m_GBufferPass = null;
            m_OutlinePass = null;
            m_HairDepthPass = null;
            m_ShadingPass = null;
            m_EyeMarkPass = null;
            m_EyeShadePass = null;
            m_OverlayPass = null;
            m_TransparentPass = null;
        }
    }
}