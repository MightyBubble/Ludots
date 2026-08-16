# fx-07 reference · 相位监听器

> 现状参考。第一性需求见 [fx-07 PRD](../prd/fx-08-phase-listeners.md)；配置说明见 [fx-07 配置说明](../config/fx-08-phase-listeners.md)。

## 1. 现状快照

- EffectPhaseListenerBuffer：容量 8；字段 ListenTagIds/ListenEffectIds（0 通配）/Phases/Scopes(Target=0, Source=1)/ActionFlags(ExecuteGraph=1, PublishEvent=2, Both=3)/GraphProgramIds/EventTagIds/Priorities/OwnerEffectIds；匹配由 PhaseListenerMatcher 裁决。
- 契约：flags 恰为三种组合；ExecuteGraph⇔graphProgramId>0、PublishEvent⇔eventTagId>0；纯相位禁 PublishEvent；loader 与执行计划双重校验。
- 宿主：模板编译写 0；运行期以宿主效果实体 id 注册、应用事务内延迟回放；移除阶段按宿主清理（StageListenerRemoval→RemoveByOwner）并压缩缓冲。
- Instant 模板禁携（loader 拒；运行期再遇抛持久监听需跨帧寿命错）。
- 收集处注释称"预算截断"、实现为 dropped>0 即抛——注释与行为不一致（todo/effect.md E6）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 缓冲字段与容量 | src/Core/Gameplay/GAS/Components/EffectPhaseListenerBuffer.cs:214-267 |
| 匹配器 | EffectPhaseListenerBuffer.cs:17-25 |
| 契约（组合/对应/纯相位） | EffectPhaseListenerBuffer.cs:54-195, 118-147 |
| 缓冲压缩 | EffectPhaseListenerBuffer.cs:301-323 |
| loader 校验 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1523-1535 |
| Instant 禁携 | EffectTemplateLoader.cs:309-313 |
| 计划编译二次校验 | src/Core/Gameplay/GAS/EffectExecutionPlan.cs:260-361 |
| 运行期注册与延迟回放 | src/Core/Gameplay/GAS/Systems/EffectApplicationSystem.cs:759-765, 496-515 |
| 按宿主清理 | src/Core/Gameplay/GAS/EffectPhaseSideEffectTransaction.cs:1604 |
| 运行期跨帧寿命抛错 | src/Core/Gameplay/GAS/Systems/EffectProposalProcessingSystem.cs:1616-1620 |
| 收集抛错（注释不一致处） | EffectProposalProcessingSystem.cs:511-515 |

**相关文档**：[fx-07 PRD](../prd/fx-08-phase-listeners.md) · [fx-08 reference](fx-09-target-query.md)
