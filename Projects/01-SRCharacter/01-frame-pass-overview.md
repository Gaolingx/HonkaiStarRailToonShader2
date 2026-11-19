# StarRail frame 13063 - pass/event overview

Capture: `E:\FileRecv\StarRail_2024.12.09_01.39_frame13063.rdc` (D3D11, RTX 3050 Laptop, 622 events, 581 draws).
Event ids below are RenderDoc event ids; `chunkIndex` is the index in the XML chunk stream (used for ordering).

| pass | events (eid) | draws | dispatches | indices | viewport | color RT | depth | inferred role |
|---|---|---|---|---|---|---|---|---|
| P00 | 23-23 | 1 | 0 | 461 | - | - | - | GPU skinning / vertex-stream pre-pass (VS+GS+SO), single probe draw |
| P01 | 40-82 | 0 | 4 | 0 | - | - | - | Compute culling/skinning dispatch batch |
| P02 | 93-651 | 49 | 0 | 64731 | - | - | - | GPU skinning batches -> stream-out vertex buffers (49 draws) |
| P03 | chunk 2581-2583 | 0 | 0 | 0 | 2560x1600@0,0 | tex683 | tex687 | Bind main color RT 683 + depth 687 (state only) |
| P04 | 680-700 | 0 | 2 | 0 | 2560x1600@0,0 | tex683 | tex687 | Compute pass on main framebuffer state |
| P05 | chunk 2618-2618 | 0 | 0 | 0 | 3072x3072@0,0 | - | tex14816 | Clear shadow atlas (3072x3072 R16 depth) |
| P06 | 726-776 | 6 | 0 | 109794 | 1536x1536@0,0 | - | tex14816 | Directional shadow map (cascade) depth-only |
| P07 | 790-790 | 1 | 0 | 10527 | 1536x960@0,0 | - | tex9499 | Spot/point shadow map clear + depth-only |
| P08 | 812-3546 | 246 | 0 | 974781 | 3072x1920@0,0 | tex9503 + tex9507 + tex9511 | tex9515 | Opaque GBuffer pass: albedo RGBA8 + normal RGBA16F + mask R8, depth/stencil D32S8; characters write stencil IDs |
| P09 | 3564-3573 | 2 | 0 | 6 | 1536x960@0,0 | tex9529 | tex9525 | Half-res depth copy + fullscreen draws |
| P10 | 3593-3662 | 0 | 6 | 0 | 1536x960@0,0 | tex9529 | tex9525 | Half-res compute effects (6 dispatches) |
| P11 | chunk 5578-5579 | 0 | 0 | 0 | 1536x960@0,0 | tex9529 | tex9525 | Half-res compute effects (state only) |
| P12 | 3683-3683 | 1 | 0 | 3 | 128x128@0,0 | tex17656 | - | 128x128 probe render |
| P13 | 3701-3701 | 0 | 1 | 0 | 128x128@0,0 | tex17656 | - | 128x128 compute |
| P14 | chunk 5617-5620 | 0 | 0 | 0 | 128x128@0,0 | tex17656 | - | 128x128 state |
| P15 | 3721-3721 | 0 | 1 | 0 | 128x128@0,0 | tex17656 | - | 128x128 compute |
| P16 | 3744-3863 | 10 | 0 | 112038 | 512x512@0,0 | - | tex18188 | CSM shadow atlas tile 0 (512x512) depth-only |
| P17 | 3878-3915 | 5 | 0 | 20946 | 512x512@512,0 | - | tex18188 | CSM shadow atlas tile 1 |
| P18 | 3930-3956 | 4 | 0 | 15876 | 512x512@1024,0 | - | tex18188 | CSM shadow atlas tile 2 |
| P19 | 3971-4008 | 5 | 0 | 20799 | 512x512@0,512 | - | tex18188 | CSM shadow atlas tile 3 |
| P20 | 4023-4056 | 5 | 0 | 24228 | 512x512@512,512 | - | tex18188 | CSM shadow atlas tile 4 |
| P21 | 4071-4108 | 5 | 0 | 13434 | 512x512@1024,512 | - | tex18188 | CSM shadow atlas tile 5 |
| P22 | 4123-4162 | 5 | 0 | 11490 | 512x512@0,1024 | - | tex18188 | CSM shadow atlas tile 6 |
| P23 | 4177-4207 | 4 | 0 | 8676 | 512x512@512,1024 | - | tex18188 | CSM shadow atlas tile 7 |
| P24 | 4222-4250 | 4 | 0 | 2466 | 512x512@512,1536 | - | tex18188 | CSM shadow atlas tile 8 |
| P25 | 4265-4296 | 5 | 0 | 3756 | 512x512@1024,1536 | - | tex18188 | CSM shadow atlas tile 9 |
| P26 | 4311-4336 | 4 | 0 | 2334 | 512x512@0,2048 | - | tex18188 | CSM shadow atlas tile 10 |
| P27 | 4357-4622 | 27 | 0 | 939 | 3072x1920@0,0 | tex9580 | tex9515 | Screen-space shadow/occlusion mask (R8) for characters/objects |
| P28 | 4650-4700 | 0 | 4 | 0 | 1536x960@0,0 | tex683 | tex687 | Compute dispatch batch (main view) |
| P29 | 4718-4735 | 2 | 0 | 72 | 3072x1920@0,0 | tex15780 | tex9515 | Fog / atmosphere pass into R11G11B10 |
| P30 | 4762-4762 | 1 | 0 | 3 | 3072x1920@0,0 | tex9596 | tex514 | Fullscreen composite into RGBA8 (samples depth, GBuffer, masks) |
| P31 | 4783-4827 | 0 | 3 | 0 | 3072x1920@0,0 | tex9596 | tex514 | Compute batch on composite target |
| P32 | 4848-4848 | 1 | 0 | 3 | 864x540@0,0 | tex17616 + tex17620 | - | Half-res 864x540 dual-RGBA16F effect |
| P33 | 4860-4860 | 1 | 0 | 3 | 1728x1080@0,0 | tex5271 | - | 1728x1080 RGBA16F pass |
| P34 | 4872-4872 | 1 | 0 | 3 | 1728x1080@0,0 | tex5267 | - | 1728x1080 RGBA16F pass |
| P35 | 4906-5595 | 55 | 0 | 306372 | 3072x1920@0,0 | tex5279 | tex9515 | Deferred lighting / material shading into HDR R11G11B10: stencil-masked fullscreen material passes + forward geometry overlays (characters shaded here) |
| P36 | 5611-6068 | 71 | 0 | 438933 | 3072x1920@0,0 | tex9621 + tex5288 | tex9515 | Post-lighting MRT pass: R16G16 + R8 mask (character silhouette / effect mask consumed by bloom) |
| P37 | 6090-6090 | 1 | 0 | 3 | 3072x1920@0,0 | tex5300 | - | Bloom extract (full-res 3072x1920 -> tex5300) |
| P38 | 6104-6104 | 1 | 0 | 3 | 1536x960@0,0 | tex9665 | - | Bloom downsample 1536x960 |
| P39 | 6116-6116 | 1 | 0 | 3 | 768x480@0,0 | tex9669 | - | Bloom downsample 768x480 |
| P40 | 6126-6126 | 1 | 0 | 3 | 320x200@0,0 | tex9633 | - | Bloom downsample 320x200 |
| P41 | 6136-6136 | 1 | 0 | 3 | 144x90@0,0 | tex9641 | - | Bloom downsample 144x90 |
| P42 | 6146-6146 | 1 | 0 | 3 | 64x40@0,0 | tex9649 | - | Bloom downsample 64x40 |
| P43 | 6156-6156 | 1 | 0 | 3 | 32x20@0,0 | tex9657 | - | Bloom downsample 32x20 |
| P44 | chunk 8072-8072 | 0 | 0 | 0 | 465x200@0,0 | tex9629 | - | Bloom atlas clear (465x200) |
| P45 | 6171-6171 | 1 | 0 | 3 | 320x200@0,0 | tex9629 | - | Bloom upsample (mip 320x200 into atlas) |
| P46 | 6180-6180 | 1 | 0 | 3 | 144x90@321,0 | tex9629 | - | Bloom upsample (mip 144x90) |
| P47 | 6189-6189 | 1 | 0 | 3 | 64x40@321,91 | tex9629 | - | Bloom upsample (mip 64x40) |
| P48 | 6198-6198 | 1 | 0 | 3 | 32x20@386,91 | tex9629 | - | Bloom upsample (mip 32x20) |
| P49 | chunk 8114-8114 | 0 | 0 | 0 | 465x200@0,0 | tex9625 | - | Bloom atlas clear (second level) |
| P50 | 6212-6212 | 1 | 0 | 3 | 320x200@0,0 | tex9625 | - | Bloom upsample (second level) |
| P51 | 6220-6220 | 1 | 0 | 3 | 144x90@321,0 | tex9625 | - | Bloom upsample (second level) |
| P52 | 6228-6228 | 1 | 0 | 3 | 64x40@321,91 | tex9625 | - | Bloom upsample (second level) |
| P53 | 6236-6236 | 1 | 0 | 3 | 32x20@386,91 | tex9625 | - | Bloom upsample (second level) |
| P54 | 6249-6249 | 1 | 0 | 3 | 320x200@0,0 | tex9633 | - | Bloom final combine 320x200 |
| P55 | 6268-6855 | 43 | 0 | 6777 | 2560x1600@0,0 | tex683 | tex687 | Tonemap + UI composition into RT 683 (43 draws) |
| P56 | 6882-6882 | 1 | 0 | 6 | 2560x1600@0,0 | - | - | Present blit to swapchain texture 678 |