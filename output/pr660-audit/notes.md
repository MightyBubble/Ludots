# PR660 合并提交独立审计笔记（审计中）

合并提交 9a6af246cc；树与 PR head 948640f6 完全一致；CI 4/4 绿；合并后未建任何残留跟踪 issue。

## 机械扫描（每一新增行）
- legacy 4 处：均为"删除旧枚举值 + fail-fast throw"——合规（禁止向后兼容的正确执行）。
- `?? new` 3 处：EffectApplication/EffectLifetime/EffectProposal 的 `fanOutBudget ?? new RootBudgetTable(fanOutCommandCapacity)` 兜底——待验证。
- catch 29 处：Transaction 内为 rollback+rethrow（合规）；其余待抽查。
- float 42 处：GAS 属性域为主（与 AttributeBuffer 一致）；`CompositeOrderPlanner.ResolveCastRangeCm` out float 待查。
- dict-enum 1 处：ChunkedNodeGraphStore `_chunks.Keys.CopyTo(sortedKeys,0)` 是 #675 确定性修复本体——合规。
- World.Add/Remove 2 处：EffectApplicationSystem `World.Remove<ActiveEffectContainer>` ×2——待查上下文。
- new QueryDescription 2 处：均 static readonly 缓存——合规。
- 静默默认值 10 处：多为错误文案/计数 null 容错——低风险。
- capacity magic 3 处：命名常量 DefaultCapacity=4096/16384/4096。

## EffectPhaseSideEffectTransaction.cs（新增 1605 行）— 已通读 100%
- 评级：优。固定容量 staging；构造期一次性分配；ValidateCommit 先全量预检（存活/组件/容量/监听器上限），再 Playback CommandBuffer + 分量写回；外部队列 checkpoint+RollbackWrites；世界写回有 original 快照回滚；catch→Rollback→rethrow，不吞异常。
- 结构变更走 Arch CommandBuffer（GameplayAttributeChangedBits/AttributeAggregateDirty/EffectPhaseListenerBuffer 补件）——符合"结构变更经正式回放链路"。
- 小注 1：StagePresentationEvent 在 _presentationEvents==null 时静默 return（表现事件，headless 容错，可接受但属"静默丢弃"边缘）。
- 小注 2：RollbackWorldWrites 的黑板三段（float/int/entity）无 IsAlive 守卫直接 Get——极端时序下回滚自身可抛；低风险。
- 小注 3：ResetAbortedStructuralCommands 在回滚路径 new CommandBuffer——冷路径分配，可接受。

## EffectProposalProcessingSystem.cs（+745/−280）— diff 100% 精读
- 评级：优。#673 闭合：_listenersDirty/MarkListenersDirty 删除，改为 EffectRequestQueue.ResponseChainListenerRevision 驱动的自动重建（TrackResponseChainListenerLifecycle），缓存与世界状态脱钩问题根治（待验证 revision 触发点）。
- 连锁单消费走 admission Reserve/Commit/Cancel/finally，恰好一次结局；payload 释放置于 commit 之后；finally 防双 Commit（注释明确说明顺序意图）。
- 窗口深度/创建数溢出由"计数器+静默丢弃"改为类型化 throw（计数器保留用于遥测）。
- WaitInput：prompt 与 OrderRequest 先双双预检容量再发布（同事务语义），闭合 #651 残留。
- 瞬时内联路径统一进 phase executor；缺 phase runtime 且模板带行为 preset 时 throw（不静默跳过）；ConfigContext 全局态删除，config 显式传参。
- 叠层合并：tag 贡献失败时 stack/effect 快照回滚再 rethrow。
- 构造函数新增必填 fanOutCommandCapacity + IClock（null 即 throw）——容量/时钟注入化。
- `?? new RootBudgetTable` 兜底：仅在未注入 fanOutBudget 时按配置容量自建私有预算——非跨职责 fallback，属"默认私有预算"设计，可接受但记录。

