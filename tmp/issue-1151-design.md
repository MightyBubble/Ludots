# Issue #1151「InstancedBatch typed lane 打通」实现前设计决策报告

基线：`C:\001_AI\LudotsProd\tmp\wt-base`（main 干净检出）。所有行号均指向该 worktree。

## 0. 关键事实复核（精读后确认/修正）

| # | 事实 | 出处 |
|---|------|------|
| F1 | typed 请求**不携带 transform 数据**，只有寻址（BatchAssetId/PresenterStableId/Address）+ `InstanceStart/InstanceCount/FinalChunk`。transform 真相在 `InstancedBatchAssetRegistry` 的 group（inline `Transforms` 或外部 `Source` 描述符） | `src/Core/Presentation/Instancing/InstancedBatchRequestBuffer.cs`、`InstancedBatchEmissionSystem.cs:114-126` |
| F2 | 两个 typed buffer 是**帧瞬态**：`GameEngine.Update()` 起始 Clear（:3994-3995），tick 尾部 validator 校验（:4027-4035）。请求在 tick 结束后、下一次 tick 前对 host 可见（渲染阶段可安全消费） | `src/Core/Engine/GameEngine.cs` |
| F3 | progressive submission：`InstancedBatchSubmissionRuntime` 按 `(presenter, stableId, batchAssetId, groupIndex)` 记账，chunk 分帧提交，`Completed` 后不再发；`TotalInstances` 变化自动重置；`Remove(presenter, stableId)` 在 PresenterDestroyed 时清账。**当前无人调用 `MarkDirty`**（Refresh 重提交链路不存在） | `InstancedBatchSubmissionRuntime.cs`、`InstancedBatchEmissionSystem.cs:131-189` |
| F4 | **装配边界硬约束**：`Ludots.Raylib.Render` 只引用 `Platform.Abstractions + Raylib-cs + SkiaSharp`（csproj 已核实），看不到 Core 的 typed 类型；`Ludots.Adapter.Raylib` 同时引用 Core 与 renderer。→ typed 消费器只能放在 **Adapter 层**，或经 neutral 接口注入 renderer | 两个 .csproj |
| F5 | 静态 lane 全链：`PresenterEmitSystem → StableDrawCache（ContentRevision/StaticMeshGeometryRevision/delta 通道）→ PresentationRequestFlushSystem → PrimitiveDrawBuffer(可见) + snapshot buffer → RaylibHostLoop :798 → RaylibPrimitiveRenderer.Draw → RaylibIsmRenderBridge.SyncPersistentLanes（planner 差分）→ bucket → 矩阵缓存(bucket.Revision) → 分块 `Rl.DrawMeshInstanced`（块上限 32768/131072）` | 见文件清单 |
| F6 | 去重防线已存在：`ShouldSkipImmediateDraw`（RaylibPrimitiveRenderer.cs:578）按 stableId 查 `_ismBridge.ActiveBindings` 阻止持久 lane 与动态 lane 双画 | 同上 |
| F7 | 能力位唯一声明点：`RaylibHostComposer.Compose` :105-110（host 组装期，`Decal|Vfx|Surface|NavMeshTileGeometry`）；仅 raylib 声明，Web host 不声明（Web 用 InstancedBatch presenter 会照旧 fail-loud，符合预期）。`RegisterPresentationAdapterCapabilities` 只对 ExternalTargetLifecycle 位做额外 wiring 校验 | `RaylibHostComposer.cs`、`GameEngine.cs:187-194` |
| F8 | 仓库内**没有任何已交付的 `instanced_batches.json`**（fixtures/assets/mods 全查无；仅 `assets/config_catalog.json:305` 注册路径 + presenters.schema.json 有 `instancedBatch` 行为槽）。→ 静默不画的现实风险为零起点，但接线后首个使用者就是首个验收者 | grep 全仓 |
| F9 | ISM 基准两套：① 300k 级 stress scene 走 `RaylibBenchmarkScene`（`BucketCacheOwner.BenchmarkScene`，与持久 lane 互斥占用同一 bridge 桶缓存）；② `PresenterMeshIsmBenchmarkTests`（3k/10k/30k）走 presenter→PrimitiveDrawItem→snapshot→`SyncPersistentLanes`，断言 binding 数==实例数、bucket 数==1 | `RaylibIsmRenderBridge.cs:91,412`、`PresenterMeshIsmBenchmarkTests.cs:130-141` |
| F10 | `IVisualHeightmap` 已有批量 SoA 采样 `SampleHeightsCm(worldXCm[], worldYCm[], outHeightCm[])`，且 renderer 每帧已收到该实例（`Draw(..., visualHeightmap, ...)`）——`groundToVisualHeightmap` 的落地手段现成 | `IVisualHeightmap.cs:9` |
| F11 | 静态 lane 的相机裁剪发生在 Core 侧可见 buffer；bridge 消费的是 **snapshot（全量 resident span）**，即现有 ISM 静态 lane 本身也不做逐实例视锥裁剪。→ typed lane 不做逐实例裁剪不是倒退，是与现 lane 平价 | `retained-static-incremental-projection.md` |

