# fx-03 reference · Preset 类型系统

> 现状参考。第一性需求见 [fx-03 PRD](../prd/fx-03-preset-types.md)；配置说明见 [fx-03 配置说明](../config/fx-03-preset-types.md)。

## 1. 现状快照

- `assets/GAS/preset_types.json` 实存 16 条：InstantDamage、Heal、Buff、DoT、HoT、ApplyForce2D、Search、PeriodicSearch、LaunchProjectile、CreateUnit、Displacement、Relation、Exchange、CompleteProgression、SubmitOrderFromBlackboard、DeployConsumeSource（全表见配置说明）。HoT 与 PeriodicSearch 仅允许 After；Displacement/Exchange/CompleteProgression/SubmitOrderFromBlackboard/DeployConsumeSource 的 components 为空；DeployConsumeSource 的 OnApply 为 graph 图处理器。
- PresetTypeLoader 全字段必填；handler 仅 type=builtin|graph。
- PresetTypeRegistry：内建枚举名占固定 id；mod 预设从 FirstModPresetTypeId=1024 起，上限 2048；Freeze 后拒注册。
- 组件名到块名映射（ComponentFlags 11 值）：ModifierParams→modifiers、DurationParams→duration、TargetQueryParams→targetQuery、TargetFilterParams→targetFilter（无 preset 声明）、TargetDispatchParams→targetDispatch、ForceParams→configParams 力保留键、ProjectileParams→projectile、UnitCreationParams→unitCreation、RelationParams→relation、PhaseGraphBindings→phaseGraphs、PhaseListenerSetup→phaseListeners。
- components 为纯声明性元数据，不驱动块校验。
- 核心 effects.json 仅 1 条模板（Effect.Preset.ApplyForce2D），16 preset 绝大多数无核心资产消费者（todo/effect.md E1）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 16 条 preset 现状 | assets/GAS/preset_types.json:1-149 |
| 加载必填与 handler 二态 | src/Core/Gameplay/GAS/Config/PresetTypeLoader.cs:155-181 |
| id 段与上限、Freeze | src/Core/Gameplay/GAS/PresetTypeRegistry.cs:12-13 |
| 组件名→块名映射 | src/Core/Gameplay/GAS/Config/GasEnumParser.cs:220-233 |
| components 声明性定位 | src/Core/Gameplay/GAS/EffectExecutionPlan.cs:480-498 |
| presetType 解析序 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1161-1174 |

**相关文档**：[fx-03 PRD](../prd/fx-03-preset-types.md) · [fx-02 reference](fx-02-template.md)
