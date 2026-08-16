# gr-04 reference · 执行模型

> 现状参考。第一性需求见 [gr-04 PRD](../prd/gr-05-execution.md)；配置说明见 [gr-04 配置说明](../config/gr-05-execution.md)。

## 1. 现状快照

- run-to-halt：预算耗尽抛出；Yield 硬拒（提示走 ExecuteSlice 与 Script kind）；掉队指令指针按 PcOutOfRange 处理；调用栈 span ≥16 禁堆分配。
- 切片：BudgetSuspended 不抛、可恢复；ExecuteSlice 仅 Script；ConstInt/MoveInt/HaltReturnInt 走内联快速路径。
- 宿主政策：GraphActionHost 四值 BehaviorTree/Hfsm/Level/Script，仅 BT 与 Script 允许挂起；ActionLib 装载期对 Hfsm/Level 做可达挂起校验。
- 预算：treeSteps = max(状态累计, 游标步数)，超单执行上限即持久化 BudgetSuspended；跨图调用深度超限抛出；调用目标必须 Script 且子图 ContainsYield 直接拒。
- 零分配：寄存器、目标表、调用栈全 stackalloc。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| run-to-halt 与 Yield 硬拒 | src/Core/NodeLibraries/GASGraph/GasGraphOpHandlerTable.cs:292-342 |
| 切片可恢复 | GasGraphOpHandlerTable.cs:344-363 |
| 快速路径内联 | src/Core/NodeLibraries/GASGraph/GraphExecutor.cs:399-531 |
| 切片仅 Script | GraphExecutor.cs:169-173 |
| 树共享预算与持久化 | GraphExecutor.cs:396-418 |
| 深度超限与子图拒挂起 | GraphExecutor.cs:865-891 |
| 零分配 stackalloc | GraphExecutor.cs:352-357,426-431 |
| 宿主枚举与政策 | src/Core/GraphRuntime/GraphActionHost.cs:5-17 |
| 装载期挂起可达校验 | src/Core/NodeLibraries/GASGraph/Host/GraphActionCatalogLoader.cs:104-114 |

**相关文档**：[gr-04 PRD](../prd/gr-05-execution.md) · [gr-00 reference](gr-01-model.md) · [gr-06 reference](gr-07-actionlib.md)
