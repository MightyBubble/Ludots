# FuncLib / ActionLib UAT（Epic #915）

## 1. 概述

图复用库与可挂起动作的合同验收：FuncLib 纯函数无 Yield；ActionLib 供行为调度跨拍；Effect 可调 FuncLib 不可调 ActionLib；L1 Kind 前门与加载期失败关闭。

对照：[图复用库合同](../architecture/graph-funclib-actionlib-contract.md) §6。本页是该合同场景的唯一验收映射；状态只写本树（`main`）实测，不把等价守卫写成合同原文已覆盖。

## 2. 结构

加载期 catalog 校验 → L1 前门编译 → 运行时 ScriptSlice / Effect 事务边界 → Showcase 玩家可读断言。

## 3. 详情

覆盖 Epic #915 P3：至少一个 `bt.*` ActionLib 叶子真 Yield；Query Kind FuncLib `InvokeScript.functionName`；行为树跨拍 resume。

名称对齐：ActionLib 巡逻条目以资产名 `bt.patrol` 为准（`assets/Configs/GAS/action_lib.json` → `Graph.BT.Leaf.Patrol`，图内含 `Yield`）。合同 §6 写作 `bt.patrolStep`，本页按资产名映射，不另立一条场景。

合同 §6「技能阶段不能调用 ActionLib」原文节点是 `InvokeAction`。全仓 `GraphNodeOp` 无此作者节点；作者侧实际调用口是 `InvokeScript`。

## 4. 场景

```gherkin
Feature: 效果时间轴与阶段图分工
  Scenario: 用模板字段而不是 Yield 定义 DoT
    Given 我创建一条灼烧效果并设置持续 10 秒、每 1 秒跳一次
    And OnPeriod 阶段绑定一张结算图
    When 战斗进行超过 1 秒
    Then 系统应再次执行 OnPeriod 结算图
    And 该结算图不得包含 Wait 或 Yield 节点
    And 靶子应按跳动次数受到对应结算

Feature: 纯函数库可被技能阶段复用
  Scenario: Effect 阶段调用 FuncLib
    Given FuncLib 中登记了名为 damage.falloff 的纯函数且不含 Yield
    And 某技能的 OnApply 图调用该函数
    When 技能命中并进入 OnApply
    Then 衰减计算应生效并完成当次阶段
    And 技能效果事务不得因该调用而跨拍挂起

  Scenario: 含 Yield 的图不能进 FuncLib
    Given 作者试图把含 Wait 的图登记进 FuncLib
    When 配置加载或校验运行
    Then 加载必须失败并指出该条目违反纯函数合同

Feature: 可挂起动作只给行为调度用
  Scenario: 行为树叶子调用 ActionLib
    Given ActionLib 中登记了 bt.patrol 且允许 Yield
    And 行为树叶子绑定该动作
    When 代理执行该叶子且当拍未完成
    Then 叶子应在下一拍从断点继续
    And 玩家应看到代理继续完成巡逻一步

  Scenario: 技能阶段不能调用 ActionLib
    Given 作者在 Effect 图中写入 InvokeAction
    When 图通过作者前门编译
    Then 编译必须失败
    And 失败原因应说明 Action 不得进入效果事务

Feature: 纯 Kind 保持纯函数语义
  Scenario: Score 图产出分数且可调 FuncLib
    Given Score 图调用 FuncLib 中的距离衰减函数
    When 系统对两个候选执行打分
    Then 每个候选应得到一个分数
    And 打分过程不得 Yield
    And 不得对世界施加技能效果类副作用
```

## 5. 边界

Score / Validation FuncLib 登记延后至 `InvokeScore` / `InvokeValidation` 真调用路径；Derived FuncLib 默认不进库；Showcase 不以假 Script 旁路冒充 ActionLib 加载。本页不改合同落地状态。

## 6. UAT

