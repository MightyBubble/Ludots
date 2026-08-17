# gr-09 reference · 挂接点总表

> 现状参考。第一性需求见 [gr-09 PRD](../prd/gr-08-mount-points.md)；配置说明见 [gr-09 配置说明](../config/gr-08-mount-points.md)。

## 1. 现状快照

八主挂点现状：

| 挂点 | kind | 锚点 |
|---|---|---|
| 效果相位图 | OnPropose→Validation，其余→Effect | EffectPhaseExecutor.cs:630,802 |
| 相位监听 | 同相位 + 纯度闸 | EffectPhaseExecutor.cs:613-660,796-812；EffectPhaseListenerBuffer.cs:62-63 |
| 派生属性 | Derived | AttributeAggregatorSystem.cs:106-113 |
| 能力前置 | Validation | AbilityActivationPreconditionEvaluator.cs:40-49 |
| 订单校验 | Validation | OrderBufferSystem.cs:526-542；ContextScoredOrderResolver.cs:238,260 |
| AI 打分 | Score | UtilityAiRuntimeEvaluator.cs:862-864 |
| BT 叶 | Script | BehaviorTreeWorld.cs:443 |
| HFSM | Script | GraphProgramHfsmHost.cs:121 |

次要挂点现状：关卡脚本（LevelScriptPrograms.cs:22-50，步数预算 64、禁挂起）、进度校验（ProgressionRequirementEvaluator.cs:387）、表现规则（PresenterRuleSystem.cs:334,760——条件 Validation、参数 Score）、瞄准预览（AbilityAimPresentationRuntime.cs:389-393）、Query 物化（GraphReturnWriter.cs:49）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 八挂点锚点 | 见上表 |
| kind 终检（含中文文案） | src/Core/GraphRuntime/GraphProgramRegistry.cs:197-198 |
| Score 图消费实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/GAS/graphs.json |
| 相位 scratch 容量 | assets/game.json gasRuntimeCapacity（事实页） |

**相关文档**：[gr-09 PRD](../prd/gr-08-mount-points.md) · [gr-04 reference](gr-03-kinds.md) · [gr-09 reference](gr-09-outputs.md)
