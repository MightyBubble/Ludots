# fx-23 reference · 进度完成

> 现状参考。第一性需求见 [fx-23 PRD](../prd/fx-21-progression.md)；配置说明见 [fx-23 配置说明](../config/fx-21-progression.md)。

## 1. 现状快照

- loader：progression 块仅 CompleteProgression + Instant；id 经 ProgressionIdRegistry.GetId（<=0 报未注册）；scope 三态（self/explicit 固定，命名走 ScopeKeyRegistry）；level 与 delta 互斥且均须 >0，缺省编译为 Complete。
- runtime：HandleCompleteProgression 以 RoleResolverContext(actor=Source, subject=Target, explicitScopeHost=TargetContext) 解析作用域宿主，ProgressionEvaluator.TryApply 应用变更；宿主解析失败或 ProgressionStateBuffer 未就位抛错；注册为 External(Progression) 独占计划。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块与 preset 组合校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:417-445 |
| 作用域解析 | EffectTemplateLoader.cs:1559-1593 |
| 变更编译（互斥/正数） | EffectTemplateLoader.cs:1595-1628 |
| 进度完成处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:709-741 |
| External(Progression) 注册 | BuiltinHandlers.cs:80 |
| 进度求值器 | src/Core/Gameplay/Progression/ProgressionRequirementEvaluator.cs:13-189 |
| 进度名注册表 | src/Core/Gameplay/Progression/Registry/ProgressionIdRegistry.cs |
| 作用域注册表（revealArea 共用） | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1572-1591 |
| 展示 mod 现货 | mods/showcases/progression_scope/ProgressionScopeShowcaseMod/assets/GAS/effects.json |

**相关文档**：[fx-23 PRD](../prd/fx-21-progression.md) · [fx-23 配置说明](../config/fx-21-progression.md)
