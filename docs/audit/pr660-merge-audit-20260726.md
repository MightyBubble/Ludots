# PR #660 合并提交严肃审计报告（merge-commit audit）

- 审计日期：2026-07-26
- 审计对象：远端最新合并提交 `9a6af246cc`（PR #660 → main）
- 审计人：独立审计（Kimi Work agent），与先前两份本地审计（`pr660-architecture-audit-20260725.md`、`pr660-architecture-audit-20260726.md`）相互独立复核
- 审计基准：合并基 `5712a4eef4`；PR head `948640f6aa`
- 审计方法：合并完整性校验 → PR/issue 全演进链阅读 → 按仓库架构规范（AGENTS.md、`gitbook/contributing/ai-assisted-development.md` 任务执行决策规范）逐行精读合并树代码 → 干净 worktree 内 Release 构建与全量测试 → 基线对比归因

---

## 1. 合并完整性验证

| 校验项 | 方法 | 结果 |
| --- | --- | --- |
| 合并提交结构 | `git cat-file -p 9a6af246cc` | parents = `5712a4eef4`（main 基）+ `948640f6aa`（PR head），结构正常 |
| 树保真 | `git diff --quiet 9a6af246cc 948640f6aa` | **合并树与 PR head 完全一致**，main 在 PR 期间未移动，无合并窗口期私改 |
| 规模 | `git diff --stat` | 118 commits / 475 files / +54,175 / −27,285 |
| 合并后 CI | GitHub Checks | 4/4 绿（solution-verify、docs-governance、pages、camera-baseline，2026-07-26T10:50:40Z） |
| CI 覆盖盲区 | 读 `.github/workflows/solution-verify.yml` | CI 只跑架构守门 + `TestCategory=arch-guard` 的 GasTests 子集，**不跑全量 GasTests**（见 §5 后果） |

结论：合并动作本身在 git 层面干净、无夹带。

## 2. issue 演进链与流程卫生

演进链（依 GitHub 时间线）：

1. `#644` epic 起步，多轮审计（二次/三次/三方/严格/final）。
2. 07-19 SSOT 收口至 `#689`，`#644–#688` 批量关闭（`state_reason=completed`，但关闭评论自称"迁移历史≠验收完成"）。
3. 07-24 多票 "Status correction" 评论：这些票不应视为完全闭合，残留归 `#669`。
4. 07-25 `#669` 以 "superseded by #689" 关闭。
5. **PR 正文自报"未完成收口，不要当作可合并依据"；`#689` 正文同样写明"未收口、不得标完成、等待远端审计"。**
6. 07-25 本地独立审计（`pr660-architecture-audit-20260726.md`，针对最终 head `948640f6aa`）给出 **83 分 B"不建议合并"**，列出 5 项合并前最少动作。
7. 07-26 10:50:37 合并发生；**38 秒后 `#689` 以 completed 关闭**，关闭评论承认"审计残留不阻塞本次合并，后续另开 issue/PR 处理"。
8. **截至本审计时点，承诺的后续跟踪 issue 未创建**（API `since=2026-07-26T10:50:00Z` 仅返回 PR660 与 #689 本体）。

流程裁定（对照"任务执行决策规范"中 SSOT 与收口纪律）：

- **合并行为违背本 PR 自己的 SSOT 声明**：PR 与 #689 双双声明"未收口/等待远端审计"，却在未做远端审计、独立审计明示"不建议合并"的情况下合并。流程违规成立。
- `#644–#688` 以 `state_reason=completed` 关闭与"迁移历史"口径自相矛盾（completed 语义=验收完成），issue 卫生失真，至今未纠正。
- 合并承诺的残留跟踪票未建，残留项（见 §6）目前处于无主人状态。

## 3. 逐域代码审计（合并树逐行精读）

以下文件在合并树 worktree（`.worktrees/audit-pr660-merge @ 9a6af246cc`）内逐行精读，无一遗漏核心 diff。裁定基于：六边形分层、四个禁止（禁 fallback/禁向后兼容包袱/禁重复造轮子/禁跨越职责）、ECS 硬约束（纯数据组件、结构变更 CommandBuffer 化、static readonly Query、固定容量、确定性）。

