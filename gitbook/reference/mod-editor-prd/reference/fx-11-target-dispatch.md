# fx-11 reference · 目标派发

> 现状参考。第一性需求见 [fx-11 PRD](../prd/fx-11-target-dispatch.md)；配置说明见 [fx-11 配置说明](../config/fx-11-target-dispatch.md)。

## 1. 现状快照

- 映射：preset 与 contextMapping 互斥；双缺省用默认映射（Source=OriginalSource、Target=ResolvedEntity、TargetContext=OriginalTarget）；payloadEffect 引用注册表；槽值域四值。
- 预设表现存 4 条：SourceToResolved / TargetToResolved / ResolvedToSource / SourceToOriginalTargetContext（assets/GAS/target_dispatch_presets.json）。
- FanOut 链（内建）：HandleSpatialQuery（纯查询写候选数）→HandleDispatchPayload（过滤+根预算+FanOutCommandBuffer）→HandleReResolveAndDispatch（二合一）。
- 图路径：FanOutDispatchEffect→运行时 API，事务内暂存随 Commit 发布、无事务直发；命令落地按三槽重映射发布。
- 查询中心解析：锥/线/矩形偏 source；其余先 target 点再 source 兜底。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 互斥与默认映射 | src/Core/Gameplay/GAS/Config/EffectTemplateLoader.cs:1870-1874 |
| 默认映射常量 | src/Core/Gameplay/GAS/EffectTemplateRegistry.cs:102-108 |
| payloadEffect 注册校验 | EffectTemplateLoader.cs:1863-1868 |
| 预设槽值域 | src/Core/Gameplay/GAS/Config/TargetDispatchPresetLoader.cs:63-78 |
| 内建三处理器 | src/Core/Gameplay/GAS/BuiltinHandlers.cs:150-271 |
| 图路径扇出 API | src/Core/NodeLibraries/GASGraph/Host/GasGraphRuntimeApi.cs:954-997 |
| 命令落地重映射发布 | src/Core/Gameplay/GAS/TargetResolverFanOutHelper.cs:352-367 |
| 查询中心解析 | TargetResolverFanOutHelper.cs:132-155 |
| 事务内随提交发布 | src/Core/Gameplay/GAS/EffectPhaseSideEffectTransaction.cs:960-963 |

**相关文档**：[fx-11 PRD](../prd/fx-11-target-dispatch.md) · [fx-12 reference](fx-12-stack.md)
