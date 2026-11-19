# 角色渲染链路初析（StarRail frame 13063）

本文件只记录**有证据支撑**的结论。事件号 = RenderDoc event id；chunk 序号 = XML chunk 流顺序（`raw/xml_events_with_eid.json` 内含两者映射）。
抓帧：D3D11、NVIDIA RTX 3050 Laptop、`StarRail_2024.12.09_01.39_frame13063.rdc`（frame 13063，622 actions / 581 draws）。

## 0. 一句话结论

HSR 的角色不是"一个前向 pass 画完"：**几何在 P08 写入 GBuffer（albedo / normal / R8 mask + 深度模板），模板缓冲写入材质/描边 ID；P35 用全屏 stencil-masked pass 按材质类别（皮肤、面部、头发…）做 toon 着色，再叠加角色几何的前向高光/描边层；P27/P36 生成屏幕空间阴影与角色遮罩供后续使用，P37–P54 的 bloom 也读取角色遮罩，P55 tonemap+UI。**

## 1. 帧级链路

| 阶段 | pass | 事件(eid) | 证据 |
|---|---|---|---|
| GPU 蒙皮/顶点流 | P00–P02 | 23–651 | VS+GS+StreamOut，骨骼 SRV = buffer 7211（`R32G32B32A32_FLOAT`），输出 VB/StreamOut 缓冲 20481/20484/20486…；49 个蒙皮 draw 对应 49 份输出缓冲 |
| 阴影 | P05–P07, P16–P26 | 726–4336 | 3072x3072 R16 深度图（tex14816）、1536x960（tex9499）、1536x2560 图集 12×512x512（tex18188），均只绑 DSV |
| GBuffer + 角色 | **P08** | 812–3546 | 3072x1920 MRT：tex9503 RGBA8 / tex9507 RGBA16F / tex9511 R8，DSV tex9515(D32S8)；角色相关调用集中在 940–1093（GBuffer）与 3180–3546（描边/附加层） |
| 屏幕空间阴影 | P27 | 4357–4622 | R8 tex9580，采样主深度 tex514 + 阴影图集 tex18188/tex17638；输出为角色/物体柔和阴影轮廓（`img/tex9580_screenspace_shadowmask.png`） |
| 光照/材质着色 | **P35** | 4906–5595 | HDR R11G11B10 tex5279；前 6 个 3-index 全屏 pass 用深度模板状态 655/662（stencil EQUAL，掩码 0xC0/0x84/0x81）按材质类别着色；后续角色几何做前向叠加 |
| 角色遮罩 | P36 | 5611–6068 | MRT tex9621(R16G16) + tex5288(R8)，5288 输出角色剪影（`img/tex5288_character_maskR8.png`） |
| Bloom | P37–P54 | 6090–6249 | 逐级 downsample/upsample，ps3/ps4 读取 P36 的 9621/5288 |
| Tonemap + UI | P55 | 6268–6855 | 43 draws 写入 tex683；像素历史显示首个 draw(6268) 把 HDR 场景色调映射后写入 |
| Present | P56 | 6882 | 6-index 全屏 blit 到交换链 tex678 |

## 2. 蒙皮预计算（P00–P02）

- 骨骼数据：SRV `view7212 -> buffer 7211`，格式 `R32G32B32A32_FLOAT`（float4 数组，bone matrix 流）。
- 每个 program = 一种顶点格式变体（输入布局含 `BLENDWEIGHTS0`/`BLENDINDICES0`）：
  - 例：`VS7214/GS7215`，SO 目标 `buf7210`，461 indices（eid 23）；
  - `VS8467/GS8468/CS8432`，SO `buf20481`，20804 indices（eid 93）。
