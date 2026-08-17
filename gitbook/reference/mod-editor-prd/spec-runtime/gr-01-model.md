# gr-01 runtime spec · 图编程模型

> 引擎实现任务书。第一性需求见 [gr-01 PRD](../prd/gr-01-model.md)；现状见 [reference](../reference/gr-01-model.md)。

## 1. 概述

指令、注册表、执行游标三件套的机器合同与治理项。

## 2. 设计

- **指令**：GraphInstruction 九字段封闭（Op/Dst/A/B/C/Flags/Imm/ImmF）；Flags 目前仅 FuncLibName 一位，新增位必须显式定义。
- **注册终态**：Register 全量校验（graphId 有效、kind 显式、拒重复，失败回滚移除）；ReplaceProgram 仅新 id 重启、kind 不变、失败回滚；图名注册表 InvalidId=0、装载尾 Freeze。
- **游标**：五态 NotStarted/Yielded/Halted/BudgetSuspended/Running；IsSuspended=Yielded 或 BudgetSuspended；挂起语义归 gr-05。
- **分层**：宿主调度（BT/HFSM/Level/Script，gr-05）不是图 kind——L2 概念不得进 GraphKind 枚举。

## 3. 精确语义与不变量

- 同一文档编译结果确定；字符串符号装载期解析后运行期零字符串查找；预算树共享，超限即失败；冻结后注册一律失败。

## 4. 迁移与治理

现状即基线；治理项 G1（错误文案中英混杂）、G2（程序缓冲溢出静默丢弃）见 todo/graph.md，立项后回写 reference。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-01 PRD](../prd/gr-01-model.md) · [reference](../reference/gr-01-model.md) · [gr-05 spec](gr-05-execution.md)
