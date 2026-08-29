## 背景

raylib 自研引擎当前定位是最小验收引擎。2026-08 做了一轮全面工程化评审：分层纪律与测试工程化是强项——依赖方向被 `src/Tests/ArchitectureTests/CoreBoundaryTests.cs` 机器强制，渲染库零 Core 依赖，Gallery/AssetAcceptance 两个 app 只拿渲染库即可驱动；短板集中在帧组织、资产生命周期、GPU 驻留与内存治理。评审由 ZCode 主审并逐条复核证据，codex 与 pi（claude opus）做两轮交叉评审，分歧已全部裁定（见「设计决策记录」）。

## 子 issue

> 状态速览（2026-08-29 收官）：#1322/#1323/#1324/#1325/#1326/#1327/#1328/#1331/#1332 已完成（PR 依次为 #1338/#1352/#1364/#1369/#1359/#1375/#1377/#1379/#1379）；#1329 spike 负结论关闭；#1330 挂起（等 vendored 绑定扩展政策）。


- [x] #1322 —— W0 计量守卫（前置小包）
- [x] #1323 —— W1a 帧执行单一化（唯一生产路径 + pass 漂移机器校验）
- [x] #1324 —— W1b HostLoop 拆分：输入路由
- [x] #1325 —— W1c HostLoop 拆分：证据采集与诊断 HUD
- [x] #1326 —— W2 能力接线修复包（阴影/互斥/合同/schema/文档）
- [x] #1327 —— W3a 资产句柄与租约
- [x] #1328 —— W3b 两阶段异步加载 + bootstrap 闸门
- [x] #1329 —— W4a 持久实例 buffer spike
- [ ] #1330 —— W4b 持久 buffer 推广（删 fixed 指针上传）
- [x] #1331 —— W4c lane 内逐实例 frustum culling
- [x] #1332 —— W5 文档漂移清扫

## 现状关键事实（均有代码证据）

1. **帧序三份表达且互不一致**：`RaylibHostLoop.cs`（约 2450 行 internal static class）内联执行真实帧序列，主循环单方法约 880 行；`RaylibFrameRenderer.cs`（pass 枚举 `:26-44`、`BuildPassPlan` `:204-272`）生产路径零实例化、仅测试引用；`BuildPassPlan` 缺水面双 pass 与 NavMesh overlay，也不被 `RenderFrame` 消费。
2. **后处理与水面互斥**：`RaylibHostLoop.cs:624` `postProcessWorldFrame = !waterFboEnabled`，水面 FBO 开启时调色 pass 直接熄灭。
3. **阴影能力存在但主宿主未接线**：`RaylibDirectionalShadowMap.cs`（2048 单级正交）在引擎画廊使用，主宿主 RaylibHostLoop 中 shadow 零命中；`gitbook/architecture/raylib-render-code-shape.md` 关于宿主组阴影帧的描述与代码不符。
4. **实例矩阵不驻留 GPU**：`RaylibPrimitiveRenderer.cs:2337` 每次 `DrawMeshInstanced` 从 CPU 裸指针上传；绑定层已有 `UploadMesh(dynamic)` / `UpdateMeshBuffer`（`src/Libraries/Raylib-cs/Raylib.cs:574,577`）可作持久 buffer 通道，但无任何调用。
5. **资产全同步懒加载、缓存只进不出**：模型/贴图在 draw 路径同步加载（`RaylibPrimitiveRenderer.cs:1616-1671`、`:1760-1813`）；无引用计数、无运行时逐资产卸载；同 URI 被 PrimitiveRenderer / MaterialLibrary / VfxRenderer 各自缓存、各上传一份；负缓存导致文件补齐后须重启进程。
6. **native 内存不可观测**：全仓库无 `GC.AddMemoryPressure`、无进程/VRAM 计量；alloc 指标大多只入报告不设阈值。对照已有的正确做法：`src/Tests/PresentationTests/Presenter/PresenterTimerBenchmarkTests.cs:35` 有 0-alloc 硬断言，`artifacts/benchmarks/presentation-skia-hotpath/benchmark-report.md` 实测 0.0 B/帧，但 50k HUD 的 382.7 B/帧无阈值防线。
7. **文档/合同漂移**：`assets/Presentation/host_assets.schema.json` 的 assetKind enum 漏 `Sound`（运行时 loader 已支持）；`gitbook/architecture/gpu-skinned-instancing-and-offline-retarget.md` 声称 FBX 仅离线转换，实际 `RaylibModelFileConverter.cs:22` 运行时转换；`gitbook/architecture/runtime-overview.md` 的 SystemGroup 相位数落后于 `ArchitectureGuardTests.cs` 锁定的 11 相。
8. **已有的正确地基（复用，不重造）**：资产合同 `IRenderMeshAssets` / `IRenderMaterialAssets` 定义在 Platform.Abstractions（`RenderServiceContracts.cs:7,21`），由 Core 的 `MeshAssetRegistry` 实现并注入渲染器——资产所有权边界已由接口定好；实体级剔除/LOD 已有 `CameraCullingSystem` 写 `CullState`；地形 chunk 已有 240 帧未用逐出（`RaylibTerrainRenderer.cs:512-531`）。

