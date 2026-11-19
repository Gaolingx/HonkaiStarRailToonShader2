# 01-SRCharacter — StarRail 角色渲染抓帧分析（第一轮）

抓帧：`E:\FileRecv\StarRail_2024.12.09_01.39_frame13063.rdc`
API：D3D11 / NVIDIA RTX 3050 Laptop / 622 actions / 581 draws / 无 debug marker（pass 靠 RT+viewport+shader 聚类）。

## 本轮交付

| 文件 | 内容 |
|---|---|
| [01-frame-pass-overview.md](01-frame-pass-overview.md) | 57 个 pass 的事件区间、draw/dispatch 数、viewport、RTV/DSV、推断作用 |
| [02-pass-bindings.md](02-pass-bindings.md) | 逐 pass：RTV/DSV、SRV 资源、CB 缓冲与大小、blend/DSS/RS 状态、逐 program 绑定表 |
| [03-resource-dependencies.md](03-resource-dependencies.md) | pass 间资源依赖图（Mermaid）+ 关键 RT 的生产者/消费者表 |
| [04-character-rendering.md](04-character-rendering.md) | **角色渲染链路**：蒙皮预计算 → GBuffer+模板 → 全屏 stencil 材质着色 → 描边 → 后处理；含像素历史证据 |
| `raw/draws.csv` | 581 个 draw 的逐条索引（pass、eid、VS/PS、VB/IB、RT、状态、SRV/CB 摘要） |
| `raw/pass_detail.json` | 逐 pass/逐 program 的完整绑定（机器可读，供脚本继续分析） |
| `raw/xml_events_with_eid.json` | 632 个动作的完整状态快照 + `chunkIndex ↔ mcp eventId` 映射 |
| `raw/state_objects.json` | 17 blend / 28 depth-stencil / 18 rasterizer / 8 sampler 状态对象定义 |
| `raw/xml_resources.json` | 纹理/缓冲/视图/着色器/输入布局定义 |
| `raw/mesh_4968.json` / `mesh_5349.json` | P35 眼睛 proxy（42 顶点 / 80 三角形）与半透明刘海（219 顶点 / 352 三角形）的 post-VS 网格导出 |
| `img/` | 关键 RT 导出（GBuffer、HDR、遮罩、SS 阴影） |
| `img/eye/` | P35 眼睛 pass 前后的 HDR 帧（eid 4956 / 5015）与面部裁切 |

## 数据来源与方法

1. **RenderDoc MCP（原生工具）**：`open_capture`、`list_events`、`list_draws`、`get_pipeline_state`、`get_bindings`、`goto_event`、`export_texture`、`pixel_history`、`pick_pixel`。
2. **XML chunk 流**：`renderdoccmd convert -c xml` 把 649 MB 抓帧转成 11 MB XML，包含每次 `OMSetRenderTargets`、`*SetShaderResources`、`*SetConstantBuffers`、`Map/Unmap`（含写入字节区间）、`Draw*` 的真实资源 ID —— MCP 的 `get_bindings` 只给反射名（`texture0`/`cbuffer0`），拿不到 ID，因此 XML 是本轮资源依赖分析的主数据源。
3. **像素历史**：在 P08 末尾（eid 3546）与 P35 末尾（eid 5595）对皮肤/面部/头发像素做 `pixel_history`，直接定位角色 draw。
4. **视觉确认**：导出关键 RT 为 PNG（已存 `img/`）。

复现脚本（本目录）：`parse_xml.py`（chunk→事件/资源）→ `parse_states.py`（状态对象）→ `eid_map.py`（XML↔MCP 事件号）→ `pass_report.py` / `lookup.py`（查询）→ `build_docs.py`（生成本目录 md 与 csv）。

## 关键结论（详见 04）

- 角色几何在 **P08** 写入 GBuffer：tex9503 albedo(RGBA8)、tex9507 normal(RGBA16F)、tex9511 mask(R8)、tex9515 depth/stencil(D32S8，反向 Z)。
- GBuffer 阶段用 **stencil REPLACE 写材质/描边 ID**；**P35** 用 6 个全屏三角形 + `StencilFunc=EQUAL` 的材质掩码 pass 分材质着色到 HDR tex5279（皮肤像素由 eid 4956 写入得到证实），随后角色几何做前向叠加（eid 5083/5096）。
- **描边**是独立 draw：`CULL_FRONT` + backface stencil REPLACE（eid 3194/3366），与 GBuffer 共用 GPU 蒙皮输出缓冲。
- **GPU 蒙皮**：P00–P02 用 VS+GS+StreamOut 把蒙皮结果写入 `VERTEX_BUFFER|STREAM_OUTPUT` 缓冲（如 buf20481），骨骼矩阵来自 SRV buffer 7211（float4 数组）。
- 屏幕空间阴影（P27，tex9580）与角色剪影（P36，tex5288）作为后续着色/bloom 的输入。
- P35 用 **stencil bit0** 传递“眼睛是否被挡住”（eid 4968/4989/4999/5015）；半透明刘海不进 GBuffer，
  以 alpha 混合 + `NOT_EQUAL` 第一层去重绘制（eid 5349/5361/5404/5585/5595，见 04 §4.4）。

## 已知限制

- 抓帧无 debug marker，pass 边界由 RT/DSV/viewport/着色器序列推断，命名（P00…P56）是本轮自定义编号。
- MCP `get_bindings` 不含真实资源 ID；CB 只记录到"槽位→buffer→大小"，**常量内容未解码**（XML 只存 `MapWrittenData` 的缓冲引用）。
- 纹理/着色器无调试名（引擎已 strip），角色识别依赖像素历史与顶点缓冲（蒙皮 SO 输出）关联，而非名称。
- P30/P31/P32–P34 的功能尚待确认（可能是 SSGI/SSR/体积光），已在 04 的待办中列出。
