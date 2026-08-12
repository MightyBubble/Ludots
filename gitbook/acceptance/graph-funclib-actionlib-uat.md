# Graph FuncLib / ActionLib UAT 映射

本页映射 `gitbook/architecture/graph-funclib-actionlib-contract.md` §6 的 Gherkin 场景到当前验收/单测。状态只描述已知测试事实；没有玩家可见路径或端到端覆盖时明确标为 gap。

| 合同 §6 场景 | 当前测试名 | 状态 | Gap |
|--------------|------------|------|-----|
| 用模板字段而不是 Yield 定义 DoT | `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectWaitYieldAndLoopSugar_FailClosed`; `GraphScriptWaitLoopSugarTests.GraphKindOperationPolicy_NonScriptKinds_RejectYield`; `CoreHeroSkillInfraTests` phaseGraphs 配置解析片段 | Partial | 缺一条持续效果 OnPeriod 端到端 UAT：Duration/Period 驱动多跳结算，并断言阶段图无 Wait/Yield。 |
| Effect 阶段调用 FuncLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectInvokeScriptFunctionName_CompilesAndPatchesViaFuncLib`; `GraphEffectAuthoringExpressivenessTests.FrontDoor_EffectBranchBool_CompilesToJumps` | Partial | 缺技能命中进入 OnApply 后调用真实 `damage.falloff` FuncLib 并完成当次效果事务的 UAT。 |
| 含 Yield 的图不能进 FuncLib | `GraphFunctionCatalogLoaderTests.FuncCatalogLoader_RejectsYieldProgram`; `GraphFunctionCatalogLoaderTests.FuncCatalogLoader_RejectsPureScriptInvokeScriptGraphIdClosureToYield`; `GraphFunctionCatalogLoaderTests.FuncCatalogLoader_RejectsPureScriptInvokeScriptFunctionNameClosureToYieldBeforePatch`; `LiveGasEditPipelineTests.Classify_FuncLibGraphBodyReplaceThatReachesYield_MapReloadRequired` | Covered contract-level | 缺面向作者的错误文案截图/launcher UAT；加载器与热改分类已有失败关闭覆盖。 |
| 行为树叶子调用 ActionLib | `GraphActionCatalogLoaderTests.LoadsScriptAction_WithYieldProgram`; `ScriptFlowSandboxShowcaseAcceptanceTests.DrinkUntilFull_YieldsThenHaltsAtLimit`; `BehaviorTreeArenaShowcaseAcceptanceTests.RegistryName_DelegatesToSeparatedSuite` | Partial | 缺一条 `bt.patrolStep` 真 Yield ActionLib 叶子在 BT 宿主中跨拍续跑，并让玩家看到巡逻一步完成的 showcase 验收。 |
| 技能阶段不能调用 ActionLib | 无 | Gap | 需要作者前门测试：Effect 图写入 InvokeAction/ActionLib 名称时编译失败，并说明 Action 不得进入效果事务。 |
| Score 图产出分数且可调 FuncLib | `GraphEffectAuthoringExpressivenessTests.FrontDoor_LinearKindsInvokeScriptFunctionName_Compile` 覆盖 Score kind 的线性 InvokeScript authoring | Partial | 缺 Score 图通过真实 FuncLib catalog patch、对两个候选产出分数，并断言无 Yield/无副作用的 UAT。 |

## Showcase first wave

| Showcase | 测试名 | 玩家可读断言 |
|----------|--------|--------------|
| `capability_standard_ability_graph_sandbox` | `AbilityGraphSandboxShowcaseAcceptanceTests.RegistryName_DelegatesToSeparatedSuite`; `GraphBehaviorSeparatedShowcaseAcceptanceTests.AbilityGraphSandbox_CastArc_UnderBudget` | `Detail` 必须包含“查一圈”“挂状态”“加好感”“状态牌”，避免只展示 opcode 名称。 |
