# gr-00 reference · 图编程模型

> 现状参考。第一性需求见 [gr-00 PRD](../prd/gr-01-model.md)；配置说明见 [gr-00 配置说明](../config/gr-01-model.md)。

## 1. 现状快照

- 指令：GraphInstruction 九字段（Op/Dst/A/B/C/Flags/Imm/ImmF）；Flags 唯一位 FuncLibName=1。
- Kind：六值 None=0、Effect=1、Query=2、Score=3、Validation=4、Derived=5、Script=6；解析大小写敏感、拒 None；宿主调度概念未入枚举。
- 注册：Registration{Program,Kind,Symbols,ContainsYield}；Register 拒重复、验证失败回滚移除；ReplaceProgram 新 id 重启、kind 不许变、失败回滚；RequireRegistration 的 kind 不匹配文案为中文；装载尾 ValidateInvokeTargets 终检。
- 游标：NotStarted/Yielded/Halted/BudgetSuspended/Running；IsSuspended=Yielded 或 BudgetSuspended。
- 程序缓冲：CAPACITY=128，Add 溢出静默丢弃（G2）；产物为 GraphProgramPackage record（GraphName/Symbols/Program/Kind）。
- VM 限额：Float/Int/Bool/Entity 寄存器各 32、Targets 256、CallStack 16、InvokeDepth 16、每执行 4096 指令（跨图树共享）、HandlerTable 2048。
- 图名注册表：InvalidId=0、MaxGraphs=4095、装载尾 Freeze。
- 帧绑定：E[0]=caster、E[1]=explicitTarget、E[2]=按上下文（TargetContext/Viewer/PreviewTarget）。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| 指令九字段 | src/Graph/Ludots.Graph.Abstractions/GraphInstruction.cs:5-15 |
| Flags 唯一位 | src/Core/GraphRuntime/GraphInstructionFlags.cs:7 |
| Kind 六值与解析 | src/Graph/Ludots.Graph.Abstractions/GraphKind.cs:12-45 |
| 注册终态与回滚 | src/Core/GraphRuntime/GraphProgramRegistry.cs:72-94 |
| ReplaceProgram 边界 | GraphProgramRegistry.cs:115-158 |
| kind 中文文案 | GraphProgramRegistry.cs:197-198 |
| Invoke 终检 | GraphProgramRegistry.cs:225-270 |
| 游标五态 | src/Core/GraphRuntime/GraphExecutionCursor.cs:3-33 |
| 缓冲溢出静默丢弃 | src/Core/GraphRuntime/GraphProgramBuffer.cs:21 |
| VM 限额 | src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs:5-29 |
| 图名上限与冻结 | src/Core/NodeLibraries/GASGraph/Host/GraphIdRegistry.cs:5-8；GraphProgramConfigLoader.cs:145 |
| 帧绑定 E0-E2 | src/Core/NodeLibraries/GASGraph/GraphFrame.cs:8-14,87-104 |

**相关文档**：[gr-00 PRD](../prd/gr-01-model.md) · [gr-01 reference](gr-02-document.md) · [gr-02 reference](gr-03-kinds.md)