---

## 1. 接法决策：推荐 **方案 A（adapter 独立消费器）**，但以「A′=分层 A」落地

### 1.1 方案对比

| 维度 | A：adapter 独立 typed 消费器 | B：折叠进 PrimitiveDrawBuffer 静态 lane |
|---|---|---|
| 合同符合度 | 完全符合 `instanced-batch-source-contract.md`（Core 拥有语义/数量，adapter 只解析 assetUri、建本地缓存） | **违约**：typed 合同（Address/bucket/span、OperationBuffer 的 span 级操作、CustomDataChannel）在 B 下无下游载体；「不把外部实例展开为一实体一实例」的同等精神在绘制侧被打破（一实例一 PrimitiveDrawItem） |
| 与 StableDrawCache/ContentRevision | 零接触：typed lane 不进 retained 投影，不制造 revision/delta | 50k~300k 实例 → 每实例一个 item；`WriteCustomData`/`SetVisibility` 一次 span 操作 = 全 span item 重写 → `StaticMeshGeometryRevision` 风暴 + planner `ItemEquals` 30 万次 + bucket 矩阵全量重建 |
| 与 StaticMeshAdapterSyncPlanner/持久 lane | 零接触；不需要每实例 stableId（省掉 300k 个 planner 字典项） | 需要合成每实例 stableId（presenterStableId<<32 \| index 之类），侵入 lane key/slot/generation 语义 |
| LOD/裁剪 | 无逐实例裁剪——但与现有静态 lane ISM 平价（F11），LOD 字段本就被 bridge 忽略 | 白得逐实例可见性/裁剪（收益），代价是上面的 delta 风暴 |
| #1152 兼容 | factorized 源 50k 实例 = 一条 lane + 一块矩阵缓冲，adapter 缓存即合同允许的「本地缓存」 | 50k PrimitiveDrawItem（~200B/个 ≈ 10MB+）常驻 snapshot，Web 流（PresentationExtractor）会被迫序列化或跳过 |
| 300k 基准回归风险 | **零**（静态 lane 一行不改） | 高：benchmark 断言 `IsmPrimitiveCount==count` 仍会过，但 retained flush/planner/bucket 路径成本全面上升 |
| 双画风险 | 需一道防线（见 §6.3） | 无（单路径） |
| UE/Unity 未来 adapter | 合同可移植（typed buffer → 各自 ISM 组件） | 合同名存实亡 |

### 1.2 推荐：A′（分层的方案 A）

对 A 的原始描述（「直接读 typed buffer 逐条 DrawMeshInstanced」）做两点收紧，避免它退化成与静态 lane 抢桶缓存的平行轨：

