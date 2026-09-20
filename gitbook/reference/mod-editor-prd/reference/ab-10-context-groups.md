# ab-10 reference · 上下文组

> 现状参考。第一性需求见 [ab-10 PRD](../prd/ab-10-context-groups.md)；配置说明见 [ab-10 配置说明](../config/ab-10-context-groups.md)。

## 1. 现状快照

- 加载器：rootAbilityId 必填已知技能；searchRadiusCm 必填非负；candidates 非空；候选 abilityId 必填、preconditionGraph/scoreGraph 可选但须可解析（kind 校验在运行期消费时才做）、requiresTarget/basePriority 必填；requiresTarget=true 时 maxDistanceCm/distanceWeight/maxAngleDeg/angleWeight/hoveredBiasScore 全必填，false 可缺省 0。
- 打分消费（ContextScoredOrderResolver）：I0=根槽 → TryGetByRootAbility → SearchRadius 空间查询 + 视知门；逐候选：score 从 basePriority 起步；maxDistanceCm 硬过滤 + (1−d/max)×distanceWeight；maxAngleDeg 对 FacingDirection 硬过滤 + 归一化 angleWeight；悬停加 hoveredBiasScore；preconditionGraph 要求 Validation、scoreGraph 要求 Score 累加；平分先比实体 Id 再比槽号。
- 真实数据：interaction 沙盒 3 组（arcweaver/vanguard/commander），候选 2-4 个不等。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 表加载与校验 | src/Core/Gameplay/GAS/Config/ContextGroupConfigLoader.cs:24-152 |
| 打分消费 | src/Core/Input/Orders/ContextScoredOrderResolver.cs |
| 真实实例 | mods/showcases/interaction/InteractionShowcaseMod/assets/GAS/context_groups.json |

**相关文档**：[ab-10 PRD](../prd/ab-10-context-groups.md) · [ab-09 reference](ab-09-targeting.md)
