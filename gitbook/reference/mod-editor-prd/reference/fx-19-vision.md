# fx-19 reference · 视野揭示

> 现状参考。第一性需求见 [fx-19 PRD](../prd/fx-19-vision.md)；配置说明见 [fx-19 配置说明](../config/fx-19-vision.md)。

## 1. 现状快照

- 无专属 presetType：revealArea 块可挂任意模板；限 Instant/After，After 需 periodTicks>0；radius>0；scope 需 ScopeKeyRegistry；layers 1..KnowledgeAreaRevealDescriptor.MaxLayers（=4）且逐层需 FogLayerRegistry；memoryTtlTicks>=0；detectionStrength 0..255。
- HandleRevealArea/HandleDecayRevealArea 调 KnowledgeAreaRevealRuntime（Reveal/DecayArea），中心不可解析静默跳过；两者注册为 Unsupported(Vision)。
- 全代码库无两者调用点；计划编译对 Unsupported 一律抛 `GAS.EFFECT_PLAN.ERR.UnsupportedOperation`——字段全部可写但含本块模板无法通过 FinalizeAll。
- 仓库无 mod 使用该块；唯一 JSON 实例在集成测试内嵌字符串。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 块挂载与生命周期校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:386-400 |
| revealArea 编译 | EffectTemplateLoader.cs:688-759 |
| 揭示/衰减处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:619-671 |
| Unsupported(Vision) 注册 | BuiltinHandlers.cs:77-78 |
| 计划编译 fail-closed | src/Core/Gameplay/GAS/EffectExecutionPlan.cs:600-603 |
| 层上限常量 | src/Core/Vision/KnowledgeAreaRevealRuntime.cs:11 |
| 揭示运行时 | src/Core/Vision/KnowledgeAreaRevealRuntime.cs:30-32 |
| 测试内嵌实例 | src/Tests/GasTests/Integration/CoreHeroSkillInfraTests.cs:236-258 |

**相关文档**：[fx-19 PRD](../prd/fx-19-vision.md) · [fx-19 配置说明](../config/fx-19-vision.md)
