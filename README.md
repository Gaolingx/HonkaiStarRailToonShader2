# HonkaiStarRailToonShader2

基于 **RenderDoc 抓帧** 的《崩坏：星穹铁道》角色渲染逆向与 URP 14 重建仓库。

- 抓帧：`StarRail_2024.12.09_01.39_frame13063.rdc`（D3D11，3072×1920，622 actions / 581 draws，无 debug marker；抓帧文件不入库）
- 目标环境：Unity 2022.3.62f1 + URP 14.0.12（宿主工程 `Glx.CustomURP/URPSample01`）
- 本仓库只包含**分析文档 + 可导入的渲染模块**，不包含 Unity 工程、游戏模型与原始贴图。

## 当前状态（2026-11-20 验收）

| 项 | 结果 |
|---|---|
| 模块编译（2 个 shader + C#） | 0 错误，0 编译消息 |
| 冒烟渲染（胶囊体走完整 pass 链） | 通过：角色 5608 px，关闭 feature 后 0 px |
| 全屏模板着色独立性（关闭 overlay） | 通过：仍 5608 px |
| 深度遮挡（相机与角色之间放不透明物体） | 通过：屏幕中心被遮挡物占据，角色像素 5608 → 24 |
| 半透明刘海（50% alpha quad） | 通过：中心像素 `(230,178,178)` → `(243,132,132)`，红升绿降 |
| 眼睛遮挡 bit0（proxy + 前方刘海条） | 通过：可见侧亮度 90 → 173；被挡侧保持 `(50,153,50)` 不变 |
| 总体结论 | **PARTIAL**：角色核心 pass 链、眼睛 bit0 与半透明刘海已重建并验证；GPU 蒙皮、P27 屏幕空间阴影、P36 遮罩 + bloom 未移植 |

完整证据：[02-SRCharacterShaderReserve/validation/verify.log](Projects/02-SRCharacterShaderReserve/validation/verify.log)、
[smoke_render.png](Projects/02-SRCharacterShaderReserve/validation/smoke_render.png)、
[smoke_transparent.png](Projects/02-SRCharacterShaderReserve/validation/smoke_transparent.png)、
[smoke_eye.png](Projects/02-SRCharacterShaderReserve/validation/smoke_eye.png)。

## 仓库结构

```
Projects/
  01-SRCharacter/                 抓帧分析（第一轮）
    README.md                     索引与方法
    01-frame-pass-overview.md     57 个 pass 的事件区间 / RTV / DSV / 作用推断
    02-pass-bindings.md           逐 pass 的 RTV/DSV/SRV/CB/状态 + 逐 program 表
    03-resource-dependencies.md   pass 依赖图与关键 RT 生产者/消费者
    04-character-rendering.md     角色链路结论 + 待办（§9 含第二轮状态）
    raw/                          581 draw 索引、逐 pass JSON、XML↔事件号映射、状态对象
    img/                          关键 RT 导出（GBuffer/HDR/遮罩/SS 阴影）
    *.py                          XML/状态解析与文档生成脚本
  02-SRCharacterShaderReserve/    URP 重建模块（第二轮）
    README.md                     模块设计、证据映射、使用步骤、验证结果
    Runtime/                      RendererFeature + 7 个 Pass + 模板编码 + 材质设置组件
    Shaders/                      GBuffer/描边/眼睛/加法叠加/半透明刘海 + 全屏分组着色 + 公共 HLSL
    Editor/                       材质 Inspector + 批处理验证入口
    analysis/extract_stencil_refs.py
    validation/                   verify.log + smoke_render / smoke_transparent / smoke_eye.png
```

## 1. 抓帧证据摘要

第一轮的分析见 [01-SRCharacter](Projects/01-SRCharacter/README.md)。第二轮补充了**逐 draw 的 `StencilRef`**：
`renderdoccmd convert -c xml` 导出的 XML 中，`OMSetDepthStencilState` 块带有 StencilRef，
用 [extract_stencil_refs.py](Projects/02-SRCharacterShaderReserve/analysis/extract_stencil_refs.py) 提取后与
`raw/draws.csv` 按 `chunkIndex` 关联：

