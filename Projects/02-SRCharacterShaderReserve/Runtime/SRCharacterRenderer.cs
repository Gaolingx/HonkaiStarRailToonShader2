using System.Collections.Generic;
using UnityEngine;

namespace SRCharacter
{
    /// <summary>
    /// Authors the per-material stencil payload (shading group + material id) and the head axes
    /// used by the face SDF evaluation. Attach to the character root.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class SRCharacterRenderer : MonoBehaviour
    {
        public enum Area
        {
            Body = 1,
            Face = 2,
            Hair = 3
        }

        public Area area = Area.Body;
        [Range(0, SRCharacterStencil.MaxGroups - 1)] public int shadingGroup;
        [Range(0, SRCharacterStencil.MaxMaterialId)] public int materialId = 16;

        [Tooltip("Optional head bone. Its world axes drive the face SDF evaluation.")]
        public Transform head;

        private readonly List<Material> m_Materials = new List<Material>();

        private void OnEnable()
        {
            Apply();
        }

        private void OnValidate()
        {
            materialId = Mathf.Clamp(materialId, 0, SRCharacterStencil.MaxMaterialId);
            shadingGroup = Mathf.Clamp(shadingGroup, 0, SRCharacterStencil.MaxGroups - 1);
            Apply();
        }

        private void LateUpdate()
        {
            if (head != null)
                ApplyHeadAxes();
        }

        [ContextMenu("Apply Character Settings")]
        public void Apply()
        {
            int stencilRef = SRCharacterStencil.MakeRef(shadingGroup, materialId);
            bool isFace = area == Area.Face;

            m_Materials.Clear();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                // In play mode .materials returns instances; in edit mode writing to shared materials is safe.
                Material[] materials = Application.isPlaying ? r.materials : r.sharedMaterials;
                foreach (Material material in materials)
                {
                    if (material == null)
                        continue;

                    material.SetFloat(SRCharacterShaderIDs.StencilRef, stencilRef);
                    material.SetFloat(SRCharacterShaderIDs.Area, (int)area);
                    material.SetFloat(SRCharacterShaderIDs.ShadingGroup, shadingGroup);
                    material.SetFloat(SRCharacterShaderIDs.MaterialID, materialId);

                    if (isFace)
                        material.EnableKeyword(SRCharacterShaderIDs.AreaFaceKeyword);
                    else
                        material.DisableKeyword(SRCharacterShaderIDs.AreaFaceKeyword);

                    m_Materials.Add(material);
                }
            }

            ApplyHeadAxes();
        }

        private void ApplyHeadAxes()
        {
            if (head == null)
                return;

            Vector3 right = head.right;
            Vector3 forward = head.forward;
            Vector3 up = head.up;

            foreach (Material material in m_Materials)
            {
                if (material == null)
                    continue;

                material.SetVector(SRCharacterShaderIDs.HeadRight, right);
                material.SetVector(SRCharacterShaderIDs.HeadForward, forward);
                material.SetVector(SRCharacterShaderIDs.HeadUp, up);
            }
        }
    }
}