# gr-op-14 reference · 节点：Script 控制流

> 现状参考。第一性需求见 [gr-op-14 PRD](../prd/gr-op-14-control-flow.md)；配置说明见 [gr-op-14 配置说明](../config/gr-op-14-control-flow.md)。

## 1. 现状快照

- 八件：Jump（:84，SC，相对）、JumpIfFalse（:85，E+SC，condition，true/false 双口）、Call（:191，SC，imm 绝对）、Return（:192，SC）、Yield（:193，SC，scriptOnly）、HaltReturnInt（:194，L+Q+SC，value 可缺省读 I[0]）、InvokeScript（:195，L+Q+SC，flags=FuncLibName，imm=函数名→Int）、MoveInt（:196，SC）。
- 糖五个：BranchBool/SwitchInt/Wait/While/Until（Wait=Yield 别名；后四者 Script-only）。
- 缺 Halt 编译失败（MissingHalt，GraphKindOperationPolicy）；InvokeScript 深度上限 MaxInvokeDepth=16，子图禁 Yield；步数预算 MaxInstructionsPerExecution=4096 全树共享；Call 栈深 MaxCallStackDepth=16。
- 环境槽 I[0]：HaltReturnInt 缺省 value 与 Script Host ABI 同槽。

## 2. 代码锚点

| 机制 | 位置 |
|---|---|
| Jump/JumpIfFalse 描述符 | src/Core/NodeLibraries/GASGraph/GraphOpDescriptorTable.Data.cs:84-85 |
| Call/Return/Yield/HaltReturnInt/InvokeScript/MoveInt | GraphOpDescriptorTable.Data.cs:191-196 |
| 糖常量表 | src/Core/NodeLibraries/GASGraph/GraphAuthoringSugar.cs:12-16 |
| Halt 缺省 I[0] | src/Core/NodeLibraries/GASGraph/GraphControlFlowCompiler.cs:814-819 |
| MissingHalt 检查 | src/Core/NodeLibraries/GASGraph/GraphKindOperationPolicy.cs:185 |
| 深度与步数限额 | src/Core/NodeLibraries/GASGraph/GraphVmLimits.cs:10,16,23 |

**相关文档**：[gr-op-14 PRD](../prd/gr-op-14-control-flow.md) · [gr-op-01 reference](gr-op-01-context.md)