| 阶段 | draw（eid） | StencilRef | 关键状态 |
|---|---|---|---|
| P08 GBuffer 主体 | 961 | **0x10** | DSS 1288：W255 / REPLACE / GREATER_EQUAL |
| P08 GBuffer 第二套 | 1010 | **0x1F** | DSS 8527：W254 / REPLACE |
| P08 GBuffer 头发 | 2130/2208/… | **0xE0** | DSS 1288（组 3） |
| P08 描边 | 3194 / 3366 | 0x10 / 0x1F | DSS 1935 / 8545：背面 REPLACE，CULL_FRONT |
| P35 全屏 | 4906 / 4924 / 4948 / 4956 | **192 / 64 / 128 / 0** | DSS 655/662：ReadMask **0xC0**，EQUAL；组 1..3 `WriteMask 1 / Pass=Zero` |
| P35 眼睛标记 | 4968 / 4999 | 1 | DSS 1303：`ZFail=Replace`，WriteMask 1，ColorMask 0 |
| P35 眼睛高光 | 4989 / 5015 | 128 | DSS 1305：ReadMask **0x81**，WriteMask 1，Pass/Fail/ZFail=Zero，blend ONE/ONE |
| P35 前向叠加 | 5083 / 5096 | 16 | DSS 817：stencil ALWAYS、ZWrite Off、blend **208 = ONE/ONE** |
| P35 透明刘海 | 5349 / 5361 / 5404 / 5585 / 5595 | 0 / 24 / 10 / 11 / 11 | blend 213，`NOT_EQUAL→Replace`，描边 Cull Front + ZWrite On |

由此确定的编码（模块中 `SRCharacterStencil` 与 `SRCharacterDefines.hlsl` 一致实现）：

```
stencil bits 7..6 = shading group (0..3)
stencil bits 5..0 = material id (0..63)
bit0             = 临时标志（眼睛遮挡），不属于材质 ID
全屏 pass：Ref = group << 6，ReadMask = 0xC0，Comp = Equal
眼睛高光：Ref = 0x80，ReadMask = 0x81，Comp = Equal，所有 stencil op = Zero
```

深度：P08 的 DSV 为 `D32S8`，全部比较为 `GREATER_EQUAL`，清深度 0（反向 Z）。
P35 的全屏 pass 深度比较为 `ALWAYS` 且不写深度；前向叠加为 `GREATER_EQUAL` 且不写深度。

## 2. 重建管线

模块入口：[SRCharacterFeature.cs](Projects/02-SRCharacterShaderReserve/Runtime/SRCharacterFeature.cs)。
7 个 pass 统一在 `AfterRenderingOpaques` 按 enqueue 顺序执行：

| 顺序 | Pass | 目标 | 关键状态 | 对应抓帧 |
|---|---|---|---|---|
| 1 | `SRCharacterGBuffer` | `_SRCharacterAlbedo` RGBA8 + `_SRCharacterNormal` RGBA16F + `_SRCharacterMask` RGBA8 + 相机 depth | `Stencil Replace`，`ZWrite On / ZTest LEqual` | P08 |
| 2 | `SRCharacterOutline` | 同上（不清除） | `Cull Front`，背面 REPLACE，VS 法线外扩 | P08 eid 3194/3366 |
| 3 | `SRCharacterShading` ×4 | 相机 color + depth | `Ref=g<<6, ReadMask 192, Equal`，组 1..3 清 bit0 | P35 eid 4906/4924/4948/4956 |
| 4 | `SRCharacterEyeMark` | 相机 color（ColorMask 0）+ depth | `Ref 1 / WriteMask 1 / ZFail Replace` | P35 eid 4968/4999 |
| 5 | `SRCharacterEyeShade` | 相机 color + depth | `Ref=0x80, ReadMask 0x81, Equal, ops Zero`，`Blend One One`，`Cull Front` | P35 eid 4989/5015 |
| 6 | `SRCharacterOverlay` | 相机 color + depth | `Blend One One / ZWrite Off / ZTest LEqual` | P35 eid 5083/5096 |
| 7 | `SRCharacterTransparent` | 相机 color + depth | `Blend SrcAlpha OneMinusSrcAlpha`，`Comp NotEqual / Pass Replace`，背面描边 ZWrite | P35 eid 5349/5361/5404/5585/5595 |