- 输出缓冲带 `D3D11_BIND_VERTEX_BUFFER | D3D11_BIND_STREAM_OUTPUT`（如 buf20481 = 832160 B），后段直接当顶点缓冲使用。
- 同批还有 CS 绑定（shader 8432/275 等）；`UpdateSubresource`（chunk 1596）一次 256 KiB 上传，`pDstBox=128x30`，对应骨骼矩阵/实例表类的常量上传。
- 注意：这是全场景蒙皮，不是角色专属；角色识别见第 3 节。

## 3. 角色 GBuffer（P08）——按像素历史定位

在 P08 结束（eid 3546）对三个屏幕像素做 `pixel_history`（target tex9503）：

| 屏幕像素 | 实际写入 draw(eid) | 说明 |
|---|---|---|
| (1500,1000) 皮肤/衣料 | **1010** | `VS8476/PS8484`，index 12552，VB 20484 + IB 15343 |
| (1150,350) 脸部 | **961**，fragment1 再由 **977** 覆盖 | `VS8531/PS8534`(67665 idx) / `VS8531/PS8537`(11970 idx)，VB 20481 + IB 15339 |
| (900,700) 头发 | **2155** | `VS5971/PS5972`，index 1359，VB 6870（非 SO，骨骼常量在 cb2=4096B） |

同一像素历史里还出现：`1023`（shaderDiscarded）、`1034/1079/1093`（backface culled，描边）、`3145/3194/3366/3522/3546`（深度失败或描边层）。

### 3.1 角色 draw 的关键状态

| eid | VS/PS | VB/IB | 深度模板状态 | 光栅化 | 用途推断 |
|---|---|---|---|---|---|
| 961 | 8531/8534 | 20481/15339 | DSS 1288：stencil REPLACE（mask 255） | CULL_BACK | 角色主体 GBuffer + stencil 材质 ID |
| 977 | 8531/8537 | 20481/15339 | DSS 5978：双面 REPLACE | CULL_NONE | 双面部件（发片/裙摆）GBuffer |
| 1010 | 8476/8484 | 20484/15343 | DSS 8527：REPLACE（mask 254） | CULL_BACK | 第二套材质（皮肤/衣料） |
| 1023 | 8492/8499 | 20484/15343 | DSS 1288 | CULL_BACK | 同几何的混合层（blend 140，皮肤像素被 discard） |
| 1045/1056 | 8514/8516、8519/8521 | 20484/15343 | DSS 1342/8533 | CULL_BACK | 小部件叠加层（180 idx） |
| 1079/1093 | 8523/8525、8528/8529 | 20486/15347 | DSS 8536/8538 | CULL_BACK | 10527 idx 部件（正/反面两遍） |
| **3194/3202** | 8520/8522 | 20481/15339 | DSS 1935：backface REPLACE（mask 255） | **CULL_FRONT** | **描边 pass**（79635 idx，2 draw） |
| **3366** | 8496/8503 | 20484/15343 | DSS 8545：backface REPLACE（mask 254） | **CULL_FRONT** | 第二套描边 |
| 3522/3535/3546 | 8515/8517 | 43722/11888 等 | DSS 1935 | CULL_FRONT | 其他体积描边 |

结论：**描边是独立 draw（front-face cull + backface stencil REPLACE），与 GBuffer 共享同一蒙皮缓冲**；GBuffer 通过 `StencilPassOp=REPLACE` 把材质/描边编号写入模板，供 P35 用 `StencilFunc=EQUAL` 做分材质着色。

### 3.2 GBuffer 目标格式

- tex9503 `R8G8B8A8_UNORM`：albedo（`img/tex9503_gbuffer_albedo.png`，可清楚看到角色）
- tex9507 `R16G16B16A16_FLOAT`：法线（`img/tex9507_gbuffer_normal.png`）
- tex9511 `R8_UNORM`：二值遮罩（`img/tex9511_gbuffer_maskR8.png`，角色/场景剪影 → 用于 bloom 或雾遮罩）
- tex9515 `D32_FLOAT_S8_UINT`：深度存近平面，`DepthFunc=GREATER_EQUAL`（**反向 Z**）

