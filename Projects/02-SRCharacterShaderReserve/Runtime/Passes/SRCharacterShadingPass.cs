using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace SRCharacter
{
    /// <summary>
    /// Fullscreen toon shading (capture P35). One fullscreen triangle per stencil group;
    /// the pass material's stencil reference is group &lt;&lt; 6 and the stencil test uses
    /// ReadMask 0xC0 + Equal, exactly like the captured material passes.
    /// </summary>
    internal sealed class SRCharacterShadingPass : ScriptableRenderPass
    {
        private readonly SRCharacterSettings m_Settings;
        private readonly SRCharacterGBufferPass m_Targets;
        private readonly Material[] m_Materials = new Material[SRCharacterStencil.MaxGroups];

        public SRCharacterShadingPass(SRCharacterSettings settings, SRCharacterGBufferPass targets)
        {
            m_Settings = settings;
            m_Targets = targets;
            profilingSampler = new ProfilingSampler("SRCharacter Shading");

            if (settings.shadingShader != null)
            {
                for (int group = 0; group < SRCharacterStencil.MaxGroups; group++)
                {
                    Material material = new Material(settings.shadingShader)
                    {
                        name = $"SRCharacter Shading Group {group}",
                        hideFlags = HideFlags.HideAndDontSave
                    };
                    material.SetInt(SRCharacterShaderIDs.ShadeRef, group << SRCharacterStencil.GroupShift);
                    if (group == 3)
                        material.EnableKeyword(SRCharacterShaderIDs.HairShadeKeyword);
                    m_Materials[group] = material;
                }
            }
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RTHandle cameraColor = renderingData.cameraData.renderer.cameraColorTargetHandle;
            RTHandle cameraDepth = renderingData.cameraData.renderer.cameraDepthTargetHandle;
            ConfigureTarget(cameraColor, cameraDepth);
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (m_Materials[0] == null)
                return;

            CommandBuffer cmd = CommandBufferPool.Get();
            using (new ProfilingScope(cmd, profilingSampler))
            {
                cmd.SetGlobalTexture(SRCharacterShaderIDs.CharacterAlbedo, m_Targets.Albedo);
                cmd.SetGlobalTexture(SRCharacterShaderIDs.CharacterNormal, m_Targets.Normal);
                cmd.SetGlobalTexture(SRCharacterShaderIDs.CharacterMask, m_Targets.Mask);

                cmd.SetGlobalTexture(SRCharacterShaderIDs.RampCool, GetRamp(m_Settings.rampCool));
                cmd.SetGlobalTexture(SRCharacterShaderIDs.RampWarm, GetRamp(m_Settings.rampWarm));
                cmd.SetGlobalTexture(SRCharacterShaderIDs.HairRampCool, GetRamp(m_Settings.hairRampCool));
                cmd.SetGlobalTexture(SRCharacterShaderIDs.HairRampWarm, GetRamp(m_Settings.hairRampWarm));

                for (int group = 0; group < SRCharacterStencil.MaxGroups; group++)
                {
                    Material material = m_Materials[group];
                    material.SetFloat(SRCharacterShaderIDs.ToonShadowSoftness, m_Settings.shadowSoftness);
                    material.SetFloat(SRCharacterShaderIDs.ShadowAttenuationStrength, m_Settings.shadowAttenuationStrength);
                    material.SetFloat(SRCharacterShaderIDs.IndirectIntensity, m_Settings.indirectIntensity);

                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3);
                }
            }

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        private static Texture GetRamp(Texture2D ramp)
        {
            return ramp != null ? ramp : (Texture)Texture2D.whiteTexture;
        }

        public void Dispose()
        {
            for (int i = 0; i < m_Materials.Length; i++)
            {
                if (m_Materials[i] == null)
                    continue;

                if (Application.isPlaying)
                    Object.Destroy(m_Materials[i]);
                else
                    Object.DestroyImmediate(m_Materials[i]);

                m_Materials[i] = null;
            }
        }
    }
}