# 02-SRCharacterShaderReserve — 星穹铁道角色渲染重建（第二轮）

在 URP 14.0.12 / Unity 2022.3.62f1 下，按 [01-SRCharacter](../01-SRCharacter/README.md) 的抓帧分析重建 HSR 角色渲染的 **多 pass 管线**：
G-Buffer（MRT + 模板材质 ID）→ 描边（front-face cull）→ 全屏分组模板着色 → 加法前向叠加。

本目录不是 Unity 工程的一部分（在 `Assets` 之外），导入方式见 [使用步骤](#使用步骤)。

## 1. 证据基线

抓帧：`StarRail_2024.12.09_01.39_frame13063.rdc`（D3D11，3072×1920，无 debug marker）。

本轮在 01 的结论之上补了一项关键证据：用 `renderdoccmd convert -c xml` 导出后，从
`OMSetDepthStencilState` 块里提取了**每个 draw 的 `StencilRef`**（01 的报告中只有状态对象 ID）。
脚本：[analysis/extract_stencil_refs.py](analysis/extract_stencil_refs.py)。

结果（与 01 的 `raw/draws.csv` 按 `chunkIndex` 关联）：

| 阶段 | draw（eid） | 几何 | StencilRef | DSS 关键状态 | 解释 |
|---|---|---|---|---|---|
| P08 GBuffer | 961 | 67665 idx（主体） | **0x10** (16) | 1288：W255 / REPLACE / GREATER_EQUAL | 组 0 + 材质 16 |
| P08 GBuffer | 977 | 11970 idx（双面） | 0x10 | 5978：双面 REPLACE，Cull None | 同一套材质 |
| P08 GBuffer | 1010 | 12552 idx（第二套） | **0x1F** (31) | 8527：**W254**（保留 bit0）/ REPLACE | 组 0 + 材质 31 |
| P08 GBuffer | 2130/2208/… | 5971/5972 头发 | **0xE0** (224) | 1288 | 组 3（高两位 11）+ 材质 32 |
| P08 描边 | 3194/3220/3468 | 同蒙皮缓冲，CULL_FRONT | 0x10 | 1935：背面 REPLACE / W255 | 描边走**同一套 G-Buffer** |
| P08 描边 | 3366/3383 | 第二套 | 0x1F | 8545：背面 REPLACE / W254 | 第二套描边 |
| P35 全屏 | 4906 | 3 idx 全屏 | **192** (0xC0) | 655：**ReadMask 0xC0**，EQUAL，Pass=ZERO / W1 | 组 3（头发）着色，并清 bit0 |
| P35 全屏 | 4924 | 3 idx 全屏 | **64** (0x40) | 655 | 组 1 |
| P35 全屏 | 4948 | 3 idx 全屏 | **128** (0x80) | 655 | 组 2 |
| P35 全屏 | 4956 | 3 idx 全屏 | **0** | 662：ReadMask 0xC0，EQUAL，不写 | 组 0（肤色/脸，像素历史证实） |
| P35 前向 | 5083/5096 | 角色几何 | 16 | 817：stencil 实际 ALWAYS、ZWrite Off、**blend 208 = ONE/ONE 加法** | 叠加高光/边缘 |

由此确定编码：

```
stencil bits 7..6 = shading group (0..3)
stencil bits 5..0 = material id (0..63)
全屏 pass：Ref = group << 6，ReadMask = 0xC0，Comp = Equal
```

GBuffer 的 MRT 格式（P08）：`tex9503 RGBA8`（albedo）+ `tex9507 RGBA16F`（normal）+ `tex9511 R8`（mask），
DSV `tex9515 D32S8`，深度比较 `GREATER_EQUAL`（反向 Z），P08 开头清空 depth+stencil。
P35 的 HDR 目标 `tex5279 R11G11B10`，全屏 pass 只写 RGB（blend 210，writeMask=RGB）。

## 2. 实现结构

```
Runtime/
  SRCharacterFeature.cs          RendererFeature：按顺序 enqueue 4 个 pass
  SRCharacterSettings.cs         资源/开关/光照参数
  SRCharacterStencil.cs          模板编码（与抓帧一致，含断言用的 Preset）
  SRCharacterShaderIDs.cs        属性/全局纹理 ID
  SRCharacterRenderer.cs         按 area/group/id 写材质属性与 head 轴，驱动关键字
  Passes/
    SRCharacterGBufferPass.cs    3 MRT + 相机 depth/stencil，清 color 不清深度
    SRCharacterOutlinePass.cs    同一 MRT，front-face cull，背面模板 REPLACE
    SRCharacterShadingPass.cs    4 个全屏三角形，按 group 模板门控
    SRCharacterOverlayPass.cs    加法前向层（高光/边缘/自发光）
Shaders/
  SRCharacter.shader             几何阶段全部 pass：GBuffer / Outline / Overlay / ShadowCaster / DepthOnly / DepthNormals
  SRCharacterShading.shader      全屏 toon 着色（每 group 一个材质实例）
  SRCharacterCommon.hlsl         材质 CBUFFER、法线、面部 SDF
  SRCharacterDefines.hlsl        模板/area 编码（HLSL 侧）
Editor/
  SRCharacterShaderGUI.cs        由 group/id 自动计算 `_SRStencilRef`、切换 `_SR_AREA_FACE`
  SRCharacterVerify.cs           批处理验证入口（编译检查 + 胶囊体冒烟渲染）
analysis/
  extract_stencil_refs.py        从 RenderDoc XML 提取逐 draw 的 StencilRef
validation/
  verify.log                     最近一次验证日志（exit=0）
  smoke_render.png               冒烟渲染结果（胶囊体 + 对照立方体）
```

### 2.1 Pass 顺序（与抓帧对应）

| 顺序 | 本实现 | 抓帧 | 目标 | 状态要点 |
|---|---|---|---|---|
| 1 | `SRCharacterGBuffer` | P08 角色段 | albedo/normal/mask + 相机 depth | `Stencil Ref[_SRStencilRef] Comp Always Pass Replace`，`ZWrite On, ZTest LEqual, Cull[_Cull]` |
| 2 | `SRCharacterOutline` | P08 eid 3194/3366 | 同上 | `Cull Front`，背面 REPLACE，法线外扩（FOV/距离补偿） |
| 3 | `SRCharacterShading` ×4 | P35 eid 4906/4924/4948/4956 | 相机 color + depth | `Ref = group<<6, ReadMask 192, Comp Equal`，`ZTest Always, ZWrite Off, ColorMask RGB` |
| 4 | `SRCharacterOverlay` | P35 eid 5083/5096 | 相机 color + depth | `Blend One One, ZWrite Off, ZTest LEqual` |

所有 pass 统一 `RenderPassEvent.AfterRenderingOpaques`（同事件按 enqueue 顺序执行），
保证角色深度测试能用到场景深度、主光阴影已解析。

### 2.2 G-Buffer 载荷（对抓帧的工程化取舍）

| 目标 | 格式 | 内容 |
|---|---|---|
| `_SRCharacterAlbedo` | RGBA8 UNorm | rgb = 线性 albedo；**a = (area<<6 \| materialId)/255** |
| `_SRCharacterNormal` | RGBA16F | rgb = world normal；**a = device depth**（全屏 pass 用它反推 world pos） |
| `_SRCharacterMask` | RGBA8 UNorm（抓帧是 R8） | R = 角色剪影（=1，供 bloom）；G = ILM.g 阴影形状；A = 面部 SDF lit（GBuffer 阶段算好） |

两处有意的偏差，原因写在代码注释里：

1. **mask 从 R8 扩到 RGBA8**：全屏 pass 没有 UV，无法再采样角色的 ILM 贴图；把阴影形状（G）和
   面部 SDF 结果（A）在 GBuffer 阶段写进 mask，是全屏阶段还能逐像素分材质着色的最小代价。
2. **面部 SDF 在 GBuffer 里求值**：SDF 需要面部 UV 与头骨坐标轴；GBuffer pass 有 UV，且同帧光源方向不变，
   所以在此计算并存 A 通道。这样全屏 pass 仍是"无 UV 的延迟着色"，同时保留 SDF 的逐像素精度。

`object-space area`（body/face/hair）同时用于：剪影（R）、全屏分支（A/G）、`_SR_AREA_FACE` 关键字。

### 2.3 自定义 lighting model

着色被拆成两段（与 P35 的结构一致）：

- **全屏 diffuse（`SRCharacterShading.shader`）**
  ```hlsl
  remappedNoL = saturate(NdotL * 0.5 + 0.5);
  lit = smoothstep(1 - mask.g - soft, 1 - mask.g + soft, remappedNoL);   // body/hair
  lit = area == FACE ? mask.a : lit;                                     // face SDF
  lit *= lerp(1, shadowAtten, strength);
  diffuse = albedo * lerp(rampTint(remappedNoL, id), 1, lit);
  ambient = SampleSH(N) * albedo * _SRIndirectIntensity;
  ```
  ramp 为 256×16 的 2D 贴图，V 由 `(materialId+0.5)/16` 选行；组 3（头发）使用独立的 hair ramp 对。
  参考 [HonkaiStarRailToonShader](https://github.com/Gaolingx/HonkaiStarRailToonShader)（MIT）的
  cool/warm ramp + shadow shape 思路，重新实现。

- **加法高光/边缘（`SRCharacterOverlay`，几何 pass）**
  Blinn-Phong 阈值化高光（`_SRSpecThreshold/_SRSpecRoughness` × ILM.r）+
  Fresnel 边缘光（`_SRRimIntensity/_SRRimWidth`）+ 自发光；主光阴影与 NdotL 参与衰减。

第一轮抓帧里，P35 全屏 pass 之后确实还有角色几何的加法叠加（eid 5083/5096，blend=ONE/ONE），
本实现把"漫反射/面部阴影"留在全屏、"高光/边缘/自发光"放到叠加层，这是与证据一致的分工。

### 2.4 描边

`SRCharacter/Character` 的 `SRCharacterOutline` pass：

- `Cull Front`、`ZWrite On`、`ZTest LEqual`、背面 stencil REPLACE（Ref 同材质）；
- 顶点沿法线（或切线，`_OUTLINE_NORMAL_TANGENT`）在视图空间外扩，宽度按
  `unity_CameraProjection._m11`（FOV）与视距补偿，`COLOR.a` 可逐顶点缩放（游戏模型用法）；
- 输出写回同一套 MRT：albedo 用 `_OutlineColor`，mask.G = 1（避免 ramp 再给描边染色），
  因此描边像素也会被全屏 pass 正常着色，和抓帧中"描边与 GBuffer 共用目标"一致。

## 3. 使用步骤

1. 把 `02-SRCharacterShaderReserve` 整个目录复制到 `Assets/` 下（例如 `Assets/SRCharacterShaderReserve`）。
   Unity 会为新文件生成 `.meta`；本目录刻意不带 `meta`（GUID 不可复用）。
2. 在 URP Renderer Data 上 **Add Renderer Feature → SRCharacter Feature**；
   在 Inspector 里指定 `Character Shader = SRCharacter/Character`、`Shading Shader = SRCharacter/Shading`（留空时会 `Shader.Find`）。
3. **URP Asset 的 MSAA 必须为 1**：G-Buffer MRT 是单采样，D3D11 不允许与 MSAA 深度附件混绑；
   feature 检测到 MSAA>1 时会打印一次警告并整体跳过。
4. 给角色部件建材质（`SRCharacter/Character`），按部件设置：
   - `_SRArea`：1 body / 2 face / 3 hair；
   - `_SRShadingGroup`：0 普通、3 头发（对应抓帧 0xE0）；`_SRMaterialID`：ramp 行号（0..63）；
   - 自定义 Inspector 会据此自动写 `_SRStencilRef = group<<6 | id`（0x10 / 0x1F / 0xE0 即默认组合）。
5. 在角色根节点挂 `SRCharacterRenderer`，设置同样的 area/group/id，并把 `Head` 指向头骨
   （面部 SDF 需要头骨的 right/forward/up）。
6. 在 Feature 里指定 ramp 贴图：`Ramp Cool/Warm` 与 `Hair Ramp Cool/Warm`，均为 256×16，
   V 方向 16 行对应材质 ID；不指定时采样白图（结果=纯 albedo）。
7. 角色材质应只被本 feature 绘制：shader 没有 `UniversalForward`，所以 URP 的前向 pass 不会画它；
   `ShadowCaster/DepthOnly/DepthNormals` 已提供，URP 阴影与 SSAO 正常。

## 4. 验证

命令（本机）：

```bash
"D:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath E:/GitHub/Glx.CustomURP/URPSample01 \
  -executeMethod SRCharacter.SRCharacterVerify.Run \
  -logFile <repo>/URPSample01/tmp/02-SRCharacterShaderReserve/validation/verify.log
```

验证内容：

1. 模板编码断言（`MakeRef(0,16)==0x10`、`MakeRef(0,31)==0x1F`、`MakeRef(3,32)==0xE0`、payload 打包）；
2. 两个 shader 强制导入 + `ShaderUtil.ShaderHasError`；
3. 冒烟渲染：把**克隆的 renderer data**（挂上 feature）临时换进当前 URP asset，
   新建相机（`SolidColor` 深蓝背景）+ 主光 + 胶囊体（角色材质）+ 绿色 URP/Lit 立方体，
   通过 `RenderPipeline.SubmitRenderRequest` 渲染 256×256（`Camera.Render()` 在 SRP 下会走内置管线），
   读回像素计数；随后关闭 feature / 关闭 overlay 再各渲染一次。finally 里恢复原 renderer data 与 MSAA。

最近一次结果（完整日志：[validation/verify.log](validation/verify.log)）：

```
[SRCharacterVerify] SRCharacter/Character: 0 compiler message(s)
[SRCharacterVerify] SRCharacter/Shading: 0 compiler message(s)
[SRCharacterVerify] swapped renderer data; active renderer features = 1, currentPipeline=UniversalRenderPipeline
[SRCharacterVerify] pipeline=UniversalRenderPipeline, background=RGBA(0, 0, 64, 255),
                     baseline green pixels=1296, character pixels=5608
[SRCharacterVerify] ok: stock URP lit object rendered (pipeline works)
[SRCharacterVerify] ok: character pixels rendered by the SRCharacter feature
[SRCharacterVerify] character pixels without overlay = 5608
[SRCharacterVerify] ok: fullscreen stencil shading renders without the additive overlay
[SRCharacterVerify] character pixels with feature disabled = 0
[SRCharacterVerify] ok: no character pixels without the feature
[SRCharacterVerify] finished, errors=0
```

渲染结果：[validation/smoke_render.png](validation/smoke_render.png)（胶囊体为粉肤色 + 高光点，
左侧可见深色描边；右侧绿色立方体为 URP/Lit 对照）。

## 5. 已知差异与限制

| 项 | 抓帧 | 本实现 | 说明 |
|---|---|---|---|
| GPU 蒙皮 / Stream-Out | P00–P02（VS+GS+SO，骨骼 SRV） | 用 Unity `SkinnedMeshRenderer` | 重建 Stream-Out 会牵动剔除/indirect args，超出本轮范围 |
| 屏幕空间阴影 | P27（R8 tex9580，P35 ps0 输入） | 未重建 | 全屏 diffuse 只乘主光 shadow；P27 可作为下一步接入 `_SRShading` |
| 角色遮罩 | P36（R8 tex5288，供 bloom） | 导出 `_SRCharacterMask` 全局（G-Buffer mask R=剪影） | bloom/tonemap（P37–P56）未重建 |
| G-Buffer mask | R8（二值剪影） | RGBA8（R 剪影，G 阴影形状，A 面部 lit） | 见 2.2 |
| 全屏着色输入 | 采样深度拷贝 tex514 | 深度存 normal.a，避免同 pass 同时绑 DSV 又采样 | 语义等价 |
| 模板清理 | P08 开头清 depth+stencil | 只清 color，保留相机 depth | 需要模板在 `AfterRenderingOpaques` 时为 0；若其它 feature 写模板需额外 clear |
| 附加光源 | — | 仅主光 + SH | 全屏 pass 没有 per-object 光列表 |
| 雾 / 烘焙 GI / 光 Cookie | P29 等 | 未实现 | |
| ramp / 材质参数 | per-character LUT | feature 级全局 ramp + 材质属性 | 多角色不同配色时需要扩展为 per-character 表 |
| 面部 SDF 通道 | FaceMap.a（参考实现约定） | 同样用 A | 参考 StarRailNPRShader / HonkaiStarRailToonShader 的约定 |

### 与抓帧的模板差异

- 抓帧中全屏 pass 用 `Pass=ZERO / WriteMask=1` 消费 bit0（防止重叠部件重复着色）；本实现的 GBuffer
  已经用深度解决了重复覆盖，因此全屏 pass 用 `Pass=Keep`，只保留"group + id"两组位域。
- 抓帧的 0xE0 头发组由第 1 个全屏 pass（ref 192）着色；本实现组 3 同时也是 hair ramp 的选择依据。

## 6. 下一轮建议

1. 接入 P27 屏幕空间阴影：把 R8 mask 作为 `_SRScreenSpaceShadowMap` 在 diffuse 阶段乘入。
2. 用 P36 的角色遮罩 + 自定义 bloom（参考 StarRailNPRShader 的 CustomBloom）补齐后处理链。
3. 把全局 ramp 扩成 per-character LUT（8×N 浮点纹理 + material id 索引），对齐抓帧的 8×8 RGBA16F 小图。
4. 若需要在正式场景使用：恢复 P08 的 GPU 蒙皮预计算（VS+GS Stream-Out 或 Compute），
   避免 SkinnedMeshRenderer 的 CPU 蒙皮开销。