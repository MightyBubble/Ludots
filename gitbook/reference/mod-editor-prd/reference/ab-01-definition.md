# ab-01 reference · 技能定义骨架

> 现状参考。第一性需求见 [ab-01 PRD](../prd/ab-01-definition.md)；配置说明见 [ab-01 配置说明](../config/ab-01-definition.md)。

## 1. 现状快照

- 加载入口：默认根 `GAS/abilities.json`，ArrayById 按 id 收集，**按 id 排序后注册**；单条失败聚合为 AggregateException 一次抛出（不中断扫描）。
- 顶层块：exec 必填；cooldown（valueAttribute 须已注册 + tag 至少其一）；blockTags{requiredAll,blockedAny}；categories 纯分类（AbilityCategoryRegistry）；interactionContextProfile 非空 Trim；activationPrecondition.validationGraph 必填已注册；toggleSpec（toggleTag 必填、activeEffects ≤4、deactivateExec 可选）；targeting（castRangeCm 必填非负 0=自施、impactEffect 必填已注册）；presentation（九字段，全空→null，mode 键须解析为 InteractionModeType）；input（五字段至少一项）；useRequirement/showRequirement 未知名抛。
- 专门报错的历史字段：indicator、onActivateEffects、瞄准表现族（aimVisual/areaPerformerId/rangeCirclePerformerId/previewPerformerId/performerId 递归扫描拒）、四项改名（cooldown.cooldownValueAttribute→valueAttribute、cooldown.cooldownTag→tag、toggleSpec.tag→toggleTag、targeting.range→castRangeCm）、clockId "Turn" 已移除。
- presentation token：必须已注册且 locale 有模板，拒 Unknown 与 `Ability#` 前缀兜底键。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 加载入口与排序注册 | src/Core/Gameplay/GAS/Config/AbilityExecLoader.cs:43-91 |
| 单条编译与顶层块 | AbilityExecLoader.cs:96-233 |
| exec 编译 | AbilityExecLoader.cs:256-298（items ≤16 :277-281）；item 字段 :366-421 |
| callerParams 池编译 | AbilityExecLoader.cs:437-489 |
| cooldown 编译（含旧名报错） | AbilityExecLoader.cs:322-364 |
| toggle/targeting 编译 | AbilityExecLoader.cs:493-545、547-594 |
| presentation 编译 | AbilityExecLoader.cs:629-745；input :747-821 |
| 瞄准表现族递归拒 | AbilityExecLoader.cs:25-32、596-627 |
| 进度需求解析 | AbilityExecLoader.cs:236-252 |
| clockId "Turn" 移除 | AbilityExecLoader.cs:832 |
| token 校验 | src/Core/Gameplay/GAS/Config/AbilityPresentationTextValidator.cs:33-38、164-182 |
| 真实实例 | mods/showcases/rts_red_alert_like/RtsRedAlertLikeShowcaseMod/assets/GAS/abilities.json |

**相关文档**：[ab-01 PRD](../prd/ab-01-definition.md) · [ab-02 reference](ab-02-exec-timeline.md)
