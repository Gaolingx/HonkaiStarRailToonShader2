# 02-SRCharacterShaderReserve — 星穹铁道角色渲染重建（第二轮）

在 URP 14.0.12 / Unity 2022.3.62f1 下，按 [01-SRCharacter](../01-SRCharacter/README.md) 的抓帧分析重建 HSR 角色渲染的 **多 pass 管线**：
G-Buffer（MRT + 模板材质 ID）→ 描边（front-face cull）→ 全屏分组模板着色 →
眼睛遮挡（stencil bit0 标记 + 加法高光）→ 加法前向叠加 → 半透明刘海（alpha 混合 + 第一层去重）。

本目录不是 Unity 工程的一部分（在 `Assets` 之外），导入方式见 [使用步骤](#使用步骤)。

> 本轮（2026-11-20）的完整验收矩阵（attachment / CB / 算法 / 逐像素口径 / stencil / 深度）
> 见仓库根 [README.md §3](../../README.md#3-本轮验收)。

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
| P35 全屏 | 4956 | 3 idx 全屏 | **0** | 662：ReadMask 0xC0，EQUAL，不写 | 组 0（身体皮肤，像素历史在躯干皮肤上证实） |
| P35 前向 | 5083/5096 | 角色几何 | 16 | 817：stencil 实际 ALWAYS、ZWrite Off、**blend 208 = ONE/ONE 加法** | 叠加高光/边缘 |
| P35 眼睛标记 | 4968/4999 | 240 idx（眼睛 proxy：42 顶点 / 80 三角形） | 1 | 1303：depth GREATER_EQUAL，**ZFail=Replace**，WriteMask 1，ColorMask 0 | 眼睛表面被刘海/眼皮挡住时置 **bit0=1** |
| P35 眼睛高光 | 4989/5015 | 同一 proxy 的背面（Cull Front） | 128 | 1305：depth LESS_EQUAL，ReadMask **0x81**，WriteMask 1，Pass/Fail/ZFail=Zero，blend 204 (ONE/ONE RGB) | bit7=1 且 bit0=0 时才加法着色，并清 bit0 |
| P35 透明刘海 | 5349/5361/5404/5585/5595 | 1056 idx ×5（VB 41839） | 0/24/10/11/11 | blend 213 (SrcAlpha/OneMinusSrcAlpha)、`NOT_EQUAL`→Replace、描边 Cull Front + ZWrite On | 半透明发片：不进 GBuffer，stencil 做第一层去重 |

由此确定编码：

```
stencil bits 7..6 = shading group (0..3)
stencil bits 5..0 = material id (0..63)
bit0            = 临时标志（眼睛遮挡；不是材质 ID 的一部分）
全屏 pass：Ref = group << 6，ReadMask = 0xC0，Comp = Equal
组 1..3 的 pass 以 WriteMask 1 + Pass/Fail=Zero 清 bit0（DSS 655）；组 0 不清（DSS 662）
眼睛高光：Ref = 0x80，ReadMask = 0x81，Comp = Equal，所有 ops = Zero
```

GBuffer 的 MRT 格式（P08）：`tex9503 RGBA8`（albedo）+ `tex9507 RGBA16F`（normal）+ `tex9511 R8`（mask），
DSV `tex9515 D32S8`，深度比较 `GREATER_EQUAL`（反向 Z），P08 开头清空 depth+stencil。
P35 的 HDR 目标 `tex5279 R11G11B10`，全屏 pass 只写 RGB（blend 210，writeMask=RGB）。

## 2. 实现结构

```
Runtime/
  SRCharacterFeature.cs          RendererFeature：按顺序 enqueue 8 个 pass
  SRCharacterSettings.cs         资源/开关/光照参数
  SRCharacterStencil.cs          模板编码（与抓帧一致，含 bit0 标志与断言用的 Preset）
  SRCharacterShaderIDs.cs        属性/全局纹理 ID
  SRCharacterRenderer.cs         按 area/group/id 写材质属性、eye proxy 标志与 head 轴
  Passes/
    SRCharacterGBufferPass.cs    3 MRT + 相机 depth/stencil，清 color 不清深度
    SRCharacterOutlinePass.cs    同一 MRT，front-face cull，背面模板 REPLACE
    SRCharacterHairDepthPass.cs  前发深度 prepass（参考实现）：半分辨率 R32F 线性深度，MIN 混合
    SRCharacterShadingPass.cs    4 个全屏三角形，按 group 模板门控，组 1..3 清 bit0
    SRCharacterEyePasses.cs      眼睛遮挡标记 + 加法眼睛高光（bit0）
    SRCharacterOverlayPass.cs    加法前向层（高光/边缘/自发光）
    SRCharacterTransparentPass.cs 半透明刘海（alpha 混合 + 第一层去重 + 描边）
Shaders/
  SRCharacter.shader             几何阶段全部 pass：GBuffer / Outline / EyeMark / EyeShade / Overlay / ShadowCaster / DepthOnly / DepthNormals
  SRCharacterTransparent.shader  半透明刘海：fill（stencil 去重）+ 背面描边
  SRCharacterShading.shader      全屏 toon 着色（每 group 一个材质实例）
  SRCharacterCommon.hlsl         材质 CBUFFER、法线、面部 SDF
  SRCharacterDefines.hlsl        模板/area/bit0 编码（HLSL 侧）
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
| 3 | `SRCharacterHairDepth` | 参考实现（抓帧无） | `_SRHairDepthTexture` R32F（半分辨率） | 只画 hair/bangs；线性深度 + `MIN` 混合；供 FACE 偏移采样 |
| 4 | `SRCharacterShading` ×4 | P35 eid 4906/4924/4948/4956 | 相机 color + depth | `Ref = group<<6, ReadMask 192, Comp Equal`，组 1..3 `WriteMask 1 / Pass=Zero` 清 bit0 |
| 5 | `SRCharacterEyeMark` | P35 eid 4968/4999 | 相机 color（ColorMask 0）+ depth | `Ref 1, WriteMask 1, Comp Always, ZFail Replace`，`ZTest LEqual`（=抓帧 GREATER_EQUAL） |
| 6 | `SRCharacterEyeShade` | P35 eid 4989/5015 | 相机 color + depth | `Ref = _SRStencilRef, ReadMask 129, Comp Equal, Pass/Fail/ZFail Zero`，`Blend One One`，`Cull Front` |
| 7 | `SRCharacterOverlay` | P35 eid 5083/5096 | 相机 color + depth | `Blend One One, ZWrite Off, ZTest LEqual` |
| 8 | `SRCharacterTransparent` | P35 eid 5349/5361/5404/5585/5595 | 相机 color + depth | `Blend SrcAlpha OneMinusSrcAlpha`，`Comp NotEqual, Pass Replace`，背面描边 `Cull Front, ZWrite On` |

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
  // 参考实现：前发阴影（抓帧无对应，见 2.7）
  if (area == FACE) lit *= lerp(1, GetFrontHairShadow(positionCS, lightDirWS), _SRHairShadowStrength);
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

### 2.5 眼睛遮挡（stencil bit0）

抓帧 P35 在材质 pass 之后有两次 240-index draw（眼睛 proxy，42 顶点 / 80 三角形），用 bit0 传递
"这个像素的眼睛表面是否被挡住"：

| 本实现 | 抓帧 | 状态 | 说明 |
|---|---|---|---|
| `SRCharacterEyeMark` | 4968/4999（DSS 1303） | `Ref 1, ReadMask 255, WriteMask 1, Comp Always, ZFail Replace`，`ColorMask 0`，`ZTest LEqual` | 用与 GBuffer 相同的网格/变换重画 proxy；深度测试失败（刘海/眼皮在前）→ bit0=1 |
| `SRCharacterEyeShade` | 4989/5015（DSS 1305） | `Ref [_SRStencilRef], ReadMask 129, Comp Equal, Pass/Fail/ZFail Zero`，`Blend One One`，`Cull Front` | bit7=1 且 bit0=0 才加色；成功后把 bit0 归零 |

- **proxy 约定**：眼睛部件挂 `SRCharacterRenderer` 并勾选 `Eye Proxy`（材质 `_SREyeProxy = 1`）。
  抓帧的 `ReadMask 0x81` 只比较 bit7 + bit0，因此眼睛部件所在材质类必须满足 bit7=1（组 2/3）；
  本实现默认要求眼睛与脸部材质都用 group 2（`Ref 0x80`）。组 0 在抓帧里是身体皮肤
  （像素历史证实），不参与眼睛高光。GBuffer/Outline/全屏着色都正常处理 proxy，
  只有两个 eye pass 会额外重画它。
- **bit0 生命周期**：GBuffer 写材质 ID 时 bit0=0；组 1..3 的全屏 pass 再清一次（镜像 DSS 655）；
  眼睛标记置位；眼睛高光消费并清零。组 0 的全屏 pass 不写（镜像 DSS 662）。
- 着色载荷用屏幕空间 GBuffer（albedo / normal / 深度）＋主光，复现抓帧 PS 9584 的“屏幕空间眼睛着色”结构；
  其常量与虹膜折射公式未解码，参数以材质属性（`_SREyeColor/SpecIntensity/Shininess/FresnelPower/RimIntensity`）表达。

### 2.6 半透明刘海

抓帧中 VB 41839（219 顶点 / 352 三角形）只在 P16 阴影和 P35 出现，**从不进入 GBuffer**：

- 填色：`Blend SrcAlpha OneMinusSrcAlpha`、`ZTest LEqual`、`ZWrite Off`，
  `Stencil { Ref [_SRStencilRef], Comp NotEqual, Pass Replace }`——同一像素只有第一层发片通过，
  复现抓帧 5585/5595 的 `NOT_EQUAL → REPLACE` 去重（5361 是同一机制的 `Always → Replace` 变体）。
- 描边：`Cull Front` + `ZWrite On` + 背面模板 Replace（抓帧 5404/5595）。
- 着色：toon ramp + ILM 阴影形状 + 边缘光；alpha = `_BaseMap.a * _BaseColor.a * _SRAlpha * vertexColor.a`
  （抓帧 PS 15531 输出 alpha = 软边 × 顶点 alpha，即发梢淡出）。
- 由 `SRCharacterTransparentPass` 在 `AfterRenderingOpaques` 末尾用 `RenderQueueRange.transparent`
  绘制（两个 ShaderTagId 分别对应 fill / outline）；URP 的透明队列不会重复绘制它。

### 2.7 前发阴影（HairDepth prepass，参考实现）

抓帧里 **没有**这个机制：脸部是模板组 0，由 `eid 4956`（PS 661）直出 albedo；tex9580 只被
`ps 9571`（Ref 192，头发）和 `ps 9572`（Ref 128）采样，P27 的每物体投影又限定 Ref 192。
因此抓帧的“刘海暗部”来自半透明刘海几何本身，而不是一张投到脸上的阴影图。

若需要真正的“刘海在脸上投影”，本实现采用 [StarRailNPRShader](https://github.com/stalomeow/StarRailNPRShader)
的 HairDepthOnly + `GetHairShadow` 方案（GPL-3.0，仅参考算法，未复制代码）：

1. `SRCharacterHairDepthPass` 在 Shading 之前把 hair/bangs 画进半分辨率 `_SRHairDepthTexture`（R32F，
   `linear01 = -viewZ / _ProjectionParams.z`，`BlendOp Min` 保留最近发丝，无需深度附件）；
2. 两个 shader 各有 `SRCharacterHairDepth` pass：Character 用 `clip(_SRArea - 2.5)` 只放行组 3，
   Transparent 用 `_SRAlpha` 做 alpha clip，保证半透明刘海也参与；
3. `SRCharacterShading.shader` 的 `SRFrontHairShadow` 沿**视空间光向**偏移屏幕 UV：
   `width = distance*0.04 * (proj._m11/2.414) * (modelScale/eyeDepth) * (log10(screenHeight)/3.33)`，
   比较发丝深度与脸部深度，`step(faceDepth, hairDepth)`；
4. 仅在 `area == FACE` 时乘入 `lit`，强度/距离/深度偏置/模型缩放由 Feature 暴露。

验证（`smoke_hairshadow.png`）：横向主光下，刘海 quad 的阴影落在**光源反侧**；距离 3→6 时
变暗像素质心 X 从 110.7 移到 112.8、最右像素 121→125。

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
   - 眼睛 proxy 部件同样挂 `SRCharacterRenderer`，勾选 **Eye Proxy**，area 设为 Face、group 设为 2；
   - 半透明刘海用 `SRCharacter/Transparent` 材质（`_SRAlpha` 控制整体透明度），放在同一 Layer 上即可；
     Feature 会从透明队列单独绘制它，不需要 URP 的前向 pass。
6. 在 Feature 里指定 ramp 贴图：`Ramp Cool/Warm` 与 `Hair Ramp Cool/Warm`，均为 256×16，
   V 方向 16 行对应材质 ID；不指定时采样白图（结果=纯 albedo）。
7. 角色材质应只被本 feature 绘制：shader 没有 `UniversalForward`，所以 URP 的前向 pass 不会画它；
   `ShadowCaster/DepthOnly/DepthNormals` 已提供，URP 阴影与 SSAO 正常。
8. 前发阴影（参考实现，默认开启）：Feature 里设置 `Enable Front Hair Shadow`、
   `Hair Shadow Distance`（默认 0.2）、`Strength`、`Depth Bias`、`Hair Depth Downscale` 与 `Model Scale`；
   头发/刘海材质必须是组 3（`_SRArea = 3`），脸部材质 `_SRArea = 2` 才会采样。

## 4. 验证

命令（本机，仓库本身不含 Unity 工程，需把本模块临时复制到宿主工程 `Assets/` 下）：

```bash
# 1) 复制到宿主工程
cp -r Projects/02-SRCharacterShaderReserve/{Runtime,Shaders,Editor} \
      E:/GitHub/Glx.CustomURP/URPSample01/Assets/_SRCharacterVerify/
# 2) 批处理验证
"D:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath E:/GitHub/Glx.CustomURP/URPSample01 \
  -executeMethod SRCharacter.SRCharacterVerify.Run \
  -logFile Projects/02-SRCharacterShaderReserve/validation/verify.log
# 3) 验证结束后删除 Assets/_SRCharacterVerify（连同 .meta）
```

验证内容：

1. 模板编码断言（`MakeRef(0,16)==0x10`、`MakeRef(0,31)==0x1F`、`MakeRef(3,32)==0xE0`、
   `EyeReadMask==0x81`、bit0 不与 group 位重叠、payload 打包）；
2. 三个 shader（Character / Shading / Transparent）强制导入 + `ShaderUtil.ShaderHasError`；
3. 冒烟渲染：把**克隆的 renderer data**（挂上 feature）临时换进当前 URP asset，
   新建相机（`SolidColor` 深蓝背景）+ 主光 + 胶囊体（角色材质）+ 绿色 URP/Lit 立方体，
   通过 `RenderPipeline.SubmitRenderRequest` 渲染 256×256（`Camera.Render()` 在 SRP 下会走内置管线），
   读回像素计数；随后依次：
   关闭 overlay、加一个位于相机与角色之间的红色不透明遮挡物、
   加一片 50% alpha 的透明刘海 quad、加眼睛 proxy 球 + 位于其前方左半边的绿色刘海条、
   关闭 eye pass、关闭 feature 各渲染一次。finally 里恢复原 renderer data 与 MSAA。
4. 前发阴影：主光改为横向（+X）、胶囊体临时切到 FACE、放一个小刘海 quad（Transparent / 组 3）；
   对比 `Enable Front Hair Shadow` 开/关的像素差，断言阴影落在**光源反侧**，再用距离 3/6
   验证变暗区域随距离外移。

最近一次结果（完整日志：[validation/verify.log](validation/verify.log)）：

```
[SRCharacterVerify] SRCharacter/Character: 0 compiler message(s)
[SRCharacterVerify] SRCharacter/Shading: 0 compiler message(s)
[SRCharacterVerify] SRCharacter/Transparent: 0 compiler message(s)
[SRCharacterVerify] swapped renderer data; active renderer features = 1, currentPipeline=UniversalRenderPipeline
[SRCharacterVerify] pipeline=UniversalRenderPipeline, background=RGBA(0, 0, 64, 255),
                     baseline green pixels=1296, character pixels=5608, capsule center=RGBA(230, 178, 178, 255)
[SRCharacterVerify] ok: stock URP lit object rendered (pipeline works)
[SRCharacterVerify] ok: character pixels rendered by the SRCharacter feature
[SRCharacterVerify] character pixels without overlay = 5608
[SRCharacterVerify] ok: fullscreen stencil shading renders without the additive overlay
[SRCharacterVerify] occluded center=RGBA(218, 36, 36, 255), character pixels with occluder=24
[SRCharacterVerify] ok: occluder wins the depth test at the screen center
[SRCharacterVerify] ok: character pixels are reduced behind an opaque occluder
[SRCharacterVerify] transparent bangs center=RGBA(243, 132, 132, 191) (capsule center was RGBA(230, 178, 178, 255))
[SRCharacterVerify] ok: semi-transparent bangs alpha-blend over the shaded character
[SRCharacterVerify] eye overlay: visible delta=249 (off=RGBA(90, 90, 90, 255), on=RGBA(173, 173, 173, 255)),
                     covered delta=0 (off=RGBA(50, 153, 50, 255), on=RGBA(50, 153, 50, 255))
[SRCharacterVerify] ok: eye overlay adds a highlight on visible eye pixels
[SRCharacterVerify] ok: eye overlay is skipped where stencil bit0 marks bangs occlusion
[SRCharacterVerify] front hair shadow: caster.x=104.2, near(d=3) count=813 centroidX=110.7 maxX=121, far(d=6) count=966 centroidX=112.8 maxX=125
[SRCharacterVerify] ok: front hair shadow darkens face pixels
[SRCharacterVerify] ok: front hair shadow lands on the side opposite the light
[SRCharacterVerify] ok: a larger hair shadow distance pushes the shadow further from the caster
[SRCharacterVerify] character pixels with feature disabled = 0
[SRCharacterVerify] ok: no character pixels without the feature
[SRCharacterVerify] finished, errors=0
```

遮挡测试同时验证了三点：feature 确实在 opaque 之后执行、G-Buffer 对场景深度做 `ZTest LEqual`、
全屏着色不会把被遮挡的像素重新画出来（角色像素 5608 → 24，剩余为左侧描边边缘）。

新增的三个行为测试：

- **半透明刘海**：50% alpha 的红色 quad 叠在已着色的胶囊上，中心像素从 `(230,178,178)` 变为
  `(243,132,132)`（红升、绿降），说明它走的是 alpha 混合而不是覆盖；
- **眼睛遮挡（bit0）**：眼睛 proxy 中心可见的一侧亮度 90→173（加法高光生效），
  被绿色刘海条挡住的另一侧保持 `(50,153,50)` 不变——proxy 的深度测试失败置了 bit0，
  高光 pass 因此跳过。注意该测试里刘海条本身也是 group 2 的角色材质，
  所以“组位相等”成立，唯一能挡住加法的是 bit0，能区分 bit0 是否生效；
- **前发阴影（参考实现）**：横向主光 + 小刘海 quad；开/关前置深度后做像素差，
  变暗像素质心在刘海**右侧**（光源反侧），距离 3→6 时质心 110.7→112.8、最右 121→125。

渲染结果：[validation/smoke_render.png](validation/smoke_render.png)（胶囊体为粉肤色 + 高光点，
左侧可见深色描边；右侧绿色立方体为 URP/Lit 对照）、
[smoke_transparent.png](validation/smoke_transparent.png)、
[smoke_eye.png](validation/smoke_eye.png)、
[smoke_hairshadow.png](validation/smoke_hairshadow.png)（红色为刘海 quad，右侧暗带为投影）。

## 5. 已知差异与限制

| 项 | 抓帧 | 本实现 | 说明 |
|---|---|---|---|
| GPU 蒙皮 / Stream-Out | P00–P02（VS+GS+SO，骨骼 SRV） | 用 Unity `SkinnedMeshRenderer` | 重建 Stream-Out 会牵动剔除/indirect args，超出本轮范围 |
| 屏幕空间阴影 | P27（R8 tex9580） | 未重建 | 实测：tex9580 只被 P35 ps9571(Ref 192，头发)/ps9572(Ref 128) 采样；每物体投影限定 Ref 192；脸部组 0（ps661）只读 albedo，抓帧皮肤像素全 255。**与刘海阴影无关** |
| 前发阴影（刘海投脸） | 抓帧无此机制 | HairDepth prepass + FACE 偏移采样 | REFERENCE-BASED（StarRailNPRShader 算法）；参数 `_SRHairShadowDistance/_SRHairShadowStrength/_SRHairShadowDepthBias/_SRModelScale` |
| 角色遮罩 | P36（R8 tex5288，供 bloom） | 导出 `_SRCharacterMask` 全局（G-Buffer mask R=剪影） | bloom/tonemap（P37–P56）未重建 |
| G-Buffer mask | R8（二值剪影） | RGBA8（R 剪影，G 阴影形状，A 面部 lit） | 见 2.2 |
| 全屏着色输入 | 采样深度拷贝 tex514 | 深度存 normal.a，避免同 pass 同时绑 DSV 又采样 | 语义等价 |
| 模板清理 | P08 开头清 depth+stencil | 只清 color，保留相机 depth | 需要模板在 `AfterRenderingOpaques` 时为 0；若其它 feature 写模板需额外 clear |
| 附加光源 | — | 仅主光 + SH | 全屏 pass 没有 per-object 光列表 |
| 雾 / 烘焙 GI / 光 Cookie | P29 等 | 未实现 | |
| ramp / 材质参数 | per-character LUT | feature 级全局 ramp + 材质属性 | 多角色不同配色时需要扩展为 per-character 表 |
| 面部 SDF 通道 | FaceMap.a（参考实现约定） | 同样用 A | 参考 StarRailNPRShader / HonkaiStarRailToonShader 的约定 |
| 眼睛 proxy | 42 顶点 / 80 三角形独立网格，P08 也画 | 由用户指定 proxy 部件；eye pass 重画同一网格 | 没有 proxy 时整条眼睛链路不产生额外像素 |
| 眼睛着色公式 | PS 9584：屏幕空间 + 虹膜折射（常量不可得） | 屏幕空间 specular + Fresnel 高光 | 结构与门控一致，具体数值不可比 |
| 透明刘海排序 | P35 内与其它前向/透明物体混合排序 | Feature 在 `AfterRenderingOpaques` 末尾先画 | 与 URP 透明队列的相对顺序不同；单角色场景无影响 |

### 与抓帧的模板差异

- 抓帧的 0xE0 头发组由第 1 个全屏 pass（ref 192）着色；本实现组 3 同时也是 hair ramp 的选择依据。
- 眼睛高光的 Ref 直接复用材质 `_SRStencilRef`（`ReadMask 0x81` 会忽略低 6 位），
  因此要求眼睛 proxy 与脸部同属 group 2；抓帧里同样是 `Ref 0x80`。
- 眼睛 proxy 用**网格重画**实现（与抓帧一致），没有做抓帧 PS 9584 的虹膜/折射常量还原；
  着色载荷是可配置的屏幕空间高光。抓帧的材质类型判断（`normal.a*65535 & 7 == 2` 时输出 0）
  在本实现中由 `_SREyeProxy` + GBuffer area 判断替代。
- 抓帧的透明刘海有 5 个 draw（0/24/10/11/11，其中 10 是描边、24 是 `Always→Replace` 变体）；
  本实现每个材质只产生 fill + outline 两个 draw，`_SRStencilRef` 由材质设置决定去重键。

## 6. 下一轮建议

1. 接入 P27：把 tex9580 作为 `_SRScreenSpaceShadowMap` 接到头发（组 3）与组 2 的着色；注意 P27 的角色投影只写头发组，脸部不读取。
2. 用 P36 的角色遮罩 + 自定义 bloom（参考 StarRailNPRShader 的 CustomBloom）补齐后处理链。
3. 把全局 ramp 扩成 per-character LUT（8×N 浮点纹理 + material id 索引），对齐抓帧的 8×8 RGBA16F 小图。
4. 若需要在正式场景使用：恢复 P08 的 GPU 蒙皮预计算（VS+GS Stream-Out 或 Compute），
   避免 SkinnedMeshRenderer 的 CPU 蒙皮开销。
5. 解码 PS 9584 / 15531 的 CB 常量后，把眼睛高光换成抓帧的虹膜折射公式，
   并给透明刘海补上抓帧的 3 段材质分件（ref 10/11/24）。