G-Buffer 载荷：

| 目标 | 内容 |
|---|---|
| albedo RGBA8 | rgb=线性 albedo；**a=(area<<6 \| materialId)/255** |
| normal RGBA16F | rgb=world normal；**a=device depth**（全屏 pass 反推 world pos） |
| mask RGBA8 | R=角色剪影；G=ILM.g 阴影形状；A=面部 SDF lit（GBuffer 阶段算好） |

## 3. 本轮验收

对应用户提出的 5 个检查维度。判定用 **PASS / PASS\* / PARTIAL / NOT PORTED**（PASS\* = 语义一致但实现载体不同）。

### 3.1 Attachment 绑定

| 抓帧资源 | 抓帧格式/用途 | 本实现 | 判定 |
|---|---|---|---|
| P08 tex9503 | RGBA8 albedo | `_SRCharacterAlbedo` RGBA8（[GBufferPass:41-42](Projects/02-SRCharacterShaderReserve/Runtime/Passes/SRCharacterGBufferPass.cs#L41)） | PASS（alpha 语义改为材质载荷） |
| P08 tex9507 | RGBA16F normal | `_SRCharacterNormal` RGBA16F（[:44-45](Projects/02-SRCharacterShaderReserve/Runtime/Passes/SRCharacterGBufferPass.cs#L44)） | PASS\*（alpha 存 device depth） |
| P08 tex9511 | R8 mask | `_SRCharacterMask` RGBA8（[:47-48](Projects/02-SRCharacterShaderReserve/Runtime/Passes/SRCharacterGBufferPass.cs#L47)） | PARTIAL（扩到 RGBA8 以传阴影形状/SDF） |
| P08 tex9515 | D32S8 depth/stencil | 相机 depth（URP 为 D24S8） | PARTIAL（stencil 可用；深度 24 位） |
| P08 clear | color + depth + stencil | 只清 color（[GBufferPass:53](Projects/02-SRCharacterShaderReserve/Runtime/Passes/SRCharacterGBufferPass.cs#L53)） | PARTIAL（不破坏场景深度；依赖 stencil 初值为 0） |
| P35 tex5279 | R11G11B10 HDR | 相机 color（HDR 开启时为 R11G11B10） | PASS\* |
| P35 tex514 深度拷贝 | 供全屏 pass 采样 | device depth 存 normal.a，避免同一 pass 既绑 DSV 又采样 | PASS\* |
| P35 眼睛 proxy | 240 idx 几何 → 相机 HDR + depth | `SRCharacterEyeMark/EyeShade` 重画同一网格 | PASS（proxy 网格由用户指定） |
| P35 透明刘海 | 1056 idx 几何 → 相机 HDR + depth | `SRCharacterTransparent` fill/outline | PASS |
| P36 tex9621/tex5288 | 后光照 mask/effect | 未重建；仅导出 `_SRCharacterMask` | NOT PORTED |
| P27 tex9580 | 屏幕空间阴影 | 全屏 pass 未采样 | NOT PORTED |
| P00–P02 SO 缓冲 | GPU 蒙皮顶点流 | Unity `SkinnedMeshRenderer` | NOT PORTED |

### 3.2 CB 绑定

| 项 | 状态 | 说明 |
|---|---|---|
| 本实现用到的 CB | 已声明 | `UnityPerMaterial`（[SRCharacterCommon.hlsl:17-56](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterCommon.hlsl#L17)）+ URP 的 PerCamera/PerDraw/阴影/SH 全局 |
| 抓帧 per-draw CB（VS cb0=4544B/4560B 等） | **无法字节级对应** | 01 §6 已说明：XML 只记录槽位→buffer→大小，常量内容未解码；且 HSR 自带引擎 CB 布局 |
| 骨骼矩阵 / 逐物体数据 | NOT PORTED | GPU 蒙皮链路整体未移植；改用 Unity 蒙皮 |
| 材质参数 | PASS\* | 用 ShaderLab 材质属性表达（albedo/ILM/ramp/SDF/描边/高光/边缘），语义覆盖、布局不同 |
| ramp / LUT | PARTIAL | 全局 16 行 ramp（[Shading.shader:89-101](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterShading.shader#L89)）；抓帧中的 8×8 RGBA16F 小图未解码 |

**结论**：CB 的“绑定关系与用途分组”已覆盖；**内容级对应不成立**，需要下一轮用 RenderDoc
`GetCBufferVariableContents` / `debug_pixel --trace` 解码后才能做逐项比对。

### 3.3 算法移植

| 算法 | 状态 | 实现位置 |
|---|---|---|
| G-Buffer + 模板材质 ID | PASS | [SRCharacter.shader:81-165](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader#L81)，载荷见 `SRCharacterDefines.hlsl:20` |
| 描边（front-face cull + 法线外扩 + FOV/距离补偿） | PASS | [SRCharacter.shader:207-248](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader#L207) |
| 全屏分组 toon 着色（模板门控） | PASS | [SRCharacterShading.shader:104-147](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterShading.shader#L104) |
| ramp 漫反射 + ILM.g 阴影形状 | PASS\* | [Shading.shader:124-140](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterShading.shader#L124) |
| 面部 SDF 阴影 | PASS\* | [SRCharacterCommon.hlsl:100-116](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterCommon.hlsl#L100)（GBuffer 阶段求值，写 mask.A） |
| 高光 / 边缘光 / 自发光（加法叠加） | PASS\* | [SRCharacter.shader:322-340](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader#L322) |
| 主光阴影 | PASS\* | 全屏与叠加均采样 URP 主光阴影 |
| 环境光 | PARTIAL | `SampleSH` 近似；抓帧的 GI/雾（P29）未解码 |
| 法线贴图 | PASS\* | `SRGetNormalWS`（[Common:87](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterCommon.hlsl#L87)） |
| GPU 蒙皮 / Stream-Out / indirect | NOT PORTED | P00–P02 |
| 屏幕空间阴影（P27） | NOT PORTED | 抓帧 P35 ps0 的 tex9580 |
| 眼睛遮挡 bit0 + 眼睛高光 | PASS\* | 标记 pass（[SRCharacter.shader:SRCharacterEyeMark](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader)）+ 加法高光 pass（`SRCharacterEyeShade`）；公式为可配置的屏幕空间高光，非抓帧虹膜折射 |
| 半透明刘海 / stencil bit0 机制 | PASS\* | [SRCharacterTransparent.shader](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterTransparent.shader)（alpha 混合 + `NOT_EQUAL` 第一层去重 + 背面描边）+ [SRCharacterTransparentPass.cs](Projects/02-SRCharacterShaderReserve/Runtime/Passes/SRCharacterTransparentPass.cs) |
| 全屏 pass 消费 bit0 | PASS | 组 1..3 `WriteMask 1 / Pass=Zero`（DSS 655），组 0 不写（DSS 662） |
| 角色遮罩 + bloom/tonemap（P36–P55） | NOT PORTED | 未重建后处理链 |

### 3.4 资源绑定与“逐像素对应”

**能证明的**：绑定槽位与状态语义逐项对应（见 3.1/3.5），且每个阶段都有行为证据：
关闭 feature → 0 像素；关闭 overlay → 全屏着色仍生效；加入遮挡物 → 被遮挡像素不再被角色覆盖；
50% alpha 刘海 → 中心颜色按 alpha 混合；眼睛 proxy 被刘海条挡住的一侧不出现加法高光（bit0 生效）。

**不能证明的**：与抓帧图像逐像素一致。原因：

1. 抓帧没有模型/贴图资源，`img/` 里的 GBuffer/HDR 是原游戏内容，本仓库的冒烟渲染是胶囊体；
2. 抓帧 shader 的 CB 常量内容不可得（01 §6），无法复现逐材质参数；
3. 抓帧是 3072×1920 + 1.2× 超采样 + 自有 tonemap/bloom，本模块输出到宿主相机的 HDR 目标。

因此本仓库的验收口径是**结构性对应**（pass/绑定/状态/像素级行为），而不是图像级相等；README 与 02 文档中
所有“对应”都按这个口径表述。

### 3.5 Stencil 与绘制顺序

| 项 | 抓帧 | 本实现 | 判定 |
|---|---|---|---|
| 编码 | bits7..6 group + low id | 相同（`MakeRef`） | PASS |
| GBuffer 写入 | Replace，W255 / 第二套 W254 | Replace，W255 | PASS\*（W254 未镜像；本实现的 id 在 albedo.a，低 6 位不参与着色） |
| 描边 | CULL_FRONT + 背面 REPLACE | `Cull Front` + Replace | PASS |
| 全屏分组 | Ref 0/64/128/192 + ReadMask 0xC0 + Equal | 相同（[Shading.shader:33-38](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterShading.shader#L33)） | PASS |
| bit0 消费 | 组 1..3 `Pass=ZERO / W1`；组 0 不写 | 相同（组 0 `WriteMask 0`） | PASS |
| bit0 眼睛标记 | DSS 1303 `ZFail=Replace / W1` | 相同（`SRCharacterEyeMark`） | PASS |
| bit0 眼睛高光 | DSS 1305 `ReadMask 0x81 / W1 / ops Zero` | 相同（`SRCharacterEyeShade`） | PASS |
| 透明刘海去重 | `NOT_EQUAL → Replace`（blend 213） | 相同 | PASS |
| 顺序 | P08（GBuffer→描边）→ P35（全屏→眼睛→前向叠加→透明刘海） | GBuffer→Outline→Shading×4→EyeMark→EyeShade→Overlay→Transparent | PASS |
| P27 位于 P08 与 P35 之间 | 有 | 无 | GAP |

### 3.6 深度信息

| 项 | 抓帧 | 本实现 | 判定 |
|---|---|---|---|
| 缓冲格式 | D32_FLOAT_S8_UINT | D24_UNORM_S8_UINT（URP 相机深度） | PARTIAL |
| 反向 Z | `GREATER_EQUAL`，清 0 | Unity D3D11 反向 Z；ShaderLab `ZTest LEqual` 自动映射 | PASS |
| GBuffer 深度写 | WRITE_ALL | `ZWrite On`（[SRCharacter.shader:96](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader#L96)） | PASS |
| 描边深度写 | WRITE_ALL | `ZWrite On`（[SRCharacter.shader:185](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader#L185)） | PASS |
| 全屏 pass 深度 | `ALWAYS` + 不写 | `ZTest Always / ZWrite Off`（[Shading.shader:26-27](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterShading.shader#L26)） | PASS |
| 前向叠加深度 | `GREATER_EQUAL` + 不写 | `ZTest LEqual / ZWrite Off`（[SRCharacter.shader:279-280](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacter.shader#L279)） | PASS |
| 深度传递 | 拷贝纹理 tex514 | device depth 写 normal.a，世界坐标用 `unity_MatrixInvVP` 重建（[Shading.shader:116-119](Projects/02-SRCharacterShaderReserve/Shaders/SRCharacterShading.shader#L116)） | PASS\* |
| 行为验证 | — | 遮挡测试：中心像素被遮挡物占据（RGBA 218,36,36），角色像素 5608 → 24 | PASS |

## 4. 使用

模块导入与材质/Feature 配置步骤见 [02-SRCharacterShaderReserve/README.md §3](Projects/02-SRCharacterShaderReserve/README.md#3-使用步骤)。
要点：

1. 把 `Projects/02-SRCharacterShaderReserve` 复制到宿主工程的 `Assets/` 下；
2. 在 URP Renderer Data 上添加 `SRCharacter Feature`；
3. URP Asset 的 **MSAA 必须为 1**（G-Buffer MRT 不能与 MSAA 深度混绑，feature 会警告并跳过）；
4. 材质设置 `_SRArea`（1 body / 2 face / 3 hair）、`_SRShadingGroup`、`_SRMaterialID`，
   Inspector 自动写 `_SRStencilRef`；角色根节点挂 `SRCharacterRenderer` 并指定头骨；
5. 提供 256×16 的 cool/warm ramp（V 方向 16 行对应材质 ID）。

## 5. 复现验收

```bash
# 1) 复制模块到宿主工程
cp -r Projects/02-SRCharacterShaderReserve/{Runtime,Shaders,Editor} \
      E:/GitHub/Glx.CustomURP/URPSample01/Assets/_SRCharacterVerify/

# 2) 批处理：编译检查 + 冒烟渲染 + 深度遮挡
"D:/Program Files/Unity/Hub/Editor/2022.3.62f1/Editor/Unity.exe" \
  -batchmode -quit -projectPath E:/GitHub/Glx.CustomURP/URPSample01 \
  -executeMethod SRCharacter.SRCharacterVerify.Run \
  -logFile Projects/02-SRCharacterShaderReserve/validation/verify.log

# 3) 删除临时目录
rm -rf E:/GitHub/Glx.CustomURP/URPSample01/Assets/_SRCharacterVerify*
```

验证脚本会克隆 renderer data 注入 feature、用 `RenderPipeline.SubmitRenderRequest` 渲染 256×256
（`Camera.Render()` 在 SRP 下会走内置管线），结束后恢复原 renderer data 与 MSAA。

## 6. 已知差距与优先级

| 优先级 | 项 | 影响 |
|---|---|---|
| 1 | P27 屏幕空间阴影（tex9580） | 角色的接触阴影/柔和投影缺失，全屏 diffuse 只乘主光 shadow |
| 2 | P36 角色遮罩 + bloom/tonemap（P37–P55） | 角色不会参与 bloom 强度控制，最终观感差异大 |
| 3 | GPU 蒙皮 / Stream-Out（P00–P02） | 大场景 CPU 开销与逐帧顶点流构建方式不同 |
| 4 | CB / shader 解码（`GetCBufferVariableContents`、`debug_pixel`） | 才能把 ramp/SDF/LUT 公式从“推断”提升为“证实” |
| 5 | per-character LUT、附加光/雾、眼睛虹膜折射公式（PS 9584）、透明刘海 3 段材质分件 | 细节与多角色场景 |

## 7. 文档索引

- 抓帧分析：[01-SRCharacter/README.md](Projects/01-SRCharacter/README.md)
  （pass 总览 / 绑定 / 依赖图 / 角色链路）
- 重建模块：[02-SRCharacterShaderReserve/README.md](Projects/02-SRCharacterShaderReserve/README.md)
  （设计、证据映射、导入步骤、验证）
- 本轮证据：[verify.log](Projects/02-SRCharacterShaderReserve/validation/verify.log)、
  [smoke_render.png](Projects/02-SRCharacterShaderReserve/validation/smoke_render.png)

## 8. 致谢与许可

- 面部 SDF 与描边宽度补偿的思路参考了 [HonkaiStarRailToonShader](https://github.com/Gaolingx/HonkaiStarRailToonShader)（MIT）。
- 多 pass / stencil 分组与角色渲染架构参考了 [StarRailNPRShader](https://github.com/stalomeow/StarRailNPRShader)（GPL-3.0；
  本仓库未复制其代码，仅参考架构思路）。
- 抓帧、游戏模型与贴图版权归米哈游所有，本仓库不分发这些资源；`raw/` 中仅包含为此分析生成的派生 JSON/XML 片段。