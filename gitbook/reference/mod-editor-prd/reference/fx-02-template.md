# fx-05 reference · 效果模板骨架

> 现状参考。第一性需求见 [fx-04 PRD](../prd/fx-02-template.md)；配置说明见 [fx-04 配置说明](../config/fx-02-template.md)。

## 1. 现状快照

- 顶层字段合同在 EffectTemplateConfig：id 与条目 id 逐字同；tags ≤1；presetType 必填显式，解析序 preset_types 注册表→内建枚举→抛；lifetime 必填精确三值；participatesInResponse 必填；expireCondition 可选块 kind/tag/sense；duration 拒标量；共 17 个组件块。
- 禁用字段报错并指路：顶层 period、标量 duration、lifecycleDeploy（指到 configParams 保留键与 preset 图）。
- 跨字段：modifiers 容量 8、ApplyForce2D 预留 2；Instant 禁 phaseListeners；displacement/relation/progression/projectile/unitCreation/submitOrderFromBlackboard 六块"只在对应 presetType 合法且必须带"。
- 注册表：容量 4096；Finalize 后拒绝注册、重复 id 报冲突；FinalizeExecutionPlans 要求四窗口全 finalized。
- 热通道：TryReplaceHotNumericField 白名单仅 duration.durationTicks / periodTicks / modifiers.0.value（≥0，两种写法等价）；另 TryReplaceHotProjectileEffectRef（仅 LaunchProjectile 的 impact/hit/presentation 三个引用位）、RestoreHotTemplate、TryReplaceHotGrantedTagFixed（槽 0）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 顶层字段合同 | src/Core/Gameplay/GAS/Config/EffectTemplateConfig.cs:6-80 |
| id 逐字一致 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:107-115 |
| presetType 显式与解析序 | EffectTemplateLoader.cs:1153-1174 |
| lifetime 必填三值 | EffectTemplateLoader.cs:159-162 |
| participatesInResponse | EffectTemplateLoader.cs:533, 1847-1855 |
| expireCondition | EffectTemplateLoader.cs:1930-1959 |
| 禁用字段 | EffectTemplateLoader.cs:128-149 |
| tags ≤1 | EffectTemplateLoader.cs:209-213 |
| 跨字段容量与六块规则、Instant 禁 phaseListeners | EffectTemplateLoader.cs:243-251, 309-313, 348-502 |
| 注册表容量与 Finalize | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:394-444 |
| 四窗口 finalized 门槛 | EffectTemplateRegistry.cs:707-737 |
| 热字段白名单 | EffectTemplateRegistry.cs:478-547 |
| 弹道引用热替换 | EffectTemplateRegistry.cs:552-603 |
| 恢复模板 / 槽 0 tag 热替换 | EffectTemplateRegistry.cs:609-618, 622-672 |

**相关文档**：[fx-04 PRD](../prd/fx-02-template.md) · [fx-03 reference](fx-01-pipeline.md)
