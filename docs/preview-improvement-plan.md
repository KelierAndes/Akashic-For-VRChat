# 预览改进计划与落地方案

> 本文是「预览」这一条链路的待办清单与落地方案，来源是一次与外部项目 VRCAL（VRChat 素材库 1.4.0，Electron + three.js）的横向对比，加上对 `src/Akashic.Core/Services/Preview/`、`src/Akashic.Host/preview/`、`app/lib/src/pages/preview_page.dart` 的逐文件调研。
>
> 行文约定沿用 `DEVELOPMENT.md`：源码不携带注释，解释性内容写在这里；`### ` 小节按「现状 → 方案 → 验收」三段展开；文件位置一律写成 `路径:行号`，行号以 v2.2.0（HEAD `da84a6a`）为准。
>
> **证据分级**（引用前请注意）：
>
> * **甲类** — 本次直接读取并核对过原文与行号：`FxcTool.cs` 全文、`HlslPreviewService.cs` 全文、`ShaderCompileService.cs` 的 13/23/74/286-351/423-424、`PreviewCachePaths.cs` 的 6/8-10/45/55/142-144、`Shader/ShaderEnvironment.cs` 的 7/19-31、`Api/PreviewEndpoints.cs` 的 34-158、`UnityShim.hlsl` 的 51-55/118-123/199/440-476/574、`viewer.html` 的 100/108/148/162/188/452/524/543-545/604/891、两个 csproj 的依赖清单、`.gitignore`，以及 `DEVELOPMENT.md` 的 288-349 与 382-383、`README.md` 的 126-128。
> * **乙类** — 调研记录里给出的行号，方向可信但未逐条二次核对，动代码前建议按当前文件复核：`D3D11PreviewRenderer.cs`、`HlslSceneData.cs`、`ShaderLabExtractor.cs`、`PrefabParser.cs`、`GlbBuilder.cs`、`PackageContentCache.cs`、`app/lib/src/pages/preview_page.dart` 的绝大多数行号。
> * 本文**没有**构建或运行 Akashic，所有性能量级（首次预览耗时、编译排队时长、峰值内存）都是从代码路径推断，不是实测；需要实测时见「七 · 验证方法」。

---

## 一 · 现状速览

三条渲染路径并存，「实时」在两条里成立、在一条里不成立，这是所有后续判断的前提。

```
  素材（.unitypackage / 包内文件 / 已解压目录）
        │
        ├──► PackageContentCache 解包（tar.gz 顺序读 → 落盘 extract-v5）
        │            │
        ├──► PrefabParser（YAML → GameObject 层级 + 材质语义）
        │            │
        │      ┌─────┴──────────────┐
        │      ▼                    ▼
        │  GlbBuilder          ShaderLabExtractor + UnityShim
        │  （SharpGLTF）            │
        │      │                    ▼
        │      │                ShaderCompileService（fxc.exe → cso，SHA256 落盘 hlsl-v2）
        │      ▼                    │
        │  viewer.html three.js     ▼
        │  WebGL 实时视窗      D3D11PreviewRenderer 离屏
        │      │                    │
        │      │                    ▼
        │      │              RT → Map → 交换 R/B → JPEG q=90
        ▼      ▼                    │
   WebView2（flutter_inappwebview）◄┘
```

| 路径 | 输入 | 渲染 | 帧驱动 | 保真上限 | 主要缺口 |
| --- | --- | --- | --- | --- | --- |
| GLB 实时视窗 | prefab 解析后的 GLB | three.js r170 WebGL（`viewer.html:148`） | 持续 `setAnimationLoop`（`viewer.html:891`） | PBR / unlit / toon 近似 | 无蒙皮无 morph、无 mipmap、贴图只嵌 png/jpg、无 contextlost 处理 |
| HLSL 精确渲染 | `.shader` 源码真编译 | D3D11 FL 11_0（WARP 兜底） | **无帧循环**，一次请求一张 JPEG | ShaderLab 状态 + 原 PS 逻辑 | 不可交互（四视图固定、缩放是 CSS `transform`）、无阴影/多光/探针、变体关键字不参与编译 |
| 贴图预览 | DDS/TGA/HDR 等 | `TexturePreviewService` CPU 解码 → PNG | 静态 | 单张图 | 不进 three.js 场景 |

VRCAL 只有一条路径（three.js WebGL 看原始模型文件），它不做 prefab 解析也不碰 shader 源码，换来的是极厚的输入护栏与 1293 个自动化测试。我们反过来：保真度和语义层次都更高，护栏和回归是欠的。本计划因此把 P0 全部押在这两项上。

---

## 二 · 待办总览

| 编号 | 优先级 | 标题 | 影响面 | 预估改动 |
| --- | --- | --- | --- | --- |
| A1 | P0 | 不可信 shader 源码的输入护栏 | 安全 | 小（2 文件） |
| A2 | P0 | 预览链路自动化回归基线 | 交付质量 | 中（新建测试工程） |
| B1 | P1 | HLSL 路径实时化（分三档，先做档③） | 体验 | 档③小 / 档①大 |
| B2 | P1 | 采样与压缩纹理质量（mipmap、BC、MSAA） | 观感 | 中 |
| B3 | P1 | shader 变体清单做成手动开关（材质关键字已在编译，别误判） | 保真 / 可信度 | 中 |
| B4 | P1 | 编译并发与排队（拆掉全局串行门） | 响应性 | 小 |
| C1 | P2 | WebGL 路径生命周期与按需绘制 | 稳定性 / 功耗 | 小 |
| C2 | P2 | 蒙皮与 morph（先决策做不做） | 保真上限 | 大 |
| C3 | P2 | 光照完整性或把边界写进文档 | 观感 | 中 |
| C4 | P2 | 文档漂移收敛（4 处） | 可信度 | 很小 |
| C5 | P2 | 依赖与工作树卫生 | 可维护性 | 很小 |