## 工作流（按执行顺序）

### W0 计量守卫（前置小包，零行为风险）
- native 资源（Texture/Model/Mesh/Shader/RT）创建与释放处登记 resident bytes 与计数，可查询、可诊断。
- 为关键 benchmark（HUD 50k、skia hotpath、dynamic worker）补 alloc/resident 阈值断言：超阈值 CI 失败，不是只写报告。
- 不改任何运行行为，不改 GC 配置。

### W1 帧执行单一化 + HostLoop 拆分
- `RaylibFrameRenderer` 长出水面双 pass、NavMesh overlay、诊断 HUD 能力后，成为唯一生产执行路径；HostLoop 删除内联帧序；`BuildPassPlan` 被真实消费。
- 机器校验：全部可选开关组合下，声明的 pass 顺序与真实执行顺序一致；缺 pass、重复 pass、UI 提前执行均测试失败。
- 从 HostLoop 拆出输入路由、证据采集（截图 / FrameCapture）、诊断 HUD；截图与合成回放依赖的时序基准（runtimeStopwatch、frameIndex，以及 输入 → Core Tick → Present → EndDrawing → 取证 的链路）作为显式参数传递，不拆散语义。
- 既有确定性截图 / 回放 CI 全部不回归。

### W2 能力接线修复包（纠正性、低成本；为后续重构提供一条能正确出图的帧）
- 方向光 + 阴影 pass 接入主宿主：没有光源驱动的阴影是空转，两者一起接；主宿主截图可观察到阴影。
- 后处理/水面互斥修正（按 W1 的新帧结构落）。
- 错误策略统一合同（材质/skinned fail-loud 与 mesh/billboard Warn+跳过 二选一定稿）+ 负缓存失效语义：合同在本包定稿，实现随 W3 落地。
- schema 补 `Sound` 并对齐运行时校验；修正 `raylib-render-code-shape.md` 阴影声称与 `gpu-skinned-instancing-and-offline-retarget.md` FBX 声称。
- 异质内容允许分多个 PR 落地，一个子 issue 跟踪。

### W3 资产生命周期
- 分层：Core 管描述符/VFS 合同（对齐 `MeshAssetRegistry` 模式）；后端资源状态与句柄放 `Ludots.Raylib.Render`；Adapter 负责注入。GPU 类型不进 Core。
- AssetHandle 状态机：`Unrequested → Preparing → CpuReady → UploadQueued → Resident / Failed`，失败原因可观察。worker 只做 VFS 解析/读取/转换，GL 资源创建只在渲染线程。
- 句柄租约 + 后端引用计数 + GPU 帧延迟销毁 + Dispose 顺序防 double-free；同 URI 全局去重，跨 PrimitiveRenderer / MaterialLibrary / VfxRenderer 只驻留一份。
- 启动期同步预加载定义为显式 bootstrap 闸门状态：Loading 态的被定义行为，一次性、可观察、fail-loud；运行态禁止 draw-time 同步加载。不做同步 fallback 开关。
- 负缓存失效：文件补齐或版本变化后，句柄可重入 Preparing，不须重启。

