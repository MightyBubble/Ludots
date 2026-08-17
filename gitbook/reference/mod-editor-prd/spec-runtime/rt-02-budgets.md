# rt-02 runtime spec · 预算与容量

> 引擎实现任务书。第一性需求见 [rt-02 PRD](../prd/rt-02-budgets.md)；现状见 [reference](../reference/rt-02-budgets.md)。

## 1. 概述

预算三层合同：帧计数器（GasBudget）、单根扇出表（RootBudgetTable）、容量装配（gasRuntimeCapacity）。

## 2. 设计

- GasBudget 保持 12 计数器+帧号、Reset 帧号自增清零、HasWarnings 聚合九丢弃；每帧复位系统挂 SchemaUpdate 组。
- 单根表保持开放寻址+斐波那契散列+戳记换帧 O(1)；rootId==0 恒放行；事务三段（checkpoint/Commit/Rollback）与四个错误码保持。
- **治理项 R1**：单根上限（MAX_CREATES_PER_ROOT，引擎常量）与记账表容量（=effectFanOutCommandCapacity，game.json）职责不同而数字巧合相近层级，易被当同一上限混配——错误信息与文档双处显式区分；TryConsume 拒绝原因注明"per-root creates cap"而非"table capacity"。
- 报告系统保持九丢弃按 System/Metric 发布 + 订单水位（backlog/高水位/溢出/八拒绝原因）+ 溢出计数回退检测。

## 3. 精确语义与不变量

- 计数器只描述当前帧；帧号单调增。
- TryConsume O(1)，事务回滚后表内容与检查点时完全一致。
- 预算拒绝路径不抛错、不中断帧；事务误用路径必抛错带码。
- 溢出计数单调不减，回退即诊断错误。

## 4. 迁移与治理

现状即基线；R1 处置入 TODO（见 todo/runtime.md）。

## 变更记录

- v1（2026-08-17）：初版。

**相关文档**：[rt-02 PRD](../prd/rt-02-budgets.md) · [reference](../reference/rt-02-budgets.md)