### 3.1 Core 生产代码（裁定：优）

| 文件 | 要点 | 裁定 |
| --- | --- | --- |
| `EffectPhaseSideEffectTransaction.cs`（新 1,605 行） | staging→validate→commit→rollback 教科书实现；读路径先查 staged 状态 | 优 |
| `OrderAdmissionResults.cs`（新 725 行） | 双代固定容量 admission + 整批预约事务 + 终态故障合同 | 优 |
| `EffectProposalProcessingSystem.cs` | #673 监听器缓存改 revision 驱动；连锁单 admission 化；prompt+OrderRequest 预检同事务 | 优 |
| `AbilityExecSystem.cs` | 热路径 `World.Add/Remove` 全改 CommandBuffer；快照固定容量；终态合同完备 | 优 |
| `OrderContinuationSystem.cs` / `OrderBufferSystem.cs` | 代际驱动、整批预约、actor 销毁发终态、整批 admission 事务 | 优 |
| `OrderSubmitter.cs` / `OrderQueue.cs` | 黑板副本 prepare→finalize→commit 替换事务；GlobalIntake admission 化；Clear 遇载荷 throw | 优 |
| `InputOrderMappingSystem.cs` | #651 actor 固定+鉴权+可重入；`Array.Resize` 倍增全删改 fail-fast | 优 |
| `EffectLifetimeSystem.cs` / `EffectApplicationSystem.cs` / `EffectProcessingLoopSystem.cs` | 三相一位于单一事务；persistent 挂接事务化（ResolveOrder 排序、RollbackPersistentAttachment、容器回收、listener 容量预检）；三相共享 work-unit 预算带不变量校验 | 优 |
| `EffectPhaseExecutor.cs` / `EffectTagContributionHelper.cs` | scratch 容量配置化+越界 throw；config context try/finally；tag 授予/撤销/更新全快照回滚，`??= new TagOps()` 删净 | 优 |
| `GasGraphRuntimeApi.cs` | 生产服务强约束注入；派生属性写作用域+副作用拒绝；全部副作用接事务 staging；空间查询统一 world-cm + `SpatialQueryResult`（单位一致性修复）；黑板写静默 skip 改 throw | 优 |
| `RuntimeEntitySpawnSystem.cs` | 关系副作用 preflight-before-drain；批复制→预检→排水（队列变更一致性 throw）；receipt/on-spawn effect 先 Reserve 后 spawn；静默 return 全改 throw | 优 |
| `CompositeOrderPlanner.cs` / `BlackboardStoredTargetOps.cs` | bool→`OrderSubmitResult` 类型化；续接注册事务化（失败释放载荷）；黑板写 prepare/commit + 歧义目标/死目标/容量全 throw | 优 |
| `GraphPathComponents.cs`（#674） | class→struct + `InlineArray(128)`，EnsureCapacity/`Array.Resize` 删净 | 优 |
| `ChunkedNodeGraphStore.cs`（#675） | chunkKeys 排序+去重（字典枚举序消除，确定性达成）；cross-edge 目标缺失/TagSetId 越界静默 continue/default 改 typed throw | 优 |

机械扫描（全部新增行，Core 13,129 行 + mods 1,940 行）复核：

- `?? new TagOps()`：src/Core、src/Tests、核心 mods **零命中** → #667 全闭合。
- `Array.Resize`：src/Core 仅剩 `Association/EntityKeyedSoaTable.cs`（既有基础设施，非本 PR 范围）。
- 规划层 `VisualTransform`：`OrderWorldSpatialResolver`/`CompositeOrderPlanner` 删净（#672 规划层闭合）。残留 3 处（`CommandSourcePointerHitResolver`、`CommandSourceAcquisitionSystem`、`GameplayCueSystem`）均为本 PR 未触及文件，属合并声明延后的已知残留。
- Legacy 枚举 4 处命中均为"删除旧枚举 + fail-fast throw"，合规。

### 3.2 mods 关键补丁（裁定：优）

