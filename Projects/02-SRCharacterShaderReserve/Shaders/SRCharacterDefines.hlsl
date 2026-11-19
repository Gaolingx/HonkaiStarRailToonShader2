#ifndef SR_CHARACTER_DEFINES_INCLUDED
#define SR_CHARACTER_DEFINES_INCLUDED

// Stencil encoding reconstructed from the frame-13063 capture (see README):
//   bits 7..6 = shading group (0..3)
//   bits 5..0 = material id (ramp row / material slot)
// Observed refs: body 0x10, second set 0x1F, hair 0xE0.
// P08 wrote with Pass=Replace, P35 read with ReadMask 0xC0 + Comp=Equal.
#define SR_STENCIL_GROUP_MASK   0xC0
#define SR_STENCIL_ID_MASK      0x3F
#define SR_STENCIL_GROUP_SHIFT  6

// Bit 0 is a transient flag, not part of the material id:
//   1 = this pixel is covered by an eye proxy surface that failed the depth test
//       (capture DSS 1303: Stencil ZFail=Replace, WriteMask=1).
//   The eye shading pass tests (group bits | bit0) with Equal and clears bit0
//   (capture DSS 1305: ReadMask=0x81, WriteMask=1, Pass/Fail/ZFail=Zero).
#define SR_STENCIL_FLAG_MASK    0x01
#define SR_STENCIL_EYE_READ_MASK 0x81

// Area type stored in the character mask (G-buffer mask R channel stays a silhouette flag).
#define SR_AREA_BODY 1
#define SR_AREA_FACE 2
#define SR_AREA_HAIR 3

// Material payload written into the albedo alpha channel:
//   alpha = ((area & 3) << 6 | (materialId & 63)) / 255
float SREncodeMaterialPayload(float area, float materialId)
{
    uint packed = ((uint)area << SR_STENCIL_GROUP_SHIFT) | ((uint)materialId & SR_STENCIL_ID_MASK);
    return (float)packed / 255.0;
}

void SRDecodeMaterialPayload(float encoded, out uint area, out uint materialId)
{
    uint packed = (uint)round(saturate(encoded) * 255.0);
    area = (packed >> SR_STENCIL_GROUP_SHIFT) & 3u;
    materialId = packed & (uint)SR_STENCIL_ID_MASK;
}

#endif // SR_CHARACTER_DEFINES_INCLUDED