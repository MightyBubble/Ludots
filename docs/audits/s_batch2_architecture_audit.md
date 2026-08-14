# S 第二批 + S9 架构审计：#951 / #952 / #950 / #948 / #953

**审计对象：** GAS + Graph VM 修复计划（[#942](https://github.com/MightyBubble/Ludots/pull/942) / [`gas_graph_architecture_fix_plan.md`](gas_graph_architecture_fix_plan.md)）第二批四张票，外加叠在 #944 上的 S9  
**合同 SSOT：** 计划书 §S8 / §S9 / §S10 / §S11 / §S15  
**前序审计：** [`s_batch1_architecture_audit.md`](s_batch1_architecture_audit.md)（#949）  
**基线：** `main` @ `82ddb3322`；S9 另以 #944 tip `cd84fdf00` 为叠层基线  
**本审计：** 零生产代码改动。对照源码，不对照 PR 自称。

| 任务 | PR | tip | 实现方声称 | 本轮结论 |
|------|----|-----|------------|----------|
| S8 假防线 | [#951](https://github.com/MightyBubble/Ludots/pull/951) | `a7cfe1cb9` | 假防线补成真断言；阶段顺序按设计表比顺序 | **可关** |
| S10 镜头外仍能指挥 | [#952](https://github.com/MightyBubble/Ludots/pull/952) | `066bf1c0d` | 镜头外的单位仍能被点到、能收指令 | **可关** |
| S11 覆盖表真跑节点 | [#950](https://github.com/MightyBubble/Ludots/pull/950) | `e2584a4cb` | 覆盖表说覆盖了，就必须真跑那个节点 | **可关** |
| S15 验收页合成一份 | [#948](https://github.com/MightyBubble/Ludots/pull/948) | `00bb7d6fd` | 验收页合成一份，覆盖结论按实测写 | **可关** |
| S9 同一道门 | [#953](https://github.com/MightyBubble/Ludots/pull/953) | `8dfa59f35` | 所有图执行走同一道门；坏跳转不能报成正常完成；预算挂起从断点接着跑 | **可关** |

没有新的阻断项。没有把合同改成「已落地」。没有审 S3 / S6 / S7，也没有把 #941 当成已合。

S9 的已知缺口（图号仍是普通整数、展厅仍走内部入口）本轮证实存在，不当新阻断，也不当已收口。

---

## 1. 概述

### 1.1 合并结论

**Verdict：五张都可以关单。** 玩家能感觉到的主故障，以及这五张票自己声明要守住的测试/文档，对照源码都成立。收口债记在下面，不够把关单卡住。

| 声称 | 是否成立 |
|------|----------|
| 以前只打印的测试，现在有会失败的断言；门槛改名没有放宽真实数字 | **成立。** 分配量为零的补了 ≤64 断言。约 8.6MB 那条没有写成零分配门。生产一行没动 |
| 阶段顺序按设计表比顺序，不是集合相等 | **成立。** `Is.EqualTo`，并交叉校验运行时阶段表 |
| 点选和 Tab 读模拟位置，不是相机裁剪 | **成立。** 测试真的造了「看不见但仍可选」，点选和框选都能下到停止令 |
| 覆盖表登记的测试展开后真执行该节点 | **成立。** 原先指错的指针已改；120/120 仍是 covered，没有降成未覆盖来躲 |
| 验收页只有一个一级标题；没做的写成没做；查询直绑图号按主线写未覆盖 | **成立。** `validate-docs.ps1` 零 finding |
| 七个生产宿主走同一道门；效果图绑行为树叶子失败；坏跳转登记失败；预算耗尽下一拍从断点续跑 | **成立。** 图号仍是整数、展厅仍有内部入口，按已知缺口记下 |

### 1.2 与 #949 / 计划的衔接

| 计划 | #949 | 本轮 |
|------|------|------|
| S1 可关，S9 等它合进主线 | S1 可关 | S9 按叠在 #944 上审，三条验收成立，**可关** |
| S8 假防线可独立开 | 未审 | **可关**；约 8.6MB 分配是合同允许的「新发现，单独开票」 |
| S5 已删假计数 | 可关 | S8 侧确认 `DroppedCount == 0` 断言已删，全文无自增 |
| 不要把 #941 当已合 | — | S15 查询口写「本树未合入查询口收口」 |

---

## 2. 结构

```text
1 概述：Verdict / 与 #949 衔接
2 结构（本页）
3 详情：方法、逐票对照、债
4 场景
5 边界
6 UAT
附录 A 测试证据
附录 B 给后续票的最短提示（不是本轮修复）
```

---

## 3. 详情

### 3.1 审计方法

1. 读任务执行决策规范，以及 #942 计划 §S8 / §S9 / §S10 / §S11 / §S15 全文。
2. 五张票各自独立 worktree。S9 只审 `f89ba8181` 及之后，不重审 S1。
3. 打开改过的测试、覆盖表指针、验收页一级标题、七个宿主调用点。自称不当证据。
4. 跑需求里点名的过滤器（附录 A）。S9 指定过滤器第一次 100/101，失败项是思考波 5ms；同机对 #944 复跑与 #953 单测复跑都绿且数字接近，记为冷启动抖动，不当回归。
5. 零生产修复。

### 3.2 S8 · #951 — 可关

**生产：** `git diff src/Core` 为空。纯测试。

**以前只打印、现在会失败的断言（打开过）：**

| 测试 | 现在的断言 | 能否红 |
|------|------------|--------|
| `GraphPerfTests.Benchmark_GraphExecutor_SmallProgram` | 分配 ≤64，并补了 `[Category("benchmark")]` | 能 |
| `GasBenchmarkTests` 里 TagOps WorldGet / WithRules / Enqueue / TagCount | 分配 ≤64 | 能 |
| `EffectPhaseStressTests.PhaseExecutor_HighVolume` | 分配 ≤64；耗时仍是 50μs，注释改成与断言一致 | 能 |
| `BlackboardOps_Stress_MassWriteRead_ZeroGc` | 分配 ≤64；`CallStack` 移出测量循环 | 能 |
| `AllocationTests.DurationEffectTick_AllocatesZeroAfterWarmup` | 持续效果 tick 五系统，分配 ≤64 | 能 |
| `ThinkWave_*` | 改名为 `UnderFifteenMilliseconds`，断言仍是 15ms | 能；**没有放宽数字** |

**约 8.6MB 那条：** `Benchmark_DeferredTriggerCollectionSystem_SparseDirtyTags` 仍只打印分配、只断言触发器个数。没有写成 ≤64。合同原文：非零分配是新发现，单独开票，禁止用零分配门蒙混。本轮按这个执行，**不当 S8 关单缺口**。两条 InlineQuery 基准同样是非零（约 0.6–1KB），只打印。压力矩阵补了耗时/停机断言，不再只查 CSV 存在。

**恒真断言：** `GraphFailFastAndCapacityTests` 里 `DroppedCount == 0` 已删。`EffectRequestQueue._dropped` 全文无自增。

**顺序守卫：** `SystemGroup_MustMatchDesignDocument` 改为 `Is.EqualTo(DesignedSystemGroupOrder)`。新测试交叉校验 `PhaseOrder` 与 enum，漏组会红。`RuntimeEntityBinding` 进了设计表。两份同名 `ArchitectureGuardTests` 删掉 GasTests 那份复制。

### 3.3 S10 · #952 — 可关

点选、框选、Tab 查询签名现在是 `WorldPositionCm + CommandSourceSelectableTag`。`src/Core/Input/CommandSources/` 与 `mods/CoreInputMod/` 里零处再读 `CullState` / `VisualTransform`。

测试 `CommandSourceAcquisitionSystem_CameraCulledEntity_RemainsSelectableAndReceivesOrders` 造了 `CullState.IsVisible = false` 的单位：点击选中它，框选把它和镜头外另一个一起选中，停止令发到两边。另有一条断言命中用模拟坐标、忽略视觉坐标和裁剪。

守卫扫点选 / 框选 / Tab 以及 GAS 命名空间下的模拟系统，禁止读视觉变换和裁剪。`CullState.IsVisible` 运行时写入只许相机裁剪系统。分层文档 §5.1 写了三个跨层组件的 write owner。

关单尺子：守卫挡住了当初那条「模拟相读裁剪」的生产路径。不是只写了文档。

**不挡关单的缺口：** 守卫是点名类型清单 + GAS 命名空间，不是「所有模拟相系统」。Tab 没有单独的「裁掉仍能切目标」测试。知识披露测试方法名还带着 CameraVisible。展厅模拟仍有写 `IsVisible` 的，守卫扫不到。这些不是「生产仍在读裁剪」。

本票没有改知识披露语义。选中若还要看「看不看得见」，走的是知识投影，不是相机。

### 3.4 S11 · #950 — 可关

字段从 `unitTestFilter` 改成 `unitTestRefs`（类.方法 列表）。守卫按（类, 方法）对校验，并展开 `TestCase` / `TestCaseSource` / 方法里的 `BindOp` 字面量。生成器：该节点不在目标测试的节点列表里就不写；没有专属画廊测试就失败关闭。`hasGalleryTest` 要求至少一条非全量枚举的画廊测试。`ExistingVignettes_CompileWithFeaturedOp` 与 `GeneratedMaps_SpawnEveryVignetteActor` 进入全部 120 条。CI 在 `solution-verify.yml` 里跑 `--strict` 再 `git diff --exit-code`。生成器会删孤儿门。

**抽查原先错指针（打开过登记的测试方法）：**

| 节点 | 现在登记的方法 | 展开后是否执行该节点 |
|------|----------------|----------------------|
| ConstFloat | `ConstFloat_SetsTargetHealthToAuthoredConstant` | 是，`BindOp("ConstFloat")` |
| AddFloat | `AddFloat_SubtractsSumFromTargetHealth` | 是 |
| SendEvent | `SendEvent_BroadcastsPlayerReadableHit` | 是 |
| ClampTargetToRange | `ClampTargetToRange_PullsLandingPointInRange` | 是 |
| KnowledgeHasProjection | `KnowledgeHasProjection_ShowsVisible` | 是 |
| LoadViewer | `LoadViewer_ReadsTheAudience` | 是 |
| FanOutDispatchEffect | `EventFamilyOp_RendersPlayerCaption` | 是，`TestCaseSource` 数组含此名 |
| LoadTargetPosX / ControlDomainResolve / IsPointInCircle | 同上家族参数化 | 是 |

120/120 仍是 `covered`。没有把任何节点降成未覆盖来躲。本轮 `python3 scripts/generate-graph-op-node-galleries.py --strict` 后工作区干净。声称「105」是过时数字，现在是 120。

**不挡关单的缺口：** Python 与 C# 两套展开规则没有对等测试；9 个事件节点共用参数化画廊而不是各自一条。字母合同已满足。

### 3.5 S15 · #948 — 可关

验收页只剩一个一级标题。`SUMMARY.md` 不再指向已删的下半标题。互相矛盾的覆盖表合成一张，按本树实测写：

- `bt.patrol` 跨拍续跑写成 **已覆盖**。下半那句「缺一条 bt.patrolStep 真 Yield」删了。名称以资产 `bt.patrol` 为准。
- 「技能阶段不能调用 ActionLib」写成 **合同原文未覆盖**：原文节点 `InvokeAction` 全仓不存在；真正的守卫是 Effect 上按函数名调 ActionLib 的失败关闭。查询方言直绑图号按主线写仍开着，并写明「本树未合入查询口收口」。没有把 #941 当成已合。
- DoT / Effect→FuncLib / Score 写成 **部分覆盖**，缺什么写什么。
- 没有「已落地」。没有删场景来让表好看。没有把等价守卫写成合同原文已覆盖。

`pwsh ./scripts/validate-docs.ps1`：Documentation validation passed。链接修复还在。

### 3.6 S9 · #953 — 可关（叠在 #944 上审）

七个生产宿主不再直连内部执行入口：

| 宿主 | 现在走的门 |
|------|------------|
| `BehaviorTreeWorld` | `RequireHostKind(Script)` + `GraphExecutor.ExecuteRegisteredSlice` |
| `GraphProgramHfsmHost` | 同上（修前连种类都不查） |
| `LevelScriptPrograms` | 同上 |
| `EffectPhaseExecutor` | `GraphFrame.Bind` + `GraphExecutor.Execute` |
| `PerformerRuleSystem` | 同上 |
| `GraphReturnWriter` | 同上 |
| `AbilityAimPresentationRuntime` | 同上 |

Core 生产里 `GasGraphOpHandlerTable.Execute / ExecuteSlice` 只剩 `GraphExecutor` 两处内部调用。

三条验收测试都在 `GraphFrameFrontDoorTests`，打开过：

- 效果图绑到行为树叶子 → `GAS.GRAPH.ERR.KindMismatch`，点名种类
- 跳到程序外的跳转 → 登记失败，程序不进表
- 预算 1 步挂起后再给 8 步 → 从断点续跑，传感器只喂一次（已产生的副作用不重放）；掉出程序尾部抛错，不再当成功

没有放宽种类校验，没有把脚本方言变宽。

**已知缺口（证实，不升级）：** 图号仍是普通整数。`AssemblyInfo.cs` 给十个展厅 Mod 开了 `InternalsVisibleTo`，展厅仍可直调内部入口。

**指定过滤器：** 第一次 101 项里 100 绿、1 红（`CombinedThinkWaves` 5ms，over5ms=8）。同机立刻对 #944 跑同一条为绿；对 #953 单测复跑 `avg=3.392 over5ms=0`，#944 复跑 `avg=3.419 over5ms=0`。记冷启动抖动，不当 S9 回归，也不当放宽门槛。

### 3.7 债（不挡关单）

| # | 项 | 票 |
|---|----|----|
| d1 | `DeferredTriggerCollectionSystem` 基准约 8.6MB 分配，只打印。按 S8 合同应单独开票，不要补零分配门 | S8 |
| d2 | InlineQuery 两条基准非零分配，只打印 | S8 |
| d3 | 模拟相守卫是点名清单，不是全相扫描；Tab 缺裁掉仍能切的专测 | S10 |
| d4 | 覆盖归因 Python/C# 双份，无对等测试 | S11 |
| d5 | 图号仍是整数；展厅仍有内部入口 | S9 已知 |
| d6 | S3 / S6 / S7 仍在 #941，本轮未审 | — |

没有 Major。没有阻断项。

---

## 4. 场景

**作者跑「零分配」测试。** 以前：打印一串数字就绿。现在：真是零才会绿；那条 8.6MB 的不会假装是零。

**把 Cleanup 挪到阶段表最前面。** 以前：守卫仍绿。现在：红。

**镜头外点自己的人、下停止。** 以前：相机裁掉就点不到。现在：点得到，令发得出。

**覆盖表写着某个节点已覆盖。** 以前：可能指着别人的画廊。现在：打开那条测试，展开后就是这个节点。手改生成物，CI 会红。

**打开验收页。** 以前：两个一级标题，同一条场景一边说已覆盖一边标缺口。现在：一份。查询方言直绑图号仍写未覆盖。

**行为树叶子挂错成效果图，或图里跳到外面，或当拍预算用完。** 以前：种类不对可能空引用；坏跳转报成功；预算耗尽下一拍从头再来、副作用重放。现在：绑定时说明种类不对；登记时拒坏跳转；下一拍从断点接着跑。

---

## 5. 边界

**包含：** #951 / #952 / #950 / #948 相对 main，以及 #953 相对 #944 的 S9 提交。

**不包含：** S3 / S6 / S7 / #941；S2 / S4 收口；S12 / S13 / S14；把合同改成已落地；实现修复。

---

## 6. UAT

```gherkin
Feature: 第二批和 S9 的主故障是真关了
  作为维护者
  我希望测试和文档不再盖假章
  并且门是真的、镜头外仍能指挥

  Scenario: 名字里写零分配的测试必须能红
    Given 一批改过的零分配 / 基准测试
    When 分配量超过它们写下的数字
    Then 测试必须失败
    And 约八兆的那条不得写成零分配门

  Scenario: 打乱阶段顺序必须红
    Given 我把系统阶段的顺序打乱
    When CI 跑架构守卫
    Then 守卫必须失败

  Scenario: 镜头外的单位仍然可以被指挥
    Given 我的一个单位当前被相机裁掉
    When 我点它或框到它并下停止
    Then 指令必须生效

  Scenario: 覆盖表登记的测试必须真跑那个节点
    Given 覆盖表给某个图节点登记了一条测试
    When 守卫展开这条登记
    Then 那条测试必须真的执行这个节点

  Scenario: 验收页只有一份结论
    Given 我打开 FuncLib / ActionLib 验收页
    Then 只能有一个一级标题
    And 查询方言直绑图号必须写成未覆盖
    And 不得把 #941 写成已合

  Scenario: 图必须走同一道门
    Given 我把一张效果图绑到行为树叶子上
    When 代理执行到这个叶子
    Then 必须失败并说明种类不对
    And 跳到程序外的跳转在登记时失败
    And 预算耗尽后下一拍从断点继续、已产生的效果不重放
```

关单门槛（本轮认定均已达到）：

- S8 / S11 / S15：主故障就是测试和文档本身，过关可关。
- S10：守卫挡住模拟相选中路径读裁剪，可关。
- S9：七个宿主和三条验收成立可关。typed 图号和展厅内部入口不是本票关单条件。

---

## 附录 A — 测试证据

独立 worktree、`-m:1`：

| 票 | 命令 | 结果 |
|----|------|------|
| #951 S8 | `AllocationTests\|GraphPerfTests\|GasBenchmarkTests\|EffectPhaseStressTests\|GraphBehaviorPressureMatrixTests` | 17 / 17 |
| #951 S8 | `ArchitectureTests` × `ArchitectureGuardTests` | 45 / 45 |
| #952 S10 | `ArchitectureGuardTests\|Rfc0065\|PerformContracts` | 83 / 83 |
| #952 S10 | `OrderBufferSystem\|CommandSource\|CameraCulledEntity\|UsesWorldPositionCm` | 47 / 47 |
| #950 S11 | `GraphNodeOpCoverageRegistryTests` | 5 / 5 |
| #950 S11 | `python3 scripts/generate-graph-op-node-galleries.py --strict` + 工作区干净 | 120 条，零漂移 |
| #948 S15 | `pwsh ./scripts/validate-docs.ps1` | passed |
| #953 S9 | 需求指定过滤器 | 第一次 100 / 101（思考波 5ms）；单测复跑绿，#944 同机 `avg=3.419`，#953 `avg=3.392` |

日志：`/opt/cursor/artifacts/s8_951_bench_tests.log`、`s8_951_arch_guard.log`、`s10_952_arch_tests.log`、`s10_952_selection_tests.log`、`s11_950_coverage_guard.log`、`s11_950_generator_strict.log`、`s15_948_validate_docs.log`、`s9_953_front_door_tests.log`、`s9_953_arena_retest.log`、`s9_944_arena_retest.log`。

---

## 附录 B — 不是本轮修复

1. 给 `DeferredTriggerCollectionSystem` 约 8.6MB 分配单独开票。不要补 ≤64。
2. S10 守卫若要收到「所有模拟相」，那是加严，不是 S10 关单条件。
3. S9 的 typed 图号和展厅内部入口留给后续票，不要在本票重开。
4. 不要审、不要合 #941。