## OrderAdmissionResults.cs（新增 725 行）— 100% 精读
- 优。OrderSubmitResult 11 态全枚举 + IsAccepted/ToFailureReason 完备映射（未知值 throw）；双代固定容量缓冲；Reserve/Commit/Cancel 预约事务；容量耗尽 EnterTerminalFault（fail-fast 不再静默丢结果）；CarryForward 未配对 GlobalIntake 跨代保留；ResetForWorldRestore 保持 OrderId 单调。闭合 #650 核心。

## AbilityExecSystem.cs（+754/−261）— diff 100% 精读
- 优。#669 P0 闭合：热路径 World.Add/Remove<AbilityExecInstance> 全删，改 _structuralCommands CommandBuffer 在切片边界回放；EnsureTagComponents（运行期补组件）→ TagOps.RequireTagState fail-fast；正时长 TagClip 缺 TimedTagBuffer 硬失败。
- #649 闭合：启动/终态每路径 FinalizeCurrent（终态+原因），finalize 失败 throw（订单不蒸发）；TerminalReasonMissing/NonTerminalStateFinalized 防呆。
- #646 闭合：exec 快照固定容量（snapshotCapacity 必填），超限 throw 不静默截断。
- 显式目标死亡→类型化失败，不再静默回落 actor（Default 分支仅在未设显式目标时回落）。
- TimedTag 预约 + AddTag 失败 RemoveAtSwapBack 回滚——事务正确。
- tagOps ?? throw（#667 本文件闭合）。
- 小注：ActivateToggle 预检在 AddTag 之前但 tag 添加与 effect 发布非同一事务（注释自承"Finalize/promote 失败留实例下帧重试，ActivateToggle 幂等"）——设计自洽。

## OrderContinuationSystem.cs（+540/−38 重写）— 100% 精读
- 优。由 CompletedOrderSignal 查询改为终态结果代际驱动；整批 continuation 预约 admission、预检投影队列、再 Extract；预检失败整批类型化终态 + admission 结局；actor 销毁钩子在 intake 内外两条路径都为全部 pending continuation 发 Failed 终态（无孤儿）；异常路径 RestoreContinuationOwnership，回补失败也发终态。闭合 #689 "continuation 不静默丢失"。

## OrderBufferSystem.cs（+462/−40）— 100% 精读
- 优。整批 admission 事务：Reserve→Preflight→Dequeue 校验数量→逐单 Commit；预检失败整批发 Failed 终态 + 逐单 admission 结局（无可查缺口）；Peek-before-dequeue 注释说明所有权语义；Expired 释放带终态容量预检；AdmissionResultBufferMismatch 防双 SSOT；直接 SubmitOrder 强制 OrderId>0。

## OrderSubmitter.cs（+835/−100 重写）— 100% 精读
- 优。旧 6 态枚举（Blocked/QueueFull/Ignored/InvalidEntity）删除，换 11 态类型合同——破坏性变更符合禁止向后兼容。
- #680 闭合：激活黑板在副本上 prepare（Spatial/Entity/Int 三类容量预检 RejectedBlackboardCapacity/RejectedMissingBlackboard），旧单 FinalizeActive 发终态后才 CommitPreparedBlackboard——替换事务成立。
- 所有释放路径（清队/Replace/过期/Pending 清除/DropOldest）统一 PublishReleasedTerminal + 容量预检；CompletedOrderSignal 机制删除，终态缓冲为唯一 SSOT。
- ValidateTerminalOutcome 防呆：CompletedWithFailure/FailedWithoutReason/InvalidCancellationReason 全 throw。

## InputOrderMappingSystem.cs（+639/−151）— diff 100% 精读
- 优。#651 闭合：ActivateMappedAction(explicit context) actor 固定 + ActivationActorValidator 鉴权 + OrderIdentityAssigner 必填 + finally 恢复上下文（可重入）；TryActivateMappedAction(bool) 旧签名删除。
- 固定 scratch 容量注入（commandIntentScratchCapacity，默认 4096 命名常量）；Ensure*Scratch 的 Array.Resize 倍增全删，超容 throw（INPUT.ORDER_MAPPING.ERR.*）。
- TryCaptureCollectionEntities 连 List.Capacity 超限都拒绝——防 provider 偷偷扩容。
- 瞄准时换 actor 重入同技能 → RejectedByRule；激活结果三态（EnteredAiming/Submitted/Rejected+原因）替代 bool。
- ActivateMappedAction 预置 RejectedByRule 再执行——"默认拒绝"防御，无路径可漏设结果。