1. **消费器分两层**：
   - **Adapter 层 `RaylibInstancedBatchLaneStore`**（放 `Ludots.Adapter.Raylib`，能看 Core）：每帧 tick 后消费 `InstancedBatchRequestBuffer.GetSpan()` + 读 `InstancedBatchAssetRegistry`，维护 resident 批次（key = `(BatchAssetId, PresenterStableId, Address)` → 矩阵数组 + `FinalChunk` 进度 + visible 位）。**不触碰任何 raylib API**——纯 C# 状态机，模仿 `StaticMeshAdapterSyncPlanner` 的可测性。
   - **Renderer 层 neutral 注入点**（放 `Ludots.Raylib.Render`，不引 Core）：仿照 `IRaylibReceiverMeshProjector` 的先例，新增 `IRaylibInstancedBatchLaneSource`（暴露 resident 批次枚举：meshAssetId/materialId/renderPath/矩阵 span/count/visible）。`RaylibPrimitiveRenderer` 新增 `BindInstancedBatchLaneSource(...)`，在 `DrawPersistentStaticLanes` 之后（`DrawSnapshotDynamicLanes` 之前）统一绘制，**复用** `DrawModelInstanceBatch` 的分块、lane shader、材质管线、阴影路径（`DrawInstancedBucketShadow`）。
2. **不共用 ISM bridge 的桶缓存**：typed 批次自己持有矩阵（resident、按 chunk 写入、Revision 只在 chunk 到达时 +1），绝不写入 `RaylibIsmRenderBridge._bucketMap`——这避开 `BucketCacheOwner`（BenchmarkScene/PersistentSync）互斥重建问题（F9①），也保证 benchmark 零回归。

为什么不是 B：B 的唯一实质收益（逐实例裁剪/可见性）在 typed 合同里本来就由 `SetVisibility` 操作（span 级）表达，后续在 lane store 上实现 span 级开关即可，不需要 per-item 化。

---

## 2. 生命周期设计（「adapter 不拥有实例数」合同下的清理路径）

**所有权边界（定死）**：
- Core（SSOT）：实例总数（inline `Transforms.Length` / `Source.InstanceCount`）、chunk 切分、何时提交、何时 Remove。adapter 只被动接收 `InstanceStart/InstanceCount`。
- Adapter（执行细节）：矩阵数组容量、chunk 写入进度、绘制。lane store 对 `InstanceCount` 的唯一合法用法是「分配本地缓冲 + 验收」：收到 CreateOrUpdate 时若 `InstanceStart+InstanceCount > 声明容量` **必须抛错**（fail-loud，防 Core/adapter 失步），不得自行扩容改语义。

**帧间稳定性**：
- `Completed` 后 `ShouldSubmit` 返回 false → 不再有请求 → lane store 保持 resident，矩阵不重建（Revision 不变 → renderer 矩阵缓存命中，与静态 lane bucket.Revision 机制同构）。
- 渐进期间（未 FinalChunk）：绘制已到达的 chunk 是**允许的**（progressiveSubmission 的存在意义就是分帧摊销）；lane store 记录 `residentCount` 只用于绘制上限。

**源热替换**（配置热重载重注册同 key batch）：`InstancedBatchAssetRegistry.Register` 原位覆盖 asset；`SubmissionRuntime` 因 `TotalInstances` 变化自动重置并全量重发 chunk。lane store 端：CreateOrUpdate 请求流自然覆盖矩阵数组；但**若新 InstanceCount < 旧值**，尾部的旧矩阵会残留 → 必须在 lane store 记录「本 key 上一次的声明容量」，容量变化时整 lane 重置为空再收 chunk（每 key 一行的防御逻辑，合同允许，因为是 Core 声明驱动的）。

**map 切换清理**：presenter 销毁 → `PresenterDestroyed` 事件 → `EmitRemovals` 对每个 group 发 typed `Remove`（finalChunk:true）→ lane store 释放该 key 的矩阵缓冲与 lane 槽。**不需要**额外 map 级钩子——Remove 请求就是唯一清理指令；lane store 补一条防御：收到 `Remove` 时 key 不存在则静默（幂等），key 存在但未 FinalChunk 也可直接释放（Core 已放弃该批）。若 host 整体卸载引擎（进程退出/换引擎实例），lane store 随 Adapter 服务释放，无全局状态。