**落地状态**（详细取舍与坑记在 `DEVELOPMENT.md` 的 Prefab 预览小节，这里只记状态与日期）

| 编号 | 状态 | 备注 |
| --- | --- | --- |
| A1 | 已完成（2026-10-27） | 2 MB / 4 MB 体积闸门、include 自行展开 + 越界拒绝（`ShaderIncludeResolver`）、60 秒 / 180 秒分级超时；失败按 `size/include/timeout/compile/unsupported` 分类型进 `X-Akashic-Reject-<kind>` 与 `-fb` 的 meta。**偏差**：允许根保留了「包内容根」，只拒绝绝对路径与含 `..` 的段——包内跨目录 include 是常态，逃出包内容根才是要防的那一步；因此 200 shader 的 fallback 率回归尚未做（缺第 5 步脚本），目前只有样例包实机验证 |
| A2 | 进行中（2026-10-29） | 第 1、2、3、5、6 步已落地：`tests/Akashic.Tests` 32 个用例（提取器 / include 解析 / 关键字 / 缓存键 / 缓存族与配额 + **黄金图**：WARP 出图，结构断言 + 原始 RGBA 的 SHA256 对 `docs/preview-golden.json`，`AKASHIC_GOLDEN_UPDATE=1` 重生成）；`scripts/preview-regression.ps1` 跑固定 5 包清单并写 `docs/preview-regression-baseline.json`，默认「只准变好」；`test-all.ps1 -Regression` 接入。**待做**：第 4 步 Flutter 状态机测试（`_toggleHlsl` 的 1.5 s 复查与 900 ms 轮询用假时钟断言）。首轮基线（5 包 11 prefab）：可渲染 7、零回退 2、绘制 86、回退材质 19、材质球 24/24、构建失败 0、预期内不可预览 4；顺带量到 ABT 有 2 张贴图解不出（`.dds`/`.exr` 那一类，与 B2 的记录对上）。脚本侧的坑与结论都记在 `DEVELOPMENT.md`。 |
| B4 | 已完成（2026-10-27） | `KeyedSemaphore`，同 shader 串行、跨 shader 并行，上限 `min(4, CPU/2)`；内存缓存换 `ConcurrentDictionary`。未实测批量预热墙钟时间（缺第 5 步的回归脚本） |
| B1 | 已完成（2026-10-29，档③并直接做到全尺寸单视图） | 四视图方案删除：侧车按 `view:"orbit"` 出透视单视图、回原始 RGBA（`X-Akashic-Camera` 让视窗接管机位），视窗用 canvas + 轨道相机做按需渲染（拖动 2–10 ms/帧，静止不发请求）。**未做**：档②的零拷贝帧缓冲（WebView2 仍走本地 HTTP），以及渲染/排队埋点的批量实测 |
| B2 | 部分完成（2026-10-29） | 第 ②③ 步落地：mip 链（`GenerateMips`）+ 各向异性采样 + 4× MSAA（WARP 下关）。斜视角轮廓走样能量 148.4→81.1（−45%）、正视角 134.4→93.1，单帧 4.1→6.5 ms，贴图显存 151→201 MB（mip +33%）。**偏差**：第 ① 步 BC/DDS 直传没做——实测 12 个工程 4200+ 张贴图里 `.dds` 只有 1 个，本节「绝大多数是 DDS」的前提不成立；数字与结论见 `DEVELOPMENT.md` 的 Prefab 预览小节 |
| C1 | 部分完成（2026-10-29） | ①contextlost/restore 已接（含坐标轴那块画布，`preventDefault` 是关键）③隐藏时停 `setAnimationLoop`、回到可见补一帧，Flutter 侧视图被遮挡时停 900 ms 轮询。②「模型级 dispose」经核对**不需要做**：换内容整块重建 WebView，GL 资源由浏览器回收；真正只增不减的是服务端的贴图与场景缓存，已分别封顶（1 GB LRU / 最近 2 个会话）并有 `TextureCacheTests` 兜住。④渲染比例下拉未做。**验收未达标部分已判定**：新增 `GET /api/preview/diagnostics[?collect=1]` 报 GC 堆与各缓存计数，实测连看 9 个包，强制 GC 后托管堆恒为 7–21 MB、`scenes`/`artifacts` 稳定在 2、`parsers` 全程 0 —— 私有内存的增长是 .NET 大对象堆未压缩 + GPU/驱动侧占用，不是泄漏；据此把贴图上限从 1 GB 收到 512 MB（实测跨模型累积会被压回，单模型峰值超出上限属预期，因为淘汰只针对「当前会话不再引用」）。②' 给 `_gpuPasses` 加回收试过，无收益且要付重建开销，已撤回。 |
| C4 | 已完成（2026-10-29） | 五条全部处理：`MeshToonMaterial` 的说法改成实际的自写 `ShaderMaterial`；`TextureST` 的两路径差异写进 `DEVELOPMENT.md`（补 `KHR_texture_transform` 留作后续，SharpGLTF 1.0.7 已有该扩展）；UI 档位 32 GB 与常量 128 GB 的关系加了说明；文档里所有写死的 `shim-vN`/`hlsl-vN`/`extract-vN`/`tex-vN` 改成占位或指向常量（`ShaderEnvironment.Version`）；上下文丢失的文案随 C1 落地。顺带把 B2 里「贴图绝大多数是 DDS」这条错误前提就地更正。 |
| B3 / C2 / C3 / C5 | 未开始 | 见 §六 批次 2、3 |

---

## 三 · P0