## 4. 角色着色（P35，eid 4906–5595）

### 4.1 全屏 stencil-masked 材质 pass

| eid | VS/PS | 深度模板状态 | 像素历史表现 |
|---|---|---|---|
| 4906 | 653/9571 | DSS 655：EQUAL，ReadMask 0xC0，pass 清 0 | 皮肤像素 stencil fail |
| 4924 | 656/657 | DSS 655 | 皮肤像素 stencil fail（输出 0.79,0.52,0.49 皮肤色） |
| 4948 | 658/9572 | DSS 655 | 皮肤像素 stencil fail |
| **4956** | 660/661 | DSS 662：EQUAL，ReadMask 0xC0，不写模板 | **皮肤像素通过，写入 0.754,0.496,0.469** |
| 4989 | 9573/9584 | DSS 1305 | 通过但写 0（不改变颜色） |

这些 draw 都是 3-index 全屏三角形（VB buf96、IB buf97），SRV 同时读取：
`tex9580`(SS 阴影)、`tex9503/9507`(GBuffer)、`tex514`(深度)、`tex3403`(SSAO)、`tex9596`、`tex15780`(雾)、`tex5267`(反射/降噪) 等；CB 为 `buf211(1040B)` + `buf184/buf193(576B)` + `buf191(1872B)`。
即：**deferred/toon 光照按模板材质 ID 分派，全屏求值而非逐几何求值**。

### 4.2 角色几何前向叠加

像素历史中又有角色几何 draw 在原像素上做附加（additive 类 blend）：

| eid | VS/PS | VB/IB | 状态 | 说明 |
|---|---|---|---|---|
| 5083 | 8507/8511 | 20481/15339 | DSS 817（stencil on，不写深度） | 角色 67665 idx，前向叠加 |
| 5096 | 8507/8518 | 20481/15339 | DSS 817 | 角色 11970 idx（头发/发饰），fragment1 在头发像素生效 |
| 5382 | — | — | depthTestFailed | 叠加层（光圈/高光），未通过深度 |

### 4.3 材质输入（PS SRV，角色相关）

- 961：`tex15288 4096x2048 BC7_SRGB`（角色图集 albedo）+ `tex15290`(BC7) + `tex15285/15282 256x16`（渐变/ramp 图）+ 阴影图 `tex14816` + 4x4 dummy。
- 1010：`tex15296 1024x1024`、`tex15318/15320 1024x1024`、`tex9499`(半分辨率阴影)。
- 2155：`tex9105/9095 1024x1024`（NPC/道具类）。
- 光照侧还有 8x8 `R16G16B16A16_FLOAT`（tex15314/15322/15292，疑似 LUT/参数图）。

所有绑定（slot→资源 ID→格式尺寸）见 `02-pass-bindings.md` 的 P08/P35 表，逐 draw 见 `raw/draws.csv`。

### 4.4 低 2 位模板：眼睛遮挡（bit0）与半透明刘海（第二轮补证）

P35 的前 4 个全屏材质 pass 之后还有 4 个 240-index draw（eid 4968/4989/4999/5015），
用 stencil **bit0** 做“眼睛被头发/眼皮遮住”的临时标记，再决定是否叠加眼睛高光：

| eid | 几何 | DSS | StencilRef | 关键状态 | 作用 |
|---|---|---|---|---|---|
| 4968 / 4999 | 眼睛 proxy：42 顶点 / 80 三角形，VB 463 / IB 464 | 1303 | 1 | depth `GREATER_EQUAL`，Stencil `Always`，**`ZFail=Replace`**，WriteMask 1，ColorMask 0 | proxy 深度测试失败（被前方的刘海/眼皮盖住）时置 bit0=1 |
| 4989 / 5015 | 同一 proxy 的背面 | 1305 | 128 | depth `LESS_EQUAL`，ReadMask **0x81**，WriteMask 1，Pass/Fail/ZFail=`Zero`，blend 204（ONE/ONE，RGB） | 仅当“bit7=1 且 bit0=0”时做加法眼睛高光，同时清 bit0 |