**操作通道（OperationBuffer）暂不消费**：SetVisibility/WriteCustomData/Effect 等在切片 3 之外；本 issue 只声明请求侧能力位（见 §3），操作侧能力位留到消费实现时再声明——validator 语义正好支持按位渐进。

---

## 3. 能力位

- **声明哪些**：切片 2 只声明 `InstancedStaticMeshBatch` 与 `HierarchicalInstancedStaticMeshBatch`（raylib 的 `DrawMeshInstanced` 对两者绘制等价，Hierarchical 在 raylib 无真分层——照实声明、按 flat 绘制，在文档标注降级语义；若评审认为「照实」要求分层语义，则切片 2 只声明 `InstancedStaticMeshBatch` 并让 Hierarchical 保持 fail-loud，**推荐后者**，更诚实）。操作位（`InstancedBatchVisibility` 等）在实现对应消费时逐位追加。
- **何时声明**：host 组装期（`RaylibHostComposer.Compose` :105），与现有四位并列——**不是**首个使用时。理由：validator 是 tick 尾部 fail-loud 关卡（GameEngine :4027），声明点必须先于任何 tick；且能力位描述的是 host 已装配的渲染路径，host 组装期即已确定。
- **validator 语义不动**。新增防线在声明侧：**能力位与 lane store 绑定必须是同一提交里的原子动作**（声明了位就必须有消费器绑定，绑定检查可做成 composer 内的显式顺序：先 `BindInstancedBatchLaneSource` 再 `RegisterPresentationAdapterCapabilities`，或 composer 内加一条 `ValidateRequiredContextBeforeStart` 式断言），这是防「声明后静默不画」的结构性保证（见 §6.3）。

---

## 4. #1152 接口预留

- **挂靠点**：lane store 的 transform 供给函数。切片 1 把供给接口定为 `TryGetGroupTransforms(batchAssetId, groupAddress, out ReadOnlySpan<InstancedBatchTransform>)`（inline 路径直接返回 `group.Transforms`）。#1152 在 Adapter 侧新增 `FactorizedInstancedSourceCache`：`format=="ludots.instanced_transform_factorized.v1"` 时经 `IRenderAssetPathResolver.TryResolveFullPath(assetUri)`（Platform.Abstractions 已有，正是为此设计）解析 VFS 路径 → 读 JSON → 按 `setId` 取 SoA 数组（positionX[]/positionY[]/positionZ[] + 可选 rotation/scale 数组）→ 缓存为 `InstancedBatchTransform[]`。lane store 只认 span，不感知来源——inline 与外部源在消费器内完全同构。
- **数据形状约束（本 issue 定死，#1152 实现）**：SoA→AoS 展开发生在缓存加载时（一次性），lane store 与绘制路径只见 `ReadOnlySpan<InstancedBatchTransform>`；`instanceCount` 与数组长度不符 → 抛错（Core 声明是 SSOT）。
- **GroundToVisualHeightmap 消费位置**：lane store 把 chunk 的 transforms 转矩阵时（唯一拥有 `IVisualHeightmap` 的时机不必是这里——**定死在 Adapter**：host loop 每帧已从 `CoreServiceKeys.VisualHeightmap` 取实例传给 renderer；lane store 在**矩阵构建时**对启用了该位的 group 调 `SampleHeightsCm`（SoA 批量版）覆写 Y。要点：① 用 Core 服务的 heightmap 实例（Core-owned 真相，符合 gitbook「不是 adapter 私有开关」）；② 采样发生在 chunk 写入时一次性完成，不在每帧绘制时重采样（静态地形假设下正确；地形会变 → 依赖 #1152 之外的 dirty 通知，先不做，文档标注限制）。

---

## 5. 切片计划

