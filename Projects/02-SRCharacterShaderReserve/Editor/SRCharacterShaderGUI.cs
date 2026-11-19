using UnityEditor;
using UnityEngine;

namespace SRCharacter
{
    /// <summary>
    /// Keeps the stencil reference in sync with (shading group, material id):
    /// Ref = group &lt;&lt; 6 | material id, matching the captured 0x10 / 0x1F / 0xE0 values.
    /// </summary>
    public sealed class SRCharacterShaderGUI : ShaderGUI
    {
        public override void OnGUI(MaterialEditor materialEditor, MaterialProperty[] properties)
        {
            EditorGUI.BeginChangeCheck();
            base.OnGUI(materialEditor, properties);
            bool changed = EditorGUI.EndChangeCheck();

            foreach (Object target in materialEditor.targets)
            {
                if (target is Material material)
                    changed |= SyncMaterial(material);
            }

            if (materialEditor.target is Material selected)
            {
                int stencilRef = (int)selected.GetFloat(SRCharacterShaderIDs.StencilRef);
                EditorGUILayout.HelpBox(
                    $"Stencil Ref = {stencilRef} (0x{stencilRef:X2})\n" +
                    "group = Ref >> 6 (0..3), material id = Ref & 0x3F.\n" +
                    "Group 3 is shaded by the hair pass; groups 0..2 share the body ramps.",
                    MessageType.None);
            }
        }

        private static bool SyncMaterial(Material material)
        {
            if (material == null || material.shader == null || !material.shader.name.StartsWith("SRCharacter/"))
                return false;

            int group = Mathf.Clamp((int)material.GetFloat(SRCharacterShaderIDs.ShadingGroup), 0, SRCharacterStencil.MaxGroups - 1);
            int materialId = Mathf.Clamp((int)material.GetFloat(SRCharacterShaderIDs.MaterialID), 0, SRCharacterStencil.MaxMaterialId);
            int area = Mathf.Clamp((int)material.GetFloat(SRCharacterShaderIDs.Area), 1, 3);

            float stencilRef = SRCharacterStencil.MakeRef(group, materialId);
            bool changed = !Mathf.Approximately(material.GetFloat(SRCharacterShaderIDs.StencilRef), stencilRef);

            if (changed)
                material.SetFloat(SRCharacterShaderIDs.StencilRef, stencilRef);

            bool faceWasEnabled = material.IsKeywordEnabled(SRCharacterShaderIDs.AreaFaceKeyword);
            bool faceShouldBeEnabled = area == 2;
            if (faceWasEnabled != faceShouldBeEnabled)
            {
                if (faceShouldBeEnabled)
                    material.EnableKeyword(SRCharacterShaderIDs.AreaFaceKeyword);
                else
                    material.DisableKeyword(SRCharacterShaderIDs.AreaFaceKeyword);
                changed = true;
            }

            return changed;
        }
    }
}