### A1 · 不可信 shader 源码的输入护栏

**现状** `ShaderCompileService.CompileAsync` 把 include 根交给 fxc：`_shimRoot`、`_shimRoot/UnityStubs`、`shaderDir`、`contentRoot`，以及 `shaderDir` 的父目录（`ShaderCompileService.cs:326-334`），随后 `FxcTool.CompileAsync` 把它们拼成 `/I` 参数（`FxcTool.cs:48-50`），工作目录取源码文件所在目录（`FxcTool.cs:59`）。已有的防护是：关键字白名单 `长度 0<n<96 且仅 [A-Za-z0-9_]`（`ShaderCompileService.cs:423-424`）、`ArgumentList` 逐项传参（不构成 shell 注入面）、90 秒超时 + `Kill(entireProcessTree)`（`FxcTool.cs:76-92`）、`UsePass` 递归深度 ≤4（`ShaderLabExtractor.cs:104`）。

缺的是三件事：

* `.shader` 源码本身没有字节上限。整个文件被 `File.ReadAllText` 读进内存再拼进单文件 Pass（`ShaderCompileService.cs:289` 调用链上的读取点），一个大文件或者被塞了超长字符串的 shader 会直接进内存和 fxc。
* `#include` 没有路径约束。fxc 的 `#include` 可以写相对路径，`../` 能从 `shaderDir` 逃出到任意目录，也可以写绝对路径；`/I` 只是搜索顺序，不是沙箱。素材包的 `.shader` 是第三方作者写的文本，这条路径等于「别人决定的文件会被打开读入」。
* 编译是全局串行的（`SemaphoreSlim(1, 1)`，`ShaderCompileService.cs:23`），所以一个恶意或病态 shader 卡满 90 秒，会把队列上所有用户的预览一起拖住 — 这是拒绝服务面，不是正确性问题，但和上面两条同源。

**方案**

1. 在 `ShaderLabExtractor.Parse` 之前加一道读取闸门：单个 `.shader` 超过 **2 MiB** 直接判定 `UnsupportedReason = "着色器源码过大"` 并走 fallback（`FallbackLit.hlsl` 已有 opaque/cutout/unlit 三档，`HlslPreviewService` 的降级路径本来就通）。同理给 `CGINCLUDE` 拼接后的单 Pass 源文件设上限，超限按 Pass 失败跳过而非整体失败。
2. include 治理分两步。第一步零风险：把 `contentRoot` 与 `shaderDir` 父目录这两个宽根去掉，只保留 `_shimRoot`、`_shimRoot/UnityStubs`、`shaderDir`，实测有多少第三方 shader 因此退化到 fallback — 这个数字决定第二步做多重。第二步：在拼接单文件时**就地展开** `#include "x"`（自己做搜索：先 `shaderDir`，再 shim 根，且解析后的全路径必须 `Path.GetFullPath` 后落在允许根内，含 `..` 或绝对路径一律拒绝并记为 Pass 失败），把展开后的源码写进临时文件再喂给 fxc，`/I` 只传 shim 根。这样第三方文本对文件系统的读取意图在我们这一层就被判完了，fxc 只看到一个自包含文件。
3. 把超时分级：编译超时从固定 90 秒改为「首个 Pass 60 秒，同一 shader 累计 180 秒后整条放弃」，并把超时原因写进 `-fb` 的 meta，让 UI 能说「因为这个 shader 编译超时所以用了近似材质」。
4. 顺带把 A1 的三项做成可观测：`/preview/render` 已经在回 `X-Akashic-Draws / Fallbacks / Fallback-Detail` 响应头（`Api/PreviewEndpoints.cs:64-86`），把「源码过大拒绝 / include 越界拒绝 / 超时」三类计数加进同一个头，验收时不用翻日志。

**验收**

* 构造三个用例进测试集：8 MiB 的 `.shader`、含 `#include "../../Windows/win.ini"` 的 `.shader`、含编译不通过死循环（`for(;;)` 写在 PS 里且带依赖数据）的 `.shader`。三者都必须：不产生文件读取、不阻塞其它预览、界面显示具体退化原因、缓存目录里留下 `-fb` 记录。
* 200 个真实包内 shader 的批量回归：fallback 率相对改动前的变化 ≤3 个百分点（第 2 步收紧 `/I` 前后各跑一次，这就是那个决定性的数字）。

### A2 · 预览链路自动化回归基线

**现状** 全仓库能跑测试的只有 `app/test/widget_test.dart`（Flutter 模板遗留），`src/` 下没有测试工程。也就是说 `PrefabParser.cs` 1256 行 YAML 解析、`ShaderLabExtractor.cs` 586 行 ShaderLab 扫描、`D3D11PreviewRenderer.cs` 1280 行渲染器、`ShaderCompileService.cs` 499 行编译缓存，全部靠人开应用验证。

不过缺的不是**验证**，而是**沉淀**：`DEVELOPMENT.md` 里有 10 处「实测」记录，而且已经是全库量级的批量回归 —— `:305` 记着「SUIMONO 从 4/49 到 39/49；全库 89 个包、抽 229 个材质跑一遍球渲染是 225 成功 / 4 二进制 / 0 其他失败，同一轮 prefab 回归是 buildFail 0、可渲染 52、无网格 22、无 prefab 15、428 次绘制 / 215 个回退材质」，`:311` 记着「全库 93 个包重跑：可渲染 46、零回退从 39 升到 42」。这些数字说明跑批的方法早已存在，只是每次都以文字形式收尾，没变成可重复执行的断言 —— 下次改动无法自动知道自己把 225 变成了 220。对比 VRCAL：1293 个 `node --test` 用例（本机实测全绿，346.9 秒），做法是把预算/路径/包围盒/变换抽成纯函数、测试里注入假 `WebGLRenderer` 和伪 Electron，因此不需要 GPU；真实渲染结果另用 Playwright 起 Electron，读 `canvas.dataset.previewFrame` 计帧 + `canvas.screenshot()` 逐像素统计目标色占比来断言「确实画出来了」。我们要补的正是这两层。

