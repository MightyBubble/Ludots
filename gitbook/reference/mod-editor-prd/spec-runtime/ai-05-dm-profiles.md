# ai-07 runtime spec · 决策者与档案

> 引擎实现任务书。第一性需求见 [ai-06 PRD](../prd/ai-05-dm-profiles.md)；现状见 [reference](../reference/ai-05-dm-profiles.md)。

## 1. 概述

决策者区间与档案参数的编译合同、择优与换挡语义、思考节奏。

## 2. 设计

- 两层区间解析（ResolveDecisionRange/ResolveDecisionMakerRange）合同保持；至少一条与连续性强制的报错路径保持。
- 择优比较保持：UtilityScore 下 score>best+margin 才换；|Δ|≤margin 时依次比 PriorityBucket、DistanceSq；FixedPriority 走 Priority 排序。
- 节奏保持：NextThinkStep=step+DecisionIntervalSteps；OrderBuffer HasActive/Queued/Pending 任一即跳过本轮。
- DefaultStanceId 数字显式拒绝保持；DefaultStance 语义键解析保持。
- **治理项（引 todo/ai.md）**：I6——DefaultStance 编译后无系统消费（UtilityAiStanceState 无读写），接通消费系统或显式冻结该字段；I3——区间约束的分片限制文档化。

## 3. 精确语义与不变量

- margin 语义仅 UtilityScore；FixedPriority 忽略 SwitchMargin。
- 挑战者比较顺序固定：score → (margin 内) bucket → distanceSq。
- interval 与 maxCandidates 编译期必须为正。
- MaxCandidates 与全部 TargetFilters.MaxResults 的最大值决定评估 scratch 容量。

## 4. 迁移与治理

现状即基线；I6 处置入 todo/ai.md。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-06 PRD](../prd/ai-05-dm-profiles.md) · [reference](../reference/ai-05-dm-profiles.md)
