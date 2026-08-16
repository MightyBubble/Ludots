# fx-13 reference · 参数化

> 现状参考。第一性需求见 [fx-13 PRD](../prd/fx-14-config-params.md)；配置说明见 [fx-13 配置说明](../config/fx-14-config-params.md)。

## 1. 现状快照

- 七类型：Float/Int/EffectTemplate/Attribute/ExchangeOperation/EntityTemplate/LifecycleAttributeValueSource（仅 Base/Current）；容量 `EFFECT_CONFIG_PARAMS_MAX`（事实页）。
- 键经 ConfigKeyRegistry.Register 归一 int id（上限 4095 键）；`_ep.*` 保留键由 EffectParamKeys.Initialize 在模板加载前注册。
- 合并三路径：实体路径优先读创建时预合并的 EffectConfigParams 组件；请求路径 template+request.CallerParams；Instant 内联每次现算。
- MergeFrom：caller 同键连 Types 一起覆盖；新键容量内追加，**满时静默丢弃**（无任何计数或日志）。
- ApplyForce2D 力值先读 CallerParams 后读模板；创建实体时预合并存组件。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 七类型编译 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1283-1431 |
| 保留键全集注册 | src/Core/Gameplay/GAS/EffectParamKeys.cs:69-140 |
| 键注册表 | src/Core/Gameplay/GAS/Registry/ConfigKeyRegistry.cs:5-10 |
| 实体/请求两条合并路径 | src/Core/Gameplay/GAS/ConfigParamsMerger.cs:18-47 |
| MergeFrom 覆盖与静默丢弃 | src/Core/Gameplay/GAS/Components/EffectConfigParams.cs:193-221 |
| 创建时预合并 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1699-1710 |
| 内联现算 | EffectProposalProcessingSystem.cs:1864-1870 |
| 力值 caller 优先 | EffectProposalProcessingSystem.cs:1385-1407 |
| 容量常量 | src/Core/Gameplay/GAS/GasConstants.cs:50 |

**相关文档**：[fx-13 PRD](../prd/fx-14-config-params.md) · [fx-13 配置说明](../config/fx-14-config-params.md)
