# FuncLib / ActionLib UAT（Epic #915）

## 1. 概述

图复用库与可挂起动作的合同验收：FuncLib 纯函数无 Yield；ActionLib 供行为调度跨拍；Effect 可调 FuncLib 不可调 ActionLib；L1 Kind 前门与加载期失败关闭。

对照：[图复用库合同](architecture/graph-funclib-actionlib-contract.md)。

## 2. 结构

加载期 catalog 校验 → L1 前门编译 → 运行时 ScriptSlice / Effect 事务边界 → Showcase 玩家可读断言。

## 3. 详情

覆盖 Epic #915 P3：至少一个 `bt.*` ActionLib 叶子真 Yield；Query Kind FuncLib `InvokeScript.functionName`；行为树跨拍 resume。

## 4. 场景

```gherkin
Feature: 技能周期结算不靠 Wait
  Scenario: 周期效果按 Duration 与 Period 自动重复
    Given 某技能带周期效果且 periodTicks 已配置
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

Score / Validation FuncLib 登记延后至 `InvokeScore` / `InvokeValidation` 真调用路径；Derived FuncLib 默认不进库；Showcase 不以假 Script 旁路冒充 ActionLib 加载。

## 6. UAT

| Cucumber 场景 | 自动化测试 |
|---------------|------------|
| 周期效果按 Duration 与 Period 自动重复 | `GraphEffectAuthoringExpressivenessTests`（Effect 线性方言与周期 op 白名单）；`GasExecutionBudgetTests.EffectLifetime_ResumesExpiredEffectsAcrossWorkSlices` |
| Effect 阶段调用 FuncLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib`；`GraphFunctionCatalogLoaderTests.Compile_InvokeScript_ByFunctionName_PatchesToGraphId` |
| 含 Yield 的图不能进 FuncLib | `GraphFunctionCatalogLoaderTests.FuncCatalogLoader_RejectsPureScriptInvokeScriptGraphIdClosureToYield`；`FuncCatalogLoader_RejectsPureScriptInvokeScriptFunctionNameClosureToYieldBeforePatch` |
| 行为树叶子调用 ActionLib（bt.patrol Yield resume） | `BehaviorTreeRuntimeTests.PatrolYield_ResumesAcrossThinkWaves_ThenReturnsPatrolIntent`；`GraphBehaviorSeparatedShowcaseAcceptanceTests.BehaviorTreeArena_PatrolLeaf_YieldsAcrossThinkWaves`；`ScriptFlowSandboxShowcaseAcceptanceTests.DrinkUntilFull_YieldsThenHaltsAtLimit`（Script ActionLib 原子沙盒） |
| 技能阶段不能调用 ActionLib | `GraphEffectAuthoringExpressivenessTests`（Effect 前门拒 Yield / InvokeAction）；`GraphKindOperationPolicy` 合同测试 |
| Score 图产出分数且可调 FuncLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_LinearKindsInvokeScriptFunctionName_Compile`（Score / Validation / Derived） |
| Query Kind 调 FuncLib（§3.3 所有 L1） | `GraphEffectAuthoringExpressivenessTests.FrontDoor_QueryInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib` |
| L2 ActionLib 与 FuncLib 同名隔离 | `GraphFunctionCatalogLoaderTests.CoreCatalogs_MoveL2ActionsToActionLib`；`GraphActionCatalogLoaderTests.RejectsFuncLibNameClash` |
| LSW FuncLib 热替换含 Yield 拒绝 | `LiveGasEditPipelineTests.Classify_FuncLibGraphBodyReplaceThatReachesYield_MapReloadRequired` |

Preset / Showcase：`capability_standard_behavior_tree_arena_raylib`、`capability_standard_script_flow_sandbox_raylib`、`capability_standard_ability_graph_sandbox_raylib`。
