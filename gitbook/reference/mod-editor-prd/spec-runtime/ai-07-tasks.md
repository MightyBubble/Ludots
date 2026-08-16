# ai-06 runtime spec · 任务

> 引擎实现任务书。第一性需求见 [ai-06 PRD](../prd/ai-07-tasks.md)；现状见 [reference](../reference/ai-07-tasks.md)。

## 1. 概述

任务执行合同：SubmitOrder 的订单构造与槽位回退链、组合 Kind 的现状语义。

## 2. 设计

- TrySubmitOrderTask 保持：OrderTypeId≤0 拒；槽位链 task.AbilitySlotIndex→decision.AbilitySlotIndex→TryFindAbilitySlot；I0=槽位否则 IntArg0≥0；I1=IntArg1；Spatial=目标位置；TryEnqueue 失败返 false。
- SubmitOrder 首个成功即 Complete 返回（短路）；失败时 submittedAny 决定 Running/Blocked。
- **治理项（引 todo/ai.md）**：I5——Sequence 现状 continue（no-op）、Parallel/ParallelComplete 仅置 requiredAny 不做事，三种组合 Kind 行为近乎等价、命名误导；要么实现真实编排语义，要么收窄 Kind 枚举并在文档声明 SubmitOrder 单发。
- 双引用互验（OrderTypeKey/OrderId、AbilityKey/AbilityId）保持。

## 3. 精确语义与不变量

- 决策胜出 ⇒ 至少尝试一次 SubmitOrder（若 Tasks 全为组合 Kind 则实际不提交任何订单，返回 None）。
- Blocked 不改变 UtilityAiState 的 CurrentDecision（未提交不记切换）。
- 提交成功 ⇒ CurrentDecisionId/Cooldown/SharedCooldown 全部就位。

## 4. 迁移与治理

现状即基线；I5 处置入 todo/ai.md（二选一：实现或收窄）。

## 变更记录

- v1（2026-08-15）：初版。

**相关文档**：[ai-06 PRD](../prd/ai-07-tasks.md) · [reference](../reference/ai-07-tasks.md)