全屏材质 pass 的 bit0 清理：4906/4924/4948（组 1–3）用 DSS 655（ReadMask 0xC0 / WriteMask 1 / Pass=Zero）；
4956（组 0）用 DSS 662（WriteMask 0，不清）。P27 的屏幕空间阴影也用同一 bit0 做标记（Ref 1 / W1 / ZFail Replace，eid 4371…）。

`4989` 的 PS（9584）用屏幕空间 GBuffer 采样：t0=tex9503 albedo、t1=tex9507 normal（`normal.a*65535 & 7` 解材质类型）、
t3=tex514 深度、t4=tex3403，并用 `unity_MatrixInvVP` 反推世界坐标——即“屏幕空间眼睛着色”，不是简单的几何高光。
常量内容仍不可得（见 §6），本仓库只复现其模板门控与加法载荷。

半透明刘海：VB 41839 / IB 15331（219 顶点 / 352 三角形）只在 P16 阴影与 P35 出现，**从不进入 P08 GBuffer**：

| eid | blend | DSS | StencilRef | 状态 | 说明 |
|---|---|---|---|---|---|
| 3836（P16） | 213 | 15533 | 11 | `NOT_EQUAL` → Replace | 阴影图里只写第一层 |
| 5349 | 213 | 1342（无 stencil） | 0 | ZWrite Off | 该部件的无模板绘制 |
| 5361 | 213 | 8553 | 24 | `Always` → Replace | 写入自己的材质 ID |
| 5404 | 213 | 1935 | 10 | 背面 Replace / ZWrite ALL / Cull Front | 描边 |
| 5585 / 5595 | 213 | 15533 / 15538 | 11 | `NOT_EQUAL` → Replace（背面同样） | 多层发片在同一像素只画第一层 |

blend 213 = `SRC_ALPHA / INV_SRC_ALPHA`；PS 15531 输出 alpha = 软边系数 × 顶点 alpha，即发梢淡出。
因为不进 GBuffer，半透明刘海不会覆盖脸部 stencil，也不会被全屏材质 pass 二次着色（眼睛遮挡标记因此仍需 bit0）。

## 5. 角色相关后处理

- **P27 屏幕空间阴影**（tex9580 R8，`img/tex9580_screenspace_shadowmask.png`）：27 draws，其中 3-index/36-index 全屏+小几何；采样主深度与阴影图集 18188/17638，输出柔和阴影轮廓，P35 的 ps0 使用。
- **P36 后光照遮罩**：tex5288(R8) 输出角色剪影（`img/tex5288_character_maskR8.png`），tex9621(R16G16) 导出为纯黑（可能仅在其它通道/G 通道有值）。二者被 P37–P54 的 bloom 读取（ps3/ps4），说明**角色遮罩参与 bloom 强度控制**。
- P55 的 tonemap draw(eid 6268) 把 tex5279(HDR) 写入最终 RT 683；此前 bloom 结果 tex5300/9633 也在 P55 被读取。

## 6. CB 绑定情况

- 每个 program 的 CB 绑定（slot → buffer id → byteWidth）已逐条记录在 `02-pass-bindings.md` 与 `raw/pass_detail.json`。
- 角色主 draw 的典型 CB 布局（eid 1010，VS）：cb0=4560B、cb1=576B、cb2=656B、cb3=352B、cb4=320B、cb5=1872B；PS：cb0=4560B、cb1=576B、cb2=656B、cb3=320B、cb4=576B。
- 这些是 **D3D11_USAGE_DYNAMIC + CPU_WRITE** 缓冲（如 buf1911=4096B、buf184/181 等），逐帧通过 `Map/Unmap` 写入；XML 仅存"写入了哪些字节区间"（Unmap 的 MapWrittenData），**不含常量内容**。
- 因此本轮的 CB 结论止于"绑定关系 + 大小 + 用途分组"；变量语义需下一轮通过下列手段补齐：
  1. 用 RenderDoc 的 `GetCBufferVariableContents`（本地 renderdoc-mcp 源码已有 `cbuffer` 工具但未注册）导出变量/值；
  2. `debug_pixel` 在 4956/1010 等 draw 上抓 PS/VS 寄存器快照；
  3. 对 `MapWrittenData` 缓冲（ResourceId 形如 5478）用 `export_buffer` 解码（若该 buffer 属可导出资源）。