**方案**

1. 新建 `tests/Akashic.Tests/`（xUnit，net8.0），优先覆盖**不需要 GPU 也不需要文件系统的纯逻辑**，按性价比排序：`ShaderLabExtractor.Parse`（给定 `.shader` 文本 → Pass 列表 / LightMode 过滤 / Properties 默认值 / URP 判定 / `SupportsHlsl` 与 `UnsupportedReason`）、`IsValidKeyword`、`CacheKey` 稳定性（同输入同 key、mtime 变则变、族名解析）、`PreviewCachePaths.FamilyOf`（`hlsl-v1` 与 `shim-v7` 不得互相误删，见 `:45/:55`）、`Quota` 钳制（`ClampQuota` `:142-144`，顺带把 C4 的文档数字问题变成断言）。
2. 解析层再加一档「fixture 包」测试：把 `.prefab/.mat/.shader` 的最小样本放 `tests/fixtures/`，断言 `PrefabParser` 产出的层级树、材质槽、Cull/Blend/clip 判定，以及 `GlbBuilder` 的 GLB 结构（用 SharpGLTF 读回自己生成的 GLB，断言材质数量、`KHR_materials_unlit` 出现与否、alphaMode）。这条能同时守住「文档说 toon 用 MeshToonMaterial 但代码是自写 ShaderMaterial」这类漂移。
3. 渲染层用「黄金图 + 像素断言」，和 VRCAL 同一思路但更简单：`D3D11PreviewRenderer` 已有离屏 RT 与 JPEG 出图（`Api/PreviewEndpoints.cs:79` 的 `Results.Bytes(frame.Jpeg, "image/jpeg")`），直接对固定 fixture 出图，断言中心区域非背景像素占比、目标色（紫/蓝/绿这类）落在容差内、四视图四个子格都有内容。CI 用 WARP（`D3D11PreviewRenderer.cs` 里硬件失败退 WARP 的分支已经存在，测试里强制走它）即可无 GPU 运行。
4. Flutter 侧补 `preview_page.dart` 的状态机测试：`_loading/_building` 覆盖层出现与消失、HLSL 切换后 1.5 秒查状态与 900 毫秒轮询这两条计时器（`preview_page.dart:218-266`）用假时钟断言，换模型时 `ValueKey(_viewerUrl)` 确实重建。这类「状态卡死」是手工测试最难复现的。
5. **把已有的全库重跑做成脚本** `scripts/preview-regression.ps1`：输入固定包清单（现在文档里点名的 SUIMONO、Nijiiro、ABT、MiniSimplisticPens 就是天然样本），输出与 `DEVELOPMENT.md:305/:311` 同一组指标（可渲染数、零回退数、绘制次数、二进制 `.mat` 数量、构建耗时），并把上一次结果写成 `docs/preview-regression-baseline.json` 做劣化断言。这一步不需要新逻辑，只是把手工流程固定下来，收益立刻可见 —— A1/B3/C2 每一项都要靠它来证明「没有变差」。
6. 把 1–5 串进 `scripts/test-all.ps1`（与既有 `scripts/stage-dist.ps1` 同风格）：dotnet test → flutter test → 黄金图 → 全库回归（后者可选，因为要真实素材库，CI 里用 fixture 子集）。

**验收** 预览相关语句覆盖 ≥60%（先从纯函数区起步）；A1 的三个恶意用例和 C1 的 contextlost 用例必须在 CI 里；`preview-regression.ps1` 的指标相对基线不劣化（零回退数只增不减）；任何后续改动 `scripts/test-all.ps1` 必须绿，否则不发版。

---

## 四 · P1

### B1 · HLSL 精确渲染的实时化

**现状** 这条路径精确但不实时：每次渲染请求出 2×2 四视图（正交相机自动居中、共用 halfHeight），RT → `CopyResource` → Map staging → 交换 R/B → JPEG q=90，`POST /api/preview/render` 回 `image/jpeg` + `Cache-Control: no-store`，`viewer.html` 塞进 `<img id="hlslFrame">`。缩放是 CSS `transform: scale()` 不重渲（`viewer.html:452`），HLSL 模式下 OrbitControls 被禁用，请求尺寸 = 视口 × DPR × 2、上限 4096。服务端渲染 `SemaphoreSlim(1)` 串行。GPU 会话按 `scene+outline+mode+gizmo+背景+shader签名` 缓存复用。

也就是说**输入回路是唯一缺的东西**，场景数据、uniform 绑定、ShaderLab 状态翻译都已经在了。

**方案（分三档，按投入产出排序，建议先做档③再做档①）**

