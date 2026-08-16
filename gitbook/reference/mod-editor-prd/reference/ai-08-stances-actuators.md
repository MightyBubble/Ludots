# ai-07 reference · 战斗姿态与执行器门

> 现状参考。第一性需求见 [ai-07 PRD](../prd/ai-08-stances-actuators.md)；配置说明见 [ai-07 配置说明](../config/ai-08-stances-actuators.md)。

## 1. 现状快照

- stances：TargetFilter 可选引用；AutoAcquire/Retaliate/AllowMoveChase 默认 false；编译进 Stances 数组。
- actuators：AbilityKey 可选；ReadinessInput/AimGateInput 可选引用 inputs 表。
- 组件：ActuatorReadiness（ActuatorId/Ready01/BlockReason/EtaSteps/RequiresPreparation）与 AimGate（ActuatorId/Ready01/BlockReason）可从实体配置注入；门控入口 PassesActuatorGates。
- stance 半成品（I6）：无系统消费，仅 AIInspectorMod 的 PrintAiConfigTrigger 打印 Stances.Length；UtilityAiStanceState 无读写；DefaultStanceId 只在测试出现。
- 占位（I7）：utility_autocast 的 stances.json/actuators.json 均为 []。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| stance 编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:791-815 |
| actuator 编译 | AiConfigLoader.cs:817-840 |
| 门控判定 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:730-760 |
| 就绪检查入口 | UtilityAiRuntimeEvaluator.cs:707（PassesDecisionReadiness） |
| 组件定义 | src/Core/Gameplay/AI/Components/UtilityAiRuntimeComponents.cs:65-80 |
| stance 唯一消费（打印） | mods/AIInspectorMod/Triggers/PrintAiConfigTrigger.cs:58 |
| 空占位实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/stances.json、actuators.json |

**相关文档**：[ai-07 PRD](../prd/ai-08-stances-actuators.md) · [ai-08 reference](ai-09-behavior-trees.md)
