# ai-06 reference · 任务

> 现状参考。第一性需求见 [ai-06 PRD](../prd/ai-07-tasks.md)；配置说明见 [ai-06 配置说明](../config/ai-07-tasks.md)。

## 1. 现状快照

- 4 Kind：SubmitOrder/Sequence/Parallel/ParallelComplete；仅 SubmitOrder 强制 OrderTypeKey/OrderId（双查互验）；SubmitMode 默认 Immediate(0)；PlayerId 默认 0；IntArg0 默认 -1；IntArg1 默认 0；AbilityKey 可选；AbilitySlotIndex 默认 -1。
- 运行：Sequence=continue no-op；Parallel/ParallelComplete 仅置 requiredAny——三种组合行为近乎等价（I5）。
- SubmitOrder 构造：槽位回退链 task→decision→TryFindAbilitySlot；I0=slot 或 IntArg0≥0；I1=IntArg1；Spatial=目标 WorldPositionCm；TryEnqueue 失败=Blocked。
- SubmitOrder 首个成功即短路返回 Complete。
- 真实资产：utility_autocast 3 条全 SubmitOrder（castAbility 槽 0/1/2）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 任务编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:729-789 |
| 订单类型双验 | AiConfigLoader.cs:1073-1117 |
| TrySubmitTasks 分派 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:152-216 |
| 订单构造与槽位回退 | UtilityAiRuntimeEvaluator.cs:218-280 |
| 槽位反查 | UtilityAiRuntimeEvaluator.cs:799-807 |
| 另一任务执行系统（黑板节点） | src/Core/Gameplay/AI/Systems/TaskNodeExecutionSystem.cs |
| 真实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/tasks.json |

**相关文档**：[ai-06 PRD](../prd/ai-07-tasks.md) · [ai-07 reference](ai-08-stances-actuators.md)