| 合同 §6 场景 | 本树测试 | 状态 | 未覆盖部分 |
|--------------|----------|------|------------|
| 用模板字段而不是 Yield 定义 DoT | `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectWaitYieldAndLoopSugar_FailClosed`；`GraphScriptWaitLoopSugarTests.GraphKindOperationPolicy_NonScriptKinds_RejectYield`；`LifetimeConditionTests.EffectLifetime_OnPeriodPostGraph_ModifiesTargetAttributeCurrent`（`periodTicks=2` 驱动 OnPeriod 图改属性） | 部分覆盖 | 缺合同原文那种 Duration/Period 多跳灼烧端到端 UAT：阶段图无 Wait/Yield，且靶子按跳动次数结算。 |
| Effect 阶段调用 FuncLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib`；`GraphFunctionCatalogLoaderTests.Compile_InvokeScript_ByFunctionName_PatchesToGraphId` | 部分覆盖 | FuncLib 资产无 `damage.falloff`（现有 `demo.const.seven` / `ability.slash` / `ability.bash`）。缺技能命中进入 OnApply 后调用真实衰减函数并完成当次效果事务的 UAT。 |
| 含 Yield 的图不能进 FuncLib | `GraphFunctionCatalogLoaderTests.FuncCatalogLoader_RejectsYieldProgram`；`FuncCatalogLoader_RejectsPureScriptInvokeScriptGraphIdClosureToYield`；`FuncCatalogLoader_RejectsPureScriptInvokeScriptFunctionNameClosureToYieldBeforePatch`；`LiveGasEditPipelineTests.Classify_FuncLibGraphBodyReplaceThatReachesYield_MapReloadRequired` | 已覆盖 | 加载器与热改分类已失败关闭。缺面向作者的错误文案截图 / launcher UAT，不影响本条合同场景。 |
| 行为树叶子调用 ActionLib | `BehaviorTreeRuntimeTests.PatrolYield_ResumesAcrossThinkWaves_ThenReturnsPatrolIntent`；`GraphBehaviorSeparatedShowcaseAcceptanceTests.BehaviorTreeArena_PatrolLeaf_YieldsAcrossThinkWaves`；`GraphActionCatalogLoaderTests.LoadsScriptAction_WithYieldProgram`；`ScriptFlowSandboxShowcaseAcceptanceTests.DrinkUntilFull_YieldsThenHaltsAtLimit`（`script.drinkUntilFull` 原子沙盒，旁证而非本条主证据） | 已覆盖 | `bt.patrol` 真 Yield 叶子已在 BT 宿主跨拍续跑。 |
| 技能阶段不能调用 ActionLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectInvokeScriptFunctionNameActionLibName_PatchFailsClosed`（Effect 方言、`InvokeScript.functionName`，符号 patch 失败关闭） | 合同原文未覆盖 | 合同原文要 Effect 图写入 `InvokeAction` 且编译失败；该节点全仓不存在，不得把等价守卫写成原文已覆盖。 |
| Score 图产出分数且可调 FuncLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_LinearKindsInvokeScriptFunctionName_Compile`（Score kind 线性 `InvokeScript`）；`GraphContractTests.GraphKindOperationPolicy_ReadOnlyKindsRejectGameplayWrites`（Score 拒玩法写入） | 部分覆盖 | 缺 Score 图经真实 FuncLib catalog patch、对两个候选产出分数，并断言无 Yield / 无技能效果类副作用的 UAT。 |

合同外、本树已有的相邻守卫（不升格为 §6 已覆盖）：

| 相邻守卫 | 本树测试 | 状态 |
|----------|----------|------|
| Query Kind 调 FuncLib（`InvokeScript.functionName`） | `GraphEffectAuthoringExpressivenessTests.FrontDoor_QueryInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib` | 已覆盖 |
| L1 线性方言拒 `InvokeScript.graphId`（含 Query / Effect） | `GraphEffectAuthoringExpressivenessTests.FrontDoor_LinearKindsInvokeScriptGraphId_FailClosed`（`Effect` / `Query` / `Score` / `Validation` / `Derived`）；`GraphControlFlowCompiler.Query.cs` 与 `Linear.cs` 同一套诊断（缺函数名 `MissingNodeRef`，带了图号 `TypeMismatch`） | 已覆盖 |
| L2 ActionLib 与 FuncLib 同名隔离 | `GraphFunctionCatalogLoaderTests.CoreCatalogs_MoveL2ActionsToActionLib`；`GraphActionCatalogLoaderTests.RejectsFuncLibNameClash` | 已覆盖 |

Preset / Showcase：`capability_standard_behavior_tree_arena_raylib`、`capability_standard_script_flow_sandbox_raylib`、`capability_standard_ability_graph_sandbox_raylib`。

| Showcase | 测试名 | 玩家可读断言 |
|----------|--------|--------------|
| `capability_standard_behavior_tree_arena` | `GraphBehaviorSeparatedShowcaseAcceptanceTests.BehaviorTreeArena_PatrolLeaf_YieldsAcrossThinkWaves`；`BehaviorTreeArenaShowcaseAcceptanceTests.RegistryName_DelegatesToSeparatedSuite` | Detail 含 `patrol leaf yielding`，证明 `bt.patrol` ActionLib 叶子跨 think wave Yield 后续跑。 |
| `capability_standard_script_flow_sandbox` | `ScriptFlowSandboxShowcaseAcceptanceTests.DrinkUntilFull_YieldsThenHaltsAtLimit` | Detail 含 `Script halted`；水位停在上限。 |
| `capability_standard_ability_graph_sandbox` | `GraphBehaviorSeparatedShowcaseAcceptanceTests.AbilityGraphSandbox_CastArc_UnderBudget`；`AbilityGraphSandboxShowcaseAcceptanceTests.RegistryName_DelegatesToSeparatedSuite` | Detail 必须包含「巡逻查一圈」「挂状态」「加好感」「状态牌」，避免只展示 opcode 名称。 |
