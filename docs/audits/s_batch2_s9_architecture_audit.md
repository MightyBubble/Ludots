# S 第二批 + S9 架构审计：#951 / #952 / #950 / #948 / #953

**当时对象：** GAS + Graph VM 修复计划（[#942](https://github.com/MightyBubble/Ludots/pull/942) / [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md)）的第二批四张票，以及叠在 S1 上的 S9  
**现在怎样：** [图能力现在走到哪](../../gitbook/architecture/graph-capability-status.md)  
**当时对照：** [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md) §S8 / §S10 / §S11 / §S15 / §S9；审查正本 [`gas_graph_architecture_review.md`](gas_graph_architecture_review.md)  
**基线：** `main` @ `82ddb3322`（S9 叠在 S1 `cursor/s1-graph-invoke-cycle-2489` @ `02635f258`，不当独立于 S1 的主线票）  
**本审计 tip：** 见当时 PR；零生产代码改动

**事后更正：** 这些票已合。不要把本页当时的结论当成当前进度。分层墙仍未立完。

| 任务 | PR | tip | 实现方声称 | 本轮结论 |
|------|----|-----|------------|----------|
| S8 假防线要有牙齿 | [#951](https://github.com/MightyBubble/Ludots/pull/951) | `a7cfe1cb9` | 只打印的补成真断言；门槛改名不放宽数字；假计数断言删掉 | **可关单** |
| S10 选中不读相机裁剪 | [#952](https://github.com/MightyBubble/Ludots/pull/952) | `066bf1c0d` | 点选/Tab 读模拟位置；守卫禁止模拟相读裁剪与视觉变换 | **可关单** |
| S11 覆盖表说覆盖了就要真跑 | [#950](https://github.com/MightyBubble/Ludots/pull/950) | `e2584a4cb` | 21 条错指针先修；生成器失败关闭；守卫按（类,方法）对 | **可关单** |
| S15 验收页合成一份 | [#948](https://github.com/MightyBubble/Ludots/pull/948) | `00bb7d6fd` | 只留一个 H1；没做的写成没做；查询直绑按主线未覆盖 | **可关单** |
| S9 图执行走同一道门 | [#953](https://github.com/MightyBubble/Ludots/pull/953) | `8dfa59f35` | 七宿主改走 `GraphExecutor`；种类不对/坏跳转/掉出尾部失败；预算从断点续跑 | **可关单**（typed 图号与展厅内部入口是已知缺口，不当新阻断，也不当已收口） |

第一批（[#949](https://github.com/MightyBubble/Ludots/pull/949)）已审 S1 / S2 / S4 / S5。S3 / S6 / S7 仍在 [#941](https://github.com/MightyBubble/Ludots/pull/941)，本报告不给那张票结论，也不把它写成已合。

---

## 1. 概述

### 1.1 合并结论

**Verdict：FIX-FORWARD。** 五张票都可以合，也都可以按各自关单尺子关单。没有新阻断。

五张票都没有用任务书点名禁止的绕法：S8 没有为了变绿放宽真实门槛；S10 没有把剔除改名了事；S11 没有把那 21 条降成 `missing`；S15 没有把等价守卫写成合同原文已覆盖，也没有把 #941 当已合；S9 没有放宽 kind，没有拓宽 Script。声称的主结论对照源码成立：

| 声称 | 是否成立 |
|------|----------|
| 以前只打印的基准现在有会失败的分配/耗时断言；门槛改名没有放宽数字 | **成立**。8.6MB 那条只打印，没有写成零分配门 |
| 点选和 Tab 读 `WorldPositionCm`，不读相机裁剪 / `VisualTransform` | **成立**。镜头外单位测试真造了 `IsVisible = false` 仍可选、可下单 |
| 覆盖表登记的测试展开后真执行该节点；生成器找不到专属测试就退出 | **成立**。抽查原 21 条错指针，没有降成未覆盖 |
| 验收页只剩一个 H1；矛盾覆盖结论消失；没做的写成没做 | **成立**。查询 `graphId` 直绑按主线写未覆盖 |
| 七个生产宿主不直连内部 `Execute` / `ExecuteSlice`；三条验收有测试 | **成立**。typed 图号仍是 `int`；展厅仍走内部入口——已知缺口，本轮证实存在 |

不能把「已知缺口」写成已收口，也不能拿它们挡住 S9 关单。typed 图号不是关单条件。

### 1.2 与审查 / 计划的衔接

| 审查编号 | 计划任务 | 本轮 |
|----------|----------|------|
| B7 / B26–B28 / pr932 M3–M4 守卫强度（测试侧） | S8 | **关闭**（点名的假防线；守卫层 ReadAllText 去重已做，改 IL/Roslyn 仍可另开） |
| A6 / C22 表现层决定玩法选中 | S10 | **关闭**。守卫扫到了选中/输入/GAS 模拟相；不是只写文档 |
| pr932 M3 / M4 覆盖表错误归因 | S11 | **关闭** |
| pr932 M12 验收页两份拼接 | S15 | **关闭**（M13 链接已在 #942 修掉，本票只做 SSOT 合并） |
| A5 / B15–B17 / C13 L2 宿主绕门、掉出尾部、预算重放 | S9 | **关闭**（绑定校验 + 执行帧）。typed id 与展厅内部入口按计划允许留下 |

---

## 2. 结构

```text
1 概述：Verdict / 与审查衔接
2 结构（本页）
3 详情：方法、逐票对照、Major / Minor / 债
4 场景：作者与玩家现在会撞到什么
5 边界：覆盖与不覆盖
6 UAT：关单前必须成立的验收（Cucumber）
附录 A 测试证据
附录 B 给收口 Agent 的最短提示词
```

---

## 3. 详情

### 3.1 审计方法

1. 读 `gitbook/contributing/ai-assisted-development.md` 的任务执行决策规范，以及 #942 计划 §S8 / §S10 / §S11 / §S15 / §S9 全文（含禁止项与 Cucumber）。
2. S8 / S10 / S11 / S15 各自相对 `main@82ddb3322` 独立分支。S9 相对 S1 `02635f258`，不当成已经合进主线的独立票。在独立 worktree 对照源码，不把实现方声称当证据。
3. 本轮复跑了任务书指定过滤器（或读已有日志并抽查源码，再亲手复跑关键子集）。全绿。没有为了变绿而改生产代码。
4. 零生产修复。本报告只下结论。不改合同落地状态。

### 3.2 S8 · #951 — 可关单

纯测试。`git diff --name-only origin/main...HEAD` 只碰到 `src/Tests/**`。没有动 `EffectRequestQueue` 或任何生产计数器。

**一、以前只打印的，现在有会失败的断言。**

| 测试 | 主线 | 现在 |
|------|------|------|
| `GraphPerfTests.Benchmark_GraphExecutor_SmallProgram` | 100 万次图 VM，只打印分配 | `[Category("benchmark")]` + `alloc <= 64`（本轮日志 40 字节） |
| `GasBenchmarkTests` WorldGet / WorldGet WithRules / Enqueue / TagCountContainer | 注释写 Assert & Log，实际只有 Log | `alloc <= 64` |
| `EffectPhaseStressTests.PhaseExecutor_HighVolume_*` | 算了 `allocDelta` 只打印 | `alloc <= 64`；耗时门槛仍是 `50.0` |
| `BlackboardOps_Stress_MassWriteRead_ZeroGc` | 循环内 `new int[MaxCallStackDepth]`；只断言 `< 30s` | 分配移出循环后 `alloc <= 64` |
| `GraphBehaviorPressureMatrixTests` | 3 句 `File.Exists` | 仍检查 CSV 写出，并补 M1/M2/M6 `< 15ms`、M3 `halted == A` 且 `< 1000ms` |
| `AllocationTests.DurationEffectTick_AllocatesZeroAfterWarmup` | 无持续效果 tick 覆盖 | 新：热循环 `alloc <= 64` |

**二、门槛改名没有放宽真实数字。**

主线上 `ThinkWave_10k_AlwaysSuccess16_UnderFiveMilliseconds` 断言已经是 `15.0`，名字却写 Five。本票改名为 `UnderFifteenMilliseconds`，断言仍是 `15.0`，并补了失败消息。`ThinkWave_10k_SentryHfsmWithScripts_*` 同样：主线已是 `15.0`，只改名。`ThinkWave_10k_SentryHfsm_UnderFiveMilliseconds` 仍守 `5.0`。效果阶段注释从「<10μs」改成与断言一致的 50μs，**数字没动**。

**三、删掉的 `DroppedCount == 0` 全文不自增。**

`GraphFailFastAndCapacityTests` 主线三句 `That(q.DroppedCount, Is.EqualTo(0))` 已删。`EffectRequestQueue._dropped` 只在 checkpoint 读写，**没有任何 `++` / `Add` / 赋值自增**。断言恒真，删掉是对的。字段本身还在——那是生产债，S8 任务书禁止动生产。

**四、SystemGroup 守卫按序比。**

`ArchitectureGuardTests.SystemGroup_MustMatchDesignDocument` 从 `Is.EquivalentTo` 改成 `Is.EqualTo(DesignedSystemGroupOrder)`。新测试 `SystemGroup_PhaseOrder_MustIncludeEveryEnumValueInDesignedOrder` 反射读 `PhaseOrderedCooperativeSimulation.PhaseOrder`，与 enum / 设计表交叉校验。设计表补上了 `RuntimeEntityBinding`，与运行时 `PhaseOrder` 一致。把 Cleanup 挪到最前面现在会红。

GasTests 里那份逐字节复制的 `Integration/ArchitectureGuardTests.cs`（1973 行）已删，只留 ArchitectureTests 一份。改成 IL/Roslyn 门禁任务书允许另开，本票没做，不挡关单。

**五、新发现的 8.6MB 没有写成零分配门。**

`Benchmark_DeferredTriggerCollectionSystem_SparseDirtyTags` 量了分配、打印、只断言触发器条数。本轮日志 `AllocatedBytes=9036280`（约 8.6MB），测试仍绿。`TagOps.AddTag` InlineQuery / WithRules 打印 864 / 656 字节，同样没有套 `<= 64`。这是任务书允许的「真实但无人看管的分配，单独开票」，不是把发现写成零分配门混过去。

**禁止项核对：** 没有为了变绿放宽真实门槛；没有删测试而不替换覆盖。

### 3.3 S10 · #952 — 可关单

关单尺子：守卫真挡住可关；只写文档则合入不关。本票两边都做了。

**点选和 Tab 读模拟位置。**

`CommandSourcePointerHitResolver` / `CommandSourceAcquisitionSystem` / `TabTargetCycleSystem` 查询签名是 `WorldPositionCm + CommandSourceSelectableTag`。全文不再出现 `CullState` / `VisualTransform` / `IsVisible`。`SpatialBoundsUtility.TryGetSimulationPose` 用 `WorldPositionCm`，朝向用可选的 `FacingDirection`，不再读视觉变换。

**镜头外指挥测试真造了看不见但仍可选。**

`CommandSourceAcquisitionSystem_CameraCulledEntity_RemainsSelectableAndReceivesOrders`：实体挂 `CullState { IsVisible = false, LOD = Culled }`，点选进编队，框选带上第二个镜头外单位，Stop 订单两个 actor 都收到。`CommandSourcePointerHitResolver_UsesWorldPositionCm_NotVisualTransformOrCull` 把 `VisualTransform` 放到 `(80,0,80)`、裁剪关掉，指针打在模拟厘米坐标 `(1600,1200)`，命中的仍是这个实体。本轮这两条绿。

**三个跨层组件 owner 写进分层文档。**

`gitbook/architecture/entity-simulation-layering.md` §5.1 表：

| 组件 | write owner |
|------|-------------|
| `PresentationStableId` | 模拟侧分配器与 spawn/lifecycle |
| `PresentationDestroyPending` | 模拟侧 lifecycle |
| `CullState` | `CameraCullingSystem` 写 `IsVisible` / 视觉 LOD |

`gas-order-input-runtime-contract.md` 补了一行：选中与收令只读模拟真相，不读相机 `CullState`。守卫 `CrossLayerPresentationComponents_HaveDocumentedSingleWriteOwners` 读这份表。

**守卫禁止模拟相读裁剪和视觉变换。**

`SimulationPhaseSelectionAndInputSystems_MustNotReadVisualTransformOrCullState`：IL 扫点选/Tab/空间投影/输入系统的方法参数与调用泛型；再对源文件做 `VisualTransform` / `CullState` token 扫描。`SimulationPhaseGasSystems_MustNotReadVisualTransformOrCullState` 扫 `Ludots.Core.Gameplay.GAS` 下所有 `ISystem<float>`。`CullStateIsVisible_RuntimeWriteOwnerIsCameraCullingSystem` 禁止 Core / CoreInputMod 里非 `CameraCullingSystem` 写 `cull.IsVisible =`。本轮 ArchitectureTests 指定过滤器 83 绿。

这不是「每一个挂在 InputCollection 上的未来系统自动入闸」——名单是点名的选中/输入类型 + GAS 命名空间。对任务书点名的那几处，今天加回 `if (!cull.IsVisible) return` 会红。够关单。

**不要审成知识披露变了。**

`SelectionKnowledgeProjectionTests` 只把实体位置从 `VisualTransform` 换成 `WorldPositionCm`。`Issue197_*` 仍走 `KnowledgeProjectionStore` live inspect：未知/最后已知被 Tab 滤掉，敌对关系仍拒绝。本轮这些测试绿。知识档位语义没动。

`CameraAcceptanceModTests` 把框选高度从视觉变换改成 `SpatialBounds.LocalCenterYCm`，这是选中投影跟模拟位对齐，不是战争迷雾。

**禁止项核对：** 没有把剔除改名了事；没有保留视觉 gate。

### 3.4 S11 · #950 — 可关单

**原 21 条错指针：先修指针，没有降成未覆盖。**

覆盖表仍是 120 条，`status` 全是 `covered`，零条 `missing`。主线上指向家族盲配方法的条目，本票改到展开后真跑该 op 的（类,方法）对。

任务书点名的 21 条（事件族 14 条错指 + `ConstFloat` / `AddFloat` + 黑板 5 条专属方法没被引用）：

| 原错指 | 主线指向 | 现在专属画廊方法 | 打开后是否真跑该 op |
|--------|----------|------------------|---------------------|
| `SendEvent` | `SnapToNearestInCollection_SucceedsWithPlayerCaption` | `SendEvent_BroadcastsPlayerReadableHit` | `BindAndTick("SendEvent")` |
| `ClampTargetToRange` | 同上 | `ClampTargetToRange_PullsLandingPointInRange` | `BindAndTick("ClampTargetToRange")` |
| `LoadViewer` | 同上 | `LoadViewer_ReadsTheAudience` | `BindAndTick("LoadViewer")` |
| `KnowledgeHasProjection` | 同上 | `KnowledgeHasProjection_ShowsVisible` | `BindAndTick("KnowledgeHasProjection")` |
| `SnapToNearestGraphEdge` | 同上 | `SnapToNearestGraphEdge_SnapsOntoTheRoad` | `BindAndTick("SnapToNearestGraphEdge")` |
| `FanOutDispatchEffect` 等 9 条 | 同上 | `EventFamilyOp_RendersPlayerCaption` | `[TestCaseSource]` 数组里就是这 9 个 op |
| `ConstFloat` | `FloatFamilyOp_RendersPlayerCaption`（数组里没有它） | `ConstFloat_SetsTargetHealthToAuthoredConstant` | `BindOp("ConstFloat")` |
| `AddFloat` | 同上 | `AddFloat_SubtractsSumFromTargetHealth` | `BindOp("AddFloat")` |
| `WriteBlackboardFloat` / `ReadBlackboardFloat` | `BlackboardOp_CaptionContainsAssertPhrase`（TestCase 里没有它们） | `WriteThenReadFloat_VisibleOnHealthAndCaption` | `TickOp("WriteBlackboardFloat")` / `TickOp("ReadBlackboardFloat")` |
| `LoadConfigFloat` | 同上 | `LoadConfigFloat_NotZero_CaptionContainsConfigPower` | `TickOp("LoadConfigFloat")` |
| `BeginLifecycleTransaction` | 同上 | `BeginLifecycleTransaction_CaptionContainsTransaction` | `TickOp("BeginLifecycleTransaction")` |
| `InvokeBuiltin` | 同上 | `InvokeBuiltin_ClearsMark_CaptionIsPlayerChinese` | `TickOp("InvokeBuiltin")` |

`FloatFamilyOp_RendersPlayerCaption` 的数组现在是 `ConstBool` / `MulFloat` / …，**没有** `ConstFloat` / `AddFloat`。守卫测试 `Attribution_RejectsTheKnownFamilyMispointers` 锁住：旧指针对 `ConstFloat` / `SendEvent` 必须判假，新指针必须判真。`ExistingVignettes_CompileWithFeaturedOp` 与 `GeneratedMaps_SpawnEveryVignetteActor` 进了每条 `unitTestRefs`。

**生成器失败关闭。**

`coverage_refs_for_op`：没有 op 专属 `GraphOpsNodeGallery` 测试就 `SystemExit`。缺 vignette driver、`status != covered`、文件名与 op 不符，同样退出。本轮 `python3 scripts/generate-graph-op-node-galleries.py --strict` 后 `git diff --exit-code` 零漂移。

**守卫按（类,方法）对。**

`GraphNodeOpCoverageRegistryTests` 不再按裸方法名建集合。`unitTestRefs` 必须是 `Class.Method`；`attribution.HasMethod(class, method)` 与 `attribution.Executes(class, method, op)` 同时成立。字段名从名不副实的 `unitTestFilter` 改成 `unitTestRefs`。CI：`.github/workflows/solution-verify.yml` checkout 后复跑生成器并 `git diff --exit-code`。

孤儿检测：`GeneratedPerOpArtifacts_HaveNoOrphans` 扫退役 op 留下的 map / 薄入口。生成器仍 upsert 覆盖表指针，但缺专属测试会失败关闭，不再靠家族盲配自证。

**禁止项核对：** 没有把 21 条改成 `status=missing`。

### 3.5 S15 · #948 — 可关单

只改 `gitbook/acceptance/graph-funclib-actionlib-uat.md` 与 `gitbook/SUMMARY.md`。不改合同落地状态，不改生产代码。

**验收页只有一个 H1。** 主线该文件有两个 H1：`# FuncLib / ActionLib UAT（Epic #915）` 与 `# Graph FuncLib / ActionLib UAT 映射`。现在只剩第一句。`SUMMARY.md` 标题从指向被删下半标题的「Graph FuncLib / ActionLib UAT 映射」改成「FuncLib / ActionLib UAT」。

**矛盾覆盖结论消失。** 主线上半表把「技能阶段不能调用 ActionLib」写成 `GraphEffectAuthoringExpressivenessTests`（Effect 前门拒 Yield / InvokeAction），下半表同一条标 `Gap` / 「无」。现在只留一张表：该条状态是 **合同原文未覆盖**，并列出现有的 `InvokeScript.functionName` 等价守卫，写明不得升格。

**没做的写成没做。** DoT 多跳灼烧、`damage.falloff` OnApply、Score 经真实 FuncLib 打两个候选，都标部分覆盖并写缺口。`bt.patrol` 跨拍续跑标已覆盖；下半那句「缺一条 `bt.patrolStep` 真 Yield」已删。名称以资产 `bt.patrol` 为准。

**查询直绑图号按主线写未覆盖，没有把 #941 当已合。** 原文：「查询方言仍可用 `InvokeScript.graphId` 直绑……本树未合入查询口收口。」没有出现 #941，也没有写成已覆盖。

本轮 `validate-docs.ps1` 零 finding。相对链接保持 `../architecture/graph-funclib-actionlib-contract.md`。

**禁止项核对：** 没有删场景让映射表好看；没有把等价守卫写成合同原文已覆盖。

### 3.6 S9 · #953 — 可关单

叠在 S1（#944）上。S1 的环检测仍在 `Register` / `ReplaceProgram` 的 `EnsureNoInvokeCycle`。本票在同一处加了 `EnsureProgramValid`（kind 能力、跳转目标、显式 `HaltReturnInt`、寄存器边界）。

**七个生产宿主不直连内部 Execute / ExecuteSlice。**

`GasGraphOpHandlerTable.Execute` / `ExecuteSlice` 现为 `internal`。Core 生产代码里，这两处只被 `GraphExecutor` 调用。七个点名宿主：

| 宿主 | 现在走的门 |
|------|------------|
| `BehaviorTreeWorld` | `GraphExecutor.ExecuteRegisteredSlice`；绑定时 `RequireHostKind(..., Script, "行为树叶子")` |
| `GraphProgramHfsmHost` | 同上；标签「状态机条件」/「状态机生命周期」 |
| `LevelScriptPrograms` / `GraphProgramLevelHost` | 同上；标签「关卡脚本」 |
| `EffectPhaseExecutor` | `GraphExecutor.Execute(ref frame, ...)` |
| `PerformerRuleSystem` | 同上 |
| `GraphReturnWriter` | 同上 |
| `AbilityAimPresentationRuntime` | 同上 |

`RequireHostKind` 失败文案带 `GAS.GRAPH.ERR.KindMismatch`、实际种类、宿主标签、只接受的种类。

**三条验收。**

- 效果图绑行为树叶子：`效果图绑到行为树叶子必须失败并说明种类不对` 登记 `GraphKind.Effect`，Tick 抛错，消息含 KindMismatch、`Effect`、`行为树叶子`、`Script`。
- 坏跳转登记失败：`跳到程序外的跳转登记必须失败并指出这个跳转`，`Jump Imm=8` 被拒，图号不留在表里。
- 预算耗尽从断点续跑不重放：`预算耗尽后从断点续跑且已产生的副作用不重放`。第一拍 `budgetSteps=1` 状态 Running，传感器写入 1 次；第二拍续跑 Success，返回 7，传感器仍是 1 次。`BehaviorTreeWorld` 续跑条件是 `cursor.IsSuspended && _scriptResumeGraphIds[agent] == node.GraphId`，不再只认 `Yielded`。`GraphExecutionStatus` 区分 `NotStarted` / `Yielded` / `BudgetSuspended` / `Halted`。

**掉出尾部不再算成功。** `ExecuteSliceCore` 在 `(uint)pc >= (uint)program.Length` 时抛 `GAS.GRAPH.ERR.PcOutOfRange`，文案写明必须用 `HaltReturnInt`。登记期缺终结指令抛 `MissingHalt`。线性/查询编译器在没有下一步的末端发出已有停机指令。测试 `掉出程序尾部不再算成功` 绿。

**不放宽 kind，不拓宽 Script。** `ExecuteSlice` 仍只接受 `GraphKind.Script`。`GraphKindOperationPolicy` 的能力表没有为 L2 放行 Effect 节点。本票 diff 不往 Script 方言加新作者节点。

**已知缺口：证实存在，不当新阻断，也不当已收口。**

- 图号仍是裸 `int`（`BehaviorTreeNode.GraphId`、HFSM / 关卡宿主参数）。种类靠绑定时 `RequireHostKind`，没有 typed id。
- 展厅多条路径仍走 `GasGraphOpHandlerTable.Execute` / `ExecuteSlice`（`InternalsVisibleTo` 给了 NodeGallery / 八家族 / LSW / ScriptFlowSandbox）。`GraphExecutor` 仍留着不带 kind 的 `internal Execute` 重载给测试与旧调用。

这两处任务书写过「如果 typed id 太大，至少绑定时失败关闭」——绑定校验已做。展厅内部入口是已知债。

### 3.7 Major / Minor / 债

#### Major

无。没有挡住合入或关单的项。

#### Minor

| # | 项 | 票 |
|---|----|----|
| m1 | ArchitectureGuardTests 仍有 ReadAllText + Contains；任务书允许另开 IL/Roslyn 票 | S8 |
| m2 | `EffectRequestQueue.DroppedCount` 字段还在，只是不再有恒真断言 | S8（生产债，本票不许动） |
| m3 | 选中/输入守卫是点名类型名单，不是「凡挂在 InputCollection 的系统」自动入闸 | S10 |
| m4 | 覆盖归因按源码展开 `BindOp` / `[TestCase]` / 数组字面量，不是运行期 opcode 轨迹 | S11（任务书要的就是展开） |
| m5 | 验收页「含 Yield 不能进 FuncLib」标已覆盖，仍缺作者错误文案截图 | S15（页上已写不影响合同场景） |

#### 债

| # | 项 |
|---|----|
| d1 | `DeferredTriggerCollectionSystem.SparseDirtyTags` 约 8.6MB / `TagOps.AddTag` InlineQuery 864 与 656 字节：真实分配，未套零分配门。单独开票 |
| d2 | `GameplayEventBus.DroppedEventsLastUpdate` 仍恒 0（第一批 d1）。S8 只删了点名的 `EffectRequestQueue` 假断言 |
| d3 | typed 图号未做。S9 已知缺口 |
| d4 | 展厅 / 画廊仍走内部执行入口。S9 已知缺口 |
| d5 | S3 / S6 / S7 仍在 #941，本轮未审，也不当已合 |
| d6 | S12 / S13 / S14 不在本轮范围 |

### 3.8 合同逐条（Cucumber 原文）

| 场景 | 结果 |
|------|------|
| S8 名字里写零分配的测试必须断言分配量 | **过**（点名的那些；8.6MB 那条按发现留下，不断言零） |
| S8 顺序守卫必须真的守顺序 | **过** `EqualTo` + PhaseOrder 交叉校验 |
| S10 镜头外的单位仍然可以被指挥 | **过** 真造 `IsVisible = false` |
| S10 模拟系统读视觉变换或剔除状态，守卫必须失败 | **过** IL + token；本轮守卫测试绿 |
| S11 登记的测试必须真的跑那个节点 | **过** 抽查 21 条 + 守卫展开 |
| S11 人手改生成器产物，CI 复跑必须失败 | **过** workflow 复跑 + `git diff --exit-code`；本轮零漂移 |
| S15 一份 H1、矛盾消失、没做的写成没做 | **过** |
| S9 效果图绑到行为树叶子必须失败并说明种类 | **过** |
| S9 坏跳转登记必须失败 | **过** |
| S9 预算耗尽后从断点继续且不重放 | **过** |
| S9 掉出尾部不再算成功 | **过**（登记缺 Halt + 运行期 pc 越界） |

---

## 4. 场景

**作者跑「零分配」基准。** 以前：打印一行数字，CI 永远绿。现在：超过 64 字节会红。持续效果 tick 也有门。发现 8.6MB 的脏标签收集不会假装是零分配。

**把 Cleanup 挪到阶段表最前面。** 以前：集合相等，守卫照样绿，系统每帧不跑。现在：顺序不对就红；enum 有、PhaseOrder 漏了也红。

**镜头外还有一个自己的人。** 以前：相机裁掉就不能点、不能进编队。现在：点选和 Tab 看模拟位置；裁掉的人仍能收 Stop。看不见的敌人会不会出现在 Tab 里，仍由知识投影决定，不是这张票改的。

**覆盖表写着 SendEvent 已覆盖。** 以前：指针指向「吸到花名册」那条单 op 测试，守卫只看方法名在不在。现在：展开后必须真跑 `SendEvent`；指错就红；生成器写不出专属测试就退出。

**作者打开 FuncLib / ActionLib 验收页。** 以前：两个标题，同一条场景上半已覆盖、下半 Gap。现在：一份表。`InvokeAction` 合同原文标未覆盖；查询直绑图号按主线未覆盖。

**作者把效果图挂到行为树叶子。** 以前：HFSM 连 kind 都不查，能跑到 NRE。现在：登记或执行时说明这个种类不能挂在行为树叶子上。坏跳转进不了表。动作叶子预算用尽后下一拍从断点接着跑，传感器副作用不重放。没有 `HaltReturnInt` 的图掉出尾部会抛，不再报成功。

---

## 5. 边界

**本审计包含：** #951 / #952 / #950 / #948 相对 `main@82ddb3322` 的实现与测试，以及 #953 相对 S1 的实现与测试，对照 #942 计划对应章节。

**不包含：**

- #941 上的 S3 / S6 / S7
- S12 / S13 / S14
- 五张票 rebase 到同一棵树上之后的集成（它们目前是平行分支；S9 叠在 S1）
- 把 8.6MB 脏标签分配修掉（那是新发现，不是本审计）
- 给图号做 typed id，或把展厅改走正式前门（S9 已知缺口，留给后续）
- 把第一批 M1 / M2（展厅裸写、S4 提交对齐 S2）收口
- 真机 / 展厅游玩（本轮是架构对照 + 指定过滤器测试）
- 合同落地状态改写

**不要因为本报告去改别人正在开的 S12 / S13 / S14。** 债务只报告。

---

## 6. UAT

```gherkin
Feature: 第二批和 S9 修完之后，防线、选中、覆盖表、验收页和图前门不再说谎
  作为维护者
  我希望这五张票的主故障是真关了
  并且已知缺口不会被写成「已收口」

  Scenario: 名字里写零分配的测试必须咬人
    Given 一批名字包含 ZeroAlloc / Benchmark / ZeroGc 的测试
    When CI 跑它们
    Then 每一条都必须对分配量下断言
    And 断言数值必须与方法名和注释一致
    And 新发现的 8.6MB 不得被写成零分配门

  Scenario: 打乱系统阶段顺序必须让 CI 红
    Given 我把 SystemGroup 或 PhaseOrder 的顺序打乱
    When CI 跑架构守卫
    Then 守卫必须失败

  Scenario: 镜头看不见的单位仍然可以被指挥
    Given 我的一个单位当前不在镜头范围内
    And 它仍然带有玩法可选中标记和模拟位置
    When 我通过点选或框选对它下指令
    Then 指令必须生效
    And 这件事不得被审成知识披露变了

  Scenario: 模拟相读视觉变换或剔除状态必须让 CI 红
    Given 一个跑在选中或 GAS 模拟相里的系统
    When 它读了 VisualTransform 或 CullState
    Then 架构守卫必须失败并点名这个调用方

  Scenario: 覆盖表说覆盖了就要真跑那个节点
    Given 覆盖表给 SendEvent 登记了一条测试
    When 守卫展开这条登记
    Then 那条测试必须真的执行 SendEvent
    And 不得靠把节点标成 missing 过关

  Scenario: 验收页只有一份结论
    Given 作者打开 FuncLib / ActionLib UAT
    Then 页面只有一个一级标题
    And 「技能阶段不能调用 ActionLib」按合同原文标未覆盖
    And 查询方言 graphId 直绑按主线标未覆盖

  Scenario: 效果图不能挂在行为树叶子上
    Given 我把一张效果图绑到行为树的叶子上
    When 代理执行到这个叶子
    Then 必须失败并说明这个种类的图不能挂在这里

  Scenario: 坏跳转不能被报成正常完成
    Given 一张图里有一个跳到程序外面的跳转
    When 这张图被登记
    Then 登记必须失败并指出这个跳转

  Scenario: 预算耗尽后从断点继续
    Given 一个行为树叶子里的动作因为当拍预算耗尽而挂起
    When 下一拍继续
    Then 它必须从断点继续，而不是从头重跑
    And 已经产生的效果不得重复发生
```

关单门槛：

- S8 / S11 / S15：本轮认定可以关。
- S10：守卫已挡住点名的选中/输入/GAS 模拟相，可以关。不是「只写了文档」。
- S9：七宿主 + 三条验收成立，可以关。typed 图号不是关单条件。展厅内部入口证实存在，不要写成已收口。

---

## 附录 A — 测试证据

本轮在各票独立 worktree 复跑指定过滤器（已有日志抽查 + 亲手复跑）。全部 Passed：

| 票 | 过滤器 / 命令 | 结果 |
|----|----------------|------|
| #951 S8 | ArchitectureTests `ArchitectureGuardTests` | 45 / 45 |
| #951 S8 | GasTests 计划过滤器（Allocation / GraphPerf / GasBenchmark / EffectPhase / EntityCollection / AiBenchmark / BT / FSM / Arena / PressureMatrix） | 35 / 35 |
| #951 S8 | 实现方子集日志（另含 GraphFailFast 等） | 48 / 48；SparseDirtyTags 打印 9036280 字节且不断言零 |
| #952 S10 | ArchitectureTests `ArchitectureGuardTests\|Rfc0065\|PerformContracts` | 83 / 83 |
| #952 S10 | GasTests `OrderBufferSystem\|CommandSource` | 47 / 47 |
| #952 S10 | 镜头外点选 + 知识投影抽查 | 7 / 7，含 `CameraCulledEntity_RemainsSelectableAndReceivesOrders` |
| #950 S11 | `GraphNodeOpCoverageRegistryTests` | 5 / 5 |
| #950 S11 | `python3 scripts/generate-graph-op-node-galleries.py --strict` + `git diff --exit-code` | 零漂移 |
| #950 S11 | 实现方 `GraphNodeOpCoverageRegistryTests\|GraphOpsNodeGallery` 日志 | 105 / 105 |
| #948 S15 | `pwsh ./scripts/validate-docs.ps1` | 零 finding |
| #953 S9 | `GraphFrameFrontDoorTests\|GraphInvokeCycleTests\|GraphExecuteSliceBudgetTests\|GraphContractTests` | 30 / 30 |
| #953 S9 | 实现方计划过滤器日志 | 101 / 101 |

---

## 附录 B — 给收口 Agent 的最短提示词

这五张票没有挡住关单的收口。不要顺手改 S12 / S13 / S14 / #941。

若另开债票，只做下面两件，且不要回写本审计的关单结论。

### B.1 真实分配单独建票（不是 S8 重开）

```text
对照 docs/audits/s_batch2_s9_architecture_audit.md d1。
S8 已把假防线补成真断言，不要改 64 字节门槛，不要动已绿的零分配测试。

要做：给 DeferredTriggerCollectionSystem.SparseDirtyTags（约 8.6MB）
和 TagOps.AddTag InlineQuery（864 / 656）单独开生产修复票。
修好之后再把对应基准补成会失败的断言。

禁止：在 S8 上补零分配门把 8.6MB 测红；为了变绿放宽 64。
```

### B.2 S9 已知缺口不要当本票重开

```text
对照 docs/audits/s_batch2_s9_architecture_audit.md d3 / d4。
S9 七宿主和三条验收已经关单。不要重做 GraphFrame。

typed 图号、展厅 InternalsVisibleTo 内部 Execute，按计划是已知缺口。
要做就另开票：给 GraphId 带 kind，或把展厅改走 GraphExecutor 正式门。
禁止：把这两处写成 S9 没做完；禁止为了让展厅绿而放宽 kind。
```
