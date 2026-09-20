# gr-op-14 runtime spec · 节点：Script 控制流

> 引擎实现任务书。第一性需求见 [gr-op-14 PRD](../prd/gr-op-14-control-flow.md)；现状见 [reference](../reference/gr-op-14-control-flow.md)。

## 1. 概述

控制流合同：程序计数器指令、Halt 必达、环境槽 I[0]、糖展开、深度与步数预算。

## 2. 设计

- 八件保持寄存器机指令形态；Jump 相对、Call 绝对由编译器编码，作者面不感知编址。
- HaltReturnInt 缺省 value 读 I[0] 的 ABI 合同保持：I[0] 定性为"传感器/环境寄存器"，图内非 Halt 用途禁写。
- 糖展开在编译期一次完成：BranchBool→JumpIfFalse 形态、Wait→Yield；展开结果与手写等价（诊断映射回糖节点）。
- 预算双卡：MaxCallStackDepth 管同程序 Call、MaxInvokeDepth 管 InvokeScript 树；超限失败信息带深度值。
- **治理项**：While/Until 糖的循环变量钉槽靠作者自觉（不钉会被暂存复用冲掉）——编译器对"糖循环内被写的未钉槽 Int"给一条 lint。

## 3. 精确语义与不变量

- 每图必达 Halt：缺终结即编译失败（MissingHalt）。
- 子图（FuncLib 图）内禁 Yield；InvokeScript 深度超限即失败。
- 步数预算整个 InvokeScript 树共享，嵌套不重置。

## 4. 迁移与治理

现状即基线；循环变量 lint 入 TODO。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-14 PRD](../prd/gr-op-14-control-flow.md) · [reference](../reference/gr-op-14-control-flow.md)