### 切片 1：基础接线（无行为变化）
**目标**：neutral 通道贯通 + lane store 骨架，默认空转。不声明任何新能力位。
**文件**：
- 新增 `src/Client/Ludots.Raylib.Render/Rendering/IRaylibInstancedBatchLaneSource.cs`（neutral 接口 + 批次只读结构）
- 改 `src/Client/Ludots.Raylib.Render/Rendering/RaylibPrimitiveRenderer.cs`（`BindInstancedBatchLaneSource` + Draw 持久阶段后绘制空列表 + 阴影路径同样接线 + LastInstanced* 统计口径并入）
- 新增 `src/Adapters/Raylib/Ludots.Adapter.Raylib/Rendering/RaylibInstancedBatchLaneStore.cs`（纯状态机：ApplyRequests(span, registry)、resident 字典、容量防御、幂等 Remove）
- 改 `src/Adapters/Raylib/Ludots.Adapter.Raylib/RaylibHostComposer.cs`（构造 store、绑定到 renderer）与 `RaylibHostLoop.cs` / `RaylibFrameRenderer.cs`（tick 后 `store.ApplyRequests(...)`——渲染路径两者共用同一 renderer 实例，绑定自动覆盖验收截图路径）
**测试**：lane store 单测（新 `src/Tests/RaylibAdapterTests/RaylibInstancedBatchLaneStoreTests.cs`：chunk 累积、FinalChunk、Remove 幂等、超容量抛错、容量收缩重置）；`RaylibFrameRendererTests` 补一条空 source 无异常。
**验收**：全量 `PresentationTests` + `RaylibAdapterTests` 绿；任一现有 preset（如 blacksmith showcase）截图与 main 基线 diff 为零。

### 切片 2：typed lane 真绘制 + 能力位
**目标**：inline transforms 批次真实可见；声明 `InstancedStaticMeshBatch`（Hierarchical 是否声明按 §3 推荐只声明 flat 位）。
**文件**：切片 1 全部文件补绘制实现（复用 `DrawModelInstanceBatch`/`DrawMeshInstancedShadow`，抽出共享 helper）；`RaylibHostComposer` 能力位；新增示例内容（`instanced_batches.json` + 一个演示 presenter，建议挂在现有 showcase mod，或 `fixtures` 新增最小 preset）。
**测试**：契约测试新增「raylib 声明位后 validator 通过」用例；lane store→renderer 集成测试（小实例数断言 bucket/矩阵数）；声明位但未绑定 source 的 composer 防御测试。
**验收**：新 preset 启动截图（N×M 网格实例，肉眼可见 + 数量断言走 AgentBridge 或 lane store 统计导出）；`PresenterMeshIsmBenchmarkTests`（benchmark 类）数字与 main 对比无回归。

### 切片 3（可选，衔接 #1152）：外部 factorized 源消费
**目标**：`source` 组经 VFS 加载 SoA → 绘制；`groundToVisualHeightmap` 采样落地。
**文件**：`RaylibInstancedBatchLaneStore` 供给函数接 `FactorizedInstancedSourceCache`（新文件）；#1152 的 Core 侧 loader 不在本 issue。
**测试**：fixture 一个 `ludots.instanced_transform_factorized.v1` 资产 + 契约测试（数量不符抛错、heightmap 位改变 Y）。
**验收**：5 万级实例 preset 截图 + lane store 统计（提交帧数、内存）写入 artifacts。

---

## 6. 风险清单与防线

