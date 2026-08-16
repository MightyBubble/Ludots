# ai-05 runtime spec · 目标过滤器

> 引擎实现任务书。第一性需求见 [ai-05 PRD](../prd/ai-06-target-filters.md)；现状见 [reference](../reference/ai-06-target-filters.md)。

## 1. 概述

九种过滤 op 的编译与判定合同：顺序 AND、拒绝码、MaxResults 截断。

## 2. 设计

- CompileTargetFilterOp 保持九 Kind 分派与正数校验；ops 平铺 + offset/count 布局保持。
- 判定链保持：逐 op 短路淘汰并写专属 UtilityAiFilterRejectReason；DistanceMax 用平方距离；RecentAttacker 校验 LastAttacker 存活 + TTL。
- **治理项（引 todo/ai.md）**：I4——HasAllTags 的 IntB 编译端固定传 0，运行时 priorityBucket+=op.IntB 恒加 0，死字段未接线；接通权重通道（配字段如 BucketBonus）或删掉累加行。
- RelationshipFilter 解析与 Team 组件缺失行为保持（缺 Team 即淘汰）。

## 3. 精确语义与不变量

- op 序列 AND：首个失败 op 的拒绝码生效。
- MaxResults 只在链尾截断，不影响拒绝码。
- 目标无位置组件时 DistanceMax 判 MissingPosition 淘汰。

## 4. 迁移与治理

现状即基线；I4 处置入 todo/ai.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-05 PRD](../prd/ai-06-target-filters.md) · [reference](../reference/ai-06-target-filters.md)
