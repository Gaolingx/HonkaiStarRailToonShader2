using UnityEngine;

namespace SRCharacter
{
    /// <summary>
    /// Stencil layout reconstructed from the captured frame (see README):
    /// bits 7..6 = shading group, bits 5..0 = material id.
    /// </summary>
    public static class SRCharacterStencil
    {
        public const int GroupMask = 0xC0;
        public const int IdMask = 0x3F;
        public const int GroupShift = 6;
        public const int MaxGroups = 4;
        public const int MaxMaterialId = IdMask;

        // Refs observed in the capture.
        public const int BodyRefCaptured = 0x10; // group 0, id 16
        public const int SecondSetRefCaptured = 0x1F; // group 0, id 31
        public const int HairRefCaptured = 0xE0; // group 3, id 32

        public static int MakeRef(int group, int materialId)
        {
            return ((group & (MaxGroups - 1)) << GroupShift) | (materialId & IdMask);
        }

        public static int GroupFromRef(int stencilRef)
        {
            return (stencilRef & GroupMask) >> GroupShift;
        }

        // Matches SREncodeMaterialPayload in SRCharacterDefines.hlsl.
        public static int MakePackedMaterialId(int area, int materialId)
        {
            return ((area & 3) << GroupShift) | (materialId & IdMask);
        }

        public static float EncodeMaterialPayload(int area, int materialId)
        {
            return MakePackedMaterialId(area, materialId) / 255f;
        }
    }
}