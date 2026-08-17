# ed-02 reference · 热应用白名单与边界

> 现状参考。第一性需求见 [ed-02 PRD](../prd/ed-02-hot-apply.md)；配置说明见 [ed-02 配置说明](../config/ed-02-hot-apply.md)。

## 1. 现状快照

- 效果模板通道：TryReplaceHotNumericField 支持 duration.durationTicks / duration.periodTicks / modifiers.0.value（`modifiers[0].value` 等价；duration 两字段另有短名等价写法），值需 ≥0、首修改器须已存在；TryReplaceHotProjectileEffectRef 仅 LaunchProjectile 预设的 projectile.impactEffect/hitEffect/presentationEffect；TryReplaceHotGrantedTagFixed 替换槽 0（无授予则追加）；RestoreHotTemplate 按快照回滚。
- TryReplaceHotNumericField 的 XML 注释漏写 modifiers.0.value——文档代码漂移（治理项 R4）。
- 图通道：GraphProgramRegistry.ReplaceProgram 同 id 同 kind；流水线替换前克隆原程序与符号，失败以克隆恢复。
- tag 通道：TagOps.ReplaceTagRuleSet 仅已注册 tagId，未注册即拒并注明"新 tag 身份需 EngineRestart"；替换用先前权威快照回滚。
- 属性约束通道：AttributeRegistry.ReplaceConstraints 三边界（id 已注册/旧约束非空/新约束非空）。
- 提交编排：四通道由 LiveGasEditPipeline 在安全帧按 Graph→EffectNumeric→TagRule→AttrConstraint→EffectRef→GrantedTag 顺序提交、失败逆序回滚（见 ed-01 reference）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 数值热字段（含等价写法） | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:475-548 |
| 注释漂移点（R4） | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:476 |
| 弹道引用热改 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:550-601 |
| 模板快照回滚 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:603-621 |
| 授予 tag 槽 0 热改 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:623-660 |
| 图程序替换 | src/Core/GraphRuntime/GraphProgramRegistry.cs:103-120 |
| tag 规则集替换 | src/Core/Gameplay/GAS/TagOps.cs:120-126 |
| 属性约束三边界 | src/Core/Gameplay/GAS/Registry/AttributeRegistry.cs:67-93 |
| 提交顺序与逆序回滚 | src/Core/Gameplay/GAS/LiveSkillWorkbench/LiveGasEditPipeline.cs:179-290 |

**相关文档**：[ed-02 PRD](../prd/ed-02-hot-apply.md) · [ed-01 reference](ed-01-workbench-base.md) · [attr-01 reference](attr-01-definition.md)