### W4 GPU 驻留与逐实例剔除
- 持久实例 buffer spike（先行）：单条静态 lane 用 `UploadMesh(dynamic)` + `UpdateMeshBuffer` 走 mesh-buffer 通道，不碰裸 rlgl 句柄、不在绑定层造新耦合；画面与 CPU 指针路径一致；软件 GL 路径明确报告不支持，不静默回退；buffer/VAO/材质 Dispose 全过测试。
- spike 通过后推广到 typed lane 与 ISM，删除 `fixed` 指针上传路径。
- lane 内逐实例 AABB frustum culling：消费现有 `CullState`，不改写 Core 实体可见性，不造第二套实体级 culling SSOT。

### W5 文档漂移清扫（只收历史欠账，收完即关）
- 本 epic 开始前已存在的代码-文档漂移统一修正（含 `runtime-overview.md` SystemGroup 相位数）。此后文档变更并入对应工作流的验收项，不长期单独漂浮。

## 非目标（继续暂缓）

- RHI 抽象 / render graph / 多后端：最小验收引擎定位下未到收益点；W1 的唯一帧执行合同为将来引入留出挂点。
- 贴图压缩 / 纹理图集 / mipmap / mesh LOD 离线管线：与资产上传策略耦合，待 W3 落地后按上传质量与内存预算统一设计，不顺手单独加。
- MSAA / TAA / bloom、点光/聚光：非验收必需。
- GC 模式（ServerGC / 堆上限）：先靠 W0 计量表征 pause、工作集与 native 峰值，实测后单独决策，不进本 epic 默认项。

## 设计决策记录（三方评审裁定）

评审人：ZCode（主审 + 证据复核）、codex（gpt-5.6-sol）、pi（claude opus）。流程：独立评审（Round 1）→ 六项分歧裁定（Round 2），全部收敛：

1. **顺序 W0→W1→W2→W3→W4**。接线修复先于资产重构：资产是分配与生命周期模型的重构，必须在一条能正确出图（含光照阴影）的帧上验收，否则资产 bug 与接线 bug 混在一起无法归因。（codex 原案，pi Round 2 改判认可。）
2. **GC 配置与计量分离**：前置的是计量不是配置。（pi Round 2 撤回 ServerGC 前置主张。）
3. **不做同步 fallback 开关**：启动期同步预加载定性为 bootstrap 闸门状态，不是回退分支。
4. **阴影接线放帧收敛之后、资产之前**。（D4 双方一致。）
5. **mipmap 归入上传策略/离线管线**，不随首次加载顺手加。（一致。）
6. **粒度**：6 个工作流为父项；W1、W3 落地时再拆窄实现 issue；文档对齐并入代码 issue 验收项，W5 只收历史欠账。（codex 提出父项+窄拆，pi 补充 W5 限定。）

压力测试「只保三个工作流」时双方收敛于同一脊柱：W1（帧序）、W2（接线）、W3（资产）；W0 可折叠进 W1 开头。

## 验收纪律（全工作流通用）

- 仓库现行门禁全部保持：ArchitectureTests / CoreBoundaryTests（Core 禁引渲染侧）、InstancedBatchContractTests、确定性截图证据链。
- 每个工作流拆子 issue 时把验收要点落成 Given/When/Then；行为改动必须带前后截图或 benchmark 对比。
- 禁 fallback / 向后兼容分支适用于全部新增路径，含异步加载与持久 buffer spike。