1. **300k ISM benchmark 回归**：防线 = 方案 A′ 不改静态 lane 任何一行（planner/bridge/矩阵缓存不动）；typed 批次不进 bridge 桶缓存（避开 `BucketCacheOwner` 互斥重建，F9①）；切片 2 验收显式跑 `PresenterMeshIsmBenchmarkTests` 对比。
2. **双重渲染**：typed lane 的实例**不会**出现在 PrimitiveDrawBuffer（emission 只写 typed buffer，无 assetBinding 则无 item）——结构性无双画；但作者若给同一 presenter 同时配 assetBinding 子级 + instancedBatch 行为会双画。防线：① 文档写明互斥；② 便宜的结构检查可在 lane store 侧做——同一 `PresenterStableId` 既出现在请求又出现在 snapshot 静态 lane 时 `RenderDiagnostics.Warn`（不抛，因为子级+批混排未来可能合法）。
3. **声明能力位后「静默不画」**：三道防线：① 位与 store 绑定同提交原子化（composer 顺序 + 断言）；② lane store 暴露 `LastAppliedRequestCount/LastDroppedKeyCount` 进 RenderDiagnostics，首个用户可诊断；③ 契约测试锁定「无 capabilities → 仍抛」（现有 `CapabilityValidator_RejectsUnsupportedRenderPathAndOperation` 保持）。
4. **chunk 丢失/失步**：请求 buffer 满会**抛**（`InstancedBatchRequestBuffer.Add`，与 PrimitiveDrawBuffer 的 drop 计数不同）→ tick 内 fail-loud，无静默丢；lane store 的超容量/收缩防御见 §2。
5. **`Refresh`/`MarkDirty` 链路缺失**：当前没有任何代码调 `MarkDirty`（F3），若未来操作通道要支持 Refresh 重提交，lane store 已有的「容量变化重置」防御可复用；本 issue 不实现，风险标注给 #1152 后续。
6. **Hierarchical 位语义**：raylib 无真分层 ISM，按 §3 推荐只声明 flat 位、Hierarchical 保持 fail-loud，防止「声明了但语义降级」的隐性合同漂移。

## 附：关键文件绝对路径索引

- `C:\001_AI\LudotsProd\tmp\wt-base\src\Core\Presentation\Instancing\`（RequestBuffer / OperationBuffer / Asset / AssetRegistry / Addressing / SubmissionRuntime / CapabilityValidator）
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Core\Presentation\Systems\InstancedBatchEmissionSystem.cs`、`InstancedBatchBehaviorSystem.cs`
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Core\Presentation\Config\InstancedBatchAssetConfigLoader.cs`
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Core\Engine\GameEngine.cs`（:2138/:2153 注册，:3994 清buffer，:4027 validator，:187 能力位注册）
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Adapters\Raylib\Ludots.Adapter.Raylib\RaylibHostComposer.cs`（:105 能力位）、`RaylibHostLoop.cs`（:488 tick / :798 draw）、`Rendering\RaylibFrameRenderer.cs`（:375 验收绘制）
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Client\Ludots.Raylib.Render\Rendering\RaylibIsmRenderBridge.cs`、`RaylibPrimitiveRenderer.cs`（:277 SyncPersistentLanes / :401 持久绘制 / :578 去重 / :2081 DrawInstancedBucket / :2365 DrawModelInstanceBatch）、`StaticMeshAdapterSyncPlanner.cs`
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Platform\Ludots.Platform.Abstractions\`（StaticMeshLaneKey / RenderServiceContracts / IVisualHeightmap / PrimitiveDrawItem）
- `C:\001_AI\LudotsProd\tmp\wt-base\src\Core\Presentation\Rendering\PrimitiveDrawBuffer.cs`、`StableDrawCache.cs`、`PresentationAdapterCapabilities.cs`
- 测试：`src\Tests\PresentationTests\Rendering\InstancedBatchContractTests.cs`、`src\Tests\PresentationTests\Presenter\PresenterMeshIsmBenchmarkTests.cs`、`src\Tests\RaylibAdapterTests\RaylibFrameRendererTests.cs`
- 文档：`gitbook\architecture\instanced-batch-source-contract.md`、`retained-static-incremental-projection.md`

**结论一句话**：定案方案 A′——Adapter 层纯状态机 lane store + renderer 层 neutral 注入接口（仿 ReceiverProjector 先例），不触碰静态 lane 与 bridge 桶缓存；能力位在 host 组装期与 store 绑定原子声明（切片 2 仅 flat 位）；#1152 以供给函数 + VFS/SoA 缓存挂靠，grounding 在矩阵构建时经 Core heightmap 服务一次性采样。