- `LocalOrderSourceHelper` / `InputInteractionContextAccessor` / `MobaLocalOrderSourceSystem`：scratch 容量统一从 `GameConfig.gasRuntimeCapacity.commandIntentScratchCapacity` 注入（缺失/非正即 throw）；自建 `GasGraphRuntimeApi` 改取引擎生产实例；接 `InputOrderActorAuthorization` 鉴权；collection 拷贝容量语义 `RejectedAdmissionCapacity`。
- `CollectionGasEntityCommandPanelSource`：`ActivateSlot` 从"仅首成员"改"全部存活成员逐个激活"，成员级 typed result 聚合、aiming 成员拒止、**`MemberScratchCapacity` 满容 throw**。
- `GasEntityCommandPanelSource`：激活结果全类型化（`InputOrderActivationResult`）、playerId 校验、`WouldEnterUiAiming` 探测、`ItemGrantedSlotBuffer` 并入 revision 哈希与 `AbilitySlotResolver` 统一解析。
- `EntityCommandPanelController`/`Runtime`：激活结果落 runtime 并在页脚展示 Rejected/EnteredAiming/Submitted(OrderId)。
- `RoadMoveOrderExpander`：bool→`OrderSubmitResult`；失败路径 `OrderSpatialPayloadOps.Release`（单条与整批均释放）；不再把非队列满失败谎报为 queue-full。
- `VisualBenchmarkRuntime`：知识投影受众所有权跟踪 + 完整拆卸（服务移除+实体销毁）。
- `RtsRelationRuntimeSystem`（857 行重写）：static readonly QueryDescription 落地；Query 回调内结构变更全改 CommandBuffer + Flush；收集-排序-处理确定性管线（WorldId/Id/Version 堆排序+tie-break）；父子关系全前置校验 typed throw；`?? new TagOps()` 删除改服务缺失即 throw。

### 3.3 测试代码（裁定：良，一处漏改）

- 新增守门测试文件 8 个（`ArchitectureGuardTests` 41、`GasExecutionBudgetTests` 30、`TagStateInstallationContractTests` 13、`GasProductionWiringIntegrationTests` 9 等），断言真实行为（`Assert.Throws`+消息内容、容量边界对"第 16 片提交/第 17 片前置失败"成对断言、admission 精确 `RejectedAdmissionCapacity`），非恒真摆设。
- `#689` 宣称的 7 项新门禁中 6 项 PASS；**Collection `MemberScratchCapacity` fail-fast 代码在、锁定测试仍无**（前审计 WEAK 项，合并后仍未补）。
- 一处测试夹具漏改导致合并树上 1 个确凿红测试（见 §5.2）。

### 3.4 文档与配置

- `gitbook/architecture/gas-order-input-runtime-contract.md`：新运行时合同文档随合并落地，优。
- `assets/Configs/game.json`：新增 16 项 `gasRuntimeCapacity` 容量字段，配置驱动替代硬编码，优。
- `.github/workflows/solution-verify.yml`：新增 3 个守门测试过滤器步骤，良。
- `artifacts/gas-composition-gate.md`：**乱码未修**——第 1 行 `鈥?`、第 500 行起大段双重编码中文（前审计已点名，合并树仍在）。
- `artifacts/ci-audit/pr660/result.md`/`result.json`：已更新为 final closeout（取代 832481e 旧证据），但**证据系 pre-push 外部 worktree 产出**，且自述"final gate PASS for the final working tree that will be committed and pushed after this evidence update"——最终提交 `948640f6aa` 晚于证据（见 §5.2 后果）。

## 4. 独立跑测（本机 Release，干净 worktree）

环境修复：本机 Kimi 环境缺 `ProgramFiles(x86)` 环境变量导致 NuGet 机器级配置解析崩溃（`Value cannot be null (Parameter 'path1')`）；补注入后构建/测试正常。此为主/基线两树共性问题，非 PR 引入。

### 4.1 构建

| 树 | 结果 | 警告 |
| --- | --- | --- |
| 基线 `5712a4eef4` | 0 错误 | 1,547 |
| 合并 `9a6af246cc` | 0 错误 | 1,558 |