## OrderQueue.cs（+329/−71）— 100% 精读
- 优。GlobalIntake 全路径（单/批/共享/簇）admission 预约+提交；拒绝整批记录且队列零突变（命名即合同 RejectBatchWithoutQueueMutation）；OrderId 发行收口到 AdmissionResultBuffer（身份 SSOT）；Clear() 遇未释放空间载荷即 throw（防载荷泄漏）；TryPeekBatch 支撑 peek-before-dequeue。
- 残留验证（合并树 grep）：`?? new TagOps()` 在 src/Core、src/Tests、核心 mods 零命中（#667 全闭合）；OrderWorldSpatialResolver/CompositeOrderPlanner 的 VisualTransform 已删净（#672 规划层闭合）；VisualTransform 残留仅在未被本 PR 触及的 CommandSourcePointerHitResolver/AcquisitionSystem/GameplayCue（输入指向解析/表现层，合并声明延后的已知残留）；Array.Resize 在 src/Core 仅剩 Association/EntityKeyedSoaTable（既有基础设施摊销扩容，非本 PR 范围，非热路径逐帧）。

## 续审 2（合并树精读）
- EffectApplicationSystem.cs（954 行，毕）：persistent 挂接已事务化（CollectPendingEffectsJob 按 ResolveOrder 排序→逐条经 _persistentPhaseTransaction staging→失败 RollbackPersistentAttachment+WasContainerCreatedThisPass 回收容器；listener 注册先 ValidateListenerRegistrationCapacity 预检再写；AddFixed 满容 throw；演示事件走 StagePresentationEvent）。裁定：优。
- GraphPathComponents.cs #674：class→struct + InlineArray(128)，EnsureCapacity/Array.Resize 全删。裁定：优。
- ChunkedNodeGraphStore.cs #675：BuildLoadedView 两入口统一 BuildLoadedViewCore，chunkKeys 排序+去重（字典枚举序消除，确定性达成）；cross-edge 目标缺失/TagSetId 越界由静默 continue/default 改 typed throw。裁定：优。
- RuntimeEntitySpawnSystem.cs：关系副作用改 preflight-before-drain（TryCopyTemplateBatch→PreflightTemplateBatchBeforeDrain→TryDrainCopiedTemplateBatch 带队列变更一致性校验 throw；SpawnRelationshipPlan 值类型计划；receipt/on-spawn effect 先 Reserve 再 spawn；_effectRequests==null 静默 return 改 throw；EnsureRuntimeState 接 EntityRuntimeStatePlan）。裁定：优。
- GasGraphRuntimeApi.cs：GasGraphRuntimeProductionServices 强约束注入；派生属性写作用域 Begin/End+属主校验+副作用拒绝 RejectDerivedAttributeSideEffect；接 EffectPhaseSideEffectTransaction（tag/attribute/blackboard/effectRequest/fanout/event/cancel 全 staging，读路径先查 staged）；空间查询 API 从 grid 坐标改 world-cm + SpatialQueryResult（单位一致性修复）；黑板写静默 skip 改 RequireBlackboard throw。裁定：优。
- EffectPhaseExecutor.cs：graph scratch 容量配置化(默认16384)+越界 throw（Array.Resize 删除）；listener dispatch scratch 按容量定尺寸、dropped>0 throw；config context 改 try/finally 防泄漏；rootId 全链路透传。裁定：优。
- EffectProcessingLoopSystem.cs：三相共享 work-unit 预算 ConsumeWork 不变量校验；HasPendingEffects 查询改队列计数；每相 ProcessedLastSlice 遥测；lifetime 可切片+ResetSlice 联动。裁定：优。
- EffectTagContributionHelper.cs：Grant/Revoke/Update 全快照恢复事务；??= new TagOps() 删净改 RequireTagState typed throw；Update 改 per-contribution delta。裁定：优。