* **档③「交互期间降质量」（改动最小，1 天量级）** 在 `viewer.html` 里给 HLSL 模式加一个可拖动的伪轨道相机：按下拖动时用**单视图 + 短边 512**连续请求，松手后请求一次 2×2 全尺寸收尾。服务端 `HlslPreviewService` 增加 `quality` 参数，跳过四视图拼接。JPEG 编码 + HTTP 往返在 512 单视图下大概能进 10–20 毫秒级（**待实测**，这是档③值不值得做的判断依据）。同时在请求间加 `hlslBusy/hlslQueued` 已有的单飞合并，避免拖动时排队爆炸。
* **档②「共享内存帧缓冲」** 不走 JPEG：sidecar 用 D3D11 打开一个 `ResourceOptionFlags.SharedResource` 的纹理（或 Named Mutex + Section 的 BGRA8 原始帧），通过本地 HTTP 以 WebSocket/SSE 推帧号，Flutter 侧用 `Texture` + 平台通道注册外部纹理消费。这条路能到 60fps，但要把 WebView2 的角色从「渲染面」降级为「控制面」，跨 WebView2/Flutter/D3D11 三方的生命周期会显著变复杂。
* **档①「HLSL 直接在 Flutter 渲染」** 把 cso 二进制（`passN-vs.cso / passN-ps.cso` 已经落盘）通过 HTTP 交给 Flutter，用 `flutter_gpu` 类自研 D3D11 嵌入层自己画。保真与帧率都最好，代价是自己维护一个 Flutter D3D11 纹理/交换链集成层，并且 `viewer.html` 里的 three.js 路径要么废弃要么变成「近似模式」。不建议在没有实测数据前启动。

无论做哪档，先补一个东西：**渲染耗时与排队时长的自测埋点**（现有 `X-Akashic-Draws/Fallbacks` 头扩成 `X-Akashic-Render-Ms / X-Akashic-Queue-Ms`）。没有这个数字，档③够不够用是猜的。

### B2 · 采样与压缩纹理

**现状**（乙类行号，`D3D11PreviewRenderer.cs`）贴图是 StbImageSharp CPU 解码成 RGBA8 后按 `R8G8B8A8_UNorm` 上 GPU，1 级 mip；采样器只有 LinearWrap / LinearClamp 两个，靠名字含 `clamp` 选；`MultisampleEnable=false`，单采样 BGRA8 RT + D24S8。

**影响** VRChat 素材的贴图绝大多数是 BC7/BC1 的 DDS，CPU 展开成 RGBA8 意味着显存占用是压缩格式的 4–8 倍，且**没有 mip 链 → 斜视角严重走样**，这两条叠加是「HLSL 图和 Unity 里比明显更噪/更闪」的主因，比 shader 差异更大。

> **这条判断的实测更正（2026-10-29）**：「贴图绝大多数是 DDS」不成立。12 个工程 `Assets` 目录里 4200+ 张贴图的分布是 PNG 3681 / JPG 469 / EXR 96 / HDR 67 / TGA 67 / PSD 60 / TIF 29 / **DDS 1**，Asset Store 缓存与 Booth 目录里 `.dds` 为 0。所以本条的真实收益全在 mip 链与 MSAA（走样），压缩纹理直传既省不到显存、还要为 0.02% 的资产引入 unsafe 指针路径 —— 已按实测放弃，只保留「`.dds`/`.exr` 解码失败会白图并计入『N 张无法解码』」这个可观测兜底。

**方案** 三小步：①按 DDS 头把 BC1/BC3/BC4/BC5/BC7 直接建成对应 `DXGI_FORMAT` 纹理并用 `Texture2D.FromMemory` 类的现成路径（Vortice 已依赖，或直接自写 BC 头解析），CPU 解码只留 PNG/JPG/TGA 兜底；②`GenerateMips` + 采样器加 `Filter.MinMagMipLinear` 与 AddressU/V 的 mirror/clamp 判定（Unity 的 wrap mode 在 `.mat` 里没有，取 shader 语义默认即可）；③RT 做成可选 MSAA 4×（提供开关，低端卡或 WARP 下关）。

**验收** 同一张贴图在 45° 斜视角下截图对比，走样区域像素差异显著下降；显存占用测出改动前后数字（这条要进「七 · 验证方法」的实测清单）。

### B3 · 把 shader 声明的变体做成手动开关

**先纠正一个容易踩空的判断**：材质关键字**确实已经参与编译** —— `HlslPreviewService.RenderSceneAsync` 把 `material.Keywords` 传进 `_compiler.GetAsync`（甲类：`HlslPreviewService.cs:110`），编译侧过滤后加上固定 `DIRECTIONAL`（甲类：`ShaderCompileService.cs:336-337`），`DEVELOPMENT.md:349` 也写明带的是材质的 `m_ValidKeywords`/`m_ShaderKeywords`。这跟 Unity 的语义是一致的：`shader_feature` 没被材质勾选就不该生效，所以「变体没编译出来」大多不是 bug。

真正的缺口是三个：

* shader 自己 `#pragma multi_compile` / `shader_feature` 声明的**变体清单只被采集不展示**（乙类：`ShaderLabExtractor.cs:365-374` 的 `pass.DefaultKeywords`），用户在右侧组件面板里看不到「这个材质其实还有 `_ALPHAPREMULTIPLY_ON`、`_UseShadowColor`、`_SHADOWS_SOFT` 这些档」，也就无从判断预览效果和 Unity 不同是不是因为材质没勾全。
* **引擎侧关键字只有 `DIRECTIONAL` 一个**，`FOG_LINEAR/EXP/EXP2`、`LIGHTMAP_ON`、`SHADOWS_SCREEN`、`_LIGHTPROBE_SH` 全部处于关闭档，而且 shim 里雾的宏本来就是空实现、`SHADOW_ATTENUATION` 恒 1（甲类：`UnityShim.hlsl:51-54、:574`）。所以材质里写了雾/阴影关键字也不会有任何变化 —— 这一点必须在 UI 上说清，否则用户会以为勾了就该有。
* `UNITY_COLORSPACE_GAMMA` 被写死成 `1`（甲类：`UnityShim.hlsl:199`），凡是靠这个 `#ifdef` 分支的 shader 都被强制走 gamma 路径，线性空间作者的作品会静默偏色。

