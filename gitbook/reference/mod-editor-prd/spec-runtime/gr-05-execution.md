# gr-04 runtime spec · 执行模型

> 引擎实现任务书。第一性需求见 [gr-04 PRD](../prd/gr-05-execution.md)；现状见 [reference](../reference/gr-05-execution.md)。

## 1. 概述

执行合同：两种入口、宿主挂起政策、树共享预算、零分配。

## 2. 设计

- run-to-halt 保持逢挂起硬拒（错误信息指向切片入口与 Script kind）；预算耗尽抛出；掉队指令指针按越界处理；调用栈 span 达阈值禁堆分配。
- 切片执行保持可恢复：预算尽持久化挂起态不抛；入口仅 Script；ConstInt/MoveInt/HaltReturnInt 保持内联快速路径。
- 宿主政策保持四宿主枚举，仅 BT 与 Script 允许挂起；ActionLib 装载期对 Hfsm/Level 做可达挂起校验（gr-06）。
- 跨图调用保持三约束：目标必须 Script、被调子图含挂起直接拒、深度计入上限；步数预算树共享且跨帧累计。

## 3. 精确语义与不变量

- 寄存器、目标表、调用栈全部栈分配；挂起状态可无损恢复且续跑共享同一预算；run-to-halt 只有正常收尾与失败两类停机点。

## 4. 迁移与治理

现状即基线；无新增治理项。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-04 PRD](../prd/gr-05-execution.md) · [reference](../reference/gr-05-execution.md) · [gr-06 spec](gr-07-actionlib.md)
