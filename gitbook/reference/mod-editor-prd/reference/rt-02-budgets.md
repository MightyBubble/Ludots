# rt-02 reference · 预算与容量

> 现状参考。第一性需求见 [rt-02 PRD](../prd/rt-02-budgets.md)；配置说明见 [rt-02 配置说明](../config/rt-02-budgets.md)。

## 1. 现状快照

- GasBudget：12 计数器（ResponseWindows/Steps/Creates + 9 个 Dropped 类）+ 帧号；Reset 帧号自增并清零；HasWarnings 聚合九丢弃。GasBudgetResetSystem 每帧复位（SchemaUpdate 组）。
- RootBudgetTable：开放寻址+戳记（NextFrame O(1)）+斐波那契乘法散列；TryConsume(rootId,limit) 对 rootId==0 恒放行；事务 checkpoint/Commit/Rollback，错误码 4 个（检查点重入/换帧时检查点未闭合/回滚容量超限/无效检查点）。
- 单根上限=MAX_CREATES_PER_ROOT=256（引擎常量）；记账表容量=effectFanOutCommandCapacity（game.json）——两数字职责不同（治理项 R1）。
- GasBudgetReportSystem（EventDispatch 组）：九丢弃按 System/Metric 发布；订单准入 Backlog/HighWatermark/Overflow 与 8 种拒绝原因；溢出计数回退检测（回退即发布诊断错误）。
- 容量全表在 game.json gasRuntimeCapacity（事实页）；引擎常量全清单见事实页 GasConstants 节。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 预算计数器与复位 | src/Core/Gameplay/GAS/GasBudget.cs:5-44 |
| 每帧复位系统 | src/Core/Gameplay/GAS/Systems/GasBudgetResetSystem.cs；src/Core/Engine/GameEngine.cs:1676 |
| 单根表事务与错误码 | src/Core/Gameplay/GAS/RootBudgetTable.cs:55-118、167-182 |
| TryConsume 与散列 | src/Core/Gameplay/GAS/RootBudgetTable.cs:123-155 |
| 单根上限常量 | src/Core/Gameplay/GAS/GasConstants.cs:16 |
| 表容量接线 | src/Core/Gameplay/GAS/Systems/EffectProcessingLoopSystem.cs:73（=effectFanOutCommandCapacity） |
| 报告系统发布 | src/Core/Gameplay/GAS/Systems/GasBudgetReportSystem.cs:32-67；src/Core/Engine/GameEngine.cs:1856 |
| 容量配置 | assets/game.json（gasRuntimeCapacity，见事实页） |

**相关文档**：[rt-02 PRD](../prd/rt-02-budgets.md) · [rt-03 reference](rt-03-diagnostics.md)