**方案** ①把 `pass.DefaultKeywords` 与 `.mat` 已勾选的 `m_ShaderKeywords` 做交集展示在组件面板（已启用/可启用两栏），允许勾选后重渲染：缓存 key 里本来就含 `keywordKey`（甲类：`ShaderCompileService.cs:74`），不需要新机制，只要限制单材质一次最多手动启用 2 个可选关键字并提供「恢复材质原状」；②`UNITY_COLORSPACE_GAMMA` 从 `#define` 改成 `RenderOptions` 传入的 define（默认仍为 1，加一个「线性空间」开关），代价是多一倍缓存条目，只给愿意用的人；③明确**不做**雾/阴影/lightmap 的引擎分支，把「这些档恒为关闭」写进 C3 的边界说明。

**验收** 选 3 个已知带开关的材质（`DEVELOPMENT.md:305-306` 点名的 SUIMONO 是现成样本），面板能看到变体清单、勾选后出图确实变化，并固化为 B 组黄金图；未勾选路径的出图必须与改动前逐像素一致。

### B4 · 拆掉全局编译串行门

**现状** `SemaphoreSlim(1, 1)`（甲类：`ShaderCompileService.cs:23`）让所有 shader 编译排队；`FxcTool` 是子进程调用，本身可并行。

**方案** 改成按 shader 路径分键的 `KeyedSemaphore`（同文件串行、不同文件并行），全局上限 `min(4, Environment.ProcessorCount/2)`。fxc 是短生命周期进程，内存风险低；但要防止 A1 的恶意用例并发放大 — 因此**A1 必须先于 B4**，否则 4 个死循环 shader 能同时吃满 4 个核 90 秒。

**验收** 一个「首次解包的 unitypackage 带 20 个不同 shader」的批量预热场景，总墙钟时间相对改动前测出倍数；单 shader 编译失败不影响其它 shader 完成。

---

## 五 · P2

### C1 · WebGL 路径生命周期与按需绘制

**现状** `viewer.html:148` 建 renderer 时没有 `failIfMajorPerformanceCaveat`；全文没有 `webglcontextlost` / `webglcontextrestored` 监听（甲类核对：grep 无匹配）；资源释放只覆盖栅格重建（`viewer.html:188-189` 的 `grid.geometry/material.dispose()`）与 HLSL 帧图 blob 的即时释放（`:543-545`，那是 `<img id="hlslFrame">` 的 URL，与模型贴图无关），**模型级 dispose（geometry/texture/material）缺失**，靠 Flutter 换 URL 时整体重建 WebView 兜底（`preview_page.dart:880-881`）；渲染是持续 `setAnimationLoop`（`:891`），页面切到后台仍在满帧跑。

**方案** ①监听 `webglcontextlost` 并 `preventDefault`，界面提示「显卡预览中断，请关闭本页重开」；②`builds/<id>` 切换时遍历上一次 scene 做 `geometry.dispose()` / `texture.dispose()` / `material.dispose()`，保留一个 `disposeScene(root)` 小工具；③加 `document.visibilityState` 判定，隐藏时 `setAnimationLoop(null)`、可见时恢复；④可选：仿 VRCAL 加一个「渲染比例」下拉（25/50/75/100% 乘 `setPixelRatio`），低配机省电。

**验收** 连续切 10 个模型后 `performance.memory` 或任务管理器 WebView2 进程内存不再单调上涨；后台标签页 GPU 占用降到接近 0。

### C2 · 蒙皮与 morph

**现状** D3D11 顶点布局是 128 字节交错的 POSITION/NORMAL/TANGENT/TEXCOORD0-3/COLOR，**没有 BLENDINDICES/BLENDWEIGHTS**；`SkinnedMeshRenderer` 只在 scene.json 打 `Skinned` 标记；GLB 路径同样不导出骨架，viewer 也不播 `gltf.animations`。

**判断** 这是当前「预览里看到的是 T-pose 摆好的静态形状、且和 Unity 里穿好衣服的角色不完全一致」的根因。做全骨骼 + 一个姿势选择（A-pose / T-pose）成本不低（要读 `.asset` 序列化的 skin 数据，而现在 `.asset` 网格根本不读）；**建议先只决策、不排期**：如果目标是「看款式」，维持现状并在文档里写清楚；如果目标是「看上身效果」，那 C2 优先级直接跳到 P1，且 `.asset` 网格读取是它的前置。

### C3 · 光照完整性

**现状** 单一定向光 `(0.32, 0.62, 0.72)`、光色白、常量 SH；`ShadeSH9` 返回常量 `(0.22, 0.25, 0.30)`、`Shade4PointLights/ShadeVertexLights` 返回 0、阴影恒 1.0、雾为空宏（乙类：`UnityShim.hlsl:440-476、573-575、51-54`）；`LightMode` 过滤只留 ForwardBase/Always，ShadowCaster/Depth/ForwardAdd 全丢（甲类：`ShaderCompileService.cs:311-316`）。

`README.md:128` 和 `DEVELOPMENT.md:296-297` 已经把「不运行自定义 Shader、URP 或编译失败退回近似、四视图不能旋转、依赖脚本与粒子不显示、MatCap 默认不应用、Lightmap 不重建」写清了，这部分边界不用补。没写的是 **HLSL 精确模式下光照本身是简化过的**：切到「精确」的用户会合理推断阴影和 GI 也是准的，而实际是无阴影 Pass、无多光 ForwardAdd、SH 是常量、雾被吃掉。

**判断** 与其补全光照管线（成本 = 重建一个小型 Unity 前向管线，而且要引入 ShadowCaster Pass 收集与 depth prepass），不如先把上面这 5 个降级点写进 `DEVELOPMENT.md` 的 Prefab 预览小节，并在 README 的「和 Unity 里不一样」段落加一句「精确模式当前不还原阴影、多光源叠加与雾」。这一条的正确交付物是**文字，不是代码**；真要动代码，优先级排在 B2（mipmap 与压缩纹理）之后 —— 走样比缺阴影更容易被用户当成 bug。