净增 +11，全部为长构造函数链 nullable（CS8625/CS8618/CS8600 类），无新警告类别；仓库基线本非零警告（1,547），属既有风格的边际增量。

### 4.2 ArchitectureTests（合并树）

**188/188 通过**（1 m 29 s）。与 ci-audit 证据一致，较基线 +3（新增 3 个守门测试类）。

### 4.3 GasTests 全量（合并树，切片并集全覆盖 ~2,020 用例）

| 切片 | 结果 |
| --- | --- |
| Features | 275/276，**1 红**（见 §5.2-a） |
| Integration | 31/31 |
| Presentation | 111/111 |
| Production | 184/184 |
| Physics2D | 39/39 |
| GAS.Effect | 71/71 |
| GAS.Ability | 71/72，**1 红**（见 §5.2-b） |
| GAS.Map / GAS.Graph / Vision / Spatial / Association / Terrain / MovePlanOrder | 全绿 |
| Config | 184/185，**1 红**（见 §5.2-c） |
| 兜底剩余（排除以上全部 token） | 986/1001，**15 红**（见 §5.2-d） |

基线归因（同过滤器对跑基线树 `5712a4eef4`）：

- 兜底切片基线 **39 红**（含合并树仍红的 15 个全部）→ **PR 净修复 24 个既有失败测试**。
- 15 个兜底红 + Config 组 1 红，全部为 `AllocatesZeroAfterWarmup` 型零分配断言，**基线同红**，属本机环境性既有失败，与 PR 无关。

## 5. 发现清单

### 5.1 流程发现（严重度：高）

- **F1 合并违背自身 SSOT**：PR 正文与 #689 均声明"未收口/等待远端审计"，独立审计 83 分"不建议合并"在先，合并在后；合并 38 秒后以 completed 关闭 #689；承诺的残留跟踪 issue 至今未建。
- **F2 issue 卫生失真**：#644–#688 `state_reason=completed` 与"迁移历史≠验收完成"口径矛盾，未纠正。

### 5.2 测试发现（严重度：a 高，b/c 中，d 记录）

- **a. `MobaLocalOrderSource_ResolvesCallerSuppliedTargetCollectionKey`：确凿 PR 回归。** 基线绿、合并红。最终提交 `948640f6aa`（合并前最后一次 push）在 `MobaLocalOrderSourceSystem` 构造函数新增 `GameConfig.gasRuntimeCapacity` 硬要求，但未更新该测试夹具（`InputOrderContractTests.cs:341` 的 `GameConfig` 未带 `GasRuntimeCapacity`），构造即抛 `InvalidOperationException`。**该提交晚于 ci-audit 证据产出 → PR 自报"2011/2011 全绿"证据对最终 head 过期**；合并后 CI 不跑全量 GasTests，故未暴露。
- **b. `SkillMappingOverrideResolver_TracksAllocatedZeroAfterWarmup`（PR 新增自设门禁）合并树红**：warmed 路径实测分配 24 B。与本机 16 个既有环境性零分配失败同型（同为 24 B 量级、基线同红类），疑似环境性，但无法在本机证伪；即便是环境性，它与 a 共同证明**最终提交后未重跑全量套件**。
- **c. `MaintenancePolicy_ExpiresAndCompactsChurnedViewerTargetsWithinConfiguredBounds`**：基线同红，既有环境性失败，非 PR 引入（记录）。
- **d. 15 个零分配失败**：基线同红，既有环境性（记录）；同时确认 PR 净修复同切片 24 个既有红测试。

### 5.3 残留发现（严重度：中/低）

- **R1 `artifacts/gas-composition-gate.md` 乱码仍在**（双重编码，前审计点名未修）。中。
- **R2 Collection `MemberScratchCapacity` fail-fast 无锁定测试**（前审计 WEAK，未补）。中。
- **R3 ci-audit 证据为 pre-push 产出**且最终提交在其后，"final closeout"措辞与事实有出入。中。
- **R4 规划层外 3 处 `VisualTransform` 读残留**（合并声明延后项）。低。
- **R5 Core 净增 11 个 nullable 警告**。低。
- **R6 CI 不覆盖全量 GasTests**（本 PR 新增守门过滤器有改善，但全量回归无门禁）。低→中。

