# ai-03 runtime spec · 决策

> 引擎实现任务书。第一性需求见 [ai-03 PRD](../prd/ai-04-decisions.md)；现状见 [reference](../reference/ai-04-decisions.md)。

## 1. 概述

决策编译与评估合同：考量平铺、四聚合语义、任务连续区间、节流参数。

## 2. 设计

- EvaluateDecision 保持：multiply 初始为 BaseScore，Multiply 入乘积（curved×Weight）、WSum/PriorityBucket 入加权和、Veto curved≤0 即返 0；总分 (multiply+weighted)×decision.Weight。
- ComputePriorityBucket 保持：仅聚合为 PriorityBucket 的考量四舍五入累加。
- ResolveTaskRange 连续性合同保持；Flags 解析保持五布尔与 Flags[] 混写。
- **治理项（引 todo/ai.md）**：I3——Tasks/Decisions/DecisionMakers 三层连续区间约束对跨 mod 分片是隐性限制，需文档化"同一决策者的任务必须整体由一方提供"，或改区间为显式 id 列表（结构改动，需立项）。
- SharedCooldownTag 未配置回退 ability 冷却 tag 的链路保持。

## 3. 精确语义与不变量

- 考量平铺后决策只持 offset+count；两决策考量区间不相交。
- Tasks 至少 1 条且解析后 offset..offset+count-1 连续。
- Veto 短路发生在任何乘加之前（return 0）。
- PriorityBucket 考量同时计入加权和与优先桶（双通道）。

## 4. 迁移与治理

现状即基线；I3 处置入 todo/ai.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-03 PRD](../prd/ai-04-decisions.md) · [reference](../reference/ai-04-decisions.md)