### C4 · 文档漂移收敛

* **`DEVELOPMENT.md` 自己前后矛盾**：`:308` 说「名字命中 toon/lilToon/Poiyomi 等或带 ramp 槽位的用 `MeshToonMaterial`」，`:325` 又说「视窗用自写的 lilToon 近似 Shader」，实际代码是后者（乙类：`viewer.html:669-671、706-852`）。改 `:308`。
* **`TextureST` 在两条路径里行为不同且文档未提**：D3D11 侧按 `TextureST` 绑定（乙类：`D3D11PreviewRenderer.cs:618-623`），GLB 侧解析了却没写进 GLB（未用 `KHR_texture_transform`，乙类：`GlbBuilder.cs:171-198`）→ 同一材质在「近似」和「精确」下平铺/偏移不一样。要么补 `KHR_texture_transform`，要么在文档写明这个差异。
* **缓存上限的两个数字要说明关系**：`README.md:126/:148` 写设置页可选 2 / 4 / 8 / 16 / 32 GB（默认 8），代码 `MaxQuotaBytes` 是 **128 GB**（甲类：`PreviewCachePaths.cs:8-10`）。这不是错误——UI 档位与防御性钳制本就两回事——但读代码的人会当成漂移（我第一遍就误读成不一致）。建议在 `DEVELOPMENT.md` 加半句：「UI 只暴露到 32 GB，常量留到 128 GB 是给 gist 同步来的历史值兜底」。
* **版本号被写进了叙述句**：`DEVELOPMENT.md:309` 记 `ShaderEnvironment`（`shim-v6`）、`:310` 记 `shim-v7`，而常量现在是 `shim-v7`（甲类：`ShaderEnvironment.cs:7`）。带版本的叙述会一路过期，改成「见 `ShaderEnvironment.Version`」并把历史版本单独记在版本变更处。
* **WebGL 上下文丢失**既没处理也没文案（见 C1）。VRCAL 有现成说法可借鉴：「显卡预览上下文已中断，请关闭预览后重开；资料库仍可使用」。

### C5 · 依赖与工作树卫生

* `AssimpNet 4.1.0`（甲类：`Akashic.Core.csproj:13`）已停止维护多年，绑定的是老 assimp 5.0 时代 API，新 FBX 变体（FBX 2020 压缩二进制、加密）解析失败率会随素材更新上涨。评估替换为 `Assimp.Net.VR` 或直接锁一个自带 `assimp-*.dll` 的版本并把版本记进 DEVELOPMENT.md。
* **`hlsl` 编译缓存在升版本时不被族回收**：`ShaderCompileService` 建目录用的是裸 `Directory.CreateDirectory(directory)`（甲类：`ShaderCompileService.cs:324`），没有走 `VersionedUnder`；而 `DEVELOPMENT.md:382` 明确讲「版本号必须回收，不能只递增」并解释了族判断（`hlsl-v2` 与 `shim-v5` 互不误伤）。`bin/Debug/*/cache/preview/hlsl/hlsl-v1/` 里 39 个旧目录（含 9 个 `-fb`）对当前常量 `hlsl-v2`（甲类：`:13`）就是这个缺口的现场证据。注意这与配额回收是两件事 —— `hlsl` 不参与配额回收是刻意设计，理由是「重编一次要几十秒，比省几百 MB 贵」（甲类：`PreviewCachePaths.cs:6` 的 `GcSections` + `DEVELOPMENT.md:383`）。要补的是**升版清理**：`CacheVersion` 递增时删同族旧目录，保持与 `shim-vN` 互不干扰。改动很小，但它同时是 B3 手动开关的容量前提 —— 变体条目会成倍增长。
* 仓库根有个未跟踪的 `NVIDIA Corporation` 目录（甲类核对：`git ls-files` 为空、`.gitignore` 未覆盖）— 疑似某次 native 调用写出。确认来源后补进 `.gitignore` 或从根上消除。

---

## 六 · 建议批次

**批次 1（安全 + 回归，先把底座铺好）** A1 全部 → A2 的第 5 步（把现有全库手工重跑固定成 `preview-regression.ps1`，零新逻辑、当天可完成，之后每条改动都有数字可比）→ A2 的 1、2 两步 + CI 脚本 → B4。理由：A1 是纯防御、改动面小、收益即时；回归脚本先立起来，A2 后面的纯函数测试才有对照物；B4 必须晚于 A1 的上限与超时收紧。

**批次 2（观感与可测性）** B2 全部 → A2 的第 3 步（黄金图，正好用 B2 前后各出一批）→ C1 → C4。理由：B2 是「看起来更像 Unity」的最大单点收益（走样 + 显存），且不依赖任何前置；C4 顺手。

**批次 3（实时化，需要实测数据才能定档）** 先做 B1 的渲染/排队埋点，跑一批真实素材量出 `render_ms` 分布，再决定走档③还是档②。C5 里的 `hlsl` 族回收排在 B3 之前做 —— 变体手动开关会让缓存条目成倍增长，没有升版清理就不该开这个口子。B3 与 C2/C3 一起进这一批：它们都要求先回答「精确渲染到底要对齐到什么程度」。

---

## 七 · 验证方法（本计划未覆盖的部分）

本文所有性能结论都是推断，落地前需要一份实测基线。建议一次性测完，做成 `docs/preview-measurement-report.md`：

