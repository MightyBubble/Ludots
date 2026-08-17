# misc-01 reference · 进度域

> 现状参考。第一性需求见 [misc-01 PRD](../prd/misc-01-progression.md)；配置说明见 [misc-01 配置说明](../config/misc-01-progression.md)。

## 1. 现状快照

- 三表 `Progression/scopes.json`、`progressions.json`、`requirements.json`（ArrayById；目录计数见事实页）。
- scopes：id、memberSource（ScopeBinding | EntityCollection）、collection（EntityCollection 时必填且集合须已配置）。
- progressions：id、scope。
- requirements：id、root 条件树（EntityCount + scope/entitySource/count/tags 等组合）。
- 运行：ProgressionScopeBindingSystem 维护成员；RequirementEvaluator 注入效果处理。
- GAS 联动：CompleteProgression 预设要求 lifetime=Instant + progression 块；progression.id 经 ProgressionIdRegistry 解析（上限 4095、可冻结），未注册即抛错。
- 真实用例：FourXAssociationShowcaseMod 三件套；tech tree 展示见 panel_kit_techtree_progression_showcase。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 三表加载校验 | src/Core/Gameplay/Progression/Config/ProgressionConfigLoader.cs:41-101 |
| 范围绑定系统挂接 | src/Core/Engine/GameEngine.cs:1699 |
| 需求求值注入 | src/Core/Engine/GameEngine.cs:1768 |
| CompleteProgression 合同 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:420-445 |
| 样例 | mods/showcases/fourx_association/FourXAssociationShowcaseMod/assets/Progression/ |

**相关文档**：[misc-01 PRD](../prd/misc-01-progression.md) · [misc-02 reference](misc-02-items-exchange.md)
