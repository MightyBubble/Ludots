# ai-03 reference · 决策

> 现状参考。第一性需求见 [ai-03 PRD](../prd/ai-04-decisions.md)；配置说明见 [ai-03 配置说明](../config/ai-04-decisions.md)。

## 1. 现状快照

- 全字段默认：Priority 0 / BaseScore 1 / Weight 1 / MomentumBonus 0 / MinDurationSteps 0 / CooldownSteps 0 / AbilitySlotIndex -1；SharedCooldownTag 可选未配回退 ability 冷却 tag；五布尔 Autocast/OrdinaryAttack/RequiresTarget/KeepRunningUntilFinished/ExplicitOrderOnly 与 Flags[] 可混写。
- 考量四件套 Input/Normalization/Curve 必填引用，Weight 默认 1，Aggregate 默认 Multiply（四值 Multiply/WeightedSum/Veto/PriorityBucket）。
- 聚合语义：Veto curved≤0 返 0；WeightedSum/PriorityBucket 入加权和；Multiply 入乘积；总分 (multiply+weighted)×Weight。
- Tasks 必填≥1 且须解析为编译任务表连续区间（I3：跨 mod 分片易触发 contiguous 报错）。
- 真实资产：utility_autocast 3 条（Attack/HealBurst/Curse，全部 WeightedSum 单考量 + GCD 共享冷却）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 决策编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:842-910 |
| 任务连续区间 | AiConfigLoader.cs:912-943 |
| Flags 解析 | AiConfigLoader.cs:1300+（ParseDecisionFlags） |
| 聚合求值 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:482-519 |
| 优先桶 | UtilityAiRuntimeEvaluator.cs:521-545 |
| 冷却/共享冷却状态写入 | src/Core/Gameplay/AI/Systems/UtilityAiSystems.cs:193-205 |
| 真实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/decisions.json |

**相关文档**：[ai-03 PRD](../prd/ai-04-decisions.md) · [ai-04 reference](ai-05-dm-profiles.md)