## 续审 3（mods 14 关键文件，全毕）
- LocalOrderSourceHelper/InputInteractionContextAccessor/MobaLocalOrderSourceSystem：scratch 容量统一从 GameConfig.gasRuntimeCapacity.commandIntentScratchCapacity 注入（缺失/非正即 throw）；Array.Resize 全删改 typed throw；自建 GasGraphRuntimeApi 改取引擎生产实例（缺失即 throw）；接 InputOrderActorAuthorization 鉴权；TryCopyCollectionEntities 容量语义 RejectedAdmissionCapacity。裁定：优。
- CollectionGasEntityCommandPanelSource：ActivateSlot 从"仅首成员"改"全部存活成员逐个激活"，成员级 typed result 聚合、aiming 成员拒止、MemberScratchCapacity 满容 throw（代码侧 fail-fast 已具）。裁定：优。
- GasEntityCommandPanelSource：bool→InputOrderActivationResult 全类型化、playerId 校验、WouldEnterUiAiming 探测、ItemGrantedSlotBuffer 并入 revision 哈希与 AbilitySlotResolver 统一解析。裁定：优。
- EntityCommandPanelController/Runtime：点击激活结果落 _runtime.RecordActivationResult→页脚展示 Rejected/EnteredAiming/Submitted(OrderId)。裁定：优。
- RoadMoveOrderExpander：bool→OrderSubmitResult；失败路径 OrderSpatialPayloadOps.Release（单条与整批均释放）；状态文案按结果类型分发。裁定：优。
- VisualBenchmarkRuntime：知识投影受众（audience viewer）所有权跟踪+ClearOwnedBenchmarkAudience 完整拆卸（服务移除+实体销毁）。裁定：优。
- RtsRelationRuntimeSystem（857 行重写）：static readonly QueryDescription 落地；Query 回调内结构变更全改 CommandBuffer+FlushStructuralCommands；收集-排序-处理确定性管线（CompareStable WorldId/Id/Version 堆排序）；父子关系全部前置校验 typed throw；TagOps 服务缺失即 throw（?? new TagOps() 删除）。裁定：优。

## 跑测结论（合并树 9a6af246cc vs 基线 5712a4eef4，本机 Release）
- dotnet 修复：Kimi 环境缺 `ProgramFiles(x86)` 环境变量导致 NuGet 机器级配置解析崩溃（path1 null）；补 env 后正常。
- Core 构建：基线 1547 警告 vs 合并 1558 警告，净增 +11（均为长构造函数链 nullable CS8625/CS8618 类，无新警告类别；0 错误）。
- ArchitectureTests：合并树 188/188 绿（与 ci-audit 证据一致）。
- GasTests 全量切片并集全覆盖（~2020 用例）：
  - 合并树红 17 个：15 个零分配环境性（基线同红，同切片基线共 39 红）+ MaintenancePolicy（Config 组，基线同红）+ SkillMappingOverrideResolver（PR 新增自设门禁，allocated=24 与环境性同型，隔离复跑仍红）+ MobaLocalOrderSource_ResolvesCallerSuppliedTargetCollectionKey（确凿 PR 回归）。
  - MobaLocalOrderSource 归因：基线绿→合并红；最终提交 948640f6aa 在 ctor 新增 gasRuntimeCapacity 硬要求但未更新该测试夹具（InputOrderContractTests.cs:341 GameConfig 未带 GasRuntimeCapacity）。948640f6aa 晚于 ci-audit 证据产出 → 证据过期实锤。
  - 同切片基线 39 红 → 合并 15 红：PR 净修复 24 个既有失败测试。
- 残留项：gas-composition-gate.md 乱码仍在（L1 鈥? + L500+ 大段双重编码）；ci-audit result.md/json 已更新为 final closeout 但证据为 pre-push 外部 worktree 产出；workflow 新增 3 守门过滤器步骤；game.json 新增 16 项容量配置；Collection MemberScratchCapacity fail-fast 代码在、锁定测试仍无。
