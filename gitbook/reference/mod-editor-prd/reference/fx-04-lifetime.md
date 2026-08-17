# fx-04 reference · 生命周期与时长

> 现状参考。第一性需求见 [fx-04 PRD](../prd/fx-04-lifetime.md)；配置说明见 [fx-04 配置说明](../config/fx-04-lifetime.md)。

## 1. 现状快照

- 三值 EffectLifetimeKind：Instant（同帧内联，条件为模板 Instant 且周期为 0）、After N tick、Infinite（清理作业直接判不过期，永不自然过期）。
- duration 矩阵：Instant 禁块；After 必带且 durationTicks>0、periodTicks/clockId 显式（可 0）；Infinite 可选、字段可缺省、显式全零块禁、clockId 缺省 FixedFrame；Turn 时钟已移除。
- 周期首拍惰性初始化：仅 After/Infinite 且周期>0；首拍偏移 = FNV hash(RootId/templateId/periodTicks/Source/Target/TargetContext) % periodTicks + 1；period≤1 固定 +1；State<Committed 不参与；时钟取运行时当前值（EntityLocal 以目标为准）。
- 过期路径：After 到期（惰性到时点=now+TotalTicks）→取消请求强制走移除→否则求值过期条件；过期走 OnExpire 再统一 OnRemove；过期/取消各自暂存展示事件。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 三值定义 | src/Core/Gameplay/GAS/EffectLifetimeKind.cs:8-16 |
| duration 矩阵校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:151-207 |
| Turn 时钟移除 | EffectTemplateLoader.cs:1550-1557 |
| Instant 内联条件 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1612-1622 |
| 周期惰性首拍与散列 | src/Core/Gameplay/GAS/Systems/EffectLifetimeSystem.cs:508-569 |
| Infinite 不过期 | EffectLifetimeSystem.cs:613-616 |
| 过期条件与到期路径 | EffectLifetimeSystem.cs:595-630 |
| 过期/取消展示事件 | EffectLifetimeSystem.cs:644-654 |

**相关文档**：[fx-04 PRD](../prd/fx-04-lifetime.md) · [fx-05 reference](fx-05-phases.md)