* **样本集** 固定 20 个真实包，覆盖：纯 FBX 单模型、带 lilToon 的 prefab、多 shader 大包、含 BC7 DDS 的包、只有 prefab 无模型的包、`!` 嵌套 zip 包。
* **指标** 首次预览：解包耗时、GLB 生成耗时、shader 编译总时长与失败率、首帧可见时间；缓存命中预览：同一组数字；稳态：GPU 占用、显存峰值、WebView2 与 sidecar 各自内存；B2 前后各测一次显存。
* **对照** 同一批样本在 VRCAL 1.4.0 里跑一次（它只吃原始模型文件，因此对照组限定在有可直接预览模型的样本上），量「首帧时间 / 交互帧率 / 是否需要落盘」三列 — 这组数字能直接回答「我们这条更重的链路值不值」。
* **口径声明** 测完在两份文档里都注明测试机、显卡、是否 WARP、素材包版本，避免「我这边不卡」的争论。
* **落地的记录方式** 沿用 `DEVELOPMENT.md:305/:311` 那种写法 —— 「全库 89 个包、抽 229 个材质跑一遍球渲染是 225 成功 / 4 二进制 / 0 其他失败」这类句子就是本项目最好的回归资产。每条待办完成时，把改动前后的同组数字写进对应小节，而不是只在这里打勾。

---

## 附录 · 关键代码位置索引

| 主题 | 位置 | 分级 |
| --- | --- | --- |
| fxc 查找 / 参数 / 超时 | `src/Akashic.Host/preview/Hlsl/FxcTool.cs:16-38、43-54、76-92` | 甲 |
| include 根目录构造 | `src/Akashic.Host/preview/Hlsl/ShaderCompileService.cs:326-334` | 甲 |
| 编译缓存版本与 key | 同上 `:13、:72-74` | 甲 |
| 关键字白名单 / DIRECTIONAL | 同上 `:336-337、:423-424` | 甲 |
| 全局串行门 | 同上 `:23` | 甲 |
| LightMode 过滤 | 同上 `:311-316` | 甲 |
| 缓存族 GC 范围与配额 | `src/Akashic.Core/Services/Preview/PreviewCachePaths.cs:6、8-10、45、55、142-144` | 甲 |
| 预览 HTTP 端点 | `src/Akashic.Host/Api/PreviewEndpoints.cs:34-158` | 甲 |
| three.js renderer / 帧循环 / 栅格释放 | `src/Akashic.Host/preview/viewer.html:148、:891、:188-189` | 甲 |
| HLSL 帧显示与 CSS 缩放 | 同上 `:108、:40、:452` | 甲 |
| Flutter 侧轮询与 WebView 重建 | `app/lib/src/pages/preview_page.dart:218-266、:880-881、:281-288` | 乙 |
| ShaderLab 扫描 / UsePass 深度 / 关键字暂存 | `src/Akashic.Core/Services/Preview/Shader/ShaderLabExtractor.cs:104、:365-374` | 乙 |
| Unity 兼容层降级点（雾空宏 / gamma 写死 / SH 常量 / 点光与顶点光返回 0 / 阴影恒 1） | `.../Shader/Resources/UnityShim.hlsl:51-55、:199、:440-448、:460-476、:574` | 甲 |
| shim 释放与当前版本常量 | `src/Akashic.Core/Services/Preview/Shader/ShaderEnvironment.cs:7、:19-31` | 甲 |
| 材质关键字进入编译 / debug 与三档 fallback / 渲染串行门 | `src/Akashic.Host/preview/Hlsl/HlslPreviewService.cs:73-79、:96-122、:110、:127-137` | 甲 |
| 官方边界与既有实测记录 | `README.md:126-128`；`DEVELOPMENT.md:296-297、:305、:311、:344、:349、:382-383` | 甲 |
| 回退 shader 三档 | `src/Akashic.Host/preview/Hlsl/FallbackLit.hlsl:45-74` | 乙 |
| D3D11 渲染器（顶点布局 / 贴图 / 状态 / 出图） | `src/Akashic.Host/preview/Hlsl/D3D11PreviewRenderer.cs:430-465、:475-496、:801-808、:814-838、:894-982、:1040` | 乙 |
| prefab / GLB 生成 | `src/Akashic.Core/Services/Preview/PrefabParser.cs:308-331、:617-745`；`GlbBuilder.cs:156-198、:228、:243-284` | 乙 |
| unitypackage 解包缓存 | `src/Akashic.Core/Services/Preview/PackageContentCache.cs:156-175、:256-307` | 乙 |

对照项目 VRCAL 的可借鉴实现（本机已克隆并完成 1293 测试全绿）：预算预检与手写 FBX/tar 解析在 `src/main/model-preview-files.ts`，blob 生命周期与取消竞态在 `src/shared/model-preview.ts` 的 `ModelPreviewResources` 与 `src/renderer/model-viewer.ts` 的 `generation/staged` 事务，按需渲染与失焦停帧在 `model-viewer.ts` 的 `tick`，沙箱窗口与逐次重新授权在 `src/main/model-preview-window.ts`。

---

## 八 · 与主文档的衔接

* 本文只列**待办与方案**，不替代 `DEVELOPMENT.md` 的实现要点：那里的 `### Prefab 预览` 记的是「已经踩过的坑和为什么这样写」，本文记的是「还没做的事」。建议在该小节开头加一行指向本文（我没有替你改 `DEVELOPMENT.md`，因为这条改动属于你的文档编排习惯）。
* 每完成一条，把编号（`A1`/`B2`…）连同改动前后的实测数字回写进 `DEVELOPMENT.md`，本文这边只把状态标成「已完成 + 日期」，避免两处正文互相抄写导致漂移 —— 这正是 C4 那一类问题的成因。
* 本文的行号会随代码演进失效。动某条之前，先按附录的「关键代码位置索引」重新定位一次；标 **乙类** 的条目尤其不要直接照抄行号。
