# ed-02 runtime spec · 热应用白名单与边界

> 引擎实现任务书。第一性需求见 [ed-02 PRD](../prd/ed-02-hot-apply.md)；现状见 [reference](../reference/ed-02-hot-apply.md)。

## 1. 概述

四通道热替换合同：效果模板字段、图程序、tag 规则集、属性约束——各自边界与回滚。

## 2. 设计

- 效果模板通道保持：TryReplaceHotNumericField 仅 duration.durationTicks/duration.periodTicks/modifiers.0.value（点写与下标写等价归一）；TryReplaceHotProjectileEffectRef 仅 LaunchProjectile 预设的 projectile.impact/hit/presentationEffect；TryReplaceHotGrantedTagFixed 槽 0（无则追加）；RestoreHotTemplate 快照回滚。
- 图通道保持：ReplaceProgram 同 id 同 kind；替换前克隆原程序/符号/源映射，失败恢复。
- tag 通道保持：ReplaceTagRuleSet 仅已注册 tagId——错误信息显式"新 tag 身份需 EngineRestart"。
- 属性约束通道保持三边界（id 已注册/旧约束非空/新约束非空）。
- 拒绝路径统一带"升级路径"文案（重进地图/重启），与四级分级枚举同词表。
- **治理项 R4**：TryReplaceHotNumericField 的 XML 注释只写了 duration 两路径，漏 modifiers.0.value——文档代码漂移；注释与实现同步（此类白名单注释即合同，必须全列）。

## 3. 精确语义与不变量

- 热替换不改身份：模板 id、图 id、tag id、属性 id 前后一致。
- 每通道替换失败后注册表与替换前逐字段一致。
- 白名单是封闭集：新增热字段需先过本 spec 变更评审，不允许实现先行。

## 4. 迁移与治理

现状即基线；R4 为注释修正（低风险、随下次触碰该文件带上）。处置入 TODO（见 todo/runtime.md）。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[ed-02 PRD](../prd/ed-02-hot-apply.md) · [reference](../reference/ed-02-hot-apply.md)