## 6. 评分

| 维度 | 权重 | 得分 | 依据 |
| --- | --- | --- | --- |
| 六边形架构 | 20 | 19 | 分层干净，mods 全走 Core 服务/合同；长构造链继续膨胀为既有风格微瑕 |
| 四个禁止 | 20 | 20 | `?? new TagOps()`/热路径 `Array.Resize`/静默 skip 全清；无 fallback、无平行 runtime、无重复造轮子 |
| ECS 硬约束 | 20 | 20 | 结构变更 CommandBuffer 化、static readonly Query、固定容量、InlineArray struct 化、确定性排序、快照回滚事务，逐行核实 |
| 测试与证据 | 20 | 12 | ArchitectureTests 188/188 实跑绿、净修 24 红、新门禁断言扎实（+）；但合并树 1 个确凿 PR 回归红、自报证据对最终 head 过期、Collection scratch 门禁仍无测试、CI 无全量门禁（−） |
| 文档与自审 | 10 | 6 | 新合同文档与容量配置落地（+）；自审 gate 文件乱码未修、ci-audit"final"证据实为 pre-push（−） |
| 规模与流程卫生 | 10 | 4 | 475 文件巨型 PR 违背自身"未收口"声明合并、#644–688 completed 口径失真、承诺跟踪票未建（−−） |
| **总分** | 100 | **81** | |

## 7. 最终结论

**评分：81 / 100（B）。代码本体 A 级，合并流程不合规。**

1. **代码质量结论**：合并树的工程本体是高质量的。交易化（staging/validate/commit/rollback）、类型化失败（`OrderSubmitResult`/`InputOrderActivationResult`）、容量配置驱动、确定性（排序/代际/固定容量）、副作用禁戒（派生属性写作用域、事务内 staging）逐行核实到位；四个禁止与 ECS 硬约束在全部精读文件中零违规；净修复 24 个既有失败测试。**若只评代码，95+。**
2. **流程结论**：合并行为本身违反仓库 SSOT 纪律——在 PR 与 #689 均自述"未收口"、独立审计 83 分"不建议合并"的情况下合并，合并后以 completed 关闭 #689，承诺的残留跟踪票至今未建。**流程违规成立。**
3. **当前 main 状态**：可构建、ArchitectureTests 全绿；GasTests 有 1 个确凿 PR 回归红（`MobaLocalOrderSource_ResolvesCallerSuppliedTargetCollectionKey`，最终提交引入的夹具漏改）+ 1 个疑似环境性的 PR 新增门禁红 + 16 个既有环境性红（基线同红）。main 不是"全绿"状态。

### 必须补做的收口动作（建议立即执行）

1. 修复 `InputOrderContractTests.cs:341` 夹具（补 `GasRuntimeCapacity`），并隔离复跑确认 `MobaLocalOrderSource_ResolvesCallerSuppliedTargetCollectionKey` 转绿。
2. 在干净 CI 环境重跑全量 GasTests，判定 `SkillMappingOverrideResolver` 分配红的真伪；若为真回归则修复 warmed 路径分配。
3. 创建合并时承诺的残留跟踪 issue（至少覆盖：R1 gate 文件乱码、R2 Collection scratch 锁定测试、R4 VisualTransform 3 处残留、R6 CI 全量 GasTests 门禁），并更正 #644–#688 的 completed 口径。
4. 修复 `artifacts/gas-composition-gate.md` 双重编码；将 ci-audit 证据刷新为合并提交后的真实全量结果。

---

附：审计工作产物（本仓库 `output/pr660-audit/`）：`notes.md`（逐文件裁定笔记）、`diff-*.patch` 与 `per-file*/`（按域/按文件切分的全量 diff）、`build-base.log`/`build-merge.log`（构建日志）、`gastests-*.log`（测试日志）、`gh/`（PR/issue API 原始数据）。
