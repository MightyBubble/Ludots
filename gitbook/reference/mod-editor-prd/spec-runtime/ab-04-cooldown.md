# ab-04 runtime spec · 冷却三件套

> 引擎实现任务书。第一性需求见 [ab-04 PRD](../prd/ab-04-cooldown.md)；现状见 [reference](../reference/ab-04-cooldown.md)。

## 1. 概述

冷却合同：数据契约、TagClip 闭环（挂 tag→到期移除→门拒绝）、AI 就绪判定三段各自保持。

## 2. 设计

- 闭环保持：起播 FireTagClip 加 tag 并预约到期（含回滚）；到期体系移除 tag；下次激活被 blockTags 拒绝（带可观察原因）。
- 契约消费保持：AI 就绪读 valueAttribute（>0 未就绪）与冷却 tag（在场未就绪，决策级共享 tag 优先）；AI 提交后写共享冷却窗口步。
- **治理项 AB4**：cooldown 块是零使用配置面——二选一：接通（起播自动挂 tag/写属性，成为声明式入口）或收缩为 AI 查询面并在文档言明；决策前编辑器向导默认生成 TagClip+blockTags 闭环。

## 3. 精确语义与不变量

- 冷却期间再施放 = 激活拒绝（非订单错误）；同一冷却 tag 在场对人与 AI 判定一致。

## 4. 迁移与治理
现状即基线；AB4 按决策改造后回写。

## 变更记录
- v1（2026-08-15）：初版。

**相关文档**：[ab-04 PRD](../prd/ab-04-cooldown.md) · [reference](../reference/ab-04-cooldown.md)