## 7. 已确认的实现特征（对本项目复现有意义）

1. **反向 Z**：全部深度比较 `GREATER_EQUAL`，清深度 0。
2. **模板即材质 ID**：GBuffer 用 REPLACE 写模板；光照 pass 用 EQUAL + ReadMask（0xC0/0x81/0x84/0xC1）选择材质分支；描边 pass 用 backface REPLACE。
3. **描边 = front-face cull 的独立几何 pass**，读同一蒙皮输出缓冲，不做全屏边缘检测。
4. **渲染分辨率 1.2×**：内部 RT 3072x1920，最终 UI/输出 2560x1600。
5. **GPU 驱动**：蒙皮/剔除用 SO+CS 构建顶点流与 indirect args（55 次 `DrawIndexedInstancedIndirect`）。
6. **角色遮罩参与 bloom**：P36 的 R8 角色剪影被 bloom 链读取。

## 8. 待确认 / 下一轮建议

- [ ] 导出 `MapWrittenData` 缓冲，确认角色 CB（cb0 4560B）里是骨骼矩阵、材质参数还是动画曲线。
- [ ] 对 eid 4956/1010 用 `debug_pixel --trace`，确认 toon ramp / SDF 面部阴影的采样方式（tex15285/15282 256x16 疑似 ramp）。
- [ ] 确认 tex9511(R8) 与 tex5288(R8) 的具体消费者语义（是否都进 bloom mask）。
- [ ] P30/P31（tex9596）与 P32–P34（864x540/1728x1080 RGBA16F）的功能确认（可能为 SSGI/SSR 或体积光）。
- [ ] 对角色描边 draw(3194) 做 `debug_vertex`，确认法线外扩发生 VS 还是几何阶段。

## 9. 第二轮（重建）对上述待办的状态

第二轮在 [02-SRCharacterShaderReserve](../02-SRCharacterShaderReserve/README.md) 中重建了多 pass 管线，
并新增了逐 draw 的 `StencilRef` 提取（[analysis/extract_stencil_refs.py](../02-SRCharacterShaderReserve/analysis/extract_stencil_refs.py)）。
以下状态只反映"是否被重建工作触及"，**不等于抓帧已直接证实**：

| 待办 | 状态 | 说明 |
|---|---|---|
| CB 内容（cb0 4560B） | 仍未知 | CB 仍未解码；重建模块用 URP 自己的 CB 布局，不声称字节级对应 |
| toon ramp / SDF 采样方式 | 部分 | 按 256x16 与 8x8 LUT 的尺寸证据实现了 ramp 行选择与面部 SDF；HSR 的具体采样公式仍是推断 |
| tex9511 / tex5288 消费者语义 | 部分 | 重建中 9511 语义映射为角色 mask（R=剪影/G=阴影形状/A=面部 lit），5288 未重建 |
| P30/P31 与 P32–P34 功能 | 仍未知 | 未重建 |
| 描边外扩阶段 | 部分 | 重建采用 VS 法线外扩（与 StarRailNPRShader / HonkaiStarRailToonShader 一致），抓帧未做 `debug_vertex` 验证 |
| 低 2 位模板 / 半透明刘海 | 已补证 | 见 §4.4：bit0 用于眼睛遮挡标记（DSS 1303/1305），透明刘海走 alpha 混合 + `NOT_EQUAL` 第一层去重 |
