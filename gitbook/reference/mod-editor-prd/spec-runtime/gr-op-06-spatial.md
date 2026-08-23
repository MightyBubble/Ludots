# gr-op-06 runtime spec · 节点：空间查询

> 引擎实现任务书。第一性需求见 [gr-op-06 PRD](../prd/gr-op-06-spatial.md)；现状见 [reference](../reference/gr-op-06-spatial.md)。

## 1. 概述

空间形状查询与管线合同：中心解析两派、容量策略、kind 掩码差异。

## 2. 设计

- 中心解析保持两派：锥/线/矩形 preferSourceCenter；其余先目标点再施法者兜底。规则集中在解析器一处，不再散落各 handler。
- 容量策略二值：RequireComplete 命中超容即失败；AllowTruncated 截断并记 dropped。两者都不做隐式降级。
- 排序稳定语义保持：相等保持原序；QueryLimit 截断在排序后。
- **治理项 G9**：op 枚举里 opcode 101/110 的删除注释已失真——110 的注释称 QueryFilterTeam 已删、建议用 QueryFilterRelationship，实际 QueryFilterTeam 以新 opcode 重生且与后者并存（团队过滤与关系过滤并存是产品语义，不是重复）。删除死注释，改由描述符表承担"现存 op"SSOT。

## 3. 精确语义与不变量

- 同一图同一执行内，管线节点只原位收窄列表，不重新发起空间检索。
- 中心解析结果在形状查询节点求值时一次确定，管线阶段不变。
- TargetList 容量上限与事实页一致；RequireComplete 失败信息带命中数与容量。

## 4. 迁移与治理

G9 入 todo/graph.md；注释清理随下次 GraphOps.cs 变更带出。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[gr-op-06 PRD](../prd/gr-op-06-spatial.md) · [reference](../reference/gr-op-06-spatial.md)
