# ai-08 reference · 目标过滤器

> 现状参考。第一性需求见 [ai-07 PRD](../prd/ai-06-target-filters.md)；配置说明见 [ai-07 配置说明](../config/ai-06-target-filters.md)。

## 1. 现状快照

- 顶层：MaxResults 默认 64 须正；Ops 必填。
- 九 op：SourceSelf/SpatialRadius(+RadiusCm 正)/Relationship(+Value)/HasAllTags(+Tags[])/HasNoneTags(+Tags[])/LayerAny(+Mask 正)/DistanceMax(+MaxCm 正)/AbilityEligible(+AbilityKey required)/RecentAttacker(+TtlSteps 默认 30 正，校验 LastAttacker 存活与 TTL)。
- 判定：顺序 AND，专属拒绝码（Relationship/RequiredTagMissing/BlockedTagPresent/Layer/Distance/MissingPosition 等）；HasAllTags 通过后 priorityBucket+=op.IntB（I4：IntB 恒 0）。
- RecentAttacker 的记忆来自 UtilityAiCombatMemory（清理系统 TTL 300 步）。
- 真实资产：utility_autocast 2 条（Hostile 半径 1600 / Friendly 半径 1200）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 过滤器与 op 编译 | src/Core/Gameplay/AI/Config/AiConfigLoader.cs:487-585 |
| 运行判定链 | src/Core/Gameplay/AI/Utility/UtilityAiRuntimeEvaluator.cs:394-480 |
| IntB 死字段累加 | UtilityAiRuntimeEvaluator.cs:423-432 |
| 战斗记忆与清理 | src/Core/Gameplay/AI/Components/UtilityAiRuntimeComponents.cs:43-49；Systems/UtilityAiSystems.cs:248-296 |
| 拒绝码 | UtilityAiRuntimeEvaluator.cs（UtilityAiFilterRejectReason） |
| 真实例 | mods/showcases/utility_autocast/UtilityAutocastShowcaseMod/assets/AI/target_filters.json |

**相关文档**：[ai-07 PRD](../prd/ai-06-target-filters.md) · [ai-08 reference](ai-07-tasks.